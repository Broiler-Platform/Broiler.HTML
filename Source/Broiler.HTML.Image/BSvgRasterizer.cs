using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Graphics.Color;
using Broiler.HTML.Orchestration.IR;
using Broiler.Layout.IR;

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

        // One SVG renderer, not two. This used to draw the document itself, with a regex pass per
        // element type that knew only rect/circle/ellipse/line/path/text — so a <polygon>, the
        // commonest shape after <path>, produced a fully transparent bitmap and the image simply did
        // not appear. The same file rendered correctly as inline <svg> markup, because that path goes
        // through Broiler.Layout's SvgRenderer. It now goes through that renderer too, replayed onto
        // this bitmap by the same raster backend the inline path uses. See issue #1627.
        var bounds = new RectangleF(0, 0, width, height);
        using var graphics = bitmap.OpenGraphics(bounds);
        SvgImageRaster.Render(svgContent, bounds, RGraphicsRasterBackend.Instance, graphics);

        return bitmap;
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
