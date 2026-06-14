using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.CSS.Core.Dom;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Core.Utils;
using Broiler.HTML.Utils.Core.Utils;

namespace Broiler.HTML.Dom.Core.Dom;

internal static class CssLayoutEngine
{
    /// <summary>
    /// Returns true when <paramref name="box"/> or any of its ancestors
    /// (up to but not including <paramref name="stop"/>) has
    /// <c>position:absolute</c> or <c>position:fixed</c>.
    /// </summary>
    private static bool IsInAbsposSubtree(CssBox box, CssBox stop)
    {
        for (var b = box; b != null && b != stop; b = b.ParentBox)
        {
            if (b.Position == CssConstants.Absolute || b.Position == CssConstants.Fixed)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Approximate ratio of font ascent to total font height for typical
    /// Latin fonts.  Used to compute baseline position when full font
    /// metrics are not directly available (CSS2.1 §10.8 strut).
    /// </summary>
    private const double TypicalAscentRatio = 0.8;

    /// <summary>
    /// Ratio to convert typographic points to CSS pixels (96 DPI / 72 DPI).
    /// Layout coordinates are in CSS px, but font metrics from the layout
    /// font are at pt-scale (the layout font is created in canvas units, and
    /// the layout font is created at pt size).  This factor bridges the gap
    /// for line-height calculations where font.Height is the fallback.
    /// </summary>
    private const double PtToCssPx = 96.0 / 72.0;

    public static void MeasureImageSize(CssRectImage imageWord)
    {
        ArgumentNullException.ThrowIfNull(imageWord);
        ArgumentNullException.ThrowIfNull(imageWord.OwnerBox);

        var width = new CssLength(imageWord.OwnerBox.Width);
        var height = new CssLength(imageWord.OwnerBox.Height);

        bool hasImageTagWidth = width.Number > 0 && width.Unit == CssUnit.Pixels;
        bool hasImageTagHeight = height.Number > 0 && height.Unit == CssUnit.Pixels;
        bool scaleImageHeight = false;

        if (hasImageTagWidth)
        {
            imageWord.Width = width.Number;
        }
        else if (width.Number > 0 && width.IsPercentage)
        {
            imageWord.Width = width.Number * imageWord.OwnerBox.ContainingBlock.Size.Width;
            scaleImageHeight = true;
        }
        else if (imageWord.Image != null)
        {
            imageWord.Width = imageWord.ImageRectangle == RectangleF.Empty ? imageWord.Image.Width : imageWord.ImageRectangle.Width;

            // CSS2.1 §10.3.2: when width is auto the used value is the
            // intrinsic width.  Do NOT clamp to the containing block —
            // inline replaced elements are allowed to overflow their
            // container.  Authors use max-width:100% to opt into clamping.
        }
        else
        {
            imageWord.Width = hasImageTagHeight ? height.Number / 1.14f : 20;
        }

        var maxWidth = new CssLength(imageWord.OwnerBox.MaxWidth);
        if (maxWidth.Number > 0)
        {
            double maxWidthVal = -1;
            if (maxWidth.Unit == CssUnit.Pixels)
            {
                maxWidthVal = maxWidth.Number;
            }
            else if (maxWidth.IsPercentage)
            {
                maxWidthVal = maxWidth.Number * imageWord.OwnerBox.ContainingBlock.Size.Width;
            }

            if (maxWidthVal > -1 && imageWord.Width > maxWidthVal)
            {
                imageWord.Width = maxWidthVal;
                scaleImageHeight = !hasImageTagHeight;
            }
        }

        if (hasImageTagHeight)
        {
            imageWord.Height = height.Number;
        }
        else if (imageWord.Image != null)
        {
            imageWord.Height = imageWord.ImageRectangle == RectangleF.Empty ? imageWord.Image.Height : imageWord.ImageRectangle.Height;
        }
        else
        {
            imageWord.Height = imageWord.Width > 0 ? imageWord.Width * 1.14f : 22.8f;
        }

        if (imageWord.Image != null && imageWord.Image.HasIntrinsicRatio)
        {
            bool widthDriven = (hasImageTagWidth && !hasImageTagHeight) || scaleImageHeight;
            // If only the width was set in the html tag, ratio the height.
            if (widthDriven)
            {
                // Divide the given tag width with the actual image width, to get the ratio.
                double ratio = imageWord.Width / imageWord.Image.Width;
                imageWord.Height = imageWord.Image.Height * ratio;
            }
            // If only the height was set in the html tag, ratio the width.
            else if (hasImageTagHeight && !hasImageTagWidth)
            {
                // Divide the given tag height with the actual image height, to get the ratio.
                double ratio = imageWord.Height / imageWord.Image.Height;
                imageWord.Width = imageWord.Image.Width * ratio;
            }
        }

        // CSS2.1 §10.4/§10.7: Apply min/max width/height constraints
        // for replaced elements (images).  These are applied after intrinsic
        // sizing and aspect-ratio adjustments.
        var minWidth = new CssLength(imageWord.OwnerBox.MinWidth);
        if (minWidth.Number > 0)
        {
            double minWidthVal = minWidth.Unit == CssUnit.Pixels
                ? minWidth.Number
                : minWidth.IsPercentage
                    ? minWidth.Number * imageWord.OwnerBox.ContainingBlock.Size.Width
                    : -1;
            if (minWidthVal > 0 && imageWord.Width < minWidthVal)
            {
                if (imageWord.Image != null && !hasImageTagHeight && imageWord.Image.HasIntrinsicRatio)
                {
                    double ratio = minWidthVal / imageWord.Width;
                    imageWord.Height *= ratio;
                }
                imageWord.Width = minWidthVal;
            }
        }

        var maxHeight = new CssLength(imageWord.OwnerBox.MaxHeight);
        if (maxHeight.Number > 0)
        {
            double maxHeightVal = maxHeight.Unit == CssUnit.Pixels
                ? maxHeight.Number
                : maxHeight.IsPercentage
                    ? maxHeight.Number * imageWord.OwnerBox.ContainingBlock.Size.Height
                    : -1;
            if (maxHeightVal > 0 && imageWord.Height > maxHeightVal)
            {
                if (imageWord.Image != null && !hasImageTagWidth && imageWord.Image.HasIntrinsicRatio)
                {
                    double ratio = maxHeightVal / imageWord.Height;
                    imageWord.Width *= ratio;
                }
                imageWord.Height = maxHeightVal;
            }
        }

        var minHeight = new CssLength(imageWord.OwnerBox.MinHeight);
        if (minHeight.Number > 0)
        {
            double minHeightVal = minHeight.Unit == CssUnit.Pixels
                ? minHeight.Number
                : minHeight.IsPercentage
                    ? minHeight.Number * imageWord.OwnerBox.ContainingBlock.Size.Height
                    : -1;
            if (minHeightVal > 0 && imageWord.Height < minHeightVal)
            {
                if (imageWord.Image != null && !hasImageTagWidth && imageWord.Image.HasIntrinsicRatio)
                {
                    double ratio = minHeightVal / imageWord.Height;
                    imageWord.Width *= ratio;
                }
                imageWord.Height = minHeightVal;
            }
        }

        imageWord.Height += imageWord.OwnerBox.ActualBorderBottomWidth + imageWord.OwnerBox.ActualBorderTopWidth + imageWord.OwnerBox.ActualPaddingTop + imageWord.OwnerBox.ActualPaddingBottom;
    }

    public static void CreateLineBoxes(RGraphics g, CssBox blockBox)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(blockBox);

        blockBox.LineBoxes.Clear();

        double limitRight = blockBox.ActualRight - blockBox.ActualPaddingRight - blockBox.ActualBorderRightWidth;

        //Get the start x and y of the blockBox
        double startx = blockBox.Location.X + blockBox.ActualPaddingLeft - 0 + blockBox.ActualBorderLeftWidth;
        double starty = blockBox.Location.Y + blockBox.ActualPaddingTop - 0 + blockBox.ActualBorderTopWidth;
        double curx = startx + blockBox.ActualTextIndent;
        double cury = starty;

        //Reminds the maximum bottom reached
        double maxRight = startx;
        double maxBottom = starty;

        //First line box
        CssLineBox line = new(blockBox);

        //Flow words and boxes
        FlowBox(g, blockBox, blockBox, limitRight, 0, startx, ref line, ref curx, ref cury, ref maxRight, ref maxBottom);

        // if width is not restricted we need to lower it to the actual width
        if (blockBox.ActualRight >= 90999)
        {
            blockBox.ActualRight = maxRight + blockBox.ActualPaddingRight + blockBox.ActualBorderRightWidth;
        }

        //Gets the rectangles for each line-box
        bool plaintext = string.Equals(blockBox.UnicodeBidi, "plaintext", StringComparison.OrdinalIgnoreCase);
        // CSS Text §bidi-linebox: under unicode-bidi:plaintext each line is its own
        // bidi paragraph.  Its base direction is the first strong character's; a line
        // with no strong character inherits the previous paragraph's base direction,
        // or the containing block's direction when there is none.  Otherwise every
        // line shares the block's own direction.  Because a <br> splits content into
        // sibling anonymous blocks, the "previous paragraph" may live in an earlier
        // sibling — seed the running base direction from the most recent strong
        // character preceding this block in document order.
        bool baseRtl = plaintext
            ? SeedPlaintextBaseRtl(blockBox)
            : blockBox.Direction == CssConstants.Rtl;
        foreach (var linebox in blockBox.LineBoxes)
        {
            bool lineRtl = baseRtl;
            if (plaintext)
            {
                lineRtl = LineFirstStrongRtl(linebox) ?? baseRtl;
                baseRtl = lineRtl;
            }
            ApplyHorizontalAlignment(g, linebox, lineRtl);
            ApplyRightToLeft(linebox, lineRtl);
            BubbleRectangles(blockBox, linebox);
            ApplyVerticalAlignment(g, linebox);
            linebox.AssignRectanglesToBoxes();
        }

        // CSS2.1 §10.8: After vertical alignment adjusts inline-block
        // positions (e.g. vertical-align: 2em raises boxes), recalculate
        // maxBottom from the actual post-alignment positions.
        //
        // CSS2.1 §10.8.1: The line box height is the distance between
        // the uppermost box top and the lowermost box bottom.  When
        // positive vertical-align raises inline-blocks above the flow
        // start, the line box extends upward.  The full line box height
        // must be reflected in the block's content height so that
        // subsequent siblings are positioned correctly.
        //
        // Example: Acid3's .buckets div has font: 0/0 (baseline at
        // content edge) and bucket6 extends 162px above the baseline.
        // The line box height is 162px, so the div's auto height = 162px
        // (plus padding/border).
        maxBottom = starty;
        double minTop = starty;
        foreach (var linebox in blockBox.LineBoxes)
        {
            foreach (var rect in linebox.Rectangles)
            {
                // CSS2.1 §9.6.1: Absolutely/fixed positioned elements are
                // out of normal flow and must not affect the line box height.
                if (IsInAbsposSubtree(rect.Key, blockBox))
                    continue;

                maxBottom = Math.Max(maxBottom, rect.Value.Bottom);
                minTop = Math.Min(minTop, rect.Value.Top);
            }
            foreach (var word in linebox.Words)
            {
                if (IsInAbsposSubtree(word.OwnerBox, blockBox))
                    continue;

                maxBottom = Math.Max(maxBottom, word.Bottom);
                minTop = Math.Min(minTop, word.Top);
            }

            if (blockBox.ActualLineHeight > 0)
            {
                double lineTop = double.MaxValue;
                bool hasLineContent = false;

                foreach (var rect in linebox.Rectangles)
                {
                    if (IsInAbsposSubtree(rect.Key, blockBox))
                        continue;

                    lineTop = Math.Min(lineTop, rect.Value.Top);
                    hasLineContent = true;
                }

                foreach (var word in linebox.Words)
                {
                    if (IsInAbsposSubtree(word.OwnerBox, blockBox))
                        continue;

                    lineTop = Math.Min(lineTop, word.Top);
                    hasLineContent = true;
                }

                if (hasLineContent)
                    maxBottom = Math.Max(maxBottom, lineTop + blockBox.ActualLineHeight);
            }
        }
        // CSS2.1 §10.8.1: The line box height is the distance between
        // the uppermost box top and the lowermost box bottom.  When
        // inline-level boxes overflow above the starting flow position
        // (minTop < starty), the full line box height must be reflected
        // in maxBottom so subsequent siblings are positioned correctly.
        if (minTop < starty)
        {
            double lineBoxHeight = maxBottom - minTop;
            maxBottom = Math.Max(maxBottom, starty + lineBoxHeight);

            // CSS2.1 §9.4.2: Line boxes are laid out beginning at the
            // top of the containing block.  When vertical-align raises
            // inline-blocks above the flow start, the entire line box
            // content must be shifted downward so it renders within the
            // block container's content area (from starty to
            // starty + lineBoxHeight) instead of overflowing above.
            // The shift amount is computed from the global minTop across
            // ALL line boxes in the block (lines 162-176), so it must be
            // applied uniformly to all line boxes.
            double shift = starty - minTop;
            foreach (var linebox in blockBox.LineBoxes)
            {
                // Shift line box rectangle positions
                var keys = new List<CssBox>(linebox.Rectangles.Keys);
                foreach (var box in keys)
                {
                    var r = linebox.Rectangles[box];
                    linebox.Rectangles[box] = new RectangleF(r.X, (float)(r.Y + shift), r.Width, r.Height);

                    // For inline-block boxes, also update the CssBox's
                    // own Location and ActualBottom (used by the paint system).
                    if (box.Display == CssConstants.InlineBlock)
                    {
                        box.Location = new PointF(box.Location.X, (float)(box.Location.Y + shift));
                        box.ActualBottom += shift;
                    }

                    // Update the box's own Rectangles copy (assigned
                    // earlier by AssignRectanglesToBoxes).
                    if (box.Rectangles.ContainsKey(linebox))
                        box.Rectangles[linebox] = linebox.Rectangles[box];
                }

                // Shift word positions
                foreach (var word in linebox.Words)
                    word.Top += shift;
            }
        }

        // CSS2.1 §10.8: The "strut" — each line box starts with an
        // imaginary zero-width inline box with the block container's font
        // and line-height properties.  This establishes the minimum line
        // box height for inline formatting contexts.
        // The strut only affects content height when height is 'auto';
        // an explicit height (CSS2.1 §10.6.3) overrides the content height.
        // CSS2.1 §9.4.2: The strut only contributes to height when the
        // inline formatting context has actual inline content (words or
        // inline-level boxes).  An empty block should have zero content
        // height from the IFC.
        bool hasExplicitHeight = blockBox.Height != null && blockBox.Height != CssConstants.Auto;
        bool hasInlineContent = false;
        foreach (var lb in blockBox.LineBoxes)
        {
            // CSS2.1 §9.6.1: Words and rectangles from absolutely/fixed
            // positioned elements are not in-flow inline content.
            foreach (var w in lb.Words)
            {
                if (!IsInAbsposSubtree(w.OwnerBox, blockBox))
                {
                    hasInlineContent = true;
                    break;
                }
            }
            if (hasInlineContent) break;
            foreach (var r in lb.Rectangles)
            {
                if (!IsInAbsposSubtree(r.Key, blockBox))
                {
                    hasInlineContent = true;
                    break;
                }
            }
            if (hasInlineContent) break;
        }
        if (blockBox.ActualLineHeight > 0 && !hasExplicitHeight && hasInlineContent)
            maxBottom = Math.Max(maxBottom, starty + blockBox.ActualLineHeight);

        blockBox.ActualBottom = maxBottom + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;

        // CSS2.1 §10.6.3: When height is not 'auto', the used value is the
        // specified value.  Content may overflow (controlled by 'overflow').
        // For overflow:hidden, overflow:auto, and overflow:scroll the box's
        // layout height is clamped to the specified height so that subsequent
        // siblings are not pushed down by overflowing content.
        if (hasExplicitHeight
            && blockBox.Overflow is CssConstants.Hidden or CssConstants.Auto or CssConstants.Scroll
            && blockBox.ActualBottom - blockBox.Location.Y > blockBox.ActualHeight)
            blockBox.ActualBottom = blockBox.Location.Y + blockBox.ActualHeight;
    }

    public static void ApplyCellVerticalAlignment(RGraphics g, CssBox cell)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.VerticalAlign == CssConstants.Top || cell.VerticalAlign == CssConstants.Baseline)
            return;

        double cellbot = cell.ClientBottom;
        double bottom = CssBoxHelper.GetMaximumBottom(cell, 0f);
        double dist = 0f;

        if (cell.VerticalAlign == CssConstants.Bottom)
        {
            dist = cellbot - bottom;
        }
        else if (cell.VerticalAlign == CssConstants.Middle)
        {
            dist = (cellbot - bottom) / 2;
        }

        // CSS Box Alignment §6.2: When align-content is 'normal' on a
        // table cell, vertical-align maps to safe alignment.  If the
        // content overflows the cell (dist < 0), safe alignment clamps
        // to start (top), preventing negative shifts.
        if (dist < 0 && (cell.AlignContent == null || cell.AlignContent == "normal"))
            dist = 0;

        foreach (CssBox b in cell.Boxes)
        {
            b.OffsetTop(dist);
        }
    }

    private static void FlowBox(RGraphics g, CssBox blockbox, CssBox box, double limitRight, double linespacing, double startx, ref CssLineBox line, ref double curx, ref double cury, ref double maxRight, ref double maxbottom)
    {
        var startX = curx;
        var startY = cury;
        box.FirstHostingLineBox = line;
        var localCurx = curx;
        var localMaxRight = maxRight;
        var localmaxbottom = maxbottom;

        foreach (CssBox b in box.Boxes)
        {
            // CSS2.1 §9.2.4: display:none elements generate no boxes and
            // must not participate in layout — skip them entirely.
            if (b.Display == CssConstants.None)
                continue;

            // CSS2.1 §9.5: Floated elements are out of normal flow and
            // must not participate in the inline formatting context.
            // Their positioning is handled separately in PerformLayoutImp.
            if (b.Float != CssConstants.None)
                continue;

            // CSS2.1 §9.6.1: Absolutely and fixed positioned elements are
            // out of normal flow.  Save the current flow state so we can
            // restore it after laying out the child — its words must not
            // shift subsequent siblings or inflate the parent's content
            // height.
            bool isAbsposChild = b.Position == CssConstants.Absolute
                || b.Position == CssConstants.Fixed;
            double childSaveCurx = curx;
            double childSaveMaxRight = maxRight;
            double childSaveMaxBottom = maxbottom;

            double leftspacing = !isAbsposChild ? b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft : 0;
            double rightspacing = !isAbsposChild ? b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight : 0;

            b.RectanglesReset();
            b.MeasureWordsSize(g);

            curx += leftspacing;

            if (b.Words.Count > 0)
            {
                bool wrapNoWrapBox = false;
                if (b.WhiteSpace == CssConstants.NoWrap && curx > startx)
                {
                    var boxRight = curx;
                    foreach (var word in b.Words)
                        boxRight += word.FullWidth;

                    if (boxRight > limitRight)
                        wrapNoWrapBox = true;
                }

                if (DomUtils.IsBoxHasWhitespace(b))
                    curx += box.ActualWordSpacing;

                foreach (var word in b.Words)
                {
                    // CSS2.1 §10.8: Every line box has a minimum height
                    // from the block container's line-height (the "strut").
                    // When line-height is 'normal' (ActualLineHeight == 0),
                    // the minimum comes from the font metrics, scaled to CSS
                    // px (font.Height is at pt-scale because the layout font
                    // is created at pt size in canvas units).
                    double boxLineHeight = box.ActualLineHeight > 0
                        ? box.ActualLineHeight
                        : box.ActualFont.Height * PtToCssPx;
                    if (maxbottom - cury < boxLineHeight)
                        maxbottom += boxLineHeight - (maxbottom - cury);

                    // CSS2.1 §10.8: The "strut" — each line box has a minimum
                    // height from the block container's font and line-height.
                    // For replaced inline elements (images), apply the block
                    // container's strut so that baseline alignment pushes the
                    // image down when the font is larger than the image.
                    double strutHeight = 0;
                    if (word.IsImage)
                    {
                        strutHeight = blockbox.ActualLineHeight;
                        if (strutHeight <= 0)
                            strutHeight = blockbox.ActualFont.Height * PtToCssPx;

                        if (maxbottom - cury < strutHeight)
                            maxbottom += strutHeight - (maxbottom - cury);
                    }

                    if ((b.WhiteSpace != CssConstants.NoWrap && b.WhiteSpace != CssConstants.Pre && curx + word.Width + rightspacing > limitRight
                         && (b.WhiteSpace != CssConstants.PreWrap || !word.IsSpaces)
                         && (b.WhiteSpace != CssConstants.PreLine || !word.IsSpaces)) || word.IsLineBreak || wrapNoWrapBox)
                    {
                        wrapNoWrapBox = false;
                        curx = startx;

                        // handle if line is wrapped for the first text element where parent has left margin\padding
                        if (b == box.Boxes[0] && !word.IsLineBreak && (word == b.Words[0] || (box.ParentBox != null && box.ParentBox.IsBlock)))
                            curx += box.ActualMarginLeft + box.ActualBorderLeftWidth + box.ActualPaddingLeft;

                        cury = maxbottom + linespacing;

                        line = new CssLineBox(blockbox);

                        if (word.IsImage || word.Equals(b.FirstWord))
                            curx += leftspacing;
                    }

                    line.ReportExistanceOf(word);

                    word.Left = curx;

                    // CSS2.1 §10.8.1: Replaced inline elements (images) are
                    // baseline-aligned by default — the bottom of the replaced
                    // element sits on the baseline.  The baseline position
                    // within the strut is at the font's ascent from the top.
                    if (word.IsImage && strutHeight > word.Height)
                    {
                        double fontHeight = blockbox.ActualFont.Height * PtToCssPx;
                        double baseline = fontHeight * TypicalAscentRatio;
                        word.Top = Math.Max(cury, cury + baseline - word.Height);
                    }
                    else
                    {
                        word.Top = cury;
                    }

                    if (!box.IsFixed)
                    {
                        word.BreakPage();
                    }

                    curx = word.Left + word.FullWidth;

                    maxRight = Math.Max(maxRight, word.Right);
                    maxbottom = Math.Max(maxbottom, word.Bottom);

                    if (b.Position == CssConstants.Absolute)
                    {
                        word.Left += box.ActualMarginLeft;
                        word.Top += box.ActualMarginTop;
                    }
                }
            }
            else
            {
                // Determine if this child should use inline-block sizing:
                // 1. Explicit display:inline-block
                // 2. display:inline-flex / inline-grid (inline-level flex/grid)
                // 3. Direct child of a flex/grid container (all children
                //    become flex/grid items with shrink-to-fit sizing per
                //    CSS Flexbox §4 / CSS Grid §6; since Broiler lacks a
                //    true flex/grid engine, use FlowInlineBlock as a
                //    reasonable approximation)
                bool useInlineBlockFlow = b.Display == CssConstants.InlineBlock
                    || b.Display is "inline-flex" or "inline-grid"
                    || box.Display is "flex" or "inline-flex" or "grid" or "inline-grid";

                if (useInlineBlockFlow)
                {
                    // CSS 2.1 §10.3.9/§10.6.6: Inline-block boxes are laid
                    // out as blocks internally, then placed atomically in
                    // the inline flow (like replaced inline elements).
                    FlowInlineBlock(g, blockbox, b, limitRight, linespacing, startx,
                        leftspacing, rightspacing,
                        ref line, ref curx, ref cury, ref maxRight, ref maxbottom);

                    // CSS Flexbox §9.4: flex-direction:column stacks items
                    // vertically — force a line break after each flex item so
                    // the next item starts on a new row.
                    if (box.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                        && box.FlexDirection is "column" or "column-reverse")
                    {
                        cury = maxbottom;
                        curx = startx;
                        line = new CssLineBox(blockbox);
                    }
                }
                else
                {
                    // Block-level child inside inline flow: force a line break
                    // before and after the block (CSS2.1 §9.2.1.1 anonymous
                    // block boxes).  This ensures elements like <p> inside an
                    // inline <form> start on their own line.
                    if (b.IsBlock)
                    {
                        if (curx > startx || maxbottom > cury)
                        {
                            cury = maxbottom;
                            curx = startx;
                            line = new CssLineBox(blockbox);
                        }
                    }

                    FlowBox(g, blockbox, b, limitRight, linespacing, startx, ref line, ref curx, ref cury, ref maxRight, ref maxbottom);

                    if (b.IsBlock)
                    {
                        cury = maxbottom;
                        curx = startx;
                        line = new CssLineBox(blockbox);
                    }
                }
            }

            curx += rightspacing;

            // CSS2.1 §9.6.1: Restore flow state after an absolutely/fixed
            // positioned child so it does not affect siblings or parent
            // content height.
            if (isAbsposChild)
            {
                curx = childSaveCurx;
                maxRight = childSaveMaxRight;
                maxbottom = childSaveMaxBottom;
            }
        }

        // handle height setting
        if (maxbottom - startY < box.ActualHeight)
            maxbottom += box.ActualHeight - (maxbottom - startY);

        // handle width setting
        // CSS 2.1 §10.3.9: inline-block boxes handle their own sizing in
        // FlowInlineBlock — do not register them here when processing
        // their own internal content (box == blockbox).
        if (box.IsInline && box != blockbox && 0 <= curx - startX && curx - startX < box.ActualWidth)
        {
            // hack for actual width handling
            curx += box.ActualWidth - (curx - startX);
            line.Rectangles.Add(box, new RectangleF((float)startX, (float)startY, (float)box.ActualWidth, (float)box.ActualHeight));
        }

        // handle box that is only a whitespace
        if (box.Text.Length > 0 && box.Text.Span.IsWhiteSpace() && !box.IsImage && box.IsInline && box.Boxes.Count == 0 && box.Words.Count == 0)
            curx += box.ActualWordSpacing;

        // hack to support specific absolute position elements
        if (box.Position == CssConstants.Absolute)
        {
            curx = localCurx;
            maxRight = localMaxRight;
            maxbottom = localmaxbottom;
            AdjustAbsolutePosition(box, 0, 0);
        }

        box.LastHostingLineBox = line;
    }

    /// <summary>
    /// CSS 2.1 §10.3.9 / §10.6.6: Lay out an inline-block box as a
    /// block internally, then place it atomically in the inline flow.
    /// The inline-block establishes a new block formatting context for
    /// its children while participating in the parent's inline
    /// formatting context as a single opaque box.
    /// </summary>
    private static void FlowInlineBlock(RGraphics g, CssBox blockbox, CssBox b,
        double limitRight, double linespacing, double startx,
        double leftspacing, double rightspacing,
        ref CssLineBox line, ref double curx, ref double cury,
        ref double maxRight, ref double maxbottom)
    {
        // Compute the container content width for resolving percentage and
        // em-based lengths on the inline-block.
        double containerWidth = blockbox.Size.Width
            - blockbox.ActualPaddingLeft - blockbox.ActualPaddingRight
            - blockbox.ActualBorderLeftWidth - blockbox.ActualBorderRightWidth;

        // --- Compute inline-block content width ---
        double ibContentWidth;
        if (b.Width != CssConstants.Auto && !string.IsNullOrEmpty(b.Width))
        {
            ibContentWidth = CssValueParser.ParseLength(b.Width, containerWidth, b.GetEmHeight());
            if (b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            {
                ibContentWidth -= b.ActualBorderLeftWidth + b.ActualBorderRightWidth
                    + b.ActualPaddingLeft + b.ActualPaddingRight;
                if (ibContentWidth < 0)
                    ibContentWidth = 0;
            }
        }
        else
        {
            // CSS 2.1 §10.3.9: auto-width inline-block uses shrink-to-fit.
            // Measure descendant words for intrinsic width computation.
            MeasureDescendantWords(g, b);
            b.GetMinMaxWidth(out double prefMin, out double prefMax);
            if (double.IsNaN(prefMin)) prefMin = 0;
            if (double.IsNaN(prefMax)) prefMax = 0;
            // GetMinMaxWidth returns border-box widths (content + padding +
            // border).  Convert to content-only widths so the shrink-to-fit
            // calculation matches the content-only `available` value and the
            // padding/border added back at ibBoxWidth below.
            double ownPaddingBorder = b.ActualBorderLeftWidth + b.ActualBorderRightWidth
                + b.ActualPaddingLeft + b.ActualPaddingRight;
            prefMin = Math.Max(0, prefMin - ownPaddingBorder);
            prefMax = Math.Max(0, prefMax - ownPaddingBorder);
            double available = Math.Max(0, limitRight - curx - rightspacing
                - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                - b.ActualPaddingLeft - b.ActualPaddingRight);
            ibContentWidth = Math.Min(Math.Max(prefMin, available), prefMax);
        }

        // CSS 2.1 §10.4: Apply min-width constraint.
        // min-width takes priority over computed width (including
        // shrink-to-fit for auto-width inline-blocks).
        if (b.MinWidth != "0" && !string.IsNullOrEmpty(b.MinWidth))
        {
            double minW = CssValueParser.ParseLength(b.MinWidth, containerWidth, b.GetEmHeight());
            double minContentW = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? minW - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                    - b.ActualPaddingLeft - b.ActualPaddingRight
                : minW;
            if (minContentW > ibContentWidth)
                ibContentWidth = minContentW;
        }

        // CSS 2.1 §10.4: Apply max-width constraint.
        // max-width limits the computed width from above.  When both
        // min-width and max-width are specified, min-width wins if it
        // exceeds max-width (CSS2.1 §10.4).
        if (b.MaxWidth != "none" && !string.IsNullOrEmpty(b.MaxWidth))
        {
            double maxW = CssValueParser.ParseLength(b.MaxWidth, containerWidth, b.GetEmHeight());
            double maxContentW = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? maxW - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                    - b.ActualPaddingLeft - b.ActualPaddingRight
                : maxW;
            if (maxContentW < ibContentWidth)
                ibContentWidth = maxContentW;
        }

        double ibBoxWidth = ibContentWidth
            + b.ActualBorderLeftWidth + b.ActualBorderRightWidth
            + b.ActualPaddingLeft + b.ActualPaddingRight;

        // --- Line wrap check ---
        // Total inline extent: margin-left + box-width + margin-right.
        // curx already includes leftspacing (margin+border+padding), so the
        // border-box left edge is at curx - border - padding.
        double ibBorderLeft = curx - b.ActualBorderLeftWidth - b.ActualPaddingLeft;
        double edgeBeforeBox = ibBorderLeft - b.ActualMarginLeft;
        double totalExtent = b.ActualMarginLeft + ibBoxWidth + b.ActualMarginRight;
        if (edgeBeforeBox + totalExtent > limitRight && edgeBeforeBox > startx)
        {
            double lineStrut = blockbox.ActualLineHeight > 0
                ? blockbox.ActualLineHeight
                : blockbox.ActualFont.Height * PtToCssPx;
            double baselineDescent = lineStrut * (1.0 - TypicalAscentRatio);

            curx = startx + leftspacing;
            cury = maxbottom + linespacing + baselineDescent;
            line = new CssLineBox(blockbox);
            ibBorderLeft = curx - b.ActualBorderLeftWidth - b.ActualPaddingLeft;
        }

        // --- Position and size the inline-block ---
        b.Location = new PointF((float)ibBorderLeft, (float)(cury + b.ActualMarginTop));
        b.Size = new SizeF((float)ibBoxWidth, 0);
        b.ActualBottom = b.Location.Y;

        // --- Lay out children inside the inline-block ---
        if (b.Display is "grid" or "inline-grid")
        {
            // CSS Grid Level 1: Grid items should be laid out as blocks
            // (not inline-blocks) so that width:auto stretches to the
            // column width.  Use the block layout path, then apply grid
            // stacking or auto-placement to fix positioning.
            foreach (var child in b.Boxes)
                child.PerformLayout(g);

            double childMaxBottom = b.Location.Y;
            foreach (var child in b.Boxes)
                childMaxBottom = Math.Max(childMaxBottom, child.ActualBottom);
            b.ActualBottom = childMaxBottom;

            b.ApplyGridLayoutAfterInline();
        }
        else if (DomUtils.ContainsInlinesOnly(b))
        {
            CreateLineBoxes(g, b);
        }
        else if (b.Boxes.Count > 0)
        {
            foreach (var child in b.Boxes)
                child.PerformLayout(g);

            double childMaxBottom = b.Location.Y;
            foreach (var child in b.Boxes)
                childMaxBottom = Math.Max(childMaxBottom, child.ActualBottom);
            b.ActualBottom = childMaxBottom;
        }

        // --- Compute height ---
        double ibHeight;
        if (b.Height != CssConstants.Auto && !string.IsNullOrEmpty(b.Height))
        {
            double cssHeight = CssValueParser.ParseLength(b.Height, containerWidth, b.GetEmHeight());
            ibHeight = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? cssHeight
                : cssHeight
                    + b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                    + b.ActualPaddingTop + b.ActualPaddingBottom;
        }
        else
        {
            ibHeight = Math.Max(0, b.ActualBottom - b.Location.Y);
        }

        // CSS 2.1 §10.7: Apply min-height constraint for inline-blocks.
        if (b.MinHeight != "0" && !string.IsNullOrEmpty(b.MinHeight))
        {
            double minH = CssValueParser.ParseLength(b.MinHeight, containerWidth, b.GetEmHeight());
            double minBoxH = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? minH
                : minH
                    + b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                    + b.ActualPaddingTop + b.ActualPaddingBottom;
            if (minBoxH > ibHeight)
                ibHeight = minBoxH;
        }

        b.ActualBottom = b.Location.Y + ibHeight;
        b.Size = new SizeF(b.Size.Width, (float)ibHeight);

        // --- Register the inline-block as a rectangle in the line box ---
        line.Rectangles[b] = new RectangleF(b.Location.X, b.Location.Y,
            (float)ibBoxWidth, (float)ibHeight);

        // --- Advance flow position ---
        // curx has leftspacing (margin+border+padding) already added.
        // After the inline-block, set curx so that after rightspacing
        // (margin+border+padding right) is added, we end up at the
        // right margin edge of the box.
        curx = ibBorderLeft + ibBoxWidth
            - b.ActualBorderRightWidth - b.ActualPaddingRight;

        maxRight = Math.Max(maxRight, ibBorderLeft + ibBoxWidth);
        maxbottom = Math.Max(maxbottom, b.ActualBottom + b.ActualMarginBottom);
    }

    /// <summary>
    /// Recursively measures word sizes on all descendant boxes so that
    /// intrinsic width calculations are reliable.
    /// </summary>
    private static void MeasureDescendantWords(RGraphics g, CssBox box)
    {
        box.MeasureWordsSize(g);
        foreach (var child in box.Boxes)
            MeasureDescendantWords(g, child);
    }

    private static void AdjustAbsolutePosition(CssBox box, double left, double top)
    {
        left += box.ActualMarginLeft;
        top += box.ActualMarginTop;

        // CSS 2.1 §9.3.2: Apply 'top' and 'left' offsets for absolutely
        // positioned elements.
        if (box.Top != CssConstants.Auto && !string.IsNullOrEmpty(box.Top))
        {
            double topOffset = CssValueParser.ParseLength(box.Top, box.Size.Height, box.GetEmHeight());
            if (!double.IsNaN(topOffset))
                top += topOffset;
        }
        if (box.Left != CssConstants.Auto && !string.IsNullOrEmpty(box.Left))
        {
            double leftOffset = CssValueParser.ParseLength(box.Left, box.Size.Width, box.GetEmHeight());
            if (!double.IsNaN(leftOffset))
                left += leftOffset;
        }

        if (box.Words.Count > 0)
        {
            foreach (var word in box.Words)
            {
                word.Left += left;
                word.Top += top;
            }
        }
        else
        {
            foreach (var b in box.Boxes)
                AdjustAbsolutePosition(b, left, top);
        }
    }

    private static void BubbleRectangles(CssBox box, CssLineBox line)
    {
        if (box.Words.Count > 0)
        {
            double x = float.MaxValue, y = float.MaxValue, r = float.MinValue, b = float.MinValue;
            List<CssRect> words = line.WordsOf(box);

            if (words.Count <= 0)
                return;

            foreach (CssRect word in words)
            {
                // handle if line is wrapped for the first text element where parent has left margin\padding
                var left = word.Left;

                if (box == box.ParentBox.Boxes[0] && word == box.Words[0] && word == line.Words[0] && line != line.OwnerBox.LineBoxes[0] && !word.IsLineBreak)
                    left -= box.ParentBox.ActualMarginLeft + box.ParentBox.ActualBorderLeftWidth + box.ParentBox.ActualPaddingLeft;


                x = Math.Min(x, left);
                r = Math.Max(r, word.Right);
                y = Math.Min(y, word.Top);
                b = Math.Max(b, word.Bottom);
            }

            line.UpdateRectangle(box, x, y, r, b);
        }
        else
        {
            foreach (CssBox b in box.Boxes)
                BubbleRectangles(b, line);
        }
    }

    private static void ApplyHorizontalAlignment(RGraphics g, CssLineBox lineBox, bool lineRtl)
    {
        var box = lineBox.OwnerBox;

        // Resolve the logical 'start'/'end' keywords (and the initial value, which
        // is 'start') against the line's base direction.  In a left-to-right base,
        // start=left and end=right; in a right-to-left base they swap.  Physical
        // 'left'/'right'/'center'/'justify' values pass through unchanged.  Under
        // unicode-bidi:plaintext the base is the per-line resolved direction; this
        // is what keeps right-to-left lines aligned to the right edge and, without
        // it, an RTL box left its 'start'-aligned text on the left (CSS Text
        // §text-align).
        string resolvedAlign = box.TextAlign switch
        {
            null or "" or "start" => lineRtl ? CssConstants.Right : CssConstants.Left,
            "end" => lineRtl ? CssConstants.Left : CssConstants.Right,
            _ => box.TextAlign
        };

        switch (resolvedAlign)
        {
            case CssConstants.Right:
                ApplyRightAlignment(g, lineBox);
                break;
            case CssConstants.Center:
                ApplyCenterAlignment(g, lineBox);
                break;
            case CssConstants.Justify:
                ApplyJustifyAlignment(g, lineBox);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Returns the line's base direction as resolved from its first strong
    /// (Hebrew/Arabic vs. Latin/Greek/Cyrillic) character: <c>true</c> for
    /// right-to-left, <c>false</c> for left-to-right, or <c>null</c> when the line
    /// has no strong character (so the caller inherits the previous paragraph's or
    /// the containing block's direction).  Used for <c>unicode-bidi: plaintext</c>.
    /// </summary>
    private static bool? LineFirstStrongRtl(CssLineBox line)
    {
        foreach (CssRect word in line.Words)
        {
            string text = word.Text;
            if (string.IsNullOrEmpty(text))
                continue;
            foreach (char c in text)
            {
                if (IsRtlStrongChar(c))
                    return true;
                if (IsLtrStrongChar(c))
                    return false;
            }
        }
        return null; // no strong character → inherit base direction
    }

    /// <summary>
    /// Seeds the running base direction for a <c>unicode-bidi: plaintext</c> block
    /// whose first paragraph has no strong character of its own.  Such a paragraph
    /// inherits the previous paragraph's direction; because neutral paragraphs
    /// propagate that direction forward, the result equals the direction of the most
    /// recent strong character that appears before this block in document order.
    /// Falls back to the block's own direction when no preceding strong character
    /// exists (i.e. the containing block's direction).
    /// </summary>
    private static bool SeedPlaintextBaseRtl(CssBox blockBox)
    {
        var parent = blockBox.ParentBox;
        if (parent != null)
        {
            int index = parent.Boxes.IndexOf(blockBox);
            for (int i = index - 1; i >= 0; i--)
            {
                bool? strong = LastStrongRtl(parent.Boxes[i]);
                if (strong.HasValue)
                    return strong.Value;
            }
        }
        return blockBox.Direction == CssConstants.Rtl;
    }

    /// <summary>
    /// Returns the direction of the last strong character within <paramref name="box"/>'s
    /// subtree in document order (<c>true</c> RTL, <c>false</c> LTR), or <c>null</c>
    /// when the subtree has no strong character.
    /// </summary>
    private static bool? LastStrongRtl(CssBox box)
    {
        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            bool? strong = LastStrongRtl(box.Boxes[i]);
            if (strong.HasValue)
                return strong;
        }

        for (int i = box.Words.Count - 1; i >= 0; i--)
        {
            string text = box.Words[i].Text;
            if (string.IsNullOrEmpty(text))
                continue;
            for (int c = text.Length - 1; c >= 0; c--)
            {
                if (IsRtlStrongChar(text[c]))
                    return true;
                if (IsLtrStrongChar(text[c]))
                    return false;
            }
        }

        return null;
    }

    private static bool IsRtlStrongChar(char c) =>
        (c >= 0x0590 && c <= 0x05FF) ||   // Hebrew
        (c >= 0x0600 && c <= 0x06FF) ||   // Arabic
        (c >= 0x0750 && c <= 0x077F) ||   // Arabic Supplement
        (c >= 0x08A0 && c <= 0x08FF) ||   // Arabic Extended-A
        (c >= 0xFB1D && c <= 0xFDFF) ||   // Hebrew/Arabic presentation forms-A
        (c >= 0xFE70 && c <= 0xFEFF);     // Arabic presentation forms-B

    private static bool IsLtrStrongChar(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
        (c >= 0x00C0 && c <= 0x024F) ||   // Latin-1 supplement / extended
        (c >= 0x0370 && c <= 0x03FF) ||   // Greek
        (c >= 0x0400 && c <= 0x04FF);     // Cyrillic

    private static void ApplyRightToLeft(CssLineBox lineBox, bool lineRtl)
    {
        // When the line's base direction is right-to-left the whole line is
        // mirrored; otherwise only the individual inline boxes that opt into RTL
        // are reversed.  Under unicode-bidi:plaintext 'lineRtl' is the per-line
        // resolved direction, so left-to-right lines inside an RTL block stay on
        // the left instead of being mirrored to the right edge.
        if (lineRtl)
        {
            ApplyRightToLeftOnLine(lineBox);
        }
        else
        {
            foreach (var box in lineBox.RelatedBoxes)
            {
                if (box.Direction == CssConstants.Rtl)
                    ApplyRightToLeftOnSingleBox(lineBox, box);
            }
        }
    }

    private static void ApplyRightToLeftOnLine(CssLineBox line)
    {
        if (line.Words.Count <= 0)
            return;

        double left = line.Words[0].Left;
        double right = line.Words[line.Words.Count - 1].Right;

        foreach (CssRect word in line.Words)
        {
            double diff = word.Left - left;
            double wright = right - diff;

            word.Left = wright - word.Width;
        }
    }

    private static void ApplyRightToLeftOnSingleBox(CssLineBox lineBox, CssBox box)
    {
        int leftWordIdx = -1;
        int rightWordIdx = -1;

        for (int i = 0; i < lineBox.Words.Count; i++)
        {
            if (lineBox.Words[i].OwnerBox != box)
                continue;

            if (leftWordIdx < 0)
                leftWordIdx = i;

            rightWordIdx = i;
        }

        if (leftWordIdx <= -1 || rightWordIdx <= leftWordIdx)
            return;

        double left = lineBox.Words[leftWordIdx].Left;
        double right = lineBox.Words[rightWordIdx].Right;

        for (int i = leftWordIdx; i <= rightWordIdx; i++)
        {
            double diff = lineBox.Words[i].Left - left;
            double wright = right - diff;

            lineBox.Words[i].Left = wright - lineBox.Words[i].Width;
        }
    }

    private static void ApplyVerticalAlignment(RGraphics g, CssLineBox lineBox)
    {
        // CSS 2.1 §10.8: The baseline is where text sits, approximated as
        // the top of each box plus the font ascent. Most Latin fonts have
        // an ascent/height ratio near 0.8 (e.g. OS/2 sTypoAscender is
        // typically ~80% of UPM). This matches common browser heuristics.
        const double TypicalAscentRatio = 0.8;

        // CSS2.1 §10.8.1: Boxes with vertical-align:top or bottom do not
        // contribute to the initial line box height calculation.  Collect
        // them for a second positioning pass.
        var topBottomBoxes = new HashSet<CssBox>();
        foreach (var box in lineBox.Rectangles.Keys)
        {
            if (box.VerticalAlign == CssConstants.Top
                || box.VerticalAlign == CssConstants.Bottom)
                topBottomBoxes.Add(box);
        }

        // CSS2.1 §10.8: The "strut" — an imaginary zero-width inline box
        // with the block container's font and line-height — establishes
        // the initial baseline of the line box.  This is critical when the
        // parent has font-size: 0 (e.g. .buckets { font: 0/0 }): the strut
        // baseline is at the top of the content area and must not be
        // overridden by child inline-block font metrics.
        double lineTop = double.MaxValue;
        foreach (var kvp in lineBox.Rectangles)
        {
            if (!topBottomBoxes.Contains(kvp.Key))
                lineTop = Math.Min(lineTop, kvp.Value.Top);
        }

        // Start with the strut baseline (parent's font ascent from line top).
        double parentFontHeight = (lineBox.OwnerBox?.ActualFont.Height ?? 0) * PtToCssPx;
        double baseline = (lineTop < double.MaxValue)
            ? lineTop + parentFontHeight * TypicalAscentRatio
            : float.MinValue;

        // Non-inline-block boxes also contribute to the baseline.
        foreach (var box in lineBox.Rectangles.Keys)
        {
            if (box.Display != CssConstants.InlineBlock && !topBottomBoxes.Contains(box))
            {
                double boxBaseline = lineBox.Rectangles[box].Top
                    + box.ActualFont.Height * PtToCssPx * TypicalAscentRatio;
                baseline = Math.Max(baseline, boxBaseline);
            }
        }

        // --- Phase 1: Position all non-top/bottom boxes ---

        var boxes = new List<CssBox>(lineBox.Rectangles.Keys);
        foreach (CssBox box in boxes)
        {
            if (topBottomBoxes.Contains(box))
                continue;

            // For inline text boxes, SetBaseLine receives the desired
            // word-top position, so baseline-relative values must be
            // converted from baseline Y to word-top Y by subtracting
            // the box's ascent.
            //
            // For inline-block boxes, CSS 2.1 §10.8.1: the baseline of
            // an inline-block with no in-flow line boxes is the bottom
            // margin edge.  SetBaseLine positions the box by its top, so
            // we must subtract the box height to convert from the desired
            // bottom-edge position to the top-edge position.
            bool isInlineBlock = box.Display == CssConstants.InlineBlock;
            double boxAscent = isInlineBlock
                ? lineBox.Rectangles[box].Height
                : box.ActualFont.Height * PtToCssPx * TypicalAscentRatio;

            //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
            switch (box.VerticalAlign)
            {
                case CssConstants.Sub:
                    lineBox.SetBaseLine(g, box, baseline - boxAscent + lineBox.Rectangles[box].Height * .5f);
                    break;
                case CssConstants.Super:
                    lineBox.SetBaseLine(g, box, baseline - boxAscent - lineBox.Rectangles[box].Height * .2f);
                    break;
                case CssConstants.TextTop:
                    // CSS 2.1 §10.8.1: Align the top of the box with the
                    // top of the parent element's content area (font top).
                    if (baseline > float.MinValue)
                    {
                        double parentContentTop = baseline - parentFontHeight * TypicalAscentRatio;
                        lineBox.SetBaseLine(g, box, parentContentTop);
                    }
                    break;
                case CssConstants.TextBottom:
                    // CSS 2.1 §10.8.1: Align the bottom of the box with the
                    // bottom of the parent element's content area (font bottom).
                    if (baseline > float.MinValue && lineBox.Rectangles.ContainsKey(box))
                    {
                        double boxHeight = lineBox.Rectangles[box].Height;
                        double parentContentBottom = baseline + parentFontHeight * (1.0 - TypicalAscentRatio);
                        lineBox.SetBaseLine(g, box, parentContentBottom - boxHeight);
                    }
                    break;
                case CssConstants.Middle:
                    // CSS 2.1 §10.8.1: Align the vertical midpoint of the box
                    // with the baseline plus half the x-height of the parent.
                    // x-height ≈ 0.5 × font height for Latin fonts; half of
                    // that is 0.25 × font height.
                    if (lineBox.Rectangles.ContainsKey(box) && baseline > float.MinValue)
                    {
                        double boxHeight = lineBox.Rectangles[box].Height;
                        double parentFont = (box.ParentBox?.ActualFont.Height ?? 0) * PtToCssPx;
                        double halfXHeight = parentFont * 0.25;
                        lineBox.SetBaseLine(g, box, baseline + halfXHeight - boxHeight / 2);
                    }
                    break;
                default:
                    // CSS 2.1 §10.8.1: A <length> or <percentage> value
                    // raises (positive) or lowers (negative) the box by
                    // the given distance relative to the baseline.
                    // A percentage is calculated against the line-height
                    // of the element itself.
                    if (box.VerticalAlign != CssConstants.Baseline
                        && !string.IsNullOrEmpty(box.VerticalAlign))
                    {
                        double lineHeight = box.ActualLineHeight > 0
                            ? box.ActualLineHeight
                            : box.ActualFont.Height * PtToCssPx;
                        double offset = CssValueParser.ParseLength(
                            box.VerticalAlign, lineHeight, box.GetEmHeight());
                        if (!double.IsNaN(offset) && offset != 0)
                        {
                            // Positive values move the box UP (raise).
                            lineBox.SetBaseLine(g, box, baseline - boxAscent - offset);
                            break;
                        }
                    }
                    //case: baseline
                    lineBox.SetBaseLine(g, box, baseline - boxAscent);
                    break;
            }
        }

        // --- Phase 2: Position top/bottom-aligned boxes ---
        // CSS 2.1 §10.8.1: After all other boxes are positioned, compute
        // the final line box extent and align top/bottom boxes within it.
        if (topBottomBoxes.Count > 0)
        {
            double finalTop = double.MaxValue;
            double finalBottom = double.MinValue;
            foreach (var kvp in lineBox.Rectangles)
            {
                if (!topBottomBoxes.Contains(kvp.Key))
                {
                    finalTop = Math.Min(finalTop, kvp.Value.Top);
                    finalBottom = Math.Max(finalBottom, kvp.Value.Bottom);
                }
            }

            // Also consider word positions for the final line box bounds.
            foreach (var word in lineBox.Words)
            {
                if (!topBottomBoxes.Contains(word.OwnerBox))
                {
                    finalTop = Math.Min(finalTop, word.Top);
                    finalBottom = Math.Max(finalBottom, word.Bottom);
                }
            }

            foreach (CssBox box in boxes)
            {
                if (!topBottomBoxes.Contains(box))
                    continue;

                if (box.VerticalAlign == CssConstants.Top)
                {
                    if (finalTop < double.MaxValue)
                        lineBox.SetBaseLine(g, box, finalTop);
                }
                else // Bottom
                {
                    if (finalBottom > double.MinValue && lineBox.Rectangles.ContainsKey(box))
                    {
                        double boxHeight = lineBox.Rectangles[box].Height;
                        lineBox.SetBaseLine(g, box, finalBottom - boxHeight);
                    }
                }
            }
        }
    }

    private static void ApplyJustifyAlignment(RGraphics g, CssLineBox lineBox)
    {
        if (lineBox.Equals(lineBox.OwnerBox.LineBoxes[lineBox.OwnerBox.LineBoxes.Count - 1]))
            return;

        double indent = lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]) ? lineBox.OwnerBox.ActualTextIndent : 0f;
        double textSum = 0f;
        double words = 0f;
        double availWidth = lineBox.OwnerBox.ClientRectangle.Width - indent;

        // Gather text sum
        foreach (CssRect w in lineBox.Words)
        {
            textSum += w.Width;
            words += 1f;
        }

        if (words <= 0f)
            return; //Avoid Zero division

        double spacing = (availWidth - textSum) / words; //Spacing that will be used
        double curx = lineBox.OwnerBox.ClientLeft + indent;

        foreach (CssRect word in lineBox.Words)
        {
            word.Left = curx;
            curx = word.Right + spacing;

            if (word == lineBox.Words[lineBox.Words.Count - 1])
                word.Left = lineBox.OwnerBox.ClientRight - word.Width;
        }
    }

    private static void ApplyCenterAlignment(RGraphics g, CssLineBox line)
    {
        if (line.Words.Count == 0 && line.Rectangles.Count == 0)
            return;

        double right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;

        // Find the rightmost content edge from both words and inline-block rectangles.
        // Lines may contain only inline-block elements (e.g. form controls inside
        // <center>) with no direct text words.
        double contentRight = 0;
        if (line.Words.Count > 0)
        {
            CssRect lastWord = line.Words[line.Words.Count - 1];
            contentRight = lastWord.Right + lastWord.OwnerBox.ActualBorderRightWidth + lastWord.OwnerBox.ActualPaddingRight;
        }

        foreach (var kvp in line.Rectangles)
        {
            if (kvp.Value.Right > contentRight)
                contentRight = kvp.Value.Right;
        }

        double diff = (right - contentRight) / 2;

        if (diff <= 0)
            return;

        foreach (CssRect word in line.Words)
            word.Left += diff;

        foreach (CssBox b in ToList(line.Rectangles.Keys))
        {
            RectangleF r = line.Rectangles[b];
            line.Rectangles[b] = new RectangleF((float)(r.X + diff), r.Y, r.Width, r.Height);
            ShiftInlineBlockBox(b, diff);
        }
    }

    private static void ApplyRightAlignment(RGraphics g, CssLineBox line)
    {
        if (line.Words.Count == 0 && line.Rectangles.Count == 0)
            return;

        double right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;

        // Find the rightmost content edge from both words and inline-block rectangles.
        double contentRight = 0;
        if (line.Words.Count > 0)
        {
            CssRect lastWord = line.Words[line.Words.Count - 1];
            contentRight = lastWord.Right + lastWord.OwnerBox.ActualBorderRightWidth + lastWord.OwnerBox.ActualPaddingRight;
        }

        foreach (var kvp in line.Rectangles)
        {
            if (kvp.Value.Right > contentRight)
                contentRight = kvp.Value.Right;
        }

        double diff = right - contentRight;

        if (diff <= 0)
            return;

        foreach (CssRect word in line.Words)
            word.Left += diff;

        foreach (CssBox b in ToList(line.Rectangles.Keys))
        {
            RectangleF r = line.Rectangles[b];
            line.Rectangles[b] = new RectangleF((float)(r.X + diff), r.Y, r.Width, r.Height);
            ShiftInlineBlockBox(b, diff);
        }
    }

    /// <summary>
    /// Shifts an inline-block box and all its descendant boxes horizontally.
    /// Called by <see cref="ApplyCenterAlignment"/> and <see cref="ApplyRightAlignment"/>
    /// to ensure the box's actual <see cref="CssBox.Location"/> matches the shifted
    /// line-box rectangle, so background, border, and child content paint at the
    /// correct position.  CSS 2.1 §9.4.2.
    /// </summary>
    private static void ShiftInlineBlockBox(CssBox b, double dx)
    {
        if (b.Display != CssConstants.InlineBlock)
            return;

        b.Location = new PointF((float)(b.Location.X + dx), b.Location.Y);

        // Shift all descendant boxes so child content (text, nested boxes)
        // renders at the correct position.
        ShiftDescendantBoxes(b, dx);

        // Shift rectangles already assigned to the inline-block from its own
        // line boxes (via BubbleRectangles + AssignRectanglesToBoxes that ran
        // before centering).  Without this, the FragmentTreeBuilder captures
        // stale InlineRects at the original position, causing double borders.
        ShiftAssignedRectangles(b, dx);
    }

    private static void ShiftDescendantBoxes(CssBox parent, double dx)
    {
        foreach (var child in parent.Boxes)
        {
            child.Location = new PointF((float)(child.Location.X + dx), child.Location.Y);

            // Shift rectangles already assigned to this child.
            ShiftAssignedRectangles(child, dx);

            ShiftDescendantBoxes(child, dx);
        }

        // Shift words and rectangles within this box's own line boxes.
        foreach (var lineBox in parent.LineBoxes)
        {
            foreach (var word in lineBox.Words)
                word.Left += dx;

            foreach (var key in ToList(lineBox.Rectangles.Keys))
            {
                var r = lineBox.Rectangles[key];
                lineBox.Rectangles[key] = new RectangleF((float)(r.X + dx), r.Y, r.Width, r.Height);
            }
        }
    }

    /// <summary>
    /// Shifts all per-line-box rectangles that have been assigned to a box
    /// (via <see cref="CssLineBox.AssignRectanglesToBoxes"/>) by <paramref name="dx"/>
    /// pixels horizontally.
    /// </summary>
    private static void ShiftAssignedRectangles(CssBox box, double dx)
    {
        if (box.Rectangles.Count == 0)
            return;

        foreach (var key in ToList(box.Rectangles.Keys))
        {
            var r = box.Rectangles[key];
            box.Rectangles[key] = new RectangleF((float)(r.X + dx), r.Y, r.Width, r.Height);
        }
    }

    /// <summary>
    /// todo: optimizate, not creating a list each time
    /// </summary>
    private static List<T> ToList<T>(IEnumerable<T> collection)
    {
        List<T> result = [.. collection];
        return result;
    }
}
