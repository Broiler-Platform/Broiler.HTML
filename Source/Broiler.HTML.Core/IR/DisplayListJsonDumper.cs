using System;
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
        bool hasExtraProps = item is not RestoreItem;
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
                sb.Append(pad2).Append("\"isRtl\": ").Append(text.IsRtl ? "true" : "false");
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

            case ClipItem clip:
                sb.Append(pad2).Append("\"clipRect\": ");
                WriteRect(sb, clip.ClipRect);
                sb.AppendLine();
                break;

            case RestoreItem:
            case RestoreOpacityItem:
                // No additional properties — remove trailing comma from bounds
                break;

            case OpacityItem opacity:
                sb.Append(pad2).Append("\"opacity\": ").Append(Round(opacity.Opacity));
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
        }

        sb.Append(pad).Append('}');
    }

    private static string GetTypeName(DisplayItem item) => item switch
    {
        FillRectItem => "FillRect",
        DrawBorderItem => "DrawBorder",
        DrawTextItem => "DrawText",
        DrawImageItem => "DrawImage",
        ClipItem => "Clip",
        RestoreItem => "Restore",
        OpacityItem => "Opacity",
        RestoreOpacityItem => "RestoreOpacity",
        DrawLineItem => "DrawLine",
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
