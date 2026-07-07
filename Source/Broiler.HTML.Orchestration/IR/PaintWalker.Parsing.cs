using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;


namespace Broiler.HTML.Orchestration.IR;

// Parsing helpers for PaintWalker, split out of PaintWalker.cs for size.
// This is a partial of the same internal static class.
internal static partial class PaintWalker
{

    /// <summary>
    /// Parses a CSS text-shadow value and returns the offset and color components.
    /// Supports: &lt;color&gt; &lt;offsetX&gt; &lt;offsetY&gt; or &lt;offsetX&gt; &lt;offsetY&gt; &lt;blur&gt;? &lt;color&gt;?.
    /// </summary>
    private static (float offsetX, float offsetY, BColor color) ParseTextShadow(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "none")
            return (0, 0, BColor.Empty);

        var parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            char c = i < value.Length ? value[i] : ' ';
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ' ' && depth == 0 && i > start)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
            else if (i == value.Length && start < i)
                parts.Add(value[start..i]);
        }

        var lengths = new List<float>();
        string colorStr = "";

        foreach (var p in parts)
        {
            var trimmed = p.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                || (trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '.')))
            {
                var num = trimmed.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    lengths.Add(v);
                    continue;
                }
            }
            colorStr += (colorStr.Length > 0 ? " " : "") + trimmed;
        }

        float offsetX = lengths.Count >= 1 ? lengths[0] : 0;
        float offsetY = lengths.Count >= 2 ? lengths[1] : 0;

        // Unrecognised colors fall back to Color.Black (the default above).
        BColor color = BColor.Black;
        if (!string.IsNullOrEmpty(colorStr))
        {
            var parsed = ParseCssColor(colorStr);
            if (parsed != BColor.Empty)
                color = parsed;
        }

        return (offsetX, offsetY, color);
    }

    /// <summary>
    /// Splits a CSS value on top-level commas, delegating to the canonical
    /// <see cref="Broiler.CSS.CssSyntax.SplitTopLevel(string, char)"/> (which also
    /// respects quotes/brackets/comments, not just parentheses). Preserves the
    /// historical behavior of dropping a trailing empty segment (e.g. "a," =&gt; ["a"])
    /// and mapping an empty input to an empty list rather than [""].
    /// </summary>
    private static List<string> SplitOnTopLevelCommas(string value)
    {
        var parts = new List<string>(CSS.CssSyntax.SplitTopLevel(value, ','));
        if (parts.Count > 0 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    private static object?[] NormalizeBackgroundImageHandles(object? backgroundImageHandle, int layerCount)
    {
        var handles = new object?[Math.Max(layerCount, 1)];
        if (backgroundImageHandle is object?[] array)
        {
            Array.Copy(array, handles, Math.Min(array.Length, handles.Length));
            return handles;
        }

        handles[0] = backgroundImageHandle;
        return handles;
    }

    /// <summary>
    /// Parses a CSS background-size value for a single layer.
    /// Supports: <c>auto</c>, <c>Wpx Hpx</c>, <c>Wpx</c>.
    /// </summary>
    private static void ParseBackgroundSize(string sizeStr, float containerW, float containerH, out float w, out float h)
    {
        w = containerW;
        h = containerH;

        if (string.IsNullOrEmpty(sizeStr) || sizeStr.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return;

        var parts = sizeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1)
        {
            string wp = parts[0].Trim();
            if (!wp.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (wp.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                    float.TryParse(wp.AsSpan(0, wp.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out w);
                else if (wp.EndsWith("%"))
                {
                    if (float.TryParse(wp.AsSpan(0, wp.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                        w = containerW * pct / 100f;
                }
                else
                    float.TryParse(wp, NumberStyles.Float, CultureInfo.InvariantCulture, out w);
            }
        }
        if (parts.Length >= 2)
        {
            string hp = parts[1].Trim();
            if (!hp.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (hp.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                    float.TryParse(hp.AsSpan(0, hp.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out h);
                else if (hp.EndsWith("%"))
                {
                    if (float.TryParse(hp.AsSpan(0, hp.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                        h = containerH * pct / 100f;
                }
                else
                    float.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out h);
            }
        }
        else if (parts.Length == 1)
        {
            // Single value: width is set, height is auto (maintain aspect ratio).
            // For gradients there's no intrinsic ratio, so use same value for both.
            h = w;
        }
    }

    /// <summary>
    /// Parses CSS <c>background-size</c> for a URL-based image, maintaining
    /// aspect ratio when one dimension is <c>auto</c>.
    /// </summary>
    private static void ParseBackgroundSizeForImage(
        string sizeStr,
        float containerW,
        float containerH,
        float intrinsicW,
        float intrinsicH,
        bool hasIntrinsicRatio,
        float intrinsicRatio,
        bool hasIntrinsicWidth,
        bool hasIntrinsicHeight,
        out float w,
        out float h)
    {
        float ratio = hasIntrinsicRatio && intrinsicRatio > 0
            ? intrinsicRatio
            : (hasIntrinsicWidth && hasIntrinsicHeight && intrinsicW > 0 && intrinsicH > 0
                ? intrinsicW / intrinsicH
                : 0);

        bool autoAutoRequested = string.IsNullOrEmpty(sizeStr)
            || sizeStr.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || sizeStr.Equals("auto auto", StringComparison.OrdinalIgnoreCase);

        if (autoAutoRequested)
        {
            if (hasIntrinsicWidth && hasIntrinsicHeight && intrinsicW > 0 && intrinsicH > 0)
            {
                w = intrinsicW;
                h = intrinsicH;
            }
            else if (hasIntrinsicWidth && intrinsicW > 0)
            {
                w = intrinsicW;
                h = ratio > 0 ? intrinsicW / ratio : containerH;
            }
            else if (hasIntrinsicHeight && intrinsicH > 0)
            {
                h = intrinsicH;
                w = ratio > 0 ? intrinsicH * ratio : containerW;
            }
            else if (ratio > 0)
            {
                if (containerW / ratio <= containerH)
                {
                    w = containerW;
                    h = containerW / ratio;
                }
                else
                {
                    h = containerH;
                    w = containerH * ratio;
                }
            }
            else
            {
                w = containerW;
                h = containerH;
            }
            return;
        }

        w = 0;
        h = 0;

        if (sizeStr.Equals("contain", StringComparison.OrdinalIgnoreCase))
        {
            if (ratio <= 0)
            {
                w = containerW;
                h = containerH;
                return;
            }
            if (!(hasIntrinsicWidth && hasIntrinsicHeight && intrinsicW > 0 && intrinsicH > 0))
            {
                intrinsicW = ratio;
                intrinsicH = 1;
            }
            float scaleX = containerW / intrinsicW;
            float scaleY = containerH / intrinsicH;
            float scale = Math.Min(scaleX, scaleY);
            w = intrinsicW * scale;
            h = intrinsicH * scale;
            return;
        }

        if (sizeStr.Equals("cover", StringComparison.OrdinalIgnoreCase))
        {
            if (ratio <= 0)
            {
                w = containerW;
                h = containerH;
                return;
            }
            if (!(hasIntrinsicWidth && hasIntrinsicHeight && intrinsicW > 0 && intrinsicH > 0))
            {
                intrinsicW = ratio;
                intrinsicH = 1;
            }
            float scaleX = containerW / intrinsicW;
            float scaleY = containerH / intrinsicH;
            float scale = Math.Max(scaleX, scaleY);
            w = intrinsicW * scale;
            h = intrinsicH * scale;
            return;
        }

        var parts = sizeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool wIsAuto = parts.Length < 1 || parts[0].Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
        bool hIsAuto = parts.Length < 2 || parts[1].Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);

        if (!wIsAuto)
        {
            string wp = parts[0].Trim();
            if (wp.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                float.TryParse(wp.AsSpan(0, wp.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out w);
            else if (wp.EndsWith("%"))
            {
                if (float.TryParse(wp.AsSpan(0, wp.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    w = containerW * pct / 100f;
            }
            else
                float.TryParse(wp, NumberStyles.Float, CultureInfo.InvariantCulture, out w);
        }

        if (!hIsAuto && parts.Length >= 2)
        {
            string hp = parts[1].Trim();
            if (hp.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                float.TryParse(hp.AsSpan(0, hp.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out h);
            else if (hp.EndsWith("%"))
            {
                if (float.TryParse(hp.AsSpan(0, hp.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    h = containerH * pct / 100f;
            }
            else
                float.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out h);
        }

        // Maintain aspect ratio when one dimension is auto
        if (wIsAuto && !hIsAuto && h > 0 && ratio > 0)
            w = h * ratio;
        else if (wIsAuto && !hIsAuto && hasIntrinsicWidth && intrinsicW > 0)
            w = intrinsicW;
        else if (wIsAuto && !hIsAuto)
            w = containerW;
        else if (!wIsAuto && hIsAuto && w > 0 && ratio > 0)
            h = w / ratio;
        else if (!wIsAuto && hIsAuto && hasIntrinsicHeight && intrinsicH > 0)
            h = intrinsicH;
        else if (!wIsAuto && hIsAuto)
            h = containerH;
        else if (wIsAuto && hIsAuto)
        {
            if (hasIntrinsicWidth && hasIntrinsicHeight && intrinsicW > 0 && intrinsicH > 0)
            {
                w = intrinsicW;
                h = intrinsicH;
            }
            else if (hasIntrinsicWidth && intrinsicW > 0)
            {
                w = intrinsicW;
                h = ratio > 0 ? intrinsicW / ratio : containerH;
            }
            else if (hasIntrinsicHeight && intrinsicH > 0)
            {
                h = intrinsicH;
                w = ratio > 0 ? intrinsicH * ratio : containerW;
            }
            else if (ratio > 0)
            {
                if (containerW / ratio <= containerH)
                {
                    w = containerW;
                    h = containerW / ratio;
                }
                else
                {
                    h = containerH;
                    w = containerH * ratio;
                }
            }
            else
            {
                w = containerW;
                h = containerH;
            }
        }
    }

    /// <summary>
    /// Splits a string on spaces that lie outside any parentheses, so that
    /// colour functions like <c>rgb(0 0 0 / 50%)</c> stay intact.
    /// </summary>
    private static List<string> SplitOnTopLevelSpaces(string value)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>
    /// Parses a CSS color value (rgba, rgb, hex, named) into a <see cref="Color"/>.
    /// </summary>
    private static BColor ParseCssColor(string colorStr)
    {
        if (string.IsNullOrWhiteSpace(colorStr))
            return BColor.Empty;

        colorStr = colorStr.Trim();

        // Canonical CSS color parser handles hex / rgb[a] / hsl[a] and the basic
        // named colors, mirroring HtmlContainerInt.ParseCssColor.
        if (CSS.CssValueParser.TryParseColor(colorStr, out var color))
            return BColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

        // The CSS engine's named table only covers the 16 basic colors, and the
        // static paint context has no adapter to resolve the rest (unlike
        // HtmlContainerInt, which defers to Adapter.GetColor). Fall back to the
        // framework's extended named-color set (e.g. "orange", "gold"); unknown
        // names yield A=0 and are rejected.
        var named = BColor.FromName(colorStr);
        if (named.A > 0)
            return named;

        return BColor.Empty;
    }
}
