using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using DegrandeScreenShot.App.Services;

namespace DegrandeScreenShot.Tests;

public class ScrollingCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(91)]
    public void ChoosesTheSameShiftAndScoresAsExhaustiveRowComparison(int shift)
    {
        const int height = 144;
        var random = new Random(177);
        var source = Enumerable.Range(0, height + shift)
            .Select(_ =>
            {
                var row = new byte[64];
                random.NextBytes(row);
                return row;
            })
            .ToArray();
        var first = source[..height];
        var next = source[shift..].Select(row => (byte[])row.Clone()).ToArray();
        for (var row = 0; row < height; row += 11)
        {
            random.NextBytes(next[row]);
        }

        double Score(int candidate)
        {
            long total = 0;
            for (var row = 0; row < height - candidate; row++)
            {
                for (var bucket = 0; bucket < 64; bucket++)
                {
                    total += Math.Abs(first[candidate + row][bucket] - next[row][bucket]);
                }
            }
            return (double)total / (height - candidate);
        }

        var actualShift = ScreenCaptureService.FindBestVerticalShift(first, next, out var best, out var baseline);
        Assert.Equal(shift, actualShift);
        Assert.Equal(Score(0), baseline);
        Assert.Equal(Score(shift), best);
    }

    [Fact]
    public void AssemblesEveryStripPixelInCaptureOrder()
    {
        using var first = new Bitmap(3, 4, PixelFormat.Format32bppRgb);
        using var second = new Bitmap(3, 1, PixelFormat.Format32bppRgb);
        using var third = new Bitmap(3, 5, PixelFormat.Format32bppRgb);
        Bitmap[] strips = [first, second, third];
        var expected = new List<Color>();
        foreach (var strip in strips)
        {
            for (var y = 0; y < strip.Height; y++)
            {
                for (var x = 0; x < strip.Width; x++)
                {
                    var color = Color.FromArgb(expected.Count * 3, x * 33, y * 17);
                    expected.Add(color);
                    strip.SetPixel(x, y, color);
                }
            }
        }

        var result = ScreenCaptureService.CombineStrips(strips);
        Assert.True(result.IsFrozen);
        Assert.Equal(3, result.PixelWidth);
        Assert.Equal(10, result.PixelHeight);
        var pixels = new byte[result.PixelWidth * result.PixelHeight * 4];
        result.CopyPixels(pixels, result.PixelWidth * 4, 0);
        for (var index = 0; index < expected.Count; index++)
        {
            var offset = index * 4;
            Assert.Equal(expected[index], Color.FromArgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]));
        }
    }

    [Fact]
    public void PreservesTheBottomOfVeryTallCaptures()
    {
        using var strip = new Bitmap(2, 1200, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.Clear(Color.CornflowerBlue);
        }

        var result = ScreenCaptureService.CombineStrips(Enumerable.Repeat(strip, 40).ToArray());
        var bottomPixel = new byte[4];
        Assert.Equal(48000, result.PixelHeight);
        result.CopyPixels(new Int32Rect(1, result.PixelHeight - 1, 1, 1), bottomPixel, 4, 0);
        Assert.Equal(Color.CornflowerBlue.R, bottomPixel[2]);
        Assert.Equal(Color.CornflowerBlue.G, bottomPixel[1]);
        Assert.Equal(Color.CornflowerBlue.B, bottomPixel[0]);
    }
}
