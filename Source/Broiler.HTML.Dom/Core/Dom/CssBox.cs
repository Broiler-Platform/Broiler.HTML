using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Core.Utils;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Broiler.HTML.Dom.Core.Dom;

internal class CssBox : CssBoxProperties, IDisposable
{
    private CssBox _parentBox;
    protected IHtmlContainerInt _htmlContainer;
    private ReadOnlyMemory<char> _text;

    internal bool _tableFixed;

    /// <summary>
    /// When the block-inside-inline correction (CSS2.1 §9.2.1.1) splits a
    /// positioned inline element into sibling anonymous blocks, the hoisted
    /// blocks lose their parent–child relationship with the positioned
    /// inline in the box tree.  This field links back to the original
    /// positioned ancestor so that <see cref="FindPositionedContainingBlock"/>
    /// can still find it.
    /// </summary>
    internal CssBox SplitPositionedAncestor { get; set; }

    private bool UsesBorderBoxSizing =>
        BoxSizing != null && BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase);

    private double ResolveSpecifiedWidthToBorderBox(double cssWidth)
    {
        if (!UsesBorderBoxSizing)
            cssWidth += ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth;

        return Math.Max(0, cssWidth);
    }

    private double ResolveSpecifiedHeightToBorderBox(double cssHeight)
    {
        if (!UsesBorderBoxSizing)
            cssHeight += ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth;

        return Math.Max(0, cssHeight);
    }

    private double ResolveSpecifiedWidthToContentBox(double cssWidth)
    {
        if (UsesBorderBoxSizing)
            cssWidth -= ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth;

        return Math.Max(0, cssWidth);
    }

    /// <summary>
    /// When the block-inside-inline correction splits a positioned inline
    /// element, the original box loses its children to anonymous "left" and
    /// "right" copies.  This list tracks those copies so that
    /// <see cref="GetInlineBoundingBox"/> can compute the bounding box
    /// across <em>all</em> fragments, not just the (now-empty) original.
    /// Only populated on the original box that serves as
    /// <see cref="SplitPositionedAncestor"/> for hoisted descendants.
    /// </summary>
    internal List<CssBox> SplitFragments { get; private set; }

    /// <summary>
    /// Register a box as a fragment of this positioned inline that was
    /// created during the block-inside-inline split.
    /// </summary>
    internal void AddSplitFragment(CssBox fragment)
    {
        SplitFragments ??= new List<CssBox>();
        SplitFragments.Add(fragment);
    }

    protected bool _wordsSizeMeasured;
    private CssBox _listItemBox;
    private List<IImageLoadHandler?>? _backgroundImageLoadHandlers;
    private bool _backgroundImagesInitialized;

    /// <summary>
    /// CSS property names that were applied with <c>!important</c> during
    /// cascade.  Normal-priority declarations must not override these.
    /// CSS2.1 §6.4.2.
    /// </summary>
    internal HashSet<string> ImportantProperties { get; private set; }

    /// <summary>
    /// CSS property names that were set by an author-origin declaration
    /// during cascade.  User-agent declarations must not override these.
    /// CSS2.1 §6.4.1: Author origin beats UA origin regardless of
    /// specificity.
    /// </summary>
    internal HashSet<string> AuthorProperties { get; private set; }

    internal Dictionary<string, string> CustomProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks a property as having been set via an <c>!important</c>
    /// declaration so that subsequent normal-priority rules cannot
    /// override it.
    /// </summary>
    internal void MarkPropertyImportant(string propertyName)
    {
        ImportantProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ImportantProperties.Add(propertyName);
    }

    /// <summary>
    /// Marks a property as having been set by an author-origin
    /// declaration so that UA declarations cannot override it.
    /// </summary>
    internal void MarkPropertyAuthor(string propertyName)
    {
        AuthorProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AuthorProperties.Add(propertyName);
    }

    internal void SetCustomProperty(string propertyName, string value)
    {
        if (string.IsNullOrEmpty(propertyName))
            return;

        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "initial", StringComparison.OrdinalIgnoreCase))
        {
            CustomProperties[propertyName] = InvalidCustomPropertySentinel;
            return;
        }

        if (string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "unset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "revert", StringComparison.OrdinalIgnoreCase))
        {
            CustomProperties.Remove(propertyName);
            return;
        }

        CustomProperties[propertyName] = value;
    }

    /// <summary>
    /// Returns the loaded background image handle, or null if no background image is loaded.
    /// Used by <c>FragmentTreeBuilder</c> to capture background images for the new paint path.
    /// </summary>
    internal object? LoadedBackgroundImage
    {
        get
        {
            if (_backgroundImageLoadHandlers == null || _backgroundImageLoadHandlers.Count == 0)
                return null;

            if (_backgroundImageLoadHandlers.Count == 1)
                return _backgroundImageLoadHandlers[0]?.Image;

            var layers = new object?[_backgroundImageLoadHandlers.Count];
            bool hasImage = false;
            for (int i = 0; i < _backgroundImageLoadHandlers.Count; i++)
            {
                var image = _backgroundImageLoadHandlers[i]?.Image;
                layers[i] = image;
                hasImage |= image != null;
            }

            return hasImage ? layers : null;
        }
    }

    public CssBox(CssBox parentBox, HtmlTag tag, Uri baseUrl)
    {
        if (parentBox != null)
        {
            _parentBox = parentBox;
            _parentBox.Boxes.Add(this);
        }

        HtmlTag = tag;
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// The container abstracted through <see cref="IHtmlContainerInt"/>. Used by
    /// CssBox and subclass code for decoupled access.
    /// </summary>
    internal IHtmlContainerInt ContainerInt
    {
        get { return _htmlContainer ??= _parentBox?.ContainerInt; }
        set { _htmlContainer = value; }
    }

    public CssBox ParentBox
    {
        get { return _parentBox; }
        set
        {
            _parentBox?.Boxes.Remove(this);
            _parentBox = value;

            if (value != null)
                _parentBox.Boxes.Add(this);
        }
    }

    public List<CssBox> Boxes { get; } = [];

    public override bool AvoidGeometryAntialias => ContainerInt?.AvoidGeometryAntialias ?? false;

    protected override bool TryGetCustomPropertyValue(string propertyName, out string value)
    {
        if (CustomProperties.TryGetValue(propertyName, out value))
            return true;

        if (ParentBox != null)
            return ParentBox.TryGetCustomPropertyValue(propertyName, out value);

        value = string.Empty;
        return false;
    }

    public bool IsBrElement => HtmlTag != null && HtmlTag.Name.Equals("br", StringComparison.InvariantCultureIgnoreCase);
    public bool IsInline => (Display == CssConstants.Inline || Display == CssConstants.InlineBlock
        || Display == "inline-flex" || Display == "inline-grid") && !IsBrElement;
    public bool IsBlock => Display == CssConstants.Block || Display == "flex"
        || Display == "grid";
    public virtual bool IsClickable
    {
        get
        {
            if (HtmlTag == null)
                return false;

            // <a> links (without only an id anchor)
            if (HtmlTag.Name == HtmlConstants.A && !HtmlTag.HasAttribute("id"))
                return true;

            // <button> elements
            if (HtmlTag.Name.Equals("button", StringComparison.OrdinalIgnoreCase))
                return true;

            // <input type="submit|button|reset"> elements
            if (HtmlTag.Name.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                var inputType = HtmlTag.TryGetAttribute("type")?.ToLowerInvariant() ?? "text";
                if (inputType is "submit" or "button" or "reset")
                    return true;
            }

            return false;
        }
    }

    public virtual bool IsFixed
    {
        get
        {
            if (Position == CssConstants.Fixed)
                return true;

            if (ParentBox == null)
                return false;

            CssBox parent = this;

            while (!(parent.ParentBox == null || parent == parent.ParentBox))
            {
                parent = parent.ParentBox;

                if (parent.Position == CssConstants.Fixed)
                    return true;
            }

            return false;
        }
    }

    public virtual string HrefLink => GetAttribute(HtmlConstants.Href);

    public CssBox ContainingBlock
    {
        get
        {
            if (ParentBox == null)
                return this; //This is the initial containing block.

            var box = ParentBox;

            // CSS2.1 §10.1: The containing block for a box is the nearest
            // ancestor that is a block container.  Block containers include:
            //   - block-level boxes (display:block, flex, grid)
            //   - inline-block boxes (display:inline-block)
            //   - list-item boxes
            //   - table cells (display:table-cell)
            //   - table boxes (display:table)
            // Inline-block establishes a BFC (§9.4.1), so its block-level
            // children must use it as their containing block.
            while (!box.IsBlock
                   && box.Display != CssConstants.InlineBlock
                   && box.Display != CssConstants.ListItem
                   && box.Display != CssConstants.Table
                   && box.Display != CssConstants.TableCell
                   && box.ParentBox != null)
            {
                box = box.ParentBox;
            }

            //Comment this following line to treat always superior box as block
            if (box == null)
                throw new Exception("There's no containing block on the chain");

            return box;
        }
    }

    /// <summary>
    /// CSS2.1 §10.1: For absolutely positioned elements, the containing
    /// block is the padding-box of the nearest ancestor with a computed
    /// position of <c>absolute</c>, <c>relative</c>, or <c>fixed</c>.
    /// Falls back to <see cref="ContainingBlock"/> if none is found.
    /// Also checks <see cref="SplitPositionedAncestor"/> which links back
    /// to positioned inlines that were restructured by the block-inside-
    /// inline correction (CSS2.1 §9.2.1.1).
    /// </summary>
    private CssBox FindPositionedContainingBlock()
    {
        var box = ParentBox;
        while (box != null)
        {
            if (box.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed || box.ParentBox == null)
                return box;

            // If the block-inside-inline correction split a positioned inline
            // and hoisted this branch out, SplitPositionedAncestor links back
            // to the original positioned inline ancestor.
            if (box.SplitPositionedAncestor is { } spa
                && spa.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed)
                return spa;

            box = box.ParentBox;
        }

        return ContainingBlock;
    }

    private bool IsInitialContainingBlock(CssBox cb) =>
        cb.ParentBox == null && ContainerInt != null;

    private void GetAbsoluteContainingBlockPaddingBox(CssBox cb,
        out double cbPadLeft,
        out double cbPadTop,
        out double cbPadWidth,
        out double cbPadHeight)
    {
        if (IsInlineContainingBlock(cb))
        {
            var bbox = GetInlineBoundingBox(cb);
            if (bbox != RectangleF.Empty)
            {
                cbPadLeft = bbox.Left;
                cbPadTop = bbox.Top;
                cbPadWidth = bbox.Width;
                cbPadHeight = bbox.Height;
                return;
            }
        }

        if (IsInitialContainingBlock(cb))
        {
            cbPadLeft = 0;
            cbPadTop = 0;
            cbPadWidth = ContainerInt!.ViewportSize.Width;
            cbPadHeight = ContainerInt.ViewportSize.Height;
            return;
        }

        cbPadLeft = cb.Location.X + cb.ActualBorderLeftWidth;
        cbPadTop = cb.Location.Y + cb.ActualBorderTopWidth;
        cbPadWidth = cb.Size.Width - cb.ActualBorderLeftWidth - cb.ActualBorderRightWidth;
        cbPadHeight = (cb.ActualBottom - cb.Location.Y) - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth;
    }

    /// <summary>
    /// CSS2.1 §10.1: When the containing block for an absolutely positioned
    /// element is formed by an inline-level element, the containing block is
    /// the bounding box around the padding boxes of the first and last inline
    /// boxes generated for that element.  Returns the bounding rectangle in
    /// absolute coordinates, or <see cref="RectangleF.Empty"/> if the inline
    /// has no line-box rectangles and no laid-out children.
    /// </summary>
    private static RectangleF GetInlineBoundingBox(CssBox cb)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        // Accumulate extents from one box (the original or a fragment).
        void AccumulateBox(CssBox box)
        {
            // Try the inline's own Rectangles (populated when the
            // inline element has direct text words).
            foreach (var rect in box.Rectangles.Values)
            {
                if (rect.Left < minX) minX = rect.Left;
                if (rect.Top < minY) minY = rect.Top;
                if (rect.Right > maxX) maxX = rect.Right;
                if (rect.Bottom > maxY) maxY = rect.Bottom;
            }

            // Also scan child boxes (inline-blocks etc.) for their
            // laid-out positions and sizes.
            foreach (var child in box.Boxes)
            {
                if (child.Size.Width <= 0 && child.Size.Height <= 0)
                    continue;
                float left = child.Location.X;
                float top = child.Location.Y;
                float right = left + child.Size.Width;
                float bottom = (float)child.ActualBottom;
                if (bottom <= top) bottom = top + child.Size.Height;

                if (left < minX) minX = left;
                if (top < minY) minY = top;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
            }
        }

        // Scan the original box.
        AccumulateBox(cb);

        // If the positioned inline was split by the block-inside-inline
        // correction, also scan inline fragment copies that received its
        // children so the bounding box covers the full inline extent.
        // Only include fragments that are still inline — block-level
        // anonymous wrappers created during the split are structural
        // containers, not inline fragments.
        if (cb.SplitFragments != null)
        {
            foreach (var frag in cb.SplitFragments)
            {
                if (frag.Display == CssConstants.Inline)
                    AccumulateBox(frag);
            }
        }

        if (minX > maxX || minY > maxY)
            return RectangleF.Empty;

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Returns <c>true</c> when the given box is a pure inline element
    /// (not inline-block/inline-table etc.) whose containing-block extent
    /// must be computed from its line-box rectangles per CSS2.1 §10.1.
    /// </summary>
    private static bool IsInlineContainingBlock(CssBox cb) =>
        cb.Display == CssConstants.Inline;

    /// <summary>
    /// Returns true when <see cref="Height"/> is a percentage that resolves
    /// to auto because the containing block's height is not explicitly
    /// specified (CSS 2.1 §10.5).  Callers must still verify that Height is
    /// not auto/empty before using this — the check only tests whether a
    /// non-auto percentage value should be treated as auto.
    /// </summary>
    internal bool HeightPercentageResolvesToAuto()
    {
        if (!Height.Contains('%'))
            return false;

        // CSS 2.1 §10.5: "A percentage height on the root element is
        // relative to the initial containing block."  The initial
        // containing block always has a definite height (the viewport),
        // so percentage heights on the root element never resolve to auto.
        if (ContainingBlock?.ParentBox == null)
            return false;

        return ContainingBlock.Height == CssConstants.Auto
            || string.IsNullOrEmpty(ContainingBlock.Height);
    }

    public HtmlTag HtmlTag { get; }

    public bool IsImage => Words.Count == 1 && Words[0].IsImage;

    public ReadOnlyMemory<char> Text
    {
        get { return _text; }
        set
        {
            _text = value;
            Words.Clear();
        }
    }

    internal List<CssLineBox> LineBoxes { get; } = [];
    internal Dictionary<CssLineBox, RectangleF> Rectangles { get; } = [];
    internal List<CssRect> Words { get; } = [];
    internal CssRect FirstWord => Words[0];

    internal CssLineBox FirstHostingLineBox { get; set; }

    internal CssLineBox LastHostingLineBox { get; set; }

    internal Uri BaseUrl { get; set; }

    public void PerformLayout(RGraphics g)
    {
        try
        {
            PerformLayoutImp(g);
        }
        catch (Exception ex)
        {
            ContainerInt.ReportError(HtmlRenderErrorType.Layout, "Exception in box layout", ex);
        }
    }

    public void SetBeforeBox(CssBox before)
    {
        int index = _parentBox.Boxes.IndexOf(before);

        if (index < 0)
            throw new Exception("before box doesn't exist on parent");

        _parentBox.Boxes.Remove(this);
        _parentBox.Boxes.Insert(index, this);
    }

    public void SetAllBoxes(CssBox fromBox)
    {
        foreach (var childBox in fromBox.Boxes)
            childBox._parentBox = this;

        Boxes.AddRange(fromBox.Boxes);
        fromBox.Boxes.Clear();
    }

    public void ParseToWords()
    {
        Words.Clear();

        // CSS2.1 §4.3.8: UAs should not render characters from the Unicode
        // "control characters" category (C0 U+0000–U+001F except tab/LF/CR,
        // and C1 U+007F–U+009F).  Strip them before word splitting.
        // Per HTML spec §13.2.2, U+0000 (NULL) is replaced with U+FFFD
        // (REPLACEMENT CHARACTER) so it remains visible.
        var textSpan = _text.Span;
        bool hasControl = false;
        for (int i = 0; i < textSpan.Length; i++)
        {
            char c = textSpan[i];
            if (c != '\t' && c != '\n' && c != '\r'
                && (char.IsControl(c) || (c >= '\u007F' && c <= '\u009F')))
            {
                hasControl = true;
                break;
            }
        }
        if (hasControl)
        {
            var sb = new System.Text.StringBuilder(textSpan.Length);
            for (int i = 0; i < textSpan.Length; i++)
            {
                char c = textSpan[i];
                if (c == '\0')
                    sb.Append('\uFFFD'); // HTML spec: NULL → REPLACEMENT CHARACTER
                else if (c == '\t' || c == '\n' || c == '\r'
                    || (!char.IsControl(c) && (c < '\u007F' || c > '\u009F')))
                    sb.Append(c);
            }
            _text = sb.ToString().AsMemory();
        }

        int startIdx = 0;
        bool preserveSpaces = WhiteSpace == CssConstants.Pre || WhiteSpace == CssConstants.PreWrap;
        bool respoctNewline = preserveSpaces || WhiteSpace == CssConstants.PreLine;

        textSpan = _text.Span;
        while (startIdx < textSpan.Length)
        {
            while (startIdx < textSpan.Length && textSpan[startIdx] == '\r')
                startIdx++;

            if (startIdx >= textSpan.Length)
                continue;

            var endIdx = startIdx;

            while (endIdx < textSpan.Length && char.IsWhiteSpace(textSpan[endIdx]) && textSpan[endIdx] != '\n')
                endIdx++;

            if (endIdx > startIdx)
            {
                if (preserveSpaces)
                {
                    // CSS2.1 §16.6: For pre-wrap, emit each space as a
                    // separate word so the layout engine can break lines
                    // at any space position.  For pre, emit the entire
                    // whitespace run as one word (no wrapping allowed).
                    if (WhiteSpace == CssConstants.PreWrap)
                    {
                        // Cache " " string to avoid per-char allocation
                        const string singleSpace = " ";
                        for (int i = startIdx; i < endIdx; i++)
                        {
                            var ch = _text.Slice(i, 1).ToString();
                            Words.Add(new CssRectWord(this, ch == " " ? singleSpace : ch, false, false));
                        }
                    }
                    else
                    {
                        Words.Add(new CssRectWord(this, HtmlUtils.DecodeHtml(_text.Slice(startIdx, endIdx - startIdx).ToString()), false, false));
                    }
                }
            }
            else
            {
                endIdx = startIdx;

                while (endIdx < textSpan.Length && !char.IsWhiteSpace(textSpan[endIdx]) && textSpan[endIdx] != '-' && WordBreak != CssConstants.BreakAll && !CommonUtils.IsAsianCharecter(textSpan[endIdx]))
                    endIdx++;

                if (endIdx < textSpan.Length && (textSpan[endIdx] == '-' || WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharecter(textSpan[endIdx])))
                    endIdx++;

                if (endIdx > startIdx)
                {
                    var hasSpaceBefore = !preserveSpaces && startIdx > 0 && Words.Count == 0 && char.IsWhiteSpace(textSpan[startIdx - 1]);
                    var hasSpaceAfter = !preserveSpaces && endIdx < textSpan.Length && char.IsWhiteSpace(textSpan[endIdx]);

                    Words.Add(new CssRectWord(this, HtmlUtils.DecodeHtml(_text.Slice(startIdx, endIdx - startIdx).ToString()), hasSpaceBefore, hasSpaceAfter));
                }
            }

            // create new-line word so it will effect the layout
            if (endIdx < textSpan.Length && textSpan[endIdx] == '\n')
            {
                endIdx++;

                if (respoctNewline)
                    Words.Add(new CssRectWord(this, "\n", false, false));
            }

            startIdx = endIdx;
        }
    }

    public virtual void Dispose()
    {
        if (_backgroundImageLoadHandlers != null)
        {
            foreach (var imageLoadHandler in _backgroundImageLoadHandlers)
                imageLoadHandler?.Dispose();
        }

        foreach (var childBox in Boxes)
            childBox.Dispose();
    }

    protected virtual void PerformLayoutImp(RGraphics g)
    {
        if (Display != CssConstants.None)
        {
            RectanglesReset();
            MeasureWordsSize(g);
        }

        if (IsBlock || Display == CssConstants.ListItem || Display == CssConstants.Table || Display == CssConstants.InlineTable || Display == CssConstants.TableCell)
        {
            // Because their width and height are set by CssTable
            if (Display != CssConstants.TableCell && Display != CssConstants.Table)
            {
                // CSS2.1 §9.6.1: The containing block for a fixed-position
                // element is the viewport (initial containing block).
                // CSS2.1 §10.1: For absolutely positioned elements, the
                // containing block is the padding-box of the nearest
                // positioned ancestor.
                // Use the viewport width for percentage/auto resolution.
                double width;
                if (Position == CssConstants.Fixed && ContainerInt != null)
                {
                    width = ContainerInt.ViewportSize.Width;
                }
                else if (Position == CssConstants.Absolute)
                {
                    var cb = FindPositionedContainingBlock();
                    GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out width, out _);
                }
                else
                {
                    width = ContainingBlock.Size.Width
                            - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                            - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth;
                }

                if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
                {
                    double containingWidth = width;
                    width = string.Equals(Width, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                        ? GetParent().ActualWidth
                        : ParseLengthWithLineHeight(Width, containingWidth);

                    // CSS2.1 §10.4: Apply max-width constraint
                    if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
                    {
                        double maxW = ParseLengthWithLineHeight(MaxWidth, containingWidth);
                        if (width > maxW) width = maxW;
                    }

                    // CSS2.1 §10.4: Apply min-width constraint (min wins over max per §10.4)
                    if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
                    {
                        double minW = ParseLengthWithLineHeight(MinWidth, containingWidth);
                        if (width < minW) width = minW;
                    }

                    width = ResolveSpecifiedWidthToBorderBox(width);
                }
                else if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
                    && Left != null && Left != CssConstants.Auto
                    && Right != null && Right != CssConstants.Auto)
                {
                    // CSS2.1 §10.3.7: For absolutely positioned, non-replaced
                    // elements when width is auto and both left and right are
                    // specified, compute width from the constraint equation:
                    // left + margin-left + width + margin-right + right = CB width
                    double cbContentWidth = width;
                    if (Position == CssConstants.Fixed && ContainerInt != null)
                        cbContentWidth = ContainerInt.ViewportSize.Width;
                    double cssLeft = CssValueParser.ParseLength(Left, cbContentWidth, GetEmHeight());
                    double cssRight = CssValueParser.ParseLength(Right, cbContentWidth, GetEmHeight());
                    width = cbContentWidth - cssLeft - cssRight - ActualMarginLeft - ActualMarginRight;
                    if (width < 0) width = 0;
                    width = ResolveSpecifiedWidthToBorderBox(width);
                }

                // CSS2.1 §10.4: Apply max-width constraint even when
                // Width is auto — the tentative used width must not exceed
                // max-width.
                if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
                {
                    double maxW = ParseLengthWithLineHeight(MaxWidth, width);
                    maxW = ResolveSpecifiedWidthToBorderBox(maxW);
                    if (width > maxW) width = maxW;
                }

                // CSS2.1 §10.4: Apply min-width constraint (min wins over
                // max per §10.4) — also when Width is auto.
                if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
                {
                    double minW = ParseLengthWithLineHeight(MinWidth, width);
                    minW = ResolveSpecifiedWidthToBorderBox(minW);
                    if (width < minW) width = minW;
                }

                Size = new SizeF((float)width, Size.Height);

                // CSS2.1 §10.3.3: For block-level, non-replaced elements in
                // normal flow with an explicit width and auto margins, resolve
                // the auto margins so the element is centered horizontally.
                if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width)
                    && Float == CssConstants.None
                    && Position != CssConstants.Absolute && Position != CssConstants.Fixed)
                {
                    double containingContentWidth = ContainingBlock.Size.Width
                        - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                        - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth;
                    double remainingSpace = containingContentWidth - Size.Width;

                    if (MarginLeft == CssConstants.Auto && MarginRight == CssConstants.Auto)
                    {
                        if (remainingSpace >= 0)
                        {
                            string halfMargin = (remainingSpace / 2).ToString("F4",
                                CultureInfo.InvariantCulture) + "px";
                            MarginLeft = halfMargin;
                            MarginRight = halfMargin;
                        }
                        else
                        {
                            MarginLeft = "0";
                            MarginRight = "0";
                        }
                    }
                    else if (MarginLeft == CssConstants.Auto)
                    {
                        double rightMargin = ActualMarginRight;
                        double leftMargin = Math.Max(0, remainingSpace - rightMargin);
                        MarginLeft = leftMargin.ToString("F4",
                            CultureInfo.InvariantCulture) + "px";
                    }
                    else if (MarginRight == CssConstants.Auto)
                    {
                        double leftMargin = ActualMarginLeft;
                        double rightMargin = Math.Max(0, remainingSpace - leftMargin);
                        MarginRight = rightMargin.ToString("F4",
                            CultureInfo.InvariantCulture) + "px";
                    }
                }

                // CSS2.1 §10.3.7: Absolutely positioned non-replaced elements
                // with auto width use shrink-to-fit when at least one of
                // left/right is auto.  Shrink-to-fit =
                //   min(max(preferred_minimum, available), preferred)
                if ((Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
                    && (Position == CssConstants.Absolute || Position == CssConstants.Fixed)
                    && (Left == null || Left == CssConstants.Auto
                     || Right == null || Right == CssConstants.Auto))
                {
                    // Ensure descendant word sizes (and ActualWordSpacing) are
                    // measured before computing intrinsic min/max widths.
                    // Without this, word.FullWidth may be NaN because
                    // ActualWordSpacing defaults to NaN until MeasureWordSpacing
                    // runs, causing the entire shrink-to-fit result to be NaN.
                    EnsureDescendantWordsMeasured(g);

                    // Compute preferred width by independently measuring each
                    // direct child and taking the maximum.  This correctly
                    // treats each block/float child as its own "line" and avoids
                    // the additive accumulation in GetMinMaxSumWords where a
                    // float's width would incorrectly sum with a preceding
                    // block child's width.
                    double preferred = ComputeShrinkToFitWidth();
                    double available = width - ActualMarginLeft - ActualMarginRight;

                    GetMinMaxWidth(out double prefMin, out _);
                    // Guard against NaN from unmeasured descendants
                    if (double.IsNaN(prefMin)) prefMin = 0;
                    if (double.IsNaN(preferred)) preferred = 0;
                    double stfWidth = Math.Min(Math.Max(prefMin, available), preferred);

                    if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
                    {
                        double maxW = ParseLengthWithLineHeight(MaxWidth, width);
                        if (stfWidth > maxW) stfWidth = maxW;
                    }
                    if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
                    {
                        double minW = ParseLengthWithLineHeight(MinWidth, width);
                        if (stfWidth < minW) stfWidth = minW;
                    }

                    // CSS2.1 §10.3.7: Shrink-to-fit gives the content
                    // width; add own borders and padding for the border-box
                    // width that Size.Width represents.
                    stfWidth += ActualBorderLeftWidth + ActualBorderRightWidth
                              + ActualPaddingLeft + ActualPaddingRight;

                    Size = new SizeF((float)stfWidth, Size.Height);
                }
                else if ((Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
                    && Float != CssConstants.None)
                {
                    // CSS2.1 §10.3.5: Floating non-replaced elements with
                    // 'width: auto' use shrink-to-fit width.
                    EnsureDescendantWordsMeasured(g);

                    double preferred = ComputeShrinkToFitWidth();
                    double available = width - ActualMarginLeft - ActualMarginRight;

                    GetMinMaxWidth(out double prefMin, out _);
                    if (double.IsNaN(prefMin)) prefMin = 0;
                    if (double.IsNaN(preferred)) preferred = 0;
                    double stfWidth = Math.Min(Math.Max(prefMin, available), preferred);

                    if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
                    {
                        double maxW = ParseLengthWithLineHeight(MaxWidth, width);
                        if (stfWidth > maxW) stfWidth = maxW;
                    }
                    if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
                    {
                        double minW = ParseLengthWithLineHeight(MinWidth, width);
                        if (stfWidth < minW) stfWidth = minW;
                    }

                    stfWidth += ActualBorderLeftWidth + ActualBorderRightWidth
                              + ActualPaddingLeft + ActualPaddingRight;

                    Size = new SizeF((float)stfWidth, Size.Height);
                }
                else if (Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
                {
                    // Margins reduce the box width only for auto-width elements.
                    // For explicit widths, margins affect position only (CSS1 box model).
                    Size = new SizeF((float)(width - ActualMarginLeft - ActualMarginRight), Size.Height);
                }
            }

            if (Display != CssConstants.TableCell)
            {
                var prevSibling = DomUtils.GetPreviousSibling(this);

                // Compute the static position for all elements (including
                // position:fixed).  Fixed elements need the static position
                // as fallback when offset properties are auto (CSS2.1 §10.6.4).
                {
                    double left = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ActualMarginLeft + ContainingBlock.ActualBorderLeftWidth;

                    // CSS2.1 §9.5: floats are out of normal flow. Non-floated
                    // blocks must be positioned as if preceding floats do not
                    // exist.  For cleared elements this also prevents margin
                    // collapsing with the float (CSS2.1 §8.3.1).
                    var flowPrev = prevSibling;
                    if (Float == CssConstants.None
                        && flowPrev != null && flowPrev.Float != CssConstants.None)
                    {
                        flowPrev = DomUtils.GetPreviousInFlowSibling(flowPrev);
                    }

                    // CSS2.1 §9.4.3: Relative positioning is visual-only.
                    // Use the flow-position bottom (before relative offset)
                    // when computing the next sibling's position.
                    double flowPrevBottom = flowPrev?.ActualBottom ?? 0;
                    if (flowPrev is CssBox flowPrevBox && flowPrevBox.Position == CssConstants.Relative)
                        flowPrevBottom -= CssBoxHelper.GetRelativeOffsetY(flowPrevBox);

                    // CSS2.1 §8.3.1: MarginTopCollapse may propagate margins
                    // and update the parent's Location, so compute it before
                    // reading ParentBox.ClientTop.
                    double marginCollapse = MarginTopCollapse(flowPrev);
                    double top = (flowPrev == null && ParentBox != null ? ParentBox.ClientTop : ParentBox == null ? Location.Y : 0) + marginCollapse + flowPrevBottom;

                    // --- Float positioning ---
                    if (Float != CssConstants.None)
                    {
                        // Align Y with previous float sibling if consecutive
                        if (prevSibling != null && prevSibling.Float != CssConstants.None)
                            top = prevSibling.Location.Y;

                        double containerLeft = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ContainingBlock.ActualBorderLeftWidth;
                        double containerRight = ContainingBlock.ClientLeft + ContainingBlock.AvailableWidth;
                        double floatHeight = Math.Max(ActualHeight + ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth, 1);

                        // Collect all preceding floats in the BFC, including
                        // those nested inside non-BFC siblings (CSS2.1 §9.5.1).
                        var precedingFloats = CssBoxHelper.CollectPrecedingFloatsInBfc(this);

                        // CSS2.1 §9.5.1 rule 4: A floating box's outer top
                        // (margin edge) may not be higher than the top of its
                        // containing block.  `top` already includes the margin
                        // contribution (from MarginTopCollapse), so the outer
                        // (margin-edge) top = top - ActualMarginTop.  The
                        // constraint outer_top >= ClientTop translates to:
                        //   top >= ClientTop + ActualMarginTop
                        // This allows negative margins to pull the float above
                        // the content-area edge while still honoring the rule.
                        if (ParentBox != null)
                            top = Math.Max(top, ParentBox.ClientTop + ActualMarginTop);

                        // CSS2.1 §9.5.1 rule 6: The outer top of a floating
                        // box may not be higher than the outer top of any
                        // block or floated box generated by an element earlier
                        // in the source document.
                        foreach (var pf in precedingFloats)
                            top = Math.Max(top, pf.Location.Y);

                        if (Float == CssConstants.Left)
                        {
                            // Iteratively resolve collisions with all prior floats (CSS1 §5.5.25)
                            for (int iter = 0; iter < 100; iter++)
                            {
                                left = containerLeft + ActualMarginLeft;

                                foreach (var floatBox in precedingFloats)
                                {
                                    if (floatBox.Float == CssConstants.Left)
                                    {
                                        double fBottom = floatBox.ActualBottom;
                                        if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                            left = Math.Max(left, floatBox.Location.X + floatBox.Size.Width + floatBox.ActualMarginRight + ActualMarginLeft);
                                    }
                                }

                                // Also ensure left float doesn't overlap with right floats
                                double effectiveRight = containerRight;
                                foreach (var floatBox in precedingFloats)
                                {
                                    if (floatBox.Float == CssConstants.Right)
                                    {
                                        double fBottom = floatBox.ActualBottom;
                                        if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                            effectiveRight = Math.Min(effectiveRight, floatBox.Location.X - floatBox.ActualMarginLeft);
                                    }
                                }

                                if (left + Size.Width <= effectiveRight)
                                    break;

                                // Move below the lowest overlapping float
                                double maxBottom = top;
                                foreach (var floatBox in precedingFloats)
                                {
                                    double fBottom = floatBox.ActualBottom;
                                    if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                        maxBottom = Math.Max(maxBottom, fBottom);
                                }

                                if (maxBottom <= top) break;
                                top = maxBottom;
                            }
                        }
                        else if (Float == CssConstants.Right)
                        {
                            // Iteratively resolve collisions with all prior floats (CSS1 §5.5.26)
                            for (int iter = 0; iter < 100; iter++)
                            {
                                left = containerRight - Size.Width - ActualMarginRight;

                                // Avoid overlapping with preceding right floats
                                foreach (var floatBox in precedingFloats)
                                {
                                    if (floatBox.Float == CssConstants.Right)
                                    {
                                        double fBottom = floatBox.ActualBottom;
                                        if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                            left = Math.Min(left, floatBox.Location.X - floatBox.ActualMarginLeft - Size.Width - ActualMarginRight);
                                    }
                                }

                                // Ensure right float doesn't overlap with left floats
                                double leftFloatEdge = containerLeft;
                                foreach (var floatBox in precedingFloats)
                                {
                                    if (floatBox.Float == CssConstants.Left)
                                    {
                                        double fBottom = floatBox.ActualBottom;
                                        if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                            leftFloatEdge = Math.Max(leftFloatEdge, floatBox.Location.X + floatBox.Size.Width + floatBox.ActualMarginRight);
                                    }
                                }

                                if (left >= leftFloatEdge)
                                    break;

                                // Move below the lowest overlapping float
                                double maxBottom = top;
                                foreach (var floatBox in precedingFloats)
                                {
                                    double fBottom = floatBox.ActualBottom;
                                    if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                        maxBottom = Math.Max(maxBottom, fBottom);
                                }

                                if (maxBottom <= top) break;
                                top = maxBottom;
                            }
                        }
                    }

                    // CSS2.1 §8.3.1/§9.5.2: Handle clear property.  Clearance
                    // inhibits margin collapsing and pushes the border edge of the
                    // cleared element below the bottom outer edge of the relevant
                    // floats.  Clearance can be negative when the uncollapsed
                    // position is already past the float.
                    if (Clear != CssConstants.None)
                    {
                        double maxFloatBottom = CssBoxHelper.GetMaxFloatBottom(this);
                        if (maxFloatBottom > 0)
                        {
                            double hypotheticalTop = top;

                            // Compute uncollapsed position: margins are NOT
                            // collapsed when clearance is present (§8.3.1).
                            // Use the effective margin for empty collapsible
                            // boxes (§8.3.1 margin-through-collapse).
                            double uncollapsedTop;
                            if (flowPrev != null)
                            {
                                double prevMarginBottom = (flowPrev is CssBox fpb)
                                    ? CssBoxHelper.GetEffectiveMarginBottom(fpb)
                                    : flowPrev.ActualMarginBottom;
                                uncollapsedTop = flowPrevBottom
                                    + prevMarginBottom
                                    + ActualMarginTop;
                            }
                            else if (ParentBox != null)
                            {
                                uncollapsedTop = ParentBox.ClientTop + ActualMarginTop;
                            }
                            else
                            {
                                uncollapsedTop = hypotheticalTop;
                            }

                            // CSS2.2 §9.5.2: Only introduce clearance when the
                            // hypothetical position (where the top border edge
                            // would be if 'clear' were 'none') is NOT past the
                            // relevant floats.  When the margin alone already
                            // places the element past the float, no clearance is
                            // needed and margin collapsing is preserved.
                            if (hypotheticalTop < maxFloatBottom)
                            {
                                // clearance = max(amount to clear float, amount to
                                // reach hypothetical position).  This can be negative.
                                double clearance = Math.Max(
                                    maxFloatBottom - uncollapsedTop,
                                    hypotheticalTop - uncollapsedTop);

                                top = uncollapsedTop + clearance;
                            }
                        }
                    }

                    // CSS2.1 §9.5: The border box of an element in normal
                    // flow that establishes a new BFC must not overlap the
                    // margin box of any floats in the same BFC.  Shift the
                    // block right past left floats and narrow it to avoid
                    // right floats.  If it cannot fit beside the floats,
                    // clear below them.
                    if (Float == CssConstants.None
                        && Position != CssConstants.Absolute && Position != CssConstants.Fixed)
                    {
                        bool isBfcRoot = Display == CssConstants.InlineBlock
                            || Display == CssConstants.TableCell
                            || Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                            || (Overflow != null && Overflow != CssConstants.Visible)
                            || (AlignContent != null && AlignContent != "normal");

                        if (isBfcRoot)
                        {
                            var precedingFloats = CssBoxHelper.CollectPrecedingFloatsInBfc(this);
                            if (precedingFloats.Count > 0)
                            {
                                double containerLeft = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ContainingBlock.ActualBorderLeftWidth;
                                double containerRight = ContainingBlock.ClientLeft + ContainingBlock.AvailableWidth;
                                double boxHeight = Math.Max(Size.Height, GetEmHeight());

                                // Try to fit beside floats; if not possible, clear
                                // below them.  100 iterations is a safe upper bound
                                // since each iteration advances past at least one
                                // float's bottom edge.
                                for (int bfcIter = 0; bfcIter < 100; bfcIter++)
                                {
                                    double leftEdge = containerLeft + ActualMarginLeft;
                                    double rightEdge = containerRight - ActualMarginRight;

                                    foreach (var fb in precedingFloats)
                                    {
                                        double fbBottom = fb.ActualBottom + fb.ActualMarginBottom;
                                        if (top < fbBottom && top + boxHeight > fb.Location.Y - fb.ActualMarginTop)
                                        {
                                            if (fb.Float == CssConstants.Left)
                                                leftEdge = Math.Max(leftEdge, fb.Location.X + fb.Size.Width + fb.ActualMarginRight + ActualMarginLeft);
                                            else if (fb.Float == CssConstants.Right)
                                                rightEdge = Math.Min(rightEdge, fb.Location.X - fb.ActualMarginLeft - ActualMarginRight);
                                        }
                                    }

                                    double availableWidth = rightEdge - leftEdge;
                                    if (availableWidth >= Size.Width || availableWidth >= 0)
                                    {
                                        left = leftEdge;
                                        if (availableWidth < Size.Width && (Width == CssConstants.Auto || string.IsNullOrEmpty(Width)))
                                            Size = new SizeF((float)availableWidth, Size.Height);
                                        break;
                                    }

                                    // Cannot fit beside floats — clear below them.
                                    double maxFb = top;
                                    foreach (var fb in precedingFloats)
                                    {
                                        double fbBottom = fb.ActualBottom + fb.ActualMarginBottom;
                                        if (top < fbBottom && top + boxHeight > fb.Location.Y - fb.ActualMarginTop)
                                            maxFb = Math.Max(maxFb, fbBottom);
                                    }
                                    if (maxFb <= top) break;
                                    top = maxFb;
                                }
                            }
                        }
                    }

                    Location = new PointF((float)left, (float)top);
                    ActualBottom = top;

                    // CSS2.1 §10.3.7 / §10.6.4: For absolutely positioned
                    // elements with explicit 'top'/'left', override the static
                    // position with the CSS-specified offset from the containing
                    // block's padding edge.
                    if (Position == CssConstants.Absolute)
                    {
                        var cb = FindPositionedContainingBlock();
                        GetAbsoluteContainingBlockPaddingBox(cb, out double cbPadLeft, out double cbPadTop, out double cbPadWidth, out double cbPadHeight);

                        float newX = Location.X, newY = Location.Y;

                        if (Left != null && Left != CssConstants.Auto)
                        {
                            double cssLeft = CssValueParser.ParseLength(Left, cbPadWidth, GetEmHeight());
                            newX = (float)(cbPadLeft + cssLeft + ActualMarginLeft);
                        }
                        else if (Right != null && Right != CssConstants.Auto)
                        {
                            // CSS2.1 §10.3.7: When left is auto and right is
                            // specified, position from the right padding edge.
                            double cssRight = CssValueParser.ParseLength(Right, cbPadWidth, GetEmHeight());
                            newX = (float)(cbPadLeft + cbPadWidth - cssRight - ActualMarginRight - Size.Width);
                        }

                        if (Top != null && Top != CssConstants.Auto)
                        {
                            double cssTop = CssValueParser.ParseLength(Top, cbPadHeight, GetEmHeight());
                            newY = (float)(cbPadTop + cssTop + ActualMarginTop);
                        }
                        else if (Bottom != null && Bottom != CssConstants.Auto)
                        {
                            // CSS2.1 §10.6.4: When top is auto and bottom is
                            // specified, position from the bottom padding edge.
                            double cssBottom = CssValueParser.ParseLength(Bottom, cbPadHeight, GetEmHeight());
                            double boxHeight = ActualBottom - Location.Y;
                            // boxHeight may be zero when the box position was
                            // just initialised and children have not yet been
                            // laid out.  Fall back to Size.Height which reflects
                            // any explicit CSS height already applied.
                            if (boxHeight <= 0) boxHeight = Size.Height;
                            newY = (float)(cbPadTop + cbPadHeight - cssBottom - ActualMarginBottom - boxHeight);
                        }

                        Location = new PointF(newX, newY);
                        ActualBottom = newY;
                    }

                    // CSS2.1 §10.6.4 / §9.6.1: For fixed-position elements,
                    // the containing block is the viewport.  When top/left/
                    // bottom/right are explicitly set, use those offsets from
                    // the viewport edge.  When they are auto, the static
                    // position (computed above) is kept.
                    if (Position == CssConstants.Fixed && ContainerInt != null)
                    {
                        bool hasLeft = Left != null && Left != CssConstants.Auto;
                        bool hasRight = Right != null && Right != CssConstants.Auto;
                        bool hasTop = Top != null && Top != CssConstants.Auto;
                        bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

                        if (hasLeft || hasRight || hasTop || hasBottom)
                        {
                            var vpSize = ContainerInt.ViewportSize;
                            float newX = Location.X, newY = Location.Y;

                            if (hasLeft)
                            {
                                double cssLeft = CssValueParser.ParseLength(Left, vpSize.Width, GetEmHeight());
                                newX = (float)(cssLeft + ActualMarginLeft);
                            }
                            else if (hasRight)
                            {
                                double cssRight = CssValueParser.ParseLength(Right, vpSize.Width, GetEmHeight());
                                newX = (float)(vpSize.Width - cssRight - ActualMarginRight - Size.Width);
                            }

                            if (hasTop)
                            {
                                double cssTop = CssValueParser.ParseLength(Top, vpSize.Height, GetEmHeight());
                                newY = (float)(cssTop + ActualMarginTop);
                            }
                            else if (hasBottom)
                            {
                                double cssBottom = CssValueParser.ParseLength(Bottom, vpSize.Height, GetEmHeight());
                                double boxHeight = ActualBottom - Location.Y;
                                if (boxHeight <= 0) boxHeight = Size.Height;
                                newY = (float)(vpSize.Height - cssBottom - ActualMarginBottom - boxHeight);
                            }

                            Location = new PointF(newX, newY);
                            ActualBottom = newY;
                        }
                        // When all offsets are auto, keep the static position
                        // (Location is already set from normal-flow
                        // calculation above).
                    }
                }
            }

            // CSS2.1 §10.5: Pre-resolve percentage heights so that children
            // can use ContainingBlock.Size.Height for their own percentage
            // height resolution.  This must run AFTER position assignment
            // (which resets Size.Height to 0 via ActualBottom = top) but
            // BEFORE child layout so descendants see the correct height.
            if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
                && Height.Contains('%') && !HeightPercentageResolvesToAuto())
            {
                double cbHeight;
                if (Position == CssConstants.Fixed && ContainerInt != null)
                    cbHeight = ContainerInt.ViewportSize.Height;
                else if (ContainingBlock?.ParentBox == null && ContainerInt != null)
                    cbHeight = ContainerInt.ViewportSize.Height;
                else
                    cbHeight = ContainingBlock.Size.Height;
                double preHeight = ResolveSpecifiedHeightToBorderBox(
                    CssValueParser.ParseLength(Height, cbHeight, GetEmHeight()));
                Size = new SizeF(Size.Width, (float)preHeight);
            }

            //If we're talking about a table here..
            if (Display == CssConstants.Table || Display == CssConstants.InlineTable)
            {
                CssLayoutEngineTable.PerformLayout(g, this, BaseUrl);
            }
            else
            {
                // CSS Flexbox §8.2/§8.4: Map flex alignment properties to
                // CSS2.1 text-align so that the inline formatting context
                // fallback (FlowInlineBlock) produces visually aligned items.
                // This only applies when the author has not set text-align
                // explicitly (i.e. it still has the default 'left' value).
                if (Display is "flex" or "inline-flex" or "grid" or "inline-grid")
                {
                    if (JustifyContent is "center" &&
                        TextAlign is CssConstants.Left or "start" or "")
                    {
                        TextAlign = CssConstants.Center;
                    }
                    else if (JustifyContent is "flex-end" or "end" &&
                        TextAlign is CssConstants.Left or "start" or "")
                    {
                        TextAlign = CssConstants.Right;
                    }
                }

                //If there's just inline boxes, create LineBoxes
                if (DomUtils.ContainsInlinesOnly(this))
                {
                    ActualBottom = Location.Y;
                    CssLayoutEngine.CreateLineBoxes(g, this); //This will automatically set the bottom of this block

                    // CSS2.1 §9.5: Floated children were skipped by
                    // CreateLineBoxes (they are out-of-flow).  Lay them out
                    // now so they are positioned and painted.
                    foreach (var childBox in Boxes)
                    {
                        if (childBox.Float != CssConstants.None)
                        {
                            childBox.PerformLayout(g);

                            // CSS2.1 §13.3.1: When page-break-inside:avoid is
                            // set on a float's containing block, move the float
                            // to the next page if it would otherwise cross a
                            // page boundary.
                            if (PageBreakInside == CssConstants.Avoid)
                                childBox.BreakPage();
                        }
                    }

                    // CSS2.1 §10.6.7: Elements that establish a new block
                    // formatting context (BFC) must include descendant floats
                    // in their auto-height calculation.  The inline path above
                    // does not call MarginBottomCollapse(), so BFC elements
                    // with only floated children would otherwise have zero
                    // content height.
                    bool isBfc = Float != CssConstants.None
                        || Display == CssConstants.InlineBlock
                        || Display == CssConstants.TableCell
                        || Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                        || (Overflow != null && Overflow != CssConstants.Visible)
                        || Position == CssConstants.Absolute
                        || Position == CssConstants.Fixed
                        || (AlignContent != null && AlignContent != "normal");
                    if (isBfc)
                    {
                        ActualBottom = MarginBottomCollapse();
                    }

                    // CSS Grid Level 1 §8.5: When all grid items share
                    // the same grid-row and grid-column, reposition them
                    // to the container's content-area origin so they
                    // overlap visually.  (This duplicates the same logic
                    // in the block path below; it is needed here because
                    // ContainsInlinesOnly() forces grid containers into
                    // the inline layout path for shrink-to-fit sizing.)
                    if (Display is "grid" or "inline-grid")
                    {
                        if (!ApplyGridStacking())
                            ApplyGridAutoPlacement();
                    }
                }
                else if (Boxes.Count > 0)
                {
                    // CSS Multi-column: Pre-constrain width so children
                    // lay out at column width instead of full container width.
                    float savedWidth = Size.Width;
                    int preColCount = 0;
                    bool hasExplicitColCount = ColumnCount != null && ColumnCount != "auto"
                        && int.TryParse(ColumnCount, out preColCount) && preColCount > 1;
                    bool hasColWidth = ColumnWidth != null && ColumnWidth != "auto"
                        && !string.IsNullOrEmpty(ColumnWidth);

                    bool isMultiColumn = hasExplicitColCount || hasColWidth;
                    if (isMultiColumn && !hasExplicitColCount && hasColWidth)
                    {
                        // Auto column-count from column-width: compute the
                        // number of columns so we can pre-constrain width.
                        double cwVal = CssValueParser.ParseLength(ColumnWidth, Size.Width, GetEmHeight());
                        double gap = GetEmHeight();
                        double available = Size.Width - ActualPaddingLeft - ActualPaddingRight
                            - ActualBorderLeftWidth - ActualBorderRightWidth;
                        if (cwVal > 0 && available > 0)
                            preColCount = Math.Max(1, (int)Math.Floor((available + gap) / (cwVal + gap)));
                        isMultiColumn = preColCount > 1;
                    }

                    if (isMultiColumn && preColCount > 1)
                    {
                        double columnGap = GetEmHeight();
                        double cw = Size.Width - ActualPaddingLeft - ActualPaddingRight
                            - ActualBorderLeftWidth - ActualBorderRightWidth;
                        double colWidth = (cw - (preColCount - 1) * columnGap) / preColCount;
                        if (colWidth > 0)
                            Size = new SizeF((float)colWidth, Size.Height);
                    }

                    foreach (var childBox in Boxes)
                    {
                        childBox.PerformLayout(g);

                        // CSS2.1 §13.3.1: When page-break-inside:avoid is
                        // set, move floated children to the next page if they
                        // would cross a page boundary.
                        if (childBox.Float != CssConstants.None
                            && PageBreakInside == CssConstants.Avoid)
                            childBox.BreakPage();
                    }

                    // Restore original width after children are laid out.
                    if (isMultiColumn)
                        Size = new SizeF(savedWidth, Size.Height);

                    ActualRight = CalculateActualRight();
                    ActualBottom = MarginBottomCollapse();

                    if (Display is "grid" or "inline-grid")
                    {
                        if (!ApplyGridStacking())
                            ApplyGridAutoPlacement();
                    }
                }
            }
        }
        else
        {
            var prevSibling = DomUtils.GetPreviousSibling(this);
            if (prevSibling != null)
            {
                if (Location == PointF.Empty)
                    Location = prevSibling.Location;

                ActualBottom = prevSibling.ActualBottom;
            }
        }

        // CSS Multi-column Layout §3: When column-count > 1 or column-width
        // is specified, redistribute in-flow children into multiple columns.
        // This is a post-layout transformation that moves children
        // horizontally and vertically to simulate multi-column flow.
        {
            int colCount = 0;
            bool hasExplicitCount = ColumnCount != null && ColumnCount != "auto"
                && int.TryParse(ColumnCount, out colCount) && colCount > 1;
            bool hasColumnWidth = ColumnWidth != null && ColumnWidth != "auto"
                && !string.IsNullOrEmpty(ColumnWidth);

            if (!hasExplicitCount && hasColumnWidth)
            {
                // Auto column-count from column-width: CSS Multi-column §3.4
                double cw = CssValueParser.ParseLength(ColumnWidth, Size.Width, GetEmHeight());
                double gap = GetEmHeight();
                double available = Size.Width - ActualPaddingLeft - ActualPaddingRight
                    - ActualBorderLeftWidth - ActualBorderRightWidth;
                if (cw > 0 && available > 0)
                    colCount = Math.Max(1, (int)Math.Floor((available + gap) / (cw + gap)));
            }

            if (colCount > 1 && Boxes.Count > 0)
            {
                ApplyMultiColumnLayout(colCount);
            }
        }

        // CSS content-box model: 'height' specifies the content height only;
        // padding and border are additive (CSS2.1 §10.6.3).
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height))
        {
            // CSS2.1 §10.5: If height is a percentage and the containing
            // block's height is not explicitly specified (auto), the
            // percentage resolves to auto and this constraint is skipped.
            if (!HeightPercentageResolvesToAuto())
            {
                // CSS2.1 §10.5: Percentage heights resolve against the
                // containing block's height, not the element's own size.
                // ActualHeight uses Size.Height (the element's own height
                // from child layout), which is wrong for percentage values.
                // Resolve against the containing block's height instead.
                double contentHeight;
                if (Height.Contains('%'))
                {
                    double cbHeight;
                    if (Position == CssConstants.Fixed && ContainerInt != null)
                        cbHeight = ContainerInt.ViewportSize.Height;
                    else if (ContainingBlock?.ParentBox == null && ContainerInt != null)
                        cbHeight = ContainerInt.ViewportSize.Height;
                    else
                        cbHeight = ContainingBlock.Size.Height;
                    contentHeight = CssValueParser.ParseLength(Height, cbHeight, GetEmHeight());
                }
                else
                {
                    contentHeight = string.Equals(Height, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                        ? GetParent().ActualHeight
                        : ActualHeight;
                }

                double borderBoxHeight = ResolveSpecifiedHeightToBorderBox(contentHeight);

                // CSS2.1 §10.6.3: An explicit height sets the content box
                // height.  Content that exceeds this height overflows
                // (visible by default) but does not affect sibling
                // positioning.  Use direct assignment so that explicit
                // height (e.g. height:0) can override the height computed
                // by CreateLineBoxes (e.g. from line-height).
                ActualBottom = Location.Y + borderBoxHeight;
            }
        }
        else if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
            && Top != null && Top != CssConstants.Auto
            && Bottom != null && Bottom != CssConstants.Auto
            && (Height == CssConstants.Auto || string.IsNullOrEmpty(Height)))
        {
            // CSS2.1 §10.6.4: For absolutely positioned, non-replaced
            // elements when height is auto and both top and bottom are
            // specified, compute height from the constraint equation:
            // top + margin-top + height + margin-bottom + bottom = CB height
            double cbHeight;
            if (Position == CssConstants.Fixed && ContainerInt != null)
                cbHeight = ContainerInt.ViewportSize.Height;
            else
            {
                var cb = FindPositionedContainingBlock();
                GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out _, out cbHeight);
            }
            double cssTop = CssValueParser.ParseLength(Top, cbHeight, GetEmHeight());
            double cssBottom = CssValueParser.ParseLength(Bottom, cbHeight, GetEmHeight());
            double resolvedHeight = cbHeight - cssTop - cssBottom - ActualMarginTop - ActualMarginBottom
                - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;
            if (resolvedHeight < 0) resolvedHeight = 0;
            double borderBoxH = resolvedHeight + ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth;
            ActualBottom = Location.Y + borderBoxH;
        }

        // CSS2.1 §10.7: Apply min-height / max-height constraints.
        // When min-height > max-height, min-height wins.
        {
            double contentHeight = ActualBottom - Location.Y - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;
            bool constrained = false;

            // CSS2.1 §9.6.1: For fixed-position elements, percentage
            // heights resolve against the viewport, not the parent.
            double cbHeight = (Position == CssConstants.Fixed && ContainerInt != null)
                ? ContainerInt.ViewportSize.Height
                : ContainingBlock.Size.Height;

            if (MaxHeight != "none" && !string.IsNullOrEmpty(MaxHeight))
            {
                // CSS2.1 §10.7: If the containing block's height is not
                // specified explicitly and this element is not absolutely
                // positioned, a percentage max-height is treated as 'none'.
                // Exception: the initial containing block always has a
                // definite height (the viewport), per §10.5.
                bool maxIsPercentageAuto = MaxHeight.Contains('%')
                    && Position != CssConstants.Absolute && Position != CssConstants.Fixed
                    && ContainingBlock?.ParentBox != null
                    && (ContainingBlock.Height == CssConstants.Auto || string.IsNullOrEmpty(ContainingBlock.Height));

                if (!maxIsPercentageAuto)
                {
                    double maxH = CssValueParser.ParseLength(MaxHeight, cbHeight, GetEmHeight());
                    if (contentHeight > maxH)
                    {
                        contentHeight = maxH;
                        constrained = true;
                    }
                }
            }

            if (MinHeight != "0" && !string.IsNullOrEmpty(MinHeight))
            {
                // CSS2.1 §10.7: If the containing block's height is not
                // specified explicitly and this element is not absolutely
                // positioned, a percentage min-height is treated as '0'.
                // Exception: the initial containing block always has a
                // definite height (the viewport), per §10.5.
                bool minIsPercentageAuto = MinHeight.Contains('%')
                    && Position != CssConstants.Absolute && Position != CssConstants.Fixed
                    && ContainingBlock?.ParentBox != null
                    && (ContainingBlock.Height == CssConstants.Auto || string.IsNullOrEmpty(ContainingBlock.Height));

                if (!minIsPercentageAuto)
                {
                    double minH = CssValueParser.ParseLength(MinHeight, cbHeight, GetEmHeight());
                    if (contentHeight < minH)
                    {
                        contentHeight = minH;
                        constrained = true;
                    }
                }
            }

            if (constrained)
            {
                ActualBottom = Location.Y + ResolveSpecifiedHeightToBorderBox(contentHeight);
            }
        }

        // Floats with an explicit CSS height establish a new BFC.
        // Their ActualBottom should reflect the stated height, not
        // content overflow from child floats (CSS2.1 §10.6.1).
        // CSS2.1 §10.5: Percentage heights resolve to auto when
        // the containing block's height is not explicitly specified.
        if (Float != CssConstants.None && Height != CssConstants.Auto && !string.IsNullOrEmpty(Height))
        {
            if (!HeightPercentageResolvesToAuto())
            {
                // For percentage heights, resolve against the containing
                // block's height directly.  ActualHeight resolves against
                // Size.Height which may have been cached before the
                // percentage height pre-resolution step set the correct
                // Size.Height (CSS2.1 §10.5).
                double contentHeight;
                if (Height.Contains('%'))
                {
                    double cbHeight;
                    if (Position == CssConstants.Fixed && ContainerInt != null)
                        cbHeight = ContainerInt.ViewportSize.Height;
                    else if (ContainingBlock?.ParentBox == null && ContainerInt != null)
                        cbHeight = ContainerInt.ViewportSize.Height;
                    else
                        cbHeight = ContainingBlock.Size.Height;
                    contentHeight = CssValueParser.ParseLength(Height, cbHeight, GetEmHeight());
                }
                else
                {
                    contentHeight = ActualHeight;
                }
                double borderBoxHeight = ResolveSpecifiedHeightToBorderBox(contentHeight);
                ActualBottom = Location.Y + borderBoxHeight;
            }
        }

        if (Position == CssConstants.Absolute)
        {
            bool hasLeft = Left != null && Left != CssConstants.Auto;
            bool hasRight = Right != null && Right != CssConstants.Auto;
            bool hasTop = Top != null && Top != CssConstants.Auto;
            bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

            if ((!hasLeft && hasRight) || (!hasTop && hasBottom))
            {
                var cb = FindPositionedContainingBlock();
                GetAbsoluteContainingBlockPaddingBox(cb, out double cbPadLeft, out double cbPadTop, out double cbPadWidth, out double cbPadHeight);

                float newX = Location.X;
                float newY = Location.Y;

                if (!hasLeft && hasRight)
                {
                    double boxWidth = ActualRight - Location.X;
                    if (boxWidth <= 0)
                        boxWidth = Size.Width;

                    double cssRight = CssValueParser.ParseLength(Right, cbPadWidth, GetEmHeight());
                    newX = (float)(cbPadLeft + cbPadWidth - cssRight - ActualMarginRight - boxWidth);
                }

                if (!hasTop && hasBottom)
                {
                    double boxHeight = ActualBottom - Location.Y;
                    if (boxHeight <= 0)
                        boxHeight = Size.Height;

                    double cssBottom = CssValueParser.ParseLength(Bottom, cbPadHeight, GetEmHeight());
                    newY = (float)(cbPadTop + cbPadHeight - cssBottom - ActualMarginBottom - boxHeight);
                }

                float deltaX = newX - Location.X;
                float deltaY = newY - Location.Y;
                if (deltaX != 0)
                    OffsetLeft(deltaX);
                if (deltaY != 0)
                {
                    OffsetTop(deltaY);
                    ActualBottom += deltaY;
                }
            }

            // CSS Box Alignment Level 3 §6.1: Post-layout self-alignment for
            // absolutely positioned elements.  After children are laid out,
            // shrink the box to fit-content size and align within the IMCB.
            // This must run after child layout so content dimensions are known.
            string jsPost = JustifySelf?.Trim().ToLowerInvariant() ?? "auto";
            bool jsPostNonDefault = jsPost != "auto" && jsPost != "normal" && jsPost != "stretch";
            string asPost = AlignSelf?.Trim().ToLowerInvariant() ?? "auto";
            bool asPostNonDefault = asPost != "auto" && asPost != "normal" && asPost != "stretch";

            if (jsPostNonDefault || asPostNonDefault)
            {
                var cb = FindPositionedContainingBlock();
                GetAbsoluteContainingBlockPaddingBox(cb, out double cbPadLeft, out double cbPadTop, out double cbPadWidth, out double cbPadHeight);

                bool hasL = Left != null && Left != CssConstants.Auto;
                bool hasR = Right != null && Right != CssConstants.Auto;
                bool hasT = Top != null && Top != CssConstants.Auto;
                bool hasB = Bottom != null && Bottom != CssConstants.Auto;

                // CSS Writing Modes Level 4: the containing block's writing mode
                // determines which physical axis corresponds to justify-self (inline)
                // and align-self (block).
                bool cbVertical = cb.WritingMode == "vertical-rl" || cb.WritingMode == "vertical-lr";

                float newX = Location.X, newY = Location.Y;

                // justify-self controls the inline axis:
                //   horizontal-tb → horizontal (L/R insets)
                //   vertical-rl/lr → vertical (T/B insets)
                if (jsPostNonDefault)
                {
                    if (!cbVertical && hasL && hasR)
                    {
                        double cssLeft = CssValueParser.ParseLength(Left, cbPadWidth, GetEmHeight());
                        double cssRight = CssValueParser.ParseLength(Right, cbPadWidth, GetEmHeight());
                        double imcbLeft = cbPadLeft + cssLeft;
                        double imcbWidth = cbPadWidth - cssLeft - cssRight;

                        double boxWidth = GetShrinkToFitWidth();
                        Size = new SizeF((float)boxWidth, Size.Height);

                        bool isRtl = Direction == "rtl";
                        double dx = ResolveAbsposSelfAlignment(
                            jsPost, imcbWidth, boxWidth, isRtl);
                        newX = (float)(imcbLeft + dx + ActualMarginLeft);
                    }
                    else if (cbVertical && hasT && hasB)
                    {
                        double cssTop = CssValueParser.ParseLength(Top, cbPadHeight, GetEmHeight());
                        double cssBottom = CssValueParser.ParseLength(Bottom, cbPadHeight, GetEmHeight());
                        double imcbTop = cbPadTop + cssTop;
                        double imcbHeight = cbPadHeight - cssTop - cssBottom;

                        double boxHeight = GetShrinkToFitHeight();

                        double dy = ResolveAbsposSelfAlignment(
                            jsPost, imcbHeight, boxHeight, false);
                        newY = (float)(imcbTop + dy + ActualMarginTop);
                    }
                }

                // align-self controls the block axis:
                //   horizontal-tb → vertical (T/B insets)
                //   vertical-rl/lr → horizontal (L/R insets)
                if (asPostNonDefault)
                {
                    if (!cbVertical && hasT && hasB)
                    {
                        double cssTop = CssValueParser.ParseLength(Top, cbPadHeight, GetEmHeight());
                        double cssBottom = CssValueParser.ParseLength(Bottom, cbPadHeight, GetEmHeight());
                        double imcbTop = cbPadTop + cssTop;
                        double imcbHeight = cbPadHeight - cssTop - cssBottom;

                        double boxHeight = GetShrinkToFitHeight();

                        double dy = ResolveAbsposSelfAlignment(
                            asPost, imcbHeight, boxHeight, false);
                        newY = (float)(imcbTop + dy + ActualMarginTop);
                    }
                    else if (cbVertical && hasL && hasR)
                    {
                        double cssLeft = CssValueParser.ParseLength(Left, cbPadWidth, GetEmHeight());
                        double cssRight = CssValueParser.ParseLength(Right, cbPadWidth, GetEmHeight());
                        double imcbLeft = cbPadLeft + cssLeft;
                        double imcbWidth = cbPadWidth - cssLeft - cssRight;

                        double boxWidth = GetShrinkToFitWidth();
                        Size = new SizeF((float)boxWidth, Size.Height);

                        bool isRtl = Direction == "rtl";
                        double dx = ResolveAbsposSelfAlignment(
                            asPost, imcbWidth, boxWidth, isRtl);
                        newX = (float)(imcbLeft + dx + ActualMarginLeft);
                    }
                }

                if (newX != Location.X || newY != Location.Y)
                {
                    float deltaX = newX - Location.X;
                    float deltaY = newY - Location.Y;
                    if (deltaX != 0)
                        OffsetLeft(deltaX);
                    if (deltaY != 0)
                    {
                        OffsetTop(deltaY);
                        ActualBottom += deltaY;
                    }
                }
            }
        }

        // CSS Box Alignment Level 3 §5.4: align-content on block containers
        // shifts the in-flow content vertically when the container has a
        // definite height larger than the content.  Values:
        //   normal/start/baseline/flex-start → no shift (top-aligned)
        //   center                           → center vertically
        //   end/flex-end/last baseline       → bottom-aligned
        //   space-between/space-around/space-evenly → distribute space
        // The "unsafe" and "safe" prefixes are stripped; safe alignment
        // falls back to start when content overflows, but for blocks this
        // is handled implicitly (shift is clamped to ≥ 0).
        if (AlignContent != null && AlignContent != "normal"
            && (IsBlock || Display == CssConstants.ListItem || Display == CssConstants.InlineBlock
                || Display == CssConstants.TableCell)
            && Boxes.Count > 0
            && (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
                || Display == CssConstants.TableCell))
        {
            double borderBoxHeight = ActualBottom - Location.Y;
            double containerContentHeight = borderBoxHeight
                - ActualPaddingTop - ActualPaddingBottom
                - ActualBorderTopWidth - ActualBorderBottomWidth;

            // Compute height of in-flow children (excluding overflow:0 boxes,
            // absolutely positioned elements, and fixed elements).
            double contentTop = double.MaxValue;
            double contentBottom = double.MinValue;
            foreach (var child in Boxes)
            {
                if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                    continue;
                if (child.Display == CssConstants.None)
                    continue;
                double childTop = child.Location.Y;
                double childBottom = child.ActualBottom;
                if (childTop < contentTop)
                    contentTop = childTop;
                if (childBottom > contentBottom)
                    contentBottom = childBottom;
            }

            if (contentTop < double.MaxValue && contentBottom > double.MinValue)
            {
                double usedContentHeight = contentBottom - contentTop;
                double freeSpace = containerContentHeight - usedContentHeight;

                // Normalise the align-content value: strip safe/unsafe prefix.
                string ac = AlignContent.Trim();
                bool explicitUnsafe = ac.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase);
                bool explicitSafe = ac.StartsWith("safe ", StringComparison.OrdinalIgnoreCase);
                if (explicitSafe)
                    ac = ac.Substring(5).Trim();
                else if (explicitUnsafe)
                    ac = ac.Substring(7).Trim();

                // CSS Box Alignment §5.3: when no explicit safe/unsafe keyword
                // is present, the default overflow alignment is "safe".
                bool isSafe = !explicitUnsafe;

                // Only compute shift when there's free space, or when unsafe
                // mode allows shifting even into overflow.
                if (freeSpace > 0.5 || (!isSafe && freeSpace < -0.5))
                {
                    double shift = 0;
                    switch (ac.ToLowerInvariant())
                    {
                        case "center":
                            shift = freeSpace / 2;
                            break;
                        case "end":
                        case "flex-end":
                        case "last baseline":
                            shift = freeSpace;
                            break;
                        case "space-between":
                            // Single content group → same as start (no shift).
                            break;
                        case "space-around":
                            shift = freeSpace / 2;
                            break;
                        case "space-evenly":
                            shift = freeSpace / 2;
                            break;
                        // start, flex-start, baseline, normal → no shift.
                    }

                    // Safe alignment: clamp shift to 0 to prevent overflow.
                    if (isSafe && shift < 0)
                        shift = 0;

                    if (Math.Abs(shift) > 0.5)
                    {
                        foreach (var child in Boxes)
                        {
                            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                                continue;
                            if (child.Display == CssConstants.None)
                                continue;
                            child.OffsetTop(shift);
                        }
                    }
                }
            }
        }

        // CSS Box Alignment Level 3 §6.1: justify-self on block-level boxes.
        // When a non-replaced block has an explicit width narrower than its
        // containing block, 'justify-self' shifts the box horizontally within
        // the containing block's content area.  Values:
        //   auto/normal/stretch → default behaviour (no shift)
        //   start/flex-start/self-start/left → left-aligned (no shift in LTR)
        //   end/flex-end/self-end/right → right-aligned
        //   center → centered
        // Floated and absolutely/fixed positioned boxes are unaffected.
        if (JustifySelf != null && JustifySelf != "auto" && JustifySelf != "normal"
            && JustifySelf != "stretch"
            && Float == CssConstants.None
            && Position != CssConstants.Absolute && Position != CssConstants.Fixed
            && (IsBlock || Display == CssConstants.ListItem)
            && ParentBox != null)
        {
            double boxWidth = ActualRight - Location.X;
            double containerWidth = ParentBox.ClientRectangle.Width;
            double freeSpace = containerWidth - boxWidth;

            if (freeSpace > 0.5)
            {
                string js = JustifySelf.Trim().ToLowerInvariant();
                // CSS Box Alignment §6.1: 'start'/'end' use the containing
                // block's writing direction; 'self-start'/'self-end' use the
                // element's own writing direction.
                bool isElementRtl = Direction == "rtl";
                bool isContainerRtl = ParentBox?.Direction == "rtl";

                double dx = 0;
                switch (js)
                {
                    case "center":
                        dx = freeSpace / 2;
                        break;
                    case "end":
                    case "flex-end":
                        dx = isContainerRtl ? 0 : freeSpace;
                        break;
                    case "self-end":
                        dx = isElementRtl ? 0 : freeSpace;
                        break;
                    case "right":
                        dx = freeSpace;
                        break;
                    case "start":
                    case "flex-start":
                        dx = isContainerRtl ? freeSpace : 0;
                        break;
                    case "self-start":
                        dx = isElementRtl ? freeSpace : 0;
                        break;
                    case "left":
                        dx = 0;
                        break;
                }

                if (dx > 0.5)
                    OffsetLeft(dx);
            }
        }

        // Apply position:relative offset after layout (visual only, does not affect flow)
        // CSS2.1 §9.4.3: For relative positioning, 'left'/'right' and
        // 'top'/'bottom' form constraint pairs.  When 'top' is auto and
        // 'bottom' is not, dy = -bottom.  When both are non-auto, 'bottom'
        // is ignored (in LTR).  Same logic applies to left/right.
        if (Position == CssConstants.Relative)
        {
            double dx = 0, dy = 0;

            bool hasLeft = Left != null && Left != CssConstants.Auto;
            bool hasRight = Right != null && Right != CssConstants.Auto;
            bool hasTop = Top != null && Top != CssConstants.Auto;
            bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

            if (hasLeft)
                dx = CssValueParser.ParseLength(Left, Size.Width, GetEmHeight());
            else if (hasRight)
                dx = -CssValueParser.ParseLength(Right, Size.Width, GetEmHeight());

            if (hasTop)
                dy = CssValueParser.ParseLength(Top, Size.Height, GetEmHeight());
            else if (hasBottom)
                dy = -CssValueParser.ParseLength(Bottom, Size.Height, GetEmHeight());

            if (dx != 0)
                OffsetLeft(dx);
            if (dy != 0)
                OffsetTop(dy);
        }

        CreateListItemBox(g);

        if (!IsFixed)
        {
            var actualWidth = Math.Max(GetMinimumWidth() + CssBoxHelper.GetWidthMarginDeep(this), Size.Width < 90999 ? ActualRight - ContainerInt.RootLocation.X : 0);
            ContainerInt.ActualSize = CommonUtils.Max(ContainerInt.ActualSize, new SizeF((float)actualWidth, (float)(ActualBottom - ContainerInt.RootLocation.Y)));
        }
    }

    /// <summary>
    /// CSS Multi-column Layout: Redistributes in-flow child boxes into
    /// multiple columns after single-column layout.  Walks down through
    /// single-child containers (e.g. html to body) to find the actual
    /// fragmentable children.
    /// </summary>
    private void ApplyMultiColumnLayout(int colCount)
    {
        double columnGap = GetEmHeight();
        double contentWidth = Size.Width - ActualPaddingLeft - ActualPaddingRight
            - ActualBorderLeftWidth - ActualBorderRightWidth;
        double columnWidth = (contentWidth - (colCount - 1) * columnGap) / colCount;
        if (columnWidth <= 0) return;

        double containerTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double columnLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;

        // Walk down through single-child containers (html -> body) to find
        // the level with multiple block children to distribute.
        var fragmentParent = FindMultiColumnFragmentParent();
        if (fragmentParent == null) return;

        var fragments = new List<CssBox>();
        foreach (var child in fragmentParent.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;
            fragments.Add(child);
        }

        if (fragments.Count == 0) return;

        double firstTop = fragments[0].Location.Y;
        double lastBottom = GetVisualBottom(fragments[^1]);
        foreach (var frag in fragments)
        {
            double vb = GetVisualBottom(frag);
            if (vb > lastBottom) lastBottom = vb;
        }
        double totalContentHeight = lastBottom - firstTop;

        if (totalContentHeight <= 0) return;

        // Determine column height: balanced columns for auto/max-height,
        // or explicit height.
        bool hasMaxHeight = MaxHeight != "none" && !string.IsNullOrEmpty(MaxHeight);
        bool hasExplicitHeight = Height != CssConstants.Auto && !string.IsNullOrEmpty(Height);
        double maxAllowedHeight = double.MaxValue;

        if (hasMaxHeight)
        {
            double maxH = CssValueParser.ParseLength(MaxHeight, ContainingBlock?.Size.Height ?? Size.Height, GetEmHeight());
            maxAllowedHeight = maxH - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;
        }

        double columnHeight;
        if (hasExplicitHeight)
        {
            double h = CssValueParser.ParseLength(Height, ContainingBlock?.Size.Height ?? Size.Height, GetEmHeight());
            columnHeight = h;
        }
        else if (ColumnFill == "auto" && hasMaxHeight)
        {
            // column-fill: auto — fill columns sequentially up to max-height.
            columnHeight = maxAllowedHeight;
        }
        else
        {
            // Balanced column layout: find the minimum column height that
            // distributes all fragments across colCount columns.  Use a
            // binary search between (tallest fragment) and (total height).
            double lo = 0;
            foreach (var frag in fragments)
            {
                double fh = GetVisualBottom(frag) - frag.Location.Y;
                if (fh > lo) lo = fh;
            }
            double hi = totalContentHeight;

            for (int iter = 0; iter < 20; iter++)
            {
                double mid = (lo + hi) / 2;
                int cols = CountColumnsNeededVisual(fragments, mid);
                if (cols <= colCount)
                    hi = mid;
                else
                    lo = mid + 0.5;
            }
            columnHeight = Math.Ceiling(hi);

            if (columnHeight > maxAllowedHeight)
                columnHeight = maxAllowedHeight;
        }

        if (columnHeight <= 0) return;

        // CSS Fragmentation §3: When fragments contain boxes with visible
        // overflow that exceeds the column height (e.g. height: 0 parents
        // with overflowing children), flatten the hierarchy by collecting
        // the deepest fragmentable blocks from inside those containers.
        bool needsDeepFragment = false;
        foreach (var frag in fragments)
        {
            double visualH = GetVisualBottom(frag) - frag.Location.Y;
            if (visualH > columnHeight + 0.5 && frag.Boxes.Count > 0)
            {
                needsDeepFragment = true;
                break;
            }
        }

        if (needsDeepFragment)
        {
            var deepFragments = new List<CssBox>();
            foreach (var frag in fragments)
            {
                double visualH = GetVisualBottom(frag) - frag.Location.Y;
                if (visualH > columnHeight + 0.5 && frag.Boxes.Count > 0)
                {
                    CollectFragmentableBlocksCore(frag, columnHeight, deepFragments, 0);
                }
                else
                {
                    deepFragments.Add(frag);
                }
            }

            if (deepFragments.Count > fragments.Count)
            {
                fragments = deepFragments;
                firstTop = fragments[0].Location.Y;
                lastBottom = firstTop;
                foreach (var frag in fragments)
                {
                    double vb = GetVisualBottom(frag);
                    if (vb > lastBottom) lastBottom = vb;
                }
                totalContentHeight = lastBottom - firstTop;

                // Re-compute balanced column height for the new fragment set.
                if (!hasExplicitHeight && !(ColumnFill == "auto" && hasMaxHeight))
                {
                    double lo = 0;
                    foreach (var frag in fragments)
                    {
                        double fh = GetVisualBottom(frag) - frag.Location.Y;
                        if (fh > lo) lo = fh;
                    }
                    double hi = totalContentHeight;
                    for (int iter = 0; iter < 20; iter++)
                    {
                        double mid = (lo + hi) / 2;
                        int cols = CountColumnsNeededVisual(fragments, mid);
                        if (cols <= colCount) hi = mid;
                        else lo = mid + 0.5;
                    }
                    columnHeight = Math.Ceiling(hi);
                    if (columnHeight > maxAllowedHeight)
                        columnHeight = maxAllowedHeight;
                }
            }
        }

        // Distribute fragments across columns.
        int currentCol = 0;
        double currentY = containerTop;

        foreach (var frag in fragments)
        {
            double fragHeight = GetVisualBottom(frag) - frag.Location.Y;

            bool wouldOverflow = (currentY - containerTop) + fragHeight > columnHeight;
            if (wouldOverflow && currentCol < colCount - 1 && currentY > containerTop + 0.5)
            {
                currentCol++;
                currentY = containerTop;
            }

            double targetX = columnLeft + currentCol * (columnWidth + columnGap)
                + (frag.Location.X - fragmentParent.Location.X);
            double targetY = currentY;

            double dx = targetX - frag.Location.X;
            double dy = targetY - frag.Location.Y;

            if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1)
            {
                frag.OffsetLeft(dx);
                frag.OffsetTop(dy);
            }

            if (frag.Size.Width > columnWidth + 1)
            {
                frag.Size = new SizeF((float)columnWidth, frag.Size.Height);
            }

            currentY += fragHeight;
        }

        // Update container dimensions.
        double maxBottom = containerTop;
        foreach (var frag in fragments)
        {
            double vb = GetVisualBottom(frag);
            if (vb > maxBottom)
                maxBottom = vb;
        }

        double newBottom = maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
        if (newBottom < ActualBottom)
            ActualBottom = newBottom;

        if (fragmentParent != this)
        {
            double fpBottom = maxBottom + fragmentParent.ActualPaddingBottom + fragmentParent.ActualBorderBottomWidth;
            if (fpBottom < fragmentParent.ActualBottom)
                fragmentParent.ActualBottom = fpBottom;
        }

        double rightEdge = columnLeft + colCount * columnWidth + (colCount - 1) * columnGap
            + ActualPaddingRight + ActualBorderRightWidth;
        if (rightEdge > ActualRight)
            ActualRight = rightEdge;
    }

    /// <summary>
    /// Walks down through single-child containers to find the nearest
    /// descendant with multiple in-flow block children for multi-column
    /// fragmentation.
    /// </summary>
    private CssBox FindMultiColumnFragmentParent()
    {
        CssBox current = this;
        for (int depth = 0; depth < 10; depth++)
        {
            int inFlowCount = 0;
            CssBox onlyChild = null;
            foreach (var child in current.Boxes)
            {
                if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                    continue;
                if (child.Display == CssConstants.None)
                    continue;
                inFlowCount++;
                onlyChild = child;
            }

            if (inFlowCount > 1)
                return current;

            if (inFlowCount == 1 && onlyChild != null && onlyChild.Boxes.Count > 0)
            {
                current = onlyChild;
                continue;
            }

            break;
        }

        return current.Boxes.Count > 1 ? current : null;
    }


    /// <summary>
    /// Counts columns needed using visual (overflow-aware) heights.
    /// </summary>
    private static int CountColumnsNeededVisual(List<CssBox> fragments, double columnHeight)
    {
        int cols = 1;
        double currentH = 0;
        foreach (var frag in fragments)
        {
            double fh = GetVisualBottom(frag) - frag.Location.Y;
            if (currentH + fh > columnHeight && currentH > 0.5)
            {
                cols++;
                currentH = fh;
            }
            else
            {
                currentH += fh;
            }
        }
        return cols;
    }

    /// <summary>
    /// Returns the visual bottom of a box, accounting for children that
    /// overflow a constrained height (e.g. height: 0 with visible overflow).
    /// </summary>
    private static double GetVisualBottom(CssBox box)
    {
        double bottom = box.ActualBottom;
        foreach (var child in box.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;
            double cb = GetVisualBottom(child);
            if (cb > bottom) bottom = cb;
        }
        return bottom;
    }


    private static void CollectFragmentableBlocksCore(CssBox parent, double columnHeight,
        List<CssBox> result, int depth)
    {
        if (depth > 15) return; // safety limit

        foreach (var child in parent.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;

            double childHeight = GetVisualBottom(child) - child.Location.Y;

            // If child fits in a column, or has break-inside: avoid, or
            // has no block children to further fragment, keep it as-is.
            bool avoidBreak = child.BreakInside == "avoid" ||
                child.BreakInside == "avoid-column";
            bool hasBlockChildren = false;
            foreach (var gc in child.Boxes)
            {
                if (gc.Position != CssConstants.Absolute && gc.Position != CssConstants.Fixed
                    && gc.Display != CssConstants.None)
                {
                    hasBlockChildren = true;
                    break;
                }
            }

            if (childHeight <= columnHeight + 0.5 || avoidBreak || !hasBlockChildren)
            {
                result.Add(child);
            }
            else
            {
                // Recurse: this child is too tall and can be fragmented.
                CollectFragmentableBlocksCore(child, columnHeight, result, depth + 1);
            }
        }
    }

    /// <summary>
    /// Loads the CSS background image if one is specified and not yet loaded.
    /// Called from <see cref="MeasureWordsSize"/> and overridden versions in
    /// subclasses (e.g. <see cref="CssBoxImage"/>) that replace the base
    /// measurement logic.
    /// </summary>
    protected void LoadBackgroundImageIfNeeded()
    {
        if (BackgroundImage == CssConstants.None || _backgroundImagesInitialized)
            return;

        _backgroundImagesInitialized = true;
        var layers = SplitBackgroundImageLayers(BackgroundImage);
        if (layers.Count == 0)
            return;

        _backgroundImageLoadHandlers = new List<IImageLoadHandler?>(layers.Count);
        foreach (var layer in layers)
        {
            var src = TryExtractBackgroundImageUrl(layer);
            if (string.IsNullOrEmpty(src))
            {
                _backgroundImageLoadHandlers.Add(null);
                continue;
            }

            var imageLoadHandler = ContainerInt.CreateImageLoadHandler(OnImageLoadComplete);
            _backgroundImageLoadHandlers.Add(imageLoadHandler);
            imageLoadHandler.LoadImage(src, HtmlTag?.Attributes, BaseUrl);
        }
    }

    private static List<string> SplitBackgroundImageLayers(string backgroundImage)
    {
        var layers = new List<string>();
        if (string.IsNullOrWhiteSpace(backgroundImage))
            return layers;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < backgroundImage.Length; i++)
        {
            switch (backgroundImage[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0)
                        depth--;
                    break;
                case ',' when depth == 0:
                    layers.Add(backgroundImage[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        layers.Add(backgroundImage[start..].Trim());
        return layers;
    }

    private static string? TryExtractBackgroundImageUrl(string layer)
    {
        if (string.IsNullOrWhiteSpace(layer))
            return null;

        layer = layer.Trim();
        if (!layer.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return layer.Contains('(') ? null : layer;

        if (!layer.EndsWith(")", StringComparison.Ordinal))
            return null;

        var src = layer.Substring(4, layer.Length - 5).Trim();
        if (src.Length >= 2 &&
            ((src[0] == '\'' && src[^1] == '\'') ||
             (src[0] == '"' && src[^1] == '"')))
        {
            src = src[1..^1];
        }

        return src;
    }

    internal virtual void MeasureWordsSize(RGraphics g)
    {
        if (_wordsSizeMeasured)
            return;

        LoadBackgroundImageIfNeeded();
        MeasureWordSpacing(g);

        if (Words.Count > 0)
        {
            foreach (var boxWord in Words)
            {
                boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text, ActualFont).Width : 0;
                boxWord.Height = ActualFont.Height;
            }
        }

        _wordsSizeMeasured = true;
    }

    /// <summary>
    /// Recursively calls <see cref="MeasureWordsSize"/> on all descendant
    /// boxes so that <c>ActualWordSpacing</c> and word dimensions are
    /// computed before <see cref="GetMinMaxWidth"/> is invoked for
    /// shrink-to-fit width (CSS2.1 §10.3.7).
    /// Note: the current box (<c>this</c>) is already measured by the
    /// <see cref="MeasureWordsSize"/> call at the start of
    /// <see cref="PerformLayoutImp"/>; only descendants need measuring.
    /// </summary>
    private void EnsureDescendantWordsMeasured(RGraphics g)
    {
        var stack = new Stack<CssBox>();
        foreach (var child in Boxes)
            stack.Push(child);

        while (stack.Count > 0)
        {
            var box = stack.Pop();
            box.MeasureWordsSize(g);
            foreach (var child in box.Boxes)
                stack.Push(child);
        }
    }

    protected override sealed CssBoxProperties GetParent() => _parentBox;

    internal void InvalidateFontDependentSubtree()
    {
        InvalidateFontDependentValues();
        foreach (var child in Boxes)
            child.InvalidateFontDependentSubtree();
    }

    private int GetIndexForList()
    {
        // Phase 2: Read list attributes from CssBoxProperties instead of GetAttribute().
        bool reversed = ParentBox.ListReversed;

        int index;
        if (ParentBox.ListStart.HasValue)
        {
            index = ParentBox.ListStart.Value;
        }
        else if (reversed)
        {
            index = 0;
            foreach (CssBox b in ParentBox.Boxes)
            {
                if (b.Display == CssConstants.ListItem)
                    index++;
            }
        }
        else
        {
            index = 1;
        }

        foreach (CssBox b in ParentBox.Boxes)
        {
            if (b.Equals(this))
                return index;

            if (b.Display == CssConstants.ListItem)
                index += reversed ? -1 : 1;
        }

        return index;
    }

    private void CreateListItemBox(RGraphics g)
    {
        if (Display != CssConstants.ListItem || ListStyleType == CssConstants.None)
            return;

        if (_listItemBox == null)
        {
            _listItemBox = new CssBox(null, null, BaseUrl);
            _listItemBox.InheritStyle(this);
            _listItemBox.Display = CssConstants.Inline;
            _listItemBox._htmlContainer = ContainerInt;

            if (ListStyleType.Equals(CssConstants.Disc, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "•".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Circle, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "o".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Square, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = "♠".AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.Decimal, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = (GetIndexForList().ToString(CultureInfo.InvariantCulture) + ".").AsMemory();
            }
            else if (ListStyleType.Equals(CssConstants.DecimalLeadingZero, StringComparison.InvariantCultureIgnoreCase))
            {
                _listItemBox.Text = (GetIndexForList().ToString("00", CultureInfo.InvariantCulture) + ".").AsMemory();
            }
            else
            {
                _listItemBox.Text = (CommonUtils.ConvertToAlphaNumber(GetIndexForList(), ListStyleType) + ".").AsMemory();
            }

            _listItemBox.ParseToWords();

            _listItemBox.PerformLayoutImp(g);
            _listItemBox.Size = new SizeF((float)_listItemBox.Words[0].Width, (float)_listItemBox.Words[0].Height);
        }

        _listItemBox.Words[0].Left = Location.X - _listItemBox.Size.Width - 5;
        _listItemBox.Words[0].Top = Location.Y + ActualPaddingTop; // +FontAscent;
    }

    internal string GetAttribute(string attribute) => GetAttribute(attribute, string.Empty);
    internal string GetAttribute(string attribute, string defaultValue) => HtmlTag != null ? HtmlTag.TryGetAttribute(attribute, defaultValue) : defaultValue;

    internal double GetMinimumWidth()
    {
        double maxWidth = 0;
        CssRect maxWidthWord = null;
        CssBoxHelper.GetMinimumWidth_LongestWord(this, ref maxWidth, ref maxWidthWord);

        double padding = 0f;
        if (maxWidthWord != null)
        {
            var box = maxWidthWord.OwnerBox;
            while (box != null)
            {
                padding += box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualBorderLeftWidth + box.ActualPaddingLeft;
                box = box != this ? box.ParentBox : null;
            }
        }

        return maxWidth + padding;
    }

    internal void GetMinMaxWidth(out double minWidth, out double maxWidth)
    {
        double min = 0f;
        double maxSum = 0f;
        double paddingSum = 0f;
        double marginSum = 0f;

        CssBoxHelper.GetMinMaxSumWords(this, ref min, ref maxSum, ref paddingSum, ref marginSum);

        maxWidth = paddingSum + maxSum;
        minWidth = paddingSum + (min < 90999 ? min : 0);
    }

    /// <summary>
    /// CSS2.1 §10.3.7: Computes the shrink-to-fit width for an auto-width
    /// absolutely positioned element by independently measuring each direct
    /// child's total width and returning the maximum.
    /// Each block or float child is its own "line"; the preferred width is
    /// the widest line.  This avoids the incorrect accumulation that occurs
    /// when <see cref="CssBoxHelper.GetMinMaxSumWords"/> sums float widths
    /// with preceding block widths.
    /// </summary>
    private double ComputeShrinkToFitWidth()
    {
        double maxLineWidth = 0;

        foreach (var child in Boxes)
        {
            double childWidth;

            if (child.Width != CssConstants.Auto && !string.IsNullOrEmpty(child.Width))
            {
                // Explicit width: use declared width + borders/padding
                double containingBlockWidth = Size.Width > 0 && !double.IsNaN(Size.Width) ? Size.Width : 0;
                childWidth = child.ParseLengthWithLineHeight(child.Width, containingBlockWidth)
                           + child.ActualBorderLeftWidth + child.ActualBorderRightWidth
                           + child.ActualPaddingLeft + child.ActualPaddingRight;
            }
            else
            {
                // Auto-width child: compute its intrinsic preferred width.
                // Guard against NaN from unmeasured words in deeply nested
                // inline elements (e.g. Acid2 .eyes → #eyes-a → <object>).
                child.GetMinMaxWidth(out _, out double childMax);
                childWidth = double.IsNaN(childMax) ? 0 : childMax;
            }

            childWidth += child.ActualMarginLeft + child.ActualMarginRight;
            if (!double.IsNaN(childWidth))
                maxLineWidth = Math.Max(maxLineWidth, childWidth);
        }

        return maxLineWidth;
    }

    internal new void InheritStyle(CssBox box = null, bool everything = false) => base.InheritStyle(box ?? ParentBox, everything);

    protected double MarginTopCollapse(CssBoxProperties prevSibling)
    {
        double value;

        if (prevSibling != null)
        {
            // CSS2.1 §8.3.1: When the previous sibling is an "empty" box
            // (zero content height, no borders/padding, height auto/0), its
            // own top and bottom margins — and its children's margins —
            // collapse through.  The resulting collapsed margin participates
            // in collapsing with this element's top margin.
            if (prevSibling is CssBox prevBox && CssBoxHelper.IsEmptyCollapsible(prevBox))
            {
                double maxPos = Math.Max(ActualMarginTop, 0);
                double maxNeg = Math.Min(ActualMarginTop, 0);
                CssBoxHelper.CollectEmptyBoxMargins(prevBox, ref maxPos, ref maxNeg);
                double collapsed = maxPos + maxNeg; // maxNeg <= 0
                // Subtract the portion of the collapsed margin already
                // consumed when positioning the empty box itself (its
                // CollapsedMarginTop was recorded during its own layout).
                value = collapsed - prevBox.CollapsedMarginTop;
            }
            else
            {
                // CSS2.1 §8.3.1: Adjoining vertical margins collapse.
                // When both are positive → max(m1, m2).
                // When one is negative  → max(positives,0) + min(negatives,0).
                // When both are negative → 0 + min(m1,m2) = most-negative.
                // The general formula covers all three cases.
                // Use GetPropagatedMarginBottom so that a last-child's
                // bottom margin propagates through its parent when the
                // parent has no bottom border/padding and auto height
                // (CSS 2.1 §8.3.1 parent-child bottom-margin collapse).
                double prevMb = (prevSibling is CssBox prevSibBox)
                    ? CssBoxHelper.GetPropagatedMarginBottom(prevSibBox)
                    : prevSibling.ActualMarginBottom;
                double maxPos = Math.Max(
                    Math.Max(prevMb, 0),
                    Math.Max(ActualMarginTop, 0));
                double minNeg = Math.Min(
                    Math.Min(prevMb, 0),
                    Math.Min(ActualMarginTop, 0));
                value = maxPos + minNeg;
            }
            CollapsedMarginTop = value;
        }
        else if (_parentBox != null && _parentBox.ActualPaddingTop < 0.1 && _parentBox.ActualPaddingBottom < 0.1 && _parentBox.ActualBorderTopWidth < 0.1 && _parentBox.ActualBorderBottomWidth < 0.1
            // CSS Box Alignment §5.4: align-content != normal establishes
            // a BFC, which prevents parent–child margin collapsing.
            && (_parentBox.AlignContent == null || _parentBox.AlignContent == "normal"))
        {
            double parentEffective = Math.Max(_parentBox.ActualMarginTop, _parentBox.CollapsedMarginTop);

            // CSS2.1 §8.3.1: First in-flow child's top margin collapses
            // with the parent's top margin when the parent has no top
            // border and no top padding.  When the child's margin
            // exceeds the parent's, propagate the excess upward by
            // shifting the parent's position down.  Only do this for
            // non-root containers (not html/body) to avoid disturbing
            // the root element's established position.
            if (ActualMarginTop > parentEffective + 0.1
                && _parentBox.ParentBox != null
                && _parentBox.ParentBox.ParentBox != null)
            {
                double propagation = ActualMarginTop - parentEffective;
                _parentBox.Location = new PointF(
                    _parentBox.Location.X,
                    _parentBox.Location.Y + (float)propagation);
                _parentBox.CollapsedMarginTop = ActualMarginTop;
                value = 0;
            }
            else
            {
                value = Math.Max(0, ActualMarginTop - parentEffective);
            }
        }
        else
        {
            value = ActualMarginTop;

            // When the parent establishes a BFC (e.g. via align-content),
            // the first child's margin is fully consumed for positioning.
            // Record it so that an empty-collapsible sibling can subtract
            // the already-consumed portion during its own collapse.
            if (_parentBox != null
                && _parentBox.AlignContent != null
                && _parentBox.AlignContent != "normal")
            {
                CollapsedMarginTop = value;
            }
        }

        // fix for hr tag
        if (value < 0.1 && HtmlTag != null && HtmlTag.Name == "hr")
            value = GetEmHeight() * 1.1f;

        return value;
    }

    public bool BreakPage()
    {
        var container = ContainerInt;

        if (Size.Height >= container.PageSize.Height)
            return false;

        var remTop = (Location.Y - container.MarginTop) % container.PageSize.Height;
        var remBottom = (ActualBottom - container.MarginTop) % container.PageSize.Height;

        if (remTop > remBottom)
        {
            var diff = container.PageSize.Height - remTop;
            Location = new PointF(Location.X, (float)(Location.Y + diff + 1));
            
            return true;
        }

        return false;
    }

    private double CalculateActualRight()
    {
        if (ActualRight <= 90999)
            return ActualRight;

        var maxRight = 0d;

        foreach (var box in Boxes)
            maxRight = Math.Max(maxRight, box.ActualRight + box.ActualMarginRight);

        return maxRight + ActualPaddingRight + ActualMarginRight + ActualBorderRightWidth;
    }

    private double MarginBottomCollapse()
    {
        double margin = 0;

        // CSS2.1 §8.3.1: Margins collapse through the parent only when
        // the parent has no bottom padding and no bottom border.
        bool collapseThrough = Boxes.Count > 0 && ParentBox != null && ParentBox.Boxes.IndexOf(this) == ParentBox.Boxes.Count - 1 && _parentBox.ActualMarginBottom < 0.1
            && ActualPaddingBottom < 0.1 && ActualBorderBottomWidth < 0.1;
        // NOTE: When collapseThrough is true, the collapsed margin is NOT
        // included in this box's height — it is external spacing handled
        // by the parent.  The `margin` variable stays 0.

        // CSS2.1 §10.6.3 / §10.6.7: Floated children contribute to the
        // height of their parent only when the parent establishes a new
        // block formatting context (BFC).  Non-BFC blocks (e.g. a plain
        // <ul> inside a floated <dd>) must not include descendant floats
        // in their height calculation.
        bool isBfc = Float != CssConstants.None
            || Display == CssConstants.InlineBlock
            || Display == CssConstants.TableCell
            || (Overflow != null && Overflow != CssConstants.Visible)
            || Position == CssConstants.Absolute
            || Position == CssConstants.Fixed
            || (AlignContent != null && AlignContent != "normal");

        // Use the maximum ActualBottom across all children to handle
        // floated children that may not be the last in source order.
        // Initialize to the content-area top so that padding is preserved
        // even when all children are floated (CSS2.1 §10.6.3: content
        // height is zero but padding is additive).
        double maxChildBottom = Location.Y + ActualBorderTopWidth + ActualPaddingTop;
        CssBox lastInFlowChild = null;
        
        foreach (var child in Boxes)
        {
            // CSS2.1 §10.6.3: Only children in the normal flow are taken
            // into account.  Absolutely positioned and fixed-position boxes
            // are out of flow and must not influence the parent's auto height.
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (!isBfc && child.Float != CssConstants.None)
                continue;

            // CSS2.1 §9.4.3: Relative positioning is visual-only and
            // does not affect the flow position used for auto-height
            // calculation.  Undo the relative offset so the parent
            // measures the child's normal-flow bottom.
            double childBottom = child.ActualBottom;
            if (child.Position == CssConstants.Relative)
                childBottom -= CssBoxHelper.GetRelativeOffsetY(child);
            maxChildBottom = Math.Max(maxChildBottom, childBottom);
            lastInFlowChild = child;
        }

        // CSS2.1 §10.6.7: When a BFC root auto-sizes its height it must
        // extend to contain all descendant floats — not only direct-child
        // floats.  Walk the subtree (stopping at nested BFC boundaries)
        // to find the maximum float bottom.
        if (isBfc)
        {
            double maxFloatDesc = maxChildBottom;
            FindMaxDescendantFloatBottom(this, ref maxFloatDesc);
            maxChildBottom = Math.Max(maxChildBottom, maxFloatDesc);
        }

        // CSS2.1 §10.6.3: The auto height extends to the bottom margin-
        // edge of the last in-flow child.  When the parent has bottom
        // border or padding, the last child's margin does not collapse
        // through (§8.3.1), so add it as internal content spacing.
        if (!collapseThrough && lastInFlowChild != null)
            maxChildBottom += lastInFlowChild.ActualMarginBottom;
        return Math.Max(ActualBottom, maxChildBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);
    }

    /// <summary>
    /// CSS Box Alignment Level 3 §6.1: Resolves justify-self / align-self
    /// alignment for an absolutely-positioned box within its inset-modified
    /// containing block (IMCB).  Returns the offset from the IMCB start
    /// edge.  Default overflow behaviour for abspos is 'safe': when the
    /// content would overflow the strong CB edge, it is shifted to align
    /// with the start of the writing direction.
    /// </summary>
    private static double ResolveAbsposSelfAlignment(
        string alignment, double containerSize, double boxSize, bool isRtl)
    {
        double freeSpace = containerSize - boxSize;

        // Strip safe/unsafe prefix.
        string a = alignment;
        bool isSafe = true; // default for abspos is safe
        if (a.StartsWith("safe ", StringComparison.OrdinalIgnoreCase))
        {
            a = a.Substring(5).Trim();
            isSafe = true;
        }
        else if (a.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase))
        {
            a = a.Substring(7).Trim();
            isSafe = false;
        }

        double dx;
        switch (a)
        {
            case "center":
                dx = freeSpace / 2;
                break;
            case "end":
            case "flex-end":
                dx = isRtl ? 0 : freeSpace;
                break;
            case "self-end":
                dx = isRtl ? 0 : freeSpace;
                break;
            case "right":
                dx = freeSpace;
                break;
            case "start":
            case "flex-start":
                dx = isRtl ? freeSpace : 0;
                break;
            case "self-start":
                dx = isRtl ? freeSpace : 0;
                break;
            case "left":
                dx = 0;
                break;
            default:
                dx = 0; // fallback: start-aligned
                break;
        }

        // Safe overflow: clamp so the element does not overflow the
        // strong edge (start edge in the writing direction).
        if (isSafe)
        {
            if (isRtl)
            {
                // Strong edge is right (end of CB) — clamp dx ≤ freeSpace
                dx = Math.Min(dx, Math.Max(freeSpace, 0));
            }
            else
            {
                // Strong edge is left (start of CB) — clamp dx ≥ 0
                dx = Math.Max(dx, 0);
            }
        }

        return dx;
    }

    /// <summary>
    /// Computes the shrink-to-fit content width of this box: the maximum
    /// right edge of all child boxes (relative to this box) plus padding
    /// and border.  Used for abspos self-alignment where the box size is
    /// content-driven rather than stretched.
    /// </summary>
    private double GetShrinkToFitWidth()
    {
        // If there's an explicit CSS width, use it (plus border/padding).
        if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
            return Size.Width;

        double maxRight = 0;
        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None) continue;
            double childRight = (child.Location.X - Location.X)
                                + child.Size.Width
                                + child.ActualMarginRight;
            maxRight = Math.Max(maxRight, childRight);
        }

        if (maxRight <= 0) return Size.Width;

        return maxRight + ActualPaddingRight + ActualBorderRightWidth;
    }

    /// <summary>
    /// Computes the shrink-to-fit content height of this box: the maximum
    /// bottom edge of all child boxes (relative to this box) plus padding
    /// and border.  Used for abspos self-alignment where the box size is
    /// content-driven rather than stretched.
    /// </summary>
    private double GetShrinkToFitHeight()
    {
        // If there's an explicit CSS height, use it (plus border/padding).
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height))
        {
            double h = ActualBottom - Location.Y;
            return h > 0 ? h : Size.Height;
        }

        double maxBottom = 0;
        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None) continue;
            double childBottom = (child.Location.Y - Location.Y)
                                 + (child.ActualBottom - child.Location.Y)
                                 + child.ActualMarginBottom;
            maxBottom = Math.Max(maxBottom, childBottom);
        }

        if (maxBottom <= 0)
        {
            double h = ActualBottom - Location.Y;
            return h > 0 ? h : Size.Height;
        }

        return maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
    }

    /// <summary>
    /// Recursively finds the maximum bottom edge of any float in the
    /// subtree, stopping at nested BFC boundaries.  Used by the BFC
    /// root height calculation so that grandchild (and deeper) floats
    /// are properly contained.
    /// </summary>
    private static void FindMaxDescendantFloatBottom(CssBox box, ref double maxBottom)
    {
        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None && child.Display != CssConstants.None)
            {
                maxBottom = Math.Max(maxBottom, child.ActualBottom + child.ActualMarginBottom);
            }

            // Don't recurse into nested BFC roots — their floats are
            // contained by them, not by the outer BFC.
            bool childIsBfc = child.Float != CssConstants.None
                || child.Display == CssConstants.InlineBlock
                || child.Display == CssConstants.TableCell
                || child.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed
                || (child.Overflow != null && child.Overflow != CssConstants.Visible)
                || (child.AlignContent != null && child.AlignContent != "normal");
            if (!childIsBfc)
                FindMaxDescendantFloatBottom(child, ref maxBottom);
        }
    }

    /// <summary>
    /// CSS Grid Level 1 §8.5: When all grid items share the same
    /// grid-row and grid-column (e.g. grid-row: 1; grid-column: 1),
    /// they overlap in the same grid cell.  Reposition them to the
    /// container's content-area top-left so they stack visually with
    /// later items painted on top.
    /// </summary>
    /// <returns><c>true</c> if stacking was applied; <c>false</c>
    /// if items are not all in the same cell.</returns>
    private bool ApplyGridStacking()
    {
        bool allSameCell = true;
        string firstRow = null, firstCol = null;
        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;
            var cr = child.GridRow;
            var cc = child.GridColumn;
            // Items without explicit grid placement use auto.
            if (string.IsNullOrEmpty(cr) || cr == "auto"
                || string.IsNullOrEmpty(cc) || cc == "auto")
            { allSameCell = false; break; }
            if (firstRow == null)
            { firstRow = cr; firstCol = cc; }
            else if (cr != firstRow || cc != firstCol)
            { allSameCell = false; break; }
        }

        if (!allSameCell || firstRow == null)
            return false;

        double cellLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;
        double cellTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double maxBottom = cellTop;
        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;
            double dx = cellLeft + child.ActualMarginLeft - child.Location.X;
            double dy = cellTop + child.ActualMarginTop - child.Location.Y;
            if (Math.Abs(dx) > 0.1)
                child.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.1)
                child.OffsetTop(dy);
            double childBottom = child.ActualBottom + child.ActualMarginBottom;
            if (childBottom > maxBottom)
                maxBottom = childBottom;
        }
        ActualBottom = maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
        return true;
    }

    /// <summary>
    /// Called from <see cref="CssLayoutEngine.FlowInlineBlock"/> after
    /// CreateLineBoxes to apply grid stacking or auto-placement for grid
    /// containers that are laid out via the inline-block path.
    /// </summary>
    internal void ApplyGridLayoutAfterInline()
    {
        if (!ApplyGridStacking())
            ApplyGridAutoPlacement();
    }

    /// <summary>
    /// CSS Grid Level 1: Auto-placement for grid items that are not all
    /// in the same cell.  The inline layout path (CreateLineBoxes) places
    /// grid items as inline-blocks on a single line.  This method
    /// repositions them into proper grid rows (one item per row for a
    /// single-column grid) and applies justify-self within the column.
    /// </summary>
    private void ApplyGridAutoPlacement()
    {
        double cellLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;
        double cellTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double columnWidth = Size.Width - ActualPaddingLeft - ActualPaddingRight
            - ActualBorderLeftWidth - ActualBorderRightWidth;
        if (columnWidth <= 0) return;

        double currentY = cellTop;
        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;

            // CSS Grid Level 1 §6.1: Grid items with auto/normal/stretch
            // justify-self and auto width should stretch to fill the column.
            bool isAutoWidth = child.Width == CssConstants.Auto
                || string.IsNullOrEmpty(child.Width);
            string js = child.JustifySelf?.Trim().ToLowerInvariant();
            bool isStretch = js == null || js == "auto" || js == "normal"
                || js == "stretch";

            if (isStretch && isAutoWidth)
            {
                double targetWidth = columnWidth
                    - child.ActualMarginLeft - child.ActualMarginRight;
                if (targetWidth > 0 && Math.Abs(child.Size.Width - targetWidth) > 0.5)
                    child.Size = new SizeF((float)targetWidth, child.Size.Height);
            }

            // Move child to the start of the current row.
            double dx = cellLeft + child.ActualMarginLeft - child.Location.X;
            double dy = currentY + child.ActualMarginTop - child.Location.Y;
            if (Math.Abs(dx) > 0.1)
                child.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.1)
                child.OffsetTop(dy);

            // CSS Grid Level 1 §6.1: Apply justify-self to position the
            // item within its grid cell (column width).
            double boxWidth = child.ActualRight - child.Location.X;
            double freeSpace = columnWidth - boxWidth;
            if (freeSpace > 0.5 && !isStretch)
            {
                bool isElementRtl = child.Direction == "rtl";
                bool isContainerRtl = Direction == "rtl";

                double justifyDx = 0;
                switch (js)
                {
                    case "center":
                        justifyDx = freeSpace / 2;
                        break;
                    case "end":
                    case "flex-end":
                        justifyDx = isContainerRtl ? 0 : freeSpace;
                        break;
                    case "self-end":
                        justifyDx = isElementRtl ? 0 : freeSpace;
                        break;
                    case "right":
                        justifyDx = freeSpace;
                        break;
                    case "start":
                    case "flex-start":
                        justifyDx = isContainerRtl ? freeSpace : 0;
                        break;
                    case "self-start":
                        justifyDx = isElementRtl ? freeSpace : 0;
                        break;
                    case "left":
                        justifyDx = 0;
                        break;
                }

                if (Math.Abs(justifyDx) > 0.5)
                    child.OffsetLeft(justifyDx);
            }

            currentY = child.ActualBottom + child.ActualMarginBottom;
        }
        ActualBottom = currentY + ActualPaddingBottom + ActualBorderBottomWidth;
    }

    internal void OffsetTop(double amount)
    {
        List<CssLineBox> lines = [.. Rectangles.Keys];

        foreach (CssLineBox line in lines)
        {
            RectangleF r = Rectangles[line];
            Rectangles[line] = new RectangleF(r.X, (float)(r.Y + amount), r.Width, r.Height);
        }

        foreach (CssRect word in Words)
            word.Top += amount;

        foreach (CssBox b in Boxes)
        {
            // CSS2.1 §9.6.1: position:fixed elements are positioned relative
            // to the viewport and must not be shifted by ancestor offsets
            // (e.g. a parent's position:relative visual offset).
            if (b.Position != CssConstants.Fixed)
                b.OffsetTop(amount);
        }

        _listItemBox?.OffsetTop(amount);

        Location = new PointF(Location.X, (float)(Location.Y + amount));
    }

    internal void OffsetLeft(double amount)
    {
        List<CssLineBox> lines = [.. Rectangles.Keys];

        foreach (CssLineBox line in lines)
        {
            RectangleF r = Rectangles[line];
            Rectangles[line] = new RectangleF((float)(r.X + amount), r.Y, r.Width, r.Height);
        }

        foreach (CssRect word in Words)
            word.Left += amount;

        foreach (CssBox b in Boxes)
        {
            // CSS2.1 §9.6.1: position:fixed elements are positioned relative
            // to the viewport and must not be shifted by ancestor offsets.
            if (b.Position != CssConstants.Fixed)
                b.OffsetLeft(amount);
        }

        _listItemBox?.OffsetLeft(amount);

        Location = new PointF((float)(Location.X + amount), Location.Y);
    }

    internal void OffsetRectangle(CssLineBox lineBox, double gap)
    {
        if (Rectangles.TryGetValue(lineBox, out RectangleF r))
            Rectangles[lineBox] = new RectangleF(r.X, (float)(r.Y + gap), r.Width, r.Height);
    }

    internal void RectanglesReset() => Rectangles.Clear();

    private void OnImageLoadComplete(RImage image, RectangleF rectangle, bool async)
    {
        if (image != null && async)
            ContainerInt.RequestRefresh(false);
    }

    protected override RFont GetCachedFont(string fontFamily, double fsize, FontStyle st) => ContainerInt.GetFont(fontFamily, fsize, st);

    protected override Color GetActualColor(string colorStr) => ContainerInt.ParseColor(colorStr);

    protected override PointF GetActualLocation(string X, string Y)
    {
        var vpSize = ContainerInt.ViewportSize;
        var left = CssValueParser.ParseLength(X, vpSize.Width, GetEmHeight(), null);
        var top = CssValueParser.ParseLength(Y, vpSize.Height, GetEmHeight(), null);

        return new PointF((float)left, (float)top);
    }

    public override string ToString()
    {
        var tag = HtmlTag != null ? $"<{HtmlTag.Name}>" : "anon";

        if (IsBlock)
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} Block {FontSize}, Children:{Boxes.Count}";
        }
        else if (Display == CssConstants.None)
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} None";
        }
        else
        {
            return $"{(ParentBox == null ? "Root: " : string.Empty)}{tag} {Display}: {Text}";
        }
    }
}
