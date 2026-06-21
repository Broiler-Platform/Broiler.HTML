using Broiler.HTML.Core.IR;
using Broiler.HTML.Dom;

namespace Broiler.HTML.Orchestration.Core.IR;

/// <summary>
/// Creates a <see cref="ComputedStyle"/> snapshot from a <see cref="CssBoxProperties"/> instance.
/// This factory captures the current lazy-parsed computed values.
/// </summary>
internal static class ComputedStyleBuilder
{
    /// <summary>
    /// Snapshots the computed style of a CssBox, capturing all resolved actual values.
    /// </summary>
    public static ComputedStyle FromBox(CssBoxProperties box, string? tagName = null)
    {
        return new ComputedStyle
        {
            // Phase 2: Element classification
            Kind = box.Kind,

            // HTML tag name for canvas background propagation (CSS 2.1 §14.2)
            TagName = tagName,

            // Box model
            Display = box.Display,
            Position = box.Position,
            Float = box.Float,
            Clear = box.Clear,
            Overflow = box.Overflow,
            Visibility = box.Visibility,
            Direction = box.Direction,

            // Dimensions (raw)
            Width = box.Width,
            Height = box.Height,
            MaxWidth = box.MaxWidth,

            // Computed dimensions
            ActualWidth = box.ActualWidth,
            ActualHeight = box.ActualHeight,

            // Spacing
            Margin = new BoxEdges(
                box.ActualMarginTop,
                box.ActualMarginRight,
                box.ActualMarginBottom,
                box.ActualMarginLeft),
            Border = new BoxEdges(
                box.ActualBorderTopWidth,
                box.ActualBorderRightWidth,
                box.ActualBorderBottomWidth,
                box.ActualBorderLeftWidth),
            Padding = new BoxEdges(
                box.ActualPaddingTop,
                box.ActualPaddingRight,
                box.ActualPaddingBottom,
                box.ActualPaddingLeft),

            // Corner radii
            ActualCornerNw = box.ActualCornerNw,
            ActualCornerNe = box.ActualCornerNe,
            ActualCornerSe = box.ActualCornerSe,
            ActualCornerSw = box.ActualCornerSw,
            CornerNwRadiusRaw = box.CornerNwRadius,
            CornerNeRadiusRaw = box.CornerNeRadius,
            CornerSeRadiusRaw = box.CornerSeRadius,
            CornerSwRadiusRaw = box.CornerSwRadius,

            // Typography
            FontFamily = box.FontFamily ?? string.Empty,
            FontSize = box.FontSize ?? "medium",
            FontStyle = box.FontStyle,
            FontVariant = box.FontVariant,
            FontWeight = box.FontWeight,
            TextAlign = box.TextAlign,
            TextDecoration = box.TextDecoration,
            TextDecorationStyle = box.TextDecorationStyle,
            ActualTextDecorationColor = box.ActualTextDecorationColor,
            WhiteSpace = box.WhiteSpace,
            WordBreak = box.WordBreak,
            VerticalAlign = box.VerticalAlign,
            ActualLineHeight = box.ActualLineHeight,
            ActualTextIndent = box.ActualTextIndent,
            ActualWordSpacing = box.ActualWordSpacing,

            // Colors
            ActualColor = box.ActualColor,
            ActualBackgroundColor = box.ActualBackgroundColor,
            ActualBackgroundGradient = box.ActualBackgroundGradient,
            ActualBackgroundGradientAngle = box.ActualBackgroundGradientAngle,

            // Border colors
            ActualBorderTopColor = box.ActualBorderTopColor,
            ActualBorderRightColor = box.ActualBorderRightColor,
            ActualBorderBottomColor = box.ActualBorderBottomColor,
            ActualBorderLeftColor = box.ActualBorderLeftColor,

            // Border styles
            BorderTopStyle = box.BorderTopStyle,
            BorderRightStyle = box.BorderRightStyle,
            BorderBottomStyle = box.BorderBottomStyle,
            BorderLeftStyle = box.BorderLeftStyle,

            // Background
            BackgroundImage = box.BackgroundImage,
            BackgroundPosition = box.BackgroundPosition,
            BackgroundRepeat = box.BackgroundRepeat,
            BackgroundAttachment = box.BackgroundAttachment,
            BackgroundOrigin = box.BackgroundOrigin,
            BackgroundSize = box.BackgroundSize,

            // List
            ListStyleType = box.ListStyleType,
            ListStylePosition = box.ListStylePosition,
            ListStyleImage = box.ListStyleImage,
            ListStyle = box.ListStyle,

            // Phase 2: List attributes
            ListStart = box.ListStart,
            ListReversed = box.ListReversed,

            // Phase 2: Image source
            ImageSource = box.ImageSource,

            // Opacity
            Opacity = box.Opacity,

            // Compositing
            MixBlendMode = box.MixBlendMode,
            BackgroundBlendMode = box.BackgroundBlendMode,
            Filter = box.Filter,
            Isolation = box.Isolation,
            BackgroundClip = box.BackgroundClip,
            ClipPath = box.ClipPath,
            Contain = box.Contain,
            Transform = box.Transform,

            // Flex
            FlexDirection = box.FlexDirection,
            JustifyContent = box.JustifyContent,
            AlignItems = box.AlignItems,

            // Table
            BorderSpacing = box.BorderSpacing,
            BorderCollapse = box.BorderCollapse,
            EmptyCells = box.EmptyCells,
            ActualBorderSpacingHorizontal = box.ActualBorderSpacingHorizontal,
            ActualBorderSpacingVertical = box.ActualBorderSpacingVertical,

            // Box shadow
            BoxShadow = box.BoxShadow,

            // Text shadow
            TextShadow = box.TextShadow,

            // Positioning
            Left = box.Left,
            Top = box.Top,

            // Content
            Content = box.Content,

            // Page
            PageBreakInside = box.PageBreakInside,
        };
    }
}
