using System;
using System.Drawing;
using Broiler.HTML.Adapters;
using Broiler.HTML.Dom.Utils;
using Broiler.HTML.Dom;
using Broiler.Layout;
using Broiler.HTML.Orchestration;
using Broiler.HTML.Core.Entities;

namespace Broiler.HTML.Core;

internal sealed class SelectionHandler : Core.ISelectionHandler, ISelectionHandler
{
    private readonly CssBox _root;
    private readonly HtmlContainerInt _htmlContainer;
    private readonly RAdapter _adapter;
    private readonly ContextMenuHandler _contextMenuHandler;
    private PointF _selectionStartPoint;
    private CssRect _selectionStart;
    private CssRect _selectionEnd;
    private int _selectionStartIndex = -1;
    private int _selectionEndIndex = -1;
    private double _selectionStartOffset = -1;
    private double _selectionEndOffset = -1;
    private bool _backwardSelection;
    private bool _inSelection;
    private bool _isDoubleClickSelect;
    private bool _mouseDownInControl;
    private bool _mouseDownOnSelectedWord;
    private bool _cursorChanged;
    private DateTime _lastMouseDown;
    private object _dragDropData;

    public SelectionHandler(CssBox root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _root = root;
        _htmlContainer = (HtmlContainerInt)root.ContainerInt;
        _adapter = (RAdapter)_htmlContainer.Adapter;
        _contextMenuHandler = new ContextMenuHandler(this, _htmlContainer);
    }

    #region ISelectionHandler explicit implementation

    void Core.ISelectionHandler.HandleMouseDown(object parent, PointF loc, bool isMouseInContainer)
        => HandleMouseDown((RControl)parent, loc, isMouseInContainer);

    bool Core.ISelectionHandler.HandleMouseUp(object parent, bool leftMouseButton)
        => HandleMouseUp((RControl)parent, leftMouseButton);

    void Core.ISelectionHandler.HandleMouseMove(object parent, PointF loc)
        => HandleMouseMove((RControl)parent, loc);

    void Core.ISelectionHandler.HandleMouseLeave(object parent)
        => HandleMouseLeave((RControl)parent);

    void Core.ISelectionHandler.SelectWord(object parent, PointF loc)
        => SelectWord((RControl)parent, loc);

    void Core.ISelectionHandler.SelectAll(object parent)
        => SelectAll((RControl)parent);

    #endregion

    public void SelectAll(RControl control)
    {
        if (!_htmlContainer.IsSelectionEnabled)
            return;

        ClearSelection();
        SelectAllWords(_root);
        control.Invalidate();
    }

    public void SelectWord(RControl control, PointF loc)
    {
        if (!_htmlContainer.IsSelectionEnabled)
            return;

        var word = DomUtils.GetCssBoxWord(_root, loc);
        if (word != null)
        {
            word.Selection = this;
            _selectionStartPoint = loc;
            _selectionStart = _selectionEnd = word;
            control.Invalidate();
        }
    }

    public void HandleMouseDown(RControl parent, PointF loc, bool isMouseInContainer)
    {
        bool clear = !isMouseInContainer;

        if (isMouseInContainer)
        {
            _mouseDownInControl = true;
            _isDoubleClickSelect = (DateTime.Now - _lastMouseDown).TotalMilliseconds < 400;
            _lastMouseDown = DateTime.Now;
            _mouseDownOnSelectedWord = false;

            if (_htmlContainer.IsSelectionEnabled && parent.LeftMouseButton)
            {
                var word = DomUtils.GetCssBoxWord(_root, loc);
                if (word != null && word.Selected)
                {
                    _mouseDownOnSelectedWord = true;
                }
                else
                {
                    clear = true;
                }
            }
            else if (parent.RightMouseButton)
            {
                var rect = DomUtils.GetCssBoxWord(_root, loc);
                var link = DomUtils.GetLinkBox(_root, loc);

                if (_htmlContainer.IsContextMenuEnabled)
                    _contextMenuHandler.ShowContextMenu(parent, rect, link);

                clear = rect == null || !rect.Selected;
            }
        }

        if (clear)
        {
            ClearSelection();
            parent.Invalidate();
        }
    }

    public bool HandleMouseUp(RControl parent, bool leftMouseButton)
    {
        bool ignore = false;

        _mouseDownInControl = false;

        if (_htmlContainer.IsSelectionEnabled)
        {
            ignore = _inSelection;

            if (!_inSelection && leftMouseButton && _mouseDownOnSelectedWord)
            {
                ClearSelection();
                parent.Invalidate();
            }

            _mouseDownOnSelectedWord = false;
            _inSelection = false;
        }

        ignore = ignore || (DateTime.Now - _lastMouseDown > TimeSpan.FromSeconds(1));
        return ignore;
    }

    public void HandleMouseMove(RControl parent, PointF loc)
    {
        if (_htmlContainer.IsSelectionEnabled && _mouseDownInControl && parent.LeftMouseButton)
        {
            if (_mouseDownOnSelectedWord)
            {
                if ((DateTime.Now - _lastMouseDown).TotalMilliseconds > 200)
                    StartDragDrop(parent);
            }
            else
            {
                HandleSelection(parent, loc, !_isDoubleClickSelect);
                _inSelection = _selectionStart != null && _selectionEnd != null && (_selectionStart != _selectionEnd || _selectionStartIndex != _selectionEndIndex);
            }
        }
        else
        {
            var link = DomUtils.GetLinkBox(_root, loc);
            if (link != null)
            {
                _cursorChanged = true;
                parent.SetCursorHand();
            }
            else if (_htmlContainer.IsSelectionEnabled)
            {
                var word = DomUtils.GetCssBoxWord(_root, loc);
                _cursorChanged = word != null && !word.IsImage && !(word.Selected && (word.SelectedStartIndex < 0 || word.Left + word.SelectedStartOffset <= loc.X) && (word.SelectedEndOffset < 0 || word.Left + word.SelectedEndOffset >= loc.X));

                if (_cursorChanged)
                    parent.SetCursorIBeam();
                else
                    parent.SetCursorDefault();
            }
            else if (_cursorChanged)
            {
                parent.SetCursorDefault();
            }
        }
    }

    public void HandleMouseLeave(RControl parent)
    {
        if (!_cursorChanged)
            return;

        _cursorChanged = false;
        parent.SetCursorDefault();
    }

    public void CopySelectedHtml()
    {
        if (!_htmlContainer.IsSelectionEnabled)
            return;

        var html = DomUtils.GenerateHtml(_root, HtmlGenerationStyle.Inline, true);
        var plainText = DomUtils.GetSelectedPlainText(_root);

        if (!string.IsNullOrEmpty(plainText))
            _adapter.SetToClipboard(html, plainText);
    }

    public string GetSelectedText() => _htmlContainer.IsSelectionEnabled ? DomUtils.GetSelectedPlainText(_root) : null;
    public string GetSelectedHtml() => _htmlContainer.IsSelectionEnabled ? DomUtils.GenerateHtml(_root, HtmlGenerationStyle.Inline, true) : null;
    public int GetSelectingStartIndex(CssRect word) => word == (_backwardSelection ? _selectionEnd : _selectionStart) ? (_backwardSelection ? _selectionEndIndex : _selectionStartIndex) : -1;
    public int GetSelectedEndIndexOffset(CssRect word) => word == (_backwardSelection ? _selectionStart : _selectionEnd) ? (_backwardSelection ? _selectionStartIndex : _selectionEndIndex) : -1;
    public double GetSelectedStartOffset(CssRect word) => word == (_backwardSelection ? _selectionEnd : _selectionStart) ? (_backwardSelection ? _selectionEndOffset : _selectionStartOffset) : -1;
    public double GetSelectedEndOffset(CssRect word) => word == (_backwardSelection ? _selectionStart : _selectionEnd) ? (_backwardSelection ? _selectionStartOffset : _selectionEndOffset) : -1;

    public void ClearSelection()
    {
        _dragDropData = null;

        ClearSelection(_root);

        _selectionStartOffset = -1;
        _selectionStartIndex = -1;
        _selectionEndOffset = -1;
        _selectionEndIndex = -1;

        _selectionStartPoint = PointF.Empty;
        _selectionStart = null;
        _selectionEnd = null;
    }

    public void Dispose() => _contextMenuHandler.Dispose();

    private void HandleSelection(RControl control, PointF loc, bool allowPartialSelect)
    {
        var lineBox = DomUtils.GetCssLineBox(_root, loc);
        if (lineBox == null)
            return;

        var word = DomUtils.GetCssBoxWord(lineBox, loc);

        if (word == null && lineBox.Words.Count > 0)
        {
            if (loc.Y > lineBox.LineBottom)
            {
                word = lineBox.Words[lineBox.Words.Count - 1];
            }
            else if (loc.X < lineBox.Words[0].Left)
            {
                word = lineBox.Words[0];
            }
            else if (loc.X > lineBox.Words[lineBox.Words.Count - 1].Right)
            {
                word = lineBox.Words[lineBox.Words.Count - 1];
            }
        }

        if (word == null)
            return;

        if (_selectionStart == null)
        {
            _selectionStartPoint = loc;
            _selectionStart = word;

            if (allowPartialSelect)
                CalculateWordCharIndexAndOffset(control, word, loc, true);
        }

        _selectionEnd = word;

        if (allowPartialSelect)
            CalculateWordCharIndexAndOffset(control, word, loc, false);

        ClearSelection(_root);
        
        if (CheckNonEmptySelection(loc, allowPartialSelect))
        {
            CheckSelectionDirection();
            SelectWordsInRange(_root, _backwardSelection ? _selectionEnd : _selectionStart, _backwardSelection ? _selectionStart : _selectionEnd);
        }
        else
        {
            _selectionEnd = null;
        }

        _cursorChanged = true;
        control.SetCursorIBeam();
        control.Invalidate();
    }

    private static void ClearSelection(CssBox box)
    {
        foreach (var word in box.Words)
            word.Selection = null;

        foreach (var childBox in box.Boxes)
            ClearSelection(childBox);
    }

    private void StartDragDrop(RControl control)
    {
        if (_dragDropData == null)
        {
            var html = DomUtils.GenerateHtml(_root, HtmlGenerationStyle.Inline, true);
            var plainText = DomUtils.GetSelectedPlainText(_root);

            _dragDropData = control.Adapter.GetClipboardDataObject(html, plainText);
        }

        control.DoDragDropCopy(_dragDropData);
    }

    public void SelectAllWords(CssBox box)
    {
        foreach (var word in box.Words)
            word.Selection = this;

        foreach (var childBox in box.Boxes)
            SelectAllWords(childBox);
    }

    private bool CheckNonEmptySelection(PointF loc, bool allowPartialSelect)
    {
        if (!allowPartialSelect)
            return true;

        if (Math.Abs(_selectionStartPoint.X - loc.X) <= 1 && Math.Abs(_selectionStartPoint.Y - loc.Y) < 5)
            return false;

        return _selectionStart != _selectionEnd || _selectionStartIndex != _selectionEndIndex;
    }

    private void SelectWordsInRange(CssBox root, CssRect selectionStart, CssRect selectionEnd)
    {
        bool inSelection = false;
        SelectWordsInRange(root, selectionStart, selectionEnd, ref inSelection);
    }

    private bool SelectWordsInRange(CssBox box, CssRect selectionStart, CssRect selectionEnd, ref bool inSelection)
    {
        foreach (var boxWord in box.Words)
        {
            if (!inSelection && boxWord == selectionStart)
                inSelection = true;

            if (!inSelection)
                continue;

            boxWord.Selection = this;

            if (selectionStart == selectionEnd || boxWord == selectionEnd)
                return true;
        }

        foreach (var childBox in box.Boxes)
        {
            if (SelectWordsInRange(childBox, selectionStart, selectionEnd, ref inSelection))
                return true;
        }

        return false;
    }

    private void CalculateWordCharIndexAndOffset(RControl control, CssRect word, PointF loc, bool selectionStart)
    {
        CalculateWordCharIndexAndOffset(control, word, loc, selectionStart, out int selectionIndex, out double selectionOffset);

        if (selectionStart)
        {
            _selectionStartIndex = selectionIndex;
            _selectionStartOffset = selectionOffset;
        }
        else
        {
            _selectionEndIndex = selectionIndex;
            _selectionEndOffset = selectionOffset;
        }
    }

    private static void CalculateWordCharIndexAndOffset(RControl control, CssRect word, PointF loc, bool inclusive, out int selectionIndex, out double selectionOffset)
    {
        selectionIndex = 0;
        selectionOffset = 0f;

        var offset = loc.X - word.Left;
        if (word.Text == null)
        {
            selectionIndex = -1;
            selectionOffset = -1;
        }
        else if (offset > word.Width - word.OwnerBox.ActualWordSpacing || loc.Y > DomUtils.GetCssLineBoxByWord(word).LineBottom)
        {
            selectionIndex = word.Text.Length;
            selectionOffset = word.Width;
        }
        else if (offset > 0)
        {
            var maxWidth = offset + (inclusive ? 0 : 1.5f * word.LeftGlyphPadding);
            control.MeasureString(word.Text, (RFont)word.OwnerBox.ActualFont, maxWidth, out int charFit, out double charFitWidth);

            selectionIndex = charFit;
            selectionOffset = charFitWidth;
        }
    }

    private void CheckSelectionDirection()
    {
        if (_selectionStart == _selectionEnd)
        {
            _backwardSelection = _selectionStartIndex > _selectionEndIndex;
        }
        else if (DomUtils.GetCssLineBoxByWord(_selectionStart) == DomUtils.GetCssLineBoxByWord(_selectionEnd))
        {
            _backwardSelection = _selectionStart.Left > _selectionEnd.Left;
        }
        else
        {
            _backwardSelection = _selectionStart.Top >= _selectionEnd.Bottom;
        }
    }
}