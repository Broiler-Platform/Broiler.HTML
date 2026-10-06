using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// The selectors a page's live state decides, as the renderer styles them: <c>:visited</c> from the
/// history its host gives it, <c>:target</c> from the fragment its host navigated to or the target a
/// scripting host stamped, and the other states a scripting host stamps into the page.
/// </summary>
/// <remarks>
/// <c>:visited</c> takes only its colours, as browsers paint it, so a page cannot learn the user's
/// history by measuring its own links: a visited link's <c>font-size</c> is the unvisited one.
/// </remarks>
public sealed class RendererSelectorStateTests
{
    private const string PageUrl = "https://example.test/page";

    private static Fragment Layout(string body, Func<Uri, bool>? visited = null, string? fragment = null)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
            VisitedLinkPredicate = visited,
            TargetFragment = fragment,
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    private const string Links =
        "<style>a:link { color: rgb(0, 0, 255) } a:visited { color: rgb(128, 0, 128); font-size: 40px }</style>" +
        "<p><a href=\"/seen\">seenmarker</a> <a href=\"new\">newmarker</a></p>";

    /// <summary>A link the host's history has is painted in its <c>:visited</c> colour, and in nothing else of that rule.</summary>
    [Fact]
    public void A_Visited_Link_Takes_Its_Visited_Colour_Only()
    {
        var tree = Layout(Links, visited: url => url.AbsolutePath == "/seen");

        var seen = Run(tree, "seenmarker");
        var fresh = Run(tree, "newmarker");
        Assert.Equal("rgb(128, 0, 128)", Rgb(seen.Style.ActualColor));
        Assert.Equal(fresh.Style.FontSize, seen.Style.FontSize);
        Assert.Equal("rgb(0, 0, 255)", Rgb(fresh.Style.ActualColor));
    }

    /// <summary>Without a history no link is visited.</summary>
    [Fact]
    public void Without_A_History_No_Link_Is_Visited()
    {
        var tree = Layout(Links);

        Assert.Equal("rgb(0, 0, 255)", Rgb(Run(tree, "seenmarker").Style.ActualColor));
    }

    private const string Sections =
        "<style>section:target { background-color: rgb(255, 255, 0) }</style>" +
        "<section id=\"s\">s-marker</section><section id=\"t\">t-marker</section>";

    /// <summary>The element the fragment names is <c>:target</c>.</summary>
    [Fact]
    public void The_Fragments_Element_Is_The_Target()
    {
        var tree = Layout(Sections, fragment: "s");

        Assert.Equal("rgb(255, 255, 0)", Rgb(Find(tree, "section", "s-marker").Style.ActualBackgroundColor));
        Assert.NotEqual("rgb(255, 255, 0)", Rgb(Find(tree, "section", "t-marker").Style.ActualBackgroundColor));
    }

    /// <summary>A target a scripting host stamped wins over the fragment the page was loaded with.</summary>
    [Fact]
    public void A_Stamped_Target_Wins_Over_The_Fragment()
    {
        var tree = Layout(Sections.Replace("<section id=\"t\">", "<section id=\"t\" data-broiler-state=\"target\">"), fragment: "s");

        Assert.Equal("rgb(255, 255, 0)", Rgb(Find(tree, "section", "t-marker").Style.ActualBackgroundColor));
        Assert.NotEqual("rgb(255, 255, 0)", Rgb(Find(tree, "section", "s-marker").Style.ActualBackgroundColor));
    }

    /// <summary>
    /// Restyling the document follows a new fragment without parsing it again: the new target is
    /// <c>:target</c>, the old one no longer, and a value typed into a field stays.
    /// </summary>
    [Fact]
    public void Restyling_Follows_A_New_Fragment_And_Keeps_What_Was_Typed()
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
            TargetFragment = "s",
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + Sections + "<input id=\"f\"></body></html>", null, PageUrl);
        container.PerformLayout();
        var field = Flatten(container.LatestFragmentTree!).First(static f => f.Style.TagName == "input");
        Assert.True(container.SetEditableInputValueAtDocumentPoint(
            new PointF(field.Location.X + 2, field.Location.Y + 2), "typed"));

        container.TargetFragment = "t";
        container.RestyleDocument();
        container.PerformLayout();
        var tree = container.LatestFragmentTree!;

        Assert.Equal("rgb(255, 255, 0)", Rgb(Find(tree, "section", "t-marker").Style.ActualBackgroundColor));
        Assert.NotEqual("rgb(255, 255, 0)", Rgb(Find(tree, "section", "s-marker").Style.ActualBackgroundColor));
        Assert.Contains("value=\"typed\"", container.GetHtml());
    }

    /// <summary>A control a scripting host stamped as interacted with is <c>:user-invalid</c> when it is invalid.</summary>
    [Fact]
    public void A_Stamped_Interaction_Makes_An_Invalid_Control_User_Invalid()
    {
        var tree = Layout(
            "<style>input:user-invalid { background-color: rgb(255, 0, 0) } input:user-valid { background-color: rgb(0, 128, 0) }</style>" +
            "<input id=\"a\" required data-broiler-state=\"user-interacted\"><input id=\"b\" required value=\"x\" data-broiler-state=\"user-interacted\"><input id=\"c\" required>",
            visited: _ => false);

        var inputs = Flatten(tree).Where(static f => f.Style.TagName == "input").ToList();
        Assert.Equal(3, inputs.Count);
        Assert.Equal("rgb(255, 0, 0)", Rgb(inputs[0].Style.ActualBackgroundColor));
        Assert.Equal("rgb(0, 128, 0)", Rgb(inputs[1].Style.ActualBackgroundColor));
        Assert.NotEqual("rgb(255, 0, 0)", Rgb(inputs[2].Style.ActualBackgroundColor));
    }

    private static string Rgb(object? color) => color switch
    {
        Broiler.Graphics.Color.BColor c => $"rgb({c.R}, {c.G}, {c.B})",
        string s => s,
        _ => color?.ToString() ?? "none",
    };

    /// <summary>The text run that holds <paramref name="marker"/>: an inline element's text is styled there, not in a box of its own.</summary>
    private static InlineFragment Run(Fragment tree, string marker) =>
        Flatten(tree).SelectMany(static f => f.Lines ?? []).SelectMany(static l => l.Inlines)
            .First(i => i.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private static Fragment Find(Fragment tree, string tag, string marker) =>
        Flatten(tree).First(f => f.Style.TagName == tag && Text(f).Contains(marker, StringComparison.Ordinal));

    private static IEnumerable<Fragment> Flatten(Fragment fragment)
    {
        yield return fragment;
        foreach (var child in fragment.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    private static string Text(Fragment fragment) =>
        string.Concat(Flatten(fragment).SelectMany(static f => f.Lines ?? []).SelectMany(static l => l.Inlines).Select(static i => i.Text));
}
