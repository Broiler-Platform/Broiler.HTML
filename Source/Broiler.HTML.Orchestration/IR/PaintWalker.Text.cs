using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Graphics.Color;
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
                // Paint-only used length (CSS zoom, increment 5): the text-shadow offsets are resolved here
                // from the raw string rather than from the zoomed box geometry, so scale them by the box's
                // effective zoom. 1.0 (the default) while the native-zoom engine is off, so this is inert.
                if (inlineStyle.EffectiveZoom != 1.0)
                {
                    shadowX *= (float)inlineStyle.EffectiveZoom;
                    shadowY *= (float)inlineStyle.EffectiveZoom;
                }

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

    /// <summary>
    /// Emits the text decorations of a block's lines: each in-flow inline box's lines across that
    /// box's own extent on each line, so a link is underlined beneath its words and the spaces between
    /// them, not across the rest of the line (CSS Text Decoration 3 §2.1).
    /// </summary>
    /// <remarks>
    /// The box tree hands a decoration down from a box with no text of its own to its children
    /// (<c>DomParser.CascadeApplyStyles</c>), so it ends on the anonymous boxes that hold the words:
    /// the <c>&lt;a&gt;</c> of a link, a <c>&lt;u&gt;</c> or a <c>p { text-decoration: underline }</c>
    /// keeps none itself. Only the block's own decoration was read, or its first child's drawn across
    /// the whole line, so the box one level further down never was: no link and no <c>&lt;u&gt;</c>
    /// was underlined.
    /// </remarks>
    private static void EmitTextDecoration(Fragment fragment, List<DisplayItem> items, BColor? bgClipTextColor = null)
    {
        if (fragment.Lines == null || fragment.Lines.Count == 0)
            return;

        var lines = fragment.Lines;
        if (DecorationLines(fragment.Style.TextDecoration) != TextDecorationLines.None)
        {
            foreach (var line in lines)
                EmitDecoration(fragment.Style, new RectangleF(line.X, line.Y, line.Width, line.Height), lines, items, bgClipTextColor);
        }

        EmitInlineTextDecorations(fragment, lines, items, bgClipTextColor);
    }

    /// <summary>
    /// The decorations of the in-flow inline boxes under <paramref name="parent"/>, whose words are in
    /// the containing block's <paramref name="lines"/>: one set per line the box is on.
    /// </summary>
    private static void EmitInlineTextDecorations(
        Fragment parent, IReadOnlyList<LineFragment> lines, List<DisplayItem> items, BColor? bgClipTextColor)
    {
        foreach (var child in parent.Children)
        {
            if (!string.Equals(child.Style.Display, "inline", StringComparison.Ordinal))
                continue;

            if (child.InlineRects is { Count: > 0 } rects
                && child.Style.Visibility == "visible"
                && DecorationLines(child.Style.TextDecoration) != TextDecorationLines.None)
            {
                foreach (var rect in rects)
                    EmitDecoration(child.Style, rect, lines, items, bgClipTextColor);
            }

            EmitInlineTextDecorations(child, lines, items, bgClipTextColor);
        }
    }

    /// <summary>
    /// One box's decoration lines across <paramref name="rect"/>, its extent on one line: an underline
    /// where the font of the words there puts it, an overline along the top, a line-through across the
    /// middle, each as thick as the 0.07em stroke of the common sans-serif faces.
    /// </summary>
    private static void EmitDecoration(
        ComputedStyle style, RectangleF rect, IReadOnlyList<LineFragment> lines, List<DisplayItem> items, BColor? bgClipTextColor)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // CSS Backgrounds Level 4: background-clip: text — text-decoration
        // uses the composited color so decorations also show the background.
        BColor color = style.ActualTextDecorationColor;
        if (bgClipTextColor.HasValue)
            color = CompositeTextColor(bgClipTextColor.Value, color);

        var font = FontWithin(lines, rect);
        double sizePt = font?.Size ?? ParseFontSize(style.FontSize);
        float thickness = MathF.Max(1f, MathF.Round((float)(sizePt * 96.0 / 72.0) / 14f));
        var decoration = DecorationLines(style.TextDecoration);
        if ((decoration & TextDecorationLines.Underline) != 0)
        {
            // The font's underline offset is measured from the top of its line box, which is where the
            // words' boxes, and so the rect, start.
            float offset = font is { UnderlineOffset: > 0 } && font.UnderlineOffset < rect.Height
                ? (float)font.UnderlineOffset
                : rect.Height * 0.85f;
            EmitDecorationStroke(items, rect, MathF.Round(rect.Y + offset), thickness, color, style.TextDecorationStyle);
        }

        if ((decoration & TextDecorationLines.Overline) != 0)
            EmitDecorationStroke(items, rect, MathF.Round(rect.Y), thickness, color, style.TextDecorationStyle);

        if ((decoration & TextDecorationLines.LineThrough) != 0)
            EmitDecorationStroke(items, rect, MathF.Round(rect.Y + (rect.Height - thickness) / 2f), thickness, color, style.TextDecorationStyle);
    }

    /// <summary>
    /// A decoration stroke whose top edge is <paramref name="top"/>; <c>double</c> adds a second one a
    /// stroke's width below it, and <c>wavy</c> is drawn straight.
    /// </summary>
    private static void EmitDecorationStroke(List<DisplayItem> items, RectangleF rect, float top, float thickness, BColor color, string? style)
    {
        var dashStyle = style is "dotted" or "dashed" ? style : "solid";
        AddStroke(top);
        if (style == "double")
            AddStroke(top + (2 * thickness));

        void AddStroke(float strokeTop)
        {
            float y = strokeTop + (thickness / 2f);
            items.Add(new DrawLineItem
            {
                Bounds = new RectangleF(rect.X, strokeTop, rect.Width, thickness),
                Start = new PointF(rect.X, y),
                End = new PointF(rect.Right, y),
                Color = color,
                Width = thickness,
                DashStyle = dashStyle,
            });
        }
    }

    /// <summary>The font of the first word of <paramref name="lines"/> inside <paramref name="rect"/>.</summary>
    private static Broiler.Graphics.Text.ILayoutFont? FontWithin(IReadOnlyList<LineFragment> lines, RectangleF rect)
    {
        foreach (var line in lines)
        {
            if (line.Y > rect.Bottom || line.Y + line.Height < rect.Y)
                continue;

            foreach (var inline in line.Inlines)
            {
                if (inline.FontHandle is Broiler.Graphics.Text.ILayoutFont font
                    && inline.X >= rect.X - 0.5f && inline.X + inline.Width <= rect.Right + 0.5f
                    && inline.Y >= rect.Y - 0.5f && inline.Y < rect.Bottom)
                {
                    return font;
                }
            }
        }

        return null;
    }

    [Flags]
    private enum TextDecorationLines
    {
        None = 0,
        Underline = 1,
        Overline = 2,
        LineThrough = 4,
    }

    /// <summary>The lines a <c>text-decoration-line</c> value names, which may be several.</summary>
    private static TextDecorationLines DecorationLines(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "none")
            return TextDecorationLines.None;

        var lines = TextDecorationLines.None;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            lines |= token.ToLowerInvariant() switch
            {
                "underline" => TextDecorationLines.Underline,
                "overline" => TextDecorationLines.Overline,
                "line-through" => TextDecorationLines.LineThrough,
                _ => TextDecorationLines.None,
            };
        }

        return lines;
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
