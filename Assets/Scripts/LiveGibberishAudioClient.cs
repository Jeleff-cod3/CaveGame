using System;
using System.Collections.Generic;
using System.Text;
using NativeWebSocket;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class LiveGibberishAudioClient : MonoBehaviour
{
    [Header("Server")]
    [SerializeField] private string websocketUrl = "ws://localhost:8000/ws/audio/";
    [SerializeField] private string[] whitelist = { "hello", "cave" };
    [SerializeField] private string seed = "cavegame-live-gibberish";
    [SerializeField] private bool connectOnStart = true;

    [Header("Audio")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private int frameMs = 20;
    [SerializeField] private string microphoneDevice = "";
    [SerializeField] private bool playFilteredAudio = true;

    private WebSocket socket;
    private AudioClip microphoneClip;
    private int lastMicPosition;
    private int frameSamples;
    private readonly List<float> pendingMicSamples = new List<float>(4096);
    private readonly Queue<float> playbackSamples = new Queue<float>(16000);
    private readonly object playbackLock = new object();

    public string LastStatus { get; private set; } = "idle";
    public string LastSegmentJson { get; private set; } = "";

    private async void Start()
    {
        frameSamples = Mathf.Max(1, sampleRate * frameMs / 1000);
        GetComponent<AudioSource>().playOnAwake = true;
        GetComponent<AudioSource>().loop = true;

        if (connectOnStart)
        {
            await Connect();
        }
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        socket?.DispatchMessageQueue();
#endif
        PumpMicrophone();
    }

    private async void OnDestroy()
    {
        StopMicrophone();
        if (socket != null)
        {
            await socket.Close();
            socket = null;
        }
    }

    public async System.Threading.Tasks.Task Connect()
    {
        await Disconnect();

        socket = new WebSocket(websocketUrl);
        socket.OnOpen += () =>
        {
            LastStatus = "connected";
            SendConfig();
            StartMicrophone();
        };
        socket.OnError += error => LastStatus = "error: " + error;
        socket.OnClose += closeCode => LastStatus = "closed: " + closeCode;
        socket.OnMessage += HandleMessage;

        LastStatus = "connecting";
        await socket.Connect();
    }

    public async System.Threading.Tasks.Task Disconnect()
    {
        StopMicrophone();
        if (socket != null)
        {
            await socket.Close();
            socket = null;
        }
    }

    private async void SendConfig()
    {
        if (socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        string json = "{\"type\":\"config\",\"config\":{\"whitelist\":" + BuildWhitelistJson() + ",\"seed\":\"" + Escape(seed) + "\"}}";
        await socket.SendText(json);
    }

    private void StartMicrophone()
    {
        StopMicrophone();
        microphoneClip = Microphone.Start(string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice, true, 1, sampleRate);
        lastMicPosition = 0;
    }

    private void StopMicrophone()
    {
        if (microphoneClip != null)
        {
            Microphone.End(string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice);
            microphoneClip = null;
        }
        pendingMicSamples.Clear();
    }

    private async void PumpMicrophone()
    {
        if (socket == null || socket.State != WebSocketState.Open || microphoneClip == null)
        {
            return;
        }

        int position = Microphone.GetPosition(string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice);
        if (position < 0 || position == lastMicPosition)
        {
            return;
        }

        int sampleCount = position > lastMicPosition
            ? position - lastMicPosition
            : microphoneClip.samples - lastMicPosition + position;

        float[] samples = new float[sampleCount];
        if (position > lastMicPosition)
        {
            microphoneClip.GetData(samples, lastMicPosition);
        }
        else
        {
            float[] tail = new float[microphoneClip.samples - lastMicPosition];
            float[] head = new float[position];
            microphoneClip.GetData(tail, lastMicPosition);
            microphoneClip.GetData(head, 0);
            Array.Copy(tail, 0, samples, 0, tail.Length);
            Array.Copy(head, 0, samples, tail.Length, head.Length);
        }

        lastMicPosition = position;
        pendingMicSamples.AddRange(samples);

        while (pendingMicSamples.Count >= frameSamples)
        {
            byte[] pcm = FloatToPcm16(pendingMicSamples, frameSamples);
            pendingMicSamples.RemoveRange(0, frameSamples);
            await socket.Send(pcm);
        }
    }

    private void HandleMessage(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        if (bytes[0] == (byte)'{')
        {
            LastSegmentJson = Encoding.UTF8.GetString(bytes);
            return;
        }

        if (!playFilteredAudio)
        {
            return;
        }

        lock (playbackLock)
        {
            for (int index = 0; index + 1 < bytes.Length; index += 2)
            {
                short sample = BitConverter.ToInt16(bytes, index);
                playbackSamples.Enqueue(sample / 32768f);
            }
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!playFilteredAudio)
        {
            return;
        }

        lock (playbackLock)
        {
            for (int index = 0; index < data.Length; index += channels)
            {
                float sample = playbackSamples.Count > 0 ? playbackSamples.Dequeue() : 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    data[index + channel] = sample;
                }
            }
        }
    }

    private static byte[] FloatToPcm16(List<float> samples, int count)
    {
        byte[] bytes = new byte[count * 2];
        for (int index = 0; index < count; index++)
        {
            short value = (short)Mathf.Clamp(Mathf.RoundToInt(samples[index] * 32767f), short.MinValue, short.MaxValue);
            byte[] encoded = BitConverter.GetBytes(value);
            bytes[index * 2] = encoded[0];
            bytes[index * 2 + 1] = encoded[1];
        }
        return bytes;
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private string BuildWhitelistJson()
    {
        if (whitelist == null || whitelist.Length == 0)
        {
            return "[]";
        }

        StringBuilder builder = new StringBuilder("[");
        for (int index = 0; index < whitelist.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }
            builder.Append('"').Append(Escape(whitelist[index])).Append('"');
        }
        builder.Append(']');
        return builder.ToString();
    }
}
