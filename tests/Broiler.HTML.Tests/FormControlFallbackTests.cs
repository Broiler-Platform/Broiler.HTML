using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// The renderer's own form-control policies, which the shared DOM form queries describe but do not supply:
/// the text an <c>&lt;input&gt;</c> shows, which inputs draw as native toggles, and how many rows a
/// <c>&lt;select multiple&gt;</c> list box shows.
/// </summary>
/// <remarks>
/// <para>
/// <c>HtmlFormQueries.GetDefaultValue</c> reflects the markup's <c>value</c> and returns an empty string when
/// there is none, so it cannot tell a submit button without a value (labelled "Submit") from one with
/// <c>value=""</c> (blank). The input text stays the renderer's.
/// </para>
/// <para>
/// <c>HtmlSelectQueries.GetSize</c> reports a missing, malformed, zero, negative or fractional <c>size</c> as
/// 0, so the list box reads <c>size</c> with <c>HtmlElementQueries.ReadNumericAttribute</c> and its own
/// fallback of four rows, clamped to two through eight.
/// </para>
/// </remarks>
public sealed class FormControlFallbackTests
{
    [Theory]
    [InlineData("<input type=\"submit\">", "Submit")]
    [InlineData("<input type=\"reset\">", "Reset")]
    [InlineData("<input type=\"SUBMIT\">", "Submit")]
    [InlineData("<input type=\"submit\" value=\"Go\">", "Go")]
    [InlineData("<input type=\"submit\" value=\"\">", "")]
    [InlineData("<input type=\"button\">", "")]
    [InlineData("<input value=\"typed\">", "typed")]
    [InlineData("<input type=\"search\" value=\"query\">", "query")]
    [InlineData("<input type=\"checkbox\" value=\"on\">", "")]
    [InlineData("<input type=\"hidden\" value=\"secret\">", "")]
    public void An_Input_Shows_Its_Value_Or_Its_Default_Label(string input, string expected)
    {
        var tree = Layout(input);

        Assert.Equal(expected, Text(Inputs(tree).Single()));
    }

    /// <summary>
    /// A native toggle drops its padding. The type matches ASCII case-insensitively and untrimmed, as the
    /// user-agent sheet's <c>input[type="radio"]</c> does: a type with spaces around it is a text field.
    /// </summary>
    [Theory]
    [InlineData("radio", 0)]
    [InlineData("CheckBox", 0)]
    [InlineData(" radio ", 5)]
    public void Only_A_Radio_Or_Checkbox_Type_Is_A_Native_Toggle(string type, double padding)
    {
        var tree = Layout($"<input type=\"{type}\" style=\"padding: 5px\">");

        Assert.Equal(padding, Inputs(tree).Single().Padding.Left, 0.5);
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData(" size=\"3\"", 3)]
    [InlineData(" size=\"20\"", 8)]
    [InlineData(" size=\"0\"", 2)]
    [InlineData(" size=\"2.5\"", 2)]
    [InlineData(" size=\"-4\"", 2)]
    [InlineData(" size=\"abc\"", 4)]
    public void A_Multiple_Select_Shows_Its_Size_In_Rows(string size, int rows)
    {
        var tree = Layout($"<select multiple{size}><option>a</option><option>b</option></select>");

        var select = Flatten(tree).First(static f => f.Style.TagName == "select");
        // Sixteen pixels a row, and four of chrome (CorrectSelectMultipleBoxes).
        Assert.Equal(rows * 16 + 4, select.Size.Height, 0.5);
    }

    private static Fragment Layout(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "</body></html>", null, "about:blank");
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    private static IEnumerable<Fragment> Inputs(Fragment tree) =>
        Flatten(tree).Where(static f => f.Style.TagName == "input");

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
        string.Concat(Flatten(fragment).SelectMany(static f => f.Lines ?? []).SelectMany(static l => l.Inlines).Select(static i => i.Text)).Trim();
}
