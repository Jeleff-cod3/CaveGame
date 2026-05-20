using System;
using System.Text;
using NativeWebSocket;
using UnityEngine;

public sealed class CaveGameSocketClient
{
    private WebSocket socket;

    public event Action Opened;
    public event Action<string> MessageReceived;
    public event Action<string> Closed;
    public event Action<string> ErrorReceived;

    public bool IsOpen => socket != null && socket.State == WebSocketState.Open;

    public async void Connect(string url)
    {
        await CloseAsync();

        socket = new WebSocket(url);
        socket.OnOpen += () => Opened?.Invoke();
        socket.OnError += error => ErrorReceived?.Invoke(error);
        socket.OnClose += closeCode => Closed?.Invoke(closeCode.ToString());
        socket.OnMessage += bytes => MessageReceived?.Invoke(Encoding.UTF8.GetString(bytes));

        try
        {
            await socket.Connect();
        }
        catch (Exception exception)
        {
            ErrorReceived?.Invoke(exception.Message);
        }
    }

    public async void SendJson(string json)
    {
        if (!IsOpen)
        {
            return;
        }

        try
        {
            await socket.SendText(json);
        }
        catch (Exception exception)
        {
            ErrorReceived?.Invoke(exception.Message);
        }
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
            await socket.Close();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"WebSocket close failed: {exception.Message}");
        }
        finally
        {
            socket = null;
        }
    }
}
