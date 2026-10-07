using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A fieldset as HTML's rendering section and Chromium draw it: a 2px groove with 0.35em, 0.75em
/// and 0.625em of padding and 2px of inline margin, no block margins on it or on a form, and the
/// block-start border running through the middle of the rendered legend, stopped behind it.
/// </summary>
/// <remarks>
/// The default sheet had CSS 2.1's 1em block margins on both and nothing else, so a fieldset had no
/// border at all, and its legend read as one more line of text: reCAPTCHA's demo form showed no
/// frame. Measured in Chromium.
/// </remarks>
public sealed class FieldsetPaintTests
{
    private const string PageUrl = "https://example.test/page";

    private const string Form =
        "<p style=\"margin: 0\">Before</p>" +
        "<form><fieldset><legend>An example form</legend><div>Content</div></fieldset></form>";

    private static (Fragment Tree, List<DisplayItem> Items) Paint(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet(
            "<!DOCTYPE html><html><body style=\"font-family: sans-serif\">" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        var items = container.CreateDisplayList().Items.ToList();
        return (container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree."), items);
    }

    private static Fragment Find(Fragment tree, string tagName) =>
        FindOrNull(tree, tagName) ?? throw new InvalidOperationException($"No <{tagName}>.");

    private static Fragment? FindOrNull(Fragment fragment, string tagName) =>
        string.Equals(fragment.Style.TagName, tagName, StringComparison.OrdinalIgnoreCase)
            ? fragment
            : fragment.Children.Select(child => FindOrNull(child, tagName)).FirstOrDefault(found => found is not null);

    /// <summary>The fieldset's border, padding and margins are the HTML Standard's; the form has no margin.</summary>
    [Fact]
    public void A_Fieldset_Has_A_Groove_And_A_Form_No_Margin()
    {
        var (tree, _) = Paint(Form);

        var fieldset = Find(tree, "fieldset");
        Assert.Equal("groove", fieldset.Style.BorderTopStyle);
        Assert.Equal(2, fieldset.Border.Top, 1);
        Assert.Equal(2, fieldset.Border.Left, 1);
        Assert.Equal(5.6, fieldset.Padding.Top, 1);
        Assert.Equal(12, fieldset.Padding.Left, 1);
        Assert.Equal(10, fieldset.Padding.Bottom, 1);
        Assert.Equal(2, fieldset.Margin.Left, 1);
        Assert.Equal(0, fieldset.Margin.Top, 1);
        Assert.Equal(0, Find(tree, "form").Margin.Top, 1);
        Assert.Equal(2, Find(tree, "legend").Padding.Left, 1);
    }

    /// <summary>
    /// The block-start border's band is centred on the legend, and it is drawn left of the legend,
    /// right of it, and below the band under it: nowhere behind the legend.
    /// </summary>
    [Fact]
    public void The_Border_Runs_Through_The_Legend_And_Stops_Behind_It()
    {
        var (tree, items) = Paint(Form);

        var fieldset = Find(tree, "fieldset");
        var legend = Find(tree, "legend").Bounds;
        float legendMiddle = legend.Top + (legend.Height / 2);

        var outerRing = items.OfType<DrawBorderItem>()
            .Where(item => Math.Abs(item.Bounds.Width - fieldset.Bounds.Width) < 0.5f)
            .ToList();
        Assert.NotEmpty(outerRing);
        Assert.All(outerRing, item => Assert.Equal(legendMiddle - 1, item.Bounds.Top, 1));

        var clips = items.OfType<ClipItem>().Select(static item => item.ClipRect).ToList();
        Assert.Contains(clips, clip => Math.Abs(clip.Right - legend.Left) < 0.5f);
        Assert.Contains(clips, clip => Math.Abs(clip.Left - legend.Right) < 0.5f);
        Assert.DoesNotContain(clips, clip =>
            clip.Left < legend.Right - 0.5f && clip.Right > legend.Left + 0.5f && clip.Top < legendMiddle + 1);
    }

    /// <summary>A fieldset without a legend draws its border whole, along the top of its box.</summary>
    [Fact]
    public void Without_A_Legend_The_Border_Is_Whole()
    {
        var (tree, items) = Paint("<fieldset><div>Content</div></fieldset>");

        var fieldset = Find(tree, "fieldset");
        Assert.Empty(items.OfType<ClipItem>().Where(item => item.ClipRect.Top >= fieldset.Bounds.Top - 0.5f
            && item.ClipRect.Bottom <= fieldset.Bounds.Bottom + 0.5f && item.ClipRect.Width < fieldset.Bounds.Width - 1));
        Assert.Contains(items.OfType<DrawBorderItem>(), item => Math.Abs(item.Bounds.Top - fieldset.Bounds.Top) < 0.5f);
    }
}
