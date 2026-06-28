using System;
using System.Drawing;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using GraphicsBitmap = Broiler.Graphics.BBitmap;
using GraphicsColor = Broiler.Graphics.BColor;
using GraphicsFormat = Broiler.Graphics.BImageEncodeFormat;
using ImageBitmap = Broiler.HTML.Image.BBitmap;
using ImageColor = Broiler.HTML.Image.BColor;
using ImageContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.HTML.Graphics;

/// <summary>
/// Broiler.Graphics based HTML rendering facade.
/// </summary>
public static class HtmlRender
{
    public static void AddFontFamily(string fontFamily) =>
        Broiler.HTML.Image.HtmlRender.AddFontFamily(fontFamily);

    public static void AddFontFamilyMapping(string fromFamily, string toFamily) =>
        Broiler.HTML.Image.HtmlRender.AddFontFamilyMapping(fromFamily, toFamily);

    [Obsolete("Use ParseStyleSet or ParseStyleSheetModel.")]
    public static CssData ParseStyleSheet(string stylesheet, bool combineWithDefault = true) =>
        Broiler.HTML.Image.HtmlRender.ParseStyleSheet(stylesheet, combineWithDefault);

    public static HtmlStyleSet ParseStyleSet(string stylesheet, bool combineWithDefault = true) =>
        Broiler.HTML.Image.HtmlRender.ParseStyleSet(stylesheet, combineWithDefault);

    public static Broiler.CSS.CssStyleSheet ParseStyleSheetModel(
        string stylesheet,
        bool combineWithDefault = true) =>
        Broiler.HTML.Image.HtmlRender.ParseStyleSheetModel(stylesheet, combineWithDefault);

    public static string? LoadFontFromFile(string path, string? cssName = null) =>
        Broiler.HTML.Image.HtmlRender.LoadFontFromFile(path, cssName);

    public static SizeF MeasureWithStyleSet(
        string html,
        HtmlStyleSet? styleSet = null,
        float maxWidth = 0,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        using var container = CreateContainer(stylesheetLoad, imageLoad);
        container.MaxSize = new SizeF(maxWidth, 0);
        container.SetHtmlWithStyleSet(html, styleSet, baseUrl);
        container.PerformLayout(new RectangleF(0, 0, maxWidth > 0 ? maxWidth : 99999, 99999));
        return container.ActualSize;
    }

    public static SizeF RenderWithStyleSet(
        GraphicsBitmap bitmap,
        string html,
        HtmlStyleSet? styleSet = null,
        PointF location = default,
        SizeF maxSize = default,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using ImageBitmap image = ToImageBitmap(bitmap);
        var actualSize = Broiler.HTML.Image.HtmlRender.RenderWithStyleSet(
            image, html, styleSet, location, maxSize, stylesheetLoad, imageLoad, baseUrl);
        CopyToGraphicsBitmap(image, bitmap);
        return actualSize;
    }

    public static GraphicsBitmap RenderToImageWithStyleSet(
        string html,
        int width,
        int height,
        HtmlStyleSet? styleSet = null,
        GraphicsColor? backgroundColor = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ValidateDimensions(width, height);
        using var image = Broiler.HTML.Image.HtmlRender.RenderToImageWithStyleSet(
            html, width, height, styleSet,
            backgroundColor.HasValue ? ToImageColor(backgroundColor.Value) : default,
            stylesheetLoad, imageLoad, baseUrl);
        return ToGraphicsBitmap(image);
    }

    public static GraphicsBitmap RenderToImageAutoSizedWithStyleSet(
        string html,
        HtmlStyleSet? styleSet = null,
        int maxWidth = 0,
        int maxHeight = 0,
        GraphicsColor? backgroundColor = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        using var image = Broiler.HTML.Image.HtmlRender.RenderToImageAutoSizedWithStyleSet(
            html, styleSet, maxWidth, maxHeight,
            backgroundColor.HasValue ? ToImageColor(backgroundColor.Value) : default,
            stylesheetLoad, imageLoad, baseUrl);
        return ToGraphicsBitmap(image);
    }

    [Obsolete("Use MeasureWithStyleSet.")]
    public static SizeF Measure(
        string html,
        float maxWidth = 0,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        if (string.IsNullOrEmpty(html))
            return SizeF.Empty;

        using var container = CreateContainer(stylesheetLoad, imageLoad);
        container.MaxSize = new SizeF(maxWidth, 0);
        container.SetHtml(html, cssData, baseUrl);
        container.PerformLayout(new RectangleF(0, 0, maxWidth > 0 ? maxWidth : 99999, 99999));
        return container.ActualSize;
    }

    [Obsolete("Use RenderWithStyleSet.")]
    public static SizeF Render(
        GraphicsBitmap bitmap,
        string html,
        float left = 0,
        float top = 0,
        float maxWidth = 0,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return Render(bitmap, html, new PointF(left, top), new SizeF(maxWidth, 0), cssData, stylesheetLoad, imageLoad, baseUrl);
    }

    [Obsolete("Use RenderWithStyleSet.")]
    public static SizeF Render(
        GraphicsBitmap bitmap,
        string html,
        PointF location,
        SizeF maxSize,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using ImageBitmap image = ToImageBitmap(bitmap);
        SizeF actualSize = Broiler.HTML.Image.HtmlRender.Render(
            image,
            html,
            location,
            maxSize,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        CopyToGraphicsBitmap(image, bitmap);
        return actualSize;
    }

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static GraphicsBitmap RenderToImage(
        string html,
        Size size,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        if (size.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        return RenderToImage(html, size.Width, size.Height, null, cssData, stylesheetLoad, imageLoad, baseUrl);
    }

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static GraphicsBitmap RenderToImage(
        string html,
        int width,
        int height,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ValidateDimensions(width, height);

        using ImageBitmap image = RenderToImageCore(
            html,
            width,
            height,
            backgroundColor.HasValue ? ToImageColor(backgroundColor.Value) : null,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        return ToGraphicsBitmap(image);
    }

    [Obsolete("Use RenderToImageAutoSizedWithStyleSet.")]
    public static GraphicsBitmap RenderToImageAutoSized(
        string html,
        int maxWidth = 0,
        int maxHeight = 0,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        using ImageBitmap image = RenderToImageAutoSizedCore(
            html,
            maxWidth,
            maxHeight,
            backgroundColor.HasValue ? ToImageColor(backgroundColor.Value) : null,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        return ToGraphicsBitmap(image);
    }

    public static GraphicsBitmap RenderPipelineToImage(
        Broiler.Graphics.IBroilerRenderer renderer,
        string html,
        int width,
        int height,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ValidateDimensions(width, height);

        GraphicsColor bgColor = backgroundColor ?? GraphicsColor.White;
        var list = new Broiler.Graphics.BRenderList();

        if (!string.IsNullOrEmpty(html))
        {
            using var container = CreatePipelineContainer(stylesheetLoad, imageLoad);
            var clip = new RectangleF(0, 0, width, height);
            container.Location = PointF.Empty;
            container.MaxSize = clip.Size;
            container.SetHtml(html, cssData, baseUrl);

            if (backgroundColor is null)
                bgColor = ResolveCanvasBackground(container, bgColor);

            container.PerformLayout(clip);
            using HtmlGraphicsRenderList renderList = container.CreateRenderList(renderer, clip);
            return renderer.RenderToImage(
                renderList.RenderList,
                CreatePipelineSurfaceDescriptor(width, height),
                new Broiler.Graphics.BFrameContext(bgColor));
        }

        return renderer.RenderToImage(
            list,
            CreatePipelineSurfaceDescriptor(width, height),
            new Broiler.Graphics.BFrameContext(bgColor));
    }

    public static GraphicsBitmap RenderPipelineToImageAutoSized(
        Broiler.Graphics.IBroilerRenderer renderer,
        string html,
        int maxWidth = 0,
        int maxHeight = 0,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        GraphicsColor bgColor = backgroundColor ?? GraphicsColor.White;
        if (string.IsNullOrEmpty(html))
        {
            return renderer.RenderToImage(
                new Broiler.Graphics.BRenderList(),
                CreatePipelineSurfaceDescriptor(1, 1),
                new Broiler.Graphics.BFrameContext(bgColor));
        }

        using var container = CreatePipelineContainer(stylesheetLoad, imageLoad);
        container.SetHtml(html, cssData, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        float measureWidth = maxWidth > 0 ? maxWidth : 99999;
        float measureHeight = maxHeight > 0 ? maxHeight : 99999;
        container.MaxSize = new SizeF(maxWidth, maxHeight);
        container.PerformLayout(new RectangleF(0, 0, measureWidth, measureHeight));

        int width = Math.Max(1, (int)Math.Ceiling(container.ActualSize.Width));
        int height = Math.Max(1, (int)Math.Ceiling(container.ActualSize.Height));

        if (maxWidth < 1 && width > 4096)
            width = 4096;
        if (maxWidth > 0 && width > maxWidth)
            width = maxWidth;
        if (maxHeight > 0 && height > maxHeight)
            height = maxHeight;

        var clip = new RectangleF(0, 0, width, height);
        container.MaxSize = clip.Size;
        using HtmlGraphicsRenderList renderList = container.CreateRenderList(renderer, clip);
        return renderer.RenderToImage(
            renderList.RenderList,
            CreatePipelineSurfaceDescriptor(width, height),
            new Broiler.Graphics.BFrameContext(bgColor));
    }

    public static GraphicsBitmap? RenderToImageAtAnchor(
        string html,
        string elementId,
        int width,
        int height,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ValidateDimensions(width, height);
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        using ImageBitmap? image = RenderToImageAtAnchorCore(
            html,
            elementId,
            width,
            height,
            backgroundColor.HasValue ? ToImageColor(backgroundColor.Value) : null,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        return image is null ? null : ToGraphicsBitmap(image);
    }

    public static byte[] RenderToPng(
        string html,
        int width,
        int height,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        using GraphicsBitmap bitmap = RenderToImage(html, width, height, backgroundColor, cssData, stylesheetLoad, imageLoad, baseUrl);
        return bitmap.Encode(GraphicsFormat.Png, 100);
    }

    public static void RenderToFile(
        string html,
        int width,
        int height,
        string filePath,
        GraphicsFormat format = GraphicsFormat.Png,
        int quality = 90,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using GraphicsBitmap bitmap = RenderToImage(html, width, height, backgroundColor, cssData, stylesheetLoad, imageLoad, baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    public static void RenderToFileAutoSized(
        string html,
        string filePath,
        int maxWidth = 0,
        int maxHeight = 0,
        GraphicsFormat format = GraphicsFormat.Png,
        int quality = 90,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using GraphicsBitmap bitmap = RenderToImageAutoSized(html, maxWidth, maxHeight, backgroundColor, cssData, stylesheetLoad, imageLoad, baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    public static void RenderPipelineToFile(
        Broiler.Graphics.IBroilerRenderer renderer,
        string html,
        int width,
        int height,
        string filePath,
        GraphicsFormat format = GraphicsFormat.Png,
        int quality = 90,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using GraphicsBitmap bitmap = RenderPipelineToImage(
            renderer,
            html,
            width,
            height,
            backgroundColor,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    public static void RenderPipelineToFileAutoSized(
        Broiler.Graphics.IBroilerRenderer renderer,
        string html,
        string filePath,
        int maxWidth = 0,
        int maxHeight = 0,
        GraphicsFormat format = GraphicsFormat.Png,
        int quality = 90,
        GraphicsColor? backgroundColor = null,
        CssData? cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad = null,
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using GraphicsBitmap bitmap = RenderPipelineToImageAutoSized(
            renderer,
            html,
            maxWidth,
            maxHeight,
            backgroundColor,
            cssData,
            stylesheetLoad,
            imageLoad,
            baseUrl);
        bitmap.Save(filePath, format, quality);
    }

    internal static GraphicsBitmap ToGraphicsBitmap(ImageBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pixels = new byte[checked(source.Width * source.Height * 4)];
        int offset = 0;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                ImageColor pixel = source.GetPixel(x, y);
                pixels[offset++] = pixel.R;
                pixels[offset++] = pixel.G;
                pixels[offset++] = pixel.B;
                pixels[offset++] = pixel.A;
            }
        }

        return new GraphicsBitmap(source.Width, source.Height, pixels, takeOwnership: true);
    }

    internal static ImageBitmap ToImageBitmap(GraphicsBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var image = new ImageBitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                GraphicsColor pixel = source.GetPixel(x, y);
                image.SetPixel(x, y, ToImageColor(pixel));
            }
        }

        return image;
    }

    internal static void CopyToGraphicsBitmap(ImageBitmap source, GraphicsBitmap destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width != destination.Width || source.Height != destination.Height)
            throw new ArgumentException("Source and destination bitmap dimensions must match.", nameof(destination));

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                ImageColor pixel = source.GetPixel(x, y);
                destination.SetPixel(x, y, new GraphicsColor(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }
    }

    private static ImageBitmap RenderToImageCore(
        string html,
        int width,
        int height,
        ImageColor? backgroundColor,
        CssData? cssData,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad,
        string? baseUrl)
    {
        ImageColor bgColor = backgroundColor ?? ImageColor.White;
        var bitmap = new ImageBitmap(width, height);

        if (string.IsNullOrEmpty(html))
        {
            bitmap.Clear(bgColor);
            return bitmap;
        }

        using var container = CreateContainer(stylesheetLoad, imageLoad);
        container.Location = new PointF(0, 0);
        container.MaxSize = new SizeF(width, height);
        container.SetHtml(html, cssData, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        bitmap.Clear(bgColor);

        var clip = new RectangleF(0, 0, width, height);
        container.PerformLayout(bitmap, clip);
        container.PerformPaint(bitmap, clip);
        return bitmap;
    }

    private static ImageBitmap RenderToImageAutoSizedCore(
        string html,
        int maxWidth,
        int maxHeight,
        ImageColor? backgroundColor,
        CssData? cssData,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad,
        string? baseUrl)
    {
        if (string.IsNullOrEmpty(html))
            return new ImageBitmap(1, 1);

        ImageColor bgColor = backgroundColor ?? ImageColor.White;
        using var container = CreateContainer(stylesheetLoad, imageLoad);
        container.SetHtml(html, cssData, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        float measureWidth = maxWidth > 0 ? maxWidth : 99999;
        float measureHeight = maxHeight > 0 ? maxHeight : 99999;
        container.MaxSize = new SizeF(maxWidth, maxHeight);
        container.PerformLayout(new RectangleF(0, 0, measureWidth, measureHeight));

        int width = Math.Max(1, (int)Math.Ceiling(container.ActualSize.Width));
        int height = Math.Max(1, (int)Math.Ceiling(container.ActualSize.Height));

        if (maxWidth < 1 && width > 4096)
            width = 4096;
        if (maxWidth > 0 && width > maxWidth)
            width = maxWidth;
        if (maxHeight > 0 && height > maxHeight)
            height = maxHeight;

        container.MaxSize = new SizeF(width, height);

        var bitmap = new ImageBitmap(width, height);
        bitmap.Clear(bgColor);
        container.PerformPaint(bitmap, new RectangleF(0, 0, width, height));
        return bitmap;
    }

    private static ImageBitmap? RenderToImageAtAnchorCore(
        string html,
        string elementId,
        int width,
        int height,
        ImageColor? backgroundColor,
        CssData? cssData,
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad,
        string? baseUrl)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        const int layoutMaxHeight = 99999;
        const int layoutBitmapHeight = 2000;

        ImageColor bgColor = backgroundColor ?? ImageColor.White;
        using var container = CreateContainer(stylesheetLoad, imageLoad);
        container.MaxSize = new SizeF(width, layoutMaxHeight);
        container.SetHtml(html, cssData, baseUrl);

        if (backgroundColor is null)
            bgColor = ResolveCanvasBackground(container, bgColor);

        using var layoutBitmap = new ImageBitmap(width, layoutBitmapHeight);
        layoutBitmap.Clear(bgColor);
        container.PerformLayout(layoutBitmap, new RectangleF(0, 0, width, layoutMaxHeight));

        RectangleF? anchorRect = container.GetElementRectangle(elementId);
        if (anchorRect is null)
            return null;

        float scrollY = anchorRect.Value.Y;
        container.Location = new PointF(0, scrollY);
        container.MaxSize = new SizeF(width, height);

        var bitmap = new ImageBitmap(width, height);
        bitmap.Clear(bgColor);
        container.PerformPaint(bitmap, new RectangleF(0, scrollY, width, height), new PointF(0, -scrollY));
        return bitmap;
    }

    private static ImageContainer CreateContainer(
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad)
    {
        var container = new ImageContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
        };

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        return container;
    }

    private static HtmlContainer CreatePipelineContainer(
        EventHandler<HtmlStylesheetLoadEventArgs>? stylesheetLoad,
        EventHandler<HtmlImageLoadEventArgs>? imageLoad)
    {
        var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
        };

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        return container;
    }

    private static Broiler.Graphics.BSurfaceDescriptor CreatePipelineSurfaceDescriptor(int width, int height) =>
        new(new Broiler.Graphics.BSize(width, height), 1.0, EnableTransparency: true);

    private static ImageColor ResolveCanvasBackground(ImageContainer container, ImageColor fallback)
    {
        Color rootBg = container.GetRootBackgroundColor();
        if (!rootBg.IsEmpty && rootBg.A > 0)
            return new ImageColor(rootBg.R, rootBg.G, rootBg.B, rootBg.A);

        return fallback;
    }

    private static GraphicsColor ResolveCanvasBackground(HtmlContainer container, GraphicsColor fallback)
    {
        Color rootBg = container.GetRootBackgroundColor();
        if (!rootBg.IsEmpty && rootBg.A > 0)
            return new GraphicsColor(rootBg.R, rootBg.G, rootBg.B, rootBg.A);

        return fallback;
    }

    private static ImageColor ToImageColor(GraphicsColor color) => new(color.R, color.G, color.B, color.A);

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
    }
}
