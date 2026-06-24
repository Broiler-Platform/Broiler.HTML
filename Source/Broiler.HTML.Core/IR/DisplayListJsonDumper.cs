using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace Broiler.HTML.Core.IR;

/// <summary>
/// Serialises a <see cref="DisplayList"/> to deterministic JSON for
/// golden-file comparison and debugging. Coordinates are rounded to 2
/// decimal places for stability across runs. Platform-specific handles
/// (<c>FontHandle</c>, <c>ImageHandle</c>) are excluded.
/// </summary>
public static class DisplayListJsonDumper
{
    /// <summary>
    /// Serialises <paramref name="displayList"/> to indented, deterministic JSON.
    /// </summary>
    public static string ToJson(DisplayList displayList)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.Append("  \"items\": ");

        if (displayList.Items.Count == 0)
        {
            sb.AppendLine("[]");
        }
        else
        {
            sb.AppendLine("[");
            for (int i = 0; i < displayList.Items.Count; i++)
            {
                WriteItem(sb, displayList.Items[i], indent: 4);
                sb.AppendLine(i < displayList.Items.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
        }

        sb.Append('}');
        sb.AppendLine();
        return sb.ToString();
    }

    private static void WriteItem(StringBuilder sb, DisplayItem item, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);

        sb.Append(pad).AppendLine("{");

        // Type discriminator
        sb.Append(pad2).Append("\"$type\": \"").Append(GetTypeName(item)).AppendLine("\",");

        // Bounds (common to all items)
        bool hasExtraProps = HasExtraProperties(item);
        sb.Append(pad2).Append("\"bounds\": ");
        WriteRect(sb, item.Bounds);
        sb.AppendLine(hasExtraProps ? "," : "");

        // Type-specific properties
        switch (item)
        {
            case FillRectItem fill:
                sb.Append(pad2).Append("\"color\": \"").Append(ColorToString(fill.Color)).AppendLine("\"");
                break;

            case DrawBorderItem border:
                sb.Append(pad2).Append("\"topColor\": \"").Append(ColorToString(border.TopColor)).AppendLine("\",");
                sb.Append(pad2).Append("\"rightColor\": \"").Append(ColorToString(border.RightColor)).AppendLine("\",");
                sb.Append(pad2).Append("\"bottomColor\": \"").Append(ColorToString(border.BottomColor)).AppendLine("\",");
                sb.Append(pad2).Append("\"leftColor\": \"").Append(ColorToString(border.LeftColor)).AppendLine("\",");
                sb.Append(pad2).Append("\"topStyle\": \"").Append(EscapeJsonString(border.TopStyle)).AppendLine("\",");
                sb.Append(pad2).Append("\"rightStyle\": \"").Append(EscapeJsonString(border.RightStyle)).AppendLine("\",");
                sb.Append(pad2).Append("\"bottomStyle\": \"").Append(EscapeJsonString(border.BottomStyle)).AppendLine("\",");
                sb.Append(pad2).Append("\"leftStyle\": \"").Append(EscapeJsonString(border.LeftStyle)).AppendLine("\",");
                sb.Append(pad2).Append("\"widths\": ");
                WriteBoxEdges(sb, border.Widths);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"cornerNw\": ").Append(Round(border.CornerNw)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerNe\": ").Append(Round(border.CornerNe)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSe\": ").Append(Round(border.CornerSe)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSw\": ").Append(Round(border.CornerSw));
                sb.AppendLine();
                break;

            case DrawTextItem text:
                sb.Append(pad2).Append("\"text\": \"").Append(EscapeJsonString(text.Text)).AppendLine("\",");
                sb.Append(pad2).Append("\"fontFamily\": \"").Append(EscapeJsonString(text.FontFamily)).AppendLine("\",");
                sb.Append(pad2).Append("\"fontSize\": ").Append(Round(text.FontSize)).AppendLine(",");
                sb.Append(pad2).Append("\"fontWeight\": \"").Append(EscapeJsonString(text.FontWeight)).AppendLine("\",");
                sb.Append(pad2).Append("\"color\": \"").Append(ColorToString(text.Color)).AppendLine("\",");
                sb.Append(pad2).Append("\"origin\": ");
                WritePoint(sb, text.Origin);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"isRtl\": ").Append(text.IsRtl ? "true" : "false").AppendLine(",");
                sb.Append(pad2).Append("\"glyphRotationDeg\": ").Append(Round(text.GlyphRotationDeg)).AppendLine(",");
                sb.Append(pad2).Append("\"textShadowOffsetX\": ").Append(Round(text.TextShadowOffsetX)).AppendLine(",");
                sb.Append(pad2).Append("\"textShadowOffsetY\": ").Append(Round(text.TextShadowOffsetY)).AppendLine(",");
                sb.Append(pad2).Append("\"textShadowColor\": \"").Append(ColorToString(text.TextShadowColor)).AppendLine("\",");
                sb.Append(pad2).Append("\"gradientAngle\": ").Append(Round(text.GradientAngle)).AppendLine(",");
                sb.Append(pad2).Append("\"gradientInterpolationSpace\": \"").Append(EscapeJsonString(text.GradientInterpolationSpace)).AppendLine("\",");
                sb.Append(pad2).Append("\"gradientBounds\": ");
                WriteRect(sb, text.GradientBounds);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"gradientStops\": ");
                WriteGradientStops(sb, text.GradientStops);
                sb.AppendLine();
                break;

            case DrawImageItem image:
                sb.Append(pad2).Append("\"sourceRect\": ");
                WriteRect(sb, image.SourceRect);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"destRect\": ");
                WriteRect(sb, image.DestRect);
                sb.AppendLine();
                break;

            case DrawTiledImageItem image:
                sb.Append(pad2).Append("\"sourceRect\": ");
                WriteRect(sb, image.SourceRect);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"fillRect\": ");
                WriteRect(sb, image.FillRect);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"positioningArea\": ");
                WriteRect(sb, image.PositioningArea);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"tileOrigin\": ");
                WritePoint(sb, image.TileOrigin);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"repeat\": \"").Append(EscapeJsonString(image.Repeat)).AppendLine("\",");
                sb.Append(pad2).Append("\"tileWidth\": ").Append(Round(image.TileWidth)).AppendLine(",");
                sb.Append(pad2).Append("\"tileHeight\": ").Append(Round(image.TileHeight));
                sb.AppendLine();
                break;

            case ClipItem clip:
                sb.Append(pad2).Append("\"clipRect\": ");
                WriteRect(sb, clip.ClipRect);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"cornerNw\": ").Append(Round(clip.CornerNw)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerNwY\": ").Append(Round(clip.CornerNwY)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerNe\": ").Append(Round(clip.CornerNe)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerNeY\": ").Append(Round(clip.CornerNeY)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSe\": ").Append(Round(clip.CornerSe)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSeY\": ").Append(Round(clip.CornerSeY)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSw\": ").Append(Round(clip.CornerSw)).AppendLine(",");
                sb.Append(pad2).Append("\"cornerSwY\": ").Append(Round(clip.CornerSwY));
                sb.AppendLine();
                break;

            case RestoreItem:
            case RestoreOpacityItem:
            case RestoreBlendModeItem:
            case RestoreTransformItem:
                // No additional properties; keep bounds as the final field.
                break;

            case OpacityItem opacity:
                sb.Append(pad2).Append("\"opacity\": ").Append(Round(opacity.Opacity));
                sb.AppendLine();
                break;

            case BlendModeItem blend:
                sb.Append(pad2).Append("\"mode\": \"").Append(EscapeJsonString(blend.Mode)).Append('"');
                sb.AppendLine();
                break;

            case DrawLineItem line:
                sb.Append(pad2).Append("\"start\": ");
                WritePoint(sb, line.Start);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"end\": ");
                WritePoint(sb, line.End);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"color\": \"").Append(ColorToString(line.Color)).AppendLine("\",");
                sb.Append(pad2).Append("\"width\": ").Append(Round(line.Width)).AppendLine(",");
                sb.Append(pad2).Append("\"dashStyle\": \"").Append(EscapeJsonString(line.DashStyle)).Append('"');
                sb.AppendLine();
                break;

            case DrawTiledGradientItem gradient:
                sb.Append(pad2).Append("\"gradientFunction\": \"").Append(EscapeJsonString(gradient.GradientFunction)).AppendLine("\",");
                sb.Append(pad2).Append("\"tileWidth\": ").Append(Round(gradient.TileWidth)).AppendLine(",");
                sb.Append(pad2).Append("\"tileHeight\": ").Append(Round(gradient.TileHeight)).AppendLine(",");
                sb.Append(pad2).Append("\"fillRect\": ");
                WriteRect(sb, gradient.FillRect);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"tileOrigin\": ");
                WritePoint(sb, gradient.TileOrigin);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"repeat\": \"").Append(EscapeJsonString(gradient.Repeat)).AppendLine("\",");
                sb.Append(pad2).Append("\"angle\": ").Append(Round(gradient.Angle)).AppendLine(",");
                sb.Append(pad2).Append("\"interpolationSpace\": \"").Append(EscapeJsonString(gradient.InterpolationSpace)).AppendLine("\",");
                sb.Append(pad2).Append("\"isRadial\": ").Append(gradient.IsRadial ? "true" : "false").AppendLine(",");
                sb.Append(pad2).Append("\"isConic\": ").Append(gradient.IsConic ? "true" : "false").AppendLine(",");
                sb.Append(pad2).Append("\"centerX\": ").Append(Round(gradient.CenterX)).AppendLine(",");
                sb.Append(pad2).Append("\"centerY\": ").Append(Round(gradient.CenterY)).AppendLine(",");
                sb.Append(pad2).Append("\"fromAngle\": ").Append(Round(gradient.FromAngle)).AppendLine(",");
                sb.Append(pad2).Append("\"stops\": ");
                WriteGradientStops(sb, gradient.Stops);
                sb.AppendLine();
                break;

            case TransformItem transform:
                sb.Append(pad2).Append("\"matrix\": ");
                WriteNumberArray(sb, transform.Matrix);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"originX\": ").Append(Round(transform.OriginX)).AppendLine(",");
                sb.Append(pad2).Append("\"originY\": ").Append(Round(transform.OriginY));
                sb.AppendLine();
                break;

            case DrawSvgRectItem rect:
                sb.Append(pad2).Append("\"x\": ").Append(Round(rect.X)).AppendLine(",");
                sb.Append(pad2).Append("\"y\": ").Append(Round(rect.Y)).AppendLine(",");
                sb.Append(pad2).Append("\"width\": ").Append(Round(rect.Width)).AppendLine(",");
                sb.Append(pad2).Append("\"height\": ").Append(Round(rect.Height)).AppendLine(",");
                sb.Append(pad2).Append("\"fill\": \"").Append(ColorToString(rect.Fill)).AppendLine("\",");
                sb.Append(pad2).Append("\"stroke\": \"").Append(ColorToString(rect.Stroke)).AppendLine("\",");
                sb.Append(pad2).Append("\"strokeWidth\": ").Append(Round(rect.StrokeWidth));
                sb.AppendLine();
                break;

            case DrawSvgEllipseItem ellipse:
                sb.Append(pad2).Append("\"cx\": ").Append(Round(ellipse.Cx)).AppendLine(",");
                sb.Append(pad2).Append("\"cy\": ").Append(Round(ellipse.Cy)).AppendLine(",");
                sb.Append(pad2).Append("\"rx\": ").Append(Round(ellipse.Rx)).AppendLine(",");
                sb.Append(pad2).Append("\"ry\": ").Append(Round(ellipse.Ry)).AppendLine(",");
                sb.Append(pad2).Append("\"fill\": \"").Append(ColorToString(ellipse.Fill)).AppendLine("\",");
                sb.Append(pad2).Append("\"stroke\": \"").Append(ColorToString(ellipse.Stroke)).AppendLine("\",");
                sb.Append(pad2).Append("\"strokeWidth\": ").Append(Round(ellipse.StrokeWidth));
                sb.AppendLine();
                break;

            case DrawSvgTextItem svgText:
                sb.Append(pad2).Append("\"text\": \"").Append(EscapeJsonString(svgText.Text)).AppendLine("\",");
                sb.Append(pad2).Append("\"x\": ").Append(Round(svgText.X)).AppendLine(",");
                sb.Append(pad2).Append("\"y\": ").Append(Round(svgText.Y)).AppendLine(",");
                sb.Append(pad2).Append("\"fontSize\": ").Append(Round(svgText.FontSize)).AppendLine(",");
                sb.Append(pad2).Append("\"fontFamily\": \"").Append(EscapeJsonString(svgText.FontFamily)).AppendLine("\",");
                sb.Append(pad2).Append("\"fill\": \"").Append(ColorToString(svgText.Fill)).Append('"');
                sb.AppendLine();
                break;

            case DrawSvgLineItem svgLine:
                sb.Append(pad2).Append("\"x1\": ").Append(Round(svgLine.X1)).AppendLine(",");
                sb.Append(pad2).Append("\"y1\": ").Append(Round(svgLine.Y1)).AppendLine(",");
                sb.Append(pad2).Append("\"x2\": ").Append(Round(svgLine.X2)).AppendLine(",");
                sb.Append(pad2).Append("\"y2\": ").Append(Round(svgLine.Y2)).AppendLine(",");
                sb.Append(pad2).Append("\"stroke\": \"").Append(ColorToString(svgLine.Stroke)).AppendLine("\",");
                sb.Append(pad2).Append("\"strokeWidth\": ").Append(Round(svgLine.StrokeWidth));
                sb.AppendLine();
                break;

            case DrawSvgPolygonItem polygon:
                sb.Append(pad2).Append("\"points\": ");
                WritePoints(sb, polygon.Points);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"fill\": \"").Append(ColorToString(polygon.Fill)).AppendLine("\",");
                sb.Append(pad2).Append("\"stroke\": \"").Append(ColorToString(polygon.Stroke)).AppendLine("\",");
                sb.Append(pad2).Append("\"strokeWidth\": ").Append(Round(polygon.StrokeWidth));
                sb.AppendLine();
                break;

            case DrawSvgPolylineItem polyline:
                sb.Append(pad2).Append("\"points\": ");
                WritePoints(sb, polyline.Points);
                sb.AppendLine(",");
                sb.Append(pad2).Append("\"fill\": \"").Append(ColorToString(polyline.Fill)).AppendLine("\",");
                sb.Append(pad2).Append("\"stroke\": \"").Append(ColorToString(polyline.Stroke)).AppendLine("\",");
                sb.Append(pad2).Append("\"strokeWidth\": ").Append(Round(polyline.StrokeWidth));
                sb.AppendLine();
                break;
        }

        sb.Append(pad).Append('}');
    }

    private static bool HasExtraProperties(DisplayItem item) => item switch
    {
        RestoreItem or RestoreOpacityItem or RestoreBlendModeItem or RestoreTransformItem => false,
        FillRectItem or DrawBorderItem or DrawTextItem or DrawImageItem or DrawTiledImageItem
            or ClipItem or OpacityItem or BlendModeItem or DrawLineItem or DrawTiledGradientItem
            or TransformItem or DrawSvgRectItem or DrawSvgEllipseItem or DrawSvgTextItem
            or DrawSvgLineItem or DrawSvgPolygonItem or DrawSvgPolylineItem => true,
        _ => false
    };

    private static string GetTypeName(DisplayItem item) => item switch
    {
        FillRectItem => "FillRect",
        DrawBorderItem => "DrawBorder",
        DrawTextItem => "DrawText",
        DrawImageItem => "DrawImage",
        DrawTiledImageItem => "DrawTiledImage",
        ClipItem => "Clip",
        RestoreItem => "Restore",
        OpacityItem => "Opacity",
        RestoreOpacityItem => "RestoreOpacity",
        BlendModeItem => "BlendMode",
        RestoreBlendModeItem => "RestoreBlendMode",
        DrawLineItem => "DrawLine",
        DrawTiledGradientItem => "DrawTiledGradient",
        TransformItem => "Transform",
        RestoreTransformItem => "RestoreTransform",
        DrawSvgRectItem => "DrawSvgRect",
        DrawSvgEllipseItem => "DrawSvgEllipse",
        DrawSvgTextItem => "DrawSvgText",
        DrawSvgLineItem => "DrawSvgLine",
        DrawSvgPolygonItem => "DrawSvgPolygon",
        DrawSvgPolylineItem => "DrawSvgPolyline",
        _ => item.GetType().Name,
    };

    private static void WriteRect(StringBuilder sb, RectangleF r)
    {
        sb.Append("{ \"x\": ").Append(Round(r.X))
          .Append(", \"y\": ").Append(Round(r.Y))
          .Append(", \"width\": ").Append(Round(r.Width))
          .Append(", \"height\": ").Append(Round(r.Height))
          .Append(" }");
    }

    private static void WritePoint(StringBuilder sb, PointF p)
    {
        sb.Append("{ \"x\": ").Append(Round(p.X))
          .Append(", \"y\": ").Append(Round(p.Y))
          .Append(" }");
    }

    private static void WritePoints(StringBuilder sb, IReadOnlyList<PointF> points)
    {
        sb.Append('[');
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            WritePoint(sb, points[i]);
        }
        sb.Append(']');
    }

    private static void WriteNumberArray(StringBuilder sb, IReadOnlyList<float> values)
    {
        sb.Append('[');
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(Round(values[i]));
        }
        sb.Append(']');
    }

    private static void WriteGradientStops(StringBuilder sb, IReadOnlyList<GradientStop> stops)
    {
        if (stops is null || stops.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append('[');
        for (int i = 0; i < stops.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append("{ \"color\": \"").Append(ColorToString(stops[i].Color))
              .Append("\", \"position\": ").Append(Round(stops[i].Position))
              .Append(" }");
        }
        sb.Append(']');
    }

    private static void WriteBoxEdges(StringBuilder sb, BoxEdges edges)
    {
        sb.Append("{ \"top\": ").Append(Round(edges.Top))
          .Append(", \"right\": ").Append(Round(edges.Right))
          .Append(", \"bottom\": ").Append(Round(edges.Bottom))
          .Append(", \"left\": ").Append(Round(edges.Left))
          .Append(" }");
    }

    private static string ColorToString(Color c)
    {
        if (c.A == 255)
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static string Round(double value)
    {
        return Math.Round(value, 2).ToString("G", CultureInfo.InvariantCulture);
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
