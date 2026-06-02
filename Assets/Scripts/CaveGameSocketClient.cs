using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

public sealed class CaveGameSocketClient
{
    private const int CloseTimeoutMs = 750;

    private WebSocket socket;
    private readonly Queue<string> outboundQueue = new Queue<string>();
    private readonly object queueLock = new object();
    private bool sendLoopRunning;
    private int connectionGeneration;
    private int queuedMessagesTotal;
    private int sentMessagesTotal;
    private int receivedMessagesTotal;
    private int sendErrorCount;
    private int transportErrorCount;
    private int maxObservedQueueDepth;
    private string lastOutboundType = "none";
    private string lastInboundType = "none";
    private string lastError = "none";
    private string lastCloseCode = "none";
    private float lastOpenTime = -1f;
    private float lastOutboundTime = -1f;
    private float lastInboundTime = -1f;
    private float lastErrorTime = -1f;
    private float lastCloseTime = -1f;

    public event Action Opened;
    public event Action<string> MessageReceived;
    public event Action<string> Closed;
    public event Action<string> ErrorReceived;

    public bool IsOpen => socket != null && socket.State == WebSocketState.Open;
    public string CurrentState => socket == null ? "null" : socket.State.ToString();

    public async void Connect(string url)
    {
        await CloseAsync();

        int generation = ++connectionGeneration;
        socket = new WebSocket(url);
        socket.OnOpen += () =>
        {
            lastOpenTime = Time.realtimeSinceStartup;
            Opened?.Invoke();
        };
        socket.OnError += error =>
        {
            transportErrorCount++;
            lastError = error ?? "unknown";
            lastErrorTime = Time.realtimeSinceStartup;
            ErrorReceived?.Invoke(error);
        };
        socket.OnClose += closeCode =>
        {
            int numericCloseCode = Convert.ToInt32(closeCode);
            lastCloseCode = $"{numericCloseCode}({closeCode})";
            lastCloseTime = Time.realtimeSinceStartup;
            Closed?.Invoke(lastCloseCode);
        };
        socket.OnMessage += bytes =>
        {
            string payload = Encoding.UTF8.GetString(bytes);
            receivedMessagesTotal++;
            lastInboundType = ExtractTypeFromJson(payload);
            lastInboundTime = Time.realtimeSinceStartup;
            MessageReceived?.Invoke(payload);
        };

        try
        {
            await socket.Connect();
            StartSendLoopIfNeeded(generation);
        }
        catch (Exception exception)
        {
            ErrorReceived?.Invoke(exception.Message);
        }
    }

    public void SendJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        lock (queueLock)
        {
            outboundQueue.Enqueue(json);
            queuedMessagesTotal++;
            if (outboundQueue.Count > maxObservedQueueDepth)
            {
                maxObservedQueueDepth = outboundQueue.Count;
            }
        }

        StartSendLoopIfNeeded(connectionGeneration);
    }

    public void Pump()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        socket?.DispatchMessageQueue();
#endif
    }

    public async void Close()
    {
        await CloseAsync();
    }

    private async System.Threading.Tasks.Task CloseAsync()
    {
        if (socket == null)
        {
            return;
        }

        WebSocket closingSocket = socket;
        try
        {
            Task closeTask = closingSocket.Close();
            Task completed = await Task.WhenAny(closeTask, Task.Delay(CloseTimeoutMs));
            if (completed != closeTask)
            {
                string stateText = closingSocket.State.ToString();
                if (closingSocket.State == WebSocketState.Closing || closingSocket.State == WebSocketState.Closed)
                {
                    Debug.Log($"WebSocket close still completing ({stateText}); forcing local socket reset.");
                }
                else
                {
                    Debug.LogWarning($"WebSocket close timed out in state={stateText}; forcing local socket reset.");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"WebSocket close failed: {exception.Message}");
        }
        finally
        {
            socket = null;
            lock (queueLock)
            {
                outboundQueue.Clear();
            }
            sendLoopRunning = false;
        }
    }

    private void StartSendLoopIfNeeded(int generation)
    {
        if (sendLoopRunning || socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        sendLoopRunning = true;
        _ = RunSendLoop(generation);
    }

    private async Task RunSendLoop(int generation)
    {
        try
        {
            while (generation == connectionGeneration && socket != null && socket.State == WebSocketState.Open)
            {
                string nextMessage = null;
                lock (queueLock)
                {
                    if (outboundQueue.Count > 0)
                    {
                        nextMessage = outboundQueue.Dequeue();
                    }
                }

                if (nextMessage == null)
                {
                    await Task.Delay(8);
                    continue;
                }

                try
                {
                    await socket.SendText(nextMessage);
                    sentMessagesTotal++;
                    lastOutboundType = ExtractTypeFromJson(nextMessage);
                    lastOutboundTime = Time.realtimeSinceStartup;
                }
                catch (Exception exception)
                {
                    sendErrorCount++;
                    lastError = exception.Message;
                    lastErrorTime = Time.realtimeSinceStartup;
                    ErrorReceived?.Invoke(exception.Message);
                    break;
                }
            }
        }
        finally
        {
            sendLoopRunning = false;
            if (generation == connectionGeneration && socket != null && socket.State == WebSocketState.Open)
            {
                StartSendLoopIfNeeded(generation);
            }
        }
    }

    public string GetDebugSnapshot()
    {
        int queueDepth;
        lock (queueLock)
        {
            queueDepth = outboundQueue.Count;
        }

        return $"state={CurrentState}, gen={connectionGeneration}, queue={queueDepth}, queueMax={maxObservedQueueDepth}, queued={queuedMessagesTotal}, sent={sentMessagesTotal}, recv={receivedMessagesTotal}, sendErr={sendErrorCount}, transportErr={transportErrorCount}, lastOut={lastOutboundType}@{FormatAgo(lastOutboundTime)}, lastIn={lastInboundType}@{FormatAgo(lastInboundTime)}, lastErr={lastError}@{FormatAgo(lastErrorTime)}, lastClose={lastCloseCode}@{FormatAgo(lastCloseTime)}, openAgo={FormatAgo(lastOpenTime)}";
    }

    private static string ExtractTypeFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "empty";
        }

        const string token = "\"type\"";
        int tokenIndex = json.IndexOf(token, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            return "no_type";
        }

        int colon = json.IndexOf(':', tokenIndex + token.Length);
        if (colon < 0)
        {
            return "type_malformed";
        }

        int firstQuote = json.IndexOf('"', colon + 1);
        if (firstQuote < 0)
        {
            return "type_non_string";
        }

        int secondQuote = json.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
        {
            return "type_malformed";
        }

        return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    private static string FormatAgo(float timestamp)
    {
        if (timestamp < 0f)
        {
            return "n/a";
        }

        float deltaMs = (Time.realtimeSinceStartup - timestamp) * 1000f;
        return $"{Mathf.Max(0f, deltaMs):0}ms";
    }
}
