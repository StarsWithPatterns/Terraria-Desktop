namespace TerrariaDesktop.Common
{
	/// <summary>
	/// Wire format shared with the DesktopOverlay app (DesktopOverlay/FrameProtocol.cs).
	/// Everything is little-endian. See PROTOCOL.md in the repository root.
	/// </summary>
	public static class FrameProtocol
	{
		public const int DefaultPort = 47615;
		public const ushort Version = 1;

		// Mod -> overlay: "TDFR"
		public const uint FrameMagic = 0x52464454;
		public const int FrameHeaderSize = 40;

		// Overlay -> mod: "TDCT"
		public const uint ControlMagic = 0x54434454;
		public const int ControlSize = 20;

		[System.Flags]
		public enum FrameFlags : ushort
		{
			None = 0,
			InWorld = 1 << 0,
			OnGround = 1 << 1,
			WalkOverride = 1 << 2,
			Mounted = 1 << 3,
		}

		public enum OverlayMode
		{
			/// <summary>The desktop character copies what your in-game character does.</summary>
			FollowGame = 0,
			/// <summary>The overlay decides where the character walks; the mod animates the legs to match.</summary>
			Wander = 1,
		}
	}
}
