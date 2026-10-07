using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// Where text decorations are drawn: a decorated inline box's lines run beneath its own words and
/// the spaces between them, on each line it is on, in the colour of the box that asked for them,
/// as Chromium draws them (CSS Text Decoration 3 §2.1).
/// </summary>
/// <remarks>
/// The box tree hands a decoration down to the boxes that hold the words, and paint read only the
/// block's own decoration or its first child's, which it drew across the whole line. A link's
/// decoration sits one box further down, under the <c>&lt;a&gt;</c>, so no link, no
/// <c>&lt;u&gt;</c> and no <c>&lt;s&gt;</c> showed a line at all.
/// </remarks>
public sealed class TextDecorationPaintTests
{
    private const string PageUrl = "https://example.test/page";

    private static List<DisplayItem> Paint(string body)
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
        return [.. container.CreateDisplayList().Items];
    }

    private static DrawTextItem Word(List<DisplayItem> items, string text) =>
        items.OfType<DrawTextItem>().Single(item => item.Text == text);

    /// <summary>
    /// A link's underline starts at its first word and ends at its last, across the space between
    /// them and none of the line's other words, below the words' baseline, in the link's colour.
    /// </summary>
    [Fact]
    public void A_Link_Is_Underlined_Beneath_Its_Words_Alone()
    {
        var items = Paint("<p>One <a href=\"/x\">two words</a> end.</p>");

        var line = Assert.Single(items.OfType<DrawLineItem>());
        var first = Word(items, "two");
        var last = Word(items, "words");
        Assert.Equal(first.Bounds.Left, line.Start.X, 1);
        Assert.Equal(last.Bounds.Right, line.End.X, 1);
        Assert.Equal(line.Start.Y, line.End.Y);
        Assert.InRange(line.Start.Y, first.Bounds.Top + (first.Bounds.Height * 0.7f), first.Bounds.Bottom);
        Assert.Equal(first.Color, line.Color);
    }

    /// <summary>A link that wraps is underlined on each of its lines.</summary>
    [Fact]
    public void A_Wrapped_Link_Is_Underlined_On_Each_Line()
    {
        var items = Paint(
            "<p style=\"width: 120px\"><a href=\"/x\">this link is long enough to wrap</a></p>");

        var lines = items.OfType<DrawLineItem>().ToList();
        Assert.True(lines.Count >= 2, $"{lines.Count} underline(s) for a link on several lines.");
        Assert.Equal(lines.Count, lines.Select(static line => line.Start.Y).Distinct().Count());
    }

    /// <summary>
    /// A block's underline is beneath its text, not across the rest of the line, and
    /// <c>&lt;u&gt;</c>, <c>&lt;s&gt;</c> and an overline are each drawn where their words are.
    /// </summary>
    [Fact]
    public void Each_Decorated_Box_Is_Decorated_Where_Its_Words_Are()
    {
        var items = Paint(
            "<p style=\"text-decoration: underline\">Short</p>" +
            "<p><u>under</u> <s>struck</s> <span style=\"text-decoration: overline\">over</span></p>");

        var lines = items.OfType<DrawLineItem>().ToList();
        Assert.Equal(4, lines.Count);

        var shortWord = Word(items, "Short");
        Assert.Equal(shortWord.Bounds.Right, LineUnder(lines, shortWord).End.X, 1);

        var under = Word(items, "under");
        var struck = Word(items, "struck");
        var over = Word(items, "over");
        Assert.InRange(LineUnder(lines, under).Start.Y, under.Bounds.Top + (under.Bounds.Height * 0.7f), under.Bounds.Bottom);
        Assert.InRange(LineUnder(lines, struck).Start.Y, struck.Bounds.Top + (struck.Bounds.Height * 0.3f), struck.Bounds.Top + (struck.Bounds.Height * 0.7f));
        Assert.InRange(LineUnder(lines, over).Start.Y, over.Bounds.Top, over.Bounds.Top + 2);
    }

    /// <summary>The one line that starts where <paramref name="word"/> does, within its height.</summary>
    private static DrawLineItem LineUnder(List<DrawLineItem> lines, DrawTextItem word) =>
        lines.Single(line => Math.Abs(line.Start.X - word.Bounds.Left) < 0.5f
            && line.Start.Y >= word.Bounds.Top && line.Start.Y <= word.Bounds.Bottom);

    /// <summary>
    /// The decoration is in the colour of the box that asked for it: its own colour for
    /// <c>currentcolor</c>, even under a word of another colour, and the colour it names otherwise.
    /// </summary>
    [Fact]
    public void The_Decoration_Keeps_The_Colour_Of_The_Box_That_Asked_For_It()
    {
        var items = Paint(
            "<p><span style=\"color: red; text-decoration: underline\"><b style=\"color: black\">inherited</b></span></p>" +
            "<p><span style=\"text-decoration: underline blue\">named</span></p>");

        var lines = items.OfType<DrawLineItem>().ToList();
        Assert.Equal(BColor.FromArgb(255, 255, 0, 0), LineUnder(lines, Word(items, "inherited")).Color);
        Assert.Equal(BColor.FromArgb(255, 0, 0, 255), LineUnder(lines, Word(items, "named")).Color);
    }

    /// <summary>A link whose author turned its decoration off has none.</summary>
    [Fact]
    public void A_Link_Without_Decoration_Has_No_Line()
    {
        var items = Paint("<style>a { text-decoration: none }</style><p>No <a href=\"/z\">underline</a> here</p>");

        Assert.Empty(items.OfType<DrawLineItem>());
    }
}
