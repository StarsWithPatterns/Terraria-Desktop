using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TerrariaDesktop.Common
{
	/// <summary>
	/// A tiny loopback-only TCP server. The game thread hands it the newest frame,
	/// a background thread sends it to the overlay, and another background thread
	/// reads the overlay's control messages. Only the newest frame is ever kept, so
	/// a slow overlay can never make the game wait.
	/// </summary>
	public sealed class FrameServer : IDisposable
	{
		private readonly int _port;
		private readonly object _frameLock = new();
		private readonly AutoResetEvent _frameReady = new(false);
		private TcpListener _listener;
		private Thread _acceptThread;
		private volatile bool _running;
		private volatile TcpClient _client;

		// Double buffer: the game writes into _pending, the sender swaps it with _sending.
		private byte[] _pending = Array.Empty<byte>();
		private int _pendingLength;
		private bool _hasPending;
		private byte[] _sending = Array.Empty<byte>();

		// Latest control message from the overlay.
		private volatile int _mode = (int)FrameProtocol.OverlayMode.Wander;
		private volatile int _direction = 1;
		private volatile bool _walking;
		private float _walkSpeed = 2f;

		public FrameServer(int port)
		{
			_port = port;
		}

		public bool HasClient => _client is { Connected: true };
		public string LastError { get; private set; }

		public FrameProtocol.OverlayMode Mode => (FrameProtocol.OverlayMode)_mode;
		public int Direction => _direction;
		public bool Walking => _walking;
		public float WalkSpeed => Volatile.Read(ref _walkSpeed);

		public void Start()
		{
			if (_running)
				return;

			_running = true;
			_listener = new TcpListener(IPAddress.Loopback, _port);
			_listener.Start();
			_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "TerrariaDesktop accept" };
			_acceptThread.Start();
		}

		/// <summary>Copies a ready-made packet (header + pixels) for the sender thread. Called on the game thread.</summary>
		public void Publish(byte[] packet, int length)
		{
			if (!HasClient)
				return;

			lock (_frameLock) {
				if (_pending.Length < length)
					_pending = new byte[length];
				Buffer.BlockCopy(packet, 0, _pending, 0, length);
				_pendingLength = length;
				_hasPending = true;
			}
			_frameReady.Set();
		}

		private void AcceptLoop()
		{
			while (_running) {
				TcpClient client;
				try {
					client = _listener.AcceptTcpClient();
				}
				catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException) {
					if (_running)
						LastError = e.Message;
					return;
				}

				client.NoDelay = true;
				client.SendBufferSize = 1 << 20;

				// One overlay at a time: a new connection replaces the old one.
				var old = _client;
				_client = client;
				try { old?.Close(); } catch { }

				new Thread(() => SendLoop(client)) { IsBackground = true, Name = "TerrariaDesktop send" }.Start();
				new Thread(() => ReceiveLoop(client)) { IsBackground = true, Name = "TerrariaDesktop receive" }.Start();
			}
		}

		private void SendLoop(TcpClient client)
		{
			try {
				var stream = client.GetStream();
				while (_running && _client == client) {
					if (!_frameReady.WaitOne(500))
						continue;

					int length;
					lock (_frameLock) {
						if (!_hasPending)
							continue;
						(_pending, _sending) = (_sending, _pending);
						length = _pendingLength;
						_hasPending = false;
					}
					stream.Write(_sending, 0, length);
				}
			}
			catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidOperationException) {
				// Overlay closed; wait for the next one.
			}
			finally {
				Drop(client);
			}
		}

		private void ReceiveLoop(TcpClient client)
		{
			var buffer = new byte[FrameProtocol.ControlSize];
			try {
				var stream = client.GetStream();
				while (_running && _client == client) {
					int read = 0;
					while (read < buffer.Length) {
						int n = stream.Read(buffer, read, buffer.Length - read);
						if (n <= 0)
							return;
						read += n;
					}

					if (BitConverter.ToUInt32(buffer, 0) != FrameProtocol.ControlMagic)
						return; // Not our overlay. Hang up.

					_mode = BitConverter.ToInt32(buffer, 4);
					_direction = BitConverter.ToInt32(buffer, 8) < 0 ? -1 : 1;
					_walking = BitConverter.ToInt32(buffer, 12) != 0;
					Volatile.Write(ref _walkSpeed, Math.Clamp(BitConverter.ToSingle(buffer, 16), 0f, 20f));
				}
			}
			catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidOperationException) {
			}
			finally {
				Drop(client);
			}
		}

		private void Drop(TcpClient client)
		{
			if (_client == client) {
				_client = null;
				_walking = false;
			}
			try { client.Close(); } catch { }
		}

		public void Dispose()
		{
			_running = false;
			try { _listener?.Stop(); } catch { }
			var c = _client;
			_client = null;
			try { c?.Close(); } catch { }
			_frameReady.Set();
		}
	}
}
