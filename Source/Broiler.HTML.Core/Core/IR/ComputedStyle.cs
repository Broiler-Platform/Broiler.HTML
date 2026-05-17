using System.Drawing;

namespace Broiler.HTML.Core.Core.IR;

/// <summary>
/// Resolved, typed CSS property values for a single element.
/// Produced by the style phase; consumed by layout and paint. Immutable once created.
/// </summary>
public sealed class ComputedStyle
{
    /// <summary>Semantic role of the element, derived from tag name during style resolution.</summary>
    public BoxKind Kind { get; init; } = BoxKind.Anonymous;

    /// <summary>
    /// Original HTML tag name (e.g. "html", "body", "div"), or <c>null</c>
    /// for anonymous boxes.  Used by canvas background propagation logic
    /// (CSS 2.1 §14.2) to identify the root and body elements.
    /// </summary>
    public string? TagName { get; init; }

    // --- Box model (raw CSS strings, matching CssBoxProperties conventions) ---

    public string Display { get; init; } = "inline";
    public string Position { get; init; } = "static";
    public string Float { get; init; } = "none";
    public string Clear { get; init; } = "none";
    public string Overflow { get; init; } = "visible";
    public string Visibility { get; init; } = "visible";
    public string Direction { get; init; } = "ltr";

    // --- Dimensions (raw CSS strings) ---

    public string Width { get; init; } = "auto";
    public string Height { get; init; } = "auto";
    public string MaxWidth { get; init; } = "none";

    // --- Computed dimensions (resolved device pixels) ---

    public double ActualWidth { get; init; }
    public double ActualHeight { get; init; }

    // --- Spacing (resolved device pixels) ---

    public BoxEdges Margin { get; init; } = BoxEdges.Zero;
    public BoxEdges Border { get; init; } = BoxEdges.Zero;
    public BoxEdges Padding { get; init; } = BoxEdges.Zero;

    // --- Corner radii ---

    public double ActualCornerNw { get; init; }
    public double ActualCornerNe { get; init; }
    public double ActualCornerSe { get; init; }
    public double ActualCornerSw { get; init; }
    public string CornerNwRadiusRaw { get; init; } = "0";
    public string CornerNeRadiusRaw { get; init; } = "0";
    public string CornerSeRadiusRaw { get; init; } = "0";
    public string CornerSwRadiusRaw { get; init; } = "0";

    // --- Typography ---

    public string FontFamily { get; init; } = string.Empty;
    public string FontSize { get; init; } = "medium";
    public string FontStyle { get; init; } = "normal";
    public string FontVariant { get; init; } = "normal";
    public string FontWeight { get; init; } = "normal";
    public string TextAlign { get; init; } = string.Empty;
    public string TextDecoration { get; init; } = string.Empty;
    public string TextDecorationStyle { get; init; } = "solid";
    public Color ActualTextDecorationColor { get; init; }
    public string WhiteSpace { get; init; } = "normal";
    public string WordBreak { get; init; } = "normal";
    public string VerticalAlign { get; init; } = "baseline";
    public double ActualLineHeight { get; init; }
    public double ActualTextIndent { get; init; }
    public double ActualWordSpacing { get; init; }

    // --- Colors ---

    public Color ActualColor { get; init; }
    public Color ActualBackgroundColor { get; init; }
    public Color ActualBackgroundGradient { get; init; }
    public double ActualBackgroundGradientAngle { get; init; }

    // --- Border colors ---

    public Color ActualBorderTopColor { get; init; }
    public Color ActualBorderRightColor { get; init; }
    public Color ActualBorderBottomColor { get; init; }
    public Color ActualBorderLeftColor { get; init; }

    // --- Border styles ---

    public string BorderTopStyle { get; init; } = "none";
    public string BorderRightStyle { get; init; } = "none";
    public string BorderBottomStyle { get; init; } = "none";
    public string BorderLeftStyle { get; init; } = "none";

    // --- Background ---

    public string BackgroundImage { get; init; } = "none";
    public string BackgroundPosition { get; init; } = "0% 0%";
    public string BackgroundRepeat { get; init; } = "repeat";
    public string BackgroundAttachment { get; init; } = "scroll";
    public string BackgroundOrigin { get; init; } = "padding-box";
    public string BackgroundSize { get; init; } = "auto";

    // --- List ---

    public string ListStyleType { get; init; } = "disc";
    public string ListStylePosition { get; init; } = "outside";
    public string ListStyleImage { get; init; } = string.Empty;
    public string ListStyle { get; init; } = string.Empty;

    // --- List attributes ---

    /// <summary>The <c>start</c> attribute of the parent <c>&lt;ol&gt;</c>, or null if not specified.</summary>
    public int? ListStart { get; init; }

    /// <summary>Whether the parent <c>&lt;ol&gt;</c> has the <c>reversed</c> attribute.</summary>
    public bool ListReversed { get; init; }

    // --- Image source ---

    /// <summary>The resolved <c>src</c> attribute for image elements, or null if not applicable.</summary>
    public string? ImageSource { get; init; }

    // --- Opacity ---

    public string Opacity { get; init; } = "1";

    // --- Compositing ---

    public string MixBlendMode { get; init; } = "normal";
    public string BackgroundBlendMode { get; init; } = "normal";
    public string Filter { get; init; } = "none";
    public string Isolation { get; init; } = "auto";
    public string BackgroundClip { get; init; } = "border-box";
    public string ClipPath { get; init; } = "none";

    /// <summary>
    /// CSS Containment Module Level 2: the <c>contain</c> property.
    /// Used for background propagation suppression when value includes <c>paint</c>.
    /// </summary>
    public string Contain { get; init; } = "none";
    public string Transform { get; init; } = "none";

    // --- Flex ---

    public string FlexDirection { get; init; } = "row";
    public string JustifyContent { get; init; } = "flex-start";
    public string AlignItems { get; init; } = "stretch";

    // --- Table ---

    public string BorderSpacing { get; init; } = "0";
    public string BorderCollapse { get; init; } = "separate";
    public string EmptyCells { get; init; } = "show";
    public double ActualBorderSpacingHorizontal { get; init; }
    public double ActualBorderSpacingVertical { get; init; }

    // --- Box shadow ---

    public string BoxShadow { get; init; } = "none";

    // --- Text shadow ---

    public string TextShadow { get; init; } = "none";

    // --- Positioning ---

    public string Left { get; init; } = "auto";
    public string Top { get; init; } = "auto";

    // --- Content ---

    public string Content { get; init; } = "normal";

    // --- Page ---

    public string PageBreakInside { get; init; } = "auto";
}
