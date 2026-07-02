using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.Layout.IR;
using static Broiler.Layout.IR.FragmentQuery;
using Broiler.Graphics;


namespace Broiler.HTML.Orchestration.IR;

// CSS gradient parsing and layer emission (linear/radial/conic).
// Split out of PaintWalker.Parsing.cs for size.
internal static partial class PaintWalker
{
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
    private static void EmitGradientLayers(Fragment fragment, RectangleF fillRect, RectangleF viewport, List<DisplayItem> items, RectangleF? scrollPositioningArea = null)
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
            // Scroll-attached layers anchor to the supplied positioning area when
            // one is given (canvas propagation: the source element's box), else to
            // the fill rect (ordinary element backgrounds — unchanged behaviour).
            var positioningArea = isFixed ? viewport : (scrollPositioningArea ?? fillRect);
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
            // CSS Images 4 §3.4.1: a colour stop may carry two positions
            // (`<color> a b`), which is shorthand for two stops of that colour at
            // each position. Expand them so the run between the positions renders
            // as a solid band rather than dropping the stop.
            foreach (string single in ExpandDoublePositionStop(stopStr))
            {
                var stop = ParseGradientStop(single, i - colorStartIdx, stopCount);
                if (stop != null)
                    info.Stops.Add(stop);
            }
        }

        return info;
    }

    /// <summary>
    /// CSS Images 4 §3.4.1: expands a double-position colour stop
    /// (<c>&lt;color&gt; &lt;pos&gt; &lt;pos&gt;</c>) into two single-position stop
    /// strings of the same colour. Other stops pass through unchanged.
    /// </summary>
    private static IEnumerable<string> ExpandDoublePositionStop(string stopStr)
    {
        var parts = SplitOnTopLevelSpaces(stopStr);
        if (parts.Count >= 3
            && IsGradientPositionToken(parts[^1])
            && IsGradientPositionToken(parts[^2]))
        {
            string color = string.Join(" ", parts.GetRange(0, parts.Count - 2));
            yield return $"{color} {parts[^2]}";
            yield return $"{color} {parts[^1]}";
        }
        else
        {
            yield return stopStr;
        }
    }

    private static bool IsGradientPositionToken(string token)
    {
        token = token.Trim();
        ReadOnlySpan<char> num =
            token.EndsWith("%", StringComparison.Ordinal) ? token.AsSpan(0, token.Length - 1)
            : token.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? token.AsSpan(0, token.Length - 2)
            : default;
        return !num.IsEmpty
            && float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
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
        BColor color = ParseCssColor(colorStr);
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
        var colors = new List<BColor>();
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
            BColor color = ParseCssColor(colorStr);
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

    private static string ParseGradientInterpolationSpace(string interpolation)
    {
        if (interpolation.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            return "hsl";
        if (interpolation.StartsWith("oklch", StringComparison.OrdinalIgnoreCase))
            return "oklch";
        return "srgb";
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
}
