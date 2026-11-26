namespace DesktopOverlay;

/// <summary>Wire format shared with the mod (TerrariaDesktop/Common/FrameProtocol.cs). See PROTOCOL.md.</summary>
internal static class FrameProtocol
{
    public const int DefaultPort = 47615;
    public const ushort Version = 1;

    public const uint FrameMagic = 0x52464454;   // "TDFR"
    public const int FrameHeaderSize = 40;

    public const uint ControlMagic = 0x54434454; // "TDCT"
    public const int ControlSize = 20;

    /// <summary>Largest picture we accept, so a bad packet can't make us allocate gigabytes.</summary>
    public const int MaxDimension = 1024;

    [Flags]
    public enum FrameFlags : ushort
    {
        None = 0,
        InWorld = 1 << 0,
        OnGround = 1 << 1,
        WalkOverride = 1 << 2,
        Mounted = 1 << 3,
    }
}

internal enum OverlayMode
{
    FollowGame = 0,
    Wander = 1,
}

/// <summary>One picture of the character plus what the player was doing at that moment.</summary>
internal sealed class Frame
{
    public FrameProtocol.FrameFlags Flags;
    public int Width;
    public int Height;
    public int FeetRow;
    public int Direction;
    public float VelocityX;
    public float VelocityY;
    public uint Number;
    /// <summary>Premultiplied BGRA, top row first.</summary>
    public byte[] Pixels = Array.Empty<byte>();

    public bool InWorld => (Flags & FrameProtocol.FrameFlags.InWorld) != 0 && Width > 0 && Height > 0;
    public bool OnGround => (Flags & FrameProtocol.FrameFlags.OnGround) != 0;
}
