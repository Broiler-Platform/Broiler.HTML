using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters;
using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.CSS.Core.Dom;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Utils.Core.Utils;

namespace Broiler.HTML.Dom;

internal sealed class CssLayoutEngineTable
{
    private readonly CssBox _tableBox;

    private CssBox _headerBox;
    private CssBox _footerBox;

    private readonly List<CssBox> _bodyrows = [];
    private readonly List<CssBox> _columns = [];
    private readonly List<CssBox> _allRows = [];

    private int _columnCount;

    private bool _widthSpecified;

    private double[] _columnWidths;
    private double[] _columnMinWidths;

    private CssLayoutEngineTable(CssBox tableBox) => _tableBox = tableBox;

    public static double GetTableSpacing(CssBox tableBox)
    {
        int count = 0;
        int columns = 0;

        foreach (var box in tableBox.Boxes)
        {
            if (box.Display == CssConstants.TableColumn)
            {
                columns += GetSpan(box);
            }
            else if (box.Display == CssConstants.TableRowGroup)
            {
                foreach (CssBox cr in tableBox.Boxes)
                {
                    count++;
                    if (cr.Display == CssConstants.TableRow)
                        columns = Math.Max(columns, cr.Boxes.Count);
                }
            }
            else if (box.Display == CssConstants.TableRow)
            {
                count++;
                columns = Math.Max(columns, box.Boxes.Count);
            }

            // limit the amount of rows to process for performance
            if (count > 30)
                break;
        }

        // +1 columns because padding is between the cell and table borders
        return (columns + 1) * GetHorizontalSpacing(tableBox);
    }

    public static void PerformLayout(RGraphics g, CssBox tableBox, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(tableBox);

        try
        {
            var table = new CssLayoutEngineTable(tableBox);
            table.Layout(g, baseUrl);
        }
        catch (Exception ex)
        {
            tableBox.ContainerInt.ReportError(HtmlRenderErrorType.Layout, "Failed table layout", ex);
        }
    }


    private void Layout(RGraphics g, Uri baseUrl)
    {
        MeasureWords(_tableBox, g);

        // get the table boxes into the proper fields
        AssignBoxKinds(baseUrl);

        // Insert EmptyBoxes for vertical cell spanning. 
        InsertEmptyBoxes(baseUrl);

        // Determine Row and Column Count, and ColumnWidths
        var availCellSpace = CalculateCountAndWidth();

        DetermineMissingColumnWidths(availCellSpace);

        // Check for minimum sizes (increment widths if necessary)
        EnforceMinimumSize();

        // While table width is larger than it should, and width is reducible
        EnforceMaximumSize();

        // Ensure there's no padding
        _tableBox.PaddingLeft = _tableBox.PaddingTop = _tableBox.PaddingRight = _tableBox.PaddingBottom = "0";

        //Actually layout cells!
        LayoutCells(g);
    }

    private void AssignBoxKinds(Uri baseUrl)
    {
        // CSS2.1 §17.2.1: Generate anonymous table-row boxes for table-cell
        // children that are direct children of the table without an
        // intermediate table-row wrapper.
        GenerateAnonymousTableRows(baseUrl);

        foreach (var box in _tableBox.Boxes)
        {
            switch (box.Display)
            {
                case CssConstants.TableCaption:
                    break;
                case CssConstants.TableRow:
                    _bodyrows.Add(box);
                    break;
                case CssConstants.TableRowGroup:
                    foreach (CssBox childBox in box.Boxes)
                        if (childBox.Display == CssConstants.TableRow)
                            _bodyrows.Add(childBox);
                    break;
                case CssConstants.TableHeaderGroup:
                    if (_headerBox != null)
                        _bodyrows.Add(box);
                    else
                        _headerBox = box;
                    break;
                case CssConstants.TableFooterGroup:
                    if (_footerBox != null)
                        _bodyrows.Add(box);
                    else
                        _footerBox = box;
                    break;
                case CssConstants.TableColumn:
                    for (int i = 0; i < GetSpan(box); i++)
                        _columns.Add(box);
                    break;
                case CssConstants.TableColumnGroup:
                    if (box.Boxes.Count == 0)
                    {
                        int gspan = GetSpan(box);
                        for (int i = 0; i < gspan; i++)
                        {
                            _columns.Add(box);
                        }
                    }
                    else
                    {
                        foreach (CssBox bb in box.Boxes)
                        {
                            int bbspan = GetSpan(bb);
                            for (int i = 0; i < bbspan; i++)
                            {
                                _columns.Add(bb);
                            }
                        }
                    }
                    break;
            }
        }

        if (_headerBox != null)
            _allRows.AddRange(_headerBox.Boxes);

        _allRows.AddRange(_bodyrows);

        if (_footerBox != null)
            _allRows.AddRange(_footerBox.Boxes);
    }

    /// <summary>
    /// CSS2.1 §17.2.1: Generate anonymous table-row boxes for children of a
    /// table element that are not proper table sub-elements (table-row,
    /// table-row-group, table-header-group, table-footer-group, table-caption,
    /// table-column, or table-column-group).  All consecutive non-row children
    /// are wrapped together in a single anonymous table-row.  Within each
    /// anonymous row, children that are not table-cell are additionally wrapped
    /// in anonymous table-cell boxes.
    /// </summary>
    private void GenerateAnonymousTableRows(Uri baseUrl)
    {
        bool needsWrapping = false;
        foreach (var box in _tableBox.Boxes)
        {
            if (!IsProperTableChild(box.Display))
            {
                needsWrapping = true;
                break;
            }
        }

        if (!needsWrapping)
            return;

        // Collect children and group consecutive non-row children into
        // anonymous table-row wrappers.
        var children = new List<CssBox>(_tableBox.Boxes);
        _tableBox.Boxes.Clear();

        List<CssBox>? pendingNonRow = null;

        foreach (var child in children)
        {
            if (IsProperTableChild(child.Display))
            {
                if (pendingNonRow != null)
                {
                    FlushAnonymousRow(pendingNonRow, baseUrl);
                    pendingNonRow = null;
                }

                _tableBox.Boxes.Add(child);
            }
            else
            {
                pendingNonRow ??= new List<CssBox>();
                pendingNonRow.Add(child);
            }
        }

        if (pendingNonRow != null)
            FlushAnonymousRow(pendingNonRow, baseUrl);
    }

    /// <summary>
    /// Returns true if the display value is a proper direct child of a table
    /// element per CSS2.1 §17.2.1 (table-row, row-group, caption, column, etc.).
    /// </summary>
    private static bool IsProperTableChild(string display)
    {
        return display == CssConstants.TableRow
            || display == CssConstants.TableRowGroup
            || display == CssConstants.TableHeaderGroup
            || display == CssConstants.TableFooterGroup
            || display == CssConstants.TableCaption
            || display == CssConstants.TableColumn
            || display == CssConstants.TableColumnGroup;
    }

    /// <summary>
    /// Creates an anonymous table-row box, re-parents the given children into
    /// it, and appends the row to the table.  Children that are not table-cell
    /// are additionally wrapped in anonymous table-cell boxes (CSS2.1 §17.2.1).
    /// </summary>
    private void FlushAnonymousRow(List<CssBox> children, Uri baseUrl)
    {
        // Create the anonymous row. The CssBox(parent, tag) constructor
        // automatically adds the new box to parent.Boxes.
        var anonRow = new CssBox(_tableBox, null, baseUrl) { Display = CssConstants.TableRow };
        foreach (var child in children)
        {
            if (child.Display == CssConstants.TableCell)
            {
                // Already a table-cell — re-parent into the anonymous row.
                // Using the ParentBox setter updates _parentBox and adds
                // the child to anonRow.Boxes.
                child.ParentBox = anonRow;
            }
            else
            {
                // CSS2.1 §17.2.1: Wrap non-cell children in an anonymous
                // table-cell box.  The CssBox constructor automatically adds
                // the anonymous cell to anonRow.Boxes.
                var anonCell = new CssBox(anonRow, null, baseUrl) { Display = CssConstants.TableCell };
                child.ParentBox = anonCell;
            }
        }
    }

    private void InsertEmptyBoxes(Uri baseUrl)
    {
        if (_tableBox._tableFixed)
            return;

        int currow = 0;
        List<CssBox> rows = _bodyrows;

        foreach (CssBox row in rows)
        {
            for (int k = 0; k < row.Boxes.Count; k++)
            {
                CssBox cell = row.Boxes[k];
                int rowspan = GetRowSpan(cell);
                int realcol = GetCellRealColumnIndex(row, cell); //Real column of the cell

                for (int i = currow + 1; i < currow + rowspan; i++)
                {
                    if (rows.Count <= i)
                        continue;

                    int colcount = 0;
                    for (int j = 0; j < rows[i].Boxes.Count; j++)
                    {
                        if (colcount == realcol)
                        {
                            rows[i].Boxes.Insert(colcount, new CssSpacingBox(_tableBox, ref cell, currow, baseUrl));
                            break;
                        }

                        colcount++;
                        realcol -= GetColSpan(rows[i].Boxes[j]) - 1;
                    }
                }
            }

            currow++;
        }

        _tableBox._tableFixed = true;
    }

    private double CalculateCountAndWidth()
    {
        //Columns
        if (_columns.Count > 0)
        {
            _columnCount = _columns.Count;
        }
        else
        {
            foreach (CssBox b in _allRows)
                _columnCount = Math.Max(_columnCount, b.Boxes.Count);
        }

        //Initialize column widths array with NaNs
        _columnWidths = new double[_columnCount];
        for (int i = 0; i < _columnWidths.Length; i++)
            _columnWidths[i] = double.NaN;

        double availCellSpace = GetAvailableCellWidth();

        if (_columns.Count > 0)
        {
            // Fill ColumnWidths array by scanning column widths
            for (int i = 0; i < _columns.Count; i++)
            {
                CssLength len = new(_columns[i].Width); //Get specified width

                if (len.Number <= 0) //If some width specified
                    continue;

                if (len.IsPercentage) //Get width as a percentage
                {
                    _columnWidths[i] = CssValueParser.ParseNumber(_columns[i].Width, availCellSpace);
                }
                else if (len.Unit == CssUnit.Pixels || len.Unit == CssUnit.None)
                {
                    _columnWidths[i] = len.Number; //Get width as an absolute-pixel value
                }
            }
        }
        else
        {
            // Fill ColumnWidths array by scanning width in table-cell definitions
            foreach (CssBox row in _allRows)
            {
                //Check for column width in table-cell definitions
                for (int i = 0; i < _columnCount; i++)
                {
                    if (i >= 20 && !double.IsNaN(_columnWidths[i])) // limit column width check
                        continue;

                    if (i >= row.Boxes.Count || row.Boxes[i].Display != CssConstants.TableCell)
                        continue;

                    double len = CssValueParser.ParseLength(row.Boxes[i].Width, availCellSpace, row.Boxes[i].GetEmHeight());

                    if (len <= 0) //If some width specified
                        continue;

                    int colspan = GetColSpan(row.Boxes[i]);
                    len /= Convert.ToSingle(colspan);

                    for (int j = i; j < i + colspan; j++)
                        _columnWidths[j] = double.IsNaN(_columnWidths[j]) ? len : Math.Max(_columnWidths[j], len);
                }
            }
        }

        return availCellSpace;
    }

    private void DetermineMissingColumnWidths(double availCellSpace)
    {
        double occupedSpace = 0f;

        if (_widthSpecified) //If a width was specified,
        {
            //Assign NaNs equally with space left after gathering not-NaNs
            int numOfNans = 0;

            //Calculate number of NaNs and occupied space
            foreach (double colWidth in _columnWidths)
            {
                if (double.IsNaN(colWidth))
                    numOfNans++;
                else
                    occupedSpace += colWidth;
            }

            var orgNumOfNans = numOfNans;
            double[] orgColWidths = null;

            if (numOfNans < _columnWidths.Length)
            {
                orgColWidths = new double[_columnWidths.Length];
                for (int i = 0; i < _columnWidths.Length; i++)
                    orgColWidths[i] = _columnWidths[i];
            }

            if (numOfNans > 0)
            {
                // Determine the max width for each column
                GetColumnsMinMaxWidthByContent(true, out double[] minFullWidths, out double[] maxFullWidths);

                // set the columns that can fulfill by the max width in a loop because it changes the nanWidth
                int oldNumOfNans;
                do
                {
                    oldNumOfNans = numOfNans;

                    for (int i = 0; i < _columnWidths.Length; i++)
                    {
                        var nanWidth = (availCellSpace - occupedSpace) / numOfNans;
                        if (double.IsNaN(_columnWidths[i]) && nanWidth > maxFullWidths[i])
                        {
                            _columnWidths[i] = maxFullWidths[i];
                            numOfNans--;
                            occupedSpace += maxFullWidths[i];
                        }
                    }
                } while (oldNumOfNans != numOfNans);

                if (numOfNans > 0)
                {
                    // Determine width that will be assigned to un assigned widths
                    double nanWidth = (availCellSpace - occupedSpace) / numOfNans;

                    for (int i = 0; i < _columnWidths.Length; i++)
                    {
                        if (double.IsNaN(_columnWidths[i]))
                            _columnWidths[i] = nanWidth;
                    }
                }
            }

            if (numOfNans == 0 && occupedSpace < availCellSpace)
            {
                if (orgNumOfNans > 0)
                {
                    // spread extra width between all non width specified columns
                    double extWidth = (availCellSpace - occupedSpace) / orgNumOfNans;
                    for (int i = 0; i < _columnWidths.Length; i++)
                        if (orgColWidths == null || double.IsNaN(orgColWidths[i]))
                            _columnWidths[i] += extWidth;
                }
                else
                {
                    // spread extra width between all columns with respect to relative sizes
                    for (int i = 0; i < _columnWidths.Length; i++)
                        _columnWidths[i] += (availCellSpace - occupedSpace) * (_columnWidths[i] / occupedSpace);
                }
            }
        }
        else
        {
            //Get the minimum and maximum full length of NaN boxes
            GetColumnsMinMaxWidthByContent(true, out double[] minFullWidths, out double[] maxFullWidths);

            for (int i = 0; i < _columnWidths.Length; i++)
            {
                if (double.IsNaN(_columnWidths[i]))
                    _columnWidths[i] = minFullWidths[i];
                occupedSpace += _columnWidths[i];
            }

            // spread extra width between all columns
            for (int i = 0; i < _columnWidths.Length; i++)
            {
                if (maxFullWidths[i] > _columnWidths[i])
                {
                    var temp = _columnWidths[i];
                    _columnWidths[i] = Math.Min(_columnWidths[i] + (availCellSpace - occupedSpace) / Convert.ToSingle(_columnWidths.Length - i), maxFullWidths[i]);
                    occupedSpace = occupedSpace + _columnWidths[i] - temp;
                }
            }
        }
    }

    private void EnforceMaximumSize()
    {
        int curCol = 0;
        var widthSum = GetWidthSum();
        while (widthSum > GetAvailableTableWidth() && CanReduceWidth())
        {
            while (!CanReduceWidth(curCol))
                curCol++;

            _columnWidths[curCol] -= 1f;

            curCol++;

            if (curCol >= _columnWidths.Length)
                curCol = 0;
        }

        // if table max width is limited by we need to lower the columns width even if it will result in clipping
        var maxWidth = GetMaxTableWidth();
        if (maxWidth >= 90999)
            return;

        widthSum = GetWidthSum();

        if (maxWidth >= widthSum)
            return;

        //Get the minimum and maximum full length of NaN boxes
        GetColumnsMinMaxWidthByContent(false, out double[] minFullWidths, out double[] maxFullWidths);

        // lower all the columns to the minimum
        for (int i = 0; i < _columnWidths.Length; i++)
            _columnWidths[i] = minFullWidths[i];

        // either min for all column is not enought and we need to lower it more resulting in clipping
        // or we now have extra space so we can give it to columns than need it
        widthSum = GetWidthSum();

        if (maxWidth < widthSum)
        {
            // lower the width of columns starting from the largest one until the max width is satisfied
            for (int a = 0; a < 15 && maxWidth < widthSum - 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                int nonMaxedColumns = 0;
                double largeWidth = 0f, secLargeWidth = 0f;

                for (int i = 0; i < _columnWidths.Length; i++)
                {
                    if (_columnWidths[i] > largeWidth + 0.1)
                    {
                        secLargeWidth = largeWidth;
                        largeWidth = _columnWidths[i];
                        nonMaxedColumns = 1;
                    }
                    else if (_columnWidths[i] > largeWidth - 0.1)
                    {
                        nonMaxedColumns++;
                    }
                }

                double decrease = secLargeWidth > 0 ? largeWidth - secLargeWidth : (widthSum - maxWidth) / _columnWidths.Length;
                if (decrease * nonMaxedColumns > widthSum - maxWidth)
                    decrease = (widthSum - maxWidth) / nonMaxedColumns;

                for (int i = 0; i < _columnWidths.Length; i++)
                    if (_columnWidths[i] > largeWidth - 0.1)
                        _columnWidths[i] -= decrease;

                widthSum = GetWidthSum();
            }
        }
        else
        {
            // spread extra width to columns that didn't reached max width where trying to spread it between all columns
            for (int a = 0; a < 15 && maxWidth > widthSum + 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                int nonMaxedColumns = 0;
                for (int i = 0; i < _columnWidths.Length; i++)
                    if (_columnWidths[i] + 1 < maxFullWidths[i])
                        nonMaxedColumns++;
                if (nonMaxedColumns == 0)
                    nonMaxedColumns = _columnWidths.Length;

                bool hit = false;
                double minIncrement = (maxWidth - widthSum) / nonMaxedColumns;
                for (int i = 0; i < _columnWidths.Length; i++)
                {
                    if (_columnWidths[i] + 0.1 < maxFullWidths[i])
                    {
                        minIncrement = Math.Min(minIncrement, maxFullWidths[i] - _columnWidths[i]);
                        hit = true;
                    }
                }

                for (int i = 0; i < _columnWidths.Length; i++)
                    if (!hit || _columnWidths[i] + 1 < maxFullWidths[i])
                        _columnWidths[i] += minIncrement;

                widthSum = GetWidthSum();
            }
        }
    }

    /// <summary>
    /// Check for minimum sizes (increment widths if necessary)
    /// </summary>
    private void EnforceMinimumSize()
    {
        foreach (CssBox row in _allRows)
        {
            foreach (CssBox cell in row.Boxes)
            {
                int colspan = GetColSpan(cell);
                int col = GetCellRealColumnIndex(row, cell);
                int affectcol = col + colspan - 1;

                if (_columnWidths.Length <= col || _columnWidths[col] >= GetColumnMinWidths()[col])
                    continue;

                double diff = GetColumnMinWidths()[col] - _columnWidths[col];
                _columnWidths[affectcol] = GetColumnMinWidths()[affectcol];

                if (col < _columnWidths.Length - 1)
                    _columnWidths[col + 1] -= diff;
            }
        }
    }

    private void LayoutCells(RGraphics g)
    {
        double startx = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
        double starty = Math.Max(_tableBox.ClientTop + GetVerticalSpacing(), 0);
        double cury = starty;
        double maxRight = startx;
        double maxBottom = 0f;
        int currentrow = 0;

        // change start X by if the table should align to center or right
        if (_tableBox.TextAlign == CssConstants.Center || _tableBox.TextAlign == CssConstants.Right)
        {
            double maxRightCalc = GetWidthSum();
            startx = _tableBox.TextAlign == CssConstants.Right
                ? GetAvailableTableWidth() - maxRightCalc
                : startx + (GetAvailableTableWidth() - maxRightCalc) / 2;

            _tableBox.Location = new PointF((float)(startx - _tableBox.ActualBorderLeftWidth - _tableBox.ActualPaddingLeft - GetHorizontalSpacing()), _tableBox.Location.Y);
        }

        for (int i = 0; i < _allRows.Count; i++)
        {
            var row = _allRows[i];

            // CSS2.1 §17.5.5: Rows with visibility:collapse are hidden and do
            // not contribute height.  Column widths are still affected (handled
            // during column width calculation).
            if (row.Visibility == CssConstants.Collapse)
            {
                currentrow++;
                continue;
            }

            double curx = startx;
            int curCol = 0;
            bool breakPage = false;

            for (int j = 0; j < row.Boxes.Count; j++)
            {
                CssBox cell = row.Boxes[j];
                if (curCol >= _columnWidths.Length)
                    break;

                int rowspan = GetRowSpan(cell);
                var columnIndex = GetCellRealColumnIndex(row, cell);
                double width = GetCellWidth(columnIndex, cell);

                cell.Location = new PointF((float)curx, (float)cury);
                cell.Size = new SizeF((float)width, 0f);
                cell.PerformLayout(g); //That will automatically set the bottom of the cell

                //Alter max bottom only if row is cell's row + cell's rowspan - 1
                if (cell is CssSpacingBox sb)
                {
                    if (sb.EndRow == currentrow)
                        maxBottom = Math.Max(maxBottom, sb.ExtendedBox.ActualBottom);
                }
                else if (rowspan == 1)
                {
                    maxBottom = Math.Max(maxBottom, cell.ActualBottom);
                }

                maxRight = Math.Max(maxRight, cell.ActualRight);
                curCol++;
                curx = cell.ActualRight + GetHorizontalSpacing();
            }

            foreach (CssBox cell in row.Boxes)
            {
                CssSpacingBox spacer = cell as CssSpacingBox;

                if (spacer == null && GetRowSpan(cell) == 1)
                {
                    cell.ActualBottom = maxBottom;
                    // CSS2.1 §17.5.3: Update Size.Height to match the
                    // stretched cell so background painting uses the full
                    // cell height.
                    cell.Size = new SizeF(cell.Size.Width, (float)(maxBottom - cell.Location.Y));
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                }
                else if (spacer != null && spacer.EndRow == currentrow)
                {
                    spacer.ExtendedBox.ActualBottom = maxBottom;
                    spacer.ExtendedBox.Size = new SizeF(spacer.ExtendedBox.Size.Width, (float)(maxBottom - spacer.ExtendedBox.Location.Y));
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, spacer.ExtendedBox);
                }

                // If one cell crosses page borders then don't need to check other cells in the row
                if (_tableBox.PageBreakInside == CssConstants.Avoid)
                {
                    breakPage = cell.BreakPage();
                    if (breakPage)
                    {
                        cury = cell.Location.Y;
                        break;
                    }
                }
            }

            if (breakPage) // go back to move the whole row to the next page
            {
                if (i == 1) // do not leave single row in previous page
                    i = -1; // Start layout from the first row on new page
                else
                    i--;

                maxBottom = 0;
                continue;
            }

            cury = maxBottom + GetVerticalSpacing();

            currentrow++;
        }

        maxRight = Math.Max(maxRight, _tableBox.Location.X + _tableBox.ActualWidth);
        _tableBox.ActualRight = maxRight + GetHorizontalSpacing() + _tableBox.ActualBorderRightWidth;
        _tableBox.ActualBottom = Math.Max(maxBottom, starty) + GetVerticalSpacing() + _tableBox.ActualBorderBottomWidth;

        // CSS2.1 §17.5.2: Update the table box's Size to match the
        // computed layout dimensions so background painting, overflow
        // clipping, and child containing-block queries use the correct
        // table bounds.
        _tableBox.Size = new SizeF(
            (float)(_tableBox.ActualRight - _tableBox.Location.X),
            (float)(_tableBox.ActualBottom - _tableBox.Location.Y));
    }

    private double GetSpannedMinWidth(CssBox row, CssBox cell, int realcolindex, int colspan)
    {
        double w = 0f;

        for (int i = realcolindex; i < row.Boxes.Count || i < realcolindex + colspan - 1; i++)
        {
            if (i < GetColumnMinWidths().Length)
                w += GetColumnMinWidths()[i];
        }

        return w;
    }

    private static int GetCellRealColumnIndex(CssBox row, CssBox cell)
    {
        int i = 0;

        foreach (CssBox b in row.Boxes)
        {
            if (b.Equals(cell))
                break;

            i += GetColSpan(b);
        }

        return i;
    }

    private double GetCellWidth(int column, CssBox b)
    {
        double colspan = Convert.ToSingle(GetColSpan(b));
        double sum = 0f;

        for (int i = column; i < column + colspan; i++)
        {
            if (column >= _columnWidths.Length)
                break;

            if (_columnWidths.Length <= i)
                break;

            sum += _columnWidths[i];
        }

        sum += (colspan - 1) * GetHorizontalSpacing();

        return sum; // -b.ActualBorderLeftWidth - b.ActualBorderRightWidth - b.ActualPaddingRight - b.ActualPaddingLeft;
    }

    private static int GetColSpan(CssBox b)
    {
        string att = b.GetAttribute("colspan", "1");

        if (!int.TryParse(att, out int colspan))
            return 1;

        return colspan;
    }

    private static int GetRowSpan(CssBox b)
    {
        string att = b.GetAttribute("rowspan", "1");

        if (!int.TryParse(att, out int rowspan))
            return 1;

        return rowspan;
    }

    private static void MeasureWords(CssBox box, RGraphics g)
    {
        if (box == null)
            return;

        foreach (var childBox in box.Boxes)
        {
            childBox.MeasureWordsSize(g);
            MeasureWords(childBox, g);
        }
    }

    private bool CanReduceWidth()
    {
        for (int i = 0; i < _columnWidths.Length; i++)
        {
            if (CanReduceWidth(i))
                return true;
        }

        return false;
    }

    private bool CanReduceWidth(int columnIndex)
    {
        if (_columnWidths.Length >= columnIndex || GetColumnMinWidths().Length >= columnIndex)
            return false;

        return _columnWidths[columnIndex] > GetColumnMinWidths()[columnIndex];
    }

    private double GetAvailableTableWidth()
    {
        CssLength tblen = new(_tableBox.Width);

        if (tblen.Number > 0)
        {
            _widthSpecified = true;
            return CssValueParser.ParseLength(_tableBox.Width, _tableBox.ParentBox.AvailableWidth, _tableBox.GetEmHeight());
        }
        else
        {
            return _tableBox.ParentBox.AvailableWidth;
        }
    }

    private double GetMaxTableWidth()
    {
        var tblen = new CssLength(_tableBox.MaxWidth);
        if (tblen.Number > 0)
        {
            _widthSpecified = true;
            return CssValueParser.ParseLength(_tableBox.MaxWidth, _tableBox.ParentBox.AvailableWidth, _tableBox.GetEmHeight());
        }
        else
        {
            return 9999f;
        }
    }

    private void GetColumnsMinMaxWidthByContent(bool onlyNans, out double[] minFullWidths, out double[] maxFullWidths)
    {
        maxFullWidths = new double[_columnWidths.Length];
        minFullWidths = new double[_columnWidths.Length];

        foreach (CssBox row in _allRows)
        {
            for (int i = 0; i < row.Boxes.Count; i++)
            {
                int col = GetCellRealColumnIndex(row, row.Boxes[i]);
                col = _columnWidths.Length > col ? col : _columnWidths.Length - 1;

                if (onlyNans && !double.IsNaN(_columnWidths[col]) || i >= row.Boxes.Count)
                    continue;

                row.Boxes[i].GetMinMaxWidth(out double minWidth, out double maxWidth);

                var colSpan = GetColSpan(row.Boxes[i]);
                minWidth = minWidth / colSpan;
                maxWidth = maxWidth / colSpan;

                for (int j = 0; j < colSpan; j++)
                {
                    var colIndex = col + j;

                    if (colIndex < minFullWidths.Length)
                        minFullWidths[colIndex] = Math.Max(minFullWidths[colIndex], minWidth);

                    if (colIndex < maxFullWidths.Length)
                        maxFullWidths[colIndex] = Math.Max(maxFullWidths[colIndex], maxWidth);
                }
            }
        }
    }

    private double GetAvailableCellWidth() => GetAvailableTableWidth() - GetHorizontalSpacing() * (_columnCount + 1) - _tableBox.ActualBorderLeftWidth - _tableBox.ActualBorderRightWidth;

    private double GetWidthSum()
    {
        double f = 0f;

        foreach (double t in _columnWidths)
        {
            if (double.IsNaN(t))
                throw new Exception("CssTable Algorithm error: There's a NaN in column widths");
            else
                f += t;
        }

        //Take cell-spacing
        f += GetHorizontalSpacing() * (_columnWidths.Length + 1);

        //Take table borders
        f += _tableBox.ActualBorderLeftWidth + _tableBox.ActualBorderRightWidth;

        return f;
    }

    private static int GetSpan(CssBox b)
    {
        double f = CssValueParser.ParseNumber(b.GetAttribute("span"), 1);
        return Math.Max(1, Convert.ToInt32(f));
    }

    private double[] GetColumnMinWidths()
    {
        if (_columnMinWidths != null)
            return _columnMinWidths;

        _columnMinWidths = new double[_columnWidths.Length];

        foreach (CssBox row in _allRows)
        {
            foreach (CssBox cell in row.Boxes)
            {
                int colspan = GetColSpan(cell);
                int col = GetCellRealColumnIndex(row, cell);
                int affectcol = Math.Min(col + colspan, _columnMinWidths.Length) - 1;
                double spannedwidth = GetSpannedMinWidth(row, cell, col, colspan) + (colspan - 1) * GetHorizontalSpacing();

                _columnMinWidths[affectcol] = Math.Max(_columnMinWidths[affectcol], cell.GetMinimumWidth() - spannedwidth);
            }
        }

        return _columnMinWidths;
    }

    private double GetHorizontalSpacing() => _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingHorizontal;
    private static double GetHorizontalSpacing(CssBox box) => box.BorderCollapse == CssConstants.Collapse ? -1f : box.ActualBorderSpacingHorizontal;
    private double GetVerticalSpacing() => _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingVertical;
}