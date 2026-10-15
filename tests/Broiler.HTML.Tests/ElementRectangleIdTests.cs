using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// <see cref="HtmlContainer.GetElementRectangle"/> finds an element by its <c>id</c> as
/// <c>getElementById</c> does: case-sensitively, and the document's own element first.
/// </summary>
/// <remarks>
/// It lowercased the id and compared case-insensitively, so <c>GetElementRectangle("box")</c> returned the
/// first element with <c>id="Box"</c>. In-page navigation goes through it (a click on <c>#box</c> scrolls
/// to the rectangle it returns), and so does the browser's form-control host.
/// </remarks>
public sealed class ElementRectangleIdTests
{
    [Fact]
    public void Ids_Match_Case_Sensitively()
    {
        using var container = Container();
        container.SetHtmlWithStyleSet(
            "<!DOCTYPE html><html><body style=\"margin: 0\">"
            + "<div id=\"Box\" style=\"width: 30px; height: 10px\"></div>"
            + "<div id=\"box\" style=\"width: 70px; height: 10px\"></div>"
            + "</body></html>",
            null,
            "about:blank");
        container.PerformLayout();

        Assert.Equal(70, container.GetElementRectangle("box")!.Value.Width, 0.5);
        Assert.Equal(30, container.GetElementRectangle("Box")!.Value.Width, 0.5);
        Assert.Null(container.GetElementRectangle("BOX"));
    }

    /// <summary>
    /// An id only a nested sub-document holds is still found there; when both documents hold it, the
    /// document's own element wins, wherever the iframe stands.
    /// </summary>
    [Fact]
    public void A_Sub_Documents_Id_Is_Found_After_The_Documents_Own()
    {
        var outer = HtmlDocumentParser.ParseDocument(
            "<!DOCTYPE html><html><body style=\"margin: 0\"><iframe width=\"200\" height=\"100\"></iframe>"
            + "<div id=\"shared\" style=\"width: 30px; height: 10px\"></div></body></html>").Document;
        var inner = HtmlDocumentParser.ParseDocument(
            "<!DOCTYPE html><html><body><div id=\"shared\" style=\"width: 50px; height: 10px\"></div>"
            + "<div id=\"inner-only\" style=\"width: 40px; height: 10px\"></div></body></html>").Document;
        var iframe = outer.Body!.ChildNodes.OfType<DomElement>().Single(e => e.LocalName == "iframe");

        using var container = Container();
        container.ContentDocumentResolver = element => ReferenceEquals(element, iframe) ? inner : null;
        container.SetDocumentWithStyleSet(outer, null, "about:blank");
        container.PerformLayout();

        Assert.Equal(30, container.GetElementRectangle("shared")!.Value.Width, 0.5);
        Assert.Equal(40, container.GetElementRectangle("inner-only")!.Value.Width, 0.5);
    }

    private static HtmlContainer Container() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };
}
