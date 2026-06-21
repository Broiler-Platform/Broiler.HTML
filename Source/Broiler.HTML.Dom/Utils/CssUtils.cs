using System;
using System.Text.RegularExpressions;
using Broiler.HTML.Adapters;
using Broiler.HTML.CSS.Parse;
using Broiler.HTML.Utils;

namespace Broiler.HTML.Dom.Utils;

internal static class CssUtils
{
    private static readonly Regex LengthAttrFunctionPattern = new(
        @"attr\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+type\(\s*<length>\s*\)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static double WhiteSpace(RGraphics g, CssBoxProperties box)
    {
        double w = box.ActualFont.GetWhitespaceWidth(g);

        if (!(string.IsNullOrEmpty(box.WordSpacing) || box.WordSpacing == CssConstants.Normal))
            w += CssValueParser.ParseLength(box.WordSpacing, 0, box.GetEmHeight(), true);

        return w;
    }

    public static string GetPropertyValue(CssBox cssBox, string propName)
    {
        return propName switch
        {
            "border-bottom-width" => cssBox.BorderBottomWidth,
            "border-left-width" => cssBox.BorderLeftWidth,
            "border-right-width" => cssBox.BorderRightWidth,
            "border-top-width" => cssBox.BorderTopWidth,
            "border-bottom-style" => cssBox.BorderBottomStyle,
            "border-left-style" => cssBox.BorderLeftStyle,
            "border-right-style" => cssBox.BorderRightStyle,
            "border-top-style" => cssBox.BorderTopStyle,
            "border-bottom-color" => cssBox.BorderBottomColor,
            "border-left-color" => cssBox.BorderLeftColor,
            "border-right-color" => cssBox.BorderRightColor,
            "border-top-color" => cssBox.BorderTopColor,
            "border-spacing" => cssBox.BorderSpacing,
            "border-collapse" => cssBox.BorderCollapse,
            "corner-radius" => cssBox.CornerRadius,
            "border-radius" => cssBox.CornerRadius,
            "opacity" => cssBox.Opacity,
            "box-shadow" => cssBox.BoxShadow,
            "text-shadow" => cssBox.TextShadow,
            "flex-direction" => cssBox.FlexDirection,
            "justify-content" => cssBox.JustifyContent,
            "justify-items" => cssBox.JustifyItems,
            "align-items" => cssBox.AlignItems,
            "corner-nw-radius" => cssBox.CornerNwRadius,
            "corner-ne-radius" => cssBox.CornerNeRadius,
            "corner-se-radius" => cssBox.CornerSeRadius,
            "corner-sw-radius" => cssBox.CornerSwRadius,
            "margin-bottom" => cssBox.MarginBottom,
            "margin-left" => cssBox.MarginLeft,
            "margin-right" => cssBox.MarginRight,
            "margin-top" => cssBox.MarginTop,
            "margin-trim" => cssBox.MarginTrim,
            "padding-bottom" => cssBox.PaddingBottom,
            "padding-left" => cssBox.PaddingLeft,
            "padding-right" => cssBox.PaddingRight,
            "padding-top" => cssBox.PaddingTop,
            "page-break-inside" => cssBox.PageBreakInside,
            "left" => cssBox.Left,
            "top" => cssBox.Top,
            "width" => cssBox.Width,
            "inline-size" => cssBox.InlineSize,
            "max-width" => cssBox.MaxWidth,
            "min-width" => cssBox.MinWidth,
            "height" => cssBox.Height,
            "block-size" => cssBox.BlockSize,
            "max-height" => cssBox.MaxHeight,
            "min-height" => cssBox.MinHeight,
            "background-color" => cssBox.BackgroundColor,
            "background-image" => cssBox.BackgroundImage,
            "background-position" => cssBox.BackgroundPosition,
            "background-repeat" => cssBox.BackgroundRepeat,
            "background-attachment" => cssBox.BackgroundAttachment,
            "background-origin" => cssBox.BackgroundOrigin,
            "background-size" => cssBox.BackgroundSize,
            "background-gradient" => cssBox.BackgroundGradient,
            "background-gradient-angle" => cssBox.BackgroundGradientAngle,
            "content" => cssBox.Content,
            "color" => cssBox.Color,
            "display" => cssBox.Display,
            "direction" => cssBox.Direction,
            "empty-cells" => cssBox.EmptyCells,
            "float" => cssBox.Float,
            "clear" => cssBox.Clear,
            "position" => cssBox.Position,
            "line-height" => cssBox.LineHeight,
            "vertical-align" => cssBox.VerticalAlign,
            "text-indent" => cssBox.TextIndent,
            "text-align" => cssBox.TextAlign,
            "text-decoration" => cssBox.TextDecoration,
            "text-decoration-line" => cssBox.TextDecoration,
            "text-decoration-style" => cssBox.TextDecorationStyle,
            "text-decoration-color" => cssBox.TextDecorationColor,
            "white-space" => cssBox.WhiteSpace,
            "word-break" => cssBox.WordBreak,
            "visibility" => cssBox.Visibility,
            "word-spacing" => cssBox.WordSpacing,
            "font-family" => cssBox.FontFamily,
            "font-feature-settings" => cssBox.FontFeatureSettings,
            "font-variant-alternates" => cssBox.FontVariantAlternates,
            "font-size" => cssBox.FontSize,
            "font-style" => cssBox.FontStyle,
            "font-variant" => cssBox.FontVariant,
            "font-weight" => cssBox.FontWeight,
            "list-style" => cssBox.ListStyle,
            "list-style-position" => cssBox.ListStylePosition,
            "list-style-image" => cssBox.ListStyleImage,
            "list-style-type" => cssBox.ListStyleType,
            "overflow" => cssBox.Overflow,
            "box-sizing" => cssBox.BoxSizing,
            "clip-path" => cssBox.ClipPath,
            "transform" => cssBox.Transform,
            "align-content" => cssBox.AlignContent,
            "justify-self" => cssBox.JustifySelf,
            "align-self" => cssBox.AlignSelf,
            "unicode-bidi" => cssBox.UnicodeBidi,
            "writing-mode" => cssBox.WritingMode,
            "column-count" => cssBox.ColumnCount,
            "column-width" => cssBox.ColumnWidth,
            "column-fill" => cssBox.ColumnFill,
            "column-gap" => cssBox.ColumnGap,
            "break-inside" => cssBox.BreakInside,
            "grid-row" => cssBox.GridRow,
            "grid-column" => cssBox.GridColumn,
            "contain" => cssBox.Contain,
            _ => null,
        };
    }

    public static void SetPropertyValue(CssBox cssBox, string propName, string value)
    {
        value = ResolveLengthAttrFunctions(cssBox, value);

        if (propName.StartsWith("--", StringComparison.Ordinal))
        {
            cssBox.SetCustomProperty(propName, value);
            return;
        }

        switch (propName)
        {
            case "border-bottom-width":
                cssBox.BorderBottomWidth = value;
                break;
            case "border-left-width":
                cssBox.BorderLeftWidth = value;
                break;
            case "border-right-width":
                cssBox.BorderRightWidth = value;
                break;
            case "border-top-width":
                cssBox.BorderTopWidth = value;
                break;
            case "border-bottom-style":
                cssBox.BorderBottomStyle = value;
                break;
            case "border-left-style":
                cssBox.BorderLeftStyle = value;
                break;
            case "border-right-style":
                cssBox.BorderRightStyle = value;
                break;
            case "border-top-style":
                cssBox.BorderTopStyle = value;
                break;
            case "border-bottom-color":
                cssBox.BorderBottomColor = value;
                break;
            case "border-left-color":
                cssBox.BorderLeftColor = value;
                break;
            case "border-right-color":
                cssBox.BorderRightColor = value;
                break;
            case "border-top-color":
                cssBox.BorderTopColor = value;
                break;
            case "border-spacing":
                cssBox.BorderSpacing = value;
                break;
            case "border-collapse":
                cssBox.BorderCollapse = value;
                break;
            case "corner-radius":
                cssBox.CornerRadius = value;
                break;
            case "border-radius":
                cssBox.CornerRadius = value;
                break;
            case "opacity":
                cssBox.Opacity = value;
                break;
            case "mix-blend-mode":
                cssBox.MixBlendMode = value;
                break;
            case "background-blend-mode":
                cssBox.BackgroundBlendMode = value;
                break;
            case "filter":
                cssBox.Filter = value;
                break;
            case "isolation":
                cssBox.Isolation = value;
                break;
            case "box-sizing":
                cssBox.BoxSizing = value;
                break;
            case "background-clip":
                cssBox.BackgroundClip = value;
                break;
            case "clip-path":
                cssBox.ClipPath = value;
                break;
            case "transform":
                cssBox.Transform = value;
                break;
            case "box-shadow":
                cssBox.BoxShadow = value;
                break;
            case "text-shadow":
                cssBox.TextShadow = value;
                break;
            case "text-fill-color":
            case "-webkit-text-fill-color":
                cssBox.Color = value;
                break;
            case "flex-direction":
                cssBox.FlexDirection = value;
                break;
            case "justify-content":
                cssBox.JustifyContent = value;
                break;
            case "justify-items":
                cssBox.JustifyItems = value;
                break;
            case "align-items":
                cssBox.AlignItems = value;
                break;
            case "align-content":
                cssBox.AlignContent = value;
                break;
            case "justify-self":
                cssBox.JustifySelf = value;
                break;
            case "align-self":
                cssBox.AlignSelf = value;
                break;
            case "unicode-bidi":
                cssBox.UnicodeBidi = value;
                break;
            case "writing-mode":
                cssBox.WritingMode = value;
                break;
            case "column-count":
                cssBox.ColumnCount = value;
                break;
            case "column-width":
                cssBox.ColumnWidth = value;
                break;
            case "column-fill":
                cssBox.ColumnFill = value;
                break;
            case "column-gap":
                cssBox.ColumnGap = value;
                break;
            case "break-inside":
                cssBox.BreakInside = value;
                break;
            case "grid-row":
                cssBox.GridRow = value;
                break;
            case "grid-column":
                cssBox.GridColumn = value;
                break;
            case "contain":
                cssBox.Contain = value;
                break;
            case "columns":
                // CSS Multi-column §3: 'columns' is a shorthand for
                // 'column-width' and 'column-count'.  A bare integer
                // value sets column-count; a length sets column-width.
                var colParts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in colParts)
                {
                    if (part == "auto")
                        continue;
                    if (int.TryParse(part, out _))
                        cssBox.ColumnCount = part;
                    else
                        cssBox.ColumnWidth = part;
                }
                break;
            case "border-inline":
                ApplyLogicalBorderShorthand(cssBox, value, inlineAxis: true);
                break;
            case "border-block":
                ApplyLogicalBorderShorthand(cssBox, value, inlineAxis: false);
                break;
            case "border-inline-start-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "color", value);
                break;
            case "border-inline-end-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "color", value);
                break;
            case "border-block-start-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "color", value);
                break;
            case "border-block-end-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "color", value);
                break;
            case "border-inline-start-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "style", value);
                break;
            case "border-inline-end-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "style", value);
                break;
            case "border-block-start-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "style", value);
                break;
            case "border-block-end-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "style", value);
                break;
            case "border-inline-start-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "width", value);
                break;
            case "border-inline-end-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "width", value);
                break;
            case "border-block-start-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "width", value);
                break;
            case "border-block-end-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "width", value);
                break;
            case "corner-nw-radius":
                cssBox.CornerNwRadius = value;
                break;
            case "corner-ne-radius":
                cssBox.CornerNeRadius = value;
                break;
            case "corner-se-radius":
                cssBox.CornerSeRadius = value;
                break;
            case "corner-sw-radius":
                cssBox.CornerSwRadius = value;
                break;
            case "margin-bottom":
                cssBox.MarginBottom = value;
                break;
            case "margin-left":
                cssBox.MarginLeft = value;
                break;
            case "margin-right":
                cssBox.MarginRight = value;
                break;
            case "margin-top":
                cssBox.MarginTop = value;
                break;
            case "margin-trim":
                cssBox.MarginTrim = value;
                break;
            case "padding-bottom":
                cssBox.PaddingBottom = value;
                break;
            case "padding-left":
                cssBox.PaddingLeft = value;
                break;
            case "padding-right":
                cssBox.PaddingRight = value;
                break;
            case "padding-top":
                cssBox.PaddingTop = value;
                break;
            case "page-break-inside":
                cssBox.PageBreakInside = value;
                break;
            case "left":
                cssBox.Left = value;
                break;
            case "top":
                cssBox.Top = value;
                break;
            case "right":
                cssBox.Right = value;
                break;
            case "bottom":
                cssBox.Bottom = value;
                break;
            case "width":
                cssBox.Width = value;
                break;
            case "inline-size":
                cssBox.InlineSize = value;
                break;
            case "max-width":
                cssBox.MaxWidth = value;
                break;
            case "min-width":
                cssBox.MinWidth = value;
                break;
            case "height":
                cssBox.Height = value;
                break;
            case "block-size":
                cssBox.BlockSize = value;
                break;
            case "max-height":
                cssBox.MaxHeight = value;
                break;
            case "min-height":
                cssBox.MinHeight = value;
                break;
            case "background-color":
                cssBox.BackgroundColor = value;
                break;
            case "background-image":
                cssBox.BackgroundImage = value;
                break;
            case "background-position":
                cssBox.BackgroundPosition = value;
                break;
            case "background-repeat":
                cssBox.BackgroundRepeat = value;
                break;
            case "background-attachment":
                cssBox.BackgroundAttachment = value;
                break;
            case "background-origin":
                cssBox.BackgroundOrigin = value;
                break;
            case "background-size":
                cssBox.BackgroundSize = value;
                break;
            case "background-gradient":
                cssBox.BackgroundGradient = value;
                break;
            case "background-gradient-angle":
                cssBox.BackgroundGradientAngle = value;
                break;
            case "color":
                cssBox.Color = value;
                break;
            case "content":
                cssBox.Content = value;
                break;
            case "display":
                cssBox.Display = value;
                break;
            case "direction":
                cssBox.Direction = value;
                break;
            case "empty-cells":
                cssBox.EmptyCells = value;
                break;
            case "float":
                cssBox.Float = value;
                break;
            case "clear":
                cssBox.Clear = value;
                break;
            case "position":
                cssBox.Position = value;
                break;
            case "line-height":
                cssBox.LineHeight = value;
                break;
            case "vertical-align":
                cssBox.VerticalAlign = value;
                break;
            case "text-indent":
                cssBox.TextIndent = value;
                break;
            case "text-align":
                cssBox.TextAlign = value;
                break;
            case "text-decoration":
                ApplyTextDecorationShorthand(cssBox, value);
                break;
            case "text-decoration-line":
                cssBox.TextDecoration = value;
                break;
            case "text-decoration-style":
                cssBox.TextDecorationStyle = value;
                break;
            case "text-decoration-color":
                cssBox.TextDecorationColor = value;
                break;
            case "white-space":
                cssBox.WhiteSpace = value;
                break;
            case "word-break":
                cssBox.WordBreak = value;
                break;
            case "visibility":
                cssBox.Visibility = value;
                break;
            case "word-spacing":
                cssBox.WordSpacing = value;
                break;
            case "font-family":
                cssBox.FontFamily = value;
                break;
            case "font-feature-settings":
                cssBox.FontFeatureSettings = value;
                break;
            case "font-variant-alternates":
                cssBox.FontVariantAlternates = value;
                break;
            case "font-size":
                cssBox.FontSize = value;
                break;
            case "font-style":
                cssBox.FontStyle = value;
                break;
            case "font-variant":
                cssBox.FontVariant = value;
                break;
            case "font-weight":
                cssBox.FontWeight = value;
                break;
            case "list-style":
                cssBox.ListStyle = value;
                break;
            case "list-style-position":
                cssBox.ListStylePosition = value;
                break;
            case "list-style-image":
                cssBox.ListStyleImage = value;
                break;
            case "list-style-type":
                cssBox.ListStyleType = value;
                break;
            case "overflow":
                cssBox.Overflow = value;
                break;
            case "z-index":
                cssBox.ZIndex = value;
                break;
            case "animation-name":
                cssBox.AnimationName = value;
                break;
            case "animation-duration":
                cssBox.AnimationDuration = value;
                break;
            case "animation-timing-function":
                cssBox.AnimationTimingFunction = value;
                break;
            case "animation-delay":
                cssBox.AnimationDelay = value;
                break;
            case "animation-iteration-count":
                cssBox.AnimationIterationCount = value;
                break;
            case "animation-direction":
                cssBox.AnimationDirection = value;
                break;
            case "animation-fill-mode":
                cssBox.AnimationFillMode = value;
                break;
            case "animation-play-state":
                cssBox.AnimationPlayState = value;
                break;
        }
    }

    private static void ApplyLogicalBorderShorthand(CssBox cssBox, string value, bool inlineAxis)
    {
        var parts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        string? width = null;
        string? style = null;
        string? color = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "none" or "hidden" or "dotted" or "dashed" or "solid"
                or "double" or "groove" or "ridge" or "inset" or "outset")
            {
                style ??= part;
            }
            else if (lower is "thin" or "medium" or "thick" || CssValueParser.IsValidLength(part))
            {
                width ??= part;
            }
            else
            {
                color ??= part;
            }
        }

        if (width != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "width", width);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "width", width);
        }

        if (style != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "style", style);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "style", style);
        }

        if (color != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "color", color);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "color", color);
        }
    }

    private static void ApplyTextDecorationShorthand(CssBox cssBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            cssBox.TextDecoration = string.Empty;
            return;
        }

        // Shorthand application resets omitted longhands back to their initial values.
        cssBox.TextDecoration = "none";
        cssBox.TextDecorationStyle = "solid";
        cssBox.TextDecorationColor = "currentcolor";

        var parts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        string? line = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "underline" or "overline" or "line-through" or "none")
            {
                line = part;
            }
            else if (lower is "solid" or "double" or "dotted" or "dashed" or "wavy")
            {
                cssBox.TextDecorationStyle = part;
            }
            else
            {
                cssBox.TextDecorationColor = part;
            }
        }

        if (line != null)
            cssBox.TextDecoration = line;
    }

    private static void SetLogicalBorderComponent(
        CssBox cssBox,
        bool inlineAxis,
        bool startSide,
        string component,
        string value)
    {
        var side = ResolveLogicalBorderSide(cssBox, inlineAxis, startSide);
        switch (side, component)
        {
            case ("top", "color"):
                cssBox.BorderTopColor = value;
                break;
            case ("right", "color"):
                cssBox.BorderRightColor = value;
                break;
            case ("bottom", "color"):
                cssBox.BorderBottomColor = value;
                break;
            case ("left", "color"):
                cssBox.BorderLeftColor = value;
                break;
            case ("top", "style"):
                cssBox.BorderTopStyle = value;
                break;
            case ("right", "style"):
                cssBox.BorderRightStyle = value;
                break;
            case ("bottom", "style"):
                cssBox.BorderBottomStyle = value;
                break;
            case ("left", "style"):
                cssBox.BorderLeftStyle = value;
                break;
            case ("top", "width"):
                cssBox.BorderTopWidth = value;
                break;
            case ("right", "width"):
                cssBox.BorderRightWidth = value;
                break;
            case ("bottom", "width"):
                cssBox.BorderBottomWidth = value;
                break;
            case ("left", "width"):
                cssBox.BorderLeftWidth = value;
                break;
        }
    }

    private static string ResolveLogicalBorderSide(CssBox cssBox, bool inlineAxis, bool startSide)
    {
        var writingMode = cssBox.WritingMode?.ToLowerInvariant() ?? "horizontal-tb";
        var direction = cssBox.Direction?.ToLowerInvariant() ?? "ltr";

        if (inlineAxis)
        {
            return writingMode switch
            {
                "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr" => startSide ? "top" : "bottom",
                _ => startSide
                    ? (direction == "rtl" ? "right" : "left")
                    : (direction == "rtl" ? "left" : "right"),
            };
        }

        return writingMode switch
        {
            "vertical-rl" or "sideways-rl" => startSide ? "right" : "left",
            "vertical-lr" or "sideways-lr" => startSide ? "left" : "right",
            _ => startSide ? "top" : "bottom",
        };
    }

    private static string ResolveLengthAttrFunctions(CssBox cssBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return value;
        }

        return LengthAttrFunctionPattern.Replace(
            value,
            match =>
            {
                var attrName = match.Groups["name"].Value;
                var fallback = match.Groups["fallback"].Success
                    ? match.Groups["fallback"].Value.Trim()
                    : string.Empty;
                var attributeValue = cssBox.GetAttribute(attrName, string.Empty).Trim();

                if (!string.IsNullOrEmpty(attributeValue) &&
                    CssValueParser.IsValidLength(attributeValue))
                {
                    return attributeValue;
                }

                if (!string.IsNullOrEmpty(fallback) &&
                    CssValueParser.IsValidLength(fallback))
                {
                    return fallback;
                }

                return string.Empty;
            });
    }
}
