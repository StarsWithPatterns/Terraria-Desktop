using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace DesktopOverlay;

/// <summary>
/// Connects to the mod on 127.0.0.1 and keeps the newest frame it sends.
/// Reconnects by itself whenever Terraria isn't running or you leave a world.
/// </summary>
internal sealed class FrameClient : IDisposable
{
    private readonly int _port;
    private readonly object _swapLock = new();
    private readonly object _sendLock = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private volatile NetworkStream? _stream;

    // Triple buffer: the network thread fills _incoming, publishes it as _latest,
    // and the UI thread swaps _latest into _display.
    private Frame _incoming = new();
    private Frame _latest = new();
    private Frame _display = new();
    private bool _hasNew;

    private byte[] _lastControl = Array.Empty<byte>();

    public FrameClient(int port)
    {
        _port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = "Frame client" };
        _thread.Start();
    }

    public bool Connected => _stream != null;

    /// <summary>Called on the UI thread. Returns the newest frame if one arrived since the last call.</summary>
    public Frame? TakeNewFrame()
    {
        lock (_swapLock)
        {
            if (!_hasNew)
                return null;
            (_latest, _display) = (_display, _latest);
            _hasNew = false;
            return _display;
        }
    }

    public void SendControl(OverlayMode mode, int direction, bool walking, float walkSpeed)
    {
        var packet = new byte[FrameProtocol.ControlSize];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), FrameProtocol.ControlMagic);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), (int)mode);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), direction < 0 ? -1 : 1);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), walking ? 1 : 0);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(16), walkSpeed);

        lock (_sendLock)
        {
            if (packet.AsSpan().SequenceEqual(_lastControl))
                return;
            _lastControl = packet;
            TryWrite(packet);
        }
    }

    private void TryWrite(byte[] packet)
    {
        var stream = _stream;
        if (stream == null)
            return;
        try { stream.Write(packet, 0, packet.Length); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }

    private void Run()
    {
        var header = new byte[FrameProtocol.FrameHeaderSize];
        while (_running)
        {
            try
            {
                using var client = new TcpClient { NoDelay = true, ReceiveBufferSize = 1 << 20 };
                client.Connect(IPAddress.Loopback, _port);
                using var stream = client.GetStream();

                _stream = stream;
                lock (_sendLock)
                {
                    if (_lastControl.Length > 0)
                        TryWrite(_lastControl);
                }

                while (_running)
                {
                    ReadExactly(stream, header);
                    ReadFrame(stream, header);
                }
            }
            catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException or InvalidDataException)
            {
                // Terraria not running, not in a world yet, or it closed. Try again shortly.
            }
            finally
            {
                _stream = null;
                PublishEmpty();
            }

            if (_running)
                Thread.Sleep(1000);
        }
    }

    private void ReadFrame(NetworkStream stream, byte[] header)
    {
        var h = header.AsSpan();
        if (BinaryPrimitives.ReadUInt32LittleEndian(h) != FrameProtocol.FrameMagic)
            throw new InvalidDataException("Not a Terraria Desktop frame.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(h[4..]) != FrameProtocol.Version)
            throw new InvalidDataException("The mod and the overlay are different versions.");

        var f = _incoming;
        f.Flags = (FrameProtocol.FrameFlags)BinaryPrimitives.ReadUInt16LittleEndian(h[6..]);
        f.Width = BinaryPrimitives.ReadInt32LittleEndian(h[8..]);
        f.Height = BinaryPrimitives.ReadInt32LittleEndian(h[12..]);
        f.FeetRow = BinaryPrimitives.ReadInt32LittleEndian(h[16..]);
        f.Direction = BinaryPrimitives.ReadInt32LittleEndian(h[20..]);
        f.VelocityX = BinaryPrimitives.ReadSingleLittleEndian(h[24..]);
        f.VelocityY = BinaryPrimitives.ReadSingleLittleEndian(h[28..]);
        int payload = BinaryPrimitives.ReadInt32LittleEndian(h[32..]);
        f.Number = BinaryPrimitives.ReadUInt32LittleEndian(h[36..]);

        if (f.Width < 0 || f.Height < 0 || f.Width > FrameProtocol.MaxDimension || f.Height > FrameProtocol.MaxDimension
            || payload != f.Width * f.Height * 4)
            throw new InvalidDataException("Bad frame size.");

        if (f.Pixels.Length != payload)
            f.Pixels = new byte[payload];
        ReadExactly(stream, f.Pixels);

        lock (_swapLock)
        {
            (_incoming, _latest) = (_latest, _incoming);
            _hasNew = true;
        }
    }

    private void PublishEmpty()
    {
        lock (_swapLock)
        {
            _incoming.Flags = FrameProtocol.FrameFlags.None;
            _incoming.Width = _incoming.Height = 0;
            (_incoming, _latest) = (_latest, _incoming);
            _hasNew = true;
        }
    }

    private static void ReadExactly(NetworkStream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
                throw new IOException("Connection closed.");
            read += n;
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _stream?.Close(); } catch { }
    }
}
