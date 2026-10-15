using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A <c>&lt;progress&gt;</c> or <c>&lt;meter&gt;</c> fills its track by the ratio the shared
/// <c>HtmlElementQueries.ResolveProgressLikeValueRatio</c> reads from the element: <c>value</c> against
/// <c>max</c> (and <c>min</c> for a meter), with a <c>max</c> at or below <c>min</c> taken as <c>min + 1</c>
/// and the result clamped to the track.
/// </summary>
/// <remarks>
/// The renderer carried a copy of that calculation over its box attributes. Both copies let a non-finite
/// number through, so <c>value="NaN"</c> gave a NaN fill; the fill here must always be a length within the
/// track, whatever the attributes say.
/// </remarks>
public sealed class ProgressMeterFillTests
{
    private const float Track = 200;

    [Theory]
    [InlineData("<progress value=\"0.5\"></progress>", 0.5)]
    [InlineData("<progress value=\"3\" max=\"4\"></progress>", 0.75)]
    [InlineData("<progress value=\"9\" max=\"4\"></progress>", 1)]
    [InlineData("<progress value=\"-1\"></progress>", 0)]
    [InlineData("<progress></progress>", 0)]
    [InlineData("<meter value=\"5\" min=\"0\" max=\"10\"></meter>", 0.5)]
    [InlineData("<meter value=\"15\" min=\"10\" max=\"20\"></meter>", 0.5)]
    // A max at or below min is min + 1.
    [InlineData("<meter value=\"10.25\" min=\"10\" max=\"5\"></meter>", 0.25)]
    // A malformed value takes the default: min for a meter.
    [InlineData("<meter value=\"abc\" min=\"0\" max=\"10\"></meter>", 0)]
    public void The_Fill_Covers_The_Ratio_Of_The_Track(string control, double ratio)
    {
        var (track, fill) = Measure(control);
        // The fill's containing block is the track's padding box (CSS2.1 §10.1).
        var paddingBox = track.Size.Width - track.Border.Left - track.Border.Right;

        Assert.Equal(Track, track.Size.Width, 0.5);
        Assert.Equal((float)(ratio * paddingBox), fill.Size.Width, 0.5);
    }

    [Theory]
    [InlineData("<meter value=\"NaN\"></meter>")]
    [InlineData("<meter value=\"0.5\" min=\"NaN\" max=\"Infinity\"></meter>")]
    [InlineData("<progress value=\"Infinity\"></progress>")]
    public void A_Non_Finite_Attribute_Never_Gives_A_Fill_Outside_The_Track(string control)
    {
        var (_, fill) = Measure(control);

        Assert.True(float.IsFinite(fill.Size.Width), $"The fill is {fill.Size.Width}px wide.");
        Assert.InRange(fill.Size.Width, 0, Track);
    }

    private static (Fragment Track, Fragment Fill) Measure(string control)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet(
            "<!DOCTYPE html><html><head><style>progress, meter { width: 200px; }</style></head>"
            + "<body style=\"margin: 0\">" + control + "</body></html>",
            null,
            "about:blank");
        container.PerformLayout();
        var tree = container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");

        var track = Flatten(tree).First(static f => f.Style.TagName is "progress" or "meter");
        // The track's own fallback content is display:none; the fill is the box the renderer adds.
        var fill = Assert.Single(track.Children, static f => f.Style.Position == "absolute");
        return (track, fill);
    }

    private static IEnumerable<Fragment> Flatten(Fragment fragment)
    {
        yield return fragment;
        foreach (var child in fragment.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }
}
