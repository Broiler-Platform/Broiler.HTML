using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A document set as HTML is in the mode its own parse decides (HTML §13.2.6.4.1,
/// <c>HtmlDocumentParseResult.IsQuirksMode</c>): the DOCTYPE token that ends the initial insertion mode,
/// or its absence. The container used to tokenize the source a second time to read it.
/// </summary>
public sealed class DocumentModeFromParseTests
{
    [Theory]
    [InlineData("<!DOCTYPE html><p>x</p>", false)]
    [InlineData("<!-- comment -->  <!DOCTYPE html><p>x</p>", false)]
    [InlineData("<p>x</p><!DOCTYPE html>", true)]
    [InlineData("<p>x</p>", true)]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\"><p>x</p>", true)]
    public void The_Parse_Decides_The_Document_Mode(string source, bool quirks)
    {
        using var container = new HtmlContainer { MaxSize = new SizeF(800, 600) };
        container.SetHtmlWithStyleSet(source, null, "about:blank");

        Assert.Equal(quirks, container.IsQuirksMode);
    }
}
