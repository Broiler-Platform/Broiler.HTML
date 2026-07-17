using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Layout.IR;
using static Broiler.Layout.IR.FragmentQuery;


namespace Broiler.HTML.Orchestration.IR;

// CSS2.1 §14.2 canvas background propagation: entry point (Paint) and the
// root/html/body cascade that decides which element paints the canvas.
internal static partial class PaintWalker
{
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

        // CSS Position 4 §top-layer: modal dialogs, open popovers, and their ::backdrops paint
        // above every ordinary stacking context, after the whole tree. A no-op unless a fragment
        // carries a top-layer order (bridge-marked); see PaintTopLayer.
        PaintTopLayer(root, items, propagatedFrom, viewport);
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
        // CSS Color Adjust §2.3: when the document root's used color scheme is
        // dark, the canvas is painted the UA dark backdrop colour instead of
        // white. Emit it as the bottom layer so a propagated root/body
        // background (found below) paints over it; a fully-transparent root and
        // body leave the dark backdrop showing.
        if (RootUsesDarkColorScheme(root))
            items.Add(new FillRectItem { Bounds = viewport, Color = DarkCanvasColor });

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
                NumberStyles.Float,
                CultureInfo.InvariantCulture, out var op))
        {
            rootOpacity = Math.Clamp(op, 0f, 1f);
        }

        if (canvasBg.A > 0)
        {
            BColor finalBg = CompositOverWhite(canvasBg, rootOpacity);

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
            // CSS2.1 §14.2: the root element's background is propagated to the
            // canvas and paints over the entire viewport, not just the source
            // element's box. Scroll-attached layers stay anchored to the
            // source's background positioning area (gradientClipRect); fixed
            // layers anchor to the viewport (handled inside EmitGradientLayers).
            EmitGradientLayers(gradientSource, viewport, viewport, items,
                scrollPositioningArea: gradientClipRect);

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

            // A multi-layer background (e.g. `background: url(a), url(b), red`)
            // stores its per-layer handles as an object?[]; the canvas
            // propagation must draw every image layer, not pass the array as a
            // single handle (which the rasterizer can't draw -> the image
            // silently vanishes and the canvas colour shows through).
            int layerCount = imgSource.BackgroundImageHandle is object?[] arr
                ? arr.Length
                : 1;
            var handles = NormalizeBackgroundImageHandles(imgSource.BackgroundImageHandle, layerCount);
            var repeats = SplitOnTopLevelCommas(imgSource.Style.BackgroundRepeat ?? "repeat");
            var positions = SplitOnTopLevelCommas(imgSource.Style.BackgroundPosition ?? "0% 0%");
            float emSize = GetPositionEmSize(imgSource.Style);

            // Paint from the bottom-most layer (last in source order) up, so
            // earlier layers composite on top, matching normal layer ordering.
            for (int i = handles.Length - 1; i >= 0; i--)
            {
                if (handles[i] is not RImage layerImage)
                    continue;

                var repeat = repeats.Count > 0
                    ? repeats[i % repeats.Count].Trim()
                    : "repeat";
                var posStr = positions.Count > 0
                    ? positions[i % positions.Count].Trim()
                    : "0% 0%";

                var tileOrigin = new PointF(viewport.X, viewport.Y);
                ApplyBackgroundPositionOffset(
                    ref tileOrigin,
                    posStr,
                    viewport.Width,
                    viewport.Height,
                    (float)layerImage.Width,
                    (float)layerImage.Height,
                    emSize);

                items.Add(new DrawTiledImageItem
                {
                    Bounds = viewport,
                    ImageHandle = layerImage,
                    SourceRect = RectangleF.Empty,
                    FillRect = viewport,
                    PositioningArea = viewport,
                    TileOrigin = tileOrigin,
                    Repeat = repeat,
                });
            }

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
    private static (BColor Color, Fragment? ColorSource, Fragment? ImageSource) FindCanvasBackgroundAndImage(Fragment root)
    {
        // Check root itself.
        if (root.Style.ActualBackgroundColor.A > 0 || root.BackgroundImageHandle != null)
        {
            // CSS Backgrounds §2.11.1: if the root element has display:none
            // its background must not propagate to the canvas.
            if (SuppressesPropagation(root, isRootElement: true))
                return (BColor.Empty, null, null);

            return (root.Style.ActualBackgroundColor, root,
                root.BackgroundImageHandle != null ? root : null);
        }

        // Step 1: html element — prefer TagName-based lookup, then
        // fall back to structural heuristics for trees without tag info.
        Fragment? html = FindFragmentByTag(root, "html")
            ?? FindFirstBlockChild(root) ?? FindFirstVisibleChild(root);
        if (html == null)
            return (BColor.Empty, null, null);

        // CSS Containment §4.2: contain:paint on the html element prevents
        // propagation from body.  The html element is the document root, so
        // CSS Display §2.5 blockifies a display:contents value here — it does
        // not remove the root's box, and its background still propagates.
        bool htmlSuppressed = SuppressesPropagation(html, isRootElement: true);

        bool htmlHasBg = html.Style.ActualBackgroundColor.A > 0;
        bool htmlHasImg = html.BackgroundImageHandle != null;

        if (htmlHasBg || htmlHasImg)
        {
            if (htmlSuppressed)
                return (BColor.Empty, null, null);

            return (html.Style.ActualBackgroundColor,
                htmlHasBg ? html : null,
                htmlHasImg ? html : null);
        }

        // Step 2: html has no background at all → fall back to body.
        // But if html has contain:paint, propagation from body is blocked.
        if (htmlSuppressed)
            return (BColor.Empty, null, null);

        // When body has display:inline, anonymous block wrappers may
        // intervene between the html fragment and the body fragment.
        // Use a recursive tag-based search (depth-limited) to locate
        // the body regardless of box-tree restructuring.
        Fragment? body = FindFragmentByTag(html, "body")
            ?? FindFirstBlockChild(html) ?? FindFirstVisibleChild(html);
        if (body == null)
            return (BColor.Empty, null, null);

        // CSS Containment §4.2 / CSS Backgrounds §2.11.1: body with
        // contain:paint or display:contents/none does not propagate.
        if (SuppressesPropagation(body))
            return (BColor.Empty, null, null);

        bool bodyHasBg = body.Style.ActualBackgroundColor.A > 0;
        bool bodyHasImg = body.BackgroundImageHandle != null;

        if (bodyHasBg || bodyHasImg)
        {
            return (body.Style.ActualBackgroundColor,
                bodyHasBg ? body : null,
                bodyHasImg ? body : null);
        }

        return (BColor.Empty, null, null);
    }

    // CSS Color Adjust §2.3: the UA-defined dark canvas backdrop colour that
    // Chromium paints for a dark used color scheme (rgb(18, 18, 18)).
    private static readonly BColor DarkCanvasColor = BColor.FromArgb(255, 18, 18, 18);

    /// <summary>
    /// CSS Color Adjust §2.2–2.3: whether the document root element's used
    /// color scheme is <c>dark</c>, which makes the canvas backdrop dark.
    /// <para>
    /// The reference environment prefers a <em>light</em> color scheme
    /// (Playwright's default). The used scheme is the preferred one when the
    /// element's <c>color-scheme</c> list includes it; otherwise the first
    /// supported scheme in the list is used. So the canvas is dark exactly when
    /// the list offers <c>dark</c> but not <c>light</c>. The <c>only</c> keyword
    /// does not change this here (it forbids a UA override we do not perform).
    /// </para>
    /// </summary>
    private static bool RootUsesDarkColorScheme(Fragment root)
    {
        // color-scheme governing the canvas is the document root element's
        // (html). `root` may be the html fragment itself or a synthetic parent.
        var html = FindFragmentByTag(root, "html") ?? FindFirstBlockChild(root) ?? root;

        var scheme = html.Style.ColorScheme;
        if (string.IsNullOrWhiteSpace(scheme))
            return false;

        bool hasDark = false, hasLight = false;
        foreach (var token in scheme.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("dark", StringComparison.OrdinalIgnoreCase))
                hasDark = true;
            else if (token.Equals("light", StringComparison.OrdinalIgnoreCase))
                hasLight = true;
        }

        return hasDark && !hasLight;
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
    /// <para>
    /// When <paramref name="isRootElement"/> is set, <c>display: contents</c>
    /// does <em>not</em> suppress: CSS Display §2.5 blockifies the document
    /// root element, so its box (and background) is generated as if
    /// <c>display: block</c> had been specified.
    /// </para>
    /// </summary>
    private static bool SuppressesPropagation(Fragment fragment, bool isRootElement = false)
    {
        var display = fragment.Style.Display;
        if (display == "none")
            return true;
        if (display == "contents" && !isRootElement)
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
    private static BColor CompositOverWhite(BColor fg, float opacity)
    {
        float a = (fg.A / 255f) * opacity;
        if (a >= 1f) return fg;
        if (a <= 0f) return BColor.White;

        int r = (int)Math.Round(fg.R * a + 255 * (1 - a));
        int g = (int)Math.Round(fg.G * a + 255 * (1 - a));
        int b = (int)Math.Round(fg.B * a + 255 * (1 - a));

        return BColor.FromArgb(255,
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(b, 0, 255));
    }

    /// <summary>
    /// Applies CSS filter functions on the root/html fragment to the canvas background color.
    /// Supports <c>invert()</c> and <c>sepia()</c>; other filter functions are silently ignored.
    /// </summary>
    private static BColor ApplyRootFilter(Fragment? htmlFragment, BColor color)
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
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var pct))
                    amount = pct / 100f;
            }
            else if (float.TryParse(valStr,
                NumberStyles.Float,
                CultureInfo.InvariantCulture, out var val))
            {
                amount = val;
            }
            amount = Math.Clamp(amount, 0f, 1f);

            // invert(amount): result = value × (1 - amount) + (255 - value) × amount
            int r = (int)Math.Round(color.R * (1 - amount) + (255 - color.R) * amount);
            int g = (int)Math.Round(color.G * (1 - amount) + (255 - color.G) * amount);
            int b = (int)Math.Round(color.B * (1 - amount) + (255 - color.B) * amount);

            return BColor.FromArgb(color.A,
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
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var pct))
                    amount = pct / 100f;
            }
            else if (float.TryParse(valStr,
                NumberStyles.Float,
                CultureInfo.InvariantCulture, out var val))
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

            return BColor.FromArgb(color.A,
                Math.Clamp(r, 0, 255),
                Math.Clamp(g, 0, 255),
                Math.Clamp(b, 0, 255));
        }

        return color;
    }
}
