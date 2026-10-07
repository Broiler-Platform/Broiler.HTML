using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A <c>::before</c>/<c>::after</c> box's generated text is its content, held the way an element's
/// text is, in an anonymous inline box inside it (CSS Generated Content 3 §2), so the box sizes
/// round its text and paints its own background whether it is in flow, floated or absolutely
/// positioned.
/// </summary>
/// <remarks>
/// The text was set on the pseudo-element's box itself. Layout reaches a positioned or floated
/// box's content only through its children, so the box came out 0×0 and its words were laid out
/// in the parent's line instead: Acid3's <c>map::after</c>, an absolutely positioned fuchsia "X"
/// that covers a red square at the body's top right, was a white X in normal flow and the red
/// square showed.
/// </remarks>
public sealed class PseudoElementTextTests
{
    private const string PageUrl = "https://example.test/page";

    private static readonly BColor Fuchsia = BColor.FromArgb(255, 255, 0, 255);

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

    private static IEnumerable<Fragment> Flatten(Fragment fragment) =>
        new[] { fragment }.Concat(fragment.Children.SelectMany(Flatten));

    private static DrawTextItem Word(List<DisplayItem> items, string text) =>
        items.OfType<DrawTextItem>().Single(item => item.Text == text);

    /// <summary>
    /// An absolutely positioned <c>::after</c> with text sits at its offsets, as wide as its text
    /// and a line tall, its background filling that box and its text drawn inside it, not in the
    /// element's line.
    /// </summary>
    [Fact]
    public void An_Absolutely_Positioned_After_Is_Laid_Out_At_Its_Offsets_Round_Its_Text()
    {
        var (tree, items) = Paint(
            "<style>#host::after { position: absolute; top: 18px; left: 138px; content: \"X\";" +
            " background: fuchsia; color: white; font: 20px/1 sans-serif }</style>" +
            "<div id=\"host\">Text</div>");

        var after = Assert.Single(Flatten(tree), static fragment => fragment.Style.Position == "absolute");
        Assert.Equal(138, after.Bounds.Left, 1);
        Assert.Equal(18, after.Bounds.Top, 1);
        Assert.Equal(20, after.Bounds.Height, 1);
        Assert.InRange(after.Bounds.Width, 8, 20);

        var fill = Assert.Single(items.OfType<FillRectItem>(), static fill => fill.Color == Fuchsia);
        Assert.Equal(after.Bounds, fill.Bounds);

        var xs = items.OfType<DrawTextItem>().Where(static item => item.Text == "X").ToList();
        Assert.NotEmpty(xs);
        Assert.All(xs, x =>
        {
            Assert.Equal(BColor.FromArgb(255, 255, 255, 255), x.Color);
            Assert.InRange(x.Bounds.Left, after.Bounds.Left - 0.5f, after.Bounds.Right);
            Assert.InRange(x.Bounds.Top, after.Bounds.Top - 2.5f, after.Bounds.Bottom);
        });
    }

    /// <summary>A floated <c>::after</c> with text paints its background round its text at the right.</summary>
    [Fact]
    public void A_Floated_After_With_Text_Paints()
    {
        var (_, items) = Paint(
            "<style>#host::after { float: right; content: \"Floated\"; background: fuchsia }</style>" +
            "<div id=\"host\" style=\"width: 400px\">Text</div>");

        var fill = Assert.Single(items.OfType<FillRectItem>(), static fill => fill.Color == Fuchsia);
        Assert.True(fill.Bounds.Width > 20 && fill.Bounds.Height > 10, $"The float's background is {fill.Bounds}.");
        Assert.Equal(408, fill.Bounds.Right, 1);

        var floated = Word(items, "Floated");
        Assert.True(fill.Bounds.Contains(floated.Bounds.Left + 1, floated.Bounds.Top + 1), $"'Floated' at {floated.Bounds}, its box at {fill.Bounds}.");
        Assert.True(Word(items, "Text").Bounds.Right <= fill.Bounds.Left);
    }

    /// <summary>
    /// Inline <c>::before</c> and <c>::after</c> between words are laid out and painted exactly as
    /// the same text in inline elements is: in the element's line, with its spaces, its
    /// <c>white-space</c>, its <c>text-transform</c>, its own decoration and its background.
    /// </summary>
    [Fact]
    public void Inline_Before_And_After_Render_Where_The_Same_Inline_Elements_Do()
    {
        const string Rules =
            ".m::before { content: \"[ \"; color: red; background: yellow }" +
            ".m::after { content: \"  end  of\"; white-space: pre; text-transform: uppercase;" +
            " text-decoration: underline }";
        var (_, generated) = Paint(
            "<style>" + Rules + "</style>" +
            "<p>One <span class=\"m\">two words</span> three</p>");
        var (_, elements) = Paint(
            "<p>One <span><span style=\"color: red; background: yellow\">[ </span>two words" +
            "<span style=\"white-space: pre; text-transform: uppercase; text-decoration: underline\">  end  of</span>" +
            "</span> three</p>");

        static string Describe(List<DisplayItem> items) => string.Join(
            "\n",
            items.Where(static item => item is DrawTextItem or FillRectItem or DrawLineItem)
                .Select(static item => item switch
                {
                    DrawTextItem text => $"text '{text.Text}' {text.Bounds} {text.Color}",
                    FillRectItem fill => $"fill {fill.Bounds} {fill.Color}",
                    DrawLineItem line => $"line {line.Start}-{line.End} {line.Color}",
                    _ => string.Empty,
                }));

        Assert.Equal(Describe(elements), Describe(generated));
        Assert.Equal(Word(generated, "One").Bounds.Top, Word(generated, "[").Bounds.Top);
        Assert.True(Word(generated, "[").Bounds.Left > Word(generated, "One").Bounds.Right);
        Assert.True(Word(generated, "END").Bounds.Left > Word(generated, "words").Bounds.Right);
        Assert.Single(generated.OfType<DrawLineItem>());
    }
}
