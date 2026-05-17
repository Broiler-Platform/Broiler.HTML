using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.Core.Core.IR;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Orchestration.Core.IR;

/// <summary>
/// <see cref="IRasterBackend"/> implementation that replays a <see cref="DisplayList"/>
/// onto an <see cref="RGraphics"/> surface. Bridges the new IR paint pipeline back to
/// the existing platform adapters.
/// </summary>
internal sealed class RGraphicsRasterBackend : IRasterBackend
{
    public static readonly RGraphicsRasterBackend Instance = new();

    public void Render(DisplayList list, object surface)
    {
        if (surface is not RGraphics g)
            throw new ArgumentException("Surface must be an RGraphics instance.", nameof(surface));

        for (int index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            switch (item)
            {
                case FillRectItem fill:
                    RenderFillRect(g, fill);
                    break;
                case DrawBorderItem border:
                    RenderDrawBorder(g, border);
                    break;
                case DrawTextItem text:
                    RenderDrawText(g, text);
                    break;
                case DrawImageItem image:
                    RenderDrawImage(g, image);
                    break;
                case DrawTiledImageItem tiled:
                    RenderDrawTiledImage(g, tiled);
                    break;
                case DrawTiledGradientItem tiledGrad:
                    RenderDrawTiledGradient(g, tiledGrad);
                    break;
                case DrawLineItem line:
                    RenderDrawLine(g, line);
                    break;
                case DrawSvgRectItem svgRect:
                    RenderSvgRect(g, svgRect);
                    break;
                case DrawSvgEllipseItem svgEllipse:
                    RenderSvgEllipse(g, svgEllipse);
                    break;
                case DrawSvgTextItem svgText:
                    RenderSvgText(g, svgText);
                    break;
                case DrawSvgLineItem svgLine:
                    RenderSvgLine(g, svgLine);
                    break;
                case DrawSvgPolygonItem svgPolygon:
                    RenderSvgPolygon(g, svgPolygon);
                    break;
                case DrawSvgPolylineItem svgPolyline:
                    RenderSvgPolyline(g, svgPolyline);
                    break;
                case ClipItem clip:
                    if (clip.CornerNw > 0 || clip.CornerNe > 0 || clip.CornerSe > 0 || clip.CornerSw > 0)
                        g.PushClipRounded(
                            clip.ClipRect,
                            clip.CornerNw, clip.CornerNwY,
                            clip.CornerNe, clip.CornerNeY,
                            clip.CornerSe, clip.CornerSeY,
                            clip.CornerSw, clip.CornerSwY);
                    else
                        g.PushClip(clip.ClipRect);
                    break;
                case RestoreItem:
                    g.PopClip();
                    break;
                case OpacityItem opacityItem:
                    g.HintNextLayerCanUseRaster(IsRasterCompatibleOpacityLayer(list.Items, index));
                    g.SaveOpacityLayer(opacityItem.Opacity);
                    break;
                case RestoreOpacityItem:
                    g.RestoreOpacityLayer();
                    break;
                case BlendModeItem blendItem:
                    g.HintNextLayerCanUseRaster(IsRasterCompatibleBlendLayer(list.Items, index, blendItem.Mode));
                    g.SaveBlendLayer(blendItem.Mode);
                    break;
                case RestoreBlendModeItem:
                    g.RestoreBlendLayer();
                    break;
            }
        }
    }

    private static bool IsRasterCompatibleOpacityLayer(IReadOnlyList<DisplayItem> items, int startIndex) =>
        IsRasterCompatibleLayer(items, startIndex, typeof(OpacityItem), typeof(RestoreOpacityItem));

    private static bool IsRasterCompatibleBlendLayer(IReadOnlyList<DisplayItem> items, int startIndex, string? blendMode) =>
        IsRasterBlendModeSupported(blendMode)
        && IsRasterCompatibleLayer(items, startIndex, typeof(BlendModeItem), typeof(RestoreBlendModeItem));

    private static bool IsRasterCompatibleLayer(
        IReadOnlyList<DisplayItem> items,
        int startIndex,
        Type openType,
        Type closeType)
    {
        int depth = 0;
        for (int index = startIndex + 1; index < items.Count; index++)
        {
            var item = items[index];
            var itemType = item.GetType();
            if (itemType == openType)
            {
                depth++;
                continue;
            }

            if (itemType == closeType)
            {
                if (depth == 0)
                    return true;

                depth--;
                continue;
            }

            if (!IsRasterCompatibleItem(item))
                return false;
        }

        return false;
    }

    private static bool IsRasterCompatibleItem(DisplayItem item) => item switch
    {
        FillRectItem => true,
        DrawBorderItem border => IsRasterCompatibleBorder(border),
        DrawImageItem => true,
        DrawTiledImageItem => true,
        DrawTiledGradientItem => true,
        DrawLineItem => true,
        DrawSvgRectItem => true,
        DrawSvgEllipseItem => true,
        DrawSvgLineItem => true,
        ClipItem => true,
        RestoreItem => true,
        OpacityItem => true,
        RestoreOpacityItem => true,
        BlendModeItem blend => IsRasterBlendModeSupported(blend.Mode),
        RestoreBlendModeItem => true,
        DrawTextItem => false,
        DrawSvgTextItem => false,
        _ => false,
    };

    private static bool IsRasterCompatibleBorder(DrawBorderItem item)
    {
        var widths = item.Widths;
        return IsRasterCompatibleBorderSide(widths.Top, item.TopColor, item.TopStyle)
            && IsRasterCompatibleBorderSide(widths.Right, item.RightColor, item.RightStyle)
            && IsRasterCompatibleBorderSide(widths.Bottom, item.BottomColor, item.BottomStyle)
            && IsRasterCompatibleBorderSide(widths.Left, item.LeftColor, item.LeftStyle);
    }

    private static bool IsRasterCompatibleBorderSide(double width, Color color, string? style) =>
        width <= 0
        || color.A <= 0
        || string.IsNullOrEmpty(style)
        || style.Equals("solid", StringComparison.OrdinalIgnoreCase)
        || style.Equals("double", StringComparison.OrdinalIgnoreCase);

    private static bool IsRasterBlendModeSupported(string? blendMode) =>
        string.IsNullOrEmpty(blendMode)
        || blendMode.Equals("normal", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("multiply", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("screen", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("darken", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("lighten", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("overlay", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("difference", StringComparison.OrdinalIgnoreCase)
        || blendMode.Equals("plus-lighter", StringComparison.OrdinalIgnoreCase);

    private static void RenderFillRect(RGraphics g, FillRectItem item)
    {
        using var brush = g.GetSolidBrush(item.Color);
        // CSS2.1 §14.2: backgrounds extend to the padding edge.
        // P3.2: Do NOT round absolute coordinates — the canvas
        // transform already contains a fractional scroll offset that
        // aligns integer *layout* positions to exact pixel boundaries.
        // Rounding absolute coords shifts the fill by ~0.09 px in
        // viewport space, causing partial-coverage AA artifacts at
        // element edges (e.g. (231,231,231) vs (255,255,255)).
        g.DrawRectangle(brush, item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height);
    }

    private static void RenderDrawBorder(RGraphics g, DrawBorderItem item)
    {
        var bounds = item.Bounds;
        var widths = item.Widths;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // P3.1/P3.2 audit: Use raw layout coordinates for border edges.
        // The canvas transform contains the fractional scroll offset
        // that maps integer layout positions to exact pixel boundaries.
        // Rounding absolute coords was tested (Round, Floor, Ceiling
        // on origin, inner edges, or all edges) and always regressed
        // because it shifted borders by ~0.09 px in viewport space,
        // creating new partial-coverage artifacts.  The existing
        // rendering with SKPaint.IsAntialias = true produces the
        // correct CSS 2.1 Appendix E paint order.

        // Fill corner rectangles to prevent anti-aliased seams along
        // the diagonal edges where two same-color border trapezoids meet.
        FillBorderCorners(g, item);

        // Top border
        if (widths.Top > 0 && item.TopColor.A > 0 && IsBorderStyleVisible(item.TopStyle))
        {
            if (item.TopStyle == "double")
            {
                DrawDoubleBorderSide(g, item, Border.Top);
            }
            else if (item.TopStyle == "solid")
            {
                // Trapezoid rendering for correct corner joins with asymmetric widths
                var pts = new PointF[4];
                pts[0] = new PointF(bounds.Left, bounds.Top);
                pts[1] = new PointF(bounds.Right, bounds.Top);
                pts[2] = new PointF((float)(bounds.Right - widths.Right), (float)(bounds.Top + widths.Top));
                pts[3] = new PointF((float)(bounds.Left + widths.Left), (float)(bounds.Top + widths.Top));
                g.DrawPolygon(g.GetSolidBrush(item.TopColor), pts);
            }
            else
            {
                var pen = CreateBorderPen(g, item.TopStyle, item.TopColor, widths.Top);
                g.DrawLine(pen, Math.Ceiling(bounds.Left), bounds.Top + widths.Top / 2, bounds.Right - 1, bounds.Top + widths.Top / 2);
            }
        }

        // Left border
        if (widths.Left > 0 && item.LeftColor.A > 0 && IsBorderStyleVisible(item.LeftStyle))
        {
            if (item.LeftStyle == "double")
            {
                DrawDoubleBorderSide(g, item, Border.Left);
            }
            else if (item.LeftStyle == "solid")
            {
                var pts = new PointF[4];
                pts[0] = new PointF(bounds.Left, bounds.Top);
                pts[1] = new PointF((float)(bounds.Left + widths.Left), (float)(bounds.Top + widths.Top));
                pts[2] = new PointF((float)(bounds.Left + widths.Left), (float)(bounds.Bottom - widths.Bottom));
                pts[3] = new PointF(bounds.Left, bounds.Bottom);
                g.DrawPolygon(g.GetSolidBrush(item.LeftColor), pts);
            }
            else
            {
                var pen = CreateBorderPen(g, item.LeftStyle, item.LeftColor, widths.Left);
                g.DrawLine(pen, bounds.Left + widths.Left / 2, Math.Ceiling(bounds.Top), bounds.Left + widths.Left / 2, Math.Floor(bounds.Bottom));
            }
        }

        // Bottom border
        if (widths.Bottom > 0 && item.BottomColor.A > 0 && IsBorderStyleVisible(item.BottomStyle))
        {
            if (item.BottomStyle == "double")
            {
                DrawDoubleBorderSide(g, item, Border.Bottom);
            }
            else if (item.BottomStyle == "solid")
            {
                var pts = new PointF[4];
                pts[0] = new PointF((float)(bounds.Left + widths.Left), (float)(bounds.Bottom - widths.Bottom));
                pts[1] = new PointF((float)(bounds.Right - widths.Right), (float)(bounds.Bottom - widths.Bottom));
                pts[2] = new PointF(bounds.Right, bounds.Bottom);
                pts[3] = new PointF(bounds.Left, bounds.Bottom);
                g.DrawPolygon(g.GetSolidBrush(item.BottomColor), pts);
            }
            else
            {
                var pen = CreateBorderPen(g, item.BottomStyle, item.BottomColor, widths.Bottom);
                g.DrawLine(pen, Math.Ceiling(bounds.Left), bounds.Bottom - widths.Bottom / 2,
                    bounds.Right - 1, bounds.Bottom - widths.Bottom / 2);
            }
        }

        // Right border
        if (widths.Right > 0 && item.RightColor.A > 0 && IsBorderStyleVisible(item.RightStyle))
        {
            if (item.RightStyle == "double")
            {
                DrawDoubleBorderSide(g, item, Border.Right);
            }
            else if (item.RightStyle == "solid")
            {
                var pts = new PointF[4];
                pts[0] = new PointF((float)(bounds.Right - widths.Right), (float)(bounds.Top + widths.Top));
                pts[1] = new PointF(bounds.Right, bounds.Top);
                pts[2] = new PointF(bounds.Right, bounds.Bottom);
                pts[3] = new PointF((float)(bounds.Right - widths.Right), (float)(bounds.Bottom - widths.Bottom));
                g.DrawPolygon(g.GetSolidBrush(item.RightColor), pts);
            }
            else
            {
                var pen = CreateBorderPen(g, item.RightStyle, item.RightColor, widths.Right);
                g.DrawLine(pen, bounds.Right - widths.Right / 2, Math.Ceiling(bounds.Top),
                    bounds.Right - widths.Right / 2, Math.Floor(bounds.Bottom));
            }
        }
    }

    /// <summary>
    /// Fills corner rectangles where two adjacent solid borders share the same color.
    /// This prevents visible anti-aliased seams along the diagonal edge where the
    /// two border trapezoids meet, which would otherwise let the background bleed through.
    /// </summary>
    private static void FillBorderCorners(RGraphics g, DrawBorderItem item)
    {
        var bounds = item.Bounds;
        var widths = item.Widths;

        // Only fill opaque corners. Semi-transparent borders (alpha < 255)
        // must NOT use corner fills because the fill rectangle and the
        // overlapping border trapezoids would composite the same alpha twice,
        // producing incorrect (darker) corner pixels.
        bool hasTop = widths.Top > 0 && item.TopColor.A == 255 && IsCornerFillBorderStyle(item.TopStyle);
        bool hasRight = widths.Right > 0 && item.RightColor.A == 255 && IsCornerFillBorderStyle(item.RightStyle);
        bool hasBottom = widths.Bottom > 0 && item.BottomColor.A == 255 && IsCornerFillBorderStyle(item.BottomStyle);
        bool hasLeft = widths.Left > 0 && item.LeftColor.A == 255 && IsCornerFillBorderStyle(item.LeftStyle);

        // Top-left corner
        if (hasTop && hasLeft && item.TopColor == item.LeftColor)
            g.DrawRectangle(g.GetSolidBrush(item.TopColor),
                bounds.Left, bounds.Top, widths.Left, widths.Top);

        // Top-right corner
        if (hasTop && hasRight && item.TopColor == item.RightColor)
            g.DrawRectangle(g.GetSolidBrush(item.TopColor),
                bounds.Right - widths.Right, bounds.Top, widths.Right, widths.Top);

        // Bottom-left corner
        if (hasBottom && hasLeft && item.BottomColor == item.LeftColor)
            g.DrawRectangle(g.GetSolidBrush(item.BottomColor),
                bounds.Left, bounds.Bottom - widths.Bottom, widths.Left, widths.Bottom);

        // Bottom-right corner
        if (hasBottom && hasRight && item.BottomColor == item.RightColor)
            g.DrawRectangle(g.GetSolidBrush(item.BottomColor),
                bounds.Right - widths.Right, bounds.Bottom - widths.Bottom, widths.Right, widths.Bottom);
    }

    private static bool IsCornerFillBorderStyle(string? style) =>
        string.Equals(style, "solid", StringComparison.OrdinalIgnoreCase)
        || string.Equals(style, "double", StringComparison.OrdinalIgnoreCase);

    private static void DrawDoubleBorderSide(RGraphics g, DrawBorderItem item, Border side)
    {
        var bounds = item.Bounds;
        var widths = item.Widths;
        // Use Math.Floor to match browser integer rounding: 25px -> 8px stripes, 9px gap.
        float topLine = (float)Math.Max(1d, Math.Floor(widths.Top / 3d));
        float rightLine = (float)Math.Max(1d, Math.Floor(widths.Right / 3d));
        float bottomLine = (float)Math.Max(1d, Math.Floor(widths.Bottom / 3d));
        float leftLine = (float)Math.Max(1d, Math.Floor(widths.Left / 3d));
        using var brush = g.GetSolidBrush(side switch
        {
            Border.Top => item.TopColor,
            Border.Right => item.RightColor,
            Border.Bottom => item.BottomColor,
            Border.Left => item.LeftColor,
            _ => Color.Empty,
        });

        switch (side)
        {
            case Border.Top:
                g.DrawRectangle(brush, bounds.Left, bounds.Top, bounds.Width, topLine);
                g.DrawRectangle(
                    brush,
                    bounds.Left + (widths.Left - leftLine),
                    bounds.Top + (widths.Top - topLine),
                    Math.Max(0, bounds.Width - (widths.Left - leftLine) - leftLine - (widths.Right - rightLine) - rightLine),
                    topLine);
                break;
            case Border.Right:
                g.DrawRectangle(brush, bounds.Right - rightLine, bounds.Top, rightLine, bounds.Height);
                g.DrawRectangle(
                    brush,
                    bounds.Right - widths.Right,
                    bounds.Top + (widths.Top - topLine),
                    rightLine,
                    Math.Max(0, bounds.Height - (widths.Top - topLine) - topLine - (widths.Bottom - bottomLine) - bottomLine));
                break;
            case Border.Bottom:
                g.DrawRectangle(brush, bounds.Left, bounds.Bottom - bottomLine, bounds.Width, bottomLine);
                g.DrawRectangle(
                    brush,
                    bounds.Left + (widths.Left - leftLine),
                    bounds.Bottom - widths.Bottom,
                    Math.Max(0, bounds.Width - (widths.Left - leftLine) - leftLine - (widths.Right - rightLine) - rightLine),
                    bottomLine);
                break;
            case Border.Left:
                g.DrawRectangle(brush, bounds.Left, bounds.Top, leftLine, bounds.Height);
                g.DrawRectangle(
                    brush,
                    bounds.Left + (widths.Left - leftLine),
                    bounds.Top + (widths.Top - topLine),
                    leftLine,
                    Math.Max(0, bounds.Height - (widths.Top - topLine) - topLine - (widths.Bottom - bottomLine) - bottomLine));
                break;
        }
    }

    private static void RenderDrawText(RGraphics g, DrawTextItem item)
    {
        if (string.IsNullOrEmpty(item.Text))
            return;

        if (item.FontHandle is RFont font)
        {
            // Phase 10.2: Round text origin to integer pixel coordinates.
            // Sub-pixel text positioning causes glyph rasterisation to differ
            // from Chromium's pixel-snapped baseline, producing per-glyph
            // anti-aliasing differences.
            var origin = new PointF((float)Math.Round(item.Origin.X), (float)Math.Round(item.Origin.Y));
            var size = new SizeF(item.Bounds.Width, item.Bounds.Height);

            // Draw text shadow first (behind the actual text)
            if (!item.TextShadowColor.IsEmpty &&
                (item.TextShadowOffsetX != 0 || item.TextShadowOffsetY != 0))
            {
                var shadowOrigin = new PointF(
                    origin.X + item.TextShadowOffsetX,
                    origin.Y + item.TextShadowOffsetY);
                g.DrawString(item.Text, font, item.TextShadowColor, shadowOrigin, size, item.IsRtl);
            }

            if (item.GradientStops != null && item.GradientStops.Count > 0)
            {
                var (colors, positions) = ExpandGradientStops(item.GradientStops, item.GradientInterpolationSpace);
                var gradientBounds = item.GradientBounds == RectangleF.Empty ? item.Bounds : item.GradientBounds;
                g.DrawGradientString(item.Text, font, gradientBounds, origin, size, item.IsRtl, colors, positions, item.GradientAngle);
            }
            else
            {
                g.DrawString(item.Text, font, item.Color, origin, size, item.IsRtl);
            }
        }
    }

    private static void RenderDrawImage(RGraphics g, DrawImageItem item)
    {
        if (item.ImageHandle is not RImage image)
            return;

        if (item.SourceRect != RectangleF.Empty)
            g.DrawImage(image, item.DestRect, item.SourceRect);
        else
            g.DrawImage(image, item.DestRect);
    }

    private static void RenderDrawTiledImage(RGraphics g, DrawTiledImageItem item)
    {
        if (item.ImageHandle is not RImage image)
            return;

        var srcRect = item.SourceRect == RectangleF.Empty
            ? new RectangleF(0, 0, (float)image.Width, (float)image.Height)
            : item.SourceRect;

        // CSS background-size: when TileWidth/TileHeight are specified,
        // the visual tile dimensions differ from the source image dimensions.
        float tileW = item.TileWidth > 0 ? item.TileWidth : srcRect.Width;
        float tileH = item.TileHeight > 0 ? item.TileHeight : srcRect.Height;

        var fill = item.FillRect;
        var positioningArea = item.PositioningArea == RectangleF.Empty ? fill : item.PositioningArea;
        var origin = item.TileOrigin;

        // Clip to the element's padding box
        var clip = fill;
        clip.Intersect(g.GetClip());
        g.PushClip(clip);

        switch (item.Repeat)
        {
            case "no-repeat":
                DrawClippedImage(g, image, new RectangleF(origin.X, origin.Y, tileW, tileH), srcRect, clip);
                break;
            case "space":
                DrawSpace(g, image, fill, clip, positioningArea, origin, srcRect, tileW, tileH);
                break;
            case "repeat-x":
                {
                    // Shift origin left to cover the fill area
                    float ox = origin.X;
                    while (ox > fill.X) ox -= tileW;
                    if (tileW == srcRect.Width && tileH == srcRect.Height)
                    {
                        using var brush = g.GetTextureBrush(image, srcRect, new PointF(ox, origin.Y));
                        g.DrawRectangle(brush, fill.X, origin.Y, fill.Width, tileH);
                    }
                    else
                    {
                        // Scaled tiles: draw individual tiles
                        for (float tx = ox; tx < fill.Right; tx += tileW)
                            g.DrawImage(image, new RectangleF(tx, origin.Y, tileW, tileH), srcRect);
                    }
                    break;
                }
            case "repeat-y":
                {
                    float oy = origin.Y;
                    while (oy > fill.Y) oy -= tileH;
                    if (tileW == srcRect.Width && tileH == srcRect.Height)
                    {
                        using var brush = g.GetTextureBrush(image, srcRect, new PointF(origin.X, oy));
                        g.DrawRectangle(brush, origin.X, fill.Y, tileW, fill.Height);
                    }
                    else
                    {
                        for (float ty = oy; ty < fill.Bottom; ty += tileH)
                            g.DrawImage(image, new RectangleF(origin.X, ty, tileW, tileH), srcRect);
                    }
                    break;
                }
            default: // "repeat"
                {
                    float ox = origin.X;
                    while (ox > fill.X) ox -= tileW;
                    float oy = origin.Y;
                    while (oy > fill.Y) oy -= tileH;
                    if (tileW == srcRect.Width && tileH == srcRect.Height)
                    {
                        using var brush = g.GetTextureBrush(image, srcRect, new PointF(ox, oy));
                        g.DrawRectangle(brush, fill.X, fill.Y, fill.Width, fill.Height);
                    }
                    else
                    {
                        for (float ty = oy; ty < fill.Bottom; ty += tileH)
                            for (float tx = ox; tx < fill.Right; tx += tileW)
                                g.DrawImage(image, new RectangleF(tx, ty, tileW, tileH), srcRect);
                    }
                    break;
                }
        }

        g.PopClip();
    }

    private static void DrawSpace(
        RGraphics g,
        RImage image,
        RectangleF fill,
        RectangleF clip,
        RectangleF positioningArea,
        PointF origin,
        RectangleF srcRect,
        float tileW,
        float tileH)
    {
        var xPositions = GetSpacePositions(positioningArea.X, positioningArea.Width, tileW, origin.X);
        var yPositions = GetSpacePositions(positioningArea.Y, positioningArea.Height, tileH, origin.Y);

        foreach (var y in yPositions)
        {
            foreach (var x in xPositions)
                DrawClippedImage(g, image, new RectangleF(x, y, tileW, tileH), srcRect, clip);
        }
    }

    private static List<float> GetSpacePositions(float start, float span, float tileSize, float fallbackPosition)
    {
        var positions = new List<float>();
        if (tileSize <= 0 || span <= 0)
            return positions;

        int count = (int)MathF.Floor(span / tileSize);
        if (count >= 2)
        {
            float gap = (span - count * tileSize) / (count - 1);
            for (int i = 0; i < count; i++)
                positions.Add(start + i * (tileSize + gap));
            return positions;
        }

        positions.Add(fallbackPosition);
        return positions;
    }

    private static void DrawClippedImage(
        RGraphics g,
        RImage image,
        RectangleF destRect,
        RectangleF srcRect,
        RectangleF clipRect)
    {
        var visibleDest = RectangleF.Intersect(destRect, clipRect);
        if (visibleDest.Width <= 0 || visibleDest.Height <= 0)
            return;

        if (visibleDest == destRect)
        {
            g.DrawImage(image, destRect, srcRect);
            return;
        }

        float scaleX = srcRect.Width / destRect.Width;
        float scaleY = srcRect.Height / destRect.Height;
        var visibleSrc = new RectangleF(
            srcRect.X + ((visibleDest.X - destRect.X) * scaleX),
            srcRect.Y + ((visibleDest.Y - destRect.Y) * scaleY),
            visibleDest.Width * scaleX,
            visibleDest.Height * scaleY);

        if ((visibleSrc.Width < 0.5f || visibleSrc.Height < 0.5f)
            && image.TryGetSampledColor(visibleSrc, out var sampledColor))
        {
            using var brush = g.GetSolidBrush(sampledColor);
            g.DrawRectangle(brush, visibleDest.X, visibleDest.Y, visibleDest.Width, visibleDest.Height);
            return;
        }

        g.DrawImage(image, visibleDest, visibleSrc);
    }

    private static void RenderDrawTiledGradient(RGraphics g, DrawTiledGradientItem item)
    {
        int tileW = (int)Math.Max(1, item.TileWidth);
        int tileH = (int)Math.Max(1, item.TileHeight);
        var fill = item.FillRect;
        var origin = item.TileOrigin;

        if (item.Stops == null || item.Stops.Count == 0)
        {
            return; // No stops to render.
        }

        var (colors, positions) = ExpandGradientStops(item.Stops, item.InterpolationSpace);

        using var tileImage = g.CreateLinearGradientTile(tileW, tileH, colors, positions, item.Angle);
        if (tileImage == null)
            return;

        var srcRect = new RectangleF(0, 0, tileW, tileH);

        // Clip to the element's fill area.
        var clip = fill;
        clip.Intersect(g.GetClip());
        g.PushClip(clip);

        switch (item.Repeat)
        {
            case "no-repeat":
                g.DrawImage(tileImage, new RectangleF(origin.X, origin.Y, tileW, tileH), srcRect);
                break;
            case "repeat-x":
            {
                float ox = origin.X;
                while (ox > fill.X) ox -= tileW;
                using var brush = g.GetTextureBrush(tileImage, srcRect, new PointF(ox, origin.Y));
                g.DrawRectangle(brush, fill.X, origin.Y, fill.Width, tileH);
                break;
            }
            case "repeat-y":
            {
                float oy = origin.Y;
                while (oy > fill.Y) oy -= tileH;
                using var brush = g.GetTextureBrush(tileImage, srcRect, new PointF(origin.X, oy));
                g.DrawRectangle(brush, origin.X, fill.Y, tileW, fill.Height);
                break;
            }
            default: // "repeat"
            {
                float ox = origin.X;
                while (ox > fill.X) ox -= tileW;
                float oy = origin.Y;
                while (oy > fill.Y) oy -= tileH;
                using var brush = g.GetTextureBrush(tileImage, srcRect, new PointF(ox, oy));
                g.DrawRectangle(brush, fill.X, fill.Y, fill.Width, fill.Height);
                break;
            }
        }

        g.PopClip();
    }

    private static (Color[] colors, float[] positions) ExpandGradientStops(IReadOnlyList<GradientStop> stops, string interpolationSpace)
    {
        if (stops.Count == 0)
            return ([], []);

        if (!interpolationSpace.Equals("hsl", StringComparison.OrdinalIgnoreCase)
            && !interpolationSpace.Equals("oklch", StringComparison.OrdinalIgnoreCase))
        {
            var directColors = new Color[stops.Count];
            var directPositions = new float[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                directColors[i] = stops[i].Color;
                directPositions[i] = stops[i].Position;
            }

            return (directColors, directPositions);
        }

        const int samplesPerSegment = 24;
        var expandedColors = new List<Color>();
        var expandedPositions = new List<float>();

        for (int i = 0; i < stops.Count - 1; i++)
        {
            var start = stops[i];
            var end = stops[i + 1];

            for (int step = 0; step < samplesPerSegment; step++)
            {
                float t = step / (float)samplesPerSegment;
                expandedColors.Add(InterpolateColor(start.Color, end.Color, t, interpolationSpace));
                expandedPositions.Add(Lerp(start.Position, end.Position, t));
            }
        }

        expandedColors.Add(stops[^1].Color);
        expandedPositions.Add(stops[^1].Position);
        return (expandedColors.ToArray(), expandedPositions.ToArray());
    }

    private static Color InterpolateColor(Color start, Color end, float t, string interpolationSpace)
    {
        if (interpolationSpace.Equals("hsl", StringComparison.OrdinalIgnoreCase))
        {
            var startHsl = RgbToHsl(start);
            var endHsl = RgbToHsl(end);
            double h = InterpolateHue(startHsl.h, endHsl.h, t);
            double s = Lerp(startHsl.s, endHsl.s, t);
            double l = Lerp(startHsl.l, endHsl.l, t);
            return HslToRgb(h, s, l, Lerp(start.A, end.A, t));
        }

        if (interpolationSpace.Equals("oklch", StringComparison.OrdinalIgnoreCase))
        {
            var startLch = RgbToOklch(start);
            var endLch = RgbToOklch(end);
            double l = Lerp(startLch.l, endLch.l, t);
            double c = Lerp(startLch.c, endLch.c, t);
            double h = InterpolateHue(startLch.h, endLch.h, t);
            return OklchToRgb(l, c, h, Lerp(start.A, end.A, t));
        }

        return Color.FromArgb(
            ClampByte(Lerp(start.A, end.A, t)),
            ClampByte(Lerp(start.R, end.R, t)),
            ClampByte(Lerp(start.G, end.G, t)),
            ClampByte(Lerp(start.B, end.B, t)));
    }

    private static (double h, double s, double l) RgbToHsl(Color color)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0;
        double l = (max + min) / 2d;
        double d = max - min;
        double s = 0;

        if (d > 0)
        {
            s = d / (1d - Math.Abs(2d * l - 1d));
            h = max == r
                ? 60d * (((g - b) / d) % 6d)
                : max == g
                    ? 60d * (((b - r) / d) + 2d)
                    : 60d * (((r - g) / d) + 4d);
        }

        if (h < 0)
            h += 360d;

        return (h, s, l);
    }

    private static Color HslToRgb(double h, double s, double l, double alpha)
    {
        double c = (1d - Math.Abs(2d * l - 1d)) * s;
        double x = c * (1d - Math.Abs((h / 60d) % 2d - 1d));
        double m = l - c / 2d;

        double r1, g1, b1;
        if (h < 60d) (r1, g1, b1) = (c, x, 0);
        else if (h < 120d) (r1, g1, b1) = (x, c, 0);
        else if (h < 180d) (r1, g1, b1) = (0, c, x);
        else if (h < 240d) (r1, g1, b1) = (0, x, c);
        else if (h < 300d) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        return Color.FromArgb(
            ClampByte(alpha),
            ClampByte((r1 + m) * 255d),
            ClampByte((g1 + m) * 255d),
            ClampByte((b1 + m) * 255d));
    }

    private static (double l, double c, double h) RgbToOklch(Color color)
    {
        double r = SrgbToLinear(color.R / 255d);
        double g = SrgbToLinear(color.G / 255d);
        double b = SrgbToLinear(color.B / 255d);

        double l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        double m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        double s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

        double lRoot = Math.Cbrt(l);
        double mRoot = Math.Cbrt(m);
        double sRoot = Math.Cbrt(s);

        double okL = 0.2104542553 * lRoot + 0.7936177850 * mRoot - 0.0040720468 * sRoot;
        double okA = 1.9779984951 * lRoot - 2.4285922050 * mRoot + 0.4505937099 * sRoot;
        double okB = 0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.8086757660 * sRoot;

        double c = Math.Sqrt(okA * okA + okB * okB);
        double h = Math.Atan2(okB, okA) * 180d / Math.PI;
        if (h < 0)
            h += 360d;

        return (okL, c, h);
    }

    private static Color OklchToRgb(double l, double c, double h, double alpha)
    {
        double radians = h * Math.PI / 180d;
        double okA = c * Math.Cos(radians);
        double okB = c * Math.Sin(radians);

        double lRoot = l + 0.3963377774 * okA + 0.2158037573 * okB;
        double mRoot = l - 0.1055613458 * okA - 0.0638541728 * okB;
        double sRoot = l - 0.0894841775 * okA - 1.2914855480 * okB;

        double lLinear = lRoot * lRoot * lRoot;
        double mLinear = mRoot * mRoot * mRoot;
        double sLinear = sRoot * sRoot * sRoot;

        double r = LinearToSrgb(+4.0767416621 * lLinear - 3.3077115913 * mLinear + 0.2309699292 * sLinear);
        double g = LinearToSrgb(-1.2684380046 * lLinear + 2.6097574011 * mLinear - 0.3413193965 * sLinear);
        double b = LinearToSrgb(-0.0041960863 * lLinear - 0.7034186147 * mLinear + 1.7076147010 * sLinear);

        return Color.FromArgb(
            ClampByte(alpha),
            ClampByte(r * 255d),
            ClampByte(g * 255d),
            ClampByte(b * 255d));
    }

    private static double SrgbToLinear(double channel)
        => channel <= 0.04045d ? channel / 12.92d : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

    private static double LinearToSrgb(double channel)
    {
        channel = Math.Clamp(channel, 0d, 1d);
        return channel <= 0.0031308d ? channel * 12.92d : 1.055d * Math.Pow(channel, 1d / 2.4d) - 0.055d;
    }

    private static double InterpolateHue(double start, double end, double t)
    {
        double delta = ((end - start + 540d) % 360d) - 180d;
        double hue = (start + delta * t) % 360d;
        return hue < 0 ? hue + 360d : hue;
    }

    private static float Lerp(float start, float end, float t) => start + (end - start) * t;
    private static double Lerp(double start, double end, double t) => start + (end - start) * t;
    private static int ClampByte(double value) => (int)Math.Clamp(Math.Round(value), 0d, 255d);

    private static void RenderDrawLine(RGraphics g, DrawLineItem item)
    {
        var pen = g.GetPen(item.Color);
        pen.Width = item.Width;
        pen.DashStyle = item.DashStyle switch
        {
            "dotted" => DashStyle.Dot,
            "dashed" => DashStyle.Dash,
            _ => DashStyle.Solid,
        };
        g.DrawLine(pen, item.Start.X, item.Start.Y, item.End.X, item.End.Y);
    }

    private static void RenderSvgRect(RGraphics g, DrawSvgRectItem item)
    {
        double x = item.Bounds.X + item.X;
        double y = item.Bounds.Y + item.Y;
        if (!item.Fill.IsEmpty && item.Fill.A > 0)
            g.DrawRectangle(g.GetSolidBrush(item.Fill), x, y, item.Width, item.Height);
        if (!item.Stroke.IsEmpty && item.Stroke.A > 0 && item.StrokeWidth > 0)
        {
            var pen = g.GetPen(item.Stroke);
            pen.Width = item.StrokeWidth;
            g.DrawRectangle(pen, x, y, item.Width, item.Height);
        }
    }

    private static void RenderSvgEllipse(RGraphics g, DrawSvgEllipseItem item)
    {
        if (item.Rx <= 0 || item.Ry <= 0)
            return;

        var points = CreateEllipsePoints(
            item.Bounds.X + item.Cx,
            item.Bounds.Y + item.Cy,
            item.Rx,
            item.Ry);

        if (!item.Fill.IsEmpty && item.Fill.A > 0)
            g.DrawPolygon(g.GetSolidBrush(item.Fill), points);

        if (!item.Stroke.IsEmpty && item.Stroke.A > 0 && item.StrokeWidth > 0)
        {
            var pen = g.GetPen(item.Stroke);
            pen.Width = item.StrokeWidth;

            for (int i = 1; i < points.Length; i++)
                g.DrawLine(pen, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y);

            g.DrawLine(pen, points[^1].X, points[^1].Y, points[0].X, points[0].Y);
        }
    }

    private static void RenderSvgText(RGraphics g, DrawSvgTextItem item)
    {
        if (string.IsNullOrEmpty(item.Text))
            return;
        if (item.FontHandle is RFont font)
        {
            var origin = new PointF(item.Bounds.X + item.X, item.Bounds.Y + item.Y);
            var size = new SizeF(item.Bounds.Width, item.Bounds.Height);
            g.DrawString(item.Text, font, item.Fill, origin, size, false);
        }
    }

    private static void RenderSvgLine(RGraphics g, DrawSvgLineItem item)
    {
        if (!item.Stroke.IsEmpty && item.Stroke.A > 0 && item.StrokeWidth > 0)
        {
            var pen = g.GetPen(item.Stroke);
            pen.Width = item.StrokeWidth;
            g.DrawLine(pen,
                item.Bounds.X + item.X1, item.Bounds.Y + item.Y1,
                item.Bounds.X + item.X2, item.Bounds.Y + item.Y2);
        }
    }

    private static void RenderSvgPolygon(RGraphics g, DrawSvgPolygonItem item)
    {
        if (item.Points.Count < 2)
            return;

        var points = ToAbsolutePoints(item.Bounds, item.Points);
        if (!item.Fill.IsEmpty && item.Fill.A > 0)
            g.DrawPolygon(g.GetSolidBrush(item.Fill), points);

        if (!item.Stroke.IsEmpty && item.Stroke.A > 0 && item.StrokeWidth > 0)
        {
            var pen = g.GetPen(item.Stroke);
            pen.Width = item.StrokeWidth;
            for (var i = 1; i < points.Length; i++)
                g.DrawLine(pen, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y);
            g.DrawLine(pen, points[^1].X, points[^1].Y, points[0].X, points[0].Y);
        }
    }

    private static void RenderSvgPolyline(RGraphics g, DrawSvgPolylineItem item)
    {
        if (item.Points.Count < 2)
            return;

        var points = ToAbsolutePoints(item.Bounds, item.Points);
        if (!item.Fill.IsEmpty && item.Fill.A > 0)
            g.DrawPolygon(g.GetSolidBrush(item.Fill), points);

        if (!item.Stroke.IsEmpty && item.Stroke.A > 0 && item.StrokeWidth > 0)
        {
            var pen = g.GetPen(item.Stroke);
            pen.Width = item.StrokeWidth;
            for (var i = 1; i < points.Length; i++)
                g.DrawLine(pen, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y);
        }
    }

    private static PointF[] ToAbsolutePoints(RectangleF bounds, IReadOnlyList<PointF> points)
    {
        var absolute = new PointF[points.Count];
        for (var i = 0; i < points.Count; i++)
            absolute[i] = new PointF(bounds.X + points[i].X, bounds.Y + points[i].Y);
        return absolute;
    }

    private static RPen CreateBorderPen(RGraphics g, string style, Color color, double width)
    {
        var pen = g.GetPen(color);
        pen.Width = width;
        pen.DashStyle = style switch
        {
            "dotted" => DashStyle.Dot,
            "dashed" => DashStyle.Dash,
            _ => DashStyle.Solid,
        };
        return pen;
    }

    private static bool IsBorderStyleVisible(string style) => !string.IsNullOrEmpty(style) && style != "none" && style != "hidden";

    private static PointF[] CreateEllipsePoints(float centerX, float centerY, float radiusX, float radiusY)
    {
        // Use roughly half a point per output pixel along the larger radius, with
        // a floor for small shapes and a ceiling to keep replay costs bounded.
        int segmentCount = Math.Clamp((int)Math.Ceiling(Math.PI * Math.Max(radiusX, radiusY)), 16, 128);
        var points = new PointF[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            double angle = (Math.PI * 2d * i) / segmentCount;
            points[i] = new PointF(
                centerX + (float)(Math.Cos(angle) * radiusX),
                centerY + (float)(Math.Sin(angle) * radiusY));
        }

        return points;
    }
}
