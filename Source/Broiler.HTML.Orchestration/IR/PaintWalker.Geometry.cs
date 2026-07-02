using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// Paint-rect, background-clip/origin geometry, and clip-path helpers.
// Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    /// <summary>
    /// Returns the list of rectangles to paint for a fragment. For inline elements
    /// that have per-line-box rectangles, returns those; otherwise returns
    /// the single <see cref="Fragment.Bounds"/> rectangle.
    /// </summary>
    private static IReadOnlyList<RectangleF> GetPaintRects(Fragment fragment)
    {
        if (fragment.InlineRects != null && fragment.InlineRects.Count > 0)
            return fragment.InlineRects;
        return [fragment.Bounds];
    }

    /// <summary>
    /// Computes the background painting area from a border-box rectangle based on
    /// the CSS <c>background-clip</c> property.
    /// <list type="bullet">
    ///   <item><c>border-box</c> (default): returns <paramref name="borderBoxRect"/> unchanged.</item>
    ///   <item><c>padding-box</c>: shrinks by border widths.</item>
    ///   <item><c>content-box</c>: shrinks by border + padding widths.</item>
    /// </list>
    /// </summary>
    private static RectangleF GetBackgroundClipRect(RectangleF borderBoxRect, Fragment fragment, string backgroundClip)
    {
        if (string.IsNullOrEmpty(backgroundClip) ||
            backgroundClip.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            return borderBoxRect;

        var border = fragment.Border;
        float bLeft = (float)border.Left;
        float bTop = (float)border.Top;
        float bRight = (float)border.Right;
        float bBottom = (float)border.Bottom;

        if (backgroundClip.Equals("padding-box", StringComparison.OrdinalIgnoreCase))
        {
            return new RectangleF(
                borderBoxRect.X + bLeft,
                borderBoxRect.Y + bTop,
                borderBoxRect.Width - bLeft - bRight,
                borderBoxRect.Height - bTop - bBottom);
        }

        if (backgroundClip.Equals("content-box", StringComparison.OrdinalIgnoreCase))
        {
            var padding = fragment.Padding;
            float pLeft = (float)padding.Left;
            float pTop = (float)padding.Top;
            float pRight = (float)padding.Right;
            float pBottom = (float)padding.Bottom;

            return new RectangleF(
                borderBoxRect.X + bLeft + pLeft,
                borderBoxRect.Y + bTop + pTop,
                borderBoxRect.Width - bLeft - bRight - pLeft - pRight,
                borderBoxRect.Height - bTop - bBottom - pTop - pBottom);
        }

        // border-area uses the same bounding rectangle as border-box;
        // the special rendering is handled downstream in EmitBorderAreaBorder.
        if (backgroundClip.Equals("border-area", StringComparison.OrdinalIgnoreCase))
            return borderBoxRect;

        // For unsupported values (e.g. "text"), fall back to border-box.
        return borderBoxRect;
    }

    private static RectangleF GetBackgroundPositioningAreaRect(RectangleF borderBoxRect, Fragment fragment, string backgroundOrigin)
    {
        if (string.IsNullOrEmpty(backgroundOrigin) ||
            backgroundOrigin.Equals("padding-box", StringComparison.OrdinalIgnoreCase))
        {
            var border = fragment.Border;
            return new RectangleF(
                borderBoxRect.X + (float)border.Left,
                borderBoxRect.Y + (float)border.Top,
                borderBoxRect.Width - (float)(border.Left + border.Right),
                borderBoxRect.Height - (float)(border.Top + border.Bottom));
        }

        if (backgroundOrigin.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            return borderBoxRect;

        if (backgroundOrigin.Equals("content-box", StringComparison.OrdinalIgnoreCase))
        {
            var border = fragment.Border;
            var padding = fragment.Padding;
            return new RectangleF(
                borderBoxRect.X + (float)border.Left + (float)padding.Left,
                borderBoxRect.Y + (float)border.Top + (float)padding.Top,
                borderBoxRect.Width - (float)(border.Left + border.Right + padding.Left + padding.Right),
                borderBoxRect.Height - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom));
        }

        return GetBackgroundPositioningAreaRect(borderBoxRect, fragment, "padding-box");
    }

    private static RectangleF GetLocalBackgroundPositioningAreaRect(RectangleF borderBoxRect, Fragment fragment, RectangleF originRect)
    {
        float maxRight = originRect.Right;
        float maxBottom = originRect.Bottom;

        if (fragment.Lines != null)
        {
            foreach (var line in fragment.Lines)
            {
                maxRight = Math.Max(maxRight, line.X + line.Width);
                maxBottom = Math.Max(maxBottom, line.Y + line.Height);
            }
        }

        if (fragment.InlineRects != null)
        {
            foreach (var inlineRect in fragment.InlineRects)
            {
                maxRight = Math.Max(maxRight, inlineRect.Right);
                maxBottom = Math.Max(maxBottom, inlineRect.Bottom);
            }
        }

        foreach (var child in fragment.Children)
        {
            maxRight = Math.Max(maxRight, child.Bounds.Right);
            maxBottom = Math.Max(maxBottom, child.Bounds.Bottom);
        }

        return new RectangleF(
            originRect.X,
            originRect.Y,
            Math.Max(originRect.Width, maxRight - originRect.X),
            Math.Max(originRect.Height, maxBottom - originRect.Y));
    }

    private static string GetEffectiveBackgroundClip(Fragment fragment, string backgroundClip)
    {
        if (string.IsNullOrEmpty(backgroundClip))
            return "border-box";

        var clips = SplitOnTopLevelCommas(backgroundClip);
        if (clips.Count == 0)
            return "border-box";

        // CSS backgrounds paint the background color using the clip box of the
        // bottom-most background layer, which is the last value in the
        // comma-separated background-clip list.
        var effectiveClip = clips[^1].Trim();
        return string.IsNullOrEmpty(effectiveClip) ? "border-box" : effectiveClip;
    }

    private static bool TryCreateInsetClipPathItem(Fragment fragment, RectangleF bounds, out ClipItem clipItem)
    {
        clipItem = null!;

        var clipPath = fragment.Style.ClipPath;
        if (string.IsNullOrWhiteSpace(clipPath)
            || clipPath.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        clipPath = clipPath.Trim();
        if (!clipPath.StartsWith("inset(", StringComparison.OrdinalIgnoreCase)
            || !clipPath.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var insetArgs = clipPath[6..^1];
        int roundIndex = insetArgs.IndexOf(" round ", StringComparison.OrdinalIgnoreCase);
        if (roundIndex >= 0)
            insetArgs = insetArgs[..roundIndex];

        var parts = insetArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 4)
            return false;

        float emSize = GetPositionEmSize(fragment.Style);
        float top = ParseInsetClipPathValue(parts[0], bounds.Height, emSize);
        float right = parts.Length switch
        {
            1 => top,
            2 => ParseInsetClipPathValue(parts[1], bounds.Width, emSize),
            3 => ParseInsetClipPathValue(parts[1], bounds.Width, emSize),
            _ => ParseInsetClipPathValue(parts[1], bounds.Width, emSize),
        };
        float bottom = parts.Length switch
        {
            1 => top,
            2 => top,
            3 => ParseInsetClipPathValue(parts[2], bounds.Height, emSize),
            _ => ParseInsetClipPathValue(parts[2], bounds.Height, emSize),
        };
        float left = parts.Length switch
        {
            1 => right,
            2 => right,
            3 => right,
            _ => ParseInsetClipPathValue(parts[3], bounds.Width, emSize),
        };

        var clipRect = new RectangleF(
            bounds.X + left,
            bounds.Y + top,
            Math.Max(0, bounds.Width - left - right),
            Math.Max(0, bounds.Height - top - bottom));
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return false;

        clipItem = new ClipItem { Bounds = bounds, ClipRect = clipRect };
        return true;
    }

    private static float ParseInsetClipPathValue(string value, float referenceLength, float emSize)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("0", StringComparison.OrdinalIgnoreCase))
            return 0;

        // '%' and all other units are resolved by CssLengthParser below
        // (referenceLength is the percentage basis), so no inline % handling needed.
        if (CssLengthParser.IsValidLength(value))
            return (float)CssLengthParser.ParseLength(value, referenceLength, emSize, defaultUnit: null);

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float raw))
            return raw;

        return 0;
    }

    private static bool TryCreateRoundedBackgroundClipItem(RectangleF borderBoxRect, Fragment fragment, string backgroundClip, out ClipItem clipItem)
    {
        clipItem = null!;

        if (!string.Equals(backgroundClip, "padding-box", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backgroundClip, "content-box", StringComparison.OrdinalIgnoreCase))
            return false;

        var style = fragment.Style;
        bool hasCornerRadius = style.ActualCornerNw > 0 || style.ActualCornerNe > 0
            || style.ActualCornerSe > 0 || style.ActualCornerSw > 0;
        if (!hasCornerRadius)
            return false;

        var clipRect = GetBackgroundClipRect(borderBoxRect, fragment, backgroundClip);
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return false;

        var border = fragment.Border;
        var padding = fragment.Padding;
        float insetLeft = (float)border.Left;
        float insetTop = (float)border.Top;
        float insetRight = (float)border.Right;
        float insetBottom = (float)border.Bottom;

        if (backgroundClip.Equals("content-box", StringComparison.OrdinalIgnoreCase))
        {
            insetLeft += (float)padding.Left;
            insetTop += (float)padding.Top;
            insetRight += (float)padding.Right;
            insetBottom += (float)padding.Bottom;
        }

        double cornerNwY = GetEffectiveCornerRadiusY(style.CornerNwRadiusRaw, style.ActualCornerNw, borderBoxRect);
        double cornerNeY = GetEffectiveCornerRadiusY(style.CornerNeRadiusRaw, style.ActualCornerNe, borderBoxRect);
        double cornerSeY = GetEffectiveCornerRadiusY(style.CornerSeRadiusRaw, style.ActualCornerSe, borderBoxRect);
        double cornerSwY = GetEffectiveCornerRadiusY(style.CornerSwRadiusRaw, style.ActualCornerSw, borderBoxRect);

        clipItem = new ClipItem
        {
            Bounds = clipRect,
            ClipRect = clipRect,
            CornerNw = Math.Max(0, style.ActualCornerNw - insetLeft),
            CornerNwY = Math.Max(0, cornerNwY - insetTop),
            CornerNe = Math.Max(0, style.ActualCornerNe - insetRight),
            CornerNeY = Math.Max(0, cornerNeY - insetTop),
            CornerSe = Math.Max(0, style.ActualCornerSe - insetRight),
            CornerSeY = Math.Max(0, cornerSeY - insetBottom),
            CornerSw = Math.Max(0, style.ActualCornerSw - insetLeft),
            CornerSwY = Math.Max(0, cornerSwY - insetBottom),
        };
        return true;
    }

    private static double GetEffectiveCornerRadiusY(string rawRadius, double cornerRadiusX, RectangleF bounds)
    {
        if (!string.IsNullOrEmpty(rawRadius)
            && rawRadius.Contains('%', StringComparison.Ordinal)
            && bounds.Width > 0)
            return cornerRadiusX * bounds.Height / bounds.Width;

        return cornerRadiusX;
    }
}
