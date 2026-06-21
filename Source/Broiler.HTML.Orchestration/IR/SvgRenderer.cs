using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using Broiler.HTML.Core.Core.IR;

namespace Broiler.HTML.Orchestration.Core.IR;

/// <summary>
/// Converts simplified SVG markup into <see cref="DisplayItem"/> entries
/// that can be rendered by <see cref="RGraphicsRasterBackend"/>.
/// </summary>
internal static class SvgRenderer
{
    /// <summary>
    /// Parses SVG XML content and returns display items positioned within
    /// the given <paramref name="bounds"/> rectangle.
    /// </summary>
    public static List<DisplayItem> RenderSvgContent(string svgXml, RectangleF bounds)
    {
        var items = new List<DisplayItem>();
        if (string.IsNullOrEmpty(svgXml))
            return items;

        ParseElements(svgXml, bounds, items);
        return items;
    }

    private static void ParseElements(string svgXml, RectangleF bounds, List<DisplayItem> items)
    {
        // Parse the viewBox from the root <svg> element to compute the
        // coordinate transform.  When a viewBox is present, SVG coordinates
        // are in viewBox space and must be scaled/translated to CSS bounds.
        // Default preserveAspectRatio is "xMidYMid meet" — scale uniformly
        // to fit, then centre in the viewport.
        float sx = 1f, sy = 1f, tx = 0f, ty = 0f;
        var pathStartsById = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        var svgMatch = Regex.Match(svgXml, @"<svg\s+([^>]*?)\/?>", RegexOptions.IgnoreCase);
        if (svgMatch.Success)
        {
            var svgAttrs = ParseAttributes(svgMatch.Groups[1].Value);
            if (svgAttrs.TryGetValue("viewBox", out var vb))
            {
                var parts = vb.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbX) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbY) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbW) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbH) &&
                    vbW > 0 && vbH > 0)
                {
                    // "xMidYMid meet": scale uniformly to fit, centre
                    float scaleX = bounds.Width / vbW;
                    float scaleY = bounds.Height / vbH;
                    float scale = Math.Min(scaleX, scaleY);
                    sx = scale;
                    sy = scale;
                    tx = -vbX * scale + (bounds.Width - vbW * scale) / 2f;
                    ty = -vbY * scale + (bounds.Height - vbH * scale) / 2f;
                }
            }
        }

        foreach (Match m in Regex.Matches(svgXml, @"<path\s+([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            if (!attrs.TryGetValue("id", out var id) || !attrs.TryGetValue("d", out var pathData))
                continue;

            var start = TryGetPathStart(pathData);
            if (start.HasValue)
                pathStartsById[id] = start.Value;
        }

        // <rect ... /> or <rect ...></rect>
        foreach (Match m in Regex.Matches(svgXml, @"<rect\s+([^/>]*)/?>" , RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgRectItem
            {
                Bounds = bounds,
                X = GetFloat(attrs, "x") * sx + tx,
                Y = GetFloat(attrs, "y") * sy + ty,
                Width = GetFloat(attrs, "width") * sx,
                Height = GetFloat(attrs, "height") * sy,
                Fill = GetColor(attrs, "fill", Color.Black),
                Stroke = GetColor(attrs, "stroke", Color.Empty),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        // <circle ... />
        foreach (Match m in Regex.Matches(svgXml, @"<circle\s+([^/>]*)/?>" , RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            float r = GetFloat(attrs, "r");
            items.Add(new DrawSvgEllipseItem
            {
                Bounds = bounds,
                Cx = GetFloat(attrs, "cx") * sx + tx,
                Cy = GetFloat(attrs, "cy") * sy + ty,
                Rx = r * sx,
                Ry = r * sy,
                Fill = GetColor(attrs, "fill", Color.Black),
                Stroke = GetColor(attrs, "stroke", Color.Empty),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        // <ellipse ... />
        foreach (Match m in Regex.Matches(svgXml, @"<ellipse\s+([^/>]*)/?>" , RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgEllipseItem
            {
                Bounds = bounds,
                Cx = GetFloat(attrs, "cx") * sx + tx,
                Cy = GetFloat(attrs, "cy") * sy + ty,
                Rx = GetFloat(attrs, "rx") * sx,
                Ry = GetFloat(attrs, "ry") * sy,
                Fill = GetColor(attrs, "fill", Color.Black),
                Stroke = GetColor(attrs, "stroke", Color.Empty),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        // <line ... />
        foreach (Match m in Regex.Matches(svgXml, @"<line\s+([^/>]*)/?>" , RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgLineItem
            {
                Bounds = bounds,
                X1 = GetFloat(attrs, "x1") * sx + tx,
                Y1 = GetFloat(attrs, "y1") * sy + ty,
                X2 = GetFloat(attrs, "x2") * sx + tx,
                Y2 = GetFloat(attrs, "y2") * sy + ty,
                Stroke = GetColor(attrs, "stroke", Color.Black),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        foreach (Match m in Regex.Matches(svgXml, @"<polygon\s+([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgPolygonItem
            {
                Bounds = bounds,
                Points = ParsePoints(attrs.GetValueOrDefault("points") ?? string.Empty, sx, sy, tx, ty),
                Fill = GetColor(attrs, "fill", Color.Black),
                Stroke = GetColor(attrs, "stroke", Color.Empty),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        foreach (Match m in Regex.Matches(svgXml, @"<polyline\s+([^/>]*)/?>", RegexOptions.IgnoreCase))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgPolylineItem
            {
                Bounds = bounds,
                Points = ParsePoints(attrs.GetValueOrDefault("points") ?? string.Empty, sx, sy, tx, ty),
                Fill = GetColor(attrs, "fill", Color.Empty),
                Stroke = GetColor(attrs, "stroke", Color.Empty),
                StrokeWidth = GetFloat(attrs, "stroke-width", 1) * Math.Max(sx, sy),
            });
        }

        // <text ...>content</text>
        foreach (Match m in Regex.Matches(svgXml, @"<text\s+([^>]*)>\s*<textpath\s+([^>]*)>(.*?)</textpath>\s*</text>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            var textPathAttrs = ParseAttributes(m.Groups[2].Value);
            if (!textPathAttrs.TryGetValue("href", out var href) || !href.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (!pathStartsById.TryGetValue(href[1..], out var start))
                continue;

            items.Add(new DrawSvgTextItem
            {
                Bounds = bounds,
                X = start.X * sx + tx,
                Y = start.Y * sy + ty,
                FontSize = GetFloat(attrs, "font-size", 16) * Math.Max(sx, sy),
                FontFamily = attrs.GetValueOrDefault("font-family") ?? "Arial",
                Fill = GetColor(attrs, "fill", Color.Black),
                Text = Regex.Replace(m.Groups[3].Value, "<.*?>", string.Empty).Trim(),
            });
        }

        foreach (Match m in Regex.Matches(svgXml, @"<text\s+([^>]*)>(.*?)</text>" ,
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (Regex.IsMatch(m.Groups[2].Value, @"<\s*textpath\b", RegexOptions.IgnoreCase))
                continue;

            var attrs = ParseAttributes(m.Groups[1].Value);
            items.Add(new DrawSvgTextItem
            {
                Bounds = bounds,
                X = GetFloat(attrs, "x") * sx + tx,
                Y = GetFloat(attrs, "y") * sy + ty,
                FontSize = GetFloat(attrs, "font-size", 16) * Math.Max(sx, sy),
                FontFamily = attrs.GetValueOrDefault("font-family") ?? "Arial",
                Fill = GetColor(attrs, "fill", Color.Black),
                Text = m.Groups[2].Value.Trim(),
            });
        }
    }

    private static PointF? TryGetPathStart(string pathData)
    {
        var match = Regex.Match(pathData, @"M\s*(?<x>-?\d*\.?\d+)\s*,?\s*(?<y>-?\d*\.?\d+)", RegexOptions.IgnoreCase);
        if (!match.Success ||
            !float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return null;
        }

        return new PointF(x, y);
    }

    private static List<PointF> ParsePoints(string value, float sx, float sy, float tx, float ty)
    {
        var points = new List<PointF>();
        var numbers = Regex.Matches(value, @"-?\d*\.?\d+(?:[eE][+-]?\d+)?");
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            if (!float.TryParse(numbers[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(numbers[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            points.Add(new PointF(x * sx + tx, y * sy + ty));
        }

        return points;
    }

    private static Dictionary<string, string> ParseAttributes(string attrStr)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(attrStr, @"([\w\-]+)\s*=\s*""([^""]*)"""))
        {
            dict[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return dict;
    }

    private static float GetFloat(Dictionary<string, string> attrs, string name, float defaultValue = 0)
    {
        if (attrs.TryGetValue(name, out var val) &&
            float.TryParse(val.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return f;
        return defaultValue;
    }

    private static Color GetColor(Dictionary<string, string> attrs, string name, Color defaultColor)
    {
        if (!attrs.TryGetValue(name, out var val) || string.IsNullOrEmpty(val) || val == "none")
            return Color.Empty;

        // rgba(r, g, b, a)
        if (val.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(")"))
        {
            string inner = val.Substring(5, val.Length - 6);
            var parts = inner.Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b)
                && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
            {
                return Color.FromArgb(
                    (int)(Math.Clamp(a, 0f, 1f) * 255),
                    Math.Clamp(r, 0, 255),
                    Math.Clamp(g, 0, 255),
                    Math.Clamp(b, 0, 255));
            }
        }

        // rgb(r, g, b)
        if (val.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(")"))
        {
            string inner = val.Substring(4, val.Length - 5);
            var parts = inner.Split(',');
            if (parts.Length == 3
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b))
            {
                return Color.FromArgb(
                    255,
                    Math.Clamp(r, 0, 255),
                    Math.Clamp(g, 0, 255),
                    Math.Clamp(b, 0, 255));
            }
        }

        // Handle hex colors
        if (val.StartsWith('#'))
        {
            try
            {
                return ColorTranslator.FromHtml(val);
            }
            catch
            {
                return defaultColor;
            }
        }

        // Handle named colors
        try { return Color.FromName(val); }
        catch { return defaultColor; }
    }
}
