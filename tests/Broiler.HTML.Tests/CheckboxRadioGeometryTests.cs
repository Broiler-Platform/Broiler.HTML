using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// Checkboxes and radios as Chromium lays them out: 13px square with their border, a radio's margins
/// 3px 3px 0 5px and a checkbox's 3px 3px 3px 4px, and no padding while the browser draws the widget,
/// whatever the page gives it; <c>appearance: none</c> hands the box back to the page's styles.
/// </summary>
/// <remarks>
/// reCAPTCHA's own demo, google.com/recaptcha/api2/demo, gives its second radio 5px 7px of padding:
/// it was drawn 14px wider and 10px taller than the first, its label pushed along, where Chromium
/// draws the two alike. Measured in Chromium.
/// </remarks>
public sealed class CheckboxRadioGeometryTests
{
    private const string PageUrl = "https://example.test/page";

    private const string Toggles =
        "<style>.padded { display: inline-block; padding: 5px 7px; position: relative }</style>" +
        "<label><input type=\"radio\" id=\"plain\" checked>Red</label><br>" +
        "<label><input type=\"radio\" id=\"padded\" class=\"padded\">Green</label><br>" +
        "<label><input type=\"checkbox\" id=\"check\">Check</label><br>" +
        "<label><input type=\"radio\" id=\"none\" class=\"padded\" style=\"appearance: none\">None</label>";

    private static Fragment Layout(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body>" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    private static List<Fragment> Inputs(Fragment fragment) =>
        string.Equals(fragment.Style.TagName, "input", StringComparison.OrdinalIgnoreCase)
            ? [fragment]
            : [.. fragment.Children.SelectMany(Inputs)];

    /// <summary>Each toggle is 13px square, at the left Chromium's margins put it.</summary>
    [Fact]
    public void A_Toggle_Is_13px_Square_Inside_Its_Margins()
    {
        var inputs = Inputs(Layout(Toggles));

        Assert.Equal(13, inputs[0].Bounds.X, 1);
        Assert.Equal(12, inputs[2].Bounds.X, 1);
        Assert.All(inputs.Take(3), input =>
        {
            Assert.Equal(13, input.Bounds.Width, 1);
            Assert.Equal(13, input.Bounds.Height, 1);
        });
        Assert.Equal(3, inputs[0].Margin.Top, 1);
        Assert.Equal(5, inputs[0].Margin.Left, 1);
        Assert.Equal(4, inputs[2].Margin.Left, 1);
    }

    /// <summary>The page's padding does not reach a radio the browser draws, but does one it draws as none.</summary>
    [Fact]
    public void Padding_Reaches_Only_A_Toggle_The_Page_Draws()
    {
        var inputs = Inputs(Layout(Toggles));

        Assert.Equal(0, inputs[1].Padding.Left, 1);
        Assert.Equal(inputs[0].Bounds.Size, inputs[1].Bounds.Size);
        Assert.Equal(7, inputs[3].Padding.Left, 1);
        Assert.Equal(5, inputs[3].Padding.Top, 1);
    }
}
