using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Graphics;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.Layout;

namespace Broiler.HTML.Dom;

/// <summary>
/// Renderer-side <see cref="ILayoutEnvironment"/> adapter for the layout
/// extraction (see <c>docs/roadmap/broiler-layout-component.md</c> §4, §6). It
/// forwards layout's metric, font, colour, refresh and initial-containing-block
/// requests to the owning <see cref="IHtmlContainerInt"/> and the active
/// <see cref="RGraphics"/> surface, so the layout code depends only on the
/// backend-neutral environment interface.
/// </summary>
/// <remarks>
/// The instance is owned by the container and created when the root box is bound
/// (so it is available before the first layout pass, e.g. for pre-layout font/colour
/// resolution). The graphics surface changes per layout pass and is supplied via
/// <see cref="SetGraphics"/>; only the text-measurement members require it.
/// </remarks>
internal sealed class HtmlLayoutEnvironment(IHtmlContainerInt container) : ILayoutEnvironment
{
    private RGraphics _graphics;

    // CSS default object size for a replaced element with no intrinsic size
    // (CSS Images §5.3 / CSS2 §10.3.2).
    private const double DefaultObjectWidth = 300;
    private const double DefaultObjectHeight = 150;

    /// <summary>Sets the graphics surface for the current layout pass (used by text measurement).</summary>
    public void SetGraphics(RGraphics graphics) => _graphics = graphics;

    public ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null)
        => container.GetFont(family, size, (FontStyle)(int)style, fontFeatures);

    public SizeF MeasureText(ILayoutFont font, string text)
        => _graphics.MeasureString(text, (RFont)font);

    public void MeasureText(ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth)
        => _graphics.MeasureString(text, (RFont)font, maxWidth, out charFit, out charFitWidth);

    public double GetWhitespaceWidth(ILayoutFont font)
        => ((RFont)font).GetWhitespaceWidth(_graphics);

    public ImageIntrinsics GetImageIntrinsics(object imageHandle)
    {
        var image = (RImage)imageHandle;

        // Replaced-element sizing must use the image's *intrinsic* CSS size, not
        // its backing-bitmap size.  They differ for SVGs: the rasterizer may
        // supersample (both-dimension SVGs) or render partial-/ratio-only SVGs
        // at an inflated working resolution, so RImage.Width/Height (the bitmap
        // size) is not the CSS intrinsic size.
        //
        // An <img> only has true intrinsic dimensions when both are known; when
        // either is missing the used object size is the 300×150 default (the
        // partial dimension and any viewBox ratio are ignored for replaced
        // sizing, matching Chromium).  Raster images always report both, so they
        // continue to use their bitmap size unchanged.
        bool bothIntrinsic = image.HasIntrinsicWidth && image.HasIntrinsicHeight;
        double width = bothIntrinsic ? image.IntrinsicWidth : DefaultObjectWidth;
        double height = bothIntrinsic ? image.IntrinsicHeight : DefaultObjectHeight;
        return new ImageIntrinsics(width, height, image.HasIntrinsicRatio);
    }

    public BColor ParseColor(string value) => container.ParseColor(value);

    public void RequestRefresh(bool relayout) => container.RequestRefresh(relayout);

    public SizeF ViewportSize => container.ViewportSize;

    public PointF RootLocation => container.RootLocation;

    public SizeF ActualSize
    {
        get => container.ActualSize;
        set => container.ActualSize = value;
    }

    public bool AvoidGeometryAntialias => container.AvoidGeometryAntialias;

    public SizeF PageSize => container.PageSize;

    public int MarginTop => container.MarginTop;

    public void ReportLayoutError(string message, Exception? exception = null)
        => container.ReportError(HtmlRenderErrorType.Layout, message, exception);

    public bool AvoidAsyncImagesLoading => container.AvoidAsyncImagesLoading;

    public bool AvoidImagesLateLoading => container.AvoidImagesLateLoading;

    public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete)
        => new LayoutImageLoader(container.CreateImageLoadHandler((image, rect, async) => onComplete(image, rect, async)));

    public string FormatListMarker(int number, string style)
        => Broiler.HTML.Utils.CommonUtils.ConvertToAlphaNumber(number, style);

    /// <summary>
    /// Wraps the renderer's <see cref="IImageLoadHandler"/> as a backend-neutral
    /// <see cref="ILayoutImageLoader"/> for the layout code (Phase 4 prep).
    /// </summary>
    private sealed class LayoutImageLoader(IImageLoadHandler handler) : ILayoutImageLoader
    {
        public object? Image => handler.Image;

        public RectangleF Rectangle => handler.Rectangle;

        public void LoadImage(string src, IReadOnlyDictionary<string, string>? attributes, Uri baseUrl)
            => handler.LoadImage(
                src,
                attributes as Dictionary<string, string>
                    ?? (attributes is null ? null : new Dictionary<string, string>(attributes)),
                baseUrl);

        public void Dispose() => handler.Dispose();
    }
}
