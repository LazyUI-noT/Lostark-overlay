namespace GaugeOverlay.Core.Domain;

/// <summary>A BGRA8 pixel buffer (4 bytes per pixel, rows packed with <see cref="Stride"/>).
/// Capture fills these; vision reads them. No decoding or platform types involved.</summary>
public sealed class Image(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride => Width * 4;
    public byte[] Bgra { get; } = bgra;

    public Image(int width, int height) : this(width, height, new byte[width * height * 4]) { }

    public ReadOnlySpan<byte> Row(int y) => Bgra.AsSpan(y * Stride, Width * 4);

    public (byte B, byte G, byte R) Pixel(int x, int y)
    {
        int i = y * Stride + x * 4;
        return (Bgra[i], Bgra[i + 1], Bgra[i + 2]);
    }

    public Image Copy() => new(Width, Height, (byte[])Bgra.Clone());

    /// <summary>A copy of <paramref name="r"/>, clipped to the image (empty when it lies outside).</summary>
    public Image Crop(PixelRect r)
    {
        int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
        int w = Math.Max(0, Math.Min(Width, r.X + r.Width) - x0), h = Math.Max(0, Math.Min(Height, r.Y + r.Height) - y0);
        var dst = new Image(w, h);
        for (int y = 0; y < h; y++) Row(y0 + y).Slice(x0 * 4, w * 4).CopyTo(dst.Bgra.AsSpan(y * dst.Stride));
        return dst;
    }

    /// <summary>The one stored form of an image (calibration files, learned emblems): width, height, packed rows.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[8 + Width * Height * 4];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), Width);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), Height);
        for (int y = 0; y < Height; y++) Row(y).CopyTo(bytes.AsSpan(8 + y * Width * 4));
        return bytes;
    }

    /// <summary>Reads <see cref="ToBytes"/> output; null if the data is truncated or malformed.</summary>
    public static Image? FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8) return null;
        int w = BitConverter.ToInt32(bytes), h = BitConverter.ToInt32(bytes[4..]);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || bytes.Length != 8 + w * h * 4) return null;
        return new Image(w, h, bytes[8..].ToArray());
    }
}
