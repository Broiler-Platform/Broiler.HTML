using System.Windows;
using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Broiler.HTML.Core.Core.Entities;

namespace Broiler.HTML.WPF;

public class HtmlPanel : HtmlControl
{
    protected ScrollBar _verticalScrollBar;
    protected ScrollBar _horizontalScrollBar;

    static HtmlPanel()
    {
        BackgroundProperty.OverrideMetadata(typeof(HtmlPanel), new FrameworkPropertyMetadata(SystemColors.WindowBrush));
        TextProperty.OverrideMetadata(typeof(HtmlPanel), new PropertyMetadata(null, OnTextProperty_change));
    }

    public HtmlPanel()
    {
        _verticalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 18
        };
        _verticalScrollBar.Scroll += OnScrollBarScroll;
        AddVisualChild(_verticalScrollBar);
        AddLogicalChild(_verticalScrollBar);

        _horizontalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Height = 18
        };
        _horizontalScrollBar.Scroll += OnScrollBarScroll;
        AddVisualChild(_horizontalScrollBar);
        AddLogicalChild(_horizontalScrollBar);

        _htmlContainer.ScrollChange += OnScrollChange;
    }

    public virtual void ScrollToElement(string elementId)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        if (_htmlContainer == null)
            return;

        var rect = _htmlContainer.GetElementRectangle(elementId);
        if (!rect.HasValue)
            return;

        ScrollToPoint(rect.Value.Location.X, rect.Value.Location.Y);
        _htmlContainer.HandleMouseMove(this, Mouse.GetPosition(this));
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index)
    {
        if (index == 0)
            return _verticalScrollBar;
        else if (index == 1)
            return _horizontalScrollBar;

        return null;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Size size = PerformHtmlLayout(constraint);

        // to handle if scrollbar is appearing or disappearing
        bool relayout = false;
        var htmlWidth = HtmlWidth(constraint);
        var htmlHeight = HtmlHeight(constraint);

        if ((_verticalScrollBar.Visibility == Visibility.Hidden && size.Height > htmlHeight) ||
            (_verticalScrollBar.Visibility == Visibility.Visible && size.Height <= htmlHeight))
        {
            _verticalScrollBar.Visibility = _verticalScrollBar.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            relayout = true;
        }

        if ((_horizontalScrollBar.Visibility == Visibility.Hidden && size.Width > htmlWidth) ||
            (_horizontalScrollBar.Visibility == Visibility.Visible && size.Width <= htmlWidth))
        {
            _horizontalScrollBar.Visibility = _horizontalScrollBar.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            relayout = true;
        }

        if (relayout)
            PerformHtmlLayout(constraint);

        if (double.IsPositiveInfinity(constraint.Width) || double.IsPositiveInfinity(constraint.Height))
            constraint = size;

        return constraint;
    }

    /// <summary>
    /// After measurement arrange the scrollbars of the panel.
    /// </summary>
    protected override Size ArrangeOverride(Size bounds)
    {
        var scrollHeight = HtmlHeight(bounds) + Padding.Top + Padding.Bottom;
        scrollHeight = scrollHeight > 1 ? scrollHeight : 1;
        var scrollWidth = HtmlWidth(bounds) + Padding.Left + Padding.Right;
        scrollWidth = scrollWidth > 1 ? scrollWidth : 1;
        _verticalScrollBar.Arrange(new Rect(Math.Max(bounds.Width - _verticalScrollBar.Width - BorderThickness.Right, 0), BorderThickness.Top, _verticalScrollBar.Width, scrollHeight));
        _horizontalScrollBar.Arrange(new Rect(BorderThickness.Left, Math.Max(bounds.Height - _horizontalScrollBar.Height - BorderThickness.Bottom, 0), scrollWidth, _horizontalScrollBar.Height));

        if (_htmlContainer == null)
            return bounds;

        if (_verticalScrollBar.Visibility == Visibility.Visible)
        {
            _verticalScrollBar.ViewportSize = HtmlHeight(bounds);
            _verticalScrollBar.SmallChange = 25;
            _verticalScrollBar.LargeChange = _verticalScrollBar.ViewportSize * .9;
            _verticalScrollBar.Maximum = _htmlContainer.ActualSize.Height - _verticalScrollBar.ViewportSize;
        }

        if (_horizontalScrollBar.Visibility == Visibility.Visible)
        {
            _horizontalScrollBar.ViewportSize = HtmlWidth(bounds);
            _horizontalScrollBar.SmallChange = 25;
            _horizontalScrollBar.LargeChange = _horizontalScrollBar.ViewportSize * .9;
            _horizontalScrollBar.Maximum = _htmlContainer.ActualSize.Width - _horizontalScrollBar.ViewportSize;
        }

        // update the scroll offset because the scroll values may have changed
        UpdateScrollOffsets();

        return bounds;
    }

    protected Size PerformHtmlLayout(Size constraint)
    {
        if (_htmlContainer == null)
            return Size.Empty;

        _htmlContainer.MaxSize = new Size(HtmlWidth(constraint), 0);
        _htmlContainer.PerformLayout();
        return _htmlContainer.ActualSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        // render rectangle in right bottom corner where both scrolls meet
        if (_horizontalScrollBar.Visibility == Visibility.Visible && _verticalScrollBar.Visibility == Visibility.Visible)
            context.DrawRectangle(SystemColors.ControlBrush, null, new Rect(BorderThickness.Left + HtmlWidth(RenderSize), BorderThickness.Top + HtmlHeight(RenderSize), _verticalScrollBar.Width, _horizontalScrollBar.Height));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        
        if (_verticalScrollBar.Visibility != Visibility.Visible)
            return;

        _verticalScrollBar.Value -= e.Delta;
        UpdateScrollOffsets();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_verticalScrollBar.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Up)
            {
                _verticalScrollBar.Value -= _verticalScrollBar.SmallChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                _verticalScrollBar.Value += _verticalScrollBar.SmallChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.PageUp)
            {
                _verticalScrollBar.Value -= _verticalScrollBar.LargeChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown)
            {
                _verticalScrollBar.Value += _verticalScrollBar.LargeChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                _verticalScrollBar.Value = 0;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                _verticalScrollBar.Value = _verticalScrollBar.Maximum;
                UpdateScrollOffsets();
                e.Handled = true;
            }
        }

        if (_horizontalScrollBar.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Left)
            {
                _horizontalScrollBar.Value -= _horizontalScrollBar.SmallChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                _horizontalScrollBar.Value += _horizontalScrollBar.SmallChange;
                UpdateScrollOffsets();
                e.Handled = true;
            }
        }
    }

    protected override double HtmlWidth(Size size)
    {
        var width = base.HtmlWidth(size) - (_verticalScrollBar.Visibility == Visibility.Visible ? _verticalScrollBar.Width : 0);
        return width > 1 ? width : 1;
    }

    protected override double HtmlHeight(Size size)
    {
        var height = base.HtmlHeight(size) - (_horizontalScrollBar.Visibility == Visibility.Visible ? _horizontalScrollBar.Height : 0);
        return height > 1 ? height : 1;
    }

    private void OnScrollChange(object sender, HtmlScrollEventArgs e) => ScrollToPoint(e.X, e.Y);

    private void ScrollToPoint(double x, double y)
    {
        _horizontalScrollBar.Value = x;
        _verticalScrollBar.Value = y;
        UpdateScrollOffsets();
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e) => UpdateScrollOffsets();

    private void UpdateScrollOffsets()
    {
        var newScrollOffset = new Point(-_horizontalScrollBar.Value, -_verticalScrollBar.Value);
        if (newScrollOffset.Equals(_htmlContainer.ScrollOffset))
            return;

        _htmlContainer.ScrollOffset = newScrollOffset;
        InvalidateVisual();
    }

    private static void OnTextProperty_change(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HtmlPanel panel)
            panel._horizontalScrollBar.Value = panel._verticalScrollBar.Value = 0;
    }
}