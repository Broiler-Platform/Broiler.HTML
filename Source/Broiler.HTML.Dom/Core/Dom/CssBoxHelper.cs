using System;
using System.Collections.Generic;
using System.Diagnostics;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Utils.Core.Utils;

namespace Broiler.HTML.Dom.Core.Dom;


internal static class CssBoxHelper
{
    public static CssBox CreateBox(HtmlTag tag, Uri baseUrl, CssBox parent = null)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Name == HtmlConstants.Img)
        {
            return new CssBoxImage(parent, tag, baseUrl);
        }
        else if (tag.Name.Equals("object", StringComparison.OrdinalIgnoreCase) &&
                 tag.TryGetAttribute("data") is { } data &&
                 data.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            // <object data="data:image/..."> — treat as a replaced image element.
            // Any nested fallback content will be removed by CorrectObjectBoxes.
            return new CssBoxImage(parent, tag, baseUrl);
        }
        else if (tag.Name == HtmlConstants.Iframe)
        {
            return new CssBox(parent, tag, baseUrl);
        }
        else if (tag.Name == HtmlConstants.Hr)
        {
            return new CssBoxHr(parent, tag, baseUrl);
        }
        else
        {
            return new CssBox(parent, tag, baseUrl);
        }
    }

    public static CssBox CreateBox(CssBox parent, Uri baseUrl, HtmlTag tag = null, CssBox before = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var newBox = new CssBox(parent, tag, baseUrl);
        newBox.InheritStyle();

        if (before != null)
            newBox.SetBeforeBox(before);

        return newBox;
    }

    public static CssBox CreateBlock(Uri baseUrl) => new(null, null, baseUrl) { Display = CssConstants.Block };

    public static CssBox CreateBlock(CssBox parent, Uri baseUrl, HtmlTag tag = null, CssBox before = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var newBox = CreateBox(parent, baseUrl, tag, before);
        newBox.Display = CssConstants.Block;

        return newBox;
    }

    internal static CssRect FirstWordOccourence(CssBox b, CssLineBox line)
    {
        if (b.Words.Count == 0 && b.Boxes.Count == 0)
            return null;

        if (b.Words.Count > 0)
        {
            foreach (CssRect word in b.Words)
            {
                if (line.Words.Contains(word))
                    return word;
            }

            return null;
        }
        else
        {
            foreach (CssBox bb in b.Boxes)
            {
                CssRect w = FirstWordOccourence(bb, line);

                if (w != null)
                    return w;
            }

            return null;
        }
    }

    public static void GetMinimumWidth_LongestWord(CssBox box, ref double maxWidth, ref CssRect maxWidthWord)
    {
        if (box.Words.Count > 0)
        {
            foreach (CssRect cssRect in box.Words)
            {
                if (cssRect.Width > maxWidth)
                {
                    maxWidth = cssRect.Width;
                    maxWidthWord = cssRect;
                }
            }
        }
        else
        {
            foreach (CssBox childBox in box.Boxes)
                GetMinimumWidth_LongestWord(childBox, ref maxWidth, ref maxWidthWord);
        }
    }

    public static double GetWidthMarginDeep(CssBox box)
    {
        double sum = 0f;

        if (box.Size.Width > 90999 || (box.ParentBox != null && box.ParentBox.Size.Width > 90999))
        {
            while (box != null)
            {
                sum += box.ActualMarginLeft + box.ActualMarginRight;
                box = box.ParentBox;
            }
        }

        return sum;
    }

    internal static double GetMaximumBottom(CssBox startBox, double currentMaxBottom)
    {
        foreach (var line in startBox.Rectangles.Keys)
            currentMaxBottom = Math.Max(currentMaxBottom, startBox.Rectangles[line].Bottom);

        foreach (var b in startBox.Boxes)
        {
            currentMaxBottom = Math.Max(currentMaxBottom, b.ActualBottom);
            currentMaxBottom = Math.Max(currentMaxBottom, GetMaximumBottom(b, currentMaxBottom));
        }

        return currentMaxBottom;
    }

    public static void GetMinMaxSumWords(CssBox box, ref double min, ref double maxSum, ref double paddingSum, ref double marginSum)
    {
        double? oldSum = null;

        // not inline (block) boxes start a new line so we need to reset the max sum
        // CSS2.1 §10.3.7: Floated children contribute to the same "line" for
        // shrink-to-fit width calculation, so they do not reset maxSum.
        if (box.Display != CssConstants.Inline && box.Display != CssConstants.TableCell && box.WhiteSpace != CssConstants.NoWrap && box.Float == CssConstants.None)
        {
            oldSum = maxSum;
            maxSum = marginSum;
        }

        // CSS2.1 §10.3.5/§10.3.7: When a floated child has an explicit width,
        // use the declared width directly for shrink-to-fit calculation
        // instead of measuring content words.
        if (box.Float != CssConstants.None
            && box.Width != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Width))
        {
            double explicitWidth = CssValueParser.ParseLength(
                box.Width, box.ContainingBlock?.Size.Width ?? 0, box.GetEmHeight());
            paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth
                        + box.ActualPaddingRight + box.ActualPaddingLeft;
            maxSum += explicitWidth;
            min = Math.Max(min, explicitWidth);

            if (oldSum.HasValue)
                maxSum = Math.Max(maxSum, oldSum.Value);
            return;
        }

        // CSS2.1 §17.5.2: Non-floated block-level children (e.g. display:table
        // or display:list-item inside an anonymous table-cell) with explicit
        // width contribute that width to the intrinsic minimum/maximum.
        if (box.Display != CssConstants.Inline
            && box.Display != CssConstants.TableCell
            && box.Float == CssConstants.None
            && box.Width != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Width))
        {
            double explicitWidth = CssValueParser.ParseLength(
                box.Width, box.ContainingBlock?.Size.Width ?? 0, box.GetEmHeight());
            if (explicitWidth > 0)
            {
                paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth
                            + box.ActualPaddingRight + box.ActualPaddingLeft;
                maxSum += explicitWidth;
                min = Math.Max(min, explicitWidth);

                if (oldSum.HasValue)
                    maxSum = Math.Max(maxSum, oldSum.Value);
                return;
            }
        }

        // add the padding 
        paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualPaddingLeft;


        // for tables the padding also contains the spacing between cells
        if (box.Display == CssConstants.Table)
            paddingSum += CssLayoutEngineTable.GetTableSpacing(box);

        if (box.Words.Count > 0)
        {
            // calculate the min and max sum for all the words in the box
            foreach (CssRect word in box.Words)
            {
                maxSum += word.FullWidth + (word.HasSpaceBefore ? word.OwnerBox.ActualWordSpacing : 0);
                min = Math.Max(min, word.Width);
            }

            // remove the last word padding
            if (box.Words.Count > 0 && !box.Words[box.Words.Count - 1].HasSpaceAfter)
                maxSum -= box.Words[box.Words.Count - 1].ActualWordSpacing;
        }
        else
        {
            // recursively on all the child boxes
            for (int i = 0; i < box.Boxes.Count; i++)
            {
                CssBox childBox = box.Boxes[i];
                marginSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;

                //maxSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;
                GetMinMaxSumWords(childBox, ref min, ref maxSum, ref paddingSum, ref marginSum);

                marginSum -= childBox.ActualMarginLeft + childBox.ActualMarginRight;
            }
        }

        // max sum is max of all the lines in the box
        if (oldSum.HasValue)
            maxSum = Math.Max(maxSum, oldSum.Value);
    }

    /// <summary>
    /// CSS2.1 §9.5.2: Returns the maximum bottom outer edge of preceding
    /// floats that the given box needs to clear, considering the box's
    /// <c>clear</c> direction (<c>left</c>, <c>right</c>, or <c>both</c>).
    /// </summary>
    public static double GetMaxFloatBottom(CssBox box)
    {
        double maxBottom = 0;
        List<(string tag, double bottom)> considered = null;

        if (box.ParentBox == null)
            return maxBottom;

        string clearDir = box.Clear;

        // Walk up the ancestor chain to find floats in the same block
        // formatting context (BFC).  Floats from ancestor-level siblings
        // are relevant for clearance even when the cleared element is
        // nested deeper (CSS2.1 §9.5.2).
        CssBox current = box;
        while (current.ParentBox != null)
        {
            foreach (var sibling in current.ParentBox.Boxes)
            {
                if (sibling == current) break;
                CollectMaxFloatBottom(sibling, clearDir, ref maxBottom, ref considered);
            }

            // Stop at BFC boundaries — floats in an outer BFC don't
            // participate in clearance for elements in an inner BFC.
            if (EstablishesBfc(current.ParentBox))
                break;

            current = current.ParentBox;
        }

        if (considered != null && considered.Count > 0)
        {
            Debug.WriteLine($"[ClearFloat] Clearance for <{box.HtmlTag?.Name ?? "?"}> clear={box.Clear}: " +
                $"considered {considered.Count} float(s), maxBottom={maxBottom:F1}");
            foreach (var (tag, bottom) in considered)
                Debug.WriteLine($"  - <{tag}> bottom={bottom:F1}");
        }

        return maxBottom;
    }

    /// <summary>
    /// Collects the maximum bottom coordinate of floats in the same
    /// block formatting context (BFC) that match the <paramref name="clearDir"/>
    /// direction.  Floated elements establish a new BFC, so their descendant
    /// floats are excluded from clearance calculations outside.
    /// </summary>
    private static void CollectMaxFloatBottom(CssBox box, string clearDir, ref double maxBottom, ref List<(string tag, double bottom)> considered)
    {
        if (box.Float != CssConstants.None)
        {
            // CSS2.1 §9.5.2: Only consider floats in the matching direction.
            // clear:left → only left floats, clear:right → only right floats,
            // clear:both → all floats.
            bool matchesDirection = clearDir == "both"
                || string.Equals(box.Float, clearDir, StringComparison.OrdinalIgnoreCase);
            if (!matchesDirection)
                return;

            // Compute the float's margin-box bottom ("bottom outer edge"
            // per CSS2.1 §9.5.2) so that clearance positions the cleared
            // element below the float's full margin box.
            // CSS2.1 §10.5: Percentage heights resolve to auto when the
            // containing block's height is not explicitly specified —
            // use ActualBottom (layout-computed) in that case.
            double bottom;
            bool hasExplicitHeight = box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height);

            if (hasExplicitHeight && !box.HeightPercentageResolvesToAuto())
                bottom = box.Location.Y + box.ActualHeight
                    + box.ActualPaddingTop + box.ActualPaddingBottom
                    + box.ActualBorderTopWidth + box.ActualBorderBottomWidth
                    + box.ActualMarginBottom;
            else
                bottom = box.ActualBottom
                    + box.ActualMarginBottom;
            maxBottom = Math.Max(maxBottom, bottom);
            considered ??= new List<(string, double)>();
            considered.Add((box.HtmlTag?.Name ?? box.Display, bottom));
            // Float establishes a new BFC – don't recurse into descendants.
            return;
        }

        foreach (var child in box.Boxes)
            CollectMaxFloatBottom(child, clearDir, ref maxBottom, ref considered);
    }

    /// <summary>
    /// Returns the effective bottom margin for a box, accounting for
    /// parent-child bottom-margin collapse (CSS 2.1 §8.3.1).
    /// When a box has no bottom border, no bottom padding, and auto height,
    /// the last in-flow block-level child's bottom margin collapses with
    /// the box's own bottom margin.  This is applied recursively.
    /// </summary>
    internal static double GetPropagatedMarginBottom(CssBox box)
    {
        double mb = box.ActualMarginBottom;

        if (box.ActualBorderBottomWidth > 0.1 || box.ActualPaddingBottom > 0.1)
            return mb;

        if (box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height))
        {
            bool resolvedToAuto = box.Height.Contains('%')
                && (box.ContainingBlock.Height == CssConstants.Auto
                    || string.IsNullOrEmpty(box.ContainingBlock.Height));
            if (!resolvedToAuto)
                return mb;
        }

        // Find last in-flow block-level child (CSS 2.1 §8.3.1).
        CssBox? lastInFlow = null;
        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.Inline
                || child.Display == CssConstants.InlineBlock)
                continue;
            lastInFlow = child;
        }

        if (lastInFlow == null)
            return mb;

        double childMb = GetPropagatedMarginBottom(lastInFlow);

        // Collapse: max(positives,0) + min(negatives,0)
        double maxPos = Math.Max(Math.Max(mb, 0), Math.Max(childMb, 0));
        double minNeg = Math.Min(Math.Min(mb, 0), Math.Min(childMb, 0));
        return maxPos + minNeg;
    }

    /// <summary>
    /// Computes the vertical offset applied by <c>position: relative</c>.
    /// CSS2.1 §9.4.3: <c>top</c> takes precedence over <c>bottom</c>.
    /// Returns 0 if the element is not relatively positioned or has no offset.
    /// </summary>
    internal static double GetRelativeOffsetY(CssBoxProperties box)
    {
        bool hasTop = box.Top != null && box.Top != CssConstants.Auto;
        bool hasBottom = box.Bottom != null && box.Bottom != CssConstants.Auto;

        if (hasTop)
            return CssValueParser.ParseLength(box.Top, box.Size.Height, box.GetEmHeight());
        if (hasBottom)
            return -CssValueParser.ParseLength(box.Bottom, box.Size.Height, box.GetEmHeight());
        return 0;
    }

    /// <summary>
    /// Collects all float boxes in the same block formatting context that
    /// precede <paramref name="box"/> in the DOM tree. This includes floats
    /// nested inside non-BFC siblings (e.g., floated <c>li</c> elements
    /// inside a non-floated <c>ul</c>) and floats that are siblings of
    /// ancestor elements when those ancestors do not establish a new BFC
    /// (CSS2.1 §9.4.1).
    /// </summary>
    internal static List<CssBox> CollectPrecedingFloatsInBfc(CssBox box)
    {
        var result = new List<CssBox>();
        if (box.ParentBox == null) return result;

        // Collect preceding sibling floats (and their non-BFC subtrees).
        foreach (var sibling in box.ParentBox.Boxes)
        {
            if (sibling == box) break;
            CollectFloatsInSubtree(sibling, result);
        }

        // Walk up ancestor chain: collect floats from each ancestor's
        // preceding siblings while the ancestor does not establish a BFC.
        var current = box.ParentBox;
        while (current != null && current.ParentBox != null)
        {
            if (EstablishesBfc(current))
                break;

            foreach (var sibling in current.ParentBox.Boxes)
            {
                if (sibling == current) break;
                CollectFloatsInSubtree(sibling, result);
            }

            current = current.ParentBox;
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="box"/> establishes a new
    /// block formatting context (CSS2.1 §9.4.1, CSS Box Alignment §5.4).
    /// </summary>
    private static bool EstablishesBfc(CssBox box)
    {
        return box.Float != CssConstants.None
            || box.Display == CssConstants.InlineBlock
            || box.Display == CssConstants.TableCell
            || box.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
            || box.Position == CssConstants.Absolute
            || box.Position == CssConstants.Fixed
            || (box.Overflow != null && box.Overflow != CssConstants.Visible)
            || (box.AlignContent != null && box.AlignContent != "normal");
    }

    private static void CollectFloatsInSubtree(CssBox root, List<CssBox> result)
    {
        if (root.Float != CssConstants.None && root.Display != CssConstants.None)
        {
            result.Add(root);
            // Float establishes a new BFC – don't recurse into descendants.
            return;
        }

        // CSS2.1 §9.5: Don't recurse into elements that establish a new
        // block formatting context — their inner floats don't participate
        // in the parent BFC's float list.
        if (EstablishesBfc(root))
            return;

        foreach (var child in root.Boxes)
            CollectFloatsInSubtree(child, result);
    }

    /// <summary>
    /// CSS2.1 §8.3.1: Returns <c>true</c> if a box is "empty" — its own
    /// top and bottom margins are adjoining and collapse through.
    /// Conditions: min-height is zero, no top/bottom borders or padding,
    /// height is 0 or auto (or percentage that resolves to auto), no line
    /// boxes, and all in-flow children's margins also collapse.
    /// </summary>
    internal static bool IsEmptyCollapsible(CssBox box)
    {
        if (box.ActualBorderTopWidth > 0.1 || box.ActualBorderBottomWidth > 0.1)
            return false;
        if (box.ActualPaddingTop > 0.1 || box.ActualPaddingBottom > 0.1)
            return false;

        // Check if height resolves to zero/auto
        if (box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height))
        {
            bool resolvedToAuto = box.Height.Contains('%')
                && (box.ContainingBlock.Height == CssConstants.Auto
                    || string.IsNullOrEmpty(box.ContainingBlock.Height));
            if (!resolvedToAuto)
            {
                double h = CssValueParser.ParseLength(box.Height, box.Size.Height, box.GetEmHeight());
                if (h > 0.1)
                    return false;
            }
        }

        // Zero content height — ActualBottom should equal Location.Y
        // (tolerance 0.5 accounts for sub-pixel rounding in layout)
        if (Math.Abs(box.ActualBottom - box.Location.Y) > 0.5)
            return false;

        // Must not contain any line boxes with actual content.
        // CreateLineBoxes always creates at least one CssLineBox for any
        // element that enters the inline-formatting path, even if the
        // element is empty.  An empty line box (no words) does not
        // constitute "content" for margin-through-collapse purposes.
        //
        // CSS2.1 §8.3.1: When height is explicitly 0, line boxes contain
        // overflowing content that doesn't prevent margin collapse.  Only
        // check for line-box content when height is auto.
        bool hasExplicitZeroHeight = box.Height != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Height);
        if (!hasExplicitZeroHeight)
        {
            foreach (var lb in box.LineBoxes)
            {
                if (lb.Words.Count > 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collects the maximum positive and minimum negative margins from an
    /// empty collapsible box and all its in-flow children (recursively for
    /// children that are also empty and collapsible).
    /// </summary>
    internal static void CollectEmptyBoxMargins(CssBox box, ref double maxPos, ref double maxNeg)
    {
        maxPos = Math.Max(maxPos, Math.Max(box.ActualMarginTop, 0));
        maxPos = Math.Max(maxPos, Math.Max(box.ActualMarginBottom, 0));
        maxNeg = Math.Min(maxNeg, Math.Min(box.ActualMarginTop, 0));
        maxNeg = Math.Min(maxNeg, Math.Min(box.ActualMarginBottom, 0));

        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed)
                continue;

            maxPos = Math.Max(maxPos, Math.Max(child.ActualMarginTop, 0));
            maxPos = Math.Max(maxPos, Math.Max(child.ActualMarginBottom, 0));
            maxNeg = Math.Min(maxNeg, Math.Min(child.ActualMarginTop, 0));
            maxNeg = Math.Min(maxNeg, Math.Min(child.ActualMarginBottom, 0));

            if (IsEmptyCollapsible(child))
                CollectEmptyBoxMargins(child, ref maxPos, ref maxNeg);
        }
    }

    /// <summary>
    /// Returns the effective bottom margin for a box, accounting for margins
    /// that collapse through the box when it is "empty" per CSS2.1 §8.3.1.
    /// For non-empty boxes returns <see cref="CssBoxProperties.ActualMarginBottom"/>.
    /// </summary>
    internal static double GetEffectiveMarginBottom(CssBox box)
    {
        if (!IsEmptyCollapsible(box))
            return box.ActualMarginBottom;

        double maxPos = 0, maxNeg = 0;
        CollectEmptyBoxMargins(box, ref maxPos, ref maxNeg);
        double collapsed = maxPos + maxNeg;
        return collapsed - box.CollapsedMarginTop;
    }
}
