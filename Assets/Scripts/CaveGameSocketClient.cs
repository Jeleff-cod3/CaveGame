using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

public sealed class CaveGameSocketClient
{
    private const int CloseTimeoutMs = 250;

    private WebSocket socket;
    private readonly Queue<string> outboundQueue = new Queue<string>();
    private bool sendLoopRunning;
    private int connectionGeneration;

    public event Action Opened;
    public event Action<string> MessageReceived;
    public event Action<string> Closed;
    public event Action<string> ErrorReceived;

    public bool IsOpen => socket != null && socket.State == WebSocketState.Open;

    public async void Connect(string url)
    {
        await CloseAsync();

        int generation = ++connectionGeneration;
        socket = new WebSocket(url);
        socket.OnOpen += () => Opened?.Invoke();
        socket.OnError += error => ErrorReceived?.Invoke(error);
        socket.OnClose += closeCode => Closed?.Invoke(closeCode.ToString());
        socket.OnMessage += bytes => MessageReceived?.Invoke(Encoding.UTF8.GetString(bytes));

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

        lock (outboundQueue)
        {
            outboundQueue.Enqueue(json);
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

        try
        {
            Task closeTask = socket.Close();
            Task completed = await Task.WhenAny(closeTask, Task.Delay(CloseTimeoutMs));
            if (completed != closeTask)
            {
                Debug.LogWarning("WebSocket close timed out; forcing local socket reset.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"WebSocket close failed: {exception.Message}");
        }
        finally
        {
            socket = null;
            lock (outboundQueue)
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
                lock (outboundQueue)
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
                }
                catch (Exception exception)
                {
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
}
