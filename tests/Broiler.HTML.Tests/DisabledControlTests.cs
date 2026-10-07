using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// Disabled controls are drawn as Chromium draws them: a text field's text rgb(84, 84, 84) and a
/// button's rgba(16, 16, 16, 0.3), both on rgba(239, 239, 239, 0.3). A control in a disabled fieldset
/// is disabled too, except in the fieldset's first legend. Measured in Chromium.
/// </summary>
/// <remarks>
/// They were drawn like live ones, black on white: the disabled fields of reCAPTCHA's demo,
/// google.com/recaptcha/api2/demo, which Chromium greys out.
/// </remarks>
public sealed class DisabledControlTests
{
    private const string PageUrl = "https://example.test/page";

    private static readonly BColor FieldText = BColor.FromArgb(255, 84, 84, 84);
    private static readonly BColor ButtonText = BColor.FromArgb(76, 16, 16, 16);
    private static readonly BColor DisabledField = BColor.FromArgb(76, 239, 239, 239);

    private static List<DisplayItem> Paint(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body>" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        return [.. container.CreateDisplayList().Items];
    }

    private static DrawTextItem Text(List<DisplayItem> items, string text) =>
        items.OfType<DrawTextItem>().Single(item => item.Text == text);

    /// <summary>The fill drawn last under <paramref name="text"/>: its control's background.</summary>
    private static BColor FieldUnder(List<DisplayItem> items, DrawTextItem text) =>
        items.OfType<FillRectItem>().Last(fill => fill.Bounds.Contains(text.Origin)).Color;

    [Fact]
    public void A_Disabled_Field_And_Button_Are_Greyed()
    {
        var items = Paint(
            "<input value=\"off\" disabled> <input value=\"on\"> <button disabled>press</button>");

        var off = Text(items, "off");
        Assert.Equal(FieldText, off.Color);
        Assert.Equal(DisabledField, FieldUnder(items, off));
        Assert.Equal(BColor.FromArgb(255, 0, 0, 0), Text(items, "on").Color);

        var press = Text(items, "press");
        Assert.Equal(ButtonText, press.Color);
        Assert.Equal(DisabledField, FieldUnder(items, press));
    }

    [Fact]
    public void A_Disabled_Fieldset_Disables_All_But_Its_First_Legend()
    {
        var items = Paint(
            "<fieldset disabled><legend><input value=\"legend\"></legend><input value=\"inside\"></fieldset>");

        Assert.Equal(BColor.FromArgb(255, 0, 0, 0), Text(items, "legend").Color);
        Assert.Equal(FieldText, Text(items, "inside").Color);
    }
}
