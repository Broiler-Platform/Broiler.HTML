using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Orchestration;
using Broiler.HTML.WPF.Adapters;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Broiler.HTML.WPF;

public static class HtmlRender
{
    public static void AddFontFamily(FontFamily fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);

        WpfAdapter.Instance.AddFontFamily(new FontFamilyAdapter(fontFamily));
    }

    public static void AddFontFamilyMapping(string fromFamily, string toFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromFamily);
        ArgumentException.ThrowIfNullOrEmpty(toFamily);

        WpfAdapter.Instance.AddFontFamilyMapping(fromFamily, toFamily);
    }

    [Obsolete("Use ParseStyleSet or ParseStyleSheetModel.")]
    public static CssData ParseStyleSheet(string stylesheet, bool combineWithDefault = true) =>
        new(HtmlStyleSet.Parse(stylesheet, combineWithDefault));

    public static HtmlStyleSet ParseStyleSet(string stylesheet, bool combineWithDefault = true) =>
        HtmlStyleSet.Parse(stylesheet, combineWithDefault);

    /// <summary>Parses CSS into the canonical shared stylesheet model.</summary>
    public static Broiler.CSS.CssStyleSheet ParseStyleSheetModel(
        string stylesheet,
        bool combineWithDefault = true) =>
        ParseStyleSet(stylesheet, combineWithDefault).StyleSheet;

    public static Size MeasureWithStyleSet(
        string html,
        HtmlStyleSet styleSet = null,
        double maxWidth = 0,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        if (string.IsNullOrEmpty(html))
            return Size.Empty;
        using var container = new HtmlContainer
        {
            MaxSize = new Size(maxWidth, 0),
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true
        };
        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;
        container.SetHtmlWithStyleSet(html, styleSet);
        container.PerformLayout();
        return container.ActualSize;
    }

    public static Size RenderWithStyleSet(
        DrawingContext drawingContext,
        string html,
        HtmlStyleSet styleSet = null,
        Point location = default,
        Size maxSize = default,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        return RenderClip(drawingContext, html, location, maxSize, styleSet, stylesheetLoad, imageLoad);
    }

    public static BitmapFrame RenderToImageWithStyleSet(
        string html,
        Size size,
        HtmlStyleSet styleSet = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null,
        EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        var renderTarget = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        if (!string.IsNullOrEmpty(html))
        {
            var visual = new DrawingVisual();
            using var drawingContext = visual.RenderOpen();
            RenderHtml(drawingContext, html, new Point(), size, styleSet, stylesheetLoad, imageLoad);
            renderTarget.Render(visual);
        }
        return BitmapFrame.Create(renderTarget);
    }

    [Obsolete("Use MeasureWithStyleSet.")]
    public static Size Measure(string html, double maxWidth = 0, CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        Size actualSize = Size.Empty;
        if (string.IsNullOrEmpty(html))
            return actualSize;

        using var container = new HtmlContainer();
        container.MaxSize = new Size(maxWidth, 0);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        container.SetHtmlWithStyleSet(html, GetStyleSet(cssData));
        container.PerformLayout();

        actualSize = container.ActualSize;
        return actualSize;
    }

    [Obsolete("Use RenderWithStyleSet.")]
    public static Size Render(DrawingContext g, string html, double left = 0, double top = 0, double maxWidth = 0, CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        ArgumentNullException.ThrowIfNull(g);
        return RenderClip(g, html, new Point(left, top), new Size(maxWidth, 0), GetStyleSet(cssData), stylesheetLoad, imageLoad);
    }

    [Obsolete("Use RenderWithStyleSet.")]
    public static Size Render(DrawingContext g, string html, Point location, Size maxSize, CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        ArgumentNullException.ThrowIfNull(g);
        return RenderClip(g, html, location, maxSize, GetStyleSet(cssData), stylesheetLoad, imageLoad);
    }

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static BitmapFrame RenderToImage(string html, Size size, CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        var renderTarget = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);

        if (string.IsNullOrEmpty(html))
            return BitmapFrame.Create(renderTarget);

        // render HTML into the visual
        DrawingVisual drawingVisual = new();
        using (DrawingContext g = drawingVisual.RenderOpen())
        {
            RenderHtml(g, html, new Point(), size, GetStyleSet(cssData), stylesheetLoad, imageLoad);
        }

        // render visual into target bitmap
        renderTarget.Render(drawingVisual);

        return BitmapFrame.Create(renderTarget);
    }

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static BitmapFrame RenderToImage(string html, int maxWidth = 0, int maxHeight = 0, Color backgroundColor = new Color(), CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null) => RenderToImage(html, Size.Empty, new Size(maxWidth, maxHeight), backgroundColor, cssData, stylesheetLoad, imageLoad);

    [Obsolete("Use RenderToImageWithStyleSet.")]
    public static BitmapFrame RenderToImage(string html, Size minSize, Size maxSize, Color backgroundColor = new Color(), CssData cssData = null,
        EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad = null, EventHandler<HtmlImageLoadEventArgs> imageLoad = null)
    {
        RenderTargetBitmap renderTarget;
        if (!string.IsNullOrEmpty(html))
        {
            using var container = new HtmlContainer();
            container.AvoidAsyncImagesLoading = true;
            container.AvoidImagesLateLoading = true;

            if (stylesheetLoad != null)
                container.StylesheetLoad += stylesheetLoad;
            if (imageLoad != null)
                container.ImageLoad += imageLoad;
            container.SetHtmlWithStyleSet(html, GetStyleSet(cssData));

            var finalSize = MeasureHtmlByRestrictions(container, minSize, maxSize);
            container.MaxSize = finalSize;

            renderTarget = new RenderTargetBitmap((int)finalSize.Width, (int)finalSize.Height, 96, 96, PixelFormats.Pbgra32);

            // render HTML into the visual
            DrawingVisual drawingVisual = new();
            using (DrawingContext g = drawingVisual.RenderOpen())
            {
                container.PerformPaint(g, new Rect(new Size(maxSize.Width > 0 ? maxSize.Width : double.MaxValue, maxSize.Height > 0 ? maxSize.Height : double.MaxValue)));
            }

            // render visual into target bitmap
            renderTarget.Render(drawingVisual);
        }
        else
        {
            renderTarget = new RenderTargetBitmap(0, 0, 96, 96, PixelFormats.Pbgra32);
        }

        return BitmapFrame.Create(renderTarget);
    }

#pragma warning disable CS0618
    private static HtmlStyleSet GetStyleSet(CssData cssData) => cssData?.StyleSet;
#pragma warning restore CS0618

    private static Size MeasureHtmlByRestrictions(HtmlContainer htmlContainer, Size minSize, Size maxSize)
    {
        // use desktop created graphics to measure the HTML
        using var mg = new GraphicsAdapter();
        var sizeInt = HtmlRendererUtils.MeasureHtmlByRestrictions(mg, htmlContainer.HtmlContainerInt, Utilities.Utils.Convert(minSize), Utilities.Utils.Convert(maxSize));
        
        if (maxSize.Width < 1 && sizeInt.Width > 4096)
            sizeInt.Width = 4096;
        
        return Utilities.Utils.ConvertRound(sizeInt);
    }

    private static Size RenderClip(DrawingContext g, string html, Point location, Size maxSize, HtmlStyleSet styleSet, EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad, EventHandler<HtmlImageLoadEventArgs> imageLoad)
    {
        if (maxSize.Height > 0)
            g.PushClip(new RectangleGeometry(new Rect(location, maxSize)));

        var actualSize = RenderHtml(g, html, location, maxSize, styleSet, stylesheetLoad, imageLoad);

        if (maxSize.Height > 0)
            g.Pop();

        return actualSize;
    }

    private static Size RenderHtml(DrawingContext g, string html, Point location, Size maxSize, HtmlStyleSet styleSet, EventHandler<HtmlStylesheetLoadEventArgs> stylesheetLoad, EventHandler<HtmlImageLoadEventArgs> imageLoad)
    {
        Size actualSize = Size.Empty;

        if (string.IsNullOrEmpty(html))
            return actualSize;

        using var container = new HtmlContainer();
        container.Location = location;
        container.MaxSize = maxSize;
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        if (stylesheetLoad != null)
            container.StylesheetLoad += stylesheetLoad;
        if (imageLoad != null)
            container.ImageLoad += imageLoad;

        container.SetHtmlWithStyleSet(html, styleSet);
        container.PerformLayout();
        container.PerformPaint(g, new Rect(0, 0, double.MaxValue, double.MaxValue));

        actualSize = container.ActualSize;

        return actualSize;
    }
}
