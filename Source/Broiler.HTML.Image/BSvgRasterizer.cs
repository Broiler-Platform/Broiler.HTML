using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Graphics;

namespace Broiler.HTML.Image;

/// <summary>
/// Rasterizes SVG image data through the current Broiler image backend.
/// This remains the temporary SVG fallback
/// boundary behind the Broiler-owned bitmap abstraction.
/// </summary>
public static class BSvgRasterizer
{
    /// <summary>
    /// Determines whether the given bytes appear to contain SVG data.
    /// </summary>
    public static bool IsSvgData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 4)
            return false;

        int offset = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            offset = 3;

        while (offset < data.Length && (data[offset] == ' ' || data[offset] == '\t' ||
               data[offset] == '\r' || data[offset] == '\n'))
            offset++;

        if (offset >= data.Length)
            return false;

        int scanLength = Math.Min(data.Length, offset + 1024);
        var header = Encoding.UTF8.GetString(data, offset, scanLength - offset);

        int index = 0;
        while (index < header.Length)
        {
            while (index < header.Length && char.IsWhiteSpace(header[index]))
                index++;

            if (index >= header.Length)
                return false;

            if (StartsWith(header, index, "<!--"))
            {
                int end = header.IndexOf("-->", index, StringComparison.Ordinal);
                if (end < 0)
                    return false;

                index = end + 3;
                continue;
            }

            if (StartsWith(header, index, "<?"))
            {
                int end = header.IndexOf("?>", index, StringComparison.Ordinal);
                if (end < 0)
                    return false;

                index = end + 2;
                continue;
            }

            if (StartsWith(header, index, "<!DOCTYPE"))
            {
                int end = header.IndexOf('>', index);
                if (end < 0)
                    return false;

                index = end + 1;
                continue;
            }

            return StartsWithSvgElement(header, index);
        }

        return false;
    }

    private static bool StartsWith(string source, int index, string value) =>
        source.AsSpan(index).StartsWith(value, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithSvgElement(string source, int index)
    {
        if (!StartsWith(source, index, "<svg"))
            return false;

        int nextIndex = index + 4;
        if (nextIndex >= source.Length)
            return true;

        char next = source[nextIndex];
        return char.IsWhiteSpace(next) || next is '>' or '/';
    }

    /// <summary>
    /// Rasterizes SVG bytes into a backend-neutral bitmap.
    /// Returns <c>null</c> when the SVG cannot be parsed.
    /// </summary>
    public static BBitmap? RasterizeToBitmap(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var svgContent = Encoding.UTF8.GetString(data);
        if (!TryCreateContext(svgContent, 300, 150, out var context))
            return null;

        int width = (int)Math.Ceiling(context.IntrinsicWidth > 0 ? context.IntrinsicWidth : 300);
        int height = (int)Math.Ceiling(context.IntrinsicHeight > 0 ? context.IntrinsicHeight : 150);
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        return RasterizeToBitmap(svgContent, width, height);
    }

    internal static BBitmap? RasterizeToBitmap(string svgContent, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(svgContent);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        if (!TryCreateContext(svgContent, width, height, out var context))
            return null;

        var bitmap = new BBitmap(width, height);
        bitmap.Clear(BColor.Transparent);

        if (context.HasDegenerateViewBox)
            return bitmap;

        using var canvas = bitmap.OpenRasterCanvas();
        RenderRectangles(svgContent, context, canvas);
        RenderCircles(svgContent, context, canvas);
        RenderEllipses(svgContent, context, canvas);
        RenderLines(svgContent, context, canvas);
        RenderPaths(svgContent, context, canvas);

        if (svgContent.Contains("<text", StringComparison.OrdinalIgnoreCase))
        {
            using var graphics = bitmap.OpenGraphics(new RectangleF(0, 0, width, height));
            RenderText(svgContent, context, graphics);
        }

        return bitmap;
    }

    private static void RenderRectangles(string svgContent, SvgRenderContext context, BCanvas canvas)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<rect\b([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            float x = context.TransformX(ParseLength(attrs, "x", context.CoordinateWidth));
            float y = context.TransformY(ParseLength(attrs, "y", context.CoordinateHeight));
            float rectWidth = context.ScaleWidth(ParseLength(attrs, "width", context.CoordinateWidth));
            float rectHeight = context.ScaleHeight(ParseLength(attrs, "height", context.CoordinateHeight));

            if (rectWidth <= 0 || rectHeight <= 0)
                continue;

            var fill = GetColor(attrs, "fill", BColor.Black);
            var stroke = GetColor(attrs, "stroke", BColor.Empty);
            float strokeWidth = context.ScaleStroke(ParseLength(attrs, "stroke-width", context.CoordinateWidth, 1));

            if (!fill.IsEmpty && fill.A > 0)
                canvas.FillRect(new RectangleF(x, y, rectWidth, rectHeight), ToBColor(fill));

            if (!stroke.IsEmpty && stroke.A > 0 && strokeWidth > 0)
                canvas.DrawRectangleStroke(new RectangleF(x, y, rectWidth, rectHeight), ToBColor(stroke), strokeWidth);
        }
    }

    private static void RenderCircles(string svgContent, SvgRenderContext context, BCanvas canvas)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<circle\b([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            float cx = context.TransformX(ParseLength(attrs, "cx", context.CoordinateWidth));
            float cy = context.TransformY(ParseLength(attrs, "cy", context.CoordinateHeight));
            float radius = ParseLength(attrs, "r", context.CoordinateWidth);
            RenderEllipseCore(
                canvas,
                context,
                attrs,
                cx,
                cy,
                context.ScaleWidth(radius),
                context.ScaleHeight(radius));
        }
    }

    private static void RenderEllipses(string svgContent, SvgRenderContext context, BCanvas canvas)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<ellipse\b([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            float cx = context.TransformX(ParseLength(attrs, "cx", context.CoordinateWidth));
            float cy = context.TransformY(ParseLength(attrs, "cy", context.CoordinateHeight));
            float rx = context.ScaleWidth(ParseLength(attrs, "rx", context.CoordinateWidth));
            float ry = context.ScaleHeight(ParseLength(attrs, "ry", context.CoordinateHeight));
            RenderEllipseCore(canvas, context, attrs, cx, cy, rx, ry);
        }
    }

    private static void RenderEllipseCore(
        BCanvas canvas,
        SvgRenderContext context,
        Dictionary<string, string> attrs,
        float cx,
        float cy,
        float rx,
        float ry)
    {
        if (rx <= 0 || ry <= 0)
            return;

        var points = CreateEllipsePoints(cx, cy, rx, ry);
        var fill = GetColor(attrs, "fill", BColor.Black);
        var stroke = GetColor(attrs, "stroke", BColor.Empty);
        float strokeWidth = context.ScaleStroke(ParseLength(attrs, "stroke-width", context.CoordinateWidth, 1));

        if (!fill.IsEmpty && fill.A > 0)
            canvas.FillPolygon(points, ToBColor(fill));

        if (!stroke.IsEmpty && stroke.A > 0 && strokeWidth > 0)
            DrawPolygonStroke(canvas, points, stroke, strokeWidth, closed: true);
    }

    private static void RenderLines(string svgContent, SvgRenderContext context, BCanvas canvas)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<line\b([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            var stroke = GetColor(attrs, "stroke", BColor.Black);
            float strokeWidth = context.ScaleStroke(ParseLength(attrs, "stroke-width", context.CoordinateWidth, 1));
            if (stroke.IsEmpty || stroke.A <= 0 || strokeWidth <= 0)
                continue;

            canvas.DrawLine(
                new PointF(
                    context.TransformX(ParseLength(attrs, "x1", context.CoordinateWidth)),
                    context.TransformY(ParseLength(attrs, "y1", context.CoordinateHeight))),
                new PointF(
                    context.TransformX(ParseLength(attrs, "x2", context.CoordinateWidth)),
                    context.TransformY(ParseLength(attrs, "y2", context.CoordinateHeight))),
                ToBColor(stroke),
                strokeWidth);
        }
    }

    private static void RenderPaths(string svgContent, SvgRenderContext context, BCanvas canvas)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<path\b([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            if (!attrs.TryGetValue("d", out var pathData) || string.IsNullOrWhiteSpace(pathData))
                continue;

            var points = ParseSimplePath(pathData, context);
            if (points.Points.Count < 2)
                continue;

            var fill = GetColor(attrs, "fill", BColor.Black);
            var stroke = GetColor(attrs, "stroke", BColor.Empty);
            float strokeWidth = context.ScaleStroke(ParseLength(attrs, "stroke-width", context.CoordinateWidth, 1));

            if (points.IsClosed && !fill.IsEmpty && fill.A > 0 && points.Points.Count >= 3)
                canvas.FillPolygon(points.Points.ToArray(), ToBColor(fill));

            if (!stroke.IsEmpty && stroke.A > 0 && strokeWidth > 0)
                DrawPolygonStroke(canvas, points.Points, stroke, strokeWidth, points.IsClosed);
        }
    }

    private static void RenderText(string svgContent, SvgRenderContext context, RGraphics graphics)
    {
        foreach (Match match in Regex.Matches(svgContent, @"<text\b([^>]*)>(.*?)</text>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = ParseAttributes(match.Groups[1].Value);
            var text = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(text))
                continue;

            var fill = GetColor(attrs, "fill", BColor.Black);
            if (fill.IsEmpty || fill.A <= 0)
                continue;

            float x = context.TransformX(ParseLength(attrs, "x", context.CoordinateWidth));
            float y = context.TransformY(ParseLength(attrs, "y", context.CoordinateHeight));
            float fontSize = context.ScaleStroke(ParseLength(attrs, "font-size", context.CoordinateHeight, 16));
            var font = CompatProvider.ImageAdapter.GetFont(
                attrs.TryGetValue("font-family", out var family) && !string.IsNullOrWhiteSpace(family) ? family : "Arial",
                Math.Max(fontSize, 1),
                FontStyle.Regular);

            graphics.DrawString(text, font, fill, new PointF(x, y), context.Bounds.Size, false);
        }
    }

    private static void DrawPolygonStroke(BCanvas canvas, IReadOnlyList<PointF> points, BColor stroke, float strokeWidth, bool closed)
    {
        if (points.Count < 2)
            return;

        for (int index = 1; index < points.Count; index++)
            canvas.DrawLine(points[index - 1], points[index], ToBColor(stroke), strokeWidth);

        if (closed)
            canvas.DrawLine(points[^1], points[0], ToBColor(stroke), strokeWidth);
    }

    private static PointF[] CreateEllipsePoints(float cx, float cy, float rx, float ry)
    {
        const int segments = 48;
        var points = new PointF[segments];
        for (int index = 0; index < segments; index++)
        {
            float angle = (float)(index * (Math.PI * 2.0 / segments));
            points[index] = new PointF(
                cx + (float)Math.Cos(angle) * rx,
                cy + (float)Math.Sin(angle) * ry);
        }

        return points;
    }

    private static PathPoints ParseSimplePath(string pathData, SvgRenderContext context)
    {
        var tokens = Regex.Matches(pathData, @"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?")
            .Select(static match => match.Value)
            .ToArray();
        var points = new List<PointF>();
        if (tokens.Length == 0)
            return new PathPoints(points, false);

        int index = 0;
        char command = '\0';
        float currentX = 0;
        float currentY = 0;
        float startX = 0;
        float startY = 0;
        bool isClosed = false;

        while (index < tokens.Length)
        {
            if (tokens[index].Length == 1 && char.IsLetter(tokens[index][0]))
            {
                command = tokens[index][0];
                index++;
            }
            else if (command == '\0')
            {
                break;
            }

            switch (command)
            {
                case 'M':
                case 'm':
                {
                    bool isRelative = command == 'm';
                    if (!TryReadFloat(tokens, ref index, out float x) || !TryReadFloat(tokens, ref index, out float y))
                        return new PathPoints(points, isClosed);

                    currentX = isRelative ? currentX + x : x;
                    currentY = isRelative ? currentY + y : y;
                    startX = currentX;
                    startY = currentY;
                    points.Add(context.TransformPoint(currentX, currentY));

                    while (TryReadFloat(tokens, ref index, out x) && TryReadFloat(tokens, ref index, out y))
                    {
                        currentX = isRelative ? currentX + x : x;
                        currentY = isRelative ? currentY + y : y;
                        points.Add(context.TransformPoint(currentX, currentY));
                    }

                    command = isRelative ? 'l' : 'L';
                    break;
                }
                case 'L':
                case 'l':
                {
                    bool isRelative = command == 'l';
                    while (TryReadFloat(tokens, ref index, out float x) && TryReadFloat(tokens, ref index, out float y))
                    {
                        currentX = isRelative ? currentX + x : x;
                        currentY = isRelative ? currentY + y : y;
                        points.Add(context.TransformPoint(currentX, currentY));
                    }

                    break;
                }
                case 'H':
                case 'h':
                {
                    bool isRelative = command == 'h';
                    while (TryReadFloat(tokens, ref index, out float x))
                    {
                        currentX = isRelative ? currentX + x : x;
                        points.Add(context.TransformPoint(currentX, currentY));
                    }

                    break;
                }
                case 'V':
                case 'v':
                {
                    bool isRelative = command == 'v';
                    while (TryReadFloat(tokens, ref index, out float y))
                    {
                        currentY = isRelative ? currentY + y : y;
                        points.Add(context.TransformPoint(currentX, currentY));
                    }

                    break;
                }
                case 'Z':
                case 'z':
                    isClosed = true;
                    currentX = startX;
                    currentY = startY;
                    break;
                default:
                    return new PathPoints(points, isClosed);
            }
        }

        return new PathPoints(points, isClosed);
    }

    private static bool TryReadFloat(string[] tokens, ref int index, out float value)
    {
        if (index >= tokens.Length || (tokens[index].Length == 1 && char.IsLetter(tokens[index][0])))
        {
            value = 0;
            return false;
        }

        bool success = float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (success)
            index++;

        return success;
    }

    private static bool TryCreateContext(string svgContent, int width, int height, out SvgRenderContext context)
    {
        context = default;
        int svgIndex = svgContent.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgIndex < 0)
            return false;

        int tagEnd = svgContent.IndexOf('>', svgIndex);
        if (tagEnd < 0)
            return false;

        var rootTag = svgContent.Substring(svgIndex, tagEnd - svgIndex + 1);
        var attrs = ParseAttributes(rootTag);
        bool parsedViewBox = TryParseViewBox(attrs, out var viewBox, out bool hasViewBox);
        bool hasDegenerateViewBox = parsedViewBox && (viewBox.Width <= 0 || viewBox.Height <= 0);
        if (hasDegenerateViewBox)
        {
            context = new SvgRenderContext(new RectangleF(0, 0, width, height), 1, 1, 0, 0, width, height, width, height, true);
            return true;
        }

        float explicitWidth = ParseLength(attrs, "width", width);
        float explicitHeight = ParseLength(attrs, "height", height);
        bool preserveAspectRatioNone = attrs.TryGetValue("preserveAspectRatio", out var preserveAspectRatio)
            && preserveAspectRatio.Trim().StartsWith("none", StringComparison.OrdinalIgnoreCase);

        float intrinsicWidth = explicitWidth > 0 ? explicitWidth : hasViewBox ? viewBox.Width : 300;
        float intrinsicHeight = explicitHeight > 0 ? explicitHeight : hasViewBox ? viewBox.Height : 150;

        float coordinateWidth = hasViewBox ? viewBox.Width : (explicitWidth > 0 ? explicitWidth : width);
        float coordinateHeight = hasViewBox ? viewBox.Height : (explicitHeight > 0 ? explicitHeight : height);
        float scaleX;
        float scaleY;
        float offsetX;
        float offsetY;

        if (hasViewBox)
        {
            if (preserveAspectRatioNone)
            {
                scaleX = width / viewBox.Width;
                scaleY = height / viewBox.Height;
                offsetX = -viewBox.X * scaleX;
                offsetY = -viewBox.Y * scaleY;
            }
            else
            {
                float uniformScale = Math.Min(width / viewBox.Width, height / viewBox.Height);
                scaleX = uniformScale;
                scaleY = uniformScale;
                offsetX = -viewBox.X * uniformScale + (width - viewBox.Width * uniformScale) / 2f;
                offsetY = -viewBox.Y * uniformScale + (height - viewBox.Height * uniformScale) / 2f;
            }
        }
        else
        {
            scaleX = coordinateWidth > 0 ? width / coordinateWidth : 1f;
            scaleY = coordinateHeight > 0 ? height / coordinateHeight : 1f;
            offsetX = 0;
            offsetY = 0;
        }

        context = new SvgRenderContext(
            new RectangleF(0, 0, width, height),
            scaleX,
            scaleY,
            offsetX,
            offsetY,
            coordinateWidth,
            coordinateHeight,
            intrinsicWidth,
            intrinsicHeight,
            false);
        return true;
    }

    private static bool TryParseViewBox(Dictionary<string, string> attrs, out RectangleF viewBox, out bool hasViewBox)
    {
        viewBox = RectangleF.Empty;
        hasViewBox = false;
        if (!attrs.TryGetValue("viewBox", out var value))
            return false;

        var parts = value.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float height))
        {
            return false;
        }

        hasViewBox = true;
        viewBox = new RectangleF(x, y, width, height);
        return true;
    }

    private static Dictionary<string, string> ParseAttributes(string tagContent)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(tagContent, @"([\w:-]+)\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase))
            attributes[match.Groups[1].Value] = match.Groups[2].Value;

        return attributes;
    }

    private static float ParseLength(Dictionary<string, string> attrs, string name, float relativeAxis, float defaultValue = 0)
    {
        if (!attrs.TryGetValue(name, out var rawValue))
            return defaultValue;

        return ParseLength(rawValue, relativeAxis, defaultValue);
    }

    private static float ParseLength(string rawValue, float relativeAxis, float defaultValue = 0)
    {
        var value = rawValue.Trim();
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        if (value.EndsWith("%", StringComparison.Ordinal))
        {
            var percentText = value[..^1];
            return float.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                ? relativeAxis * percent / 100f
                : defaultValue;
        }

        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : defaultValue;
    }

    private static BColor GetColor(Dictionary<string, string> attrs, string name, BColor defaultColor)
    {
        if (!attrs.TryGetValue(name, out var value))
            return defaultColor;

        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return BColor.Empty;

        if (value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
        {
            var parts = value[5..^1].Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b)
                && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
            {
                return BColor.FromArgb(
                    (int)(Math.Clamp(a, 0f, 1f) * 255),
                    Math.Clamp(r, 0, 255),
                    Math.Clamp(g, 0, 255),
                    Math.Clamp(b, 0, 255));
            }
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
        {
            var parts = value[4..^1].Split(',');
            if (parts.Length == 3
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b))
            {
                return BColor.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
            }
        }

        try
        {
            if (value.StartsWith("#", StringComparison.Ordinal))
                return TryParseHexColor(value, out var hex) ? hex : defaultColor;
            var named = BColor.FromName(value);
            return named.IsEmpty ? defaultColor : named;
        }
        catch
        {
            return defaultColor;
        }
    }

    private static bool TryParseHexColor(string val, out BColor color)
    {
        color = default;
        string hex = val.TrimStart('#');
        if (hex.Length == 3 || hex.Length == 4)
        {
            string expanded = "";
            foreach (char ch in hex)
                expanded += new string(ch, 2);
            hex = expanded;
        }
        if (hex.Length != 6 && hex.Length != 8)
            return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;
        color = hex.Length == 6
            ? new BColor((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF))
            : new BColor((byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    private static BColor ToBColor(BColor color) => new(color.R, color.G, color.B, color.A);

    private readonly record struct PathPoints(List<PointF> Points, bool IsClosed);

    private readonly record struct SvgRenderContext(
        RectangleF Bounds,
        float ScaleX,
        float ScaleY,
        float OffsetX,
        float OffsetY,
        float CoordinateWidth,
        float CoordinateHeight,
        float IntrinsicWidth,
        float IntrinsicHeight,
        bool HasDegenerateViewBox)
    {
        public float TransformX(float value) => OffsetX + value * ScaleX;
        public float TransformY(float value) => OffsetY + value * ScaleY;
        public float ScaleWidth(float value) => value * ScaleX;
        public float ScaleHeight(float value) => value * ScaleY;
        public float ScaleStroke(float value) => value * Math.Max(Math.Abs(ScaleX), Math.Abs(ScaleY));
        public PointF TransformPoint(float x, float y) => new(TransformX(x), TransformY(y));
    }
}
