"""
TCP server that receives strings from the Quest app over the USB cable.

The USB transport is provided by ADB. After plugging the Quest in
(developer mode on, USB debugging authorized in the headset), run once
per session on the PC:

    adb reverse tcp:7777 tcp:7777

That maps 127.0.0.1:7777 ON THE QUEST to 127.0.0.1:7777 on this PC, so
the app's TcpClient reaches this script through the cable. adb ships
with Meta Quest Developer Hub, the Android platform-tools, and inside
the Unity install (.../PlaybackEngines/AndroidPlayer/SDK/platform-tools).

Bonus: pressing Play in the Unity editor on this same PC connects to
this script directly - no adb needed for editor testing.

To use Wi-Fi instead of USB later: change HOST to "0.0.0.0" here and
set `host` in UsbStringSender.cs to this PC's LAN IP. Nothing else changes.
"""

import os
import socket

HOST, PORT = "127.0.0.1", 7777

server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
if os.name != "nt":
    # On Windows, SO_REUSEADDR would let a second copy of this script bind
    # the same port silently, and connections then go to the wrong one.
    # Without it, a duplicate instance fails loudly with WinError 10048.
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
try:
    server.bind((HOST, PORT))
except OSError as e:
    raise SystemExit(
        f"Port {PORT} is already in use by another program "
        f"(another copy of this script, or adb after 'adb forward'). "
        f"Original error: {e}"
    )
server.listen(1)
print(f"Listening on {HOST}:{PORT}.")
print(f"Run 'adb reverse tcp:{PORT} tcp:{PORT}', then connect from the app...")

try:
    while True:  # keep accepting after a disconnect, handy while testing
        client, address = server.accept()
        print(f"Connected: {address}")
        buffer = b""
        try:
            while True:
                data = client.recv(1024)
                if not data:
                    break
                buffer += data
                # The Unity script terminates every string with '\n'
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    print("Received:", line.decode("utf-8", errors="replace"))
        except OSError:
            pass
        finally:
            client.close()
            print("Client disconnected. Waiting for a new connection...")
except KeyboardInterrupt:
    pass
finally:
    server.close()
    print("Server stopped.")