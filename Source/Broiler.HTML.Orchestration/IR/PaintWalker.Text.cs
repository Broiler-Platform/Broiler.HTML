using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// Text and text-decoration emission plus font-size parsing.
// Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    private static void EmitText(Fragment fragment, List<DisplayItem> items, BColor? bgClipTextColor = null)
    {
        if (fragment.Lines == null || fragment.Lines.Count == 0)
            return;

        var style = fragment.Style;
        bool isRtl = style.Direction == "rtl";
        GradientInfo? bgClipTextGradient = null;
        if (string.Equals(style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase)
            && HasGradientBackgroundImage(style.BackgroundImage))
        {
            foreach (var layer in SplitGradientLayers(style.BackgroundImage))
            {
                bgClipTextGradient = ParseGradientFunction(layer.Trim());
                if (bgClipTextGradient?.Stops.Count > 0)
                    break;
            }
        }

        foreach (var line in fragment.Lines)
        {
            RectangleF lineGradientBounds = RectangleF.Empty;
            if (bgClipTextGradient != null)
            {
                foreach (var candidate in line.Inlines)
                {
                    if (string.IsNullOrEmpty(candidate.Text) || candidate.Text == "\n")
                        continue;

                    var candidateBounds = new RectangleF(candidate.X, candidate.Y, candidate.Width, candidate.Height);
                    lineGradientBounds = lineGradientBounds == RectangleF.Empty
                        ? candidateBounds
                        : RectangleF.Union(lineGradientBounds, candidateBounds);
                }
            }

            foreach (var inline in line.Inlines)
            {
                if (string.IsNullOrEmpty(inline.Text))
                    continue;

                // Skip line-break placeholders (CssRect uses "\n" for <br> elements)
                if (inline.Text == "\n")
                    continue;

                var inlineStyle = inline.Style;
                var inlineBounds = new RectangleF(inline.X, inline.Y, inline.Width, inline.Height);
                var gradientBounds = lineGradientBounds == RectangleF.Empty ? inlineBounds : lineGradientBounds;

                // CSS Backgrounds Level 4: background-clip: text — the text
                // color is composited with the background color so that the
                // background is visible through the text shape.
                BColor textColor = inlineStyle.ActualColor;
                if (bgClipTextColor.HasValue)
                    textColor = CompositeTextColor(bgClipTextColor.Value, textColor);

                var (shadowX, shadowY, shadowColor) = ParseTextShadow(inlineStyle.TextShadow);

                items.Add(new DrawTextItem
                {
                    Bounds = inlineBounds,
                    Text = inline.Text,
                    FontFamily = inlineStyle.FontFamily,
                    FontSize = (float)ParseFontSize(inlineStyle.FontSize),
                    FontWeight = inlineStyle.FontWeight,
                    Color = textColor,
                    Origin = new PointF(inline.X, inline.Y),
                    FontHandle = inline.FontHandle,
                    IsRtl = isRtl,
                    GlyphRotationDeg = inline.GlyphRotationDeg,
                    TextShadowOffsetX = shadowX,
                    TextShadowOffsetY = shadowY,
                    TextShadowColor = shadowColor,
                    GradientStops = bgClipTextGradient?.Stops,
                    GradientAngle = bgClipTextGradient?.Angle ?? 180f,
                    GradientInterpolationSpace = bgClipTextGradient?.InterpolationSpace ?? "srgb",
                    GradientBounds = gradientBounds,
                });
            }
        }
    }

    private static void EmitTextDecoration(Fragment fragment, List<DisplayItem> items, BColor? bgClipTextColor = null)
    {
        if (fragment.Lines == null || fragment.Lines.Count == 0)
            return;

        // Check text-decoration on the fragment itself and on its inline children.
        // In the box tree, text-decoration may be on the block or on anonymous inline children.
        string decoration = fragment.Style.TextDecoration;
        var decorationStyleSource = fragment.Style;

        // If the block fragment doesn't have decoration, check children and inlines.
        // First child with a decoration wins (consistent with old CssBox.PaintDecoration
        // which only supported a single TextDecoration per box).
        if (string.IsNullOrEmpty(decoration) || decoration == "none")
        {
            // Check if any child fragment has text-decoration
            foreach (var child in fragment.Children)
            {
                if (!string.IsNullOrEmpty(child.Style.TextDecoration) && child.Style.TextDecoration != "none")
                {
                    decoration = child.Style.TextDecoration;
                    decorationStyleSource = child.Style;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(decoration) || decoration == "none")
            return;

        // CSS Backgrounds Level 4: background-clip: text — text-decoration
        // uses the composited color so decorations also show the background.
        BColor decoColor = decorationStyleSource.ActualTextDecorationColor;
        if (bgClipTextColor.HasValue)
            decoColor = CompositeTextColor(bgClipTextColor.Value, decoColor);

        var rects = GetPaintRects(fragment);

        foreach (var rect in rects)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            var border = fragment.Border;
            var padding = fragment.Padding;

            float x1 = rect.X + (float)padding.Left + (float)border.Left;
            float x2 = rect.Right - (float)padding.Right - (float)border.Right;

            foreach (var line in fragment.Lines)
            {
                float y;
                if (decoration == "underline")
                    y = line.Y + line.Height * 0.85f; // approximate underline offset (~85% of line height)
                else if (decoration == "line-through")
                    y = line.Y + line.Height / 2f; // center of line
                else if (decoration == "overline")
                    y = line.Y; // top of line
                else
                    continue;

                items.Add(new DrawLineItem
                {
                    Bounds = new RectangleF(x1, y, x2 - x1, 1),
                    Start = new PointF(x1, y),
                    End = new PointF(x2, y),
                    Color = decoColor,
                    Width = 1,
                    DashStyle = "solid",
                });
            }
        }
    }

    /// <summary>
    /// Composites a foreground text color over a background color using
    /// standard alpha compositing (src-over).  For <c>background-clip: text</c>,
    /// the background shows through the text shape and the foreground text color
    /// is painted on top.
    /// </summary>
    private static BColor CompositeTextColor(BColor bg, BColor fg)
    {
        float fgA = fg.A / 255f;
        int r = (int)(bg.R * (1 - fgA) + fg.R * fgA);
        int g = (int)(bg.G * (1 - fgA) + fg.G * fgA);
        int b = (int)(bg.B * (1 - fgA) + fg.B * fgA);
        return BColor.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    private static double ParseFontSize(string fontSize)
    {
        if (string.IsNullOrEmpty(fontSize))
            return 12; // default: matches CssConstants.FontSize (12pt)

        // CSS 2.1 §15.7 named absolute sizes mapped to pt values
        // (relative to CssConstants.FontSize = 12)
        return fontSize switch
        {
            "medium" => 12,
            "xx-small" => 8,
            "x-small" => 9,
            "small" => 10,
            "large" => 14,
            "x-large" => 15,
            "xx-large" => 16,
            _ => TryParseNumeric(fontSize, 12),
        };
    }

    private static double TryParseNumeric(string value, double fallback)
    {
        // Strip common CSS units
        var numeric = value;
        if (numeric.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            numeric = numeric[..^2];
        else if (numeric.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            numeric = numeric[..^2];
        else if (numeric.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            numeric = numeric[..^2];

        return double.TryParse(numeric, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }
}
