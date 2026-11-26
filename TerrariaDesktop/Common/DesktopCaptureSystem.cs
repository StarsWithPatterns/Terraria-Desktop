using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaDesktop.Common
{
	/// <summary>
	/// Draws the local player into an off-screen texture with the game's own player
	/// renderer (so armor, vanity, dyes, wings, held items and animations all come
	/// straight from the game) and hands the pixels to <see cref="FrameServer"/>.
	/// </summary>
	public class DesktopCaptureSystem : ModSystem
	{
		/// <summary>Size of the picture we send, in game pixels. Big enough for wings and most mounts.</summary>
		public const int CanvasSize = 192;

		/// <summary>Row of the canvas where the player's feet go. The overlay lines this row up with the top of the taskbar.</summary>
		public const int FeetRow = CanvasSize - 8;

		private static FrameServer _server;
		private RenderTarget2D _target;
		private Color[] _pixels;
		private byte[] _packet;
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private double _nextFrameAt;
		private uint _frameNumber;
		private bool _reportedError;

		// Our own walk cycle, used when the overlay is walking the character around by itself.
		private double _walkCounter;
		private int _walkFrame = 7;

		public override void Load()
		{
			if (Main.dedServ)
				return;

			Main.OnPreDraw += OnPreDraw;
		}

		public override void Unload()
		{
			Main.OnPreDraw -= OnPreDraw;
			_server?.Dispose();
			_server = null;

			var target = _target;
			_target = null;
			if (target != null)
				Main.QueueMainThreadAction(() => target.Dispose());
		}

		private bool EnsureServer()
		{
			if (_server != null)
				return true;

			int port = ModContent.GetInstance<DesktopConfig>()?.Port ?? FrameProtocol.DefaultPort;
			try {
				var server = new FrameServer(port);
				server.Start();
				_server = server;
				Mod.Logger.Info($"Waiting for the desktop overlay on 127.0.0.1:{port}");
				return true;
			}
			catch (Exception e) {
				if (!_reportedError) {
					_reportedError = true;
					Mod.Logger.Error($"Could not open port {port} for the desktop overlay. Change the port in the mod config.", e);
				}
				return false;
			}
		}

		// Main.OnPreDraw runs at the start of every drawn frame, before the game starts
		// its own SpriteBatch work, so it is a safe place to switch render targets.
		private void OnPreDraw(GameTime gameTime)
		{
			var config = ModContent.GetInstance<DesktopConfig>();
			if (config == null || !config.Enabled || !EnsureServer() || !_server.HasClient)
				return;

			double now = _clock.Elapsed.TotalSeconds;
			if (now < _nextFrameAt)
				return;
			_nextFrameAt = Math.Max(_nextFrameAt + 1.0 / config.FramesPerSecond, now - 0.5);

			Player player = Main.gameMenu ? null : Main.LocalPlayer;
			if (player == null || !player.active || player.dead) {
				SendEmpty(player);
				return;
			}

			try {
				if (!DrawPlayerToTarget(player, config.IgnoreWorldLighting, out bool walkOverride))
					return;
				SendFrame(player, walkOverride);
			}
			catch (Exception e) {
				if (!_reportedError) {
					_reportedError = true;
					Mod.Logger.Error("Drawing the desktop character failed. It will keep trying, but only this first error is logged.", e);
				}
			}
		}

		private bool DrawPlayerToTarget(Player player, bool ignoreLighting, out bool walkOverride)
		{
			walkOverride = false;
			var device = Main.instance.GraphicsDevice;

			if (_target == null || _target.IsDisposed) {
				_target = new RenderTarget2D(device, CanvasSize, CanvasSize, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
				_pixels = new Color[CanvasSize * CanvasSize];
			}

			// Everything we change on the player only lasts for this one draw call.
			bool oldDoll = player.isDisplayDollOrInanimate;
			int oldDirection = player.direction;
			Rectangle oldLegFrame = player.legFrame;
			Rectangle oldBodyFrame = player.bodyFrame;

			var oldTargets = device.GetRenderTargets();
			try {
				device.SetRenderTarget(_target);
				device.Clear(Color.Transparent);

				// isDisplayDollOrInanimate makes the renderer use white light instead of
				// the world's lighting, like the character select screen does.
				if (ignoreLighting)
					player.isDisplayDollOrInanimate = true;

				walkOverride = ApplyWalkOverride(player);

				// Put the player's feet on FeetRow, centred horizontally. DrawPlayer takes a
				// world position and subtracts Main.screenPosition, so add it back.
				var topLeft = new Vector2((CanvasSize - player.width) / 2f, FeetRow - player.height);
				topLeft = topLeft.Floor();

				// Immediate mode is required so dye shaders apply to each piece of gear.
				Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
					DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
				try {
					Main.PlayerRenderer.DrawPlayer(Main.Camera, player, topLeft + Main.screenPosition,
						player.fullRotation, player.fullRotationOrigin, 0f, 1f);
				}
				finally {
					Main.spriteBatch.End();
				}
			}
			finally {
				player.isDisplayDollOrInanimate = oldDoll;
				player.direction = oldDirection;
				player.legFrame = oldLegFrame;
				player.bodyFrame = oldBodyFrame;

				if (oldTargets == null || oldTargets.Length == 0)
					device.SetRenderTarget(null);
				else
					device.SetRenderTargets(oldTargets);
			}

			_target.GetData(_pixels);
			return true;
		}

		/// <summary>
		/// In Wander mode the overlay walks the character around the desktop while your
		/// real character may be standing still, so we play the game's own walking frames
		/// (leg/body frames 7 to 19) and face the way the overlay is walking.
		/// </summary>
		private bool ApplyWalkOverride(Player player)
		{
			if (_server.Mode != FrameProtocol.OverlayMode.Wander)
				return false;

			player.direction = _server.Direction;

			// Mounts, grappling and item use keep their real animation.
			if (!_server.Walking || player.mount.Active || player.itemAnimation > 0 || player.pulley || player.grappling[0] >= 0) {
				_walkCounter = 0;
				return true;
			}

			// Same pacing as Player.PlayerFrame: one frame every 8 units of |velocity.X| * 1.3.
			_walkCounter += _server.WalkSpeed * 1.3 * (60.0 / ModContent.GetInstance<DesktopConfig>().FramesPerSecond);
			while (_walkCounter > 8) {
				_walkCounter -= 8;
				_walkFrame = _walkFrame >= 19 ? 7 : _walkFrame + 1;
			}

			int frameHeight = player.legFrame.Height > 0 ? player.legFrame.Height : 56;
			player.legFrame.Y = _walkFrame * frameHeight;
			player.bodyFrame.Y = _walkFrame * player.bodyFrame.Height;
			return true;
		}

		private void SendFrame(Player player, bool walkOverride)
		{
			int pixelBytes = CanvasSize * CanvasSize * 4;
			EnsurePacket(FrameProtocol.FrameHeaderSize + pixelBytes);

			var flags = FrameProtocol.FrameFlags.InWorld;
			if (player.velocity.Y == 0f)
				flags |= FrameProtocol.FrameFlags.OnGround;
			if (walkOverride)
				flags |= FrameProtocol.FrameFlags.WalkOverride;
			if (player.mount.Active)
				flags |= FrameProtocol.FrameFlags.Mounted;

			WriteHeader(flags, CanvasSize, CanvasSize, player.direction, player.velocity, pixelBytes);

			// XNA colours are RGBA and already premultiplied, which is exactly what
			// Windows layered windows want, apart from the byte order (BGRA).
			int o = FrameProtocol.FrameHeaderSize;
			var pixels = _pixels;
			var packet = _packet;
			for (int i = 0; i < pixels.Length; i++) {
				Color c = pixels[i];
				packet[o++] = c.B;
				packet[o++] = c.G;
				packet[o++] = c.R;
				packet[o++] = c.A;
			}

			_server.Publish(packet, FrameProtocol.FrameHeaderSize + pixelBytes);
		}

		private void SendEmpty(Player player)
		{
			EnsurePacket(FrameProtocol.FrameHeaderSize);
			WriteHeader(FrameProtocol.FrameFlags.None, 0, 0, player?.direction ?? 1, Vector2.Zero, 0);
			_server.Publish(_packet, FrameProtocol.FrameHeaderSize);
		}

		private void EnsurePacket(int size)
		{
			if (_packet == null || _packet.Length < size)
				_packet = new byte[size];
		}

		private void WriteHeader(FrameProtocol.FrameFlags flags, int width, int height, int direction, Vector2 velocity, int payloadLength)
		{
			var span = _packet.AsSpan();
			BitConverter.TryWriteBytes(span[0..], FrameProtocol.FrameMagic);
			BitConverter.TryWriteBytes(span[4..], FrameProtocol.Version);
			BitConverter.TryWriteBytes(span[6..], (ushort)flags);
			BitConverter.TryWriteBytes(span[8..], width);
			BitConverter.TryWriteBytes(span[12..], height);
			BitConverter.TryWriteBytes(span[16..], FeetRow);
			BitConverter.TryWriteBytes(span[20..], direction);
			BitConverter.TryWriteBytes(span[24..], velocity.X);
			BitConverter.TryWriteBytes(span[28..], velocity.Y);
			BitConverter.TryWriteBytes(span[32..], payloadLength);
			BitConverter.TryWriteBytes(span[36..], ++_frameNumber);
		}
	}
}
