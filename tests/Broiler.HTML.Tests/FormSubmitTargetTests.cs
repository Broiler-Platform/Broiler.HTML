using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A click on a submit button reports the URL its form submits to (HTML §4.10.21.3): the button's
/// <c>formaction</c>, else its form owner's <c>action</c>, else the document's URL. Only a submit button
/// does — a <c>&lt;button&gt;</c> whose type is missing, invalid or <c>submit</c>, or an
/// <c>&lt;input type=submit&gt;</c> — and its form owner is the one a <c>form="id"</c> names, before any
/// form around it.
/// </summary>
/// <remarks>
/// The renderer took every button for a submit button, so <c>type="button"</c> and <c>type="reset"</c>
/// reported the form's action too. It found the form by walking up the box tree, which ignored
/// <c>form="id"</c>, and it ignored <c>formaction</c>. An empty action gave the base URL, where the
/// standard submits to the document's own URL.
/// </remarks>
public sealed class FormSubmitTargetTests
{
    private const string PageUrl = "https://page.test/dir/doc.html";
    private const string Button = "style=\"position: absolute; left: 0; top: 0; width: 100px; height: 30px\"";

    [Theory]
    [InlineData($"<form action=\"https://form.test/a\"><button {Button}>go</button></form>", "https://form.test/a")]
    [InlineData($"<form action=\"https://form.test/a\"><button type=\"submit\" {Button}>go</button></form>", "https://form.test/a")]
    // An invalid type is the Submit Button state.
    [InlineData($"<form action=\"https://form.test/a\"><button type=\"menu\" {Button}>go</button></form>", "https://form.test/a")]
    [InlineData($"<form action=\"https://form.test/a\"><input type=\"submit\" value=\"go\" {Button}></form>", "https://form.test/a")]
    [InlineData($"<form action=\"https://form.test/a\"><button formaction=\"https://form.test/override\" {Button}>go</button></form>", "https://form.test/override")]
    [InlineData($"<form action=\"https://form.test/a\"><input type=\"submit\" formaction=\"https://form.test/override\" {Button}></form>", "https://form.test/override")]
    // A relative action resolves against the document's base.
    [InlineData($"<form action=\"next.html\"><button {Button}>go</button></form>", "https://page.test/dir/next.html")]
    public void A_Submit_Button_Reports_Its_Forms_Action(string body, string expected) =>
        Assert.Equal(expected, Click(body));

    [Fact]
    public void A_Form_Attribute_Names_The_Owner_Before_The_Enclosing_Form()
    {
        var link = Click(
            "<form id=\"owner\" action=\"https://form.test/owner\"></form>"
            + $"<form action=\"https://form.test/enclosing\"><button form=\"owner\" {Button}>go</button></form>");

        Assert.Equal("https://form.test/owner", link);
    }

    [Theory]
    [InlineData("<button type=\"button\" {0}>go</button>")]
    [InlineData("<button type=\"reset\" {0}>go</button>")]
    [InlineData("<input type=\"button\" value=\"go\" {0}>")]
    [InlineData("<input type=\"reset\" value=\"go\" {0}>")]
    public void A_Button_That_Is_Not_A_Submit_Button_Does_Not_Report_The_Action(string control)
    {
        var link = Click("<form action=\"https://form.test/a\">" + string.Format(control, Button) + "</form>");

        Assert.NotEqual("https://form.test/a", link);
    }

    /// <summary>An empty or missing action submits to the document's URL, not to its base URL.</summary>
    [Theory]
    [InlineData("<form action=\"\">")]
    [InlineData("<form>")]
    public void An_Empty_Action_Submits_To_The_Documents_Url(string form)
    {
        var link = Click(form + $"<button {Button}>go</button></form>", head: "<base href=\"https://base.test/\">");

        Assert.Equal(PageUrl, link);
    }

    private static string? Click(string body, string head = "")
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet(
            $"<!DOCTYPE html><html><head>{head}</head><body style=\"margin: 0\">{body}</body></html>", null, PageUrl);
        container.PerformLayout();

        string? link = null;
        container.LinkClicked += (_, e) =>
        {
            link = e.Link;
            e.Handled = true;
        };
        container.HandleMouseDown(new PointF(10, 10));
        container.HandleMouseUp(new PointF(10, 10));
        return link;
    }
}
