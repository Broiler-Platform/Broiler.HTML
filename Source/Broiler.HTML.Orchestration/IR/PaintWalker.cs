using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.HTML.Adapters;
using Broiler.HTML.Core.IR;
using Broiler.HTML.CSS.Parse;

namespace Broiler.HTML.Orchestration.Core.IR;

/// <summary>
/// Walks a <see cref="Fragment"/> tree and produces a flat <see cref="DisplayList"/>
/// of drawing primitives. This decouples paint from the DOM (<see cref="Dom.CssBox"/>).
/// </summary>
internal static class PaintWalker
{
    // Match PaintWalker.ParseFontSize's existing medium/default font-size fallback.
    private const float DefaultFontSize = 12f;
    // Broiler's default normal line-height is 1.2 × the computed font size.
    private const float DefaultLineHeightMultiplier = 1.2f;

    /// <summary>Default selection highlight color (semi-transparent blue).</summary>
    private static readonly Color SelectionHighlightColor = Color.FromArgb(0x69, 0x33, 0x99, 0xFF);

    /// <summary>
    /// Sentinel value indicating that a selection offset is not constrained
    /// (i.e. the entire inline is selected on that side). Matches the convention
    /// used by <c>CssRect.SelectedStartOffset</c> / <c>SelectedEndOffset</c>.
    /// </summary>
    private const double FullSelectionOffset = -1;

    /// <summary>
    /// Paints the given <see cref="Fragment"/> tree and returns a flat <see cref="DisplayList"/>.
    /// When <paramref name="viewport"/> is non-empty, CSS2.1&nbsp;§14.2 canvas background
    /// propagation is applied: the root element's background fills the entire viewport.
    /// </summary>
    public static DisplayList Paint(Fragment root, RectangleF viewport)
    {
        var items = new List<DisplayItem>();

        // CSS2.1 §14.2: Propagate root/body background to the canvas.
        // The element whose background was propagated must NOT paint its own
        // background at its box position (the spec says "must not paint a
        // background for that child element").
        Fragment? propagatedFrom = null;
        if (viewport.Width > 0 && viewport.Height > 0)
            propagatedFrom = EmitCanvasBackground(root, viewport, items);

        PaintFragment(root, items, propagatedFrom, viewport, isRoot: true);
        return new DisplayList { Items = items };
    }

    /// <summary>
    /// CSS2.1 §14.2: The background of the root element becomes the background of the canvas.
    /// If the root element has a transparent background AND no background-image,
    /// the body element's background is used instead.  Color and image are propagated
    /// as a unit from the same cascade step.
    /// Returns the fragment whose background was propagated (so it can be suppressed during
    /// normal painting), or <c>null</c> if no propagation occurred.
    /// </summary>
    private static Fragment? EmitCanvasBackground(Fragment root, RectangleF viewport, List<DisplayItem> items)
    {
        // Find which element supplies the canvas background (color + image
        // as a unit, per CSS2.1 §14.2).
        var (canvasBg, colorSource, imgSource) = FindCanvasBackgroundAndImage(root);

        // Also check for CSS gradient layers in background-image.
        var gradientSource = imgSource ?? colorSource ?? FindGradientSource(root);

        // Determine root opacity for canvas compositing.
        // CSS Compositing §2.11: the root element's opacity applies to
        // the canvas background (both color and image).
        var htmlFragment = colorSource ?? imgSource ?? gradientSource ?? FindFirstBlockChild(root) ?? FindFirstVisibleChild(root);
        float rootOpacity = 1f;
        if (htmlFragment != null
            && htmlFragment.Style.Opacity != null
            && float.TryParse(htmlFragment.Style.Opacity,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var op))
        {
            rootOpacity = Math.Clamp(op, 0f, 1f);
        }

        if (canvasBg.A > 0)
        {
            Color finalBg = CompositOverWhite(canvasBg, rootOpacity);

            // CSS Filter Effects §8: When the root element has filter: invert(N),
            // the filter applies to the canvas background.
            finalBg = ApplyRootFilter(htmlFragment, finalBg);

            items.Add(new FillRectItem { Bounds = viewport, Color = finalBg });
        }

        // CSS3 Backgrounds: Handle multiple gradient background layers.
        // These are stored as comma-separated gradient functions in background-image.
        if (gradientSource != null && HasGradientBackgroundImage(gradientSource.Style.BackgroundImage))
        {
            bool needsOpacity = rootOpacity < 1f;
            if (needsOpacity)
                items.Add(new OpacityItem { Bounds = viewport, Opacity = rootOpacity });

            var gradientClipRect = GetBackgroundClipRect(
                gradientSource.Bounds,
                gradientSource,
                gradientSource.Style.BackgroundClip);
            EmitGradientLayers(gradientSource, gradientClipRect, viewport, items);

            if (needsOpacity)
                items.Add(new RestoreOpacityItem { Bounds = viewport });

            return gradientSource;
        }

        // CSS2.1 §14.2: The root element's background-image is also
        // propagated to the canvas.  The image source comes from the
        // same cascade step as the color (root → html → body as a unit).
        if (imgSource != null && imgSource.BackgroundImageHandle != null)
        {
            // When the root has opacity < 1, wrap the background image
            // in an opacity layer so it composites correctly over the
            // canvas (white backdrop).
            bool needsOpacity = rootOpacity < 1f;
            if (needsOpacity)
                items.Add(new OpacityItem { Bounds = viewport, Opacity = rootOpacity });

            var repeat = imgSource.Style.BackgroundRepeat;
            var posStr = imgSource.Style.BackgroundPosition;

            var tileOrigin = new PointF(viewport.X, viewport.Y);

            // Apply background-position offset.
            if (!string.IsNullOrEmpty(posStr))
            {
                float imgW = 0, imgH = 0;
                if (imgSource.BackgroundImageHandle is RImage bgImg)
                {
                    imgW = (float)bgImg.Width;
                    imgH = (float)bgImg.Height;
                }

                var parts = posStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string xVal = null, yVal = null;
                foreach (var p in parts)
                {
                    if (IsHorizontalKeyword(p))
                        xVal = p;
                    else if (IsVerticalKeyword(p))
                        yVal = p;
                    else if (p.Equals("center", StringComparison.OrdinalIgnoreCase))
                    {
                        if (xVal == null) xVal = p;
                        else if (yVal == null) yVal = p;
                    }
                    else
                    {
                        if (xVal == null) xVal = p;
                        else if (yVal == null) yVal = p;
                    }
                }

                float emSize = GetPositionEmSize(imgSource.Style);
                tileOrigin.X += ParsePositionValue(xVal, viewport.Width, imgW, emSize);
                tileOrigin.Y += ParsePositionValue(yVal, viewport.Height, imgH, emSize);
            }

            items.Add(new DrawTiledImageItem
            {
                Bounds = viewport,
                ImageHandle = imgSource.BackgroundImageHandle,
                SourceRect = RectangleF.Empty,
                FillRect = viewport,
                PositioningArea = viewport,
                TileOrigin = tileOrigin,
                Repeat = repeat,
            });

            if (needsOpacity)
                items.Add(new RestoreOpacityItem { Bounds = viewport });

            // Return the image source fragment so its background image
            // is not painted again at the element's box position.
            return imgSource;
        }

        return colorSource;
    }

    /// <summary>
    /// Finds the canvas background color AND image source per CSS2.1 §14.2.
    /// The cascade is: root → html → body, but body is only considered when
    /// html has BOTH transparent background-color AND no background-image.
    /// <para>
    /// CSS Backgrounds §2.11.1 and CSS Containment §4.2: propagation is
    /// suppressed when the source element has <c>display: none</c>,
    /// <c>display: contents</c>, or <c>contain: paint</c>.
    /// </para>
    /// Returns (color, colorSource, imageSource).
    /// </summary>
    private static (Color Color, Fragment? ColorSource, Fragment? ImageSource) FindCanvasBackgroundAndImage(Fragment root)
    {
        // Check root itself.
        if (root.Style.ActualBackgroundColor.A > 0 || root.BackgroundImageHandle != null)
        {
            // CSS Backgrounds §2.11.1: if the root element has display:none
            // its background must not propagate to the canvas.
            if (SuppressesPropagation(root))
                return (Color.Empty, null, null);

            return (root.Style.ActualBackgroundColor, root,
                root.BackgroundImageHandle != null ? root : null);
        }

        // Step 1: html element — prefer TagName-based lookup, then
        // fall back to structural heuristics for trees without tag info.
        Fragment? html = FindFragmentByTag(root, "html")
            ?? FindFirstBlockChild(root) ?? FindFirstVisibleChild(root);
        if (html == null)
            return (Color.Empty, null, null);

        // CSS Containment §4.2: contain:paint on the html element prevents
        // propagation from body.
        bool htmlSuppressed = SuppressesPropagation(html);

        bool htmlHasBg = html.Style.ActualBackgroundColor.A > 0;
        bool htmlHasImg = html.BackgroundImageHandle != null;

        if (htmlHasBg || htmlHasImg)
        {
            if (htmlSuppressed)
                return (Color.Empty, null, null);

            return (html.Style.ActualBackgroundColor,
                htmlHasBg ? html : null,
                htmlHasImg ? html : null);
        }

        // Step 2: html has no background at all → fall back to body.
        // But if html has contain:paint, propagation from body is blocked.
        if (htmlSuppressed)
            return (Color.Empty, null, null);

        // When body has display:inline, anonymous block wrappers may
        // intervene between the html fragment and the body fragment.
        // Use a recursive tag-based search (depth-limited) to locate
        // the body regardless of box-tree restructuring.
        Fragment? body = FindFragmentByTag(html, "body")
            ?? FindFirstBlockChild(html) ?? FindFirstVisibleChild(html);
        if (body == null)
            return (Color.Empty, null, null);

        // CSS Containment §4.2 / CSS Backgrounds §2.11.1: body with
        // contain:paint or display:contents/none does not propagate.
        if (SuppressesPropagation(body))
            return (Color.Empty, null, null);

        bool bodyHasBg = body.Style.ActualBackgroundColor.A > 0;
        bool bodyHasImg = body.BackgroundImageHandle != null;

        if (bodyHasBg || bodyHasImg)
        {
            return (body.Style.ActualBackgroundColor,
                bodyHasBg ? body : null,
                bodyHasImg ? body : null);
        }

        return (Color.Empty, null, null);
    }

    /// <summary>
    /// Searches for a fragment with the given HTML tag name among direct
    /// children and, if not found, recursively through anonymous wrapper
    /// boxes (up to 3 levels deep).  This handles cases where CSS anonymous
    /// block boxing wraps the target element (e.g. body with display:inline
    /// containing block-level children).
    /// </summary>
    private static Fragment? FindFragmentByTag(Fragment parent, string tagName, int depth = 0)
    {
        if (depth > 3) return null;

        foreach (var child in parent.Children)
        {
            if (string.Equals(child.Style.TagName, tagName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        // Recurse into anonymous wrappers (no tag name) to find the target.
        foreach (var child in parent.Children)
        {
            if (child.Style.TagName == null && child.Style.Display != "none")
            {
                var found = FindFragmentByTag(child, tagName, depth + 1);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given fragment's CSS properties
    /// suppress background propagation to the canvas.  Per the CSS
    /// Backgrounds and CSS Containment specifications, propagation is
    /// suppressed when:
    /// <list type="bullet">
    ///   <item><c>display: none</c> — the element generates no box.</item>
    ///   <item><c>display: contents</c> — the element generates no principal box.</item>
    ///   <item><c>contain: paint</c> (or a shorthand that includes paint,
    ///         e.g. <c>strict</c>, <c>content</c>) — paint containment
    ///         prevents the background from propagating.</item>
    /// </list>
    /// </summary>
    private static bool SuppressesPropagation(Fragment fragment)
    {
        var display = fragment.Style.Display;
        if (display is "none" or "contents")
            return true;

        var contain = fragment.Style.Contain;
        if (!string.IsNullOrEmpty(contain) && contain != "none")
        {
            // contain: strict = size layout style paint
            // contain: content = layout style paint
            if (contain.Contains("paint", StringComparison.OrdinalIgnoreCase)
                || contain.Equals("strict", StringComparison.OrdinalIgnoreCase)
                || contain.Equals("content", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Composites a foreground color over a white backdrop, applying an
    /// additional opacity factor.  Uses the "source over" Porter-Duff model.
    /// </summary>
    private static Color CompositOverWhite(Color fg, float opacity)
    {
        float a = (fg.A / 255f) * opacity;
        if (a >= 1f) return fg;
        if (a <= 0f) return Color.White;

        int r = (int)Math.Round(fg.R * a + 255 * (1 - a));
        int g = (int)Math.Round(fg.G * a + 255 * (1 - a));
        int b = (int)Math.Round(fg.B * a + 255 * (1 - a));

        return Color.FromArgb(255,
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(b, 0, 255));
    }

    /// <summary>
    /// Applies CSS filter functions on the root/html fragment to the canvas background color.
    /// Supports <c>invert()</c> and <c>sepia()</c>; other filter functions are silently ignored.
    /// </summary>
    private static Color ApplyRootFilter(Fragment? htmlFragment, Color color)
    {
        if (htmlFragment == null) return color;

        string? filter = htmlFragment.Style.Filter;
        if (string.IsNullOrEmpty(filter) || filter == "none") return color;

        // Parse invert(N) where N is 0..1 (or 0%..100%)
        var invertMatch = System.Text.RegularExpressions.Regex.Match(filter, @"invert\(\s*([^)]+)\s*\)");
        if (invertMatch.Success)
        {
            string valStr = invertMatch.Groups[1].Value.Trim();
            float amount = 1f;
            if (valStr.EndsWith("%"))
            {
                if (float.TryParse(valStr.AsSpan(0, valStr.Length - 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    amount = pct / 100f;
            }
            else if (float.TryParse(valStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                amount = val;
            }
            amount = Math.Clamp(amount, 0f, 1f);

            // invert(amount): result = value × (1 - amount) + (255 - value) × amount
            int r = (int)Math.Round(color.R * (1 - amount) + (255 - color.R) * amount);
            int g = (int)Math.Round(color.G * (1 - amount) + (255 - color.G) * amount);
            int b = (int)Math.Round(color.B * (1 - amount) + (255 - color.B) * amount);

            return Color.FromArgb(color.A,
                Math.Clamp(r, 0, 255),
                Math.Clamp(g, 0, 255),
                Math.Clamp(b, 0, 255));
        }

        // Parse sepia(N) where N is 0..1 (or 0%..100%)
        // sepia transform matrix (CSS Filter Effects Module Level 1):
        //   R' = min(255, 0.393R + 0.769G + 0.189B)
        //   G' = min(255, 0.349R + 0.686G + 0.168B)
        //   B' = min(255, 0.272R + 0.534G + 0.131B)
        var sepiaMatch = System.Text.RegularExpressions.Regex.Match(filter, @"sepia\(\s*([^)]+)\s*\)");
        if (sepiaMatch.Success)
        {
            string valStr = sepiaMatch.Groups[1].Value.Trim();
            float amount = 1f;
            if (valStr.EndsWith("%"))
            {
                if (float.TryParse(valStr.AsSpan(0, valStr.Length - 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    amount = pct / 100f;
            }
            else if (float.TryParse(valStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                amount = val;
            }
            amount = Math.Clamp(amount, 0f, 1f);

            // Sepia matrix applied with interpolation: result = original × (1-amount) + sepia × amount
            double sr = 0.393 * color.R + 0.769 * color.G + 0.189 * color.B;
            double sg = 0.349 * color.R + 0.686 * color.G + 0.168 * color.B;
            double sb = 0.272 * color.R + 0.534 * color.G + 0.131 * color.B;

            int r = (int)Math.Round(color.R * (1 - amount) + Math.Min(255, sr) * amount);
            int g = (int)Math.Round(color.G * (1 - amount) + Math.Min(255, sg) * amount);
            int b = (int)Math.Round(color.B * (1 - amount) + Math.Min(255, sb) * amount);

            return Color.FromArgb(color.A,
                Math.Clamp(r, 0, 255),
                Math.Clamp(g, 0, 255),
                Math.Clamp(b, 0, 255));
        }

        return color;
    }

    /// <summary>
    /// Returns the first child fragment that is a visible block-level element,
    /// skipping <c>display:none</c> children (e.g. <c>&lt;head&gt;</c>).
    /// </summary>
    private static Fragment? FindFirstBlockChild(Fragment parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Style.Display == "none")
                continue;
            if (child.Style.Display is "block" or "list-item" or "table")
                return child;
        }
        return null;
    }

    /// <summary>
    /// Returns the first visible child fragment regardless of display type.
    /// Used as a fallback when the HTML parser doesn't generate block-level
    /// wrapper elements (<c>&lt;html&gt;</c> / <c>&lt;body&gt;</c>).
    /// </summary>
    private static Fragment? FindFirstVisibleChild(Fragment parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Style.Display == "none")
                continue;
            return child;
        }
        return null;
    }

    private static void PaintFragment(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom = null, RectangleF viewport = default, bool isRoot = false, Color? bgClipTextColor = null)
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
            && float.TryParse(style.Opacity, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedOpacity))
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
        Color? currentBgClipTextColor = bgClipTextColor;
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
        bool bgClippedRounded = false;
        if (!ReferenceEquals(fragment, propagatedFrom))
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

    private static void EmitBackground(Fragment fragment, List<DisplayItem> items)
    {
        var style = fragment.Style;

        // CSS Backgrounds Level 4: background-clip:text — the background colour
        // is not painted normally; it is applied through text shapes instead.
        if (string.Equals(style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
            return;

        // Determine the set of rectangles to paint: per-line rects for inline elements,
        // or the single fragment bounds for block elements.
        var rects = GetPaintRects(fragment);

        foreach (var rect in rects)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            // CSS Backgrounds §2.11.4: background-clip determines the painting area.
            // Default is border-box; padding-box clips to inside borders;
            // content-box clips to inside padding.
            var effectiveBackgroundClip = GetEffectiveBackgroundClip(fragment, style.BackgroundClip);
            var fillRect = GetBackgroundClipRect(rect, fragment, effectiveBackgroundClip);
            if (fillRect.Width <= 0 || fillRect.Height <= 0)
                continue;

            Color bgColor;
            // Background gradient
            if (style.ActualBackgroundGradient.A > 0 &&
                style.ActualBackgroundGradient != style.ActualBackgroundColor)
            {
                bgColor = style.ActualBackgroundColor.A > 0
                    ? style.ActualBackgroundColor
                    : style.ActualBackgroundGradient;
            }
            else if (style.ActualBackgroundColor.A > 0)
            {
                bgColor = style.ActualBackgroundColor;
            }
            else
            {
                continue;
            }

            // CSS Backgrounds Level 4: background-clip: border-area — paint
            // the background colour only within the border area (4 strips).
            bool hasRoundedClip = TryCreateRoundedBackgroundClipItem(rect, fragment, effectiveBackgroundClip, out var roundedClip);
            if (hasRoundedClip)
                items.Add(roundedClip);

            if (effectiveBackgroundClip.Equals("border-area", StringComparison.OrdinalIgnoreCase))
            {
                EmitBorderAreaBorder(rect, fragment, items, bgColor);
            }
            else
            {
                items.Add(new FillRectItem { Bounds = fillRect, Color = bgColor });
            }

            if (hasRoundedClip)
                items.Add(new RestoreItem { Bounds = fillRect });
        }
    }

    /// <summary>
    /// CSS Backgrounds Level 4 <c>background-clip: border-area</c>:
    /// Emits a border-shaped fill using the same per-side styles as normal
    /// border painting so <c>hidden</c>, <c>dashed</c>, <c>dotted</c>, etc.
    /// match the corresponding WPT reference rendering.
    /// </summary>
    private static void EmitBorderAreaBorder(RectangleF bounds, Fragment fragment, List<DisplayItem> items, Color color)
    {
        var style = fragment.Style;
        var border = fragment.Border;
        bool hasTop = HasBorder(style.BorderTopStyle, border.Top);
        bool hasRight = HasBorder(style.BorderRightStyle, border.Right);
        bool hasBottom = HasBorder(style.BorderBottomStyle, border.Bottom);
        bool hasLeft = HasBorder(style.BorderLeftStyle, border.Left);

        if (!hasTop && !hasRight && !hasBottom && !hasLeft)
            return;

        items.Add(new DrawBorderItem
        {
            Bounds = bounds,
            Widths = border,
            TopColor = hasTop ? color : Color.Empty,
            RightColor = hasRight ? color : Color.Empty,
            BottomColor = hasBottom ? color : Color.Empty,
            LeftColor = hasLeft ? color : Color.Empty,
            Style = style.BorderTopStyle ?? "solid",
            TopStyle = style.BorderTopStyle ?? "none",
            RightStyle = style.BorderRightStyle ?? "none",
            BottomStyle = style.BorderBottomStyle ?? "none",
            LeftStyle = style.BorderLeftStyle ?? "none",
            CornerNw = style.ActualCornerNw,
            CornerNe = style.ActualCornerNe,
            CornerSe = style.ActualCornerSe,
            CornerSw = style.ActualCornerSw,
        });
    }

    private static void EmitBackgroundImage(Fragment fragment, List<DisplayItem> items, RectangleF viewport = default)
    {
        // CSS Backgrounds Level 4: background-clip:text — background image
        // is not painted normally (it is clipped to text shapes).
        if (string.Equals(fragment.Style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
            return;

        // CSS3: handle gradient background layers even without a url-based image.
        if (fragment.BackgroundImageHandle == null)
        {
            if (HasGradientBackgroundImage(fragment.Style.BackgroundImage))
            {
                EmitElementGradientLayers(fragment, items, viewport);
            }
            return;
        }

        var backgroundLayers = SplitOnTopLevelCommas(fragment.Style.BackgroundImage ?? "none");
        if (backgroundLayers.Count == 0)
            backgroundLayers.Add("none");

        var repeats = SplitOnTopLevelCommas(fragment.Style.BackgroundRepeat ?? "repeat");
        var attachments = SplitOnTopLevelCommas(fragment.Style.BackgroundAttachment ?? "scroll");
        var positions = SplitOnTopLevelCommas(fragment.Style.BackgroundPosition ?? "0% 0%");
        var sizes = SplitOnTopLevelCommas(fragment.Style.BackgroundSize ?? "auto");
        var origins = SplitOnTopLevelCommas(fragment.Style.BackgroundOrigin ?? "padding-box");
        var clips = SplitOnTopLevelCommas(fragment.Style.BackgroundClip ?? "border-box");
        var layerHandles = NormalizeBackgroundImageHandles(fragment.BackgroundImageHandle, backgroundLayers.Count);

        // Use GetPaintRects to handle inline elements (which may have zero
        // Size but non-empty InlineRects from per-line-box layout).
        var rects = GetPaintRects(fragment);

        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            for (int i = backgroundLayers.Count - 1; i >= 0; i--)
            {
                var layerValue = backgroundLayers[i].Trim();
                if (string.IsNullOrEmpty(layerValue)
                    || layerValue.Equals("none", StringComparison.OrdinalIgnoreCase)
                    || layerValue.Contains("gradient(", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EmitBackgroundImageLayer(
                    fragment,
                    bounds,
                    layerHandles[i],
                    repeats.Count > 0 ? repeats[i % repeats.Count].Trim() : "repeat",
                    attachments.Count > 0 ? attachments[i % attachments.Count].Trim() : "scroll",
                    positions.Count > 0 ? positions[i % positions.Count].Trim() : "0% 0%",
                    sizes.Count > 0 ? sizes[i % sizes.Count].Trim() : "auto",
                    origins.Count > 0 ? origins[i % origins.Count].Trim() : "padding-box",
                    clips.Count > 0 ? clips[i % clips.Count].Trim() : "border-box",
                    items,
                    viewport);
            }
        }
    }

    private static void EmitBackgroundImageLayer(
        Fragment fragment,
        RectangleF bounds,
        object? imageHandle,
        string repeat,
        string attachment,
        string position,
        string size,
        string origin,
        string clip,
        List<DisplayItem> items,
        RectangleF viewport)
    {
        if (imageHandle is not RImage image)
            return;

        var effectiveBackgroundClip = GetEffectiveBackgroundClip(fragment, clip);
        var clipRect = GetBackgroundClipRect(bounds, fragment, effectiveBackgroundClip);
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return;

        var originRect = GetBackgroundPositioningAreaRect(bounds, fragment, origin);
        bool isFixed = attachment == "fixed"
            && viewport.Width > 0
            && viewport.Height > 0
            && !fragment.HasTransformAncestor;
        bool isLocal = attachment == "local";
        var positioningArea = isFixed
            ? viewport
            : isLocal
                ? GetLocalBackgroundPositioningAreaRect(bounds, fragment, originRect)
                : originRect;
        bool hasRoundedClip = TryCreateRoundedBackgroundClipItem(bounds, fragment, effectiveBackgroundClip, out var roundedClip);

        float tileW = 0, tileH = 0;
        ParseBackgroundSizeForImage(
            size,
            positioningArea.Width,
            positioningArea.Height,
            (float)image.IntrinsicWidth,
            (float)image.IntrinsicHeight,
            image.HasIntrinsicRatio,
            (float)image.IntrinsicAspectRatio,
            image.HasIntrinsicWidth,
            image.HasIntrinsicHeight,
            out tileW,
            out tileH);

        var tileOrigin = new PointF(positioningArea.X, positioningArea.Y);
        ApplyBackgroundPositionOffset(ref tileOrigin, position, positioningArea.Width, positioningArea.Height, tileW > 0 ? tileW : (float)image.Width, tileH > 0 ? tileH : (float)image.Height, GetPositionEmSize(fragment.Style));

        bool hasBgBlend = !string.IsNullOrEmpty(fragment.Style.BackgroundBlendMode)
            && !fragment.Style.BackgroundBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase);
        if (hasRoundedClip)
            items.Add(roundedClip);
        if (hasBgBlend)
            items.Add(new BlendModeItem { Bounds = clipRect, Mode = fragment.Style.BackgroundBlendMode });

        if (effectiveBackgroundClip.Equals("border-area", StringComparison.OrdinalIgnoreCase))
        {
            if (image.TryGetUniformColor(out var uniformColor))
                EmitBorderAreaBorder(bounds, fragment, items, uniformColor);
            else
                EmitBorderAreaTiledImage(bounds, image, fragment, items, tileOrigin, tileW, tileH, repeat);
        }
        else
        {
            items.Add(new DrawTiledImageItem
            {
                Bounds = clipRect,
                ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty,
                FillRect = clipRect,
                PositioningArea = positioningArea,
                TileOrigin = tileOrigin,
                Repeat = repeat,
                TileWidth = tileW,
                TileHeight = tileH,
            });
        }

        if (hasBgBlend)
            items.Add(new RestoreBlendModeItem { Bounds = clipRect });
        if (hasRoundedClip)
            items.Add(new RestoreItem { Bounds = clipRect });
    }

    private static void ApplyBackgroundPositionOffset(ref PointF tileOrigin, string position, float containerWidth, float containerHeight, float imageWidth, float imageHeight, float emSize)
    {
        if (string.IsNullOrEmpty(position))
            return;

        var parts = position.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? xVal = null, yVal = null;
        foreach (var p in parts)
        {
            if (IsHorizontalKeyword(p))
                xVal = p;
            else if (IsVerticalKeyword(p))
                yVal = p;
            else if (p.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                if (xVal == null) xVal = p;
                else if (yVal == null) yVal = p;
            }
            else
            {
                if (xVal == null) xVal = p;
                else if (yVal == null) yVal = p;
            }
        }

        tileOrigin.X += ParsePositionValue(xVal, containerWidth, imageWidth, emSize);
        tileOrigin.Y += ParsePositionValue(yVal, containerHeight, imageHeight, emSize);
    }

    /// <summary>
    /// Emits gradient layer display items for a non-canvas element's background.
    /// </summary>
    private static void EmitElementGradientLayers(Fragment fragment, List<DisplayItem> items, RectangleF viewport)
    {
        var rects = GetPaintRects(fragment);
        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            var imgRect = GetBackgroundClipRect(bounds, fragment, fragment.Style.BackgroundClip);

            if (imgRect.Width <= 0 || imgRect.Height <= 0)
                continue;

            EmitGradientLayers(fragment, imgRect, viewport.Width > 0 ? viewport : imgRect, items);
        }
    }

    /// <summary>Parses a single CSS background-position value (keyword, px, %, or length).</summary>
    /// <param name="val">The position token (e.g. "right", "50%", "10px").</param>
    /// <param name="containerSize">Width or height of the positioning area.</param>
    /// <param name="imageSize">Width or height of the background image.</param>
    /// <returns>Offset in pixels from the origin.</returns>
    private static float ParsePositionValue(string val, float containerSize, float imageSize, float emSize)
    {
        if (string.IsNullOrEmpty(val)) return 0;

        // CSS2.1 §14.2.1 keyword equivalences.
        if (val.Equals("left", StringComparison.OrdinalIgnoreCase)
            || val.Equals("top", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (val.Equals("right", StringComparison.OrdinalIgnoreCase)
            || val.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            return containerSize - imageSize;
        if (val.Equals("center", StringComparison.OrdinalIgnoreCase))
            return (containerSize - imageSize) / 2f;

        if (val.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(val.AsSpan(0, val.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return px;
        }
        else if (val.EndsWith("%"))
        {
            // CSS2.1 §14.2.1: percentage positions use (container - image) as
            // the reference length so that 100% places the image flush-right.
            if (float.TryParse(val.AsSpan(0, val.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return (containerSize - imageSize) * pct / 100f;
        }
        else if (CssValueParser.IsValidLength(val))
        {
            return (float)CssValueParser.ParseLength(val, hundredPercent: 0, emFactor: emSize, defaultUnit: null);
        }
        else if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float raw))
        {
            return raw;
        }
        return 0;
    }

    private static float GetPositionEmSize(ComputedStyle style)
    {
        float fontSize;
        if (CssValueParser.IsValidLength(style.FontSize))
        {
            fontSize = (float)CssValueParser.ParseLength(
                style.FontSize,
                hundredPercent: 0,
                emFactor: DefaultFontSize,
                defaultUnit: null);
        }
        else
        {
            // ParseFontSize returns values in CSS points (matching CssConstants.FontSize = 12pt).
            // Convert pt -> px so that em-based positions match browser rendering (12pt = 16px).
            fontSize = (float)(ParseFontSize(style.FontSize) * (96.0 / 72.0));
        }

        return fontSize > 0 ? fontSize : DefaultFontSize;
    }

    private static bool IsHorizontalKeyword(string val) =>
        val.Equals("left", StringComparison.OrdinalIgnoreCase)
        || val.Equals("right", StringComparison.OrdinalIgnoreCase);

    private static bool IsVerticalKeyword(string val) =>
        val.Equals("top", StringComparison.OrdinalIgnoreCase)
        || val.Equals("bottom", StringComparison.OrdinalIgnoreCase);

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
                TopColor = hasTop ? style.ActualBorderTopColor : Color.Empty,
                RightColor = (hasRight && isLast) ? style.ActualBorderRightColor : Color.Empty,
                BottomColor = hasBottom ? style.ActualBorderBottomColor : Color.Empty,
                LeftColor = (hasLeft && isFirst) ? style.ActualBorderLeftColor : Color.Empty,
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

    private static void EmitText(Fragment fragment, List<DisplayItem> items, Color? bgClipTextColor = null)
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
                Color textColor = inlineStyle.ActualColor;
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

    private static void EmitTextDecoration(Fragment fragment, List<DisplayItem> items, Color? bgClipTextColor = null)
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
        Color decoColor = decorationStyleSource.ActualTextDecorationColor;
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
    private static Color CompositeTextColor(Color bg, Color fg)
    {
        float fgA = fg.A / 255f;
        int r = (int)(bg.R * (1 - fgA) + fg.R * fgA);
        int g = (int)(bg.G * (1 - fgA) + fg.G * fgA);
        int b = (int)(bg.B * (1 - fgA) + fg.B * fgA);
        return Color.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    private static void PaintChildren(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom = null, RectangleF viewport = default, Color? bgClipTextColor = null, bool skipBlockBackgrounds = false)
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
                fixedNoZIndex ??= new List<Fragment>();
                fixedNoZIndex.Add(child);
            }
            else if (child.CreatesStackingContext || child.Style.Position is "relative" or "absolute")
            {
                // Steps 6–7: positioned descendants (CSS2.1 App. E)
                positioned ??= new List<Fragment>();
                positioned.Add(child);
            }
            else if (child.Style.Float is "left" or "right")
            {
                floats ??= new List<Fragment>();
                floats.Add(child);
            }
            else if (child.Style.Display is "inline" or "inline-block" or "inline-table")
            {
                inlineLevel ??= new List<Fragment>();
                inlineLevel.Add(child);
            }
            else
            {
                blocks ??= new List<Fragment>();
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
                PaintFragment(child, items, propagatedFrom, viewport, bgClipTextColor: bgClipTextColor);
        }

        // Steps 6–7: Positioned children sorted by StackLevel
        if (positioned != null)
        {
            positioned.Sort((a, b) => a.StackLevel.CompareTo(b.StackLevel));
            foreach (var child in positioned)
            {
                // CSS2.1 §9.6.1: Fixed-position elements are positioned relative
                // to the viewport.  When rendering a scrolled region, offset the
                // fragment's coordinates so it paints at the viewport-relative
                // position instead of its document-origin layout position.
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
    private static void PaintFragmentForegroundPhase(Fragment fragment, List<DisplayItem> items, Fragment? propagatedFrom, RectangleF viewport, Color? bgClipTextColor = null)
    {
        var style = fragment.Style;

        if (style.Display == "none")
            return;
        if (style.Visibility != "visible")
            return;

        // Detect background-clip: text on this fragment (propagate to children)
        Color? currentBgClipTextColor = bgClipTextColor;
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

        // Foreground content: selection, text, text-decoration
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
                positioned ??= new List<Fragment>();
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

        return double.TryParse(numeric, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }

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

        if (value.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return referenceLength * pct / 100f;
            return 0;
        }

        if (CssValueParser.IsValidLength(value))
            return (float)CssValueParser.ParseLength(value, referenceLength, emSize, defaultUnit: null);

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

    /// <summary>
    /// Offsets all display items starting at <paramref name="startIndex"/> by
    /// (<paramref name="dx"/>, <paramref name="dy"/>).  Used to reposition
    /// <c>position:fixed</c> fragments to viewport-relative coordinates and
    /// to apply scroll offsets during painting.
    /// </summary>
    internal static void OffsetDisplayItems(List<DisplayItem> items, int startIndex, float dx, float dy)
    {
        if (dx == 0 && dy == 0)
            return;

        for (int i = startIndex; i < items.Count; i++)
            items[i] = OffsetItem(items[i], dx, dy);
    }

    internal static DisplayItem OffsetItem(DisplayItem item, float dx, float dy)
    {
        var ob = OffsetRect(item.Bounds, dx, dy);
        return item switch
        {
            FillRectItem f => new FillRectItem { Bounds = ob, Color = f.Color },
            DrawBorderItem b => new DrawBorderItem
            {
                Bounds = ob,
                Widths = b.Widths,
                TopColor = b.TopColor,
                RightColor = b.RightColor,
                BottomColor = b.BottomColor,
                LeftColor = b.LeftColor,
                Style = b.Style,
                TopStyle = b.TopStyle,
                RightStyle = b.RightStyle,
                BottomStyle = b.BottomStyle,
                LeftStyle = b.LeftStyle,
                CornerNw = b.CornerNw,
                CornerNe = b.CornerNe,
                CornerSe = b.CornerSe,
                CornerSw = b.CornerSw,
            },
            DrawTextItem t => new DrawTextItem
            {
                Bounds = ob,
                Text = t.Text,
                FontFamily = t.FontFamily,
                FontSize = t.FontSize,
                FontWeight = t.FontWeight,
                Color = t.Color,
                Origin = new PointF(t.Origin.X + dx, t.Origin.Y + dy),
                FontHandle = t.FontHandle,
                IsRtl = t.IsRtl,
                TextShadowOffsetX = t.TextShadowOffsetX,
                TextShadowOffsetY = t.TextShadowOffsetY,
                TextShadowColor = t.TextShadowColor,
                GradientStops = t.GradientStops,
                GradientAngle = t.GradientAngle,
                GradientInterpolationSpace = t.GradientInterpolationSpace,
                GradientBounds = OffsetRect(t.GradientBounds, dx, dy),
            },
            DrawImageItem img => new DrawImageItem
            {
                Bounds = ob,
                ImageHandle = img.ImageHandle,
                SourceRect = img.SourceRect,
                DestRect = OffsetRect(img.DestRect, dx, dy),
            },
            DrawTiledImageItem ti => new DrawTiledImageItem
            {
                Bounds = ob,
                ImageHandle = ti.ImageHandle,
                SourceRect = ti.SourceRect,
                FillRect = OffsetRect(ti.FillRect, dx, dy),
                PositioningArea = OffsetRect(ti.PositioningArea, dx, dy),
                TileOrigin = new PointF(ti.TileOrigin.X + dx, ti.TileOrigin.Y + dy),
                Repeat = ti.Repeat,
                TileWidth = ti.TileWidth,
                TileHeight = ti.TileHeight,
            },
            DrawTiledGradientItem tg => new DrawTiledGradientItem
            {
                Bounds = ob,
                GradientFunction = tg.GradientFunction,
                TileWidth = tg.TileWidth,
                TileHeight = tg.TileHeight,
                FillRect = OffsetRect(tg.FillRect, dx, dy),
                TileOrigin = new PointF(tg.TileOrigin.X + dx, tg.TileOrigin.Y + dy),
                Repeat = tg.Repeat,
                Stops = tg.Stops,
                Angle = tg.Angle,
                InterpolationSpace = tg.InterpolationSpace,
                IsRadial = tg.IsRadial,
                IsConic = tg.IsConic,
                CenterX = tg.CenterX,
                CenterY = tg.CenterY,
                FromAngle = tg.FromAngle,
            },
            ClipItem c => new ClipItem
            {
                Bounds = ob,
                ClipRect = OffsetRect(c.ClipRect, dx, dy),
                CornerNw = c.CornerNw,
                CornerNwY = c.CornerNwY,
                CornerNe = c.CornerNe,
                CornerNeY = c.CornerNeY,
                CornerSe = c.CornerSe,
                CornerSeY = c.CornerSeY,
                CornerSw = c.CornerSw,
                CornerSwY = c.CornerSwY,
            },
            RestoreItem => new RestoreItem { Bounds = ob },
            OpacityItem o => new OpacityItem { Bounds = ob, Opacity = o.Opacity },
            RestoreOpacityItem => new RestoreOpacityItem { Bounds = ob },
            BlendModeItem bm => new BlendModeItem { Bounds = ob, Mode = bm.Mode },
            RestoreBlendModeItem => new RestoreBlendModeItem { Bounds = ob },
            TransformItem t => new TransformItem
            {
                Bounds = ob,
                Matrix = t.Matrix,
                OriginX = t.OriginX + dx,
                OriginY = t.OriginY + dy,
            },
            RestoreTransformItem => new RestoreTransformItem { Bounds = ob },
            DrawLineItem l => new DrawLineItem
            {
                Bounds = ob,
                Start = new PointF(l.Start.X + dx, l.Start.Y + dy),
                End = new PointF(l.End.X + dx, l.End.Y + dy),
                Color = l.Color,
                Width = l.Width,
                DashStyle = l.DashStyle,
            },
            _ => item,
        };
    }

    internal static RectangleF OffsetRect(RectangleF r, float dx, float dy)
        => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    /// <summary>
    /// Parses a CSS text-shadow value and returns the offset and color components.
    /// Supports: &lt;color&gt; &lt;offsetX&gt; &lt;offsetY&gt; or &lt;offsetX&gt; &lt;offsetY&gt; &lt;blur&gt;? &lt;color&gt;?.
    /// </summary>
    private static (float offsetX, float offsetY, Color color) ParseTextShadow(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "none")
            return (0, 0, Color.Empty);

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

        Color color = Color.Black;
        if (!string.IsNullOrEmpty(colorStr))
        {
            colorStr = colorStr.Trim();
            if (colorStr.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && colorStr.EndsWith(")"))
            {
                var inner = colorStr.Substring(5, colorStr.Length - 6);
                var vals = inner.Split(',');
                if (vals.Length >= 4 &&
                    int.TryParse(vals[0].Trim(), out int r) &&
                    int.TryParse(vals[1].Trim(), out int g) &&
                    int.TryParse(vals[2].Trim(), out int b) &&
                    float.TryParse(vals[3].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float a))
                {
                    color = Color.FromArgb((int)(a * 255), r, g, b);
                }
            }
            else if (colorStr.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && colorStr.EndsWith(")"))
            {
                var inner = colorStr.Substring(4, colorStr.Length - 5);
                var vals = inner.Split(',');
                if (vals.Length >= 3 &&
                    int.TryParse(vals[0].Trim(), out int r) &&
                    int.TryParse(vals[1].Trim(), out int g) &&
                    int.TryParse(vals[2].Trim(), out int b))
                {
                    color = Color.FromArgb(r, g, b);
                }
            }
            else
            {
                // Unrecognised color names fall back to Color.Black (the default above).
                try { color = Color.FromName(colorStr); } catch { /* invalid name – keep default */ }
            }
        }

        return (offsetX, offsetY, color);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  CSS3 multiple background gradient support
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the background-image value contains one or more
    /// CSS gradient function references (e.g. <c>linear-gradient(…)</c>).
    /// </summary>
    private static bool HasGradientBackgroundImage(string? bgImage)
    {
        if (string.IsNullOrEmpty(bgImage) || bgImage == "none")
            return false;
        return bgImage.Contains("gradient(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the first fragment in the canvas propagation chain (root → html → body)
    /// that has gradient functions in its <c>background-image</c>.
    /// </summary>
    private static Fragment? FindGradientSource(Fragment root)
    {
        if (HasGradientBackgroundImage(root.Style.BackgroundImage))
            return root;

        Fragment? html = FindFragmentByTag(root, "html")
            ?? FindFirstBlockChild(root) ?? FindFirstVisibleChild(root);
        if (html == null) return null;

        if (HasGradientBackgroundImage(html.Style.BackgroundImage))
            return html;

        // Only fall back to body if html has no background at all.
        if (html.Style.ActualBackgroundColor.A > 0 || html.BackgroundImageHandle != null)
            return null;

        Fragment? body = FindFragmentByTag(html, "body")
            ?? FindFirstBlockChild(html) ?? FindFirstVisibleChild(html);
        if (body != null && HasGradientBackgroundImage(body.Style.BackgroundImage))
            return body;

        return null;
    }

    /// <summary>
    /// Emits <see cref="DrawTiledGradientItem"/> display items for each gradient
    /// layer in the fragment's <c>background-image</c>.  Layers are painted
    /// bottom-most first (last in the comma list) to top-most (first in the list).
    /// </summary>
    private static void EmitGradientLayers(Fragment fragment, RectangleF fillRect, RectangleF viewport, List<DisplayItem> items)
    {
        var style = fragment.Style;
        var gradientFunctions = SplitGradientLayers(style.BackgroundImage);
        if (gradientFunctions.Count == 0) return;

        // Per-layer comma-separated properties.
        var sizes = SplitOnTopLevelCommas(style.BackgroundSize ?? "auto");
        var positions = SplitOnTopLevelCommas(style.BackgroundPosition ?? "0% 0%");
        var repeats = SplitOnTopLevelCommas(style.BackgroundRepeat ?? "repeat");
        var attachments = SplitOnTopLevelCommas(style.BackgroundAttachment ?? "scroll");

        // CSS3: layers are painted bottom-up. The last listed layer is bottom-most.
        for (int i = gradientFunctions.Count - 1; i >= 0; i--)
        {
            string gradFunc = gradientFunctions[i].Trim();
            if (string.IsNullOrEmpty(gradFunc) || gradFunc == "none")
                continue;

            // Cycle per-layer properties (CSS3 Backgrounds §3: values repeat).
            string sizeStr = sizes.Count > 0 ? sizes[i % sizes.Count].Trim() : "auto";
            string posStr = positions.Count > 0 ? positions[i % positions.Count].Trim() : "0% 0%";
            string repeatStr = repeats.Count > 0 ? repeats[i % repeats.Count].Trim() : "repeat";
            string attachStr = attachments.Count > 0 ? attachments[i % attachments.Count].Trim() : "scroll";

            // Parse the gradient function into color stops and angle.
            var gradInfo = ParseGradientFunction(gradFunc);
            if (gradInfo == null || gradInfo.Stops.Count == 0)
                continue;

            // Parse background-size for this layer.
            float tileW = fillRect.Width;
            float tileH = fillRect.Height;
            ParseBackgroundSize(sizeStr, fillRect.Width, fillRect.Height, out tileW, out tileH);

            // Determine tile origin based on attachment and position.
            // Fixed backgrounds use the viewport as the positioning area,
            // unless the fragment lives inside a transformed containing block,
            // where CSS requires fixed attachment to behave like scroll.
            bool isFixed = attachStr.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                && !fragment.HasTransformAncestor
                && viewport.Width > 0
                && viewport.Height > 0;
            var positioningArea = isFixed ? viewport : fillRect;
            var tileOrigin = new PointF(positioningArea.X, positioningArea.Y);

            // Apply background-position offset.
            var posParts = posStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (posParts.Length >= 1)
            {
                string xVal = null, yVal = null;
                foreach (var p in posParts)
                {
                    if (IsHorizontalKeyword(p))
                        xVal = p;
                    else if (IsVerticalKeyword(p))
                        yVal = p;
                    else if (p.Equals("center", StringComparison.OrdinalIgnoreCase))
                    {
                        if (xVal == null) xVal = p;
                        else if (yVal == null) yVal = p;
                    }
                    else
                    {
                        if (xVal == null) xVal = p;
                        else if (yVal == null) yVal = p;
                    }
                }
                float emSize = GetPositionEmSize(style);
                tileOrigin.X += ParsePositionValue(xVal, positioningArea.Width, tileW, emSize);
                tileOrigin.Y += ParsePositionValue(yVal, positioningArea.Height, tileH, emSize);
            }

            items.Add(new DrawTiledGradientItem
            {
                Bounds = fillRect,
                GradientFunction = gradFunc,
                TileWidth = tileW,
                TileHeight = tileH,
                FillRect = fillRect,
                TileOrigin = tileOrigin,
                Repeat = repeatStr,
                Stops = gradInfo.Stops,
                Angle = gradInfo.Angle,
                InterpolationSpace = gradInfo.InterpolationSpace,
                IsRadial = gradInfo.IsRadial,
                IsConic = gradInfo.IsConic,
                CenterX = gradInfo.CenterX,
                CenterY = gradInfo.CenterY,
                FromAngle = gradInfo.FromAngle,
            });
        }
    }

    /// <summary>
    /// Splits a comma-separated CSS background-image value into individual
    /// gradient function strings, respecting nested parentheses.
    /// </summary>
    private static List<string> SplitGradientLayers(string? bgImage)
    {
        if (string.IsNullOrEmpty(bgImage) || bgImage == "none")
            return new List<string>();
        return SplitOnTopLevelCommas(bgImage);
    }

    /// <summary>
    /// Splits a CSS value on top-level commas (outside parentheses).
    /// </summary>
    private static List<string> SplitOnTopLevelCommas(string value)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (c == ',' && depth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString());
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
    /// CSS Backgrounds Level 4 <c>background-clip: border-area</c>:
    /// Emits 4 tiled-image items, one for each border strip.
    /// </summary>
    private static void EmitBorderAreaTiledImage(RectangleF bounds, object? imageHandle, Fragment fragment, List<DisplayItem> items,
        PointF tileOrigin, float tileW, float tileH, string repeat)
    {
        var border = fragment.Border;
        float bLeft = (float)border.Left, bTop = (float)border.Top;
        float bRight = (float)border.Right, bBottom = (float)border.Bottom;

        // Top strip
        if (bTop > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Y, bounds.Width, bTop);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Bottom strip
        if (bBottom > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Bottom - bBottom, bounds.Width, bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Left strip (between top and bottom)
        if (bLeft > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Y + bTop, bLeft, bounds.Height - bTop - bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Right strip (between top and bottom)
        if (bRight > 0)
        {
            var strip = new RectangleF(bounds.X + bounds.Width - bRight, bounds.Y + bTop, bRight, bounds.Height - bTop - bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
    }

    /// <summary>
    /// Parsed gradient info: angle and color stops.
    /// </summary>
    private sealed class GradientInfo
    {
        public float Angle { get; set; } = 180f; // default: to bottom
        public string InterpolationSpace { get; set; } = "srgb";
        public List<GradientStop> Stops { get; set; } = new();
        public bool IsRadial { get; set; }
        public bool IsConic { get; set; }
        public float CenterX { get; set; } = 0.5f;
        public float CenterY { get; set; } = 0.5f;
        public float FromAngle { get; set; }
    }

    /// <summary>
    /// Parses a CSS gradient function string into angle and color stops.
    /// Supports <c>linear-gradient([angle|direction,] color [pos], color [pos], …)</c>
    /// and <c>radial-gradient([shape size at position,] color [pos], …)</c>.
    /// </summary>
    private static GradientInfo? ParseGradientFunction(string gradFunc)
    {
        bool isLinear = gradFunc.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase);
        bool isRadial = !isLinear && gradFunc.StartsWith("radial-gradient(", StringComparison.OrdinalIgnoreCase);
        bool isConic = !isLinear && !isRadial && gradFunc.StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase);
        if (!isLinear && !isRadial && !isConic)
            return null;

        int openParen = gradFunc.IndexOf('(');
        if (openParen < 0) return null;

        // Find the matching closing paren for the outer linear-gradient().
        // We cannot use TrimEnd(')') as it would strip closing parens of
        // nested color functions like rgba().
        int depth = 0;
        int closeParen = -1;
        for (int ci = openParen; ci < gradFunc.Length; ci++)
        {
            if (gradFunc[ci] == '(') depth++;
            else if (gradFunc[ci] == ')')
            {
                depth--;
                if (depth == 0) { closeParen = ci; break; }
            }
        }
        if (closeParen < 0) closeParen = gradFunc.Length;

        string inner = gradFunc.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        if (string.IsNullOrEmpty(inner)) return null;

        var tokens = SplitOnTopLevelCommas(inner);
        if (tokens.Count < 2) return null;

        var info = new GradientInfo();
        int colorStartIdx = 0;

        if (isConic)
        {
            info.IsConic = true;

            // The first token may be a geometry descriptor:
            //   [from <angle>] [at <position>]
            string first = tokens[0].Trim();
            string firstLower = first.ToLowerInvariant();
            bool isGeometry = firstLower.StartsWith("from ")
                || firstLower.StartsWith("at ")
                || firstLower.Contains(" at ");
            if (isGeometry)
            {
                colorStartIdx = 1;

                int atIdx = firstLower.IndexOf(" at ", StringComparison.Ordinal);
                string fromPart;
                string posPart;
                if (firstLower.StartsWith("at "))
                {
                    fromPart = string.Empty;
                    posPart = first[3..].Trim();
                }
                else if (atIdx >= 0)
                {
                    fromPart = first[..atIdx].Trim();
                    posPart = first[(atIdx + 4)..].Trim();
                }
                else
                {
                    fromPart = first;
                    posPart = string.Empty;
                }

                if (fromPart.StartsWith("from ", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseAngleDegrees(fromPart[5..].Trim(), out float fromDeg))
                        info.FromAngle = fromDeg;
                }

                if (!string.IsNullOrEmpty(posPart))
                    (info.CenterX, info.CenterY) = ParseRadialGradientCenter(posPart);
            }

            info.Stops = ParseConicGradientStops(tokens, colorStartIdx);
            return info;
        }

        if (isRadial)
        {
            info.IsRadial = true;

            // The first token of radial-gradient may be a geometry descriptor:
            //   [<shape> || <size>] [at <position>]
            // If the first token contains "at " or any shape/size keyword it is
            // the geometry descriptor, not a color stop.
            string first = tokens[0].Trim();
            string firstLower = first.ToLowerInvariant();
            bool isGeometry = firstLower.Contains(" at ")
                || firstLower.StartsWith("at ")
                || firstLower == "circle"
                || firstLower == "ellipse"
                || firstLower.Contains("closest-")
                || firstLower.Contains("farthest-");
            if (isGeometry)
            {
                colorStartIdx = 1;
                int atIdx = firstLower.IndexOf(" at ", StringComparison.Ordinal);
                string posStr = atIdx >= 0
                    ? first[(atIdx + 4)..].Trim()
                    : (firstLower.StartsWith("at ") ? first[3..].Trim() : string.Empty);

                if (!string.IsNullOrEmpty(posStr))
                    (info.CenterX, info.CenterY) = ParseRadialGradientCenter(posStr);
            }
        }
        else
        {
            // Check if first token is a direction/angle (linear-gradient only).
            string first = tokens[0].Trim();
            string firstLower = first.ToLowerInvariant();
            if (firstLower.StartsWith("in "))
            {
                info.InterpolationSpace = ParseGradientInterpolationSpace(first[3..].Trim());
                colorStartIdx = 1;
            }
            else
            {
                string angleToken = first;
                int interpolationIdx = firstLower.IndexOf(" in ", StringComparison.Ordinal);
                if (interpolationIdx >= 0)
                {
                    angleToken = first[..interpolationIdx].Trim();
                    info.InterpolationSpace = ParseGradientInterpolationSpace(first[(interpolationIdx + 4)..].Trim());
                    colorStartIdx = 1;
                }

                if (angleToken.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
                {
                    info.Angle = ParseCssDirection(angleToken);
                    colorStartIdx = 1;
                }
                else if (angleToken.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(angleToken.AsSpan(0, angleToken.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float deg))
                        info.Angle = deg;
                    colorStartIdx = 1;
                }
                else if (angleToken.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(angleToken.AsSpan(0, angleToken.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out float turn))
                        info.Angle = turn * 360f;
                    colorStartIdx = 1;
                }
                else if (angleToken.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(angleToken.AsSpan(0, angleToken.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float rad))
                        info.Angle = (float)(rad * 180.0 / Math.PI);
                    colorStartIdx = 1;
                }
            }
        }

        // Parse color stops.
        int stopCount = tokens.Count - colorStartIdx;
        for (int i = colorStartIdx; i < tokens.Count; i++)
        {
            string stopStr = tokens[i].Trim();
            var stop = ParseGradientStop(stopStr, i - colorStartIdx, stopCount);
            if (stop != null)
                info.Stops.Add(stop);
        }

        return info;
    }

    /// <summary>
    /// Parses a CSS gradient direction keyword (e.g. "to bottom", "to top right")
    /// into degrees (CSS convention: 0=top, 90=right, 180=bottom, 270=left).
    /// </summary>
    private static float ParseCssDirection(string direction)
    {
        string dir = direction.Trim().ToLowerInvariant();
        return dir switch
        {
            "to top" => 0f,
            "to top right" or "to right top" => 45f,
            "to right" => 90f,
            "to bottom right" or "to right bottom" => 135f,
            "to bottom" => 180f,
            "to bottom left" or "to left bottom" => 225f,
            "to left" => 270f,
            "to top left" or "to left top" => 315f,
            _ => 180f,
        };
    }

    /// <summary>
    /// Parses a CSS radial-gradient center position string (the part after <c>at</c>)
    /// and returns normalized (0.0–1.0) X and Y fractions.
    /// </summary>
    private static (float CenterX, float CenterY) ParseRadialGradientCenter(string posStr)
    {
        posStr = posStr.Trim();
        if (string.IsNullOrEmpty(posStr))
            return (0.5f, 0.5f);

        // Split into space-separated tokens.
        var parts = posStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float x = 0.5f, y = 0.5f;

        if (parts.Length == 1)
        {
            // Single keyword applies to both axes; treat it as centering the specified axis.
            string p = parts[0].ToLowerInvariant();
            if (p == "center") { x = 0.5f; y = 0.5f; }
            else if (p == "left") { x = 0f; y = 0.5f; }
            else if (p == "right") { x = 1f; y = 0.5f; }
            else if (p == "top") { x = 0.5f; y = 0f; }
            else if (p == "bottom") { x = 0.5f; y = 1f; }
            else x = y = ParsePositionFraction(p);
        }
        else
        {
            x = ParsePositionFraction(parts[0].ToLowerInvariant());
            y = ParsePositionFraction(parts[1].ToLowerInvariant());
        }

        return (Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f));

        static float ParsePositionFraction(string token)
        {
            if (token == "center") return 0.5f;
            if (token == "left" || token == "top") return 0f;
            if (token == "right" || token == "bottom") return 1f;
            if (token.EndsWith('%') && float.TryParse(token.AsSpan(0, token.Length - 1),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return pct / 100f;
            // Pixel values cannot be resolved without tile size; fall back to 50%.
            return 0.5f;
        }
    }

    private static GradientStop? ParseGradientStop(string stopStr, int index, int total)
    {
        // Default position: evenly distributed.
        float position = total > 1 ? (float)index / (total - 1) : 0f;

        // Split color from position hint. The position is the last token
        // if it's a length/percentage, but we need to be careful with
        // parenthesised color functions like rgba(0,0,0,1).
        string colorStr = stopStr;
        string posHint = null;

        // Find the last space at depth 0 to separate color from position.
        int depth = 0;
        int lastSpaceAtDepth0 = -1;
        for (int i = 0; i < stopStr.Length; i++)
        {
            if (stopStr[i] == '(') depth++;
            else if (stopStr[i] == ')' && depth > 0) depth--;
            else if (stopStr[i] == ' ' && depth == 0)
                lastSpaceAtDepth0 = i;
        }

        if (lastSpaceAtDepth0 > 0)
        {
            string possiblePos = stopStr.Substring(lastSpaceAtDepth0 + 1).Trim();
            if (possiblePos.EndsWith("%") || possiblePos.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                posHint = possiblePos;
                colorStr = stopStr.Substring(0, lastSpaceAtDepth0).Trim();
            }
        }

        // Parse the position.
        if (posHint != null)
        {
            if (posHint.EndsWith("%"))
            {
                if (float.TryParse(posHint.AsSpan(0, posHint.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    position = pct / 100f;
            }
            else if (posHint.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                // For px-based positions, treat as fraction of a 100px default tile.
                // The actual tile size will be applied when rendering.
                if (float.TryParse(posHint.AsSpan(0, posHint.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                    position = px / 100f; // Rough approximation; tile size is applied later.
            }
        }

        // Parse the color.
        Color color = ParseCssColor(colorStr);
        if (color.IsEmpty)
            return null;

        return new GradientStop { Color = color, Position = Math.Clamp(position, 0f, 1f) };
    }

    /// <summary>
    /// Parses a CSS angle token (e.g. <c>-90deg</c>, <c>0.25turn</c>,
    /// <c>1.5rad</c>, <c>100grad</c>, or a bare <c>0</c>) into degrees.
    /// </summary>
    private static bool TryParseAngleDegrees(string token, out float degrees)
    {
        degrees = 0f;
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return false;

        if (token == "0")
            return true;

        if (token.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            return float.TryParse(token.AsSpan(0, token.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);

        if (token.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(token.AsSpan(0, token.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out float grad))
            {
                degrees = grad * 0.9f; // 400grad == 360deg
                return true;
            }
            return false;
        }

        if (token.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(token.AsSpan(0, token.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out float turn))
            {
                degrees = turn * 360f;
                return true;
            }
            return false;
        }

        if (token.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(token.AsSpan(0, token.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float rad))
            {
                degrees = (float)(rad * 180.0 / Math.PI);
                return true;
            }
            return false;
        }

        return false;
    }

    /// <summary>
    /// CSS Images 4 §3.3: Parses the colour stops of a conic gradient.  Each
    /// stop position is an <c>&lt;angle&gt;</c> or <c>&lt;percentage&gt;</c>
    /// expressed as a fraction of a full turn (0.0–1.0).  Double-position
    /// stops (<c>color a b</c>) expand to two stops.  Stops without explicit
    /// positions are distributed evenly between their resolved neighbours.
    /// </summary>
    private static List<GradientStop> ParseConicGradientStops(List<string> tokens, int startIdx)
    {
        var colors = new List<Color>();
        var rawPositions = new List<float?>();

        for (int i = startIdx; i < tokens.Count; i++)
        {
            string stopStr = tokens[i].Trim();
            if (stopStr.Length == 0)
                continue;

            var parts = SplitOnTopLevelSpaces(stopStr);

            // Pop trailing position tokens (up to two) off the end.
            var stopPositions = new List<float>();
            int end = parts.Count;
            while (end > 1 && TryParseConicStopPosition(parts[end - 1], out float frac))
            {
                stopPositions.Insert(0, frac);
                end--;
            }

            string colorStr = string.Join(" ", parts.GetRange(0, end));
            Color color = ParseCssColor(colorStr);
            if (color.IsEmpty)
                continue;

            if (stopPositions.Count == 0)
            {
                colors.Add(color);
                rawPositions.Add(null);
            }
            else
            {
                foreach (var p in stopPositions)
                {
                    colors.Add(color);
                    rawPositions.Add(p);
                }
            }
        }

        int count = colors.Count;
        var stops = new List<GradientStop>(count);
        if (count == 0)
            return stops;

        // Resolve missing positions: first defaults to 0, last to 1, and any
        // interior gaps are interpolated between their resolved neighbours.
        if (rawPositions[0] == null) rawPositions[0] = 0f;
        if (rawPositions[count - 1] == null) rawPositions[count - 1] = 1f;
        int idx = 0;
        while (idx < count)
        {
            if (rawPositions[idx] != null) { idx++; continue; }
            int next = idx;
            while (next < count && rawPositions[next] == null) next++;
            float before = rawPositions[idx - 1]!.Value;
            float after = rawPositions[next]!.Value;
            int span = next - (idx - 1);
            for (int k = idx; k < next; k++)
                rawPositions[k] = before + ((after - before) * (k - (idx - 1)) / span);
            idx = next;
        }

        // Clamp to [0,1] and enforce monotonically non-decreasing positions.
        float prev = 0f;
        for (int i = 0; i < count; i++)
        {
            float v = Math.Max(prev, Math.Clamp(rawPositions[i]!.Value, 0f, 1f));
            prev = v;
            stops.Add(new GradientStop { Color = colors[i], Position = v });
        }

        return stops;
    }

    /// <summary>
    /// Parses a conic gradient stop position token (<c>&lt;angle&gt;</c> or
    /// <c>&lt;percentage&gt;</c>) into a fraction of a full turn (0.0–1.0).
    /// </summary>
    private static bool TryParseConicStopPosition(string token, out float fraction)
    {
        fraction = 0f;
        token = token.Trim();
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(token.AsSpan(0, token.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
            {
                fraction = pct / 100f;
                return true;
            }
            return false;
        }

        if (TryParseAngleDegrees(token, out float deg))
        {
            fraction = deg / 360f;
            return true;
        }

        return false;
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

    private static string ParseGradientInterpolationSpace(string interpolation)
    {
        if (interpolation.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            return "hsl";
        if (interpolation.StartsWith("oklch", StringComparison.OrdinalIgnoreCase))
            return "oklch";
        return "srgb";
    }

    /// <summary>
    /// Parses a CSS color value (rgba, rgb, hex, named) into a <see cref="Color"/>.
    /// </summary>
    private static Color ParseCssColor(string colorStr)
    {
        colorStr = colorStr.Trim();
        if (string.IsNullOrEmpty(colorStr))
            return Color.Empty;

        // rgba(r, g, b, a)
        if (colorStr.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && colorStr.EndsWith(")"))
        {
            string inner = colorStr.Substring(5, colorStr.Length - 6);
            var parts = inner.Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b)
                && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
            {
                return Color.FromArgb((int)(Math.Clamp(a, 0f, 1f) * 255), r, g, b);
            }
        }

        // rgb(r, g, b)
        if (colorStr.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && colorStr.EndsWith(")"))
        {
            string inner = colorStr.Substring(4, colorStr.Length - 5);
            var parts = inner.Split(',');
            if (parts.Length == 3
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b))
            {
                return Color.FromArgb(255, r, g, b);
            }
        }

        // Hex colors
        if (colorStr.StartsWith('#'))
        {
            try { return ColorTranslator.FromHtml(colorStr); }
            catch { return Color.Empty; }
        }

        // Named colors
        try
        {
            var c = Color.FromName(colorStr);
            if (c.A > 0 || colorStr.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return c;
        }
        catch { }

        return Color.Empty;
    }

    /// <summary>
    /// Parses a CSS transform property value (e.g. "rotate(45deg) scale(2)")
    /// into a flat 2D affine matrix [a, b, c, d, e, f].
    /// Returns <c>null</c> if the value is "none" or cannot be parsed.
    /// </summary>
    private static float[]? ParseCssTransformMatrix(string transform, RectangleF bounds)
    {
        if (string.IsNullOrWhiteSpace(transform)
            || transform.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        // Start with identity matrix [a, b, c, d, e, f]
        float a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;

        int pos = 0;
        while (pos < transform.Length)
        {
            // Skip whitespace
            while (pos < transform.Length && char.IsWhiteSpace(transform[pos]))
                pos++;
            if (pos >= transform.Length)
                break;

            // Read function name
            int nameStart = pos;
            while (pos < transform.Length && transform[pos] != '(')
                pos++;
            if (pos >= transform.Length)
                return null;

            string funcName = transform[nameStart..pos].Trim().ToLowerInvariant();
            pos++; // skip '('

            // Read arguments until ')'
            int argsStart = pos;
            int depth = 1;
            while (pos < transform.Length && depth > 0)
            {
                if (transform[pos] == '(') depth++;
                else if (transform[pos] == ')') depth--;
                if (depth > 0) pos++;
            }
            if (depth != 0)
                return null;

            string argsStr = transform[argsStart..pos];
            pos++; // skip ')'

            var args = ParseTransformArgs(argsStr, bounds);

            // Compute the function's matrix and multiply
            float fa, fb, fc, fd, fe, ff;
            switch (funcName)
            {
                case "rotate":
                    if (args.Length < 1) return null;
                    float angle = args[0];
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    fa = cos; fb = sin; fc = -sin; fd = cos; fe = 0; ff = 0;
                    break;
                case "scale":
                    if (args.Length < 1) return null;
                    float sx = args[0];
                    float sy = args.Length >= 2 ? args[1] : sx;
                    fa = sx; fb = 0; fc = 0; fd = sy; fe = 0; ff = 0;
                    break;
                case "scalex":
                    if (args.Length < 1) return null;
                    fa = args[0]; fb = 0; fc = 0; fd = 1; fe = 0; ff = 0;
                    break;
                case "scaley":
                    if (args.Length < 1) return null;
                    fa = 1; fb = 0; fc = 0; fd = args[0]; fe = 0; ff = 0;
                    break;
                case "translate":
                    if (args.Length < 1) return null;
                    fe = args[0];
                    ff = args.Length >= 2 ? args[1] : 0;
                    fa = 1; fb = 0; fc = 0; fd = 1;
                    break;
                case "translatex":
                    if (args.Length < 1) return null;
                    fa = 1; fb = 0; fc = 0; fd = 1; fe = args[0]; ff = 0;
                    break;
                case "translatey":
                    if (args.Length < 1) return null;
                    fa = 1; fb = 0; fc = 0; fd = 1; fe = 0; ff = args[0];
                    break;
                case "skew":
                    if (args.Length < 1) return null;
                    float skewX = MathF.Tan(args[0]);
                    float skewY = args.Length >= 2 ? MathF.Tan(args[1]) : 0;
                    fa = 1; fb = skewY; fc = skewX; fd = 1; fe = 0; ff = 0;
                    break;
                case "skewx":
                    if (args.Length < 1) return null;
                    fa = 1; fb = 0; fc = MathF.Tan(args[0]); fd = 1; fe = 0; ff = 0;
                    break;
                case "skewy":
                    if (args.Length < 1) return null;
                    fa = 1; fb = MathF.Tan(args[0]); fc = 0; fd = 1; fe = 0; ff = 0;
                    break;
                case "matrix":
                    if (args.Length < 6) return null;
                    fa = args[0]; fb = args[1]; fc = args[2]; fd = args[3]; fe = args[4]; ff = args[5];
                    break;
                default:
                    // Unknown transform function — skip it
                    continue;
            }

            // Multiply: result = current × func
            float na = a * fa + c * fb;
            float nb = b * fa + d * fb;
            float nc = a * fc + c * fd;
            float nd = b * fc + d * fd;
            float ne = a * fe + c * ff + e;
            float nf = b * fe + d * ff + f;
            a = na; b = nb; c = nc; d = nd; e = ne; f = nf;
        }

        // If still identity, no transform needed
        if (a == 1 && b == 0 && c == 0 && d == 1 && e == 0 && f == 0)
            return null;

        return [a, b, c, d, e, f];
    }

    /// <summary>
    /// Parses the comma-or-space-separated arguments of a CSS transform function.
    /// Handles angle units (deg, rad, grad, turn) and length units (px, %).
    /// Returns the parsed values as an array of floats (angles in radians, lengths in pixels).
    /// </summary>
    private static float[] ParseTransformArgs(string argsStr, RectangleF bounds)
    {
        var parts = argsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<float>(parts.Length);

        for (int idx = 0; idx < parts.Length; idx++)
        {
            string p = parts[idx];
            if (TryParseAngleOrLength(p, bounds, idx, out float val))
                result.Add(val);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Parses a single CSS transform argument value, handling angle units
    /// (deg, rad, grad, turn), length units (px, %), and plain numbers.
    /// For percentage values, <paramref name="argIndex"/> determines whether
    /// to reference <c>bounds.Width</c> (even indices) or <c>bounds.Height</c>
    /// (odd indices), matching the CSS spec for translate(x,y).
    /// </summary>
    private static bool TryParseAngleOrLength(string p, RectangleF bounds, int argIndex, out float result)
    {
        result = 0;
        if (p.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.TryParse(p.AsSpan(0, p.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float deg))
                return false;
            result = deg * MathF.PI / 180f;
            return true;
        }
        if (p.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
            return float.TryParse(p.AsSpan(0, p.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        if (p.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.TryParse(p.AsSpan(0, p.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out float grad))
                return false;
            result = grad * MathF.PI / 200f;
            return true;
        }
        if (p.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.TryParse(p.AsSpan(0, p.Length - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out float turn))
                return false;
            result = turn * 2f * MathF.PI;
            return true;
        }
        if (p.EndsWith('%'))
        {
            if (!float.TryParse(p.AsSpan(0, p.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return false;
            float refDim = (argIndex % 2 == 0) ? bounds.Width : bounds.Height;
            result = pct / 100f * refDim;
            return true;
        }

        // Strip optional 'px' suffix
        ReadOnlySpan<char> numSpan = p.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            ? p.AsSpan(0, p.Length - 2) : p.AsSpan();
        return float.TryParse(numSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
