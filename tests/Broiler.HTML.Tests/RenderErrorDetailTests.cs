using System.Drawing;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.IR;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A <see cref="HtmlContainer.RenderError"/> says what failed and why: the reporters inside the
/// renderer name the resource or the step and pass the exception, and the event carries both to the
/// host instead of its type alone.
/// </summary>
public sealed class RenderErrorDetailTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "broiler-render-errors-" + Guid.NewGuid().ToString("N"));

    public RenderErrorDetailTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_Missing_Local_Stylesheet_Is_Named_In_Its_Error()
    {
        var page = Path.Combine(_directory, "page.html");
        var errors = Render(
            "<html><head><link rel=\"stylesheet\" href=\"missing-sheet-marker.css\"></head><body><p>x</p></body></html>",
            new Uri(page).AbsoluteUri);

        var error = Assert.Single(errors, static e => e.Type == HtmlRenderErrorType.CssParsing);
        Assert.NotNull(error.Message);
        Assert.Contains("missing-sheet-marker.css", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Throwing_Image_Handler_Is_Reported_With_Its_Exception()
    {
        var thrown = new InvalidOperationException("image-handler-marker");
        using var container = NewContainer();
        container.ImageLoad += (_, _) => throw thrown;

        var errors = Render(container, "<html><body><img src=\"image-marker.png\"></body></html>", new Uri(Path.Combine(_directory, "page.html")).AbsoluteUri);

        var error = Assert.Single(errors, static e => e.Exception is InvalidOperationException);
        Assert.Equal(HtmlRenderErrorType.Image, error.Type);
        Assert.Same(thrown, error.Exception);
        Assert.Contains("image-marker.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Type_Only_Constructor_Still_Describes_An_Error()
    {
        var error = new HtmlRenderErrorEventArgs(HtmlRenderErrorType.Paint);

        Assert.Null(error.Message);
        Assert.Null(error.Exception);
        Assert.Equal("Type: Paint", error.ToString());
    }

    [Fact]
    public void A_Box_Tree_Deeper_Than_The_Serializers_Default_Limit_Dumps()
    {
        // Two JSON levels per box: 100 boxes is three times what the default limit of 64 allows.
        var fragment = new Fragment { Size = new SizeF(10, 10), Style = new ComputedStyle { TagName = "leaf-marker" } };
        for (var depth = 0; depth < 100; depth++)
            fragment = new Fragment { Size = new SizeF(10, 10), Children = [fragment], Style = new ComputedStyle { TagName = "div" } };

        var json = ComputedStyleJsonDumper.ToJson(fragment);

        Assert.Contains("leaf-marker", json, StringComparison.Ordinal);
    }

    private static HtmlContainer NewContainer() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
    };

    private static List<HtmlRenderErrorEventArgs> Render(string html, string baseUrl)
    {
        using var container = NewContainer();
        return Render(container, html, baseUrl);
    }

    private static List<HtmlRenderErrorEventArgs> Render(HtmlContainer container, string html, string baseUrl)
    {
        var errors = new List<HtmlRenderErrorEventArgs>();
        void OnError(object? sender, HtmlRenderErrorEventArgs e)
        {
            lock (errors)
                errors.Add(e);
        }

        container.RenderError += OnError;
        try
        {
            container.SetHtmlWithStyleSet(html, null, baseUrl);
            container.PerformLayout();
        }
        finally
        {
            container.RenderError -= OnError;
        }

        return errors;
    }
}
