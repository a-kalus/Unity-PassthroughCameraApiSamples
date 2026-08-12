using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Sends bridge commands from the Quest to the Python VR bridge on the PC
/// through the USB cable (TCP, tunneled by 'adb reverse tcp:7777 tcp:7777').
///
/// Transport and commands in one component:
///  - a single background writer thread with a message queue, so calling a
///    command every frame does not cost framerate
///  - one public no-arg method per command (Init, MoveUp, ...), ready to be
///    dropped into an OnClick()/OnSelect() slot of a UI Button or XR/Poke
///    Interactable, or called from your own code
///
/// Setup: put this on any GameObject. Leave connectOnStart on, then wire the
/// command methods to your buttons. Call Init() before any movement - the
/// Python bridge rejects movements until Ping + Init succeeded.
///
/// Works in Play Mode too (connects directly to the Python bridge on the
/// same PC, no adb needed there).
/// </summary>
public class USBStringSender : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("Keep 127.0.0.1 for USB (adb reverse). Switch to the PC's LAN IP to use Wi-Fi instead.")]
    public string host = "127.0.0.1";

    [Tooltip("Must match VR_PORT in the Python bridge.")]
    public int port = 7777;

    [Tooltip("Open the connection automatically on start.")]
    public bool connectOnStart = true;

    [Tooltip("Log each write. Leave OFF when sending every frame - logging is slow.")]
    public bool verboseLogging = false;

    public bool IsConnected => _connected;

    TcpClient _client;
    NetworkStream _stream;
    Thread _writerThread;
    readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    readonly AutoResetEvent _signal = new AutoResetEvent(false);
    volatile bool _connected;
    volatile bool _running;

    void Start()
    {
        if (connectOnStart) Connect();
    }

    // =================== COMMANDS ===================
    // The strings must match COMMAND_MAP / PAUSE_COMMANDS in the Python bridge.

    public void Init() => Send("0");     // Ping + Init, run this first
    public void MoveUp() => Send("1");     // Nach oben
    public void MoveDown() => Send("2");     // Nach unten
    public void MoveLeft() => Send("3");     // Nach links
    public void MoveRight() => Send("4");     // Nach rechts
    public void MoveFront() => Send("5");     // Nach vorne
    public void MoveBack() => Send("6");     // Nach hinten
    public void Pause() => Send("pause"); // all motors to torque 0

    /// <summary>Sends any raw command string. Cheap: only enqueues,
    /// so it is safe to call every frame. '\n' is appended automatically.</summary>
    public void Send(string command)
    {
        if (!_running)
        {
            Debug.LogWarning($"[Bridge] Not connected - '{command}' dropped.");
            Connect();      // try to (re)connect for the next call
            return;
        }
        _queue.Enqueue(command);
        _signal.Set();
    }

    // =================== TRANSPORT ===================

    /// <summary>Starts the writer thread, which opens the connection.</summary>
    public void Connect()
    {
        if (_running) return;
        _running = true;
        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "VrBridgeSender" };
        _writerThread.Start();
    }

    void WriterLoop()
    {
        // 1) Connect off the main thread, so there is no frame hitch.
        try
        {
            var client = new TcpClient { NoDelay = true };   // push small strings out immediately
            if (!client.ConnectAsync(host, port).Wait(3000))
                throw new TimeoutException($"No bridge reachable at {host}:{port}");
            _client = client;
            _stream = client.GetStream();
            _connected = true;
            Debug.Log($"[Bridge] Connected to {host}:{port}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Bridge] Connect failed: " + e.Message +
                "\nIs VR_Bridge_Unity.py running and 'adb reverse tcp:7777 tcp:7777' set?");
            Cleanup();
            return;
        }

        // 2) Drain the queue until Close() is called or the socket dies.
        var sb = new StringBuilder(128);
        while (_running)
        {
            _signal.WaitOne();              // sleeps here; zero CPU while idle
            sb.Clear();
            while (_queue.TryDequeue(out var cmd))
                sb.Append(cmd).Append('\n'); // '\n' separates the commands
            if (sb.Length == 0) continue;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                _stream.Write(bytes, 0, bytes.Length);
                if (verboseLogging) Debug.Log("[Bridge] Sent: " + sb.ToString().TrimEnd());
            }
            catch (Exception e)
            {
                Debug.LogError("[Bridge] Send failed: " + e.Message);
                break;
            }
        }
        Cleanup();
    }

    public void Close()
    {
        _running = false;
        _signal.Set();                  // wake the writer so it can exit
        _writerThread?.Join(500);
        Cleanup();
        _writerThread = null;
    }

    void Cleanup()
    {
        _connected = false;
        _running = false;
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    // Closing the socket makes the bridge zero all motors immediately,
    // so quitting the app or unplugging never leaves torque standing.
    void OnDestroy() => Close();
    void OnApplicationQuit() => Close();

    // --- Optional: controller buttons instead of UI (Meta XR SDK) ---
    void Update()
     {
         //if (OVRInput.GetDown(OVRInput.Button.One)) Init();
         //if (OVRInput.GetDown(OVRInput.Button.Two)) Pause();
         if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickLeft))   MoveLeft();
         if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown)) MoveDown();
     }
}