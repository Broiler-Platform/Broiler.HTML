using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Broiler.HTML.WPF;

public class HtmlControl : Control
{
    protected readonly HtmlContainer _htmlContainer;
    protected CssData _baseCssData;
    protected Point _lastScrollOffset;

    public static readonly DependencyProperty AvoidImagesLateLoadingProperty = DependencyProperty.Register("AvoidImagesLateLoading", typeof(bool), typeof(HtmlControl), new PropertyMetadata(false, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty AvoidAsyncImagesLoadingProperty = DependencyProperty.Register("AvoidAsyncImagesLoading", typeof(bool), typeof(HtmlControl), new PropertyMetadata(false, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty IsSelectionEnabledProperty = DependencyProperty.Register("IsSelectionEnabled", typeof(bool), typeof(HtmlControl), new PropertyMetadata(true, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty IsContextMenuEnabledProperty = DependencyProperty.Register("IsContextMenuEnabled", typeof(bool), typeof(HtmlControl), new PropertyMetadata(true, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty BaseStylesheetProperty = DependencyProperty.Register("BaseStylesheet", typeof(string), typeof(HtmlControl), new PropertyMetadata(null, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty BaseUrlProperty = DependencyProperty.Register("BaseUrl", typeof(string), typeof(HtmlControl), new PropertyMetadata(null, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(HtmlControl), new PropertyMetadata(null, OnDependencyProperty_valueChanged));

    public static readonly RoutedEvent LoadCompleteEvent = EventManager.RegisterRoutedEvent("LoadComplete", RoutingStrategy.Bubble, typeof(RoutedEventHandler<EventArgs>), typeof(HtmlControl));
    public static readonly RoutedEvent LinkClickedEvent = EventManager.RegisterRoutedEvent("LinkClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler<HtmlLinkClickedEventArgs>), typeof(HtmlControl));
    public static readonly RoutedEvent RenderErrorEvent = EventManager.RegisterRoutedEvent("RenderError", RoutingStrategy.Bubble, typeof(RoutedEventHandler<HtmlRenderErrorEventArgs>), typeof(HtmlControl));
    public static readonly RoutedEvent RefreshEvent = EventManager.RegisterRoutedEvent("Refresh", RoutingStrategy.Bubble, typeof(RoutedEventHandler<HtmlRefreshEventArgs>), typeof(HtmlControl));
    public static readonly RoutedEvent StylesheetLoadEvent = EventManager.RegisterRoutedEvent("StylesheetLoad", RoutingStrategy.Bubble, typeof(RoutedEventHandler<HtmlStylesheetLoadEventArgs>), typeof(HtmlControl));
    public static readonly RoutedEvent ImageLoadEvent = EventManager.RegisterRoutedEvent("ImageLoad", RoutingStrategy.Bubble, typeof(RoutedEventHandler<HtmlImageLoadEventArgs>), typeof(HtmlControl));

    protected HtmlControl()
    {
        SnapsToDevicePixels = false;

        _htmlContainer = new HtmlContainer();
        _htmlContainer.LoadComplete += OnLoadComplete;
        _htmlContainer.LinkClicked += OnLinkClicked;
        _htmlContainer.RenderError += OnRenderError;
        _htmlContainer.Refresh += OnRefresh;
        _htmlContainer.StylesheetLoad += OnStylesheetLoad;
        _htmlContainer.ImageLoad += OnImageLoad;
    }

    /// <summary>
    /// Binds a canonical DOM document directly to this control and invalidates
    /// layout. Existing <see cref="Text"/> behavior remains available as the
    /// serialized compatibility path.
    /// </summary>
    public void SetDocument(Broiler.Dom.DomDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _htmlContainer.SetDocument(document, _baseCssData, BaseUrl);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public event RoutedEventHandler LoadComplete
    {
        add { AddHandler(LoadCompleteEvent, value); }
        remove { RemoveHandler(LoadCompleteEvent, value); }
    }

    public event RoutedEventHandler<HtmlLinkClickedEventArgs> LinkClicked
    {
        add { AddHandler(LinkClickedEvent, value); }
        remove { RemoveHandler(LinkClickedEvent, value); }
    }

    public event RoutedEventHandler<HtmlRenderErrorEventArgs> RenderError
    {
        add { AddHandler(RenderErrorEvent, value); }
        remove { RemoveHandler(RenderErrorEvent, value); }
    }

    public event RoutedEventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad
    {
        add { AddHandler(StylesheetLoadEvent, value); }
        remove { RemoveHandler(StylesheetLoadEvent, value); }
    }

    public event RoutedEventHandler<HtmlImageLoadEventArgs> ImageLoad
    {
        add { AddHandler(ImageLoadEvent, value); }
        remove { RemoveHandler(ImageLoadEvent, value); }
    }

    [Category("Behavior")]
    [Description("If image loading only when visible should be avoided")]
    public bool AvoidImagesLateLoading
    {
        get { return (bool)GetValue(AvoidImagesLateLoadingProperty); }
        set { SetValue(AvoidImagesLateLoadingProperty, value); }
    }

    [Category("Behavior")]
    [Description("If asynchronous image loading should be avoided (forces synchronous loading)")]
    public bool AvoidAsyncImagesLoading
    {
        get { return (bool)GetValue(AvoidAsyncImagesLoadingProperty); }
        set { SetValue(AvoidAsyncImagesLoadingProperty, value); }
    }

    [Category("Behavior")]
    [Description("Is content selection is enabled for the rendered html.")]
    public bool IsSelectionEnabled
    {
        get { return (bool)GetValue(IsSelectionEnabledProperty); }
        set { SetValue(IsSelectionEnabledProperty, value); }
    }

    [Category("Behavior")]
    [Description("Is the build-in context menu enabled and will be shown on mouse right click.")]
    public bool IsContextMenuEnabled
    {
        get { return (bool)GetValue(IsContextMenuEnabledProperty); }
        set { SetValue(IsContextMenuEnabledProperty, value); }
    }

    [Category("Appearance")]
    [Description("Set base stylesheet to be used by html rendered in the control.")]
    public string BaseStylesheet
    {
        get { return (string)GetValue(BaseStylesheetProperty); }
        set { SetValue(BaseStylesheetProperty, value); }
    }

    [Category("Behavior")]
    [Description("Optional base URL used to resolve relative href values in links.")]
    public string BaseUrl
    {
        get { return (string)GetValue(BaseUrlProperty); }
        set { SetValue(BaseUrlProperty, value); }
    }

    [Description("Sets the html of this control.")]
    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    [Browsable(false)]
    public virtual string SelectedText => _htmlContainer.SelectedText;

    [Browsable(false)]
    public virtual string SelectedHtml => _htmlContainer.SelectedHtml;

    public virtual string GetHtml() => _htmlContainer?.GetHtml();

    public virtual Rect? GetElementRectangle(string elementId) => _htmlContainer != null ? _htmlContainer.GetElementRectangle(elementId) : null;

    public void ClearSelection() => _htmlContainer?.ClearSelection();

    protected override void OnRender(DrawingContext context)
    {
        if (Background.Opacity > 0)
            context.DrawRectangle(Background, null, new Rect(RenderSize));

        if (BorderThickness != new Thickness(0))
        {
            var brush = BorderBrush ?? SystemColors.ControlDarkBrush;

            if (BorderThickness.Top > 0)
                context.DrawRectangle(brush, null, new Rect(0, 0, RenderSize.Width, BorderThickness.Top));

            if (BorderThickness.Bottom > 0)
                context.DrawRectangle(brush, null, new Rect(0, RenderSize.Height - BorderThickness.Bottom, RenderSize.Width, BorderThickness.Bottom));

            if (BorderThickness.Left > 0)
                context.DrawRectangle(brush, null, new Rect(0, 0, BorderThickness.Left, RenderSize.Height));

            if (BorderThickness.Right > 0)
                context.DrawRectangle(brush, null, new Rect(RenderSize.Width - BorderThickness.Right, 0, BorderThickness.Right, RenderSize.Height));
        }

        var htmlWidth = HtmlWidth(RenderSize);
        var htmlHeight = HtmlHeight(RenderSize);

        if (_htmlContainer == null || htmlWidth <= 0 || htmlHeight <= 0)
            return;

        var windows = Window.GetWindow(this);
        if (windows != null)
        {
            // adjust render location to round point so we won't get anti-alias smugness
            var wPoint = TranslatePoint(new Point(0, 0), windows);
            wPoint.Offset(-(int)wPoint.X, -(int)wPoint.Y);
            var xTrans = wPoint.X < .5 ? -wPoint.X : 1 - wPoint.X;
            var yTrans = wPoint.Y < .5 ? -wPoint.Y : 1 - wPoint.Y;
            context.PushTransform(new TranslateTransform(xTrans, yTrans));
        }

        context.PushClip(new RectangleGeometry(new Rect(Padding.Left + BorderThickness.Left, Padding.Top + BorderThickness.Top, htmlWidth, htmlHeight)));
        _htmlContainer.Location = new Point(Padding.Left + BorderThickness.Left, Padding.Top + BorderThickness.Top);
        _htmlContainer.PerformPaint(context, new Rect(Padding.Left + BorderThickness.Left, Padding.Top + BorderThickness.Top, htmlWidth, htmlHeight));
        context.Pop(); // pop clip

        if (windows != null)
        {
            context.Pop(); // pop transform
        }

        if (!_lastScrollOffset.Equals(_htmlContainer.ScrollOffset))
        {
            _lastScrollOffset = _htmlContainer.ScrollOffset;
            InvokeMouseMove();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _htmlContainer?.HandleMouseMove(this, e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _htmlContainer?.HandleMouseLeave(this);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        _htmlContainer?.HandleMouseDown(this, e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _htmlContainer?.HandleMouseUp(this, e);
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        _htmlContainer?.HandleMouseDoubleClick(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _htmlContainer?.HandleKeyDown(this, e);
    }

    protected virtual void OnLoadComplete(EventArgs e)
    {
        RoutedEventArgs newEventArgs = new RoutedEventArgs<EventArgs>(LoadCompleteEvent, this, e);
        RaiseEvent(newEventArgs);
    }

    protected virtual void OnLinkClicked(HtmlLinkClickedEventArgs e)
    {
        RoutedEventArgs newEventArgs = new RoutedEventArgs<HtmlLinkClickedEventArgs>(LinkClickedEvent, this, e);
        RaiseEvent(newEventArgs);

        if (newEventArgs.Handled)
            e.Handled = true;
    }

    protected virtual void OnRenderError(HtmlRenderErrorEventArgs e)
    {
        RoutedEventArgs newEventArgs = new RoutedEventArgs<HtmlRenderErrorEventArgs>(RenderErrorEvent, this, e);
        RaiseEvent(newEventArgs);
    }

    protected virtual void OnStylesheetLoad(HtmlStylesheetLoadEventArgs e)
    {
        RoutedEventArgs newEventArgs = new RoutedEventArgs<HtmlStylesheetLoadEventArgs>(StylesheetLoadEvent, this, e);
        RaiseEvent(newEventArgs);
    }

    protected virtual void OnImageLoad(HtmlImageLoadEventArgs e)
    {
        RoutedEventArgs newEventArgs = new RoutedEventArgs<HtmlImageLoadEventArgs>(ImageLoadEvent, this, e);
        RaiseEvent(newEventArgs);
    }

    protected virtual void OnRefresh(HtmlRefreshEventArgs e)
    {
        if (e.Layout)
            InvalidateMeasure();

        InvalidateVisual();
    }

    protected virtual double HtmlWidth(Size size) => size.Width - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right;

    protected virtual double HtmlHeight(Size size) => size.Height - Padding.Top - Padding.Bottom - BorderThickness.Top - BorderThickness.Bottom;

    protected virtual void InvokeMouseMove() => _htmlContainer.HandleMouseMove(this, Mouse.GetPosition(this));

    private static void OnDependencyProperty_valueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not HtmlControl control)
            return;

        var htmlContainer = control._htmlContainer;
        if (e.Property == AvoidImagesLateLoadingProperty)
        {
            htmlContainer.AvoidImagesLateLoading = (bool)e.NewValue;
        }
        else if (e.Property == AvoidAsyncImagesLoadingProperty)
        {
            htmlContainer.AvoidAsyncImagesLoading = (bool)e.NewValue;
        }
        else if (e.Property == IsSelectionEnabledProperty)
        {
            htmlContainer.IsSelectionEnabled = (bool)e.NewValue;
        }
        else if (e.Property == IsContextMenuEnabledProperty)
        {
            htmlContainer.IsContextMenuEnabled = (bool)e.NewValue;
        }
        else if (e.Property == BaseStylesheetProperty)
        {
            var baseCssData = HtmlRender.ParseStyleSheet((string)e.NewValue);
            control._baseCssData = baseCssData;
            htmlContainer.SetHtml(control.Text, baseCssData);
        }
        else if (e.Property == BaseUrlProperty)
        {
            htmlContainer.BaseUrl = (string)e.NewValue;
        }
        else if (e.Property == TextProperty)
        {
            htmlContainer.ScrollOffset = new Point(0, 0);
            htmlContainer.SetHtml((string)e.NewValue, control._baseCssData, htmlContainer.BaseUrl);
            control.InvalidateMeasure();
            control.InvalidateVisual();
            control.InvokeMouseMove();
        }
    }

    private void OnLoadComplete(object sender, EventArgs e)
    {
        if (CheckAccess())
            OnLoadComplete(e);
        else
            Dispatcher.Invoke(new Action<HtmlLinkClickedEventArgs>(OnLinkClicked), e);
    }

    private void OnLinkClicked(object sender, HtmlLinkClickedEventArgs e)
    {
        if (CheckAccess())
            OnLinkClicked(e);
        else
            Dispatcher.Invoke(new Action<HtmlLinkClickedEventArgs>(OnLinkClicked), e);
    }

    private void OnRenderError(object sender, HtmlRenderErrorEventArgs e)
    {
        if (CheckAccess())
            OnRenderError(e);
        else
            Dispatcher.Invoke(new Action<HtmlRenderErrorEventArgs>(OnRenderError), e);
    }

    private void OnStylesheetLoad(object sender, HtmlStylesheetLoadEventArgs e)
    {
        if (CheckAccess())
            OnStylesheetLoad(e);
        else
            Dispatcher.Invoke(new Action<HtmlStylesheetLoadEventArgs>(OnStylesheetLoad), e);
    }

    private void OnImageLoad(object sender, HtmlImageLoadEventArgs e)
    {
        if (CheckAccess())
            OnImageLoad(e);
        else
            Dispatcher.Invoke(new Action<HtmlImageLoadEventArgs>(OnImageLoad), e);
    }

    private void OnRefresh(object sender, HtmlRefreshEventArgs e)
    {
        if (CheckAccess())
            OnRefresh(e);
        else
            Dispatcher.Invoke(new Action<HtmlRefreshEventArgs>(OnRefresh), e);
    }
}
