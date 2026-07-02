using System.Drawing;
using System;
using Broiler.Graphics;
using Broiler.HTML.Core;
using Broiler.CSS;


namespace Broiler.HTML.Rendering.Handlers;

internal sealed class BordersDrawHandler : IBordersDrawHandler
{
    public static readonly BordersDrawHandler Instance = new();

    private static readonly PointF[] _borderPts = new PointF[4];

    public static void DrawBoxBorders(RGraphics g, IBorderRenderData box, RectangleF rect, bool isFirst, bool isLast)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Fill corner rectangles to prevent anti-aliased seams along
        // the diagonal edges where two same-color border trapezoids meet.
        FillBorderCorners(g, box, rect, isFirst, isLast);

        if (!(string.IsNullOrEmpty(box.BorderTopStyle) || box.BorderTopStyle == CssConstants.None || box.BorderTopStyle == CssConstants.Hidden) && box.ActualBorderTopWidth > 0)
            DrawBorder(Border.Top, box, g, rect, isFirst, isLast);

        if (isFirst && !(string.IsNullOrEmpty(box.BorderLeftStyle) || box.BorderLeftStyle == CssConstants.None || box.BorderLeftStyle == CssConstants.Hidden) && box.ActualBorderLeftWidth > 0)
            DrawBorder(Border.Left, box, g, rect, true, isLast);

        if (!(string.IsNullOrEmpty(box.BorderBottomStyle) || box.BorderBottomStyle == CssConstants.None || box.BorderBottomStyle == CssConstants.Hidden) && box.ActualBorderBottomWidth > 0)
            DrawBorder(Border.Bottom, box, g, rect, isFirst, isLast);

        if (isLast && !(string.IsNullOrEmpty(box.BorderRightStyle) || box.BorderRightStyle == CssConstants.None || box.BorderRightStyle == CssConstants.Hidden) && box.ActualBorderRightWidth > 0)
            DrawBorder(Border.Right, box, g, rect, isFirst, true);
    }

    public static void DrawBorder(Border border, RGraphics g, IBorderRenderData box, RBrush brush, RectangleF rectangle)
    {
        SetInOutsetRectanglePoints(border, box, rectangle, true, true);
        g.DrawPolygon(brush, _borderPts);
    }

    private static void DrawBorder(Border border, IBorderRenderData box, RGraphics g, RectangleF rect, bool isLineStart, bool isLineEnd)
    {
        var style = GetStyle(border, box);
        var color = GetColor(border, box, style);

        var borderPath = GetRoundedBorderPath(g, border, box, rect);
        if (borderPath != null)
        {
            // rounded border need special path
            object prevMode = null;
            if (!box.AvoidGeometryAntialias && box.IsRounded)
                prevMode = g.SetAntiAliasSmoothingMode();

            var pen = GetPen(g, style, color, GetWidth(border, box));
            using (borderPath)
                g.DrawPath(pen, borderPath);

            g.ReturnPreviousSmoothingMode(prevMode);
        }
        else
        {
            // non rounded border
            if (style == CssConstants.Inset || style == CssConstants.Outset)
            {
                // inset/outset border needs special rectangle
                SetInOutsetRectanglePoints(border, box, rect, isLineStart, isLineEnd);
                g.DrawPolygon(g.GetSolidBrush(color), _borderPts);
            }
            else if (style == CssConstants.Solid)
            {
                // Trapezoid polygon rendering for correct corner joins
                // with asymmetric widths and anti-aliased diagonal edges.
                SetInOutsetRectanglePoints(border, box, rect, isLineStart, isLineEnd);
                g.DrawPolygon(g.GetSolidBrush(color), _borderPts);
            }
            else
            {
                // dotted/dashed border draw as simple line
                var pen = GetPen(g, style, color, GetWidth(border, box));
                switch (border)
                {
                    case Border.Top:
                        g.DrawLine(pen, Math.Ceiling(rect.Left), rect.Top + box.ActualBorderTopWidth / 2, rect.Right - 1, rect.Top + box.ActualBorderTopWidth / 2);
                        break;
                    case Border.Left:
                        g.DrawLine(pen, rect.Left + box.ActualBorderLeftWidth / 2, Math.Ceiling(rect.Top), rect.Left + box.ActualBorderLeftWidth / 2, Math.Floor(rect.Bottom));
                        break;
                    case Border.Bottom:
                        g.DrawLine(pen, Math.Ceiling(rect.Left), rect.Bottom - box.ActualBorderBottomWidth / 2, rect.Right - 1, rect.Bottom - box.ActualBorderBottomWidth / 2);
                        break;
                    case Border.Right:
                        g.DrawLine(pen, rect.Right - box.ActualBorderRightWidth / 2, Math.Ceiling(rect.Top), rect.Right - box.ActualBorderRightWidth / 2, Math.Floor(rect.Bottom));
                        break;
                }
            }
        }
    }

    private static void SetInOutsetRectanglePoints(Border border, IBorderRenderData b, RectangleF r, bool isLineStart, bool isLineEnd)
    {
        switch (border)
        {
            case Border.Top:
                _borderPts[0] = new PointF(r.Left, r.Top);
                _borderPts[1] = new PointF(r.Right, r.Top);
                _borderPts[2] = new PointF(r.Right, (float)(r.Top + b.ActualBorderTopWidth));
                _borderPts[3] = new PointF(r.Left, (float)(r.Top + b.ActualBorderTopWidth));
                if (isLineEnd)
                    _borderPts[2].X -= (float)b.ActualBorderRightWidth;
                if (isLineStart)
                    _borderPts[3].X += (float)b.ActualBorderLeftWidth;
                break;
            case Border.Right:
                _borderPts[0] = new PointF((float)(r.Right - b.ActualBorderRightWidth), (float)(r.Top + b.ActualBorderTopWidth));
                _borderPts[1] = new PointF(r.Right, r.Top);
                _borderPts[2] = new PointF(r.Right, r.Bottom);
                _borderPts[3] = new PointF((float)(r.Right - b.ActualBorderRightWidth), (float)(r.Bottom - b.ActualBorderBottomWidth));
                break;
            case Border.Bottom:
                _borderPts[0] = new PointF(r.Left, (float)(r.Bottom - b.ActualBorderBottomWidth));
                _borderPts[1] = new PointF(r.Right, (float)(r.Bottom - b.ActualBorderBottomWidth));
                _borderPts[2] = new PointF(r.Right, r.Bottom);
                _borderPts[3] = new PointF(r.Left, r.Bottom);
                if (isLineStart)
                    _borderPts[0].X += (float)b.ActualBorderLeftWidth;
                if (isLineEnd)
                    _borderPts[1].X -= (float)b.ActualBorderRightWidth;
                break;
            case Border.Left:
                _borderPts[0] = new PointF(r.Left, r.Top);
                _borderPts[1] = new PointF((float)(r.Left + b.ActualBorderLeftWidth), (float)(r.Top + b.ActualBorderTopWidth));
                _borderPts[2] = new PointF((float)(r.Left + b.ActualBorderLeftWidth), (float)(r.Bottom - b.ActualBorderBottomWidth));
                _borderPts[3] = new PointF(r.Left, r.Bottom);
                break;
        }
    }

    private static RGraphicsPath GetRoundedBorderPath(RGraphics g, Border border, IBorderRenderData b, RectangleF r)
    {
        RGraphicsPath path = null;
        switch (border)
        {
            case Border.Top:
                if (b.ActualCornerNw <= 0 && b.ActualCornerNe <= 0)
                    break;

                path = g.GetGraphicsPath();
                path.Start(r.Left + b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + b.ActualCornerNw);

                if (b.ActualCornerNw > 0)
                    path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2 + b.ActualCornerNw, r.Top + b.ActualBorderTopWidth / 2, b.ActualCornerNw, Broiler.Graphics.Corner.TopLeft);

                path.LineTo(r.Right - b.ActualBorderRightWidth / 2 - b.ActualCornerNe, r.Top + b.ActualBorderTopWidth / 2);

                if (b.ActualCornerNe > 0)
                    path.ArcTo(r.Right - b.ActualBorderRightWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + b.ActualCornerNe, b.ActualCornerNe, Broiler.Graphics.Corner.TopRight);
                
                break;

            case Border.Bottom:
                if (b.ActualCornerSw <= 0 && b.ActualCornerSe <= 0)
                    break;

                path = g.GetGraphicsPath();
                path.Start(r.Right - b.ActualBorderRightWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - b.ActualCornerSe);

                if (b.ActualCornerSe > 0)
                    path.ArcTo(r.Right - b.ActualBorderRightWidth / 2 - b.ActualCornerSe, r.Bottom - b.ActualBorderBottomWidth / 2, b.ActualCornerSe, Broiler.Graphics.Corner.BottomRight);

                path.LineTo(r.Left + b.ActualBorderLeftWidth / 2 + b.ActualCornerSw, r.Bottom - b.ActualBorderBottomWidth / 2);

                if (b.ActualCornerSw > 0)
                    path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - b.ActualCornerSw, b.ActualCornerSw, Broiler.Graphics.Corner.BottomLeft);
                
                break;

            case Border.Right:
                if (b.ActualCornerNe <= 0 && b.ActualCornerSe <= 0)
                    break;

                path = g.GetGraphicsPath();

                bool noTop = b.BorderTopStyle == CssConstants.None || b.BorderTopStyle == CssConstants.Hidden;
                bool noBottom = b.BorderBottomStyle == CssConstants.None || b.BorderBottomStyle == CssConstants.Hidden;
                path.Start(r.Right - b.ActualBorderRightWidth / 2 - (noTop ? b.ActualCornerNe : 0), r.Top + b.ActualBorderTopWidth / 2 + (noTop ? 0 : b.ActualCornerNe));

                if (b.ActualCornerNe > 0 && noTop)
                    path.ArcTo(r.Right - b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + b.ActualCornerNe, b.ActualCornerNe, Broiler.Graphics.Corner.TopRight);

                path.LineTo(r.Right - b.ActualBorderRightWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - b.ActualCornerSe);

                if (b.ActualCornerSe > 0 && noBottom)
                    path.ArcTo(r.Right - b.ActualBorderRightWidth / 2 - b.ActualCornerSe, r.Bottom - b.ActualBorderBottomWidth / 2, b.ActualCornerSe, Broiler.Graphics.Corner.BottomRight);
                break;
            case Border.Left:
                if (b.ActualCornerNw <= 0 && b.ActualCornerSw <= 0)
                    break;

                path = g.GetGraphicsPath();

                noTop = b.BorderTopStyle == CssConstants.None || b.BorderTopStyle == CssConstants.Hidden;
                noBottom = b.BorderBottomStyle == CssConstants.None || b.BorderBottomStyle == CssConstants.Hidden;
                path.Start(r.Left + b.ActualBorderLeftWidth / 2 + (noBottom ? b.ActualCornerSw : 0), r.Bottom - b.ActualBorderBottomWidth / 2 - (noBottom ? 0 : b.ActualCornerSw));

                if (b.ActualCornerSw > 0 && noBottom)
                    path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2, r.Bottom - b.ActualBorderBottomWidth / 2 - b.ActualCornerSw, b.ActualCornerSw, Broiler.Graphics.Corner.BottomLeft);

                path.LineTo(r.Left + b.ActualBorderLeftWidth / 2, r.Top + b.ActualBorderTopWidth / 2 + b.ActualCornerNw);

                if (b.ActualCornerNw > 0 && noTop)
                    path.ArcTo(r.Left + b.ActualBorderLeftWidth / 2 + b.ActualCornerNw, r.Top + b.ActualBorderTopWidth / 2, b.ActualCornerNw, Broiler.Graphics.Corner.TopLeft);

                break;
        }

        return path;
    }

    /// <summary>
    /// Fills corner rectangles where two adjacent solid borders share the same color.
    /// This prevents visible anti-aliased seams along the diagonal edge where the
    /// two border trapezoids meet, which would otherwise let the background bleed through.
    /// </summary>
    private static void FillBorderCorners(RGraphics g, IBorderRenderData box, RectangleF rect, bool isFirst, bool isLast)
    {
        bool hasTop = IsBorderVisible(box.BorderTopStyle) && box.ActualBorderTopWidth > 0 && box.BorderTopStyle == CssConstants.Solid;
        bool hasRight = isLast && IsBorderVisible(box.BorderRightStyle) && box.ActualBorderRightWidth > 0 && box.BorderRightStyle == CssConstants.Solid;
        bool hasBottom = IsBorderVisible(box.BorderBottomStyle) && box.ActualBorderBottomWidth > 0 && box.BorderBottomStyle == CssConstants.Solid;
        bool hasLeft = isFirst && IsBorderVisible(box.BorderLeftStyle) && box.ActualBorderLeftWidth > 0 && box.BorderLeftStyle == CssConstants.Solid;

        // Top-left corner
        if (hasTop && hasLeft && box.ActualBorderTopColor == box.ActualBorderLeftColor)
            g.DrawRectangle(g.GetSolidBrush(box.ActualBorderTopColor), rect.Left, rect.Top, (float)box.ActualBorderLeftWidth, (float)box.ActualBorderTopWidth);

        // Top-right corner
        if (hasTop && hasRight && box.ActualBorderTopColor == box.ActualBorderRightColor)
            g.DrawRectangle(g.GetSolidBrush(box.ActualBorderTopColor), (float)(rect.Right - box.ActualBorderRightWidth), rect.Top, 
                (float)box.ActualBorderRightWidth, (float)box.ActualBorderTopWidth);

        // Bottom-left corner
        if (hasBottom && hasLeft && box.ActualBorderBottomColor == box.ActualBorderLeftColor)
            g.DrawRectangle(g.GetSolidBrush(box.ActualBorderBottomColor),
                rect.Left, (float)(rect.Bottom - box.ActualBorderBottomWidth),
                (float)box.ActualBorderLeftWidth, (float)box.ActualBorderBottomWidth);

        // Bottom-right corner
        if (hasBottom && hasRight && box.ActualBorderBottomColor == box.ActualBorderRightColor)
            g.DrawRectangle(g.GetSolidBrush(box.ActualBorderBottomColor),
                (float)(rect.Right - box.ActualBorderRightWidth), (float)(rect.Bottom - box.ActualBorderBottomWidth),
                (float)box.ActualBorderRightWidth, (float)box.ActualBorderBottomWidth);
    }

    private static bool IsBorderVisible(string style)
        => !string.IsNullOrEmpty(style) && style != CssConstants.None && style != CssConstants.Hidden;

    private static RPen GetPen(RGraphics g, string style, BColor color, double width)
    {
        var p = g.GetPen(color);
        p.Width = width;
        switch (style)
        {
            case "solid":
                p.DashStyle = Graphics.DashStyle.Solid;
                break;
            case "dotted":
                p.DashStyle = Graphics.DashStyle.Dot;
                break;
            case "dashed":
                p.DashStyle = Graphics.DashStyle.Dash;
                break;
        }
        return p;
    }

    private static BColor GetColor(Border border, IBorderRenderData box, string style) => border switch
    {
        Border.Top => style == CssConstants.Inset ? Darken(box.ActualBorderTopColor) : box.ActualBorderTopColor,
        Border.Right => style == CssConstants.Outset ? Darken(box.ActualBorderRightColor) : box.ActualBorderRightColor,
        Border.Bottom => style == CssConstants.Outset ? Darken(box.ActualBorderBottomColor) : box.ActualBorderBottomColor,
        Border.Left => style == CssConstants.Inset ? Darken(box.ActualBorderLeftColor) : box.ActualBorderLeftColor,
        _ => throw new ArgumentOutOfRangeException(nameof(border)),
    };

    private static double GetWidth(Border border, IBorderRenderData box) => border switch
    {
        Border.Top => box.ActualBorderTopWidth,
        Border.Right => box.ActualBorderRightWidth,
        Border.Bottom => box.ActualBorderBottomWidth,
        Border.Left => box.ActualBorderLeftWidth,
        _ => throw new ArgumentOutOfRangeException(nameof(border)),
    };

    private static string GetStyle(Border border, IBorderRenderData box) => border switch
    {
        Border.Top => box.BorderTopStyle,
        Border.Right => box.BorderRightStyle,
        Border.Bottom => box.BorderBottomStyle,
        Border.Left => box.BorderLeftStyle,
        _ => throw new ArgumentOutOfRangeException(nameof(border)),
    };

    private static BColor Darken(BColor c) => BColor.FromArgb(c.R / 2, c.G / 2, c.B / 2);

    void IBordersDrawHandler.DrawBoxBorders(RGraphics g, IBorderRenderData box, RectangleF rect, bool isFirst, bool isLast)
        => DrawBoxBorders(g, box, rect, isFirst, isLast);

    void IBordersDrawHandler.DrawBorder(Border border, RGraphics g, IBorderRenderData box, RBrush brush, RectangleF rectangle)
        => DrawBorder(border, g, box, brush, rectangle);
}