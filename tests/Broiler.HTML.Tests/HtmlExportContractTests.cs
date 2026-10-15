using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// The renderer's HTML export (<see cref="HtmlContainer.GetHtml"/> and the selection a copy puts on the
/// clipboard), written by the shared <c>HtmlSerializer</c> from a view of the rendered boxes.
/// </summary>
/// <remarks>
/// <para>
/// The export's own contract is kept: an element's text is its rendered words; <c>Inline</c> writes the
/// authored inline declarations as the <c>style</c> attribute, last; <c>InHeader</c> puts the style sheet in
/// a <c>&lt;head&gt;</c> right after <c>&lt;html&gt;</c>, ahead of the document's own; a <c>&lt;link&gt;</c> whose
/// <c>href</c> is a <c>property</c> or <c>method</c> is not written; a selection exports the selected boxes with
/// their ancestors and marks the selection root with fragment comments.
/// </para>
/// <para>
/// Where the recursive writer this replaced differed from HTML fragment serialization (§13.3), these assert
/// the correction: a void element is written <c>&lt;br&gt;</c> and keeps no children, so an input's generated
/// label is not stray text after it; a raw-text element's text is not escaped; the <c>style</c> attribute
/// has no trailing <c>;</c>. The table layout's spacing boxes and a nested document's sub-viewport are not
/// markup and are left out.
/// </para>
/// </remarks>
public sealed class HtmlExportContractTests
{
    private const string Prefix = "<html><head></head><body style=\"margin: 0\">";
    private const string Suffix = "</body></html>";

    [Fact]
    public void Text_And_Attributes_Are_Escaped_And_Void_Elements_Take_No_End_Tag()
    {
        var html = Export(
            "<p id=\"a\" class=\"c\" title='x &amp; \"y\"'>Hello <b>bold</b> &lt;world&gt;</p>"
            + "<br><img src=\"data:,\" alt=\"i\"><input type=\"submit\" value=\"Go\"><input value=\"v\">");

        Assert.Equal(
            Prefix
            + "<p id=\"a\" class=\"c\" title=\"x &amp; &quot;y&quot;\">Hello <b>bold</b> &lt;world&gt;</p>"
            + "<br><img src=\"data:,\" alt=\"i\"><input type=\"submit\" value=\"Go\"><input value=\"v\">"
            + Suffix,
            html);
    }

    [Fact]
    public void Inline_Writes_The_Authored_Declarations_Last_And_The_Other_Modes_The_Attribute()
    {
        const string Body = "<p style=\"color: red; margin-left: 4px\" id=\"s\">styled</p>";
        const string Head = "<style>p { padding: 1px }</style>";

        Assert.Equal(
            "<html><head><style>p { padding: 1px }</style></head><body style=\"margin: 0\"><p id=\"s\" style=\"color: red; margin-left: 4px\">styled</p></body></html>",
            Export(Body, Head, HtmlGenerationStyle.Inline));
        Assert.Equal(
            "<html><head><style>p { padding: 1px }</style></head><body style=\"margin: 0\"><p style=\"color: red; margin-left: 4px\" id=\"s\">styled</p></body></html>",
            Export(Body, Head, HtmlGenerationStyle.None));
    }

    [Fact]
    public void InHeader_Puts_The_Style_Sheet_In_A_Head_Before_The_Documents_Own()
    {
        var html = Export("<p>x</p>", "<style>p { padding: 1px }</style>", HtmlGenerationStyle.InHeader);
        var newLine = Environment.NewLine;

        Assert.StartsWith($"<html><head>{newLine}<style type=\"text/css\">{newLine}", html, StringComparison.Ordinal);
        Assert.Contains($"{newLine}</style>{newLine}</head>{newLine}<head><style>p {{ padding: 1px }}</style></head><body", html, StringComparison.Ordinal);
        Assert.Contains("padding: 1px", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Raw_Text_Elements_Text_Is_Not_Escaped_And_An_Escapable_Ones_Is()
    {
        var html = Export("<xmp>a < b & c</xmp><textarea>t < u</textarea>", "<style>.g::before { content: 'x' }</style>");

        Assert.Contains("<style>.g::before { content: 'x' }</style>", html, StringComparison.Ordinal);
        Assert.Contains("<xmp>a < b & c</xmp>", html, StringComparison.Ordinal);
        Assert.Contains("<textarea>t &lt; u</textarea>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendered_Words_Are_Written_With_Collapsed_White_Space_And_Generated_Content()
    {
        var html = Export("<p class=\"g\">gen</p><p>  lots   of  spaces  </p>", "<style>.g::before { content: 'BEFORE ' }</style>");

        Assert.Contains("<p class=\"g\">BEFORE gen</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>lots of spaces </p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_Svg_Keeps_Its_Names_And_No_Svg_Element_Is_Void()
    {
        var html = Export(
            "<svg width=\"20\" height=\"20\" viewBox=\"0 0 20 20\"><linearGradient id=\"g\"></linearGradient>"
            + "<foreignObject width=\"10\" height=\"10\"><p>fo</p></foreignObject><rect width=\"5\" height=\"5\"/></svg>");

        Assert.Contains(
            "<svg width=\"20\" height=\"20\" viewBox=\"0 0 20 20\"><linearGradient id=\"g\"></linearGradient>"
            + "<foreignObject width=\"10\" height=\"10\"><p>fo</p></foreignObject><rect width=\"5\" height=\"5\"></rect></svg>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Property_Or_Method_Link_Is_Not_Written()
    {
        var html = Export("<link rel=\"stylesheet\" href=\"property:x\"><link rel=\"icon\" href=\"y.ico\"><p>after</p>");

        Assert.Equal(Prefix + "<link rel=\"icon\" href=\"y.ico\"><p>after</p>" + Suffix, html);
    }

    [Fact]
    public void A_Tables_Spacing_Boxes_Are_Not_Markup()
    {
        var html = Export("<table><tr><td rowspan=\"2\">a</td><td>b</td></tr><tr><td>c</td></tr></table>");

        Assert.Equal(Prefix + "<table><tbody><tr><td rowspan=\"2\">a</td><td>b</td></tr><tr><td>c</td></tr></tbody></table>" + Suffix, html);
    }

    [Fact]
    public void A_Nested_Documents_Sub_Viewport_Is_Not_Written()
    {
        var outer = HtmlDocumentParser.ParseDocument(
            "<!DOCTYPE html><html><head></head><body style=\"margin: 0\"><iframe width=\"100\" height=\"50\"></iframe><p>outer</p></body></html>").Document;
        var inner = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><html><body><p>inner</p></body></html>").Document;
        var iframe = outer.Body!.ChildNodes.OfType<DomElement>().Single(e => e.LocalName == "iframe");

        using var container = Container();
        container.ContentDocumentResolver = element => ReferenceEquals(element, iframe) ? inner : null;
        container.SetDocumentWithStyleSet(outer, null, "about:blank");
        container.PerformLayout();

        Assert.Equal(Prefix + "<iframe width=\"100\" height=\"50\"></iframe><p>outer</p>" + Suffix, container.GetHtml(HtmlGenerationStyle.None));
    }

    [Fact]
    public void A_Selection_Exports_Its_Boxes_With_Their_Ancestors_And_Marks_Its_Root()
    {
        using var container = Container();
        container.SetHtmlWithStyleSet(
            "<!DOCTYPE html><html><head></head><body style=\"margin: 0\"><p id=\"p1\">first para</p>"
            + "<div id=\"d\"><p id=\"p2\">second <b>para</b></p><p id=\"p3\">third para</p></div><p id=\"p4\">fourth</p></body></html>",
            null,
            "about:blank");
        container.PerformLayout();

        var from = container.GetElementRectangle("p2")!.Value;
        var to = container.GetElementRectangle("p3")!.Value;
        var start = new PointF(from.X + 1, from.Y + 2);
        var end = new PointF(to.Right - 1, to.Bottom - 2);
        container.HandleMouseDown(start);
        container.HandleMouseMove(start, leftButton: true);
        container.HandleMouseMove(end, leftButton: true);
        container.HandleMouseUp(end);

        Assert.Equal(
            "<html><body style=\"margin: 0\"><div id=\"d\"><!--StartFragment--><p id=\"p2\">second <b>para</b></p>"
            + "<p id=\"p3\">third para</p><!--EndFragment--></div></body></html>",
            container.HtmlContainerInt.GetSelectedHtml());
    }

    /// <summary>
    /// A nested tree exports whole. The view, the selection and the serializer use explicit stacks, but the
    /// box projection, the cascade and the layout before them still recurse, and they set the depth limit:
    /// 2,000 nested elements overflow the stack in the projection, before anything is exported.
    /// </summary>
    [Fact]
    public void A_Nested_Tree_Exports_Whole()
    {
        const int Depth = 300;
        var html = Export(string.Concat(Enumerable.Repeat("<span>", Depth)) + "deep" + string.Concat(Enumerable.Repeat("</span>", Depth)));

        Assert.Contains("deep", html, StringComparison.Ordinal);
        Assert.Equal(Depth, CountOf(html, "<span>"));
        Assert.Equal(Depth, CountOf(html, "</span>"));
    }

    [Fact]
    public void An_Empty_Container_Exports_Nothing()
    {
        using var container = Container();

        Assert.Equal(string.Empty, container.GetHtml());
    }

    private static string Export(string body, string head = "", HtmlGenerationStyle style = HtmlGenerationStyle.None)
    {
        using var container = Container();
        container.SetHtmlWithStyleSet($"<!DOCTYPE html><html><head>{head}</head><body style=\"margin: 0\">{body}</body></html>", null, "about:blank");
        container.PerformLayout();
        return container.GetHtml(style);
    }

    private static HtmlContainer Container() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
