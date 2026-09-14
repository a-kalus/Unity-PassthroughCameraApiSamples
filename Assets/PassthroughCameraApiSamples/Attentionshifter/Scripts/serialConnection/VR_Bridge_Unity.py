# =====================================================================
# VR_Bridge_Unity.py
#
# Wie VR_Bridge_Keyboard_Test.py, aber die Commands kommen aus der
# Unity-App auf der Quest statt von der Tastatur.
#
# Transport: TCP ueber das USB-Kabel (adb reverse tcp:7777 tcp:7777).
# Unity schickt pro Command eine Zeile, z.B. "1\n".
# Die Tastatur bleibt zusaetzlich aktiv (Not-Aus am PC).
#
# WICHTIG: Aenderungen an der COMMAND_MAP muessen in allen
# Bridge-Dateien gemacht werden.
#0
# Commands von Unity (identisch zu den Tasten):
#   "0"      - Init
#   "1".."6" - Bewegungen laut COMMAND_MAP
#   "pause"  - alle Motoren auf Torque 0
#   "x"      - Bridge beenden
#
# Reihenfolge beim Start:
#   1. Nucleo-Master per USB an den PC (COM-Port unten eintragen)
#   2. Dieses Script starten
#   3. adb reverse tcp:7777 tcp:7777
#   4. Unity-App auf der Quest starten
# =====================================================================

import os
import queue
import socket
import threading
import serial
import time
import msvcrt

# ================= KONFIGURATION =================

#MASTER_PORT = "COM5"    # Port zum Master (Nucleo144)
MASTER_PORT = "COM4"    # Port zum Master (Nucleo144)

BAUD = 115200

MOTORS = [1,2,3,4]

# Motor 1 - Links Oben
# Motor 2 - Lift / kranial
# Motor 3 - Rechts Oben
# Motor 4 - Descent / kaudal

PING_TIMEOUT = 10.0
INIT_TIMEOUT = 20.0

# --- VR-Verbindung (Unity) ----------------------------------------------
VR_HOST = "127.0.0.1"   # bleibt localhost - adb reverse tunnelt durchs Kabel
VR_PORT = 7777          # muss zu 'port' in UsbStringSender.cs passen

# --- Watchdog-Handling ---------------------------------------------------
# Der Slave schlaeft nach COMMAND_TIMEOUT_MS ohne empfangene Zeile ein
# (Nucleo64_stacked_Slave_Torque_Control.ino, aktuell 30 s). Das ist ein
# Totmannschalter: faellt der Master aus oder geht die UART-Leitung ab,
# darf der Motor sein letztes Drehmoment nicht unbegrenzt halten.
#
# Damit er nicht einschlaeft, waehrend nichts gesendet wird, schickt
# dieses Script pro Motor einen Heartbeat. Der Slave resettet seinen
# Timer bei JEDER empfangenen Zeile, PING reicht also.
HEARTBEAT_INTERVAL = 0.5     # s, pro Motor; nur wenn sonst nichts gesendet wurde
REINIT_RETRY_INTERVAL = 2.0  # s zwischen automatischen Re-Init-Versuchen

DEBUG_RX = True   # Antworten des Masters in der Konsole anzeigen

# ================= COMMAND-TABELLE =================
# Command  ->  (Name, Liste der Master-Befehle)
# Muss identisch zur COMMAND_MAP in VR_Bridge.py sein.

COMMAND_MAP = {
    "1": ("Nach oben",   ["2:H.T0.5.10s"]),
    "2": ("Nach unten",  ["4:H.T0.5.10s"]),
    # Links/Rechts: Motor der Zielseite zieht (positiv), Gegenseite
    # drueckt schwaecher gegen (negativ).
    "3": ("Nach links",  ["1:H.T0.5.10s",  "3:H.T-0.3.10s"]),
    "4": ("Nach rechts", ["3:H.T0.5.10s",  "1:H.T-0.3.10s"]),
    "5": ("Nach vorne",  ["1:H.T-0.5.10s", "3:H.T-0.5.10s"]),
    "6": ("Nach hinten", ["1:H.T0.5.10s", "3:H.T0.5.10s"]),
}

INIT_COMMANDS = {"0", "init"}
EXIT_COMMANDS = {"x", "q", "ende"}
PAUSE_COMMANDS = {"pause", "stop", "halt"}   # Leertaste bzw. Text aus Unity

# Interne Ereignisse aus dem VR-Thread (keine Commands)
EVENT_VR_CONNECTED = object()
EVENT_VR_DISCONNECTED = object()


# ================= LINK-STATE =================
# active:         Motoren, die Ping + Init bestanden haben
# awake:          letzter bekannter Zustand des Slaves
# last_tx:        wann zuletzt irgendeine Zeile an diesen Motor ging
# last_init_try:  wann zuletzt automatisch ein I geschickt wurde

state = {
    "active": [],
    "awake": {},
    "last_tx": {},
    "last_init_try": {},
}


def note(msg):
    print(f"{time.strftime('%H:%M:%S')}  {msg}")


def mark_tx(motor_id, hold_s=0.0):
    """TX-Zeitpunkt merken. hold_s > 0 sperrt den Heartbeat fuer diese Zeit.

    Grund: Der Master bricht bei JEDEM Nicht-Job-Befehl (auch PING) den
    laufenden H./L.-Job ab (jobs[id].active = false), OHNE das Drehmoment
    zu nullen. Ein Heartbeat waehrend eines Jobs killt also die Dauer und
    der Motor zieht endlos weiter. Waehrend eines Jobs schickt der Master
    ohnehin mit 50 Hz Torque-Werte an den Slave - der Slave-Watchdog wird
    dadurch bedient, der Heartbeat ist in dieser Zeit ueberfluessig.
    """
    state["last_tx"][motor_id] = time.time() + hold_s


def job_duration_s(line):
    """Dauer eines H./L.-Jobs in Sekunden, sonst 0.0.
    Parst exakt wie der Master (letzter Punkt trennt die Dauer ab)."""
    body = line.split(":", 1)[-1].strip()

    if not body.upper().startswith(("H.", "L.")):
        return 0.0

    last_dot = body.rfind(".")

    if last_dot <= 2:
        return 0.0

    token = body[last_dot + 1:].strip().lower()

    try:
        if token.endswith("ms"):
            return float(token[:-2]) / 1000.0
        if token.endswith("s"):
            return float(token[:-1])
        return float(token) / 1000.0   # nackte Zahl = ms (wie im Master)
    except ValueError:
        return 0.0


# ================= SERIAL-GRUNDFUNKTIONEN =================

def open_serial(port):
    ser = serial.Serial(port, BAUD, timeout=0.02)
    print(f"CONNECTED {port}")
    return ser


def send_line(ser, line, log=True):
    if log:
        print(f"TX: {line}")
    ser.write((line + "\n").encode("utf-8"))
    ser.flush()

    # TX an einen Motor merken (fuer Heartbeat).
    # Bei H./L.-Jobs zusaetzlich fuer die Job-Dauer sperren.
    prefix = line.split(":", 1)[0]
    if prefix.isdigit():
        mark_tx(int(prefix), job_duration_s(line))


def read_line(ser, log=False):
    raw = ser.readline()

    if not raw:
        return ""

    line = raw.decode("utf-8", errors="ignore").strip()

    if line and log:
        print(f"RX: {line}")

    return line


def wait_for_text(ser, expected, timeout):
    start = time.time()

    while time.time() - start < timeout:
        line = read_line(ser, log=True)

        if expected in line:
            return True

    return False


# ================= VR-VERBINDUNG (UNITY) =================
# Der Thread legt NUR Strings in die Queue. Der serielle Port wird
# ausschliesslich im Hauptthread benutzt - so gibt es keine
# ineinander verschachtelten Schreibzugriffe auf den Master.

def open_vr_server():
    """Server-Socket im Hauptthread binden, damit ein belegter Port
    sofort und sichtbar knallt statt still im Thread zu sterben."""
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

    if os.name != "nt":
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    try:
        server.bind((VR_HOST, VR_PORT))
    except OSError as e:
        raise SystemExit(
            f"Port {VR_PORT} ist belegt (zweite Instanz dieses Scripts "
            f"oder ein 'adb forward' auf dem Port?). Original: {e}"
        )

    server.listen(1)
    print(f"VR-SERVER {VR_HOST}:{VR_PORT} - warte auf die Unity-App...")
    print(f"(vorher auf dem PC ausfuehren: adb reverse tcp:{VR_PORT} tcp:{VR_PORT})")
    return server


def vr_receive_loop(server, cmd_queue):
    """Nimmt eine Verbindung nach der anderen an und schiebt jede
    empfangene Zeile als Command in die Queue."""
    while True:
        try:
            client, address = server.accept()
        except OSError:
            return

        note(f"VR verbunden: {address}")
        cmd_queue.put(EVENT_VR_CONNECTED)
        buffer = b""

        try:
            while True:
                data = client.recv(1024)

                if not data:
                    break

                buffer += data

                # Unity terminiert jeden String mit '\n'
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    text = line.decode("utf-8", errors="replace").strip()

                    if text:
                        cmd_queue.put(text)
        except OSError:
            pass
        finally:
            client.close()
            cmd_queue.put(EVENT_VR_DISCONNECTED)


# ================= MOTOR-FUNKTIONEN =================
# (uebernommen aus Motor_Control_Keyboard.py)

def ping_motor(ser, motor_id, timeout=PING_TIMEOUT, retry_interval=0.5):
    cmd = f"{motor_id}:PING"
    expected = f"{motor_id}:PONG"

    print(f"PING MOTOR {motor_id}")

    start = time.time()
    last_send = 0.0

    while time.time() - start < timeout:
        now = time.time()

        if now - last_send >= retry_interval:
            send_line(ser, cmd)
            last_send = now

        line = read_line(ser, log=True)

        if expected in line:
            print(f"Motor {motor_id} ping OK")
            return

    raise RuntimeError(f"Ping timeout Motor {motor_id}. Erwartet: {expected}")


def init_motor(ser, motor_id):
    cmd = f"{motor_id}:I"
    expected = f"{motor_id}:OK I"

    print(f"INIT MOTOR {motor_id}")
    send_line(ser, cmd)

    ok = wait_for_text(ser, expected, INIT_TIMEOUT)

    if not ok:
        raise RuntimeError(f"Init timeout Motor {motor_id}. Erwartet: {expected}")

    print(f"Motor {motor_id} init OK")


def zero_all(ser):
    motors = state["active"] or MOTORS

    for motor_id in motors:
        send_line(ser, f"{motor_id}:T0", log=False)
        time.sleep(0.03)


# ================= WATCHDOG-HANDLING =================

def send_heartbeats(ser):
    """Haelt den Slave-Watchdog wach. Nur fuer Motoren, an die ohnehin
    nichts gesendet wurde - andere Kommandos resetten den Timer selbst."""
    now = time.time()

    for motor_id in state["active"]:
        if now - state["last_tx"].get(motor_id, 0.0) < HEARTBEAT_INTERVAL:
            continue

        send_line(ser, f"{motor_id}:PING", log=False)


def recover_sleeping(ser):
    """Eingeschlafene Motoren automatisch neu initialisieren.
    Nicht blockierend - die Bestaetigung kommt ueber handle_replies."""
    now = time.time()

    for motor_id in state["active"]:
        if state["awake"].get(motor_id):
            continue

        if now - state["last_init_try"].get(motor_id, 0.0) < REINIT_RETRY_INTERVAL:
            continue

        state["last_init_try"][motor_id] = now
        send_line(ser, f"{motor_id}:I", log=False)


def handle_replies(ser):
    """Antworten vom Master auswerten und Wach-Zustand nachfuehren."""
    while ser.in_waiting:
        line = read_line(ser, log=DEBUG_RX)

        if not line:
            break

        prefix, sep, body = line.partition(":")

        if not sep or not prefix.strip().isdigit():
            continue

        motor_id = int(prefix.strip())

        if motor_id not in MOTORS:
            continue

        body = body.strip().upper()

        if body == "TIMEOUT":
            # Watchdog hat ausgeloest: Slave hat Torque auf 0 gesetzt
            # und den Driver deaktiviert.
            if state["awake"].get(motor_id):
                note(f"Motor {motor_id}: WATCHDOG TIMEOUT")
            state["awake"][motor_id] = False

        elif body.startswith("ERR SLEEP"):
            if state["awake"].get(motor_id):
                note(f"Motor {motor_id}: schlaeft (ERR SLEEP)")
            state["awake"][motor_id] = False

        elif body == "OK X":
            state["awake"][motor_id] = False

        elif body == "OK I":
            if not state["awake"].get(motor_id):
                note(f"Motor {motor_id}: wieder aktiv, Torque auf 0")
            state["awake"][motor_id] = True
            # Bewusst NICHT das alte Drehmoment wiederherstellen.
            # Nach einem Watchdog-Trip stillschweigend wieder Kraft
            # aufzubauen waere gefaehrlich - der Wert wird neu gestellt.
            send_line(ser, f"{motor_id}:T0", log=False)

        elif body.startswith("ERR I"):
            note(f"Motor {motor_id}: INIT FEHLGESCHLAGEN")
            state["awake"][motor_id] = False


# ================= COMMAND-LOGIK =================

def run_init(master):
    """Ping + Init aller Motoren. Motoren, die nicht antworten,
    werden mit Warnung uebersprungen."""
    active = []

    for motor_id in MOTORS:
        state["awake"][motor_id] = False
        state["last_tx"][motor_id] = 0.0
        state["last_init_try"][motor_id] = 0.0

        try:
            ping_motor(master, motor_id)
            init_motor(master, motor_id)
            active.append(motor_id)
            state["awake"][motor_id] = True
        except RuntimeError as e:
            print(f"WARN: Motor {motor_id}: {e}")

    state["active"] = active

    if not active:
        raise RuntimeError("Kein Motor erfolgreich initialisiert - Abbruch.")

    zero_all(master)
    print(f"INIT COMPLETE ({len(active)}/{len(MOTORS)} Motoren aktiv: {active})")


def target_motors(cmds):
    """Motor-IDs, die eine Command-Liste anspricht."""
    ids = []

    for cmd in cmds:
        prefix = cmd.split(":", 1)[0]
        if prefix.isdigit():
            ids.append(int(prefix))

    return ids


def run_movement(master, key, initialized):
    """Einen Bewegungs-Command aus der COMMAND_MAP ausfuehren."""
    name, cmds = COMMAND_MAP[key]

    if not initialized:
        print(f"WARN: '{key}' ({name}) ignoriert - erst Init ('0') ausfuehren.")
        return

    # Schlafende oder nicht aktive Zielmotoren: Command komplett ablehnen,
    # damit z.B. bei Vorne/Hinten nicht nur ein Motor von zweien zieht.
    # Re-Init schlafender Motoren laeuft automatisch (recover_sleeping).
    blocked = [m for m in target_motors(cmds) if not state["awake"].get(m)]

    if blocked:
        note(f"'{key}' ({name}) ignoriert - Motor(en) {blocked} nicht bereit.")
        return

    print(f"=== {key}: {name} ===")

    # Erst alle Motoren nullen, damit vorherige Holds nicht weiterlaufen.
    zero_all(master)

    for cmd in cmds:
        send_line(master, cmd)
        time.sleep(0.02)


def handle_command(master, raw, initialized):
    """Einen Command verarbeiten - egal ob von Tastatur oder aus Unity.
    Rueckgabe: ("init" | "move" | "pause" | "exit" | "unknown", initialized)"""
    low = raw.strip().lower()

    # Leertaste (Tastatur) bzw. "pause"/"stop" (Unity)
    if raw == " " or low in PAUSE_COMMANDS:
        note("PAUSE - alle Motoren auf Torque 0")
        zero_all(master)
        return ("pause", initialized)

    cmd = raw.strip()

    if not cmd:
        return ("unknown", initialized)

    if low in INIT_COMMANDS:
        run_init(master)
        return ("init", True)

    if low in EXIT_COMMANDS:
        return ("exit", initialized)

    if cmd in COMMAND_MAP:
        run_movement(master, cmd, initialized)
        return ("move", initialized)

    print(f"WARN: Unbekannter Command: '{cmd}'")
    return ("unknown", initialized)


def print_menu():
    print()
    print("VR-Bridge (Unity ueber USB/TCP)")
    print("------------------------------")
    print("Commands von Unity oder per Tastatur:")
    print("0     - Init")
    for key, (name, cmds) in sorted(COMMAND_MAP.items()):
        print(f"{key}     - {name:12s} -> {', '.join(cmds)}")
    print("SPACE - Pause (alle Motoren nullen)   [Unity: 'pause']")
    print("x / q - Ende")
    print()


# ================= HAUPTSCHLEIFE =================

def main():
    cmd_queue = queue.Queue()
    vr_server = open_vr_server()

    vr_thread = threading.Thread(
        target=vr_receive_loop,
        args=(vr_server, cmd_queue),
        daemon=True,
        name="vr-receive",
    )
    vr_thread.start()

    with open_serial(MASTER_PORT) as master:
        initialized = False

        try:
            print_menu()
            print("Warte auf Commands...")

            running = True

            while running:
                # Antworten des Masters auswerten (Watchdog, Sleep, ...)
                handle_replies(master)

                if initialized:
                    send_heartbeats(master)
                    recover_sleeping(master)

                # --- Commands aus Unity ---
                while running:
                    try:
                        item = cmd_queue.get_nowait()
                    except queue.Empty:
                        break

                    if item is EVENT_VR_CONNECTED:
                        continue

                    if item is EVENT_VR_DISCONNECTED:
                        # App beendet, abgestuerzt oder Kabel ab: nicht
                        # bis zum Slave-Watchdog warten, sofort nullen.
                        note("VR-Verbindung weg - alle Motoren auf Torque 0")
                        zero_all(master)
                        continue

                    print(f"VR: {item}")
                    action, initialized = handle_command(master, item, initialized)

                    if action == "exit":
                        running = False

                # --- Tastatur bleibt aktiv (Not-Aus am PC) ---
                if running and msvcrt.kbhit():
                    key = msvcrt.getch().decode("utf-8", errors="ignore")

                    action, initialized = handle_command(master, key, initialized)

                    if action == "exit":
                        running = False

                time.sleep(0.01)

        finally:
            print()
            print("FINAL ZERO")
            zero_all(master)
            time.sleep(0.5)
            vr_server.close()

    print("DONE")


if __name__ == "__main__":
    main()
