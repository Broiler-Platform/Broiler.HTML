using System.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.HTML.Utils;
using Broiler.HTML.Core;

namespace Broiler.HTML.CSS.Core.Parse;

internal sealed class CssValueParser
{
    private readonly record struct LengthEvaluation(double Pixels, bool IsUnitless);
    private readonly IColorResolver _colorResolver;

    /// <summary>
    /// Current viewport dimensions (in pixels) used to resolve CSS viewport
    /// units (vh, vw, vmin, vmax).  Set by the layout engine before each
    /// layout pass.  Defaults to 0×0 which causes viewport-unit lengths to
    /// evaluate to zero.
    /// </summary>
    [ThreadStatic]
    private static SizeF _viewportSize;

    /// <summary>Pre-computed factor for 1vh (viewport height / 100).</summary>
    [ThreadStatic]
    private static double _vhFactor;

    /// <summary>Pre-computed factor for 1vw (viewport width / 100).</summary>
    [ThreadStatic]
    private static double _vwFactor;

    /// <summary>Pre-computed factor for 1vmin (min dimension / 100).</summary>
    [ThreadStatic]
    private static double _vminFactor;

    /// <summary>Pre-computed factor for 1vmax (max dimension / 100).</summary>
    [ThreadStatic]
    private static double _vmaxFactor;

    /// <summary>
    /// Sets the viewport dimensions used by <see cref="ParseLength"/> to
    /// resolve CSS viewport-relative units.
    /// </summary>
    public static void SetViewportSize(float width, float height)
    {
        _viewportSize = new SizeF(width, height);
        _vwFactor = width * 0.01;
        _vhFactor = height * 0.01;
        _vminFactor = Math.Min(width, height) * 0.01;
        _vmaxFactor = Math.Max(width, height) * 0.01;
    }

    public CssValueParser(IColorResolver colorResolver)
    {
        ArgumentNullException.ThrowIfNull(colorResolver);

        _colorResolver = colorResolver;
    }

    public static bool IsFloat(string str, int idx, int length)
    {
        if (length < 1)
            return false;

        bool sawDot = false;

        for (int i = 0; i < length; i++)
        {
            if (str[idx + i] == '.')
            {
                if (sawDot)
                    return false;

                sawDot = true;
            }
            else if (!char.IsDigit(str[idx + i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsInt(string str, int idx, int length)
    {
        if (length < 1)
            return false;

        for (int i = 0; i < length; i++)
        {
            if (!char.IsDigit(str[idx + i]))
                return false;
        }

        return true;
    }

    public static bool IsValidLength(string value)
    {
        var defaultRootLineHeight = CssConstants.FontSize * (96.0 / 72.0) * 1.2;
        if (TryEvaluateLengthExpression(
                value,
                100f,
                16f,
                null,
                fontAdjust: false,
                returnPoints: false,
                lineHeightFactor: 16f * 1.2,
                rootLineHeightFactor: defaultRootLineHeight,
                out _))
        {
            return true;
        }

        value = NormalizeSingleValueLengthFunction(value);

        // CSS2.1 §4.3.2: "0" is a valid length (unit identifier optional after zero).
        if (value == "0")
            return true;

        if (value.Length <= 1)
            return false;

        string number = string.Empty;

        if (value.EndsWith('%'))
        {
            number = value[..^1];
        }
        else if (value.EndsWith(CssConstants.Rem, StringComparison.Ordinal) && value.Length > 3)
        {
            number = value[..^3];
        }
        else if (value.EndsWith(CssConstants.Rlh, StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            number = value[..^3];
        }
        // CSS Values 3 §5.1.2: 4-character viewport units (vmin, vmax)
        else if (value.Length > 4 &&
                 (value.EndsWith(CssConstants.Vmin, StringComparison.OrdinalIgnoreCase) ||
                  value.EndsWith(CssConstants.Vmax, StringComparison.OrdinalIgnoreCase)))
        {
            number = value[..^4];
        }
        else if (value.Length > 2)
        {
            // CSS2.1 §4.3.2: Non-zero lengths require a valid unit identifier.
            var unit = value.Substring(value.Length - 2, 2);
            switch (unit)
            {
                case CssConstants.Em:
                case CssConstants.Ex:
                case CssConstants.Ch:
                case CssConstants.Ic:
                case CssConstants.Lh:
                case CssConstants.Px:
                case CssConstants.Mm:
                case CssConstants.Cm:
                case CssConstants.In:
                case CssConstants.Pt:
                case CssConstants.Pc:
                    number = value[..^2];
                    break;
                default:
                    // CSS Values 3 §5.1.2: 2-character viewport units (vh, vw)
                    if (unit.Equals(CssConstants.Vh, StringComparison.OrdinalIgnoreCase) ||
                        unit.Equals(CssConstants.Vw, StringComparison.OrdinalIgnoreCase))
                    {
                        number = value[..^2];
                        break;
                    }
                    return false; // unrecognized unit
            }
        }

        return double.TryParse(number, out _);
    }

    public static double ParseNumber(string number, double hundredPercent)
    {
        if (string.IsNullOrEmpty(number))
            return 0f;

        string toParse = number;
        bool isPercent = number.EndsWith('%');

        if (isPercent)
            toParse = number[..^1];

        if (!double.TryParse(toParse, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double result))
            return 0f;

        if (isPercent)
            result = result / 100f * hundredPercent;

        return result;
    }

    public static double ParseLength(string length, double hundredPercent, double emFactor, bool fontAdjust = false) => ParseLength(length, hundredPercent, emFactor, null, fontAdjust, false);
    public static double ParseLength(string length, double hundredPercent, double emFactor, string defaultUnit) => ParseLength(length, hundredPercent, emFactor, defaultUnit, false, false);
    public static double ParseLength(string length, double hundredPercent, double emFactor, string defaultUnit, bool fontAdjust, bool returnPoints, double? lineHeightFactor = null, double? rootLineHeightFactor = null)
    {
        //Return zero if no length specified, zero specified
        if (string.IsNullOrEmpty(length) || length == "0")
            return 0f;

        var computedLineHeightFactor = lineHeightFactor ?? (emFactor * 1.2);
        var computedRootLineHeightFactor = rootLineHeightFactor
            ?? (CssConstants.FontSize * (96.0 / 72.0) * 1.2);

        if (TryEvaluateLengthExpression(
                length,
                hundredPercent,
                emFactor,
                defaultUnit,
                fontAdjust,
                returnPoints,
                computedLineHeightFactor,
                computedRootLineHeightFactor,
                out var evaluated))
        {
            return evaluated;
        }

        length = NormalizeSingleValueLengthFunction(length);

        //If percentage, use ParseNumber
        if (length.EndsWith('%'))
            return ParseNumber(length, hundredPercent);

        //Get units of the length
        string unit = GetUnit(length, defaultUnit, out bool hasUnit);

        //Factor will depend on the unit
        double factor;
        //Number of the length
        int unitLen = unit == CssConstants.Rem || unit == CssConstants.Rlh ? 3 :
                      unit == CssConstants.Vmin || unit == CssConstants.Vmax ? 4 :
                      unit == CssConstants.Q ? 1 : 2;
        string number = hasUnit
            ? length[..^unitLen]
            : length;

        //TODO: Units behave different in paper and in screen!
        switch (unit)
        {
            case CssConstants.Em:
                factor = emFactor;
                break;
            case CssConstants.Rem:
                // rem is relative to root element font size (default 11pt)
                factor = CssConstants.FontSize * (96.0 / 72.0);
                break;
            case CssConstants.Ex:
                factor = emFactor / 2;
                break;
            case CssConstants.Ch:
                // Approximate 1ch as half an em so 16px monospace text resolves
                // to an 8px character advance in the current focused Phase 3 slice.
                factor = emFactor / 2;
                break;
            case CssConstants.Ic:
                // Approximate 1ic as 1em for the current focused Phase 3 slice.
                factor = emFactor;
                break;
            case CssConstants.Lh:
                factor = computedLineHeightFactor;
                break;
            case CssConstants.Px:
                factor = fontAdjust ? 72f / 96f : 1f; //TODO:a check support for hi dpi
                break;
            case CssConstants.Mm:
                factor = 3.779527559f; //3 pixels per millimeter
                break;
            case CssConstants.Cm:
                factor = 37.795275591f; //37 pixels per centimeter
                break;
            case CssConstants.In:
                factor = 96f; //96 pixels per inch
                break;
            case CssConstants.Pt:
                factor = 96f / 72f; // 1 point = 1/72 of inch

                if (returnPoints)
                {
                    return ParseNumber(number, hundredPercent);
                }

                break;
            case CssConstants.Pc:
                factor = 16f; // 1 pica = 12 points
                break;
            case CssConstants.Rlh:
                factor = computedRootLineHeightFactor;
                break;
            case CssConstants.Q:
                factor = 37.795275591f / 40f; // 1Q = 1/40 cm ≈ 0.945 px
                break;
            case CssConstants.Vh:
                // CSS Values 3 §5.1.2: 1vh = 1% of viewport height
                factor = _vhFactor;
                break;
            case CssConstants.Vw:
                // CSS Values 3 §5.1.2: 1vw = 1% of viewport width
                factor = _vwFactor;
                break;
            case CssConstants.Vmin:
                // CSS Values 3 §5.1.2: 1vmin = 1% of min(vw, vh)
                factor = _vminFactor;
                break;
            case CssConstants.Vmax:
                // CSS Values 3 §5.1.2: 1vmax = 1% of max(vw, vh)
                factor = _vmaxFactor;
                break;
            default:
                factor = 0f;
                break;
        }

        return factor * ParseNumber(number, hundredPercent);
    }

    private static bool TryEvaluateLengthExpression(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        out double result)
    {
        if (TryEvaluateLengthExpressionCore(
                expression,
                hundredPercent,
                emFactor,
                defaultUnit,
                fontAdjust,
                returnPoints,
                lineHeightFactor,
                rootLineHeightFactor,
                insideMathFunction: false,
                out var evaluation))
        {
            result = evaluation.Pixels;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryEvaluateLengthExpressionCore(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        bool insideMathFunction,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var current = expression.Trim();
        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedParens(current[1..^1]))
            current = current[1..^1].Trim();

        if (TryEvaluateMathFunction(
                current,
                hundredPercent,
                emFactor,
                defaultUnit,
                fontAdjust,
                returnPoints,
                lineHeightFactor,
                rootLineHeightFactor,
                out evaluation))
        {
            return true;
        }

        var additiveOperatorIndex = FindTopLevelAdditiveOperator(current);
        if (additiveOperatorIndex > 0)
        {
            if (!TryEvaluateLengthExpressionCore(
                    current[..additiveOperatorIndex],
                    hundredPercent,
                    emFactor,
                    defaultUnit,
                    fontAdjust,
                    returnPoints,
                    lineHeightFactor,
                    rootLineHeightFactor,
                    insideMathFunction: true,
                    out var left))
            {
                return false;
            }

            if (!TryEvaluateLengthExpressionCore(
                    current[(additiveOperatorIndex + 1)..],
                    hundredPercent,
                    emFactor,
                    defaultUnit,
                    fontAdjust,
                    returnPoints,
                    lineHeightFactor,
                    rootLineHeightFactor,
                    insideMathFunction: true,
                    out var right))
            {
                return false;
            }

            evaluation = new LengthEvaluation(
                current[additiveOperatorIndex] == '+'
                    ? left.Pixels + right.Pixels
                    : left.Pixels - right.Pixels,
                IsUnitless: false);
            return true;
        }

        return TryParseSimpleLength(
            current,
            hundredPercent,
            emFactor,
            defaultUnit,
            fontAdjust,
            returnPoints,
            lineHeightFactor,
            rootLineHeightFactor,
            insideMathFunction,
            out evaluation);
    }

    private static bool TryEvaluateMathFunction(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        if (string.IsNullOrWhiteSpace(expression) || expression[^1] != ')')
            return false;

        static bool StartsWithFunction(string value, string functionName)
            => value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase);

        if (StartsWithFunction(expression, "calc"))
        {
            var content = expression[5..^1];
            return HasBalancedParens(content) &&
                   TryEvaluateLengthExpressionCore(
                       content,
                       hundredPercent,
                       emFactor,
                       defaultUnit,
                       fontAdjust,
                       returnPoints,
                       lineHeightFactor,
                       rootLineHeightFactor,
                       insideMathFunction: true,
                       out evaluation);
        }

        if (!StartsWithFunction(expression, "min") && !StartsWithFunction(expression, "max"))
            return false;

        var isMax = StartsWithFunction(expression, "max");
        var argsContent = expression[4..^1];
        if (!HasBalancedParens(argsContent))
            return false;

        var parts = SplitTopLevelArguments(argsContent);
        if (parts.Count == 0)
            return false;

        double? candidate = null;
        foreach (var part in parts)
        {
            if (!TryEvaluateLengthExpressionCore(
                    part,
                    hundredPercent,
                    emFactor,
                    defaultUnit,
                    fontAdjust,
                    returnPoints,
                    lineHeightFactor,
                    rootLineHeightFactor,
                    insideMathFunction: true,
                    out var value) ||
                value.IsUnitless)
            {
                return false;
            }

            candidate = candidate.HasValue
                ? (isMax ? Math.Max(candidate.Value, value.Pixels) : Math.Min(candidate.Value, value.Pixels))
                : value.Pixels;
        }

        if (!candidate.HasValue)
            return false;

        evaluation = new LengthEvaluation(candidate.Value, IsUnitless: false);
        return true;
    }

    private static bool TryParseSimpleLength(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        bool insideMathFunction,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        var value = expression.Trim();
        if (string.IsNullOrEmpty(value))
            return false;

        if (value.EndsWith('%'))
        {
            evaluation = new LengthEvaluation(ParseNumber(value, hundredPercent), IsUnitless: false);
            return true;
        }

        string unit = GetUnit(value, defaultUnit, out bool hasUnit);
        if (!hasUnit)
        {
            if (insideMathFunction)
                return false;

            if (double.TryParse(value, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double raw))
            {
                evaluation = new LengthEvaluation(raw, IsUnitless: true);
                return true;
            }

            return false;
        }

        int unitLen = unit == CssConstants.Rem || unit == CssConstants.Rlh ? 3 :
                      unit == CssConstants.Vmin || unit == CssConstants.Vmax ? 4 :
                      unit == CssConstants.Q ? 1 : 2;
        string number = value[..^unitLen];
        if (!double.TryParse(number, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double parsedNumber))
            return false;

        double factor = unit switch
        {
            CssConstants.Em => emFactor,
            CssConstants.Rem => CssConstants.FontSize * (96.0 / 72.0),
            CssConstants.Ex => emFactor / 2,
            CssConstants.Ch => emFactor / 2,
            CssConstants.Ic => emFactor,
            CssConstants.Lh => lineHeightFactor,
            CssConstants.Px => fontAdjust ? 72f / 96f : 1f,
            CssConstants.Mm => 3.779527559f,
            CssConstants.Cm => 37.795275591f,
            CssConstants.In => 96f,
            CssConstants.Pt => returnPoints ? 1f : 96f / 72f,
            CssConstants.Pc => 16f,
            CssConstants.Rlh => rootLineHeightFactor,
            CssConstants.Q => 37.795275591f / 40f,
            CssConstants.Vh => _vhFactor,
            CssConstants.Vw => _vwFactor,
            CssConstants.Vmin => _vminFactor,
            CssConstants.Vmax => _vmaxFactor,
            _ => double.NaN
        };

        if (double.IsNaN(factor))
            return false;

        evaluation = new LengthEvaluation(
            unit == CssConstants.Pt && returnPoints
                ? ParseNumber(number, hundredPercent)
                : factor * parsedNumber,
            IsUnitless: false);
        return true;
    }

    private static int FindTopLevelAdditiveOperator(string expression)
    {
        var depth = 0;
        for (int i = expression.Length - 1; i >= 1; i--)
        {
            switch (expression[i])
            {
                case ')':
                    depth++;
                    break;
                case '(':
                    depth--;
                    break;
                case '+':
                case '-':
                    if (depth != 0)
                        break;

                    var leftIndex = i - 1;
                    while (leftIndex >= 0 && char.IsWhiteSpace(expression[leftIndex]))
                        leftIndex--;

                    var rightIndex = i + 1;
                    while (rightIndex < expression.Length && char.IsWhiteSpace(expression[rightIndex]))
                        rightIndex++;

                    if (leftIndex >= 0 &&
                        rightIndex < expression.Length &&
                        expression[leftIndex] != '(' &&
                        expression[leftIndex] != ',' &&
                        expression[leftIndex] != '+' &&
                        expression[leftIndex] != '-')
                    {
                        return i;
                    }
                    break;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevelArguments(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(value[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(value[start..].Trim());
        return parts;
    }

    private static string NormalizeSingleValueLengthFunction(string value)
    {
        var current = value.Trim();
        while (TryUnwrapSingleValueFunction(current, "calc", out var inner) ||
               TryUnwrapSingleValueFunction(current, "max", out inner) ||
               TryUnwrapSingleValueFunction(current, "min", out inner))
        {
            current = inner.Trim();
        }

        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedParens(current[1..^1]))
            current = current[1..^1].Trim();

        return current;
    }

    private static bool TryUnwrapSingleValueFunction(string value, string functionName, out string inner)
    {
        inner = string.Empty;
        if (!value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase) || value[^1] != ')')
            return false;

        var content = value[(functionName.Length + 1)..^1];
        if (!HasBalancedParens(content))
            return false;

        var depth = 0;
        foreach (var ch in content)
        {
            switch (ch)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return false;
            }
        }

        inner = content;
        return true;
    }

    private static bool HasBalancedParens(string value)
    {
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    private static string GetUnit(string length, string defaultUnit, out bool hasUnit)
    {
        // Check for 4-character units first (e.g. "vmin", "vmax")
        if (length.Length >= 5)
        {
            var last4 = length.Substring(length.Length - 4, 4);
            if (last4.Equals(CssConstants.Vmin, StringComparison.OrdinalIgnoreCase) ||
                last4.Equals(CssConstants.Vmax, StringComparison.OrdinalIgnoreCase))
            {
                hasUnit = true;
                return last4.ToLowerInvariant();
            }
        }

        // Check for 3-character units first (e.g. "rem")
        if (length.Length >= 4)
        {
            if (length.EndsWith(CssConstants.Rem, StringComparison.Ordinal))
            {
                hasUnit = true;
                return CssConstants.Rem;
            }

            if (length.EndsWith(CssConstants.Rlh, StringComparison.OrdinalIgnoreCase))
            {
                hasUnit = true;
                return CssConstants.Rlh;
            }
        }

        var unit = length.Length >= 3 ? length.Substring(length.Length - 2, 2) : string.Empty;
        switch (unit)
        {
            case CssConstants.Em:
            case CssConstants.Ex:
            case CssConstants.Ch:
            case CssConstants.Ic:
            case CssConstants.Lh:
            case CssConstants.Px:
            case CssConstants.Mm:
            case CssConstants.Cm:
            case CssConstants.In:
            case CssConstants.Pt:
            case CssConstants.Pc:
                hasUnit = true;
                break;
            default:
                // Check for 2-character viewport units (vh, vw)
                if (unit.Equals(CssConstants.Vh, StringComparison.OrdinalIgnoreCase) ||
                    unit.Equals(CssConstants.Vw, StringComparison.OrdinalIgnoreCase))
                {
                    hasUnit = true;
                    return unit.ToLowerInvariant();
                }
                // Check for single-character units (e.g. "Q" / "q")
                if (length.Length >= 2)
                {
                    char lastChar = char.ToLowerInvariant(length[^1]);
                    char prevChar = length[^2];
                    if (lastChar == 'q' && (char.IsDigit(prevChar) || prevChar == '.'))
                    {
                        hasUnit = true;
                        return CssConstants.Q;
                    }
                }
                hasUnit = false;
                unit = defaultUnit ?? string.Empty;
                break;
        }
        return unit;
    }

    public bool IsColorValid(string colorValue) => TryGetColor(colorValue, 0, colorValue.Length, out _);

    public Color GetActualColor(string colorValue)
    {
        TryGetColor(colorValue, 0, colorValue.Length, out Color color);
        return color;
    }

    public bool TryGetColor(string str, int idx, int length, out Color color)
    {
        try
        {
            if (!string.IsNullOrEmpty(str))
            {
                if (length > 1 && str[idx] == '#')
                {
                    return GetColorByHex(str, idx, length, out color);
                }
                else if (length > 10 && CommonUtils.SubStringEquals(str, idx, 4, "rgb(") && str[idx + length - 1] == ')')
                {
                    return GetColorByRgb(str, idx, length, out color);
                }
                else if (length > 13 && CommonUtils.SubStringEquals(str, idx, 5, "rgba(") && str[idx + length - 1] == ')')
                {
                    return GetColorByRgba(str, idx, length, out color);
                }
                else if (length > 9 && CommonUtils.SubStringEquals(str, idx, 4, "hsl(") && str[idx + length - 1] == ')')
                {
                    return GetColorByHsl(str, idx, length, out color);
                }
                else if (length > 12 && CommonUtils.SubStringEquals(str, idx, 5, "hsla(") && str[idx + length - 1] == ')')
                {
                    return GetColorByHsla(str, idx, length, out color);
                }
                else
                {
                    return GetColorByName(str, idx, length, out color);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HtmlRenderer] CssValueParser.TryGetColor failed: {ex.Message}");
        }
        
        color = Color.Black;
        return false;
    }

    public static double GetActualBorderWidth(string borderValue, double emHeight)
    {
        if (string.IsNullOrEmpty(borderValue))
            return GetActualBorderWidth(CssConstants.Medium, emHeight);

        return borderValue switch
        {
            CssConstants.Thin => (double)1f,
            CssConstants.Medium => (double)2f,
            CssConstants.Thick => (double)5f,
            _ => Math.Abs(ParseLength(borderValue, 1, emHeight)),
        };
    }

    private static bool GetColorByHex(string str, int idx, int length, out Color color)
    {
        int r = -1;
        int g = -1;
        int b = -1;
        int a = 255;

        if (length == 7)
        {
            r = ParseHexInt(str, idx + 1, 2);
            g = ParseHexInt(str, idx + 3, 2);
            b = ParseHexInt(str, idx + 5, 2);
        }
        else if (length == 4)
        {
            r = ParseHexInt(str, idx + 1, 1);
            r = r * 16 + r;
            g = ParseHexInt(str, idx + 2, 1);
            g = g * 16 + g;
            b = ParseHexInt(str, idx + 3, 1);
            b = b * 16 + b;
        }
        else if (length == 9)
        {
            // #RRGGBBAA — 8-digit hex with alpha (CSS Color Level 4)
            r = ParseHexInt(str, idx + 1, 2);
            g = ParseHexInt(str, idx + 3, 2);
            b = ParseHexInt(str, idx + 5, 2);
            a = ParseHexInt(str, idx + 7, 2);
        }
        else if (length == 5)
        {
            // #RGBA — 4-digit hex with alpha (CSS Color Level 4)
            r = ParseHexInt(str, idx + 1, 1);
            r = r * 16 + r;
            g = ParseHexInt(str, idx + 2, 1);
            g = g * 16 + g;
            b = ParseHexInt(str, idx + 3, 1);
            b = b * 16 + b;
            a = ParseHexInt(str, idx + 4, 1);
            a = a * 16 + a;
        }

        if (r > -1 && g > -1 && b > -1)
        {
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    private static bool GetColorByRgb(string str, int idx, int length, out Color color)
    {
        int r = -1;
        int g = -1;
        int b = -1;

        if (length > 10)
        {
            int s = idx + 4;
            r = ParseIntAtIndex(str, ref s);

            if (s < idx + length)
            {
                g = ParseIntAtIndex(str, ref s);
            }

            if (s < idx + length)
            {
                b = ParseIntAtIndex(str, ref s);
            }
        }

        if (r > -1 && g > -1 && b > -1)
        {
            color = Color.FromArgb(r, g, b);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    private static bool GetColorByRgba(string str, int idx, int length, out Color color)
    {
        int r = -1;
        int g = -1;
        int b = -1;
        int a = -1;

        if (length > 13)
        {
            int s = idx + 5;
            r = ParseIntAtIndex(str, ref s);

            if (s < idx + length)
            {
                g = ParseIntAtIndex(str, ref s);
            }
            if (s < idx + length)
            {
                b = ParseIntAtIndex(str, ref s);
            }
            if (s < idx + length)
            {
                // CSS Colors Level 4: the alpha value is a <number> between
                // 0 and 1 (e.g. 0.5) or an <integer> 0–255.  Parse it as a
                // float first, then normalise.
                a = ParseAlphaAtIndex(str, s, idx + length);
            }
        }

        if (r > -1 && g > -1 && b > -1 && a > -1)
        {
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    private static bool GetColorByHsl(string str, int idx, int length, out Color color)
    {
        try
        {
            var inner = str.Substring(idx + 4, length - idx - 5); // content between "hsl(" and ")"

            // CSS Color Level 4: support both comma-separated and space-separated syntax.
            // Comma: hsl(H, S%, L%)
            // Space: hsl(H S% L%) or hsl(H S% L% / A)
            var parts = inner.Split(',');
            if (parts.Length == 3)
            {
                var h = double.Parse(StripAngleUnit(parts[0].Trim()), CultureInfo.InvariantCulture);
                var sTrimmed = parts[1].Trim();
                var s = double.Parse(sTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
                if (sTrimmed.EndsWith('%')) s /= 100.0;
                var lTrimmed = parts[2].Trim();
                var l = double.Parse(lTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
                if (lTrimmed.EndsWith('%')) l /= 100.0;

                HslToRgb(h, s, l, out int r, out int g, out int b);
                color = Color.FromArgb(r, g, b);
                return true;
            }

            // Space-separated: hsl(H S% L%) or hsl(H S% L% / A)
            if (parts.Length == 1)
                return ParseSpaceSeparatedHsl(inner, out color);
        }
        catch { /* fall through */ }
        color = Color.Empty;
        return false;
    }

    private static bool GetColorByHsla(string str, int idx, int length, out Color color)
    {
        try
        {
            var inner = str.Substring(idx + 5, length - idx - 6); // content between "hsla(" and ")"

            // CSS Color Level 4: support both comma-separated and space-separated syntax.
            var parts = inner.Split(',');
            if (parts.Length == 4)
            {
                var h = double.Parse(StripAngleUnit(parts[0].Trim()), CultureInfo.InvariantCulture);
                var sTrimmed = parts[1].Trim();
                var s = double.Parse(sTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
                if (sTrimmed.EndsWith('%')) s /= 100.0;
                var lTrimmed = parts[2].Trim();
                var l = double.Parse(lTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
                if (lTrimmed.EndsWith('%')) l /= 100.0;
                var a = double.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);

                HslToRgb(h, s, l, out int r, out int g, out int b);
                int alpha = Math.Max(0, Math.Min(255, (int)Math.Round(a * 255)));
                color = Color.FromArgb(alpha, r, g, b);
                return true;
            }

            // Space-separated: hsla(H S% L% / A)
            if (parts.Length == 1)
                return ParseSpaceSeparatedHsl(inner, out color);
        }
        catch { /* fall through */ }
        color = Color.Empty;
        return false;
    }

    /// <summary>
    /// Parses CSS Color Level 4 space-separated hsl/hsla syntax:
    /// <c>H S% L%</c> or <c>H S% L% / A</c>.
    /// H may have a unit suffix (e.g. <c>deg</c>).
    /// A may be a number (0–1) or percentage.
    /// </summary>
    private static bool ParseSpaceSeparatedHsl(string inner, out Color color)
    {
        color = Color.Empty;
        try
        {
            double alpha = 1.0;
            string hslPart = inner;
            int slashIdx = inner.IndexOf('/');
            if (slashIdx >= 0)
            {
                hslPart = inner[..slashIdx].Trim();
                var alphaPart = inner[(slashIdx + 1)..].Trim();
                if (alphaPart.EndsWith('%'))
                {
                    alpha = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                }
                else
                {
                    alpha = double.Parse(alphaPart, CultureInfo.InvariantCulture);
                }
            }

            var tokens = hslPart.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) return false;

            // Strip angle-unit suffixes (deg, grad, rad, turn)
            var hStr = StripAngleUnit(tokens[0].Trim());
            var h = double.Parse(hStr, CultureInfo.InvariantCulture);

            var sTrimmed = tokens[1].Trim();
            var s = double.Parse(sTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
            if (sTrimmed.EndsWith('%')) s /= 100.0;

            var lTrimmed = tokens[2].Trim();
            var l = double.Parse(lTrimmed.TrimEnd('%'), CultureInfo.InvariantCulture);
            if (lTrimmed.EndsWith('%')) l /= 100.0;

            HslToRgb(h, s, l, out int r, out int g, out int b);
            int a = Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255)));
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Converts HSL color values to RGB.
    /// H is in degrees [0, 360), S and L are in [0, 1].
    /// </summary>
    private static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Max(0, Math.Min(1, s));
        l = Math.Max(0, Math.Min(1, l));

        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;

        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = Math.Max(0, Math.Min(255, (int)Math.Round((r1 + m) * 255)));
        g = Math.Max(0, Math.Min(255, (int)Math.Round((g1 + m) * 255)));
        b = Math.Max(0, Math.Min(255, (int)Math.Round((b1 + m) * 255)));
    }

    /// <summary>
    /// Strips CSS angle-unit suffixes (<c>deg</c>, <c>grad</c>, <c>rad</c>,
    /// <c>turn</c>) from a hue value string, returning just the number.
    /// </summary>
    private static string StripAngleUnit(string value)
    {
        foreach (var suffix in new[] { "deg", "grad", "rad", "turn" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return value[..^suffix.Length];
        }
        return value;
    }

    private bool GetColorByName(string str, int idx, int length, out Color color)
    {
        // CSS2.1 §4.3.6: 'transparent' is a valid color keyword that computes
        // to rgba(0,0,0,0).  Handle it before the general alpha check to avoid
        // rejecting it due to A == 0.
        if (length == 11 && string.Compare(str, idx, "transparent", 0, 11, StringComparison.OrdinalIgnoreCase) == 0)
        {
            color = Color.FromArgb(0, 0, 0, 0);
            return true;
        }

        color = _colorResolver.GetColor(str.Substring(idx, length));
        return color.A > 0;
    }

    private static int ParseIntAtIndex(string str, ref int startIdx)
    {
        int len = 0;

        while (char.IsWhiteSpace(str, startIdx))
            startIdx++;

        while (char.IsDigit(str, startIdx + len))
            len++;

        var val = ParseInt(str, startIdx, len);
        startIdx = startIdx + len + 1;

        return val;
    }

    /// <summary>
    /// Parses the alpha component of an rgba() color value.  The alpha can be
    /// an integer (0–255) or a float (0.0–1.0).  Returns an integer in the
    /// range [0, 255].
    /// </summary>
    private static int ParseAlphaAtIndex(string str, int startIdx, int endIdx)
    {
        // Skip leading whitespace and commas/separators.
        while (startIdx < endIdx && (char.IsWhiteSpace(str[startIdx]) || str[startIdx] == ','))
            startIdx++;

        // Collect the numeric portion (digits, '.', '-').
        int numEnd = startIdx;
        while (numEnd < endIdx && (char.IsDigit(str[numEnd]) || str[numEnd] == '.' || str[numEnd] == '-'))
            numEnd++;

        if (numEnd <= startIdx)
            return -1;

        var numStr = str[startIdx..numEnd];
        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return -1;

        // CSS Colors Level 4: values in [0, 1] are fractional alpha.
        // Values > 1 are treated as 0–255 integer alpha for backward compat.
        double normalized = (val >= 0 && val <= 1.0) ? val * 255 : val;
        return Math.Clamp((int)Math.Round(normalized), 0, 255);
    }

    private static int ParseInt(string str, int idx, int length)
    {
        if (length < 1)
            return -1;

        int num = 0;
        for (int i = 0; i < length; i++)
        {
            int c = str[idx + i];
            if (!(c >= 48 && c <= 57))
                return -1;

            num = num * 10 + c - 48;
        }

        return num;
    }

    private static int ParseHexInt(string str, int idx, int length)
    {
        if (length < 1)
            return -1;

        int num = 0;
        for (int i = 0; i < length; i++)
        {
            int c = str[idx + i];
            if (!(c >= 48 && c <= 57) && !(c >= 65 && c <= 70) && !(c >= 97 && c <= 102))
                return -1;

            num = num * 16 + (c <= 57 ? c - 48 : (10 + c - (c <= 70 ? 65 : 97)));
        }

        return num;
    }
}
