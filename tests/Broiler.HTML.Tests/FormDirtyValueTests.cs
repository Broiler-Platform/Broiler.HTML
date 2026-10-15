using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// What the user types into a field is its dirty value (HTML §4.10.5.4, §4.10.11), held in the container's
/// <see cref="HtmlContainer.FormState"/>. The element's <c>value</c> attribute and a textarea's text are its
/// default value, and typing leaves them alone. The rendering shows the dirty value, keeps it across a
/// restyle, and shows one a host recorded in a form state it shares.
/// </summary>
/// <remarks>
/// <see cref="HtmlContainer.SetEditableInputValueAtDocumentPoint"/> wrote the typed text into the element's
/// <c>value</c> attribute, or replaced a textarea's text, so the document's own <c>[value]</c> selectors, a
/// form reset and a script's <c>getAttribute</c> all read what the user had typed.
/// </remarks>
public sealed class FormDirtyValueTests
{
    private const string Page =
        "<!DOCTYPE html><html><body style=\"margin: 0\">"
        + "<input id=\"field\" value=\"default\" style=\"position: absolute; left: 0; top: 0; width: 150px\">"
        + "<textarea id=\"area\" style=\"position: absolute; left: 0; top: 40px; width: 150px; height: 40px\">default text</textarea>"
        + "</body></html>";

    [Fact]
    public void Typing_Into_An_Input_Sets_Its_Dirty_Value_And_Leaves_The_Attribute()
    {
        var (container, document) = Render();
        using (container)
        {
            var field = document.GetElementById("field")!;

            Assert.True(container.SetEditableInputValueAtDocumentPoint(Inside(container, "field"), "typed"));
            container.PerformLayout();

            Assert.Equal("default", field.GetAttribute("value"));
            Assert.Equal("typed", container.FormState.GetEffectiveValue(field));
            Assert.Equal("typed", container.GetEditableInputAt(Inside(container, "field"))!.Value);
            Assert.Contains("typed", Text(container));
        }
    }

    [Fact]
    public void Typing_Into_A_Textarea_Sets_Its_Dirty_Value_And_Leaves_Its_Text()
    {
        var (container, document) = Render();
        using (container)
        {
            var area = document.GetElementById("area")!;

            Assert.True(container.SetEditableInputValueAtDocumentPoint(Inside(container, "area"), "typed text"));
            container.PerformLayout();

            Assert.Equal("default text", area.TextContent);
            Assert.Equal("typed text", container.FormState.GetEffectiveValue(area));
            Assert.Contains("typedtext", Text(container));
        }
    }

    [Fact]
    public void A_Typed_Value_Survives_A_Restyle()
    {
        var (container, _) = Render();
        using (container)
        {
            container.SetEditableInputValueAtDocumentPoint(Inside(container, "field"), "typed");
            container.SetEditableInputValueAtDocumentPoint(Inside(container, "area"), "typed text");

            container.RestyleDocument();
            container.PerformLayout();

            var text = Text(container);
            Assert.Contains("typed", text);
            Assert.Contains("typedtext", text);
            Assert.DoesNotContain("default", text);
            Assert.Equal("typed", container.GetEditableInputAt(Inside(container, "field"))!.Value);
        }
    }

    /// <summary>The renderer's own export writes what is on screen: the typed value.</summary>
    [Fact]
    public void The_Export_Writes_The_Typed_Value()
    {
        var (container, _) = Render();
        using (container)
        {
            container.SetEditableInputValueAtDocumentPoint(Inside(container, "field"), "typed");

            Assert.Contains("value=\"typed\"", container.GetHtml(HtmlGenerationStyle.None));
        }
    }

    [Fact]
    public void A_Hosts_Form_State_Is_Shown()
    {
        var document = HtmlDocumentParser.ParseDocument(Page).Document;
        var formState = new HtmlFormState();
        formState.SetDirtyValue(document.GetElementById("field")!, "from host");
        formState.SetDirtyValue(document.GetElementById("area")!, "host text");

        using var container = Container();
        container.FormState = formState;
        container.SetDocumentWithStyleSet(document, null, "about:blank");
        container.PerformLayout();

        var text = Text(container);
        Assert.Contains("fromhost", text);
        Assert.Contains("hosttext", text);
        Assert.DoesNotContain("default", text);
    }

    private static (HtmlContainer Container, DomDocument Document) Render()
    {
        var document = HtmlDocumentParser.ParseDocument(Page).Document;
        var container = Container();
        container.SetDocumentWithStyleSet(document, null, "about:blank");
        container.PerformLayout();
        return (container, document);
    }

    private static HtmlContainer Container() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };

    private static PointF Inside(HtmlContainer container, string id)
    {
        var rect = container.GetElementRectangle(id) ?? throw new InvalidOperationException($"No #{id}.");
        return new PointF(rect.X + 5, rect.Y + 5);
    }

    /// <summary>The rendered text, without the spaces between words, which the inline runs do not carry.</summary>
    private static string Text(HtmlContainer container) =>
        string.Concat(Flatten(container.LatestFragmentTree!).SelectMany(static f => f.Lines ?? []).SelectMany(static l => l.Inlines).Select(static i => i.Text)).Replace(" ", string.Empty);

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
