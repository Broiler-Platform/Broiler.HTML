using System;
using System.Drawing;
using Broiler.HTML.Image.Adapters;
using Broiler.HTML.Orchestration;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core;
using Broiler.Graphics;

namespace Broiler.HTML.Image;

public static class HtmlRender
{
    /// <summary>Parses CSS into the supported origin-aware renderer style set.</summary>
    public static HtmlStyleSet ParseStyleSet(string stylesheet, bool combineWithDefault = true) =>
        HtmlStyleSet.Parse(stylesheet, combineWithDefault);

    /// <summary>
    /// Parses CSS into the canonical shared model without exposing the legacy
    /// renderer block indexes.
    /// </summary>
    public static CSS.CssStyleSheet ParseStyleSheetModel(
        string stylesheet,
        bool combineWithDefault = true) =>
        ParseStyleSet(stylesheet, combineWithDefault).StyleSheet;

    public static BBitmap RenderToImageWithStyleSet(
        string html,
        int width,
        int height,
        HtmlStyleSet? styleSet = null,
        BColor backgroundColor = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null) =>
        RenderToImageCore(html, width, height, backgroundColor == default ? null : backgroundColor,
            styleSet, stylesheetLoad, imageLoad, baseUrl);

    public static BBitmap RenderToImageAutoSizedWithStyleSet(
        string html,
        HtmlStyleSet? styleSet = null,
        int maxWidth = 0,
        int maxHeight = 0,
        BColor backgroundColor = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null) =>
        RenderToImageAutoSizedCore(html, maxWidth, maxHeight,
            backgroundColor == default ? null : backgroundColor, styleSet, stylesheetLoad, imageLoad, baseUrl);

    public static BBitmap? RenderToImageAtAnchorWithStyleSet(
        string html,
        string elementId,
        int width,
        int height,
        HtmlStyleSet? styleSet = null,
        BColor backgroundColor = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null) =>
        RenderToImageAtAnchorCore(html, elementId, width, height,
            backgroundColor == default ? null : backgroundColor, styleSet, stylesheetLoad, imageLoad, baseUrl);

    public static byte[] RenderToPngWithStyleSet(
        string html,
        int width,
        int height,
        BColor backgroundColor,
        HtmlStyleSet? styleSet = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null) =>
        RenderToPngCore(html, width, height, backgroundColor, styleSet, stylesheetLoad, imageLoad);

    public static void RenderToFileWithStyleSet(
        string html,
        int width,
        int height,
        string filePath,
        Graphics.BImageEncodeFormat format,
        int quality = 90,
        HtmlStyleSet? styleSet = null,
        BColor backgroundColor = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var bitmap = RenderToImageWithStyleSet(
            html, width, height, styleSet, backgroundColor, stylesheetLoad, imageLoad, baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    public static void RenderToFileAutoSizedWithStyleSet(
        string html,
        string filePath,
        HtmlStyleSet? styleSet = null,
        int maxWidth = 0,
        int maxHeight = 0,
        Graphics.BImageEncodeFormat format = Graphics.BImageEncodeFormat.Png,
        int quality = 90,
        BColor backgroundColor = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var bitmap = RenderToImageAutoSizedWithStyleSet(
            html, styleSet, maxWidth, maxHeight, backgroundColor, stylesheetLoad, imageLoad, baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static BBitmap RenderToImage(string html, int width, int height,
        BColor backgroundColor = default,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null,
        string baseUrl = null) =>
        RenderToImageCore(
            html,
            width,
            height,
            backgroundColor == default ? null : backgroundColor,
            GetStyleSet(cssData),
            stylesheetLoad,
            imageLoad,
            baseUrl);

    [Obsolete("Use RenderToImageAutoSizedWithStyleSet.")]
    public static BBitmap RenderToImageAutoSized(string html, int maxWidth = 0, int maxHeight = 0,
        BColor backgroundColor = default,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null,
        string baseUrl = null) =>
        RenderToImageAutoSizedCore(
            html,
            maxWidth,
            maxHeight,
            backgroundColor == default ? null : backgroundColor,
            GetStyleSet(cssData),
            stylesheetLoad,
            imageLoad,
            baseUrl);

    [Obsolete("Use RenderToImageAtAnchorWithStyleSet.")]
    public static BBitmap? RenderToImageAtAnchor(string html, string elementId, int width, int height,
        BColor backgroundColor = default,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null,
        string baseUrl = null) =>
        RenderToImageAtAnchorCore(
            html,
            elementId,
            width,
            height,
            backgroundColor == default ? null : backgroundColor,
            GetStyleSet(cssData),
            stylesheetLoad,
            imageLoad,
            baseUrl);

    [Obsolete("Use RenderToPngWithStyleSet.")]
    public static byte[] RenderToPng(string html, int width, int height,
        BColor backgroundColor,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
        => RenderToPngCore(html, width, height, backgroundColor, GetStyleSet(cssData), stylesheetLoad, imageLoad);

    [Obsolete("Use RenderToFileWithStyleSet.")]
    public static void RenderToFile(string html, int width, int height, string filePath,
        Graphics.BImageEncodeFormat format,
        int quality = 90,
        BColor backgroundColor = default,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null, string baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var bitmap = RenderToImageCore(
            html,
            width,
            height,
            backgroundColor == default ? null : backgroundColor,
            GetStyleSet(cssData),
            stylesheetLoad,
            imageLoad,
            baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    [Obsolete("Use RenderToFileAutoSizedWithStyleSet.")]
    public static void RenderToFileAutoSized(string html, string filePath,
        int maxWidth = 0,
        int maxHeight = 0,
        Graphics.BImageEncodeFormat format = Graphics.BImageEncodeFormat.Png,
        int quality = 90,
        BColor backgroundColor = default,
        CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null,
        string baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var bitmap = RenderToImageAutoSizedCore(
            html,
            maxWidth,
            maxHeight,
            backgroundColor == default ? null : backgroundColor,
            GetStyleSet(cssData),
            stylesheetLoad,
            imageLoad,
            baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    /// <summary>
    /// Loads a TrueType/OpenType font from a file and registers it with
    /// the rendering adapter so that CSS <c>font-family</c> references
    /// using <paramref name="cssName"/> resolve to it.
    /// </summary>
    /// <param name="path">Absolute path to a .ttf or .otf font file.</param>
    /// <param name="cssName">
    /// Optional CSS family name alias.  When provided, the font will be
    /// accessible under this name in addition to its own family name
    /// (e.g. pass <c>"Ahem"</c> for the WPT Ahem test font).
    /// </param>
    /// <returns>
    /// The font's own family name, or <c>null</c> if loading failed.
    /// </returns>
    public static string LoadFontFromFile(string path, string cssName = null)
        => CompatProvider.ImageAdapter.LoadFontFromFile(path, cssName);

    public static bool TryCreatePixelBuffer(object imageHandle, out Broiler.Graphics.BPixelBuffer pixelBuffer)
    {
        if (imageHandle is ImageAdapter imageAdapter)
        {
            pixelBuffer = imageAdapter.Bitmap.ToPixelBuffer();
            return true;
        }

        pixelBuffer = null;
        return false;
    }

#pragma warning disable CS0618
    private static HtmlStyleSet? GetStyleSet(CssData? cssData) => cssData?.StyleSet;
#pragma warning restore CS0618

    private static BBitmap RenderToImageCore(string html, int width, int height,
        BColor? backgroundColor,
        HtmlStyleSet styleSet,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs> imageLoad,
        string baseUrl,
        int embedDepth = 0)
    {
        var bgColor = backgroundColor ?? BColor.White;
        var bitmap = new BBitmap(width, height);

        if (!string.IsNullOrEmpty(html))
        {
            using var container = new HtmlContainer();
            container.Location = new PointF(0, 0);
            container.MaxSize = new SizeF(width, height);
            container.AvoidAsyncImagesLoading = true;
            container.AvoidImagesLateLoading = true;

            if (stylesheetLoad != null)
                container.StylesheetLoad += stylesheetLoad;
            if (imageLoad != null)
                container.ImageLoad += imageLoad;

            container.SetHtmlWithStyleSet(html, styleSet, baseUrl);

            if (backgroundColor is null)
                bgColor = ResolveCanvasBackground(container, bgColor);

            bitmap.Erase(bgColor);

            var clip = new RectangleF(0, 0, width, height);
            container.PerformLayout(bitmap, clip);
            container.PerformPaint(bitmap, clip);

            // Nested browsing contexts (<object type="text/html">, <iframe>,
            // <frame>): rasterise each embedded document at its content-box size
            // and composite it over the box.  Depth-bounded to stop a document
            // that (transitively) embeds itself.
            if (embedDepth < MaxEmbeddedDocumentDepth && container.LatestFragmentTree is { } tree)
                CompositeEmbeddedDocuments(tree, bitmap, stylesheetLoad, imageLoad, embedDepth);
        }
        else
        {
            bitmap.Erase(bgColor);
        }

        return bitmap;
    }

    private const int MaxEmbeddedDocumentDepth = 4;

    /// <summary>
    /// Walks the laid-out fragment tree and composites every embedded document
    /// (<see cref="Layout.IR.Fragment.EmbeddedDocumentHtml"/>) over its box by
    /// recursively rendering it at the box's content-box size.
    /// </summary>
    private static void CompositeEmbeddedDocuments(
        Layout.IR.Fragment fragment,
        BBitmap target,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs> imageLoad,
        int embedDepth)
    {
        if (!string.IsNullOrEmpty(fragment.EmbeddedDocumentHtml))
        {
            var border = fragment.Border;
            var padding = fragment.Padding;

            // Content box = border box minus border and padding (mirrors the
            // replaced-image destination rect in PaintWalker.EmitReplacedImage).
            int dx = (int)Math.Round(fragment.Location.X + (float)(border.Left + padding.Left));
            int dy = (int)Math.Round(fragment.Location.Y + (float)(border.Top + padding.Top));
            int dw = (int)Math.Round(fragment.Size.Width
                - (float)(border.Left + border.Right + padding.Left + padding.Right));
            int dh = (int)Math.Round(fragment.Size.Height
                - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom));

            if (dw > 0 && dh > 0)
            {
                using var sub = RenderToImageCore(
                    fragment.EmbeddedDocumentHtml, dw, dh,
                    backgroundColor: null,          // resolve the embedded document's own canvas background
                    styleSet: null,                 // the embedded document parses its own styles
                    stylesheetLoad, imageLoad,
                    fragment.EmbeddedDocumentBaseUrl,
                    embedDepth + 1);
                BlitOnto(target, sub, dx, dy);
            }
        }

        foreach (var child in fragment.Children)
            CompositeEmbeddedDocuments(child, target, stylesheetLoad, imageLoad, embedDepth);
    }

    /// <summary>Copies <paramref name="source"/> onto <paramref name="target"/> at
    /// (<paramref name="destX"/>, <paramref name="destY"/>), clipped to the target.</summary>
    private static void BlitOnto(BBitmap target, BBitmap source, int destX, int destY)
    {
        for (int y = 0; y < source.Height; y++)
        {
            int ty = destY + y;
            if ((uint)ty >= (uint)target.Height)
                continue;
            for (int x = 0; x < source.Width; x++)
            {
                int tx = destX + x;
                if ((uint)tx >= (uint)target.Width)
                    continue;
                target.SetPixel(tx, ty, source.GetPixel(x, y));
            }
        }
    }

    private static BBitmap RenderToImageAutoSizedCore(string html, int maxWidth, int maxHeight,
        BColor? backgroundColor,
        HtmlStyleSet styleSet,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs> imageLoad,
        string baseUrl)
    {
        if (string.IsNullOrEmpty(html))
            return new BBitmap(1, 1);

        var bgColor = backgroundColor ?? BColor.White;

        using var container = new HtmlContainer();
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        container.SetHtmlWithStyleSet(html, styleSet, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        var minSize = new SizeF(0, 0);
        var maxSize = new SizeF(maxWidth, maxHeight);
        var finalSize = MeasureHtml(container, minSize, maxSize);

        int w = Math.Max(1, (int)Math.Ceiling(finalSize.Width));
        int h = Math.Max(1, (int)Math.Ceiling(finalSize.Height));

        if (maxWidth < 1 && w > 4096)
            w = 4096;

        if (maxHeight > 0 && h > maxHeight)
            h = maxHeight;

        container.MaxSize = new SizeF(w, h);

        var bitmap = new BBitmap(w, h);
        bitmap.Erase(bgColor);

        var clip = new RectangleF(0, 0, w, h);
        container.PerformPaint(bitmap, clip);

        return bitmap;
    }

    private static byte[] RenderToPngCore(string html, int width, int height,
        BColor? backgroundColor,
        HtmlStyleSet styleSet,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs> imageLoad)
    {
        using var bitmap = RenderToImageCore(html, width, height, backgroundColor, styleSet, stylesheetLoad, imageLoad, null);
        return bitmap.Encode(Graphics.BImageEncodeFormat.Png, 100);
    }

    private static BBitmap? RenderToImageAtAnchorCore(string html, string elementId, int width, int height,
        BColor? backgroundColor,
        HtmlStyleSet styleSet,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs> imageLoad,
        string baseUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        if (string.IsNullOrEmpty(html))
            return null;

        const int LayoutMaxHeight = 99999;
        const int LayoutBitmapHeight = 2000;

        var bgColor = backgroundColor ?? BColor.White;

        using var container = new HtmlContainer();
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        container.MaxSize = new SizeF(width, LayoutMaxHeight);

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        container.SetHtmlWithStyleSet(html, styleSet, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        using var layoutBitmap = new BBitmap(width, LayoutBitmapHeight);
        layoutBitmap.Erase(bgColor);
        container.PerformLayout(layoutBitmap, new RectangleF(0, 0, width, LayoutMaxHeight));

        var anchorRect = container.GetElementRectangle(elementId);
        if (anchorRect is null)
            return null;

        float scrollY = anchorRect.Value.Y;
        container.Location = new PointF(0, scrollY);
        container.MaxSize = new SizeF(width, height);

        var bitmap = new BBitmap(width, height);
        bitmap.Erase(bgColor);
        container.PerformPaint(bitmap, new RectangleF(0, scrollY, width, height), new PointF(0, -scrollY));

        return bitmap;
    }

    private static SizeF MeasureHtml(HtmlContainer container, SizeF minSize, SizeF maxSize)
    {
        // Create a small temporary surface for measurement
        using var measureBitmap = new BBitmap(1, 1);
        var clip = new RectangleF(0, 0, 99999, 99999);

        using var g = measureBitmap.OpenGraphics(clip);
        return HtmlRendererUtils.MeasureHtmlByRestrictions(g, container.HtmlContainerInt, minSize, maxSize);
    }

    /// <summary>
    /// Reads the root element's computed background color from the container.
    /// Returns the supplied
    /// <paramref name="fallback"/> when the root has no explicit background.
    /// </summary>
    private static BColor ResolveCanvasBackground(HtmlContainer container, BColor fallback)
    {
        var rootBg = container.GetRootBackgroundColor();
        if (!rootBg.IsEmpty && rootBg.A > 0)
            return new BColor(rootBg.R, rootBg.G, rootBg.B, rootBg.A);

        return fallback;
    }
}
