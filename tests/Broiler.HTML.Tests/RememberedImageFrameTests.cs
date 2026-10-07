using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;

namespace Broiler.HTML.Tests;

/// <summary>
/// A frame decoded from a response body the subresource cache keeps is copied for the next load of the
/// same body rather than decoded again (<see cref="BBitmap.TryDecodeRememberedFrameAt"/>).
/// </summary>
/// <remarks>
/// The window parses a page again whenever its scripts change it, and each parse decoded every image of
/// the page again on the window's thread, though the cache had kept the bytes. reCAPTCHA's image challenge
/// shows one picture in each of its tiles, and a click on a tile spent a quarter to half a second decoding
/// it again.
/// </remarks>
public sealed class RememberedImageFrameTests
{
    private static readonly BColor Red = BColor.FromArgb(255, 220, 30, 30);
    private static readonly BColor Blue = BColor.FromArgb(255, 30, 30, 220);

    /// <summary>
    /// The second load of a body is not decoded: with the body's pixel data spoilt in between, it still gives
    /// the picture, where decoding the spoilt body gives none.
    /// </summary>
    [Fact]
    public void A_Body_Decoded_Before_Is_Not_Decoded_Again()
    {
        var body = Encode(Red, 40, 30);
        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.Zero, out var first));
        using (first)
            Assert.Equal(Red, first.GetPixel(20, 15));

        Array.Clear(body, 33, body.Length - 33);

        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.Zero, out var second));
        using (second)
        {
            Assert.Equal((40, 30), (second.Width, second.Height));
            Assert.Equal(Red, second.GetPixel(20, 15));
        }

        Assert.False(TryDecode(body));
    }

    /// <summary>Each load gets its own copy, which it may change and dispose.</summary>
    [Fact]
    public void Each_Load_Gets_Its_Own_Copy()
    {
        var body = Encode(Red, 8, 8);
        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.Zero, out var first));
        first.SetPixel(1, 1, Blue);
        first.Dispose();

        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.Zero, out var second));
        using (second)
            Assert.Equal(Red, second.GetPixel(1, 1));
    }

    /// <summary>
    /// An animated image's first frame, kept from a load at the start of its timeline, does not answer a load
    /// further along it: 150ms into a red frame of 100ms and a blue one, the image is blue.
    /// </summary>
    [Fact]
    public void A_Kept_First_Frame_Does_Not_Answer_A_Later_Time()
    {
        var body = EncodeAnimation(Red, Blue);

        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.Zero, out var start));
        using (start)
            Assert.Equal(Red, start.GetPixel(2, 2));

        Assert.True(BBitmap.TryDecodeRememberedFrameAt(body, TimeSpan.FromMilliseconds(150), out var later));
        using (later)
            Assert.Equal(Blue, later.GetPixel(2, 2));
    }

    private static bool TryDecode(byte[] body)
    {
        try
        {
            if (!BBitmap.TryDecodeFrameAt(body, TimeSpan.Zero, out var bitmap))
                return false;

            bitmap.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException or FormatException)
        {
            return false;
        }
    }

    private static byte[] Encode(BColor color, int width, int height)
    {
        using var bitmap = new BBitmap(width, height);
        bitmap.Clear(color);
        return bitmap.Encode(ImageEncodeFormat.Png);
    }

    private static byte[] EncodeAnimation(BColor first, BColor second)
    {
        var frames = new[] { Frame(first), Frame(second) };
        using var output = new MemoryStream();
        new GifImageCodec().EncodeAsync(new ImageSequence(frames, 4, 4, 0), output, new ImageEncodeOptions(ImageEncodeFormat.Gif, 100))
            .AsTask().GetAwaiter().GetResult();
        return output.ToArray();

        static ImageFrame Frame(BColor color)
        {
            var rgba = new byte[4 * 4 * 4];
            for (var index = 0; index < rgba.Length; index += 4)
            {
                rgba[index] = color.R;
                rgba[index + 1] = color.G;
                rgba[index + 2] = color.B;
                rgba[index + 3] = color.A;
            }

            return new ImageFrame(new ImageBuffer(4, 4, rgba), TimeSpan.FromMilliseconds(100));
        }
    }
}
