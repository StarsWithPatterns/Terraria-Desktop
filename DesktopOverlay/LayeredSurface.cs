using System.Runtime.InteropServices;

namespace DesktopOverlay;

/// <summary>
/// A premultiplied 32-bit bitmap that we hand to UpdateLayeredWindow. This is the
/// Windows way to get a window with real per-pixel transparency.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    private IntPtr _memDc;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private byte[] _scratch = Array.Empty<byte>();

    public int Width { get; private set; }
    public int Height { get; private set; }

    public void EnsureSize(int width, int height)
    {
        if (width == Width && height == Height && _bitmap != IntPtr.Zero)
            return;

        Release();
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        var screenDc = Native.GetDC(IntPtr.Zero);
        try
        {
            _memDc = Native.CreateCompatibleDC(screenDc);
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = Width,
                biHeight = -Height, // negative = top row first, same as the mod sends
                biPlanes = 1,
                biBitCount = 32,
            };
            _bitmap = Native.CreateDIBSection(screenDc, ref header, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed.");
            _oldBitmap = Native.SelectObject(_memDc, _bitmap);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        _scratch = new byte[Width * Height * 4];
        Clear();
    }

    public void Clear()
    {
        Array.Clear(_scratch);
        Native.GdiFlush();
        Marshal.Copy(_scratch, 0, _bits, _scratch.Length);
    }

    /// <summary>Copies a frame in, enlarged by a whole number so the pixel art stays crisp.</summary>
    public void Draw(Frame frame, int scale)
    {
        EnsureSize(frame.Width * scale, frame.Height * scale);

        var src = frame.Pixels;
        var dst = _scratch;
        int srcStride = frame.Width * 4;
        int dstStride = Width * 4;

        for (int y = 0; y < frame.Height; y++)
        {
            int srcRow = y * srcStride;
            int dstRow = y * scale * dstStride;

            // Widen one source row into the first destination row...
            int d = dstRow;
            for (int x = 0; x < frame.Width; x++)
            {
                int s = srcRow + x * 4;
                for (int k = 0; k < scale; k++)
                {
                    dst[d] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    dst[d + 3] = src[s + 3];
                    d += 4;
                }
            }

            // ...then repeat that row (scale - 1) more times.
            for (int k = 1; k < scale; k++)
                Buffer.BlockCopy(dst, dstRow, dst, dstRow + k * dstStride, dstStride);
        }

        Native.GdiFlush();
        Marshal.Copy(dst, 0, _bits, dst.Length);
    }

    /// <summary>Shows the bitmap in the window at the given screen position.</summary>
    public void Present(IntPtr hwnd, int screenX, int screenY)
    {
        if (_bitmap == IntPtr.Zero)
            return;

        var dst = new Native.POINT(screenX, screenY);
        var size = new Native.SIZE(Width, Height);
        var src = new Native.POINT(0, 0);
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            SourceConstantAlpha = 255,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };
        Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
    }

    private void Release()
    {
        if (_memDc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
                Native.SelectObject(_memDc, _oldBitmap);
            Native.DeleteDC(_memDc);
        }
        if (_bitmap != IntPtr.Zero)
            Native.DeleteObject(_bitmap);
        _memDc = _bitmap = _oldBitmap = _bits = IntPtr.Zero;
        Width = Height = 0;
    }

    public void Dispose() => Release();
}
