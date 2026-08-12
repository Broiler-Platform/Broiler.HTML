using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Graphics;
using Broiler.Layout.IR;
using Bevel = Broiler.Layout.Engine.BorderBevel;


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

        // Outset box-shadow paints behind the inline box's own background (and outside its clip).
        EmitBoxShadow(fragment, items, inset: false);

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
        // Inset box-shadow paints over the background, inside the box, beneath the borders.
        EmitBoxShadow(fragment, items, inset: true);
        EmitBorders(fragment, items);

        if (bgClippedRounded)
            items.Add(new RestoreItem { Bounds = bounds });
    }

    /// <summary>
    /// Emits the outset layers of a fragment's CSS <c>box-shadow</c> (CSS Backgrounds §7) as solid
    /// fills behind the element's border box — one per painted rect, so an inline box shadows each
    /// of its line fragments. Must be emitted <em>before</em> the element's background so the shadow
    /// sits behind it; a solid background then covers the part of an outset shadow that overlaps the
    /// box, leaving only the visible offset/spread halo.
    /// <para>
    /// A zero-blur layer is a single sharp fill; a blurred layer is approximated by
    /// <see cref="EmitBlurredShadow"/> (concentric solid fills matching the Gaussian coverage).
    /// <c>inset</c> layers are skipped (they paint inside the box, over the background, and are not
    /// yet modelled). This is why a <c>box-shadow: -20px -20px yellow</c> on an inline
    /// <c>&lt;span&gt;</c> — previously not painted at all — now shows its yellow offset rectangle
    /// (WPT css/css-view-transitions/inline-child-with-overflow-shadow).
    /// </para>
    /// </summary>
    private static void EmitBoxShadow(Fragment fragment, List<DisplayItem> items, bool inset)
    {
        var style = fragment.Style;
        var layers = ParseBoxShadow(style.BoxShadow, style.ActualColor);
        if (layers.Count == 0)
            return;

        bool hasCornerRadius = style.ActualCornerNw > 0 || style.ActualCornerNe > 0
            || style.ActualCornerSe > 0 || style.ActualCornerSw > 0;

        foreach (var rect in GetPaintRects(fragment))
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            // Paint later-listed layers first so the first-listed shadow ends up on top.
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var s = layers[i];
                if (s.Inset != inset || s.Color.A == 0)
                    continue;

                if (inset)
                {
                    EmitInsetShadow(items, fragment, rect, s, hasCornerRadius);
                    continue;
                }

                // Outset: the un-blurred shape is the border box, offset, then grown by the spread.
                var core = InflateRect(new RectangleF(
                    rect.X + s.OffsetX, rect.Y + s.OffsetY, rect.Width, rect.Height), s.Spread);
                if (core.Width <= 0 || core.Height <= 0)
                    continue;

                if (s.Blur > 0.5f)
                    EmitBlurredShadow(items, fragment, rect, s, core, hasCornerRadius);
                else
                    EmitShadowFill(items, fragment, rect, core, s.Spread, s.Color, hasCornerRadius);
            }
        }
    }

    /// <summary>
    /// Emits an <c>inset</c> box-shadow layer (CSS Backgrounds §7). Unlike an outset shadow this
    /// paints <em>inside</em> the border box, over the background: the shadow fills the frame between
    /// the border box and an inner "clear" rectangle (the border box inset by the spread, then
    /// offset), so it reads as a recess. The whole layer is clipped to the border box. A zero-blur
    /// layer is that frame as up to four solid strips; a blurred layer softens the inner edge with the
    /// same Gaussian-coverage shells as the outset path, each shell being the frame around a
    /// progressively inflated inner rectangle. The strips of one shell never overlap, so alpha does
    /// not double up at the corners.
    /// </summary>
    private static void EmitInsetShadow(
        List<DisplayItem> items, Fragment fragment, RectangleF borderRect,
        in BoxShadowLayer s, bool hasCornerRadius)
    {
        // Confine the shadow to the (optionally rounded) border box.
        EmitBorderBoxClip(items, fragment, borderRect, hasCornerRadius);

        // The clear inner rectangle: border box shrunk by the spread, then translated by the offset.
        var innerBase = InflateRect(borderRect, -s.Spread);
        innerBase = new RectangleF(innerBase.X + s.OffsetX, innerBase.Y + s.OffsetY, innerBase.Width, innerBase.Height);

        if (s.Blur > 0.5f)
        {
            float sigma = Math.Max(0.01f, s.Blur * 0.5f);
            float extent = 3f * sigma;
            int shells = Math.Clamp((int)MathF.Ceiling(extent), 4, 24);
            float peak = s.Color.A;
            float prevT = 0f;
            for (int k = 0; k < shells; k++)
            {
                // Boundary `e` px outside the clear rect (from -extent inside to +extent toward border).
                float e = -extent + ((k + 1) / (float)(shells + 1)) * (2f * extent);
                float targetT = peak * NormalCdf(e / sigma);
                float denom = 255f - prevT;
                float aShell = denom > 0.001f ? (targetT - prevT) / denom : 0f;
                prevT = targetT;
                int alpha = (int)Math.Round(Math.Clamp(aShell, 0f, 1f) * 255f);
                if (alpha <= 0)
                    continue;
                var shellColor = BColor.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B);
                EmitFrameStrips(items, borderRect, InflateRect(innerBase, e), shellColor);
            }
        }
        else
        {
            EmitFrameStrips(items, borderRect, innerBase, s.Color);
            // Round the clear rectangle's corners to match the (rounded) border box, concentric —
            // the inner radius is the border radius reduced by the inset distance (the spread).
            if (hasCornerRadius)
                EmitInnerCornerNotches(items, fragment, innerBase, s.Spread, s.Color);
        }

        items.Add(new RestoreItem { Bounds = borderRect });
    }

    /// <summary>
    /// Rounds the corners of an inset shadow's clear rectangle by filling, at each corner, the region
    /// inside the sharp corner but outside the rounded arc (a quarter-square minus its quarter-ellipse)
    /// as thin horizontal slices. The clear rectangle is concentric with the border box, so each inner
    /// radius is the border radius reduced by <paramref name="inset"/> (the shadow's inset distance).
    /// These notch fills sit within the sharp clear rectangle, which the frame strips leave unpainted,
    /// so they never overlap the strips.
    /// </summary>
    private static void EmitInnerCornerNotches(
        List<DisplayItem> items, Fragment fragment, RectangleF inner, float inset, BColor color)
    {
        if (inner.Width <= 0 || inner.Height <= 0)
            return;

        var style = fragment.Style;
        float halfW = inner.Width / 2f;
        float halfH = inner.Height / 2f;

        float Radius(double actual) => Math.Clamp((float)actual - inset, 0f, Math.Min(halfW, halfH));
        float RadiusY(string raw, double actual) =>
            Math.Clamp((float)GetEffectiveCornerRadiusY(raw, actual, inner) - inset, 0f, Math.Min(halfW, halfH));

        EmitCornerNotch(items, inner.Left, inner.Top, Radius(style.ActualCornerNw), RadiusY(style.CornerNwRadiusRaw, style.ActualCornerNw), +1f, +1f, color);
        EmitCornerNotch(items, inner.Right, inner.Top, Radius(style.ActualCornerNe), RadiusY(style.CornerNeRadiusRaw, style.ActualCornerNe), -1f, +1f, color);
        EmitCornerNotch(items, inner.Right, inner.Bottom, Radius(style.ActualCornerSe), RadiusY(style.CornerSeRadiusRaw, style.ActualCornerSe), -1f, -1f, color);
        EmitCornerNotch(items, inner.Left, inner.Bottom, Radius(style.ActualCornerSw), RadiusY(style.CornerSwRadiusRaw, style.ActualCornerSw), +1f, -1f, color);
    }

    /// <summary>
    /// Fills one rounded-corner notch: the area between the sharp corner at
    /// (<paramref name="cornerX"/>, <paramref name="cornerY"/>) and the quarter-ellipse of radii
    /// (<paramref name="rX"/>, <paramref name="rY"/>) whose centre lies inside the rectangle in the
    /// (<paramref name="dirX"/>, <paramref name="dirY"/>) direction. Emitted as 1px-tall slices.
    /// </summary>
    private static void EmitCornerNotch(
        List<DisplayItem> items, float cornerX, float cornerY, float rX, float rY,
        float dirX, float dirY, BColor color)
    {
        if (rX <= 0.5f || rY <= 0.5f)
            return;

        float centerX = cornerX + (dirX * rX);
        float centerY = cornerY + (dirY * rY);
        int steps = (int)MathF.Ceiling(rY);
        for (int yy = 0; yy < steps; yy++)
        {
            float sliceTop = dirY > 0 ? cornerY + yy : cornerY - yy - 1f;
            float sy = sliceTop + 0.5f;
            float dyN = MathF.Min(1f, MathF.Abs(sy - centerY) / rY);
            float dx = rX * MathF.Sqrt(MathF.Max(0f, 1f - (dyN * dyN)));
            float arcX = centerX - (dirX * dx);          // clear-region boundary along this row
            float w = MathF.Abs(arcX - cornerX);
            if (w <= 0.01f)
                continue;
            float x0 = MathF.Min(cornerX, arcX);
            items.Add(new FillRectItem { Bounds = new RectangleF(x0, sliceTop, w, 1f), Color = color });
        }
    }

    /// <summary>
    /// Fills <paramref name="outer"/> minus its intersection with <paramref name="inner"/> as up to
    /// four non-overlapping rectangles (top and bottom bands span the full width; the left and right
    /// bands fill only the height between them). Used to paint an inset shadow's frame without the
    /// corner over-draw that overlapping edge strips would cause.
    /// </summary>
    private static void EmitFrameStrips(List<DisplayItem> items, RectangleF outer, RectangleF inner, BColor color)
    {
        float il = Math.Max(outer.Left, inner.Left);
        float ir = Math.Min(outer.Right, inner.Right);
        float it = Math.Max(outer.Top, inner.Top);
        float ib = Math.Min(outer.Bottom, inner.Bottom);

        // The clear rectangle does not overlap the border box: the whole box is shadowed.
        if (ir <= il || ib <= it)
        {
            items.Add(new FillRectItem { Bounds = outer, Color = color });
            return;
        }

        if (it > outer.Top)
            items.Add(new FillRectItem { Bounds = new RectangleF(outer.Left, outer.Top, outer.Width, it - outer.Top), Color = color });
        if (ib < outer.Bottom)
            items.Add(new FillRectItem { Bounds = new RectangleF(outer.Left, ib, outer.Width, outer.Bottom - ib), Color = color });
        if (il > outer.Left)
            items.Add(new FillRectItem { Bounds = new RectangleF(outer.Left, it, il - outer.Left, ib - it), Color = color });
        if (ir < outer.Right)
            items.Add(new FillRectItem { Bounds = new RectangleF(ir, it, outer.Right - ir, ib - it), Color = color });
    }

    /// <summary>Pushes a clip to the element's border box, rounded when it has a border radius.</summary>
    private static void EmitBorderBoxClip(List<DisplayItem> items, Fragment fragment, RectangleF borderRect, bool hasCornerRadius)
    {
        if (!hasCornerRadius)
        {
            items.Add(new ClipItem { Bounds = borderRect, ClipRect = borderRect });
            return;
        }

        var style = fragment.Style;
        items.Add(new ClipItem
        {
            Bounds = borderRect,
            ClipRect = borderRect,
            CornerNw = style.ActualCornerNw,
            CornerNwY = GetEffectiveCornerRadiusY(style.CornerNwRadiusRaw, style.ActualCornerNw, borderRect),
            CornerNe = style.ActualCornerNe,
            CornerNeY = GetEffectiveCornerRadiusY(style.CornerNeRadiusRaw, style.ActualCornerNe, borderRect),
            CornerSe = style.ActualCornerSe,
            CornerSeY = GetEffectiveCornerRadiusY(style.CornerSeRadiusRaw, style.ActualCornerSe, borderRect),
            CornerSw = style.ActualCornerSw,
            CornerSwY = GetEffectiveCornerRadiusY(style.CornerSwRadiusRaw, style.ActualCornerSw, borderRect),
        });
    }

    /// <summary>
    /// Emits a blurred outset shadow layer as a stack of concentric solid fills whose cumulative
    /// SrcOver coverage approximates a Gaussian blur of the shadow shape. Per CSS Backgrounds §7 the
    /// blur applies a Gaussian with standard deviation <c>blur/2</c>; the coverage across the shape
    /// edge is that Gaussian's cumulative distribution. Shells run from the outer edge of the blur
    /// band (<c>~3σ</c> outside the shape, coverage ≈ 0) inward to its interior (coverage ≈ the
    /// shadow colour's own alpha), each painted at the incremental alpha needed to reach the target
    /// cumulative coverage. Still an approximation — a true separable Gaussian would soften the
    /// corners further — but far closer than a sharp rectangle.
    /// </summary>
    private static void EmitBlurredShadow(
        List<DisplayItem> items, Fragment fragment, RectangleF borderRect,
        in BoxShadowLayer s, RectangleF core, bool hasCornerRadius)
    {
        float sigma = Math.Max(0.01f, s.Blur * 0.5f);
        float extent = 3f * sigma;                              // band half-width (±3σ ≈ full falloff)
        int shells = Math.Clamp((int)MathF.Ceiling(extent), 4, 24);
        float peak = s.Color.A;                                 // cumulative coverage reached inside

        // Outer → inner: SrcOver accumulates toward `peak` at the interior. Shell k's boundary sits
        // `d` px into the shape from the core edge (d from -extent outside to +extent inside).
        float prevT = 0f;
        for (int k = 0; k < shells; k++)
        {
            float d = -extent + ((k + 1) / (float)(shells + 1)) * (2f * extent);
            float targetT = peak * NormalCdf(d / sigma);        // desired cumulative coverage here
            float denom = 255f - prevT;
            float aShell = denom > 0.001f ? (targetT - prevT) / denom : 0f;
            prevT = targetT;

            int alpha = (int)Math.Round(Math.Clamp(aShell, 0f, 1f) * 255f);
            if (alpha <= 0)
                continue;

            float inflate = -d;                                 // boundary d inside ⇒ shrink core by d
            var shellRect = InflateRect(core, inflate);
            var shellColor = BColor.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B);
            EmitShadowFill(items, fragment, borderRect, shellRect, s.Spread + inflate, shellColor, hasCornerRadius);
        }
    }

    /// <summary>Standard-normal CDF Φ(z), via a monotonic logistic approximation (max error ≈ 1%),
    /// used to shape the blurred-shadow coverage falloff.</summary>
    private static float NormalCdf(float z) => 1f / (1f + MathF.Exp(-1.702f * z));

    private static RectangleF InflateRect(RectangleF r, float by) =>
        new(r.X - by, r.Y - by, r.Width + (2f * by), r.Height + (2f * by));

    /// <summary>
    /// Emits one shadow rectangle fill of <paramref name="color"/>, clipped to the box's rounded
    /// corners (grown by <paramref name="cornerDelta"/>) when the element has a border radius.
    /// </summary>
    private static void EmitShadowFill(
        List<DisplayItem> items, Fragment fragment, RectangleF borderRect,
        RectangleF fillRect, float cornerDelta, BColor color, bool hasCornerRadius)
    {
        if (fillRect.Width <= 0 || fillRect.Height <= 0)
            return;

        if (hasCornerRadius)
        {
            var style = fragment.Style;
            items.Add(new ClipItem
            {
                Bounds = fillRect,
                ClipRect = fillRect,
                CornerNw = Math.Max(0.0, style.ActualCornerNw + cornerDelta),
                CornerNwY = Math.Max(0.0, GetEffectiveCornerRadiusY(style.CornerNwRadiusRaw, style.ActualCornerNw, borderRect) + cornerDelta),
                CornerNe = Math.Max(0.0, style.ActualCornerNe + cornerDelta),
                CornerNeY = Math.Max(0.0, GetEffectiveCornerRadiusY(style.CornerNeRadiusRaw, style.ActualCornerNe, borderRect) + cornerDelta),
                CornerSe = Math.Max(0.0, style.ActualCornerSe + cornerDelta),
                CornerSeY = Math.Max(0.0, GetEffectiveCornerRadiusY(style.CornerSeRadiusRaw, style.ActualCornerSe, borderRect) + cornerDelta),
                CornerSw = Math.Max(0.0, style.ActualCornerSw + cornerDelta),
                CornerSwY = Math.Max(0.0, GetEffectiveCornerRadiusY(style.CornerSwRadiusRaw, style.ActualCornerSw, borderRect) + cornerDelta),
            });
            items.Add(new FillRectItem { Bounds = fillRect, Color = color });
            items.Add(new RestoreItem { Bounds = fillRect });
        }
        else
        {
            items.Add(new FillRectItem { Bounds = fillRect, Color = color });
        }
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
                // Pass the box's effective CSS zoom so a view-box-less SVG's raw user-unit geometry scales
                // with the box (a view-boxed SVG is unaffected — its scale derives from the zoomed bounds).
                // 1.0 while the native-zoom engine is off, so this is inert by default.
                items.AddRange(SvgRenderer.RenderSvgContent(fragment.SvgContent, r, fragment.Style.EffectiveZoom));
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

            // CSS 2.1 §8.5.3: a bevelled border is not four flat sides.  `inset`/`outset` shade
            // two sides down and light the other two; `groove`/`ridge` carry the same two shades
            // but split each side lengthwise, so they paint as two nested rings.  Every other
            // style has an outer "half" that is the whole side and an empty inner one, which
            // leaves it emitting exactly the one item it always did.  The rule, and which half
            // gets which shade, is Broiler.Layout.Engine.BorderBevel.
            var outerWidths = new BoxEdges(
                Bevel.HalfWidth(style.BorderTopStyle, border.Top, outerHalf: true),
                Bevel.HalfWidth(style.BorderRightStyle, border.Right, outerHalf: true),
                Bevel.HalfWidth(style.BorderBottomStyle, border.Bottom, outerHalf: true),
                Bevel.HalfWidth(style.BorderLeftStyle, border.Left, outerHalf: true));

            for (int half = 0; half < 2; half++)
            {
                bool outerHalf = half == 0;
                var widths = outerHalf
                    ? outerWidths
                    : new BoxEdges(
                        Bevel.HalfWidth(style.BorderTopStyle, border.Top, outerHalf: false),
                        Bevel.HalfWidth(style.BorderRightStyle, border.Right, outerHalf: false),
                        Bevel.HalfWidth(style.BorderBottomStyle, border.Bottom, outerHalf: false),
                        Bevel.HalfWidth(style.BorderLeftStyle, border.Left, outerHalf: false));

                if (widths.Top <= 0 && widths.Right <= 0 && widths.Bottom <= 0 && widths.Left <= 0)
                    continue;

                // The inner ring sits inside the outer one, so its box is the border box pulled in
                // by the outer half and its corner radii shrink by the same amount.
                var bounds = outerHalf
                    ? rect
                    : new RectangleF(
                        rect.X + (float)outerWidths.Left,
                        rect.Y + (float)outerWidths.Top,
                        rect.Width - (float)(outerWidths.Left + outerWidths.Right),
                        rect.Height - (float)(outerWidths.Top + outerWidths.Bottom));

                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                double InnerRadius(double radius, double inset) =>
                    outerHalf ? radius : Math.Max(0, radius - inset);

                items.Add(new DrawBorderItem
                {
                    Bounds = bounds,
                    Widths = widths,
                    TopColor = hasTop
                        ? Bevel.HalfColor(style.BorderTopStyle, isTopOrLeft: true,
                            style.ActualBorderTopColor, border.Top, outerHalf)
                        : BColor.Empty,
                    RightColor = (hasRight && isLast)
                        ? Bevel.HalfColor(style.BorderRightStyle, isTopOrLeft: false,
                            style.ActualBorderRightColor, border.Right, outerHalf)
                        : BColor.Empty,
                    BottomColor = hasBottom
                        ? Bevel.HalfColor(style.BorderBottomStyle, isTopOrLeft: false,
                            style.ActualBorderBottomColor, border.Bottom, outerHalf)
                        : BColor.Empty,
                    LeftColor = (hasLeft && isFirst)
                        ? Bevel.HalfColor(style.BorderLeftStyle, isTopOrLeft: true,
                            style.ActualBorderLeftColor, border.Left, outerHalf)
                        : BColor.Empty,
                    // Style kept for Phase 1 backward compat; per-side styles are authoritative
                    Style = style.BorderTopStyle ?? "solid",
                    TopStyle = style.BorderTopStyle ?? "none",
                    RightStyle = (isLast) ? (style.BorderRightStyle ?? "none") : "none",
                    BottomStyle = style.BorderBottomStyle ?? "none",
                    LeftStyle = (isFirst) ? (style.BorderLeftStyle ?? "none") : "none",
                    CornerNw = InnerRadius(style.ActualCornerNw, outerWidths.Left),
                    CornerNe = InnerRadius(style.ActualCornerNe, outerWidths.Right),
                    CornerSe = InnerRadius(style.ActualCornerSe, outerWidths.Right),
                    CornerSw = InnerRadius(style.ActualCornerSw, outerWidths.Left),
                });
            }
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
