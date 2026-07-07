using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// CSS2.1 Appendix E stacking/painting order: fragment traversal and the
// background/foreground phase split. Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    private static void PaintFragment(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom = null, RectangleF viewport = default, bool isRoot = false, BColor? bgClipTextColor = null, bool asInlineContent = false)
    {
        var style = fragment.Style;

        // Skip invisible fragments
        if (style.Display == "none")
            return;
        if (style.Visibility != "visible")
        {
            // Even if not visible, children may be visible (CSS spec)
            PaintChildren(fragment, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
            return;
        }

        // CSS3: opacity: 0 means fully transparent — skip the entire
        // stacking context (element and all descendants).
        float fragmentOpacity = 1f;
        if (style.Opacity != null
            && float.TryParse(style.Opacity, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsedOpacity))
        {
            if (parsedOpacity <= 0.0f)
                return;
            fragmentOpacity = Math.Clamp(parsedOpacity, 0f, 1f);
        }

        var bounds = fragment.Bounds;

        // Skip empty-cells table cells
        if (style.Display == "table-cell" && style.EmptyCells == "hide")
        {
            bool hasContent = fragment.Lines != null && fragment.Lines.Count > 0;
            if (!hasContent && fragment.Children.Count == 0)
                return;
        }

        // CSS Transforms Level 1: apply 2D transform around the element's
        // transform-origin (default: center center).  The transform is
        // applied before opacity/blend-mode layers so that the compositing
        // group is in the transformed coordinate space.
        bool hasTransform = !isRoot
            && !string.IsNullOrEmpty(style.Transform)
            && !style.Transform.Equals("none", StringComparison.OrdinalIgnoreCase);
        if (hasTransform)
        {
            var matrix = ParseCssTransformMatrix(style.Transform, bounds);
            if (matrix != null)
            {
                float originX = bounds.X + bounds.Width * 0.5f;
                float originY = bounds.Y + bounds.Height * 0.5f;
                items.Add(new TransformItem
                {
                    Bounds = bounds,
                    Matrix = matrix,
                    OriginX = originX,
                    OriginY = originY,
                });
            }
            else
            {
                hasTransform = false;
            }
        }

        // CSS3: 0 < opacity < 1 creates a compositing group — all
        // descendant content is rendered into a separate layer, then
        // composited back at the specified opacity.
        // Note: fragmentOpacity is already clamped to [0,1] above.
        bool hasOpacity = fragmentOpacity < 1f;
        if (hasOpacity)
        {
            items.Add(new OpacityItem { Bounds = bounds, Opacity = fragmentOpacity });
        }

        // CSS Compositing §3: mix-blend-mode creates a compositing layer
        // that blends with the backdrop using the specified blend mode.
        // CSS Compositing §3.1: The root element's mix-blend-mode must not
        // be applied when compositing with the canvas (root uses normal).
        bool hasBlendMode = !isRoot
            && !string.IsNullOrEmpty(style.MixBlendMode)
            && !style.MixBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase);
        if (hasBlendMode)
        {
            items.Add(new BlendModeItem { Bounds = bounds, Mode = style.MixBlendMode });
        }

        // CSS Compositing §2.2: isolation: isolate creates an isolation group.
        // This prevents children's blend modes from bleeding through to the
        // element's backdrop.  A layer with SrcOver (normal) blending achieves
        // this.  Only needed when no explicit opacity or blend mode layer is
        // already being created (those layers implicitly create isolation).
        bool hasIsolation = !hasOpacity && !hasBlendMode
            && !string.IsNullOrEmpty(style.Isolation)
            && style.Isolation.Equals("isolate", StringComparison.OrdinalIgnoreCase);
        if (hasIsolation)
        {
            items.Add(new BlendModeItem { Bounds = bounds, Mode = "normal" });
        }

        // CSS Backgrounds Level 4: background-clip: text — detect and compute
        // the composite text color from the background colour for descendants.
        BColor? currentBgClipTextColor = bgClipTextColor;
        if (string.Equals(style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
        {
            var bgColor = style.ActualBackgroundColor;
            if (bgColor.A > 0)
                currentBgClipTextColor = bgColor;
        }

        bool clipPathClipped = TryCreateInsetClipPathItem(fragment, bounds, out var clipPathItem);
        if (clipPathClipped)
            items.Add(clipPathItem);

        // CSS2.1 §14.2: Background and borders are part of the element's
        // own rendering and are NOT clipped by the element's overflow.
        // They must be emitted before the overflow clip.
        // CSS Backgrounds Level 3 §3.3: When border-radius is set, the
        // entire border-box (background + borders) is clipped to the
        // rounded shape.
        // When this fragment is a display:inline box painted as inline content
        // (Step 5), its background and borders were already emitted by the
        // containing block's EmitInlineLevelBoxDecorations pass — BEFORE the
        // block's text — so they sit behind the line's glyphs (CSS2.1 App. E).
        // Re-emitting them here would paint over the text. See the helper.
        bool bgClippedRounded = false;
        if (!asInlineContent && !ReferenceEquals(fragment, propagatedFrom))
        {
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
        }

        // Borders (clipped to the rounded border-box when border-radius is set)
        if (!asInlineContent)
            EmitBorders(fragment, items);

        // Restore the rounded clip after borders are drawn
        if (bgClippedRounded)
            items.Add(new RestoreItem { Bounds = bounds });

        // Overflow clipping — CSS2.1 §11.1.1: clip at the padding edge of the box.
        // For static rendering, overflow:auto and overflow:scroll clip content
        // identically to overflow:hidden (no scrollbar UI).
        // Applied after background/borders so they remain visible.
        bool clipped = false;
        if (style.Overflow is "hidden" or "auto" or "scroll")
        {
            var border = fragment.Border;
            var clipRect = new RectangleF(
                bounds.X + (float)border.Left,
                bounds.Y + (float)border.Top,
                bounds.Width - (float)(border.Left + border.Right),
                bounds.Height - (float)(border.Top + border.Bottom));
            items.Add(new ClipItem { Bounds = bounds, ClipRect = clipRect });
            clipped = true;
        }

        // Replaced image (e.g. <img> elements)
        EmitReplacedImage(fragment, items);

        // CSS2.1 Appendix E: backgrounds/borders of in-flow display:inline
        // descendants paint behind this block's line text. Emit them before the
        // text; the Step-5 inline paint suppresses re-emission (asInlineContent).
        if (!asInlineContent)
            EmitInlineLevelBoxDecorations(fragment, items, viewport);

        // Selection highlights (before text so highlight is behind text)
        EmitSelection(fragment, items);

        // Text (inline fragments from line boxes)
        EmitText(fragment, items, currentBgClipTextColor);

        // Text decoration
        EmitTextDecoration(fragment, items, currentBgClipTextColor);

        // Child fragments (stacking-context sorted)
        PaintChildren(fragment, items, propagatedFrom, viewport, bgClipTextColor: currentBgClipTextColor);

        // Restore clip
        if (clipped)
            items.Add(new RestoreItem { Bounds = bounds });
        if (clipPathClipped)
            items.Add(new RestoreItem { Bounds = bounds });

        // CSS UI §2: the outline is painted just outside the border edge, over the
        // element's own content and in-flow descendants, and is not clipped by the
        // element's own overflow — so emit it after children and the clip restore.
        if (!asInlineContent)
            EmitOutline(fragment, items);

        // Restore blend mode layer (must come after clip restore, before opacity restore)
        if (hasBlendMode)
            items.Add(new RestoreBlendModeItem { Bounds = bounds });

        // Restore isolation layer
        if (hasIsolation)
            items.Add(new RestoreBlendModeItem { Bounds = bounds });

        // Restore opacity layer (must come after clip and blend mode restore)
        if (hasOpacity)
            items.Add(new RestoreOpacityItem { Bounds = bounds });

        // Restore transform layer (outermost layer)
        if (hasTransform)
            items.Add(new RestoreTransformItem { Bounds = bounds });
    }

    private static void PaintChildren(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom = null, RectangleF viewport = default, BColor? bgClipTextColor = null, bool skipBlockBackgrounds = false)
    {
        if (fragment.Children.Count == 0)
            return;

        // CSS2.1 §17.5.1: Table elements use a six-layer painting model.
        // Backgrounds are painted in order: table → col-groups → cols →
        // row-groups → rows → cells.  Column/column-group backgrounds must
        // be painted before row-group/row/cell backgrounds so they show
        // through transparent areas.
        if (fragment.Style.Display is "table" or "inline-table")
        {
            PaintTableChildren(fragment, items, propagatedFrom, viewport);
            return;
        }

        // CSS2.1 Appendix E: Within a stacking context, non-positioned
        // children are painted in three phases:
        //   Step 3: In-flow, non-inline-level, non-positioned descendants (blocks)
        //   Step 4: Non-positioned floats
        //   Step 5: In-flow, inline-level, non-positioned descendants
        // Positioned children (stacking contexts + position:relative) are painted
        // in steps 6–7, sorted by StackLevel.
        //
        // position:fixed with z-index:auto are painted BEFORE step-3 blocks
        // so that in-flow content covers them (CSS2.1 §9.9, §9.6.1).
        List<Fragment>? positioned = null;
        List<Fragment>? negativeZ = null;
        List<Fragment>? fixedNoZIndex = null;
        List<Fragment>? blocks = null;
        List<Fragment>? floats = null;
        List<Fragment>? inlineLevel = null;

        // Categorize children
        foreach (var child in fragment.Children)
        {
            // position:fixed with z-index:auto: paint before step-3 blocks
            // so that in-flow face content covers them.
            if (child.Style.Position == "fixed" && child.StackLevel == 0)
            {
                fixedNoZIndex ??= [];
                fixedNoZIndex.Add(child);
            }
            else if (child.CreatesStackingContext || child.Style.Position is "relative" or "absolute")
            {
                // CSS2.1 Appendix E: a positioned descendant with a NEGATIVE
                // stack level paints in Step 2 (beneath the in-flow content);
                // z-index ≥ 0 (and auto) paint in Steps 6–7 above it. Splitting
                // them here is what lets a `z-index:-1` overlay sit BEHIND the
                // page's in-flow content — a ubiquitous WPT reftest pattern
                // ("…passes if green, no red", where red is a z-index:-1 box the
                // correctly-placed content must cover) — instead of on top of it.
                if (child.StackLevel < 0)
                {
                    negativeZ ??= [];
                    negativeZ.Add(child);
                }
                else
                {
                    positioned ??= [];
                    positioned.Add(child);
                }
            }
            else if (child.Style.Float is "left" or "right")
            {
                floats ??= [];
                floats.Add(child);
            }
            else if (child.Style.Display is "inline" or "inline-block" or "inline-table")
            {
                inlineLevel ??= [];
                inlineLevel.Add(child);
            }
            else
            {
                blocks ??= [];
                blocks.Add(child);
            }
        }

        // Paint position:fixed (z-index:auto) first, beneath all other content
        if (fixedNoZIndex != null)
        {
            foreach (var child in fixedNoZIndex)
            {
                if (viewport.Width > 0 && viewport.Height > 0)
                {
                    int startIdx = items.Count;
                    PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
                    OffsetDisplayItems(items, startIdx, viewport.X, viewport.Y);
                }
                else
                {
                    PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
                }
            }
        }

        // Paints a positioned child, applying the fixed-position viewport
        // offset (CSS2.1 §9.6.1) when rendering a scrolled region. Shared by
        // Step 2 (negative z-index) and Steps 6–7 (z-index ≥ 0).
        void PaintPositionedChild(Fragment child)
        {
            if (child.Style.Position == "fixed" && viewport.Width > 0 && viewport.Height > 0)
            {
                int startIdx = items.Count;
                PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
                OffsetDisplayItems(items, startIdx, viewport.X, viewport.Y);
            }
            else
            {
                PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
            }
        }

        // Step 2: Positioned descendants with a negative z-index paint beneath
        // the in-flow content (CSS2.1 Appendix E), most-negative first.
        //
        // Only when this is the STANDALONE painter (skipBlockBackgrounds == false,
        // i.e. step-3 block backgrounds are painted below). In the two-phase path
        // (skipBlockBackgrounds == true) the negative-z elements were already
        // painted by the matching PaintChildrenBackgroundPhase call, beneath the
        // block descendant backgrounds — painting them again here would put them
        // back on top.
        if (negativeZ != null && !skipBlockBackgrounds)
        {
            negativeZ.Sort((a, b) => a.StackLevel.CompareTo(b.StackLevel));
            foreach (var child in negativeZ)
                PaintPositionedChild(child);
        }

        // CSS2.1 Appendix E three-phase painting:
        // Blocks are split across Steps 3 and 5 so that their inline content
        // (Step 5) paints above non-positioned floats (Step 4).

        // Step 3: Block backgrounds, background images, borders, and replaced
        //         content only — inline content (text, children) is deferred.
        // When called from PaintFragmentForegroundPhase, block backgrounds were
        // already painted by the parent's PaintChildrenBackgroundPhase — skip
        // them to avoid double-drawing semi-transparent backgrounds/borders.
        if (blocks != null && !skipBlockBackgrounds)
        {
            foreach (var child in blocks)
                PaintFragmentBackgroundPhase(child, items, propagatedFrom, viewport);
        }

        // Step 4: Paint non-positioned floats
        if (floats != null)
        {
            foreach (var child in floats)
                PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
        }

        // Step 5: Inline content from blocks (text, inline children) plus
        //         direct inline-level children.
        if (blocks != null)
        {
            foreach (var child in blocks)
                PaintFragmentForegroundPhase(child, items, propagatedFrom, viewport, bgClipTextColor);
        }
        if (inlineLevel != null)
        {
            foreach (var child in inlineLevel)
                // display:inline boxes had their background/border emitted before
                // the block's text (EmitInlineLevelBoxDecorations); suppress
                // re-emission here so they stay behind the glyphs. Atomic
                // inline-level boxes (inline-block/inline-table) own their own
                // text and paint their background normally.
                PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor,
                    asInlineContent: string.Equals(child.Style.Display, "inline", StringComparison.Ordinal));
        }

        // Steps 6–7: Positioned children (z-index ≥ 0) sorted by StackLevel,
        // painted above the in-flow content.
        if (positioned != null)
        {
            positioned.Sort((a, b) => a.StackLevel.CompareTo(b.StackLevel));
            foreach (var child in positioned)
                PaintPositionedChild(child);
        }
    }

    /// <summary>
    /// CSS2.1 Appendix E Step 3 — Background phase: paints only the
    /// background colour, background image, borders, and replaced-image of
    /// a block-level fragment. Text, selection, text-decoration, and child
    /// processing are deferred to <see cref="PaintFragmentForegroundPhase"/>.
    /// Block children are visited recursively so their backgrounds also
    /// appear before floats (Step 4).
    /// </summary>
    private static void PaintFragmentBackgroundPhase(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom, RectangleF viewport)
    {
        var style = fragment.Style;

        if (style.Display == "none")
            return;
        if (style.Visibility != "visible")
        {
            // Walk block children even when not visible (their visibility is independent)
            PaintChildrenBackgroundPhase(fragment, items, propagatedFrom, viewport);
            return;
        }

        var bounds = fragment.Bounds;

        // empty-cells check (table-cell)
        if (style.Display == "table-cell" && style.EmptyCells == "hide")
        {
            bool hasContent = fragment.Lines != null && fragment.Lines.Count > 0;
            if (!hasContent && fragment.Children.Count == 0)
                return;
        }

        bool clipPathClipped = TryCreateInsetClipPathItem(fragment, bounds, out var clipPathItem);
        if (clipPathClipped)
            items.Add(clipPathItem);

        // CSS2.1 §14.2/§11.1.1: Background, background image, and borders
        // are part of the element's own rendering and are NOT clipped by
        // the element's overflow.  Emit them before the overflow clip.
        // CSS Backgrounds Level 3 §3.3: rounded border-radius clips backgrounds and borders.
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
        }

        if (!ReferenceEquals(fragment, propagatedFrom))
        {
            EmitBackground(fragment, items);
            EmitBackgroundImage(fragment, items, viewport);
        }
        EmitBorders(fragment, items);

        if (hasCornerRadius)
            items.Add(new RestoreItem { Bounds = bounds });

        // Overflow clipping — paired with RestoreItem at the end.
        // Applied after background/borders so they remain visible.
        bool clipped = false;
        if (style.Overflow is "hidden" or "auto" or "scroll")
        {
            var border = fragment.Border;
            var clipRect = new RectangleF(
                bounds.X + (float)border.Left,
                bounds.Y + (float)border.Top,
                bounds.Width - (float)(border.Left + border.Right),
                bounds.Height - (float)(border.Top + border.Bottom));
            items.Add(new ClipItem { Bounds = bounds, ClipRect = clipRect });
            clipped = true;
        }

        EmitReplacedImage(fragment, items);

        // Recursively visit block children for their backgrounds
        PaintChildrenBackgroundPhase(fragment, items, propagatedFrom, viewport);

        if (clipped)
            items.Add(new RestoreItem { Bounds = bounds });
        if (clipPathClipped)
            items.Add(new RestoreItem { Bounds = bounds });
    }

    /// <summary>
    /// Walks only the block-level, non-float, non-positioned children of
    /// <paramref name="fragment"/> and paints their backgrounds via
    /// <see cref="PaintFragmentBackgroundPhase"/>.  Float, inline-level,
    /// positioned, and table children are skipped (handled in later steps).
    /// </summary>
    private static void PaintChildrenBackgroundPhase(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom, RectangleF viewport)
    {
        // CSS2.1 Appendix E Step 2: positioned descendants with a NEGATIVE
        // z-index paint here in the background phase — after this stacking
        // context's own background (already emitted by the caller) but BEFORE
        // any in-flow block descendant backgrounds below — so a `z-index:-1`
        // overlay sits behind the page content. They are painted as full
        // stacking contexts (most-negative first); the matching foreground
        // PaintChildren(skipBlockBackgrounds:true) call skips them.
        List<Fragment>? negativeZ = null;
        foreach (var child in fragment.Children)
        {
            if (child.StackLevel < 0
                && (child.CreatesStackingContext || child.Style.Position is "relative" or "absolute" or "fixed"))
            {
                negativeZ ??= [];
                negativeZ.Add(child);
            }
        }
        if (negativeZ != null)
        {
            negativeZ.Sort((a, b) => a.StackLevel.CompareTo(b.StackLevel));
            foreach (var child in negativeZ)
            {
                if (child.Style.Position == "fixed" && viewport.Width > 0 && viewport.Height > 0)
                {
                    int startIdx = items.Count;
                    PaintFragment(child, items, propagatedFrom, viewport);
                    OffsetDisplayItems(items, startIdx, viewport.X, viewport.Y);
                }
                else
                {
                    PaintFragment(child, items, propagatedFrom, viewport);
                }
            }
        }

        foreach (var child in fragment.Children)
        {
            // Skip fixed / positioned / stacking-context — handled in Steps 0, 6–7
            if (child.Style.Position is "fixed" or "absolute" || child.CreatesStackingContext || child.Style.Position == "relative")
                continue;
            // Skip floats — handled in Step 4
            if (child.Style.Float is "left" or "right")
                continue;
            // Skip inline-level — handled in Step 5
            if (child.Style.Display is "inline" or "inline-block" or "inline-table")
                continue;
            // Skip tables — they use their own six-layer model
            if (child.Style.Display is "table" or "inline-table")
                continue;

            PaintFragmentBackgroundPhase(child, items, propagatedFrom, viewport);
        }
    }

    /// <summary>
    /// CSS2.1 Appendix E Step 5 — Foreground phase: paints the text,
    /// selection, and text-decoration of a block-level fragment, then
    /// processes its children via <see cref="PaintChildren"/> which applies
    /// the three-phase split recursively.
    /// </summary>
    private static void PaintFragmentForegroundPhase(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom, RectangleF viewport, BColor? bgClipTextColor = null)
    {
        var style = fragment.Style;

        if (style.Display == "none")
            return;
        if (style.Visibility != "visible")
            return;

        // Detect background-clip: text on this fragment (propagate to children)
        BColor? currentBgClipTextColor = bgClipTextColor;
        if (string.Equals(style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
        {
            var bgColor = style.ActualBackgroundColor;
            if (bgColor.A > 0)
                currentBgClipTextColor = bgColor;
        }

        var bounds = fragment.Bounds;

        bool clipPathClipped = TryCreateInsetClipPathItem(fragment, bounds, out var clipPathItem);
        if (clipPathClipped)
            items.Add(clipPathItem);

        // Overflow clipping — paired with RestoreItem at the end
        bool clipped = false;
        if (style.Overflow is "hidden" or "auto" or "scroll")
        {
            var border = fragment.Border;
            var clipRect = new RectangleF(
                bounds.X + (float)border.Left,
                bounds.Y + (float)border.Top,
                bounds.Width - (float)(border.Left + border.Right),
                bounds.Height - (float)(border.Top + border.Bottom));
            items.Add(new ClipItem { Bounds = bounds, ClipRect = clipRect });
            clipped = true;
        }

        // Foreground content: selection, text, text-decoration.
        // CSS2.1 Appendix E: in-flow display:inline descendants' backgrounds/
        // borders paint behind this block's text — emit them first (Step-5 inline
        // paint suppresses re-emission via asInlineContent).
        EmitInlineLevelBoxDecorations(fragment, items, viewport);
        EmitSelection(fragment, items);
        EmitText(fragment, items, currentBgClipTextColor);
        EmitTextDecoration(fragment, items, currentBgClipTextColor);

        // Process children using the standard Appendix E ordering.
        // Skip Step 3 (block backgrounds) because those were already painted
        // by the corresponding PaintChildrenBackgroundPhase call.
        PaintChildren(fragment, items, propagatedFrom, viewport, bgClipTextColor: currentBgClipTextColor, skipBlockBackgrounds: true);

        if (clipped)
            items.Add(new RestoreItem { Bounds = bounds });
        if (clipPathClipped)
            items.Add(new RestoreItem { Bounds = bounds });

        // CSS UI §2: the outline paints just outside the border edge, over the
        // element's content and in-flow descendants, and is not clipped by the
        // element's own overflow — emitted after children and the clip restore.
        EmitOutline(fragment, items);
    }

    /// <summary>
    /// CSS2.1 §17.5.1: Paints table children in the six-layer order.
    /// Layer 1 (table background) is already painted by <see cref="PaintFragment"/>
    /// via <see cref="EmitBackground"/> before calling <see cref="PaintChildren"/>.
    /// Layers 2–3: column-group and column backgrounds.
    /// Layers 4–6: row-group, row, and cell backgrounds (tree order).
    /// </summary>
    private static void PaintTableChildren(Fragment table, List<DisplayItem> items, Fragment? propagatedFrom, RectangleF viewport = default)
    {
        // Collect column/column-group fragments whose backgrounds will be
        // emitted early (layers 2–3) so PaintFragment can skip them later.
        HashSet<Fragment>? earlyBgFragments = null;

        // Layer 2–3: Paint column-group and column backgrounds first
        foreach (var child in table.Children)
        {
            if (child.Style.Display == "table-column-group")
            {
                EmitBackground(child, items);
                earlyBgFragments ??= new HashSet<Fragment>(ReferenceEqualityComparer.Instance);
                earlyBgFragments.Add(child);
                foreach (var col in child.Children)
                {
                    if (col.Style.Display == "table-column")
                    {
                        EmitBackground(col, items);
                        earlyBgFragments.Add(col);
                    }
                }
            }
            else if (child.Style.Display == "table-column")
            {
                EmitBackground(child, items);
                earlyBgFragments ??= new HashSet<Fragment>(ReferenceEqualityComparer.Instance);
                earlyBgFragments.Add(child);
            }
        }

        // Layers 4–6: Paint all children in tree order.  Column/column-group
        // fragments whose backgrounds were already emitted above are passed
        // through so PaintFragment skips their EmitBackground call.
        List<Fragment>? positioned = null;
        foreach (var child in table.Children)
        {
            if (child.CreatesStackingContext)
            {
                positioned ??= [];
                positioned.Add(child);
            }
            else
            {
                var skipBg = propagatedFrom ?? (earlyBgFragments != null && earlyBgFragments.Contains(child) ? child : null);
                PaintFragment(child, items, skipBg, viewport);
            }
        }

        if (positioned != null)
        {
            positioned.Sort((a, b) => a.StackLevel.CompareTo(b.StackLevel));
            foreach (var child in positioned)
            {
                var skipBg = propagatedFrom ?? (earlyBgFragments != null && earlyBgFragments.Contains(child) ? child : null);
                PaintFragment(child, items, skipBg, viewport);
            }
        }
    }
}
