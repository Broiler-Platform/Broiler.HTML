using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;


namespace Broiler.HTML.Orchestration.IR;

// CSS transform property parsing into a 2D affine matrix.
// Split out of PaintWalker.Parsing.cs for size.
internal static partial class PaintWalker
{
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

            var args = ParseTransformArgs(argsStr, bounds, funcName);

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
    /// <para>
    /// <paramref name="funcName"/> decides what a percentage means. A scale factor is
    /// <c>&lt;number&gt; | &lt;percentage&gt;</c> and the percentage is simply the ratio —
    /// <c>scale(50%)</c> is <c>scale(0.5)</c> (css-transforms-2 §"scale") — while only the translate
    /// family resolves one against the box. Resolving every percentage against the box multiplied a
    /// scaled element by its own pixel size: a 100px box given <c>scale(50%)</c> came out 50× and
    /// filled the canvas.
    /// </para>
    /// </summary>
    private static float[] ParseTransformArgs(string argsStr, RectangleF bounds, string funcName)
    {
        // funcName is already lower-cased by the caller; this covers scale/scaleX/scaleY/scale3d.
        bool percentageIsRatio = funcName.StartsWith("scale", StringComparison.Ordinal);

        var parts = argsStr.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<float>(parts.Length);

        for (int idx = 0; idx < parts.Length; idx++)
        {
            string p = parts[idx];
            if (TryParseAngleOrLength(p, bounds, idx, percentageIsRatio, out float val))
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
    private static bool TryParseAngleOrLength(
        string p, RectangleF bounds, int argIndex, bool percentageIsRatio, out float result)
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
            if (percentageIsRatio)
            {
                result = pct / 100f;
                return true;
            }
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
