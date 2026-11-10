using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GaugeOverlay.App.UI;

/// <summary>One recorded GIF played by frame number. WPF plays no GIF, and keeping every frame whole costs about 20 MB for a
/// 36 s loop, so only each frame's changed pixels are kept and laid over one picture as it plays (<see cref="Show"/>).
/// ponytail: the GIF disposal method is ignored (each frame is laid over the last), which is what tools/make-map-gifs.ps1
/// writes; a GIF that restores to background would keep old pixels. The styles of one window wrap by their own length, so
/// they stay in step only while the recorder gives them the same frame count.</summary>
sealed class GifTile
{
    readonly (int Left, int Top, int Width, int Height, byte[] Pixels)[] _changes;
    readonly byte[] _screen;
    readonly int _stride;
    int _shown = -1;

    public WriteableBitmap Picture { get; }
    public int Frames => _changes.Length;

    public GifTile(Stream gif)
    {
        var decoder = new GifBitmapDecoder(gif, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
        _stride = width * 4;
        _screen = new byte[_stride * height];
        _changes = [.. decoder.Frames.Select(frame =>
        {
            var meta = (BitmapMetadata)frame.Metadata;
            int w = frame.PixelWidth, h = frame.PixelHeight;
            int left = Convert.ToInt32(meta.GetQuery("/imgdesc/Left")), top = Convert.ToInt32(meta.GetQuery("/imgdesc/Top"));
            // Checked here, where MapPreview catches it, so a bad file drops its tile instead of throwing on hover.
            if (left < 0 || top < 0 || left + w > width || top + h > height) throw new FileFormatException("GIF frame outside the picture");
            var pixels = new byte[w * h * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, w * 4, 0);
            return (left, top, w, h, pixels);
        })];
        Picture = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        Show(0);
    }

    /// <summary>Frame <paramref name="frame"/> (wrapping): the changes since the one shown, or from the first (a whole picture)
    /// when it went back.</summary>
    public void Show(int frame)
    {
        frame %= Frames;
        if (frame == _shown) return;
        int from = frame > _shown ? _shown + 1 : 0;
        if (from == 0) Array.Clear(_screen);
        for (int f = from; f <= frame; f++) Lay(_changes[f]);
        _shown = frame;
        Picture.WritePixels(new System.Windows.Int32Rect(0, 0, Picture.PixelWidth, Picture.PixelHeight), _screen, _stride, 0);
    }

    /// <summary>A frame's changed pixels over the picture; its clear pixels keep what was there.</summary>
    void Lay((int Left, int Top, int Width, int Height, byte[] Pixels) c)
    {
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
            {
                int from = (y * c.Width + x) * 4, to = (c.Top + y) * _stride + (c.Left + x) * 4;
                if (c.Pixels[from + 3] != 0) Array.Copy(c.Pixels, from, _screen, to, 4);
            }
    }
}
