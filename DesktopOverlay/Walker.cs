namespace DesktopOverlay;

/// <summary>
/// Decides where the character is on the desktop. All distances are in screen pixels;
/// the game runs 60 ticks a second and reports speed in game pixels per tick.
/// </summary>
internal sealed class Walker
{
    /// <summary>Walking speed in game pixels per tick. The game's default run speed is 3.</summary>
    public const float WanderSpeed = 2f;

    private readonly Random _random = new();
    private double _stateTimeLeft = 1.5;

    public double X;          // centre of the character
    public double JumpOffset; // 0 on the taskbar, negative while in the air (Follow mode)
    public int Direction = 1;
    public bool Walking;

    public void Update(OverlayMode mode, Frame? frame, Rectangle area, int scale, double dt, int characterWidth)
    {
        if (X == 0)
            X = area.Left + area.Width * 0.25;

        if (mode == OverlayMode.Wander)
            Wander(area, scale, dt, characterWidth);
        else
            Follow(frame, area, scale, dt, characterWidth);
    }

    private void Wander(Rectangle area, int scale, double dt, int characterWidth)
    {
        JumpOffset = 0;
        _stateTimeLeft -= dt;
        if (_stateTimeLeft <= 0)
        {
            Walking = !Walking;
            if (Walking)
            {
                Direction = _random.Next(2) == 0 ? -1 : 1;
                _stateTimeLeft = 3 + _random.NextDouble() * 7;
            }
            else
            {
                _stateTimeLeft = 2 + _random.NextDouble() * 5;
            }
        }

        // Keep the whole sprite on screen; turn around at the edges.
        double margin = characterWidth * 0.25;
        double left = area.Left + margin;
        double right = area.Right - margin;
        if (X <= left) { X = left; Direction = 1; }
        if (X >= right) { X = right; Direction = -1; }

        if (Walking)
            X += Direction * WanderSpeed * 60 * scale * dt;
    }

    private void Follow(Frame? frame, Rectangle area, int scale, double dt, int characterWidth)
    {
        Walking = false;
        if (frame == null || !frame.InWorld)
            return;

        Direction = frame.Direction;
        X += frame.VelocityX * 60 * scale * dt;

        // Walk off one side of the screen, come back on the other.
        double half = characterWidth * 0.25;
        if (X > area.Right + half) X = area.Left - half;
        if (X < area.Left - half) X = area.Right + half;

        if (frame.OnGround)
            JumpOffset = 0;
        else
            JumpOffset = Math.Clamp(JumpOffset + frame.VelocityY * 60 * scale * dt, -area.Height * 0.6, 0);
    }
}
