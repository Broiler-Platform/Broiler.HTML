using System.Drawing;
using System.Text.Json;
using Broiler.Dom.Html;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Dom.Parse;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// Whether an element box is a single tag — written <c>&lt;br&gt;</c> with no end tag by the HTML export,
/// reported as <c>isSingle</c> by the parser dump, and closed with <c>/&gt;</c> by Broiler.Layout's inline-SVG
/// serialization — is the shared void classification the tree builder itself used
/// (<see cref="HtmlSerializer.VoidElements"/>).
/// </summary>
/// <remarks>
/// The renderer kept a list of its own, which disagreed with the parser about <c>isindex</c> (an ordinary
/// element since HTML dropped its special parsing) and about the legacy names <c>basefont</c>,
/// <c>bgsound</c> and <c>keygen</c>, and which matched names case-sensitively. A box the export calls single
/// but the parser gave children wrote those children after a self-closed tag.
/// </remarks>
public sealed class VoidElementClassificationTests
{
    [Theory]
    [InlineData("br")]
    [InlineData("img")]
    [InlineData("wbr")]
    [InlineData("input")]
    [InlineData("hr")]
    [InlineData("basefont")]
    [InlineData("bgsound")]
    [InlineData("keygen")]
    [InlineData("isindex")]
    [InlineData("div")]
    [InlineData("span")]
    public void A_Box_Is_Single_Exactly_When_The_Shared_Set_Says_So(string name)
    {
        var json = HtmlParserDump.DumpDomAsJson($"<!DOCTYPE html><html><head></head><body><{name}></body></html>");

        using var dump = JsonDocument.Parse(json);
        var element = Descendants(dump.RootElement).First(e => e.GetProperty("name").GetString() == name);

        Assert.Equal(HtmlSerializer.VoidElements.Contains(name), element.GetProperty("isSingle").GetBoolean());
    }

    [Fact]
    public void The_Export_Writes_Void_Elements_As_Single_Tags_And_Others_With_End_Tags()
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet(
            "<!DOCTYPE html><html><body><p>a<br>b<wbr>c<isindex>d</isindex></p></body></html>", null, "about:blank");
        container.PerformLayout();

        var html = container.GetHtml(HtmlGenerationStyle.None);

        // HTML fragment serialization (§13.3) writes a void element's start tag as <br>, with no end tag.
        Assert.Contains("a<br>b<wbr>c", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</br>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</wbr>", html, StringComparison.Ordinal);
        Assert.Contains("<isindex>", html, StringComparison.Ordinal);
        Assert.Contains("</isindex>", html, StringComparison.Ordinal);
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement box)
    {
        yield return box;
        foreach (var child in box.GetProperty("children").EnumerateArray())
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
