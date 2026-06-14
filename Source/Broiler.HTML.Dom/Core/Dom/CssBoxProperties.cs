using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.Core.Core.IR;
using Broiler.HTML.CSS.Core.Dom;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Core.Utils;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Broiler.HTML.Dom.Core.Dom;

internal abstract class CssBoxProperties : IBorderRenderData, IBackgroundRenderData
{
    internal const string InvalidCustomPropertySentinel = "\u0000";

    #region CSS Fields

    private string _borderTopWidth = "medium";
    private string _borderRightWidth = "medium";
    private string _borderBottomWidth = "medium";
    private string _borderLeftWidth = "medium";
    private string _borderTopColor = "black";
    private string _borderRightColor = "black";
    private string _borderBottomColor = "black";
    private string _borderLeftColor = "black";
    private string _bottom = "auto";
    private string _color = "black";
    private string _cornerRadius = "0";
    private string _fontSize = "medium";
    private string _left = "auto";
    private string _lineHeight = "normal";
    private string _paddingLeft = "0";
    private string _paddingBottom = "0";
    private string _paddingRight = "0";
    private string _paddingTop = "0";
    private string _right = "auto";
    private string _width = "auto";
    private string _height = "auto";
    private string _inlineSize = "auto";
    private string _blockSize = "auto";
    private string _writingMode = "horizontal-tb";
    private string _backgroundColor = "transparent";
    private string _backgroundImage = "none";
    private string _backgroundClip = "border-box";
    private string _clipPath = "none";
    private string _textIndent = "0";
    private string _textDecorationColor = "currentcolor";
    private string _top = "auto";
    private string _wordSpacing = "normal";

    #endregion


    #region Fields

    private PointF _location;
    private SizeF _size;

    private double _actualCornerNw = double.NaN;
    private double _actualCornerNe = double.NaN;
    private double _actualCornerSw = double.NaN;
    private double _actualCornerSe = double.NaN;
    private Color _actualColor = System.Drawing.Color.Empty;
    private double _actualBackgroundGradientAngle = double.NaN;
    private double _actualHeight = double.NaN;
    private double _actualWidth = double.NaN;
    private double _actualPaddingTop = double.NaN;
    private double _actualPaddingBottom = double.NaN;
    private double _actualPaddingRight = double.NaN;
    private double _actualPaddingLeft = double.NaN;
    private double _actualMarginTop = double.NaN;
    private double _collapsedMarginTop = double.NaN;
    private double _actualMarginBottom = double.NaN;
    private double _actualMarginRight = double.NaN;
    private double _actualMarginLeft = double.NaN;
    private double _actualBorderTopWidth = double.NaN;
    private double _actualBorderLeftWidth = double.NaN;
    private double _actualBorderBottomWidth = double.NaN;
    private double _actualBorderRightWidth = double.NaN;

    /// <summary>
    /// the width of whitespace between words
    /// </summary>
    private double _actualLineHeight = double.NaN;
    private double _actualTextIndent = double.NaN;
    private double _actualBorderSpacingHorizontal = double.NaN;
    private double _actualBorderSpacingVertical = double.NaN;
    private Color _actualBackgroundGradient = System.Drawing.Color.Empty;
    private Color _actualBorderTopColor = System.Drawing.Color.Empty;
    private Color _actualBorderLeftColor = System.Drawing.Color.Empty;
    private Color _actualBorderBottomColor = System.Drawing.Color.Empty;
    private Color _actualBorderRightColor = System.Drawing.Color.Empty;
    private Color _actualTextDecorationColor = System.Drawing.Color.Empty;
    private Color _actualBackgroundColor = System.Drawing.Color.Empty;
    private RFont _actualFont;

    #endregion


    #region CSS Properties

    public string BorderBottomWidth
    {
        get { return _borderBottomWidth; }
        set
        {
            _borderBottomWidth = value;
            _actualBorderBottomWidth = float.NaN;
        }
    }

    public string BorderLeftWidth
    {
        get { return _borderLeftWidth; }
        set
        {
            _borderLeftWidth = value;
            _actualBorderLeftWidth = float.NaN;
        }
    }

    public string BorderRightWidth
    {
        get { return _borderRightWidth; }
        set
        {
            _borderRightWidth = value;
            _actualBorderRightWidth = float.NaN;
        }
    }

    public string BorderTopWidth
    {
        get { return _borderTopWidth; }
        set
        {
            _borderTopWidth = value;
            _actualBorderTopWidth = float.NaN;
        }
    }

    private string _borderBottomStyle = "none";
    private string _borderLeftStyle = "none";
    private string _borderRightStyle = "none";
    private string _borderTopStyle = "none";

    /// <summary>CSS2.1 §8.5.3: Changing border-style affects the used border-width
    /// (style "none"/"hidden" forces width to zero), so invalidate the cached
    /// actual width whenever the style changes.</summary>
    public string BorderBottomStyle
    {
        get => _borderBottomStyle;
        set { _borderBottomStyle = value; _actualBorderBottomWidth = double.NaN; }
    }

    public string BorderLeftStyle
    {
        get => _borderLeftStyle;
        set { _borderLeftStyle = value; _actualBorderLeftWidth = double.NaN; }
    }

    public string BorderRightStyle
    {
        get => _borderRightStyle;
        set { _borderRightStyle = value; _actualBorderRightWidth = double.NaN; }
    }

    public string BorderTopStyle
    {
        get => _borderTopStyle;
        set { _borderTopStyle = value; _actualBorderTopWidth = double.NaN; }
    }

    public string BorderBottomColor
    {
        get { return ResolveCssVariables(_borderBottomColor); }
        set
        {
            _borderBottomColor = value;
            _actualBorderBottomColor = System.Drawing.Color.Empty;
        }
    }

    public string BorderLeftColor
    {
        get { return ResolveCssVariables(_borderLeftColor); }
        set
        {
            _borderLeftColor = value;
            _actualBorderLeftColor = System.Drawing.Color.Empty;
        }
    }

    public string BorderRightColor
    {
        get { return ResolveCssVariables(_borderRightColor); }
        set
        {
            _borderRightColor = value;
            _actualBorderRightColor = System.Drawing.Color.Empty;
        }
    }

    public string BorderTopColor
    {
        get { return ResolveCssVariables(_borderTopColor); }
        set
        {
            _borderTopColor = value;
            _actualBorderTopColor = System.Drawing.Color.Empty;
        }
    }

    public string BorderSpacing { get; set; } = "0";
    public string BorderCollapse { get; set; } = "separate";

    public string CornerRadius
    {
        get { return _cornerRadius; }
        set
        {
            string raw = value ?? string.Empty;
            int slashIndex = raw.IndexOf('/');
            if (slashIndex >= 0)
                raw = raw[..slashIndex];

            string[] r = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            switch (r.Length)
            {
                case 1:
                    CornerNeRadius = r[0];
                    CornerNwRadius = r[0];
                    CornerSeRadius = r[0];
                    CornerSwRadius = r[0];
                    break;
                case 2:
                    CornerNeRadius = r[0];
                    CornerNwRadius = r[0];
                    CornerSeRadius = r[1];
                    CornerSwRadius = r[1];
                    break;
                case 3:
                    CornerNeRadius = r[0];
                    CornerNwRadius = r[1];
                    CornerSeRadius = r[2];
                    break;
                case 4:
                    CornerNeRadius = r[0];
                    CornerNwRadius = r[1];
                    CornerSeRadius = r[2];
                    CornerSwRadius = r[3];
                    break;
            }

            _cornerRadius = value;
        }
    }

    public string CornerNwRadius { get; set; } = "0";
    public string CornerNeRadius { get; set; } = "0";
    public string CornerSeRadius { get; set; } = "0";
    public string CornerSwRadius { get; set; } = "0";
    public string MarginBottom { get; set; } = "0";
    public string MarginLeft { get; set; } = "0";
    public string MarginRight { get; set; } = "0";
    public string MarginTop { get; set; } = "0";

    /// <summary>
    /// CSS Box Model 4 §6.2 <c>margin-trim</c>: controls trimming of a box's
    /// own margins adjacent to its content edges (e.g. the block-start margin
    /// of the first child and the block-end margin of the last child).
    /// Not inherited.  Default <c>none</c>.
    /// </summary>
    public string MarginTrim { get; set; } = "none";

    public string PaddingBottom
    {
        get { return _paddingBottom; }
        set
        {
            _paddingBottom = value;
            _actualPaddingBottom = double.NaN;
        }
    }

    public string PaddingLeft
    {
        get { return _paddingLeft; }
        set
        {
            _paddingLeft = value;
            _actualPaddingLeft = double.NaN;
        }
    }

    public string PaddingRight
    {
        get { return _paddingRight; }
        set
        {
            _paddingRight = value;
            _actualPaddingRight = double.NaN;
        }
    }

    public string PaddingTop
    {
        get { return _paddingTop; }
        set
        {
            _paddingTop = value;
            _actualPaddingTop = double.NaN;
        }
    }

    public string PageBreakInside { get; set; } = CssConstants.Auto;

    public string Left
    {
        get { return _left; }
        set
        {
            _left = value;

            if (Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);
        }
    }

    public string Top
    {
        get { return _top; }
        set 
        {
            _top = value;

            if (Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);
        }
    }

    public string Right
    {
        get { return _right; }
        set { _right = value; }
    }

    public string Bottom
    {
        get { return _bottom; }
        set { _bottom = value; }
    }

    public string Width
    {
        get => ResolvePhysicalSize(_width, isWidth: true);
        set
        {
            _width = value;
            _actualWidth = double.NaN;
        }
    }
    public string MaxWidth { get; set; } = "none";
    public string MinWidth { get; set; } = "0";
    public string Height
    {
        get => ResolvePhysicalSize(_height, isWidth: false);
        set
        {
            _height = value;
            _actualHeight = double.NaN;
        }
    }
    public string MaxHeight { get; set; } = "none";
    public string MinHeight { get; set; } = "0";
    public string InlineSize
    {
        get => _inlineSize;
        set
        {
            _inlineSize = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }
    public string BlockSize
    {
        get => _blockSize;
        set
        {
            _blockSize = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }
    public string BackgroundColor
    {
        get => ResolveCssVariables(_backgroundColor);
        set
        {
            _backgroundColor = value;
            _actualBackgroundColor = System.Drawing.Color.Empty;
        }
    }
    public string BackgroundImage
    {
        get => ResolveCssVariables(_backgroundImage);
        set => _backgroundImage = value;
    }
    public string BackgroundPosition { get; set; } = "0% 0%";
    public string BackgroundRepeat { get; set; } = "repeat";
    public string BackgroundAttachment { get; set; } = "scroll";
    public string BackgroundOrigin { get; set; } = "padding-box";
    public string BackgroundSize { get; set; } = "auto";
    public string BackgroundGradient { get; set; } = "none";
    public string BackgroundGradientAngle { get; set; } = "90";

    // CSS Animations §3: Animation properties for static keyframe resolution.
    public string AnimationName { get; set; } = "none";
    public string AnimationDuration { get; set; } = "0s";
    public string AnimationTimingFunction { get; set; } = "ease";
    public string AnimationDelay { get; set; } = "0s";
    public string AnimationIterationCount { get; set; } = "1";
    public string AnimationDirection { get; set; } = "normal";
    public string AnimationFillMode { get; set; } = "none";
    public string AnimationPlayState { get; set; } = "running";

    public string Color
    {
        get { return ResolveCssVariables(_color); }
        set
        {
            _color = value;
            _actualColor = System.Drawing.Color.Empty;
        }
    }

    public string Content { get; set; } = "normal";
    public string Display { get; set; } = "inline";
    public string Direction { get; set; } = "ltr";
    public string EmptyCells { get; set; } = "show";
    public string Float { get; set; } = "none";
    public string Clear { get; set; } = "none";
    public string Position { get; set; } = "static";

    public string LineHeight
    {
        get { return _lineHeight; }
        set
        {
            // CSS2.1 §10.8: Preserve "normal" and "inherit" keywords as-is
            if (string.IsNullOrEmpty(value) || value == "normal" || value == "inherit")
            {
                _lineHeight = value ?? "normal";
                return;
            }

            // Unitless numbers (line-height: <number>) should be treated as a
            // multiplier of the element's font-size. Store as "Nem" so
            // ActualLineHeight resolves with the correct em factor at layout time,
            // avoiding precision loss from premature conversion at parse time
            // (CSS2.1 §10.8.1).
            if (!value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("ex", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("cm", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("mm", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("in", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("pc", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("%") &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                _lineHeight = value + "em";
                return;
            }

            // CSS2.1 §10.8: For explicit length values (px, em, pt, etc.),
            // store the raw value and let ActualLineHeight resolve it at
            // layout time when the element's font-size is finalized.
            _lineHeight = value;
        }
    }

    public string VerticalAlign { get; set; } = "baseline";

    public string TextIndent
    {
        get { return _textIndent; }
        set { _textIndent = value; }
    }

    public string TextAlign { get; set; } = string.Empty;
    public string TextDecoration { get; set; } = string.Empty;
    public string TextDecorationStyle { get; set; } = "solid";
    public string TextDecorationColor
    {
        get => _textDecorationColor;
        set
        {
            _textDecorationColor = value;
            _actualTextDecorationColor = System.Drawing.Color.Empty;
        }
    }
    public string WhiteSpace { get; set; } = "normal";
    public string Visibility { get; set; } = "visible";

    public string WordSpacing
    {
        get { return _wordSpacing; }
        set { _wordSpacing = value; }
    }

    public string WordBreak { get; set; } = "normal";
    public string Opacity { get; set; } = "1";
    public string ZIndex { get; set; } = CssConstants.Auto;
    public string BoxShadow { get; set; } = "none";
    public string TextShadow { get; set; } = "none";
    public string MixBlendMode { get; set; } = "normal";
    public string BackgroundBlendMode { get; set; } = "normal";
    public string Filter { get; set; } = "none";
    public string Isolation { get; set; } = "auto";
    public string BoxSizing { get; set; } = "content-box";

    public string BackgroundClip
    {
        get
        {
            if (_backgroundClip.Equals("inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null)
                return GetParent().BackgroundClip;

            return _backgroundClip;
        }
        set
        {
            _backgroundClip = value ?? "border-box";
        }
    }

    public string ClipPath
    {
        get => _clipPath;
        set => _clipPath = value ?? "none";
    }

    /// <summary>
    /// CSS Containment Module Level 2: the <c>contain</c> property.
    /// Values include <c>none</c>, <c>strict</c>, <c>content</c>,
    /// <c>size</c>, <c>layout</c>, <c>style</c>, <c>paint</c>,
    /// or a space-separated combination of the last four keywords.
    /// Used by background propagation (CSS Backgrounds §2.11.1):
    /// <c>contain: paint</c> on html or body suppresses canvas
    /// background propagation.
    /// </summary>
    public string Contain { get; set; } = "none";
    public string Transform { get; set; } = "none";
    public string FlexDirection { get; set; } = "row";
    public string JustifyContent { get; set; } = "flex-start";
    public string JustifyItems { get; set; } = "normal";
    public string AlignItems { get; set; } = "stretch";
    public string AlignContent { get; set; } = "normal";
    public string JustifySelf { get; set; } = "auto";
    public string AlignSelf { get; set; } = "auto";
    public string UnicodeBidi { get; set; } = "normal";
    public string WritingMode
    {
        get => _writingMode;
        set
        {
            _writingMode = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }
    public string ColumnCount { get; set; } = "auto";
    public string ColumnWidth { get; set; } = "auto";
    public string ColumnFill { get; set; } = "balance";
    public string BreakInside { get; set; } = "auto";
    public string GridRow { get; set; } = "auto";
    public string GridColumn { get; set; } = "auto";
    public string FontFamily { get; set; }

    /// <summary>
    /// Raw CSS <c>font-feature-settings</c> value (e.g. <c>"ss05" on, "liga" off</c>),
    /// inherited.  Resolved to enabled OpenType feature tags by
    /// <see cref="GetEnabledFontFeatureTags"/>.
    /// </summary>
    public string FontFeatureSettings { get; set; }

    /// <summary>
    /// Raw CSS <c>font-variant-alternates</c> value (e.g.
    /// <c>styleset(crossed-doubleu)</c>), inherited.  Resolved against
    /// <c>@font-feature-values</c> into concrete feature tags after the cascade.
    /// </summary>
    public string FontVariantAlternates { get; set; }

    /// <summary>
    /// Parses <see cref="FontFeatureSettings"/> into a space-separated list of
    /// the OpenType feature tags that are switched on, or <c>null</c> when none.
    /// </summary>
    protected string GetEnabledFontFeatureTags()
    {
        string value = FontFeatureSettings;
        if (string.IsNullOrWhiteSpace(value) || value == "normal")
            return null;

        var tags = new System.Text.StringBuilder();
        foreach (var part in value.Split(','))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;

            // "<tag>" [ <integer> | on | off ]; a 4-char quoted tag, optionally
            // followed by an on/off/value flag (default = on).
            int firstQuote = item.IndexOf('"');
            int altQuote = item.IndexOf('\'');
            char quote = firstQuote >= 0 ? '"' : (altQuote >= 0 ? '\'' : '\0');
            string tag;
            string flag;
            if (quote != '\0')
            {
                int start = item.IndexOf(quote);
                int endq = item.IndexOf(quote, start + 1);
                if (endq <= start)
                    continue;
                tag = item.Substring(start + 1, endq - start - 1).Trim();
                flag = item.Substring(endq + 1).Trim();
            }
            else
            {
                var sp = item.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                tag = sp[0];
                flag = sp.Length > 1 ? sp[1].Trim() : string.Empty;
            }

            if (tag.Length != 4)
                continue;

            bool enabled = flag.Length == 0
                || flag.Equals("on", StringComparison.OrdinalIgnoreCase)
                || flag == "1"
                || (int.TryParse(flag, out int v) && v != 0);
            if (enabled)
            {
                if (tags.Length > 0)
                    tags.Append(' ');
                tags.Append(tag);
            }
        }

        return tags.Length > 0 ? tags.ToString() : null;
    }

    public string FontSize
    {
        get { return _fontSize; }
        set
        {
            // CSS2.1 §6.2.1: 'inherit' resolves to the parent's computed value.
            if (value != null && value.Equals("inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null)
            {
                _fontSize = GetParent().FontSize;
                InvalidateFontDependentValues();
                return;
            }

            // CSS2.1 §15.7: a percentage font-size resolves against the PARENT's
            // computed font-size.  Resolve it to an absolute length immediately so
            // that descendants which inherit this computed value (InheritStyle copies
            // the string verbatim) do not re-apply the percentage and compound it —
            // e.g. body/div/span all set to 800% must each be 8× the root, not 8×8×8×.
            var trimmedValue = value?.Trim();
            if (trimmedValue != null
                && trimmedValue.EndsWith('%')
                && GetParent() != null
                && double.TryParse(trimmedValue[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                double resolvedPoints = CssValueParser.ParseNumber(trimmedValue, GetParent().ActualFont.Size);
                _fontSize = resolvedPoints.ToString("0.0###", CultureInfo.InvariantCulture) + "pt";
                InvalidateFontDependentValues();
                if (this is CssBox percentBox)
                {
                    foreach (var child in percentBox.Boxes)
                        child.InvalidateFontDependentSubtree();
                }
                return;
            }

            string length = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), value);

            if (length != null)
            {
                string computedValue;
                CssLength len = new(length);

                if (len.HasError)
                {
                    computedValue = "medium";
                }
                else if (len.Unit == CssUnit.Ems && GetParent() != null)
                {
                    computedValue = len.ConvertEmToPoints(GetParent().ActualFont.Size).ToString();
                }
                else
                {
                    computedValue = len.ToString();
                }

                _fontSize = computedValue;
            }
            else
            {
                _fontSize = value;
            }

            InvalidateFontDependentValues();
            if (this is CssBox cssBox)
            {
                foreach (var child in cssBox.Boxes)
                    child.InvalidateFontDependentSubtree();
            }
        }
    }

    public string FontStyle { get; set; } = "normal";
    public string FontVariant { get; set; } = "normal";
    public string FontWeight { get; set; } = "normal";
    public string ListStyle { get; set; } = string.Empty;
    public string Overflow { get; set; } = "visible";
    public string ListStylePosition { get; set; } = "outside";
    public string ListStyleImage { get; set; } = string.Empty;
    public string ListStyleType { get; set; } = "disc";

    /// <summary>Semantic role of the element, set during style resolution from tag name.</summary>
    public BoxKind Kind { get; set; } = BoxKind.Anonymous;

    /// <summary>The <c>start</c> attribute of an <c>&lt;ol&gt;</c>, or null if not specified.</summary>
    public int? ListStart { get; set; }

    /// <summary>Whether an <c>&lt;ol&gt;</c> has the <c>reversed</c> attribute.</summary>
    public bool ListReversed { get; set; }

    /// <summary>The resolved <c>src</c> attribute for image elements, or null if not applicable.</summary>
    public string? ImageSource { get; set; }

    #endregion CSS Properties

    public PointF Location
    {
        get
        {
            if (_location.IsEmpty && Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);

            return _location;
        }
        set
        {
            _location = value;
        }
    }

    public SizeF Size
    {
        get { return _size; }
        set { _size = value; }
    }

    public RectangleF Bounds => new(Location, Size);

    public double AvailableWidth => Size.Width - ActualBorderLeftWidth - ActualPaddingLeft - ActualPaddingRight - ActualBorderRightWidth;

    public double ActualRight
    {
        get { return Location.X + Size.Width; }
        set { Size = new SizeF((float)(value - Location.X), Size.Height); }
    }

    public double ActualBottom
    {
        get { return Location.Y + Size.Height; }
        set { Size = new SizeF(Size.Width, (float)(value - Location.Y)); }
    }

    public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;
    public double ClientTop => Location.Y + ActualBorderTopWidth + ActualPaddingTop;
    public double ClientRight => ActualRight - ActualPaddingRight - ActualBorderRightWidth;
    public double ClientBottom => ActualBottom - ActualPaddingBottom - ActualBorderBottomWidth;
    public RectangleF ClientRectangle => RectangleF.FromLTRB((float)ClientLeft, (float)ClientTop, (float)ClientRight, (float)ClientBottom);

    public double ActualHeight
    {
        get
        {
            if (double.IsNaN(_actualHeight))
            {
                _actualHeight = string.Equals(Height, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                    ? GetParent().ActualHeight
                    : ParseLengthWithLineHeight(Height, Size.Height);
            }

            return _actualHeight;
        }
    }

    public double ActualWidth
    {
        get
        {
            if (double.IsNaN(_actualWidth))
            {
                _actualWidth = string.Equals(Width, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                    ? GetParent().ActualWidth
                    : ParseLengthWithLineHeight(Width, Size.Width);
            }

            return _actualWidth;
        }
    }

    public double ActualPaddingTop
    {
        get
        {
            if (double.IsNaN(_actualPaddingTop))
                _actualPaddingTop = ParseLengthWithLineHeight(PaddingTop, Size.Width);

            return _actualPaddingTop;
        }
    }

    public double ActualPaddingLeft
    {
        get
        {
            if (double.IsNaN(_actualPaddingLeft))
                _actualPaddingLeft = ParseLengthWithLineHeight(PaddingLeft, Size.Width);

            return _actualPaddingLeft;
        }
    }

    public double ActualPaddingBottom
    {
        get
        {
            if (double.IsNaN(_actualPaddingBottom))
                _actualPaddingBottom = ParseLengthWithLineHeight(PaddingBottom, Size.Width);

            return _actualPaddingBottom;
        }
    }

    public double ActualPaddingRight
    {
        get
        {
            if (double.IsNaN(_actualPaddingRight))
                _actualPaddingRight = ParseLengthWithLineHeight(PaddingRight, Size.Width);

            return _actualPaddingRight;
        }
    }

    public double ActualMarginTop
    {
        get
        {
            if (double.IsNaN(_actualMarginTop))
            {
                if (MarginTop == CssConstants.Auto)
                    MarginTop = "0";

                var actualMarginTop = ParseLengthWithLineHeight(MarginTop, Size.Width);

                if (MarginTop.EndsWith("%"))
                    return actualMarginTop;

                _actualMarginTop = actualMarginTop;
            }

            return _actualMarginTop;
        }
    }

    public double CollapsedMarginTop
    {
        get { return double.IsNaN(_collapsedMarginTop) ? 0 : _collapsedMarginTop; }
        set { _collapsedMarginTop = value; }
    }

    public double ActualMarginLeft
    {
        get
        {
            if (double.IsNaN(_actualMarginLeft))
            {
                if (MarginLeft == CssConstants.Auto)
                    MarginLeft = "0";

                var actualMarginLeft = ParseLengthWithLineHeight(MarginLeft, Size.Width);

                if (MarginLeft.EndsWith("%"))
                    return actualMarginLeft;

                _actualMarginLeft = actualMarginLeft;
            }
            return _actualMarginLeft;
        }
    }

    public double ActualMarginBottom
    {
        get
        {
            if (double.IsNaN(_actualMarginBottom))
            {
                if (MarginBottom == CssConstants.Auto)
                    MarginBottom = "0";

                var actualMarginBottom = ParseLengthWithLineHeight(MarginBottom, Size.Width);

                if (MarginBottom.EndsWith("%"))
                    return actualMarginBottom;

                _actualMarginBottom = actualMarginBottom;
            }

            return _actualMarginBottom;
        }
    }

    public double ActualMarginRight
    {
        get
        {
            if (double.IsNaN(_actualMarginRight))
            {
                if (MarginRight == CssConstants.Auto)
                    MarginRight = "0";

                var actualMarginRight = ParseLengthWithLineHeight(MarginRight, Size.Width);
                
                if (MarginRight.EndsWith("%"))
                    return actualMarginRight;
                
                _actualMarginRight = actualMarginRight;
            }
            
            return _actualMarginRight;
        }
    }

    public double ActualBorderTopWidth
    {
        get
        {
            if (double.IsNaN(_actualBorderTopWidth))
            {
                _actualBorderTopWidth = CssValueParser.GetActualBorderWidth(BorderTopWidth, GetEmHeight());

                if (string.IsNullOrEmpty(BorderTopStyle) || BorderTopStyle == CssConstants.None)
                    _actualBorderTopWidth = 0f;
            }

            return _actualBorderTopWidth;
        }
    }

    public double ActualBorderLeftWidth
    {
        get
        {
            if (double.IsNaN(_actualBorderLeftWidth))
            {
                _actualBorderLeftWidth = CssValueParser.GetActualBorderWidth(BorderLeftWidth, GetEmHeight());

                if (string.IsNullOrEmpty(BorderLeftStyle) || BorderLeftStyle == CssConstants.None)
                    _actualBorderLeftWidth = 0f;
            }

            return _actualBorderLeftWidth;
        }
    }

    public double ActualBorderBottomWidth
    {
        get
        {
            if (double.IsNaN(_actualBorderBottomWidth))
            {
                _actualBorderBottomWidth = CssValueParser.GetActualBorderWidth(BorderBottomWidth, GetEmHeight());

                if (string.IsNullOrEmpty(BorderBottomStyle) || BorderBottomStyle == CssConstants.None)
                    _actualBorderBottomWidth = 0f;
            }

            return _actualBorderBottomWidth;
        }
    }

    public double ActualBorderRightWidth
    {
        get
        {
            if (double.IsNaN(_actualBorderRightWidth))
            {
                _actualBorderRightWidth = CssValueParser.GetActualBorderWidth(BorderRightWidth, GetEmHeight());

                if (string.IsNullOrEmpty(BorderRightStyle) || BorderRightStyle == CssConstants.None)
                    _actualBorderRightWidth = 0f;
            }

            return _actualBorderRightWidth;
        }
    }

    public Color ActualBorderTopColor
    {
        get
        {
            if (_actualBorderTopColor.IsEmpty)
                _actualBorderTopColor = GetActualColor(BorderTopColor);

            return _actualBorderTopColor;
        }
    }

    protected abstract PointF GetActualLocation(string X, string Y);

    protected abstract Color GetActualColor(string colorStr);

    protected virtual bool TryGetCustomPropertyValue(string propertyName, out string value)
    {
        value = string.Empty;
        return false;
    }

    private string ResolveCssVariables(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0)
            return value;

        string resolved = value;
        for (int i = 0; i < 8 && resolved.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0; i++)
        {
            resolved = Regex.Replace(
                resolved,
                @"var\(\s*(--[A-Za-z0-9_-]+)\s*(?:,\s*([^)]+))?\)",
                match =>
                {
                    var propertyName = match.Groups[1].Value;
                    if (TryGetCustomPropertyValue(propertyName, out var propertyValue))
                    {
                        if (propertyValue == InvalidCustomPropertySentinel)
                            return match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;

                        return propertyValue;
                    }

                    return match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                },
                RegexOptions.IgnoreCase);
        }

        return resolved;
    }

    public Color ActualBorderLeftColor
    {
        get
        {
            if (_actualBorderLeftColor.IsEmpty)
                _actualBorderLeftColor = GetActualColor(BorderLeftColor);

            return _actualBorderLeftColor;
        }
    }

    public Color ActualBorderBottomColor
    {
        get
        {
            if (_actualBorderBottomColor.IsEmpty)
                _actualBorderBottomColor = GetActualColor(BorderBottomColor);

            return _actualBorderBottomColor;
        }
    }

    public Color ActualBorderRightColor
    {
        get
        {
            if (_actualBorderRightColor.IsEmpty)
                _actualBorderRightColor = GetActualColor(BorderRightColor);

            return _actualBorderRightColor;
        }
    }

    public Color ActualTextDecorationColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TextDecorationColor) ||
                TextDecorationColor.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            {
                return ActualColor;
            }

            if (_actualTextDecorationColor.IsEmpty)
                _actualTextDecorationColor = GetActualColor(TextDecorationColor);

            return _actualTextDecorationColor;
        }
    }

    private string ResolvePhysicalSize(string explicitPhysicalValue, bool isWidth)
    {
        if (HasExplicitSize(explicitPhysicalValue))
            return explicitPhysicalValue;

        bool vertical = IsVerticalWritingMode(WritingMode);
        var logicalValue = isWidth
            ? (vertical ? BlockSize : InlineSize)
            : (vertical ? InlineSize : BlockSize);

        return HasExplicitSize(logicalValue) ? logicalValue : explicitPhysicalValue;
    }

    private static bool HasExplicitSize(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Equals("auto", StringComparison.OrdinalIgnoreCase);

    internal static bool IsVerticalWritingMode(string? writingMode)
    {
        var normalized = writingMode?.Trim().ToLowerInvariant();
        return normalized is "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr";
    }

    private double ParseCornerRadius(string radius)
    {
        double basis = radius != null && radius.Contains('%', StringComparison.Ordinal)
            ? Math.Max(0, Size.Width)
            : 0;

        return CssValueParser.ParseLength(radius, basis, GetEmHeight());
    }

    public double ActualCornerNw
    {
        get
        {
            if (CornerNwRadius != null && CornerNwRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerNwRadius);

            if (double.IsNaN(_actualCornerNw))
                _actualCornerNw = ParseCornerRadius(CornerNwRadius);

            return _actualCornerNw;
        }
    }

    public double ActualCornerNe
    {
        get
        {
            if (CornerNeRadius != null && CornerNeRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerNeRadius);

            if (double.IsNaN(_actualCornerNe))
                _actualCornerNe = ParseCornerRadius(CornerNeRadius);

            return _actualCornerNe;
        }
    }

    public double ActualCornerSe
    {
        get
        {
            if (CornerSeRadius != null && CornerSeRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerSeRadius);

            if (double.IsNaN(_actualCornerSe))
                _actualCornerSe = ParseCornerRadius(CornerSeRadius);

            return _actualCornerSe;
        }
    }

    public double ActualCornerSw
    {
        get
        {
            if (CornerSwRadius != null && CornerSwRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerSwRadius);

            if (double.IsNaN(_actualCornerSw))
                _actualCornerSw = ParseCornerRadius(CornerSwRadius);

            return _actualCornerSw;
        }
    }

    public bool IsRounded => ActualCornerNe > 0f || ActualCornerNw > 0f || ActualCornerSe > 0f || ActualCornerSw > 0f;

    /// <summary>
    /// Whether geometry anti-aliasing should be avoided. Returns false by
    /// default; subclasses may override to provide container-specific behavior.
    /// </summary>
    public virtual bool AvoidGeometryAntialias => false;

    public double ActualWordSpacing { get; private set; } = double.NaN;

    public Color ActualColor
    {
        get
        {
            if (_actualColor.IsEmpty)
                _actualColor = GetActualColor(Color);

            return _actualColor;
        }
    }

    public Color ActualBackgroundColor
    {
        get
        {
            if (_actualBackgroundColor.IsEmpty)
                _actualBackgroundColor = GetActualColor(BackgroundColor);

            return _actualBackgroundColor;
        }
    }

    public Color ActualBackgroundGradient
    {
        get
        {
            if (_actualBackgroundGradient.IsEmpty)
            {
                // "none" is the initial value and means no gradient; resolve to
                // fully-transparent so callers can simply check A > 0.  Without
                // this guard, GetActualColor("none") falls back to Color.Black
                // (opaque), which would cause EmitBackground to paint unintended
                // black fills.
                if (string.IsNullOrEmpty(BackgroundGradient) ||
                    string.Equals(BackgroundGradient, "none", StringComparison.OrdinalIgnoreCase))
                    _actualBackgroundGradient = System.Drawing.Color.FromArgb(0, 0, 0, 0);
                else
                    _actualBackgroundGradient = GetActualColor(BackgroundGradient);
            }

            return _actualBackgroundGradient;
        }
    }

    public double ActualBackgroundGradientAngle
    {
        get
        {
            if (double.IsNaN(_actualBackgroundGradientAngle))
                _actualBackgroundGradientAngle = CssValueParser.ParseNumber(BackgroundGradientAngle, 360f);

            return _actualBackgroundGradientAngle;
        }
    }

    public RFont ActualFont
    {
        get
        {
            if (_actualFont != null)
                return _actualFont;

            if (string.IsNullOrEmpty(FontFamily))
                FontFamily = CssConstants.DefaultFont;

            if (string.IsNullOrEmpty(FontSize))
                FontSize = CssConstants.FontSize.ToString(CultureInfo.InvariantCulture) + "pt";

            FontStyle st = Broiler.Graphics.FontStyle.Regular;

            if (this.FontStyle == CssConstants.Italic || this.FontStyle == CssConstants.Oblique)
                st |= Broiler.Graphics.FontStyle.Italic;

            if (IsBoldWeight(FontWeight, GetParent()))
                st |= Broiler.Graphics.FontStyle.Bold;

            double parentSize = CssConstants.FontSize;

            if (GetParent() != null)
                parentSize = GetParent().ActualFont.Size;

            var fsize = FontSize switch
            {
                CssConstants.Medium => CssConstants.FontSize,
                CssConstants.XXSmall => CssConstants.FontSize - 4,
                CssConstants.XSmall => CssConstants.FontSize - 3,
                CssConstants.Small => CssConstants.FontSize - 2,
                CssConstants.Large => CssConstants.FontSize + 2,
                CssConstants.XLarge => CssConstants.FontSize + 3,
                CssConstants.XXLarge => CssConstants.FontSize + 4,
                CssConstants.Smaller => parentSize - 2,
                CssConstants.Larger => parentSize + 2,
                _ => CssValueParser.ParseLength(FontSize, parentSize, parentSize, null, true, true),
            };

            // CSS 2.1 §15.4: font-size: 0 results in a zero-size em box.
            // Use a tiny positive value so the font object remains valid
            // while producing near-zero word dimensions in the layout engine.
            if (fsize <= 0)
                fsize = 0.001;

            _actualFont = GetCachedFont(FontFamily, fsize, st, GetEnabledFontFeatureTags());

            return _actualFont;
        }
    }

    protected abstract RFont GetCachedFont(string fontFamily, double fsize, FontStyle st, string fontFeatures);

    public double ActualLineHeight
    {
        get
        {
            if (double.IsNaN(_actualLineHeight))
            {
                // CSS2.1 §10.8: "normal" line-height uses a UA-chosen value.
                // Prefer the font's own line metrics so layout matches browser
                // line boxes more closely than a fixed 1.2× fallback.
                if (LineHeight == "normal" || string.IsNullOrEmpty(LineHeight))
                    _actualLineHeight = GetNormalLineHeight();
                else
                    _actualLineHeight = ParseLineHeightLength(LineHeight, Size.Height);
            }

            return _actualLineHeight;
        }
    }

    public double ActualTextIndent
    {
        get
        {
            if (double.IsNaN(_actualTextIndent))
                _actualTextIndent = ParseLengthWithLineHeight(TextIndent, Size.Width);

            return _actualTextIndent;
        }
    }

    public double ActualBorderSpacingHorizontal
    {
        get
        {
            if (double.IsNaN(_actualBorderSpacingHorizontal))
            {
                MatchCollection matches = RegexParserUtils.Match(RegexParserUtils.CssLengthRegex(), BorderSpacing);

                if (matches.Count == 0)
                {
                    _actualBorderSpacingHorizontal = 0;
                }
                else if (matches.Count > 0)
                {
                    _actualBorderSpacingHorizontal = ParseLengthWithLineHeight(matches[0].Value, 1);
                }
            }

            return _actualBorderSpacingHorizontal;
        }
    }

    public double ActualBorderSpacingVertical
    {
        get
        {
            if (double.IsNaN(_actualBorderSpacingVertical))
            {
                MatchCollection matches = RegexParserUtils.Match(RegexParserUtils.CssLengthRegex(), BorderSpacing);

                if (matches.Count == 0)
                {
                    _actualBorderSpacingVertical = 0;
                }
                else if (matches.Count == 1)
                {
                    _actualBorderSpacingVertical = ParseLengthWithLineHeight(matches[0].Value, 1);
                }
                else
                {
                    _actualBorderSpacingVertical = ParseLengthWithLineHeight(matches[1].Value, 1);
                }
            }

            return _actualBorderSpacingVertical;
        }
    }

    protected abstract CssBoxProperties GetParent();

    public double GetEmHeight() => ActualFont.Size * (96.0 / 72.0);

    protected double ParseLengthWithLineHeight(string length, double hundredPercent)
    {
        if (!string.IsNullOrWhiteSpace(length) &&
            length.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(length[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rem))
        {
            return rem * GetRootEmHeight();
        }

        return CssValueParser.ParseLength(
            length,
            hundredPercent,
            GetEmHeight(),
            null,
            false,
            false,
            ActualLineHeight,
            GetRootLineHeight());
    }

    private double ParseLineHeightLength(string length, double hundredPercent)
    {
        var parentLineHeight = GetParent()?.ActualLineHeight ?? GetNormalLineHeight();
        return CssValueParser.ParseLength(
            length,
            hundredPercent,
            GetEmHeight(),
            null,
            false,
            false,
            parentLineHeight,
            GetRootLineHeight());
    }

    private double GetRootLineHeight()
    {
        var root = GetEffectiveRootBoxProperties();

        if (!ReferenceEquals(root, this))
            return root.ActualLineHeight;

        if (!double.IsNaN(_actualLineHeight))
            return _actualLineHeight;

        if (LineHeight == "normal" || string.IsNullOrEmpty(LineHeight))
            return GetNormalLineHeight();

        return CssValueParser.ParseLength(
            LineHeight,
            Size.Height,
            GetEmHeight(),
            null,
            false,
            false,
            GetNormalLineHeight(),
            GetNormalLineHeight());
    }

    private double GetNormalLineHeight()
    {
        double fontHeight = ActualFont.Height * (96.0 / 72.0);
        return fontHeight > 0 ? Math.Ceiling(fontHeight) : GetEmHeight() * 1.2;
    }

    private double GetRootEmHeight()
    {
        var root = GetEffectiveRootBoxProperties();

        const double baseRootEmHeight = CssConstants.FontSize * (96.0 / 72.0);
        if (!string.IsNullOrWhiteSpace(root.FontSize))
        {
            var resolved = CssValueParser.ParseLength(
                root.FontSize,
                baseRootEmHeight,
                baseRootEmHeight,
                null,
                false,
                false,
                baseRootEmHeight * 1.2,
                baseRootEmHeight * 1.2);

            if (!double.IsNaN(resolved) && resolved > 0)
                return resolved;
        }

        return root.GetEmHeight();
    }

    private CssBoxProperties GetEffectiveRootBoxProperties()
    {
        CssBoxProperties root = this;
        while (root.GetParent() != null)
            root = root.GetParent();

        if (root is CssBox cssRoot)
        {
            while (cssRoot.HtmlTag == null && cssRoot.Boxes.Count == 1)
            {
                var child = cssRoot.Boxes[0];
                if (ReferenceEquals(child, cssRoot))
                    break;

                cssRoot = child;
            }

            root = cssRoot;
        }

        return root;
    }

    /// <summary>
    /// Resolves a CSS font-weight value to a numeric weight (100–900)
    /// per CSS 2.1 §15.6. Handles keywords <c>normal</c>, <c>bold</c>,
    /// <c>bolder</c>, <c>lighter</c>, and numeric strings.
    /// </summary>
    internal static int ResolveNumericFontWeight(string fontWeight, CssBoxProperties parent)
    {
        if (string.IsNullOrEmpty(fontWeight) || fontWeight == CssConstants.Normal || fontWeight == CssConstants.Inherit)
            return 400;
        if (fontWeight == CssConstants.Bold)
            return 700;

        if (int.TryParse(fontWeight, out int numeric))
            return Math.Clamp(numeric, 100, 900);

        if (fontWeight == CssConstants.Bolder || fontWeight == CssConstants.Lighter)
        {
            int parentWeight = 400;
            if (parent != null)
                parentWeight = ResolveNumericFontWeight(parent.FontWeight, parent.GetParent());

            return fontWeight == CssConstants.Bolder
                ? ResolveBolder(parentWeight)
                : ResolveLighter(parentWeight);
        }

        // Any other non-empty, non-normal value is treated as bold
        return 700;
    }

    /// <summary>
    /// CSS 2.1 §15.6: <c>bolder</c> selects the next weight above the inherited value.
    /// </summary>
    private static int ResolveBolder(int parentWeight)
    {
        if (parentWeight < 400) return 400;
        if (parentWeight < 600) return 700;
        return 900;
    }

    /// <summary>
    /// CSS 2.1 §15.6: <c>lighter</c> selects the next weight below the inherited value.
    /// </summary>
    private static int ResolveLighter(int parentWeight)
    {
        if (parentWeight > 700) return 400;
        if (parentWeight > 500) return 400;
        return 100;
    }

    /// <summary>
    /// Returns <c>true</c> when the resolved numeric font weight is 600 or above,
    /// meaning the font should use a bold face.
    /// </summary>
    private static bool IsBoldWeight(string fontWeight, CssBoxProperties parent)
    {
        if (string.IsNullOrEmpty(fontWeight) || fontWeight == CssConstants.Normal || fontWeight == CssConstants.Inherit)
            return false;
        return ResolveNumericFontWeight(fontWeight, parent) >= 600;
    }

    protected string NoEms(string length)
    {
        var len = new CssLength(length);

        if (len.Unit == CssUnit.Ems)
            length = len.ConvertEmToPixels(GetEmHeight()).ToString();

        return length;
    }

    protected void SetAllBorders(string style = null, string width = null, string color = null)
    {
        if (style != null)
            BorderLeftStyle = BorderTopStyle = BorderRightStyle = BorderBottomStyle = style;

        if (width != null)
            BorderLeftWidth = BorderTopWidth = BorderRightWidth = BorderBottomWidth = width;

        if (color != null)
            BorderLeftColor = BorderTopColor = BorderRightColor = BorderBottomColor = color;
    }

    protected void MeasureWordSpacing(RGraphics g)
    {
        if (!double.IsNaN(ActualWordSpacing))
            return;

        ActualWordSpacing = CssUtils.WhiteSpace(g, this);

        if (WordSpacing == CssConstants.Normal)
            return;

        string len = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), WordSpacing);
        ActualWordSpacing += CssValueParser.ParseLength(len, 1, GetEmHeight());
    }

    protected void InheritStyle(CssBox p, bool everything)
    {
        if (p == null)
            return;

        BorderSpacing = p.BorderSpacing;
        BorderCollapse = p.BorderCollapse;
        _color = p._color;
        EmptyCells = p.EmptyCells;
        WhiteSpace = p.WhiteSpace;
        Visibility = p.Visibility;
        _textIndent = p._textIndent;
        TextAlign = p.TextAlign;
        FontFamily = p.FontFamily;
        FontFeatureSettings = p.FontFeatureSettings;
        FontVariantAlternates = p.FontVariantAlternates;
        _fontSize = p._fontSize;
        FontStyle = p.FontStyle;
        FontVariant = p.FontVariant;
        FontWeight = p.FontWeight;
        ListStyleImage = p.ListStyleImage;
        ListStylePosition = p.ListStylePosition;
        ListStyleType = p.ListStyleType;
        ListStyle = p.ListStyle;
        _lineHeight = p._lineHeight;
        WordBreak = p.WordBreak;
        Direction = p.Direction;
        WritingMode = p.WritingMode;
        TextShadow = p.TextShadow;

        if (!everything)
            return;

        BackgroundColor = p.BackgroundColor;
        BackgroundGradient = p.BackgroundGradient;
        BackgroundGradientAngle = p.BackgroundGradientAngle;
        BackgroundImage = p.BackgroundImage;
        BackgroundPosition = p.BackgroundPosition;
        BackgroundRepeat = p.BackgroundRepeat;
        BackgroundAttachment = p.BackgroundAttachment;
        BackgroundOrigin = p.BackgroundOrigin;
        BackgroundSize = p.BackgroundSize;
        _borderTopWidth = p._borderTopWidth;
        _borderRightWidth = p._borderRightWidth;
        _borderBottomWidth = p._borderBottomWidth;
        _borderLeftWidth = p._borderLeftWidth;
        _borderTopColor = p._borderTopColor;
        _borderRightColor = p._borderRightColor;
        _borderBottomColor = p._borderBottomColor;
        _borderLeftColor = p._borderLeftColor;
        BorderTopStyle = p.BorderTopStyle;
        BorderRightStyle = p.BorderRightStyle;
        BorderBottomStyle = p.BorderBottomStyle;
        BorderLeftStyle = p.BorderLeftStyle;
        _bottom = p._bottom;
        CornerNwRadius = p.CornerNwRadius;
        CornerNeRadius = p.CornerNeRadius;
        CornerSeRadius = p.CornerSeRadius;
        CornerSwRadius = p.CornerSwRadius;
        _cornerRadius = p._cornerRadius;
        Display = p.Display;
        Float = p.Float;
        BlockSize = p.BlockSize;
        Height = p.Height;
        InlineSize = p.InlineSize;
        MarginBottom = p.MarginBottom;
        MarginLeft = p.MarginLeft;
        MarginRight = p.MarginRight;
        MarginTop = p.MarginTop;
        MarginTrim = p.MarginTrim;
        _left = p._left;
        _lineHeight = p._lineHeight;
        Overflow = p.Overflow;
        _paddingLeft = p._paddingLeft;
        _paddingBottom = p._paddingBottom;
        _paddingRight = p._paddingRight;
        _paddingTop = p._paddingTop;
        _right = p._right;
        TextDecoration = p.TextDecoration;
        TextDecorationStyle = p.TextDecorationStyle;
        TextDecorationColor = p.TextDecorationColor;
        _top = p._top;
        Position = p.Position;
        VerticalAlign = p.VerticalAlign;
        Width = p.Width;
        MaxWidth = p.MaxWidth;
        MinWidth = p.MinWidth;
        MinHeight = p.MinHeight;
        MaxHeight = p.MaxHeight;
        _wordSpacing = p._wordSpacing;
        Opacity = p.Opacity;
        BoxShadow = p.BoxShadow;
        MixBlendMode = p.MixBlendMode;
        BackgroundBlendMode = p.BackgroundBlendMode;
        Filter = p.Filter;
        Isolation = p.Isolation;
        BoxSizing = p.BoxSizing;
        BackgroundClip = p.BackgroundClip;
        ClipPath = p.ClipPath;
        FlexDirection = p.FlexDirection;
        JustifyContent = p.JustifyContent;
        JustifyItems = p.JustifyItems;
        AlignItems = p.AlignItems;
    }

    protected void InvalidateFontDependentValues()
    {
        _actualFont = null;
        _actualHeight = double.NaN;
        _actualWidth = double.NaN;
        _actualPaddingTop = double.NaN;
        _actualPaddingBottom = double.NaN;
        _actualPaddingRight = double.NaN;
        _actualPaddingLeft = double.NaN;
        _actualMarginTop = double.NaN;
        _actualMarginBottom = double.NaN;
        _actualMarginRight = double.NaN;
        _actualMarginLeft = double.NaN;
        _actualLineHeight = double.NaN;
        _actualTextIndent = double.NaN;
        _actualBorderTopWidth = double.NaN;
        _actualBorderRightWidth = double.NaN;
        _actualBorderBottomWidth = double.NaN;
        _actualBorderLeftWidth = double.NaN;
        _actualCornerNw = double.NaN;
        _actualCornerNe = double.NaN;
        _actualCornerSw = double.NaN;
        _actualCornerSe = double.NaN;
        _actualBorderSpacingHorizontal = double.NaN;
        _actualBorderSpacingVertical = double.NaN;
    }
}
