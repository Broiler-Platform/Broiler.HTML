using System.Drawing;
using System.Text.Json;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Dom.Parse;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// HTML §4.2.3: the document base URL is the frozen base URL of the first <c>&lt;base&gt;</c> in tree
/// order that has an <c>href</c>, whatever that value is, and the document's fallback URL when there is
/// none. A first base that cannot serve — an empty or unparsable value, a <c>data:</c> or
/// <c>javascript:</c> URL — freezes to the fallback; it does not hand the choice to a later base.
/// </summary>
/// <remarks>
/// The renderer used to find the base by scanning its box tree, and read "resolves to the fallback" as
/// "found nothing": <c>&lt;base href=""&gt;</c> followed by another base took the second one. The scan also
/// walked into the sub-viewports projected under an <c>&lt;iframe&gt;</c>, so a sub-document's base became
/// its parent's when the parent declared none, and the parent's base was then stamped over the
/// sub-document's boxes.
/// </remarks>
public sealed class DocumentBaseUrlTests
{
    private static readonly Uri Fallback = new("https://site.test/dir/page.html");

    private static Uri Resolve(string html) =>
        DocumentBaseUrl.Resolve(HtmlDocumentParser.ParseDocument(html).Document, Fallback);

    [Theory]
    [InlineData("<p>no base</p>", "https://site.test/dir/page.html")]
    [InlineData("<base href=\"https://a.test/x/\"><base href=\"https://b.test/\">", "https://a.test/x/")]
    [InlineData("<base href=\"sub/\">", "https://site.test/dir/sub/")]
    [InlineData("<base href=\"/root/\">", "https://site.test/root/")]
    [InlineData("<base href=\"  https://a.test/x/ \n\">", "https://a.test/x/")]
    // A base without an href is not the first base *with* one.
    [InlineData("<base target=\"_blank\"><base href=\"https://b.test/\">", "https://b.test/")]
    public void The_First_Base_With_An_Href_Sets_The_Base(string head, string expected) =>
        Assert.Equal(new Uri(expected), Resolve("<!DOCTYPE html><html><head>" + head + "</head><body></body></html>"));

    [Theory]
    [InlineData("<base href=\"\">")]
    [InlineData("<base href=\"   \">")]
    [InlineData("<base href=\"page.html\">")]
    [InlineData("<base href=\"https://site.test/dir/page.html\">")]
    [InlineData("<base href=\"data:text/html,x\">")]
    [InlineData("<base href=\"javascript:void(0)\">")]
    public void A_First_Base_That_Freezes_To_The_Fallback_Still_Hides_A_Later_One(string first)
    {
        var html = "<!DOCTYPE html><html><head>" + first + "<base href=\"https://later.test/\"></head><body></body></html>";

        Assert.Equal(Fallback, Resolve(html));
    }

    [Fact]
    public void A_Base_In_Template_Contents_Is_Not_In_The_Tree()
    {
        var html = "<!DOCTYPE html><html><head><template><base href=\"https://template.test/\"></template>"
            + "<base href=\"https://b.test/\"></head><body></body></html>";

        Assert.Equal(new Uri("https://b.test/"), Resolve(html));
    }

    /// <summary>
    /// A host-built tree (the script bridge's typed hand-off) wraps the document in a synthetic
    /// <c>#document</c> element; the base inside it is still the document's.
    /// </summary>
    [Fact]
    public void A_Host_Built_Tree_Under_A_Document_Wrapper_Is_Searched()
    {
        var document = new DomDocument();
        var wrapper = new HostElement(document, "#document");
        document.AppendChild(wrapper);
        var html = document.CreateElement("html");
        wrapper.AppendChild(html);
        var head = document.CreateElement("head");
        html.AppendChild(head);
        var baseElement = document.CreateElement("base");
        baseElement.SetAttribute("href", "https://host.test/");
        head.AppendChild(baseElement);

        Assert.Equal(new Uri("https://host.test/"), DocumentBaseUrl.Resolve(document, Fallback));
    }

    /// <summary>
    /// A legacy in-tree sub-document (rooted at <c>#subdoc-root</c>) is another document: its base is
    /// its own and is not seen from the parent.
    /// </summary>
    [Fact]
    public void An_In_Tree_Sub_Documents_Base_Stays_In_The_Sub_Document()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><html><head></head><body><iframe></iframe></body></html>").Document;
        var iframe = document.Body!.ChildNodes.OfType<DomElement>().Single();
        var subRoot = new HostElement(document, DocumentBaseUrl.NestedDocumentRootName);
        iframe.AppendChild(subRoot);
        var subHtml = document.CreateElement("html");
        subRoot.AppendChild(subHtml);
        var subBase = document.CreateElement("base");
        subBase.SetAttribute("href", "https://sub.test/");
        subHtml.AppendChild(subBase);

        Assert.Equal(Fallback, DocumentBaseUrl.Resolve(document, Fallback));
        Assert.Equal(new Uri("https://sub.test/"), DocumentBaseUrl.Resolve(subRoot, Fallback));
    }

    [Fact]
    public void The_Resource_Log_Reports_The_First_Base_And_Resolves_Against_It()
    {
        var json = HtmlParserDump.DumpResourceLogAsJson(
            "<!DOCTYPE html><html><head><base href=\"\"><base href=\"https://other.test/\"></head>"
            + "<body><img src=\"a.png\"></body></html>",
            "https://site.test/dir/page.html");

        using var log = JsonDocument.Parse(json);
        Assert.Equal("https://site.test/dir/page.html", log.RootElement.GetProperty("DocumentBaseUrl").GetString());
        var image = Assert.Single(log.RootElement.GetProperty("Resources").EnumerateArray());
        Assert.Equal("https://site.test/dir/a.png", image.GetProperty("ResolvedUrl").GetString());
    }

    /// <summary>
    /// A resolver-supplied sub-document's base reaches neither its parent's boxes nor the base the
    /// parent publishes, and the sub-document's own images resolve against it.
    /// </summary>
    [Fact]
    public void A_Content_Documents_Base_Does_Not_Reach_Its_Parent()
    {
        using var server = new LoopbackHttpServer(request => request.Path.EndsWith(".png", StringComparison.Ordinal)
            ? LoopbackResponse.Ok("image/png", TestContent.Png)
            : LoopbackResponse.NotFound());
        var run = Guid.NewGuid().ToString("N");

        var outer = HtmlDocumentParser.ParseDocument(
            $"<!DOCTYPE html><html><head></head><body><img src=\"outer.png?{run}\" width=10 height=10>"
            + "<iframe width=100 height=100></iframe></body></html>").Document;
        var inner = HtmlDocumentParser.ParseDocument(
            $"<!DOCTYPE html><html><head><base href=\"{server.Url("/sub/")}\"></head>"
            + $"<body><img src=\"inner.png?{run}\" width=10 height=10></body></html>").Document;
        var iframe = outer.Body!.ChildNodes.OfType<DomElement>().Single(e => e.LocalName == "iframe");

        using var container = TestContent.Container();
        container.MaxSize = new SizeF(800, 600);
        container.ContentDocumentResolver = element => ReferenceEquals(element, iframe) ? inner : null;
        container.SetDocumentWithStyleSet(outer, null, server.Url("/page"));
        container.PerformLayout();

        Assert.Null(container.HtmlContainerInt.DocumentBaseUrl);
        var paths = server.Requests.Select(static request => request.Path).ToList();
        Assert.Contains("/outer.png", paths);
        Assert.DoesNotContain("/sub/outer.png", paths);
        Assert.Contains("/sub/inner.png", paths);
    }

    /// <summary>An element the parser cannot create: the bridge's synthetic wrapper and sub-document root.</summary>
    private sealed class HostElement(DomDocument document, string name)
        : DomElement(document, DomName.CreateLocal(DomNamespaces.Html, name), DomNodeType.Element);
}
