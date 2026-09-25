using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A document is laid out in its own mode on whichever thread lays it out, not in the mode of the
/// document that thread parsed last.
/// </summary>
/// <remarks>
/// The browser window parses a page off its UI thread and lays it out on it, and that thread's last
/// parse was the window's start page, which has no doctype. The layout read the document mode from
/// the UI thread's thread-static state, so every standards-mode page got the quirks-mode layout:
/// Acid1's body, which ends below its text in a browser, filled the window to the bottom. Here the
/// quirks-mode body fill is the witness: in quirks mode an auto-height body fills the viewport, and
/// in standards mode it is as tall as its content.
/// </remarks>
public sealed class DocumentModeThreadTests
{
    private const string Standards = "<!DOCTYPE html><html><body><p>short</p></body></html>";
    private const string Quirks = "<html><body><p>short</p></body></html>";

    [Fact]
    public void A_Standards_Document_Parsed_Elsewhere_Is_Not_Laid_Out_In_Quirks_Mode()
    {
        var body = Body(LayOutOnThisThread(Standards, lastParsedHere: Quirks));

        Assert.True(body.Size.Height < 100, $"The body is {body.Size.Height}px tall in a 600px viewport.");
    }

    [Fact]
    public void A_Quirks_Document_Parsed_Elsewhere_Is_Laid_Out_In_Quirks_Mode()
    {
        var body = Body(LayOutOnThisThread(Quirks, lastParsedHere: Standards));

        Assert.True(body.Size.Height > 500, $"The body is {body.Size.Height}px tall in a 600px viewport.");
    }

    /// <summary>
    /// Parses <paramref name="html"/> on a thread of its own and lays it out on this one, which last
    /// parsed <paramref name="lastParsedHere"/>, as the window's UI thread parsed its start page.
    /// </summary>
    private static Fragment LayOutOnThisThread(string html, string lastParsedHere)
    {
        using (var previous = NewContainer())
            previous.SetHtmlWithStyleSet(lastParsedHere, null, "about:blank");

        HtmlContainer? container = null;
        var parser = new Thread(() =>
        {
            container = NewContainer();
            container.SetHtmlWithStyleSet(html, null, "about:blank");
        });
        parser.Start();
        parser.Join();

        using (container)
        {
            container!.PerformLayout();
            return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
        }
    }

    private static HtmlContainer NewContainer() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };

    private static Fragment Body(Fragment tree) =>
        Flatten(tree).First(static f => f.Style.TagName == "body");

    private static IEnumerable<Fragment> Flatten(Fragment fragment)
    {
        yield return fragment;
        foreach (var child in fragment.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }
}
