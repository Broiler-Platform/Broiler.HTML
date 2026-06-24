using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Core.IR;
using BroilerGraphics = Broiler.Graphics;

namespace Broiler.HTML.Graphics;

/// <summary>
/// A Broiler.Graphics render list plus backend image resources uploaded for that list.
/// </summary>
public sealed class HtmlGraphicsRenderList : IDisposable
{
    private readonly BroilerGraphics.IBroilerRenderer _renderer;
    private readonly List<BroilerGraphics.BImageHandle> _images;
    private bool _disposed;

    internal HtmlGraphicsRenderList(
        BroilerGraphics.IBroilerRenderer renderer,
        BroilerGraphics.BRenderList renderList,
        List<BroilerGraphics.BImageHandle> images)
    {
        _renderer = renderer;
        RenderList = renderList;
        _images = images;
    }

    public BroilerGraphics.BRenderList RenderList { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var image in _images)
        {
            if (image.IsValid)
                _renderer.ReleaseImage(image);
        }

        _images.Clear();
    }
}

internal static class HtmlGraphicsRenderListBuilder
{
    public static HtmlGraphicsRenderList Build(
        BroilerGraphics.IBroilerRenderer renderer,
        DisplayList displayList,
        RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(displayList);

        var list = new BroilerGraphics.BRenderList(displayList.Items.Count + 2);
        var images = new List<BroilerGraphics.BImageHandle>();
        var imageCache = new Dictionary<object, BroilerGraphics.BImageHandle>();
        var opacityStack = new Stack<double>();
        var clipStack = new Stack<bool>();
        double opacity = 1.0;

        if (IsDrawable(clip))
            list.PushClip(ToRect(clip));

        foreach (var item in displayList.Items)
        {
            switch (item)
            {
                case FillRectItem fill:
                    FillRect(list, fill.Bounds, fill.Color, opacity);
                    break;
                case DrawBorderItem border:
                    DrawBorder(list, border, opacity);
                    break;
                case DrawTextItem text:
                    DrawText(list, text, opacity);
                    break;
                case DrawImageItem image:
                    DrawImage(list, renderer, images, imageCache, image.ImageHandle, image.SourceRect, image.DestRect, opacity);
                    break;
                case DrawTiledImageItem tiled:
                    DrawTiledImage(list, renderer, images, imageCache, tiled, opacity);
                    break;
                case DrawTiledGradientItem gradient:
                    DrawGradientFallback(list, gradient, opacity);
                    break;
                case DrawLineItem line:
                    DrawLineFallback(list, line, opacity);
                    break;
                case DrawSvgRectItem svgRect:
                    DrawSvgRect(list, svgRect, opacity);
                    break;
                case DrawSvgLineItem svgLine:
                    DrawLineFallback(
                        list,
                        new DrawLineItem
                        {
                            Start = new PointF(svgLine.Bounds.X + svgLine.X1, svgLine.Bounds.Y + svgLine.Y1),
                            End = new PointF(svgLine.Bounds.X + svgLine.X2, svgLine.Bounds.Y + svgLine.Y2),
                            Color = svgLine.Stroke,
                            Width = svgLine.StrokeWidth,
                        },
                        opacity);
                    break;
                case DrawSvgTextItem svgText:
                    DrawText(
                        list,
                        new DrawTextItem
                        {
                            Text = svgText.Text,
                            FontFamily = svgText.FontFamily,
                            FontSize = svgText.FontSize,
                            Color = svgText.Fill,
                            Origin = new PointF(svgText.Bounds.X + svgText.X, svgText.Bounds.Y + svgText.Y),
                        },
                        opacity);
                    break;
                case ClipItem clipItem:
                    if (IsDrawable(clipItem.ClipRect))
                    {
                        list.PushClip(ToRect(clipItem.ClipRect));
                        clipStack.Push(true);
                    }
                    else
                    {
                        clipStack.Push(false);
                    }
                    break;
                case RestoreItem:
                    if (clipStack.Count > 0 && clipStack.Pop())
                        list.PopClip();
                    break;
                case TransformItem transform:
                    list.PushTransform(ToMatrix(transform));
                    break;
                case RestoreTransformItem:
                    list.PopTransform();
                    break;
                case OpacityItem opacityItem:
                    opacityStack.Push(opacity);
                    opacity *= Math.Clamp(opacityItem.Opacity, 0f, 1f);
                    break;
                case RestoreOpacityItem:
                    opacity = opacityStack.Count > 0 ? opacityStack.Pop() : 1.0;
                    break;
                case BlendModeItem:
                case RestoreBlendModeItem:
                    break;
            }
        }

        if (IsDrawable(clip))
            list.PopClip();

        return new HtmlGraphicsRenderList(renderer, list, images);
    }

    private static void FillRect(BroilerGraphics.BRenderList list, RectangleF rect, Color color, double opacity)
    {
        if (!IsDrawable(rect) || color.A == 0 || opacity <= 0)
            return;

        list.FillRect(ToRect(rect), ToColor(color, opacity));
    }

    private static void DrawBorder(BroilerGraphics.BRenderList list, DrawBorderItem item, double opacity)
    {
        RectangleF bounds = item.Bounds;
        BoxEdges widths = item.Widths;

        DrawBorderSide(list, new RectangleF(bounds.Left, bounds.Top, bounds.Width, (float)widths.Top), item.TopColor, item.TopStyle, widths.Top, opacity);
        DrawBorderSide(list, new RectangleF(bounds.Right - (float)widths.Right, bounds.Top, (float)widths.Right, bounds.Height), item.RightColor, item.RightStyle, widths.Right, opacity);
        DrawBorderSide(list, new RectangleF(bounds.Left, bounds.Bottom - (float)widths.Bottom, bounds.Width, (float)widths.Bottom), item.BottomColor, item.BottomStyle, widths.Bottom, opacity);
        DrawBorderSide(list, new RectangleF(bounds.Left, bounds.Top, (float)widths.Left, bounds.Height), item.LeftColor, item.LeftStyle, widths.Left, opacity);
    }

    private static void DrawBorderSide(
        BroilerGraphics.BRenderList list,
        RectangleF rect,
        Color color,
        string style,
        double width,
        double opacity)
    {
        if (width <= 0 || color.A == 0 || !IsBorderStyleVisible(style))
            return;

        if (string.Equals(style, "double", StringComparison.OrdinalIgnoreCase) && width >= 3)
        {
            float line = (float)Math.Max(1d, Math.Floor(width / 3d));
            if (rect.Width >= rect.Height)
            {
                FillRect(list, new RectangleF(rect.X, rect.Y, rect.Width, line), color, opacity);
                FillRect(list, new RectangleF(rect.X, rect.Bottom - line, rect.Width, line), color, opacity);
            }
            else
            {
                FillRect(list, new RectangleF(rect.X, rect.Y, line, rect.Height), color, opacity);
                FillRect(list, new RectangleF(rect.Right - line, rect.Y, line, rect.Height), color, opacity);
            }

            return;
        }

        FillRect(list, rect, color, opacity);
    }

    private static void DrawText(BroilerGraphics.BRenderList list, DrawTextItem item, double opacity)
    {
        if (string.IsNullOrEmpty(item.Text) || item.FontSize <= 0 || item.Color.A == 0 || opacity <= 0)
            return;

        var font = new BroilerGraphics.BFontStyle(
            string.IsNullOrWhiteSpace(item.FontFamily) ? "Segoe UI" : item.FontFamily,
            item.FontSize,
            ToFontWeight(item.FontWeight));

        if (!item.TextShadowColor.IsEmpty && item.TextShadowColor.A > 0
            && (item.TextShadowOffsetX != 0 || item.TextShadowOffsetY != 0))
        {
            list.DrawText(
                new BroilerGraphics.BTextRun(item.Text, font, ToColor(item.TextShadowColor, opacity)),
                new BroilerGraphics.BPoint(item.Origin.X + item.TextShadowOffsetX, item.Origin.Y + item.TextShadowOffsetY));
        }

        list.DrawText(
            new BroilerGraphics.BTextRun(item.Text, font, ToColor(item.Color, opacity)),
            new BroilerGraphics.BPoint(item.Origin.X, item.Origin.Y));
    }

    private static void DrawImage(
        BroilerGraphics.BRenderList list,
        BroilerGraphics.IBroilerRenderer renderer,
        List<BroilerGraphics.BImageHandle> images,
        Dictionary<object, BroilerGraphics.BImageHandle> imageCache,
        object? imageHandle,
        RectangleF source,
        RectangleF destination,
        double opacity)
    {
        if (imageHandle == null || !IsDrawable(destination) || opacity <= 0)
            return;

        BroilerGraphics.BImageHandle image = GetImage(renderer, images, imageCache, imageHandle);
        if (!image.IsValid)
            return;

        if (!IsDrawable(source))
            source = new RectangleF(0, 0, (float)image.PixelSize.Width, (float)image.PixelSize.Height);

        list.DrawImage(image, ToRect(source), ToRect(destination), opacity);
    }

    private static void DrawTiledImage(
        BroilerGraphics.BRenderList list,
        BroilerGraphics.IBroilerRenderer renderer,
        List<BroilerGraphics.BImageHandle> images,
        Dictionary<object, BroilerGraphics.BImageHandle> imageCache,
        DrawTiledImageItem item,
        double opacity)
    {
        if (item.ImageHandle == null || !IsDrawable(item.FillRect) || opacity <= 0)
            return;

        BroilerGraphics.BImageHandle image = GetImage(renderer, images, imageCache, item.ImageHandle);
        if (!image.IsValid)
            return;

        RectangleF source = IsDrawable(item.SourceRect)
            ? item.SourceRect
            : new RectangleF(0, 0, (float)image.PixelSize.Width, (float)image.PixelSize.Height);

        float tileWidth = item.TileWidth > 0 ? item.TileWidth : source.Width;
        float tileHeight = item.TileHeight > 0 ? item.TileHeight : source.Height;
        if (tileWidth <= 0 || tileHeight <= 0)
            return;

        RectangleF fill = item.FillRect;
        list.PushClip(ToRect(fill));

        bool repeatX = !string.Equals(item.Repeat, "no-repeat", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Repeat, "repeat-y", StringComparison.OrdinalIgnoreCase);
        bool repeatY = !string.Equals(item.Repeat, "no-repeat", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Repeat, "repeat-x", StringComparison.OrdinalIgnoreCase);

        float startX = item.TileOrigin.X;
        float startY = item.TileOrigin.Y;
        if (repeatX)
        {
            while (startX > fill.Left)
                startX -= tileWidth;
        }
        if (repeatY)
        {
            while (startY > fill.Top)
                startY -= tileHeight;
        }

        for (float y = startY; y < fill.Bottom; y += repeatY ? tileHeight : Math.Max(tileHeight, fill.Height + tileHeight))
        {
            for (float x = startX; x < fill.Right; x += repeatX ? tileWidth : Math.Max(tileWidth, fill.Width + tileWidth))
            {
                list.DrawImage(
                    image,
                    ToRect(source),
                    ToRect(new RectangleF(x, y, tileWidth, tileHeight)),
                    opacity);

                if (!repeatX)
                    break;
            }

            if (!repeatY)
                break;
        }

        list.PopClip();
    }

    private static BroilerGraphics.BImageHandle GetImage(
        BroilerGraphics.IBroilerRenderer renderer,
        List<BroilerGraphics.BImageHandle> images,
        Dictionary<object, BroilerGraphics.BImageHandle> imageCache,
        object imageHandle)
    {
        if (imageCache.TryGetValue(imageHandle, out BroilerGraphics.BImageHandle cached))
            return cached;

        if (!Broiler.HTML.Image.HtmlRender.TryCreatePixelBuffer(imageHandle, out BroilerGraphics.BPixelBuffer pixels))
            return BroilerGraphics.BImageHandle.Invalid;

        BroilerGraphics.BImageHandle image = renderer.CreateImage(pixels);
        images.Add(image);
        imageCache[imageHandle] = image;
        return image;
    }

    private static void DrawGradientFallback(BroilerGraphics.BRenderList list, DrawTiledGradientItem item, double opacity)
    {
        if (item.Stops == null || item.Stops.Count == 0)
            return;

        FillRect(list, item.FillRect, item.Stops[0].Color, opacity);
    }

    private static void DrawLineFallback(BroilerGraphics.BRenderList list, DrawLineItem item, double opacity)
    {
        if (item.Width <= 0 || item.Color.A == 0)
            return;

        if (Math.Abs(item.Start.Y - item.End.Y) < 0.001f)
        {
            float left = Math.Min(item.Start.X, item.End.X);
            float width = Math.Abs(item.End.X - item.Start.X);
            FillRect(list, new RectangleF(left, item.Start.Y - (item.Width / 2f), width, item.Width), item.Color, opacity);
            return;
        }

        if (Math.Abs(item.Start.X - item.End.X) < 0.001f)
        {
            float top = Math.Min(item.Start.Y, item.End.Y);
            float height = Math.Abs(item.End.Y - item.Start.Y);
            FillRect(list, new RectangleF(item.Start.X - (item.Width / 2f), top, item.Width, height), item.Color, opacity);
        }
    }

    private static void DrawSvgRect(BroilerGraphics.BRenderList list, DrawSvgRectItem item, double opacity)
    {
        var rect = new RectangleF(item.Bounds.X + item.X, item.Bounds.Y + item.Y, item.Width, item.Height);
        FillRect(list, rect, item.Fill, opacity);
        if (!item.Stroke.IsEmpty && item.StrokeWidth > 0)
            list.StrokeRect(ToRect(rect), ToColor(item.Stroke, opacity), item.StrokeWidth);
    }

    private static BroilerGraphics.BMatrix3x2 ToMatrix(TransformItem item)
    {
        float[] m = item.Matrix;
        if (m.Length < 6)
            return BroilerGraphics.BMatrix3x2.Identity;

        var matrix = new BroilerGraphics.BMatrix3x2(m[0], m[1], m[2], m[3], m[4], m[5]);
        return BroilerGraphics.BMatrix3x2.Translation(-item.OriginX, -item.OriginY)
            * matrix
            * BroilerGraphics.BMatrix3x2.Translation(item.OriginX, item.OriginY);
    }

    private static BroilerGraphics.BRect ToRect(RectangleF rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static BroilerGraphics.BColor ToColor(Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return new BroilerGraphics.BColor(color.R, color.G, color.B, alpha);
    }

    private static BroilerGraphics.BFontWeight ToFontWeight(string value)
    {
        if (int.TryParse(value, out int numeric))
        {
            if (numeric >= 800) return BroilerGraphics.BFontWeight.Black;
            if (numeric >= 700) return BroilerGraphics.BFontWeight.Bold;
            if (numeric >= 600) return BroilerGraphics.BFontWeight.SemiBold;
            if (numeric >= 500) return BroilerGraphics.BFontWeight.Medium;
            if (numeric <= 300) return BroilerGraphics.BFontWeight.Light;
            return BroilerGraphics.BFontWeight.Normal;
        }

        return value?.ToLowerInvariant() switch
        {
            "bold" or "bolder" => BroilerGraphics.BFontWeight.Bold,
            "600" => BroilerGraphics.BFontWeight.SemiBold,
            "500" => BroilerGraphics.BFontWeight.Medium,
            "lighter" or "light" => BroilerGraphics.BFontWeight.Light,
            _ => BroilerGraphics.BFontWeight.Normal,
        };
    }

    private static bool IsDrawable(RectangleF rect) =>
        rect.Width > 0
        && rect.Height > 0
        && float.IsFinite(rect.X)
        && float.IsFinite(rect.Y)
        && float.IsFinite(rect.Width)
        && float.IsFinite(rect.Height);

    private static bool IsBorderStyleVisible(string style) =>
        !string.IsNullOrEmpty(style)
        && !string.Equals(style, "none", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(style, "hidden", StringComparison.OrdinalIgnoreCase);
}
