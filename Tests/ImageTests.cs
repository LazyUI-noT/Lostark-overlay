using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class ImageTests
{
    [Fact]
    public void Bytes_round_trip_and_reject_truncated_data()
    {
        var img = new Image(3, 2);
        for (int i = 0; i < img.Bgra.Length; i++) img.Bgra[i] = (byte)i;
        var bytes = img.ToBytes();

        var back = Image.FromBytes(bytes)!;
        Assert.Equal((3, 2), (back.Width, back.Height));
        Assert.Equal(img.Bgra, back.Bgra);

        Assert.Null(Image.FromBytes(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Null(Image.FromBytes([1, 0, 0]));
    }
}
