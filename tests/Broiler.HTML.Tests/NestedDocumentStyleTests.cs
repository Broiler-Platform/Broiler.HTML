using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.Graphics.Color;
using Broiler.HTML.Dom.Parse;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A nested browsing context's sub-document is styled by its own sheets, and the page around it by its
/// own: an iframe's <c>&lt;style&gt;</c> does not reach its parent, and the parent's does not reach the
/// iframe (CSSOM §6.1). Both start from the same user-agent sheet.
/// </summary>
/// <remarks>
/// The renderer collected every <c>&lt;style&gt;</c> and <c>&lt;link rel=stylesheet&gt;</c> in its box tree
/// into one set, including those inside the sub-viewports it projects under an <c>&lt;iframe&gt;</c>, and
/// cascaded both documents with it. An iframe's <c>p { color: red }</c> turned every paragraph of the page
/// red, the same leak across the nested-document boundary that the base URL had.
/// </remarks>
public sealed class NestedDocumentStyleTests
{
    private static readonly BColor Red = BColor.FromArgb(255, 255, 0, 0);
    private static readonly BColor Blue = BColor.FromArgb(255, 0, 0, 255);

    [Fact]
    public void A_Content_Documents_Sheet_Styles_Only_The_Content_Document()
    {
        var tree = LayoutWithContentDocument(
            "<p>outer-marker</p>",
            "<style>p { color: rgb(255, 0, 0) }</style>");

        Assert.NotEqual(Red, Paragraph(tree, "outer-marker").Style.ActualColor);
        Assert.Equal(Red, Paragraph(tree, "inner-marker").Style.ActualColor);
    }

    [Fact]
    public void The_Parents_Sheet_Does_Not_Style_A_Content_Document()
    {
        var tree = LayoutWithContentDocument(
            "<style>p { color: rgb(0, 0, 255) }</style><p>outer-marker</p>",
            string.Empty);

        Assert.Equal(Blue, Paragraph(tree, "outer-marker").Style.ActualColor);
        Assert.NotEqual(Blue, Paragraph(tree, "inner-marker").Style.ActualColor);
    }

    /// <summary>A legacy in-tree sub-document (rooted at <c>#subdoc-root</c>) is scoped the same way.</summary>
    [Fact]
    public void An_In_Tree_Sub_Documents_Sheet_Stays_In_The_Sub_Document()
    {
        var document = HtmlDocumentParser.ParseDocument(
            "<!DOCTYPE html><html><head></head><body><p>outer-marker</p><iframe></iframe></body></html>").Document;
        var iframe = document.Body!.ChildNodes.OfType<DomElement>().Single(e => e.LocalName == "iframe");
        var subRoot = new HostElement(document, DocumentBaseUrl.NestedDocumentRootName);
        iframe.AppendChild(subRoot);
        var style = document.CreateElement("style");
        style.AppendChild(document.CreateTextNode("p { color: rgb(255, 0, 0) }"));
        subRoot.AppendChild(style);
        var paragraph = document.CreateElement("p");
        paragraph.AppendChild(document.CreateTextNode("inner-marker"));
        subRoot.AppendChild(paragraph);

        using var container = Container();
        container.SetDocumentWithStyleSet(document, null, "about:blank");
        container.PerformLayout();
        var tree = container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");

        Assert.NotEqual(Red, Paragraph(tree, "outer-marker").Style.ActualColor);
        Assert.Equal(Red, Paragraph(tree, "inner-marker").Style.ActualColor);
    }

    private static Fragment LayoutWithContentDocument(string outerBody, string innerHead)
    {
        var outer = HtmlDocumentParser.ParseDocument(
            $"<!DOCTYPE html><html><head></head><body>{outerBody}<iframe width=200 height=100></iframe></body></html>").Document;
        var inner = HtmlDocumentParser.ParseDocument(
            $"<!DOCTYPE html><html><head>{innerHead}</head><body><p>inner-marker</p></body></html>").Document;
        var iframe = outer.Body!.ChildNodes.OfType<DomElement>().Single(e => e.LocalName == "iframe");

        using var container = Container();
        container.ContentDocumentResolver = element => ReferenceEquals(element, iframe) ? inner : null;
        container.SetDocumentWithStyleSet(outer, null, "about:blank");
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    private static HtmlContainer Container() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };

    private static Fragment Paragraph(Fragment tree, string marker) =>
        Flatten(tree).First(f => f.Style.TagName == "p" && Text(f).Contains(marker, StringComparison.Ordinal));

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

    /// <summary>An element the parser cannot create: the bridge's synthetic sub-document root.</summary>
    private sealed class HostElement(DomDocument document, string name)
        : DomElement(document, DomName.CreateLocal(DomNamespaces.Html, name), DomNodeType.Element);
}
