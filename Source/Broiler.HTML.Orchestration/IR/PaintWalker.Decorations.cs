using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// Borders, outline, selection highlights, replaced/SVG content, and inline-box
// decorations. Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    /// <summary>
    /// CSS2.1 Appendix E: the background and borders of a non-replaced inline
    /// box (<c>display:inline</c>) paint <em>behind</em> the line's text. The
    /// text of every inline box on a line is emitted in one pass from the
    /// containing block's line boxes (<see cref="EmitText"/>); each
    /// <c>display:inline</c> child fragment carries only its own
    /// background/border (its glyphs live in the block's lines). Painted in the
    /// normal child phase (Step 5) those backgrounds land <em>on top of</em> the
    /// already-emitted text, hiding it — visible on any coloured inline span over
    /// a background. This pass emits them first, just before the block's text;
    /// the Step-5 inline paint then suppresses re-emission via
    /// <c>asInlineContent</c>. Positioned / stacking-context inline boxes are
    /// left to their own (later) phase.
    /// </summary>
    private static void EmitInlineLevelBoxDecorations(Fragment fragment, List<DisplayItem> items, RectangleF viewport)
    {
        if (fragment.Children.Count == 0)
            return;

        foreach (var child in fragment.Children)
        {
            if (!string.Equals(child.Style.Display, "inline", StringComparison.Ordinal))
                continue;
            if (child.Style.Position is "relative" or "absolute" or "fixed")
                continue;
            if (child.CreatesStackingContext)
                continue;

            if (child.Style.Visibility == "visible")
                EmitInlineBoxBackgroundAndBorder(child, items, viewport);

            // Recurse into nested inline boxes (their text is also in the
            // owning block's line boxes), even through a non-visible box whose
            // descendants may be visible.
            EmitInlineLevelBoxDecorations(child, items, viewport);
        }
    }

    /// <summary>
    /// Emits one inline box's background, background image, and borders — the
    /// same sequence (and rounded-corner clipping) <see cref="PaintFragment"/>
    /// uses for an element's own decorations, factored out so the inline
    /// pre-text pass and the Step-5 paint stay in sync.
    /// </summary>
    private static void EmitInlineBoxBackgroundAndBorder(Fragment fragment, List<DisplayItem> items, RectangleF viewport)
    {
        var style = fragment.Style;
        var bounds = fragment.Bounds;

        bool bgClippedRounded = false;
        bool hasCornerRadius = style.ActualCornerNw > 0 || style.ActualCornerNe > 0
            || style.ActualCornerSe > 0 || style.ActualCornerSw > 0;
        if (hasCornerRadius)
        {
            items.Add(new ClipItem
            {
                Bounds = bounds,
                ClipRect = bounds,
                CornerNw = style.ActualCornerNw,
                CornerNwY = GetEffectiveCornerRadiusY(style.CornerNwRadiusRaw, style.ActualCornerNw, bounds),
                CornerNe = style.ActualCornerNe,
                CornerNeY = GetEffectiveCornerRadiusY(style.CornerNeRadiusRaw, style.ActualCornerNe, bounds),
                CornerSe = style.ActualCornerSe,
                CornerSeY = GetEffectiveCornerRadiusY(style.CornerSeRadiusRaw, style.ActualCornerSe, bounds),
                CornerSw = style.ActualCornerSw,
                CornerSwY = GetEffectiveCornerRadiusY(style.CornerSwRadiusRaw, style.ActualCornerSw, bounds),
            });
            bgClippedRounded = true;
        }

        EmitBackground(fragment, items);
        EmitBackgroundImage(fragment, items, viewport);
        EmitBorders(fragment, items);

        if (bgClippedRounded)
            items.Add(new RestoreItem { Bounds = bounds });
    }

    private static void EmitReplacedImage(Fragment fragment, List<DisplayItem> items)
    {
        // SVG content for <object data="...svg"> elements — render via SvgRenderer.
        if (!string.IsNullOrEmpty(fragment.SvgContent))
        {
            EmitSvgContent(fragment, items);
            return;
        }

        if (fragment.ImageHandle == null)
            return;

        // Use GetPaintRects to handle inline replaced elements (e.g. <img>,
        // <object data="data:image/…">) whose fragment.Bounds may have zero
        // height because CssBox.Size is not set for inline boxes during layout.
        // The correct dimensions are in InlineRects (from CssBox.Rectangles),
        // populated during line-box layout.
        var rects = GetPaintRects(fragment);
        var border = fragment.Border;
        var padding = fragment.Padding;

        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            // Image dest rect: inside border + padding (matching CssBoxImage.PaintImp)
            var r = new RectangleF(
                (float)Math.Floor(bounds.X + border.Left + padding.Left),
                (float)Math.Floor(bounds.Y + border.Top + padding.Top),
                bounds.Width - (float)(border.Left + border.Right + padding.Left + padding.Right),
                bounds.Height - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom));

            if (r.Width > 0 && r.Height > 0)
            {
                items.Add(new DrawImageItem
                {
                    Bounds = r,
                    ImageHandle = fragment.ImageHandle,
                    SourceRect = fragment.ImageSourceRect,
                    DestRect = r,
                });
            }
        }
    }

    /// <summary>
    /// Renders SVG content stored on the fragment using <see cref="SvgRenderer"/>.
    /// </summary>
    private static void EmitSvgContent(Fragment fragment, List<DisplayItem> items)
    {
        var rects = GetPaintRects(fragment);
        var border = fragment.Border;
        var padding = fragment.Padding;

        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            var r = new RectangleF(
                (float)Math.Floor(bounds.X + border.Left + padding.Left),
                (float)Math.Floor(bounds.Y + border.Top + padding.Top),
                bounds.Width - (float)(border.Left + border.Right + padding.Left + padding.Right),
                bounds.Height - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom));

            if (r.Width > 0 && r.Height > 0)
                items.AddRange(SvgRenderer.RenderSvgContent(fragment.SvgContent, r));
        }
    }

    private static void EmitSelection(Fragment fragment, List<DisplayItem> items)
    {
        if (fragment.Lines == null || fragment.Lines.Count == 0)
            return;

        foreach (var line in fragment.Lines)
        {
            foreach (var inline in line.Inlines)
            {
                if (!inline.Selected)
                    continue;

                // Selection highlight rectangle
                var left = inline.SelectedStartOffset > FullSelectionOffset ? (float)inline.SelectedStartOffset : 0f;
                var width = inline.SelectedEndOffset > FullSelectionOffset ? (float)inline.SelectedEndOffset - left : inline.Width - left;

                if (width <= 0)
                    continue;

                items.Add(new FillRectItem
                {
                    Bounds = new RectangleF(inline.X + left, inline.Y, width, line.Height),
                    Color = SelectionHighlightColor,
                });
            }
        }
    }

    private static void EmitBorders(Fragment fragment, List<DisplayItem> items)
    {
        var style = fragment.Style;
        var border = fragment.Border;

        bool hasTop = HasBorder(style.BorderTopStyle, border.Top);
        bool hasRight = HasBorder(style.BorderRightStyle, border.Right);
        bool hasBottom = HasBorder(style.BorderBottomStyle, border.Bottom);
        bool hasLeft = HasBorder(style.BorderLeftStyle, border.Left);

        if (!hasTop && !hasRight && !hasBottom && !hasLeft)
            return;

        var rects = GetPaintRects(fragment);

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            bool isFirst = i == 0;
            bool isLast = i == rects.Count - 1;

            items.Add(new DrawBorderItem
            {
                Bounds = rect,
                Widths = border,
                TopColor = hasTop ? style.ActualBorderTopColor : BColor.Empty,
                RightColor = (hasRight && isLast) ? style.ActualBorderRightColor : BColor.Empty,
                BottomColor = hasBottom ? style.ActualBorderBottomColor : BColor.Empty,
                LeftColor = (hasLeft && isFirst) ? style.ActualBorderLeftColor : BColor.Empty,
                // Style kept for Phase 1 backward compat; per-side styles are authoritative
                Style = style.BorderTopStyle ?? "solid",
                TopStyle = style.BorderTopStyle ?? "none",
                RightStyle = (isLast) ? (style.BorderRightStyle ?? "none") : "none",
                BottomStyle = style.BorderBottomStyle ?? "none",
                LeftStyle = (isFirst) ? (style.BorderLeftStyle ?? "none") : "none",
                CornerNw = style.ActualCornerNw,
                CornerNe = style.ActualCornerNe,
                CornerSe = style.ActualCornerSe,
                CornerSw = style.ActualCornerSw,
            });
        }
    }

    /// <summary>
    /// CSS UI §2: paints the element's outline just outside the border edge,
    /// separated by <c>outline-offset</c>. The outline takes no layout space, so it
    /// is drawn as a uniform border on the border-box inflated by
    /// <c>outline-offset + outline-width</c> (the band between that rect's edge and
    /// the offset gap is exactly the outline).
    /// </summary>
    private static void EmitOutline(Fragment fragment, List<DisplayItem> items)
    {
        var style = fragment.Style;
        double ow = style.OutlineWidth;
        string os = style.OutlineStyle;
        if (ow <= 0 || string.IsNullOrEmpty(os)
            || os.Equals("none", StringComparison.OrdinalIgnoreCase)
            || os.Equals("hidden", StringComparison.OrdinalIgnoreCase))
            return;

        // 'auto' (focus ring) has no defined appearance here; render it solid.
        string drawStyle = os.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "solid" : os;
        var color = style.ActualOutlineColor;
        var widths = new BoxEdges(ow, ow, ow, ow);
        float inflate = (float)(style.OutlineOffset + ow);

        foreach (var r in GetPaintRects(fragment))
        {
            if (r.Width <= 0 || r.Height <= 0)
                continue;

            var outlineRect = RectangleF.Inflate(r, inflate, inflate);
            items.Add(new DrawBorderItem
            {
                Bounds = outlineRect,
                Widths = widths,
                TopColor = color,
                RightColor = color,
                BottomColor = color,
                LeftColor = color,
                Style = drawStyle,
                TopStyle = drawStyle,
                RightStyle = drawStyle,
                BottomStyle = drawStyle,
                LeftStyle = drawStyle,
            });
        }
    }

    private static bool HasBorder(string? borderStyle, double width)
    {
        if (width <= 0)
            return false;
        if (string.IsNullOrEmpty(borderStyle))
            return false;
        if (borderStyle == "none" || borderStyle == "hidden")
            return false;
        return true;
    }
}
