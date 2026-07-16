using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Sends strings from the Quest to the PC through the USB cable.
///
/// How it works: ADB tunnels a TCP port over USB. After plugging in the
/// Quest, run this on the PC once per session:
///
///     adb reverse tcp:7777 tcp:7777
///
/// That maps 127.0.0.1:7777 ON THE QUEST to 127.0.0.1:7777 on the PC,
/// so this plain TcpClient reaches the Python server through the cable.
///
/// Usage: put this on any GameObject, start the Python receiver on the PC,
/// run the adb reverse command, then call Connect() and SendString("...").
/// Also works when pressing Play in the editor (connects to the local
/// Python server directly, no adb needed).
/// </summary>
public class UsbStringSender : MonoBehaviour
{
    [Tooltip("Keep 127.0.0.1 for USB (adb reverse). Switch to the PC's LAN IP to use Wi-Fi instead.")]
    public string host = "127.0.0.1";
    public int port = 7777;

    public bool IsConnected => _client != null && _client.Connected;

    TcpClient _client;
    NetworkStream _stream;
    readonly object _writeLock = new object();

    /// <summary>Opens the TCP connection. Runs on a background thread.</summary>
    public void Connect()
    {
        if (IsConnected) return;
        new Thread(() =>
        {
            try
            {
                var client = new TcpClient { NoDelay = true }; // flush small strings immediately
                if (!client.ConnectAsync(host, port).Wait(3000))
                    throw new TimeoutException($"No server reachable at {host}:{port}");
                _client = client;
                _stream = client.GetStream();
                Debug.Log($"[USB] Connected to {host}:{port}");
            }
            catch (Exception e)
            {
                Debug.LogError("[USB] Connect failed: " + e.Message +
                    "\nDid you run 'adb reverse tcp:7777 tcp:7777' and start the Python server first?");
                Close();
            }
        }).Start();
    }

    /// <summary>Sends a UTF-8 string, terminated with '\n' so the receiver can frame it.</summary>
    public void SendString(string message)
    {
        if (!IsConnected) { Debug.LogWarning("[USB] Not connected - call Connect() first."); return; }
        new Thread(() =>
        {
            try
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(message + "\n");
                lock (_writeLock)
                {
                    _stream.Write(utf8, 0, utf8.Length);
                    _stream.Flush();
                }
                Debug.Log("[USB] Sent: " + message);
            }
            catch (Exception e)
            {
                Debug.LogError("[USB] Send failed: " + e.Message);
                Close();
            }
        }).Start();
    }

    public void Close()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    void OnDestroy() => Close();
    void OnApplicationQuit() => Close();

    // --- Example trigger (uncomment if you use the Meta XR SDK) ---
void Update()
     {
         if (OVRInput.GetDown(OVRInput.Button.One))   // 'A' button
         {
             if (!IsConnected) Connect();
             else SendString("Hello over USB at " + Time.time);
         }
     }
}
