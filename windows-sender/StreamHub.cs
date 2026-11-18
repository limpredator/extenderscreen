using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace ExtendedScreen.Sender;

/// <summary>
/// Owns the single current viewer connection (see PROTOCOL.md: v1 supports
/// one viewer at a time). Handles the handshake, keeps the most recent
/// keyframe access unit around so a newly-connected client can start
/// decoding immediately, and forwards every subsequent frame.
/// </summary>
public sealed class StreamHub
{
    private const byte MsgVideoFrame = 0x01;

    private readonly object _lock = new();
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private byte[]? _lastKeyFrameAu;

    /// Fired exactly once per actual connect/disconnect transition (not
    /// once per reconnect attempt) — used to attach/detach the virtual
    /// monitor so it only exists on the Windows desktop while someone is
    /// actually viewing it. See SenderService's subscription to these.
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public StreamHub(int width, int height, int fps)
    {
        _width = width;
        _height = height;
        _fps = fps;
    }

    public void AttachClient(TcpClient client)
    {
        bool hadClient;
        lock (_lock)
        {
            hadClient = _client is not null;
            DropCurrentClientLocked();

            _client = client;
            _stream = client.GetStream();

            try
            {
                WriteHandshake(_stream);
                if (_lastKeyFrameAu is { } kf)
                {
                    WriteMessage(_stream, MsgVideoFrame, kf);
                }
            }
            catch (IOException)
            {
                DropCurrentClientLocked();
            }
        }

        if (!hadClient) ClientConnected?.Invoke();
    }

    public void Publish(byte[] accessUnit, bool isKeyFrame)
    {
        bool droppedClient = false;
        lock (_lock)
        {
            if (isKeyFrame) _lastKeyFrameAu = accessUnit;

            if (_stream is null) return;

            try
            {
                WriteMessage(_stream, MsgVideoFrame, accessUnit);
            }
            catch (IOException)
            {
                DropCurrentClientLocked();
                droppedClient = true;
            }
        }

        if (droppedClient) ClientDisconnected?.Invoke();
    }

    private void DropCurrentClientLocked()
    {
        try { _client?.Close(); } catch { /* already gone */ }
        _client = null;
        _stream = null;
    }

    private void WriteHandshake(NetworkStream stream)
    {
        Span<byte> header = stackalloc byte[4 + 1 + 4 + 4 + 4];
        Encoding.ASCII.GetBytes("EXSC").CopyTo(header);
        header[4] = 1; // protocol version
        BinaryPrimitives.WriteUInt32BigEndian(header[5..], (uint)_width);
        BinaryPrimitives.WriteUInt32BigEndian(header[9..], (uint)_height);
        BinaryPrimitives.WriteUInt32BigEndian(header[13..], (uint)_fps);
        stream.Write(header);
        stream.Flush();
    }

    private static void WriteMessage(NetworkStream stream, byte type, byte[] payload)
    {
        Span<byte> prefix = stackalloc byte[5];
        prefix[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(prefix[1..], (uint)payload.Length);
        stream.Write(prefix);
        stream.Write(payload);
        stream.Flush();
    }
}
