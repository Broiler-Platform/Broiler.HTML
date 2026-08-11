using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
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

    /// <summary>
    /// Builds the clip for a fragment's <c>clip-path</c>, or returns <c>false</c> when it is
    /// <c>none</c> or uses a basic shape the rasterizer does not model. <c>inset()</c> becomes a
    /// rectangular clip, <c>polygon()</c> a polygon clip, and <c>circle()</c>/<c>ellipse()</c> an
    /// elliptical one. <c>url(#id)</c> resolves against the document's <c>&lt;clipPath&gt;</c>
    /// definitions. The rest (<c>path()</c>, <c>shape()</c>) are still unhandled, and leaving those
    /// unclipped is the safer failure — a wrong clip erases content the page meant to show.
    /// </summary>
    private static bool TryCreateClipPathItem(
        Fragment fragment, RectangleF bounds, RectangleF viewport, out ClipItem clipItem)
    {
        clipItem = null!;

        var clipPath = fragment.Style.ClipPath;
        if (string.IsNullOrWhiteSpace(clipPath)
            || clipPath.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        clipPath = clipPath.Trim();
        if (!clipPath.EndsWith(")", StringComparison.Ordinal))
            return false;

        if (clipPath.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateSvgReferenceClipPathItem(
                SvgFilterTable.ExtractUrlReferenceId(clipPath), fragment, bounds, viewport, out clipItem);
        }

        if (clipPath.StartsWith("polygon(", StringComparison.OrdinalIgnoreCase))
            return TryCreatePolygonClipPathItem(clipPath[8..^1], fragment, bounds, out clipItem);

        if (clipPath.StartsWith("circle(", StringComparison.OrdinalIgnoreCase))
            return TryCreateEllipseClipPathItem(clipPath[7..^1], fragment, bounds, isCircle: true, out clipItem);

        if (clipPath.StartsWith("ellipse(", StringComparison.OrdinalIgnoreCase))
            return TryCreateEllipseClipPathItem(clipPath[8..^1], fragment, bounds, isCircle: false, out clipItem);

        if (!clipPath.StartsWith("inset(", StringComparison.OrdinalIgnoreCase))
            return false;

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
        // An empty rectangle is a clip that admits nothing, not the absence of one:
        // `inset(100% 0 0 0)`, and the `clip: rect(96px, 96px, 96px, 96px)` that projects onto
        // it, both say the element is not to be seen. Emitting it lets the backend clip to it;
        // dropping it painted the element in full.

        clipItem = new ClipItem { Bounds = bounds, ClipRect = clipRect };
        return true;
    }

    /// <summary>
    /// Parses the argument list of <c>clip-path: polygon(...)</c> — an optional <c>&lt;fill-rule&gt;</c>
    /// followed by a comma-separated list of <c>&lt;x&gt; &lt;y&gt;</c> vertex pairs, each resolved
    /// against the reference box (percentages against its width and height). The fill rule is parsed
    /// and dropped: the rasterizer's crossing test is even-odd, which agrees with <c>nonzero</c> for
    /// the non-self-intersecting polygons that clip paths overwhelmingly use.
    /// </summary>
    private static bool TryCreatePolygonClipPathItem(
        string polygonArgs, Fragment fragment, RectangleF bounds, out ClipItem clipItem)
    {
        clipItem = null!;

        var vertexArgs = SplitOnTopLevelCommas(polygonArgs);
        if (vertexArgs.Count == 0)
            return false;

        var first = vertexArgs[0].Trim();
        if (first.Equals("nonzero", StringComparison.OrdinalIgnoreCase)
            || first.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
        {
            vertexArgs.RemoveAt(0);
        }

        // Fewer than three vertices is not a valid polygon(), and an invalid clip-path is dropped
        // at parse time (computed value `none`) rather than clipping everything away.
        if (vertexArgs.Count < 3)
            return false;

        float emSize = GetPositionEmSize(fragment.Style);
        var points = new PointF[vertexArgs.Count];
        for (int i = 0; i < vertexArgs.Count; i++)
        {
            // Split on top-level spaces so a calc() coordinate stays in one piece.
            var coordinates = SplitOnTopLevelSpaces(vertexArgs[i]);
            if (coordinates.Count != 2)
                return false;

            points[i] = new PointF(
                bounds.X + ParseInsetClipPathValue(coordinates[0], bounds.Width, emSize),
                bounds.Y + ParseInsetClipPathValue(coordinates[1], bounds.Height, emSize));
        }

        float minX = points[0].X, minY = points[0].Y;
        float maxX = points[0].X, maxY = points[0].Y;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        clipItem = new ClipItem
        {
            Bounds = bounds,
            ClipRect = new RectangleF(minX, minY, maxX - minX, maxY - minY),
            Polygon = points,
        };
        return true;
    }

    /// <summary>
    /// Resolves <c>clip-path: url(#id)</c> against the document's <c>&lt;clipPath&gt;</c> definitions
    /// (collected into <see cref="SvgClipPathTable"/> while the fragment tree is built, so the
    /// definition may live in any <c>&lt;svg&gt;</c> subtree). A reference that resolves to nothing —
    /// a missing id, or a definition whose shape the rasterizer does not model — leaves the element
    /// unclipped.
    /// <para>
    /// The two <c>clipPathUnits</c> establish different coordinate systems. <c>objectBoundingBox</c>
    /// makes every coordinate a fraction of the referencing element's own box. The default,
    /// <c>userSpaceOnUse</c>, puts them in the referencing element's user space — lengths in CSS
    /// pixels from the reference box's origin, but percentages against the <em>viewport</em>, which is
    /// exactly what WPT <c>clip-path-element-userSpaceOnUse-004</c> pins: a
    /// <c>&lt;rect y="50%" height="50%"&gt;</c> covering the bottom half of the page rather than of
    /// the SVG that declares it.
    /// </para>
    /// </summary>
    private static bool TryCreateSvgReferenceClipPathItem(
        string? referenceId, Fragment fragment, RectangleF bounds, RectangleF viewport, out ClipItem clipItem)
    {
        clipItem = null!;
        if (!SvgClipPathTable.TryGet(referenceId, out var shape))
            return false;

        float emSize = GetPositionEmSize(fragment.Style);

        // The basis a percentage resolves against, and the scale a plain number carries. For
        // objectBoundingBox both are the element's box and a plain number is already a fraction of
        // it; for userSpaceOnUse a plain number is a CSS pixel length, so its scale is 1.
        float percentBasisX = shape.ObjectBoundingBox ? bounds.Width : viewport.Width;
        float percentBasisY = shape.ObjectBoundingBox ? bounds.Height : viewport.Height;
        float unitScaleX = shape.ObjectBoundingBox ? bounds.Width : 1f;
        float unitScaleY = shape.ObjectBoundingBox ? bounds.Height : 1f;

        float X(string name, float fallback = 0) =>
            bounds.X + ResolveSvgClipLength(shape, name, percentBasisX, unitScaleX, emSize, fallback);
        float Y(string name, float fallback = 0) =>
            bounds.Y + ResolveSvgClipLength(shape, name, percentBasisY, unitScaleY, emSize, fallback);
        float LengthX(string name, float fallback = 0) =>
            ResolveSvgClipLength(shape, name, percentBasisX, unitScaleX, emSize, fallback);
        float LengthY(string name, float fallback = 0) =>
            ResolveSvgClipLength(shape, name, percentBasisY, unitScaleY, emSize, fallback);

        switch (shape.Kind)
        {
            case SvgClipPathTable.ClipShapeKind.Rect:
            {
                float width = LengthX("width");
                float height = LengthY("height");
                if (width <= 0 || height <= 0)
                {
                    // A zero or negative width/height disables rendering of the shape (SVG 1.1
                    // §9.2), and a clip with no shape in it clips everything away.
                    clipItem = new ClipItem { Bounds = bounds, ClipRect = RectangleF.Empty };
                    return true;
                }

                clipItem = new ClipItem
                {
                    Bounds = bounds,
                    ClipRect = new RectangleF(X("x"), Y("y"), width, height),
                };
                return true;
            }

            case SvgClipPathTable.ClipShapeKind.Circle:
            {
                // A circle's single radius resolves against the normalised diagonal, the same
                // reference length CSS circle() uses.
                float diagonal = (float)(Math.Sqrt(
                    (percentBasisX * percentBasisX) + (percentBasisY * percentBasisY)) / Math.Sqrt(2));
                float radius = ResolveSvgClipLength(
                    shape, "r", diagonal, shape.ObjectBoundingBox ? diagonal : 1f, emSize, 0);
                return TryCreateSvgEllipseClip(bounds, X("cx"), Y("cy"), radius, radius, out clipItem);
            }

            case SvgClipPathTable.ClipShapeKind.Ellipse:
                return TryCreateSvgEllipseClip(
                    bounds, X("cx"), Y("cy"), LengthX("rx"), LengthY("ry"), out clipItem);

            case SvgClipPathTable.ClipShapeKind.Polygon:
            {
                if (!shape.Attributes.TryGetValue("points", out var pointList))
                    return false;

                var numbers = SvgPointNumbers(pointList);
                if (numbers.Count < 6 || numbers.Count % 2 != 0)
                    return false;

                var points = new PointF[numbers.Count / 2];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = new PointF(
                        bounds.X + (numbers[i * 2] * unitScaleX),
                        bounds.Y + (numbers[(i * 2) + 1] * unitScaleY));
                }

                float minX = points[0].X, minY = points[0].Y, maxX = points[0].X, maxY = points[0].Y;
                foreach (var point in points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }

                clipItem = new ClipItem
                {
                    Bounds = bounds,
                    ClipRect = new RectangleF(minX, minY, maxX - minX, maxY - minY),
                    Polygon = points,
                };
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Builds the elliptical clip for a <c>&lt;circle&gt;</c>/<c>&lt;ellipse&gt;</c> clip-path
    /// child, using the same degenerate-rounded-rect encoding as CSS <c>ellipse()</c>.</summary>
    private static bool TryCreateSvgEllipseClip(
        RectangleF bounds, float centerX, float centerY, float radiusX, float radiusY, out ClipItem clipItem)
    {
        clipItem = null!;
        if (radiusX < 0 || radiusY < 0)
            return false;

        if (radiusX == 0 || radiusY == 0)
        {
            clipItem = new ClipItem { Bounds = bounds, ClipRect = RectangleF.Empty };
            return true;
        }

        clipItem = new ClipItem
        {
            Bounds = bounds,
            ClipRect = new RectangleF(centerX - radiusX, centerY - radiusY, radiusX * 2, radiusY * 2),
            CornerNw = radiusX,
            CornerNwY = radiusY,
            CornerNe = radiusX,
            CornerNeY = radiusY,
            CornerSe = radiusX,
            CornerSeY = radiusY,
            CornerSw = radiusX,
            CornerSwY = radiusY,
        };
        return true;
    }

    /// <summary>
    /// Resolves one SVG geometry attribute: a percentage against <paramref name="percentBasis"/>, or
    /// a plain number scaled by <paramref name="unitScale"/> (1 for user-space CSS pixels, the box
    /// extent for <c>objectBoundingBox</c> fractions).
    /// </summary>
    private static float ResolveSvgClipLength(
        SvgClipPathTable.ClipShape shape, string name, float percentBasis, float unitScale, float emSize, float fallback)
    {
        if (!shape.Attributes.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        raw = raw.Trim();
        if (raw.EndsWith('%'))
        {
            return float.TryParse(
                raw.AsSpan(0, raw.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                ? percent / 100f * percentBasis
                : fallback;
        }

        // A bare number is a user unit, which is what SVG geometry attributes normally carry.
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return value * unitScale;

        // A unit suffix is not valid SVG2 geometry syntax, but SVG 1.1 allowed it and the tests use
        // it (`width="150px"` in clip-path-element-userSpaceOnUse-001), so resolve it as a CSS
        // length. Being an absolute length, it is not scaled by the object bounding box.
        return CssLengthParser.IsValidLength(raw)
            ? (float)CssLengthParser.ParseLength(raw, percentBasis, emSize, defaultUnit: null)
            : fallback;
    }

    /// <summary>Pulls the numbers out of an SVG <c>points</c> list, whose separators may be any mix
    /// of commas and whitespace.</summary>
    private static List<float> SvgPointNumbers(string pointList)
    {
        var numbers = new List<float>();
        foreach (var token in pointList.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                numbers.Add(value);
            else
                return [];
        }

        return numbers;
    }

    /// <summary>
    /// Parses <c>clip-path: circle(...)</c> / <c>ellipse(...)</c> — an optional radius (one value
    /// for a circle, two for an ellipse) and an optional <c>at &lt;position&gt;</c>, defaulting to
    /// <c>closest-side</c> at the centre of the reference box (CSS Shapes §3).
    ///
    /// Both become a rounded-rectangle clip whose box is the ellipse's bounding box and whose four
    /// corner radii are the ellipse's own radii. That degenerate rounded rect <em>is</em> the
    /// ellipse — every corner arc is the same ellipse centred on the box — so this needs no new
    /// display-list item or backend entry point, and a backend that can only clip rectangles
    /// degrades to the bounding box exactly as it already does for rounded corners.
    /// </summary>
    private static bool TryCreateEllipseClipPathItem(
        string shapeArgs, Fragment fragment, RectangleF bounds, bool isCircle, out ClipItem clipItem)
    {
        clipItem = null!;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        var tokens = SplitOnTopLevelSpaces(shapeArgs);
        int atIndex = tokens.FindIndex(t => t.Equals("at", StringComparison.OrdinalIgnoreCase));

        var radiusTokens = atIndex >= 0 ? tokens.GetRange(0, atIndex) : tokens;
        var positionTokens = atIndex >= 0
            ? tokens.GetRange(atIndex + 1, tokens.Count - atIndex - 1)
            : [];

        // circle() takes one radius, ellipse() takes two, and either may be omitted entirely.
        // Anything else is invalid, and an invalid clip-path is dropped rather than half-read.
        int radiusCount = isCircle ? 1 : 2;
        if (radiusTokens.Count != 0 && radiusTokens.Count != radiusCount)
            return false;

        float emSize = GetPositionEmSize(fragment.Style);
        if (!TryResolveShapePosition(positionTokens, bounds, emSize, out float centerX, out float centerY))
            return false;

        // Distances from the centre to each edge, which the <radial-size> keywords select between.
        float toLeft = centerX - bounds.X, toRight = bounds.Right - centerX;
        float toTop = centerY - bounds.Y, toBottom = bounds.Bottom - centerY;

        float radiusX, radiusY;
        if (isCircle)
        {
            // A circle percentage resolves against sqrt(w² + h²) / sqrt(2) — the "diagonal"
            // reference length that keeps 50% on a square equal to half its side (CSS Shapes §3.1).
            float diagonal = (float)(Math.Sqrt(
                (bounds.Width * bounds.Width) + (bounds.Height * bounds.Height)) / Math.Sqrt(2));
            if (!TryResolveShapeRadius(
                    radiusTokens.Count > 0 ? radiusTokens[0] : "closest-side",
                    diagonal, emSize,
                    Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom)),
                    Math.Max(Math.Max(toLeft, toRight), Math.Max(toTop, toBottom)),
                    out radiusX))
            {
                return false;
            }

            radiusY = radiusX;
        }
        else
        {
            if (!TryResolveShapeRadius(
                    radiusTokens.Count > 0 ? radiusTokens[0] : "closest-side",
                    bounds.Width, emSize,
                    Math.Min(toLeft, toRight), Math.Max(toLeft, toRight), out radiusX)
                || !TryResolveShapeRadius(
                    radiusTokens.Count > 1 ? radiusTokens[1] : "closest-side",
                    bounds.Height, emSize,
                    Math.Min(toTop, toBottom), Math.Max(toTop, toBottom), out radiusY))
            {
                return false;
            }
        }

        // A negative radius is invalid and drops the declaration (leaving the element unclipped);
        // a zero radius is a valid, empty shape, and an empty shape clips everything away.
        if (radiusX < 0 || radiusY < 0)
            return false;

        if (radiusX == 0 || radiusY == 0)
        {
            clipItem = new ClipItem { Bounds = bounds, ClipRect = RectangleF.Empty };
            return true;
        }

        clipItem = new ClipItem
        {
            Bounds = bounds,
            ClipRect = new RectangleF(centerX - radiusX, centerY - radiusY, radiusX * 2, radiusY * 2),
            CornerNw = radiusX,
            CornerNwY = radiusY,
            CornerNe = radiusX,
            CornerNeY = radiusY,
            CornerSe = radiusX,
            CornerSeY = radiusY,
            CornerSw = radiusX,
            CornerSwY = radiusY,
        };
        return true;
    }

    /// <summary>
    /// Resolves one <c>&lt;radial-size&gt;</c>: a length or percentage against
    /// <paramref name="referenceLength"/>, or the <c>closest-side</c>/<c>farthest-side</c> keywords
    /// against the pre-computed edge distances. <c>closest-corner</c>/<c>farthest-corner</c> are
    /// gradient-only and not valid here, so they fail the shape rather than being approximated.
    /// </summary>
    private static bool TryResolveShapeRadius(
        string token, float referenceLength, float emSize, float closestSide, float farthestSide, out float radius)
    {
        if (token.Equals("closest-side", StringComparison.OrdinalIgnoreCase))
        {
            radius = closestSide;
            return true;
        }

        if (token.Equals("farthest-side", StringComparison.OrdinalIgnoreCase))
        {
            radius = farthestSide;
            return true;
        }

        radius = 0;
        if (!token.EndsWith('%') && !CssLengthParser.IsValidLength(token))
            return false;

        radius = ParseInsetClipPathValue(token, referenceLength, emSize);
        return true;
    }

    /// <summary>
    /// Resolves the <c>at &lt;position&gt;</c> of a <c>circle()</c>/<c>ellipse()</c> to a point in
    /// <paramref name="bounds"/>. Supports the one- and two-value forms (a keyword, a percentage or
    /// a length per axis); an omitted position — and an omitted second value — is <c>center</c>.
    /// The four-value edge-offset form (<c>left 10px top 20px</c>) is not modelled and fails the
    /// shape, leaving the element unclipped rather than clipped in the wrong place.
    /// </summary>
    private static bool TryResolveShapePosition(
        List<string> tokens, RectangleF bounds, float emSize, out float centerX, out float centerY)
    {
        centerX = bounds.X + (bounds.Width / 2f);
        centerY = bounds.Y + (bounds.Height / 2f);

        if (tokens.Count == 0)
            return true;
        if (tokens.Count > 2)
            return false;

        // A single value sets one axis explicitly; the other stays centred. A lone `top`/`bottom`
        // names the vertical axis, everything else the horizontal one.
        if (tokens.Count == 1
            && (tokens[0].Equals("top", StringComparison.OrdinalIgnoreCase)
                || tokens[0].Equals("bottom", StringComparison.OrdinalIgnoreCase)))
        {
            return TryResolveShapePositionValue(
                tokens[0], bounds.Y, bounds.Height, emSize, isHorizontal: false, out centerY);
        }

        if (!TryResolveShapePositionValue(
                tokens[0], bounds.X, bounds.Width, emSize, isHorizontal: true, out centerX))
        {
            return false;
        }

        return tokens.Count == 1
            || TryResolveShapePositionValue(
                tokens[1], bounds.Y, bounds.Height, emSize, isHorizontal: false, out centerY);
    }

    private static bool TryResolveShapePositionValue(
        string token, float origin, float extent, float emSize, bool isHorizontal, out float coordinate)
    {
        coordinate = origin + (extent / 2f);

        if (token.Equals("center", StringComparison.OrdinalIgnoreCase))
            return true;

        var near = isHorizontal ? "left" : "top";
        var far = isHorizontal ? "right" : "bottom";
        if (token.Equals(near, StringComparison.OrdinalIgnoreCase))
        {
            coordinate = origin;
            return true;
        }

        if (token.Equals(far, StringComparison.OrdinalIgnoreCase))
        {
            coordinate = origin + extent;
            return true;
        }

        if (!token.EndsWith('%') && !CssLengthParser.IsValidLength(token))
            return false;

        coordinate = origin + ParseInsetClipPathValue(token, extent, emSize);
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

    /// <summary>
    /// The vertical radius of a corner, given its resolved horizontal one. A single value makes a
    /// circular corner — except that a <em>percentage</em> resolves against the box's width on the
    /// horizontal axis and its height on the vertical, so the same percentage is a different length
    /// per axis. Two values (<c>border-top-left-radius: 75px 50px</c>, or the <c>/</c> form of the
    /// shorthand) name an ellipse outright.
    /// <para>
    /// The second value is applied as a ratio against the first rather than re-parsed, so the zoom
    /// already folded into <paramref name="cornerRadiusX"/> carries over for free. A ratio only means
    /// something when both values share a unit; mixed units fall back to the single-value result
    /// rather than inventing a length.
    /// </para>
    /// </summary>
    private static double GetEffectiveCornerRadiusY(string rawRadius, double cornerRadiusX, RectangleF bounds)
    {
        if (string.IsNullOrWhiteSpace(rawRadius))
            return cornerRadiusX;

        var parts = rawRadius.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var horizontal = parts.Length > 0 ? parts[0] : rawRadius;

        var singleValueY = horizontal.Contains('%', StringComparison.Ordinal) && bounds.Width > 0
            ? cornerRadiusX * bounds.Height / bounds.Width
            : cornerRadiusX;

        if (parts.Length < 2)
            return singleValueY;

        if (!TryParseCornerRadiusComponent(horizontal, out var x, out var xUnit)
            || !TryParseCornerRadiusComponent(parts[1], out var y, out var yUnit)
            || !string.Equals(xUnit, yUnit, StringComparison.OrdinalIgnoreCase)
            || x <= 0)
        {
            return singleValueY;
        }

        return singleValueY * (y / x);
    }

    /// <summary>Splits a corner-radius component into its number and its unit suffix.</summary>
    private static bool TryParseCornerRadiusComponent(string token, out double value, out string unit)
    {
        value = 0;
        unit = string.Empty;

        var end = 0;
        while (end < token.Length && (char.IsAsciiDigit(token[end]) || token[end] is '.' or '-' or '+'))
            end++;

        if (end == 0)
            return false;

        unit = token[end..].Trim();
        return double.TryParse(
            token[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
