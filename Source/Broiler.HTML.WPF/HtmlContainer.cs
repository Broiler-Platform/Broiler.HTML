using Broiler.HTML.Adapters;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Orchestration;
using Broiler.HTML.Primitives.Adapters.Entities;
using Broiler.HTML.WPF.Adapters;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SizeF = System.Drawing.SizeF;

namespace Broiler.HTML.WPF;

public sealed class HtmlContainer : IDisposable
{
    public HtmlContainer() => HtmlContainerInt = new HtmlContainerInt(WpfAdapter.Instance, HandlerFactory.Instance) { PageSize = new SizeF(99999, 99999) };

    public event EventHandler LoadComplete
    {
        add { HtmlContainerInt.LoadComplete += value; }
        remove { HtmlContainerInt.LoadComplete -= value; }
    }

    public event EventHandler<HtmlLinkClickedEventArgs> LinkClicked
    {
        add { HtmlContainerInt.LinkClicked += value; }
        remove { HtmlContainerInt.LinkClicked -= value; }
    }

    public event EventHandler<HtmlRefreshEventArgs> Refresh
    {
        add { HtmlContainerInt.Refresh += value; }
        remove { HtmlContainerInt.Refresh -= value; }
    }

    public event EventHandler<HtmlScrollEventArgs> ScrollChange
    {
        add { HtmlContainerInt.ScrollChange += value; }
        remove { HtmlContainerInt.ScrollChange -= value; }
    }

    public event EventHandler<HtmlRenderErrorEventArgs> RenderError
    {
        add { HtmlContainerInt.RenderError += value; }
        remove { HtmlContainerInt.RenderError -= value; }
    }

    public event EventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad
    {
        add { HtmlContainerInt.StylesheetLoad += value; }
        remove { HtmlContainerInt.StylesheetLoad -= value; }
    }

    public event EventHandler<HtmlImageLoadEventArgs> ImageLoad
    {
        add { HtmlContainerInt.ImageLoad += value; }
        remove { HtmlContainerInt.ImageLoad -= value; }
    }

    internal HtmlContainerInt HtmlContainerInt { get; }

    public HtmlStyleSet StyleSet => HtmlContainerInt.StyleSet;

    [Obsolete("Use StyleSet.")]
    public CssData CssData => HtmlContainerInt.CssData;

    public bool AvoidAsyncImagesLoading
    {
        get { return HtmlContainerInt.AvoidAsyncImagesLoading; }
        set { HtmlContainerInt.AvoidAsyncImagesLoading = value; }
    }

    public bool AvoidImagesLateLoading
    {
        get { return HtmlContainerInt.AvoidImagesLateLoading; }
        set { HtmlContainerInt.AvoidImagesLateLoading = value; }
    }

    public bool IsSelectionEnabled
    {
        get { return HtmlContainerInt.IsSelectionEnabled; }
        set { HtmlContainerInt.IsSelectionEnabled = value; }
    }

    public bool IsContextMenuEnabled
    {
        get { return HtmlContainerInt.IsContextMenuEnabled; }
        set { HtmlContainerInt.IsContextMenuEnabled = value; }
    }

    public Point ScrollOffset
    {
        get { return Utilities.Utils.Convert(HtmlContainerInt.ScrollOffset); }
        set { HtmlContainerInt.ScrollOffset = Utilities.Utils.Convert(value); }
    }

    public Point Location
    {
        get { return Utilities.Utils.Convert(HtmlContainerInt.Location); }
        set { HtmlContainerInt.Location = Utilities.Utils.Convert(value); }
    }

    public Size MaxSize
    {
        get { return Utilities.Utils.Convert(HtmlContainerInt.MaxSize); }
        set { HtmlContainerInt.MaxSize = Utilities.Utils.Convert(value); }
    }

    public Size ActualSize
    {
        get { return Utilities.Utils.Convert(HtmlContainerInt.ActualSize); }
        internal set { HtmlContainerInt.ActualSize = Utilities.Utils.Convert(value); }
    }

    public string SelectedText => HtmlContainerInt.SelectedText;

    public string SelectedHtml => HtmlContainerInt.SelectedHtml;

    public void ClearSelection() => HtmlContainerInt.ClearSelection();

    /// <summary>
    /// Optional base URL used to resolve relative <c>href</c> values in links.
    /// When set, relative paths (e.g. <c>./page.html</c>, <c>../section/index.html</c>)
    /// are resolved against this URL before navigation.
    /// </summary>
    public string BaseUrl
    {
        get { return HtmlContainerInt.BaseUrl; }
        set { HtmlContainerInt.BaseUrl = value; }
    }

    [Obsolete("Use SetHtmlWithStyleSet.")]
    public void SetHtml(string htmlSource, CssData baseCssData = null, string baseUrl = null) => HtmlContainerInt.SetHtml(htmlSource, baseCssData, baseUrl);
    public void SetHtmlWithStyleSet(string htmlSource, HtmlStyleSet baseStyleSet = null, string baseUrl = null) =>
        HtmlContainerInt.SetHtmlWithStyleSet(htmlSource, baseStyleSet, baseUrl);

    [Obsolete("Use SetDocumentWithStyleSet.")]
    public void SetDocument(Broiler.Dom.DomDocument document, CssData baseCssData = null, string baseUrl = null) =>
        HtmlContainerInt.SetDocument(document, baseCssData, baseUrl);
    public void SetDocumentWithStyleSet(Broiler.Dom.DomDocument document, HtmlStyleSet baseStyleSet = null, string baseUrl = null) =>
        HtmlContainerInt.SetDocumentWithStyleSet(document, baseStyleSet, baseUrl);
    public void Clear() => HtmlContainerInt.Clear();
    public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline) => HtmlContainerInt.GetHtml(styleGen);
    public string GetAttributeAt(Point location, string attribute) => HtmlContainerInt.GetAttributeAt(Utilities.Utils.Convert(location), attribute);

    public List<LinkElementData<Rect>> GetLinks()
    {
        var linkElements = new List<LinkElementData<Rect>>();

        foreach (var link in HtmlContainerInt.GetLinks())
            linkElements.Add(new LinkElementData<Rect>(link.Id, link.Href, Utilities.Utils.Convert(link.Rectangle)));

        return linkElements;
    }

    public string GetLinkAt(Point location) => HtmlContainerInt.GetLinkAt(Utilities.Utils.Convert(location));

    public Rect? GetElementRectangle(string elementId)
    {
        var r = HtmlContainerInt.GetElementRectangle(elementId);
        return r.HasValue ? Utilities.Utils.Convert(r.Value) : null;
    }

    public void PerformLayout()
    {
        using var ig = new GraphicsAdapter();
        HtmlContainerInt.PerformLayout(ig);
    }

    public void PerformPaint(DrawingContext g, Rect clip)
    {
        ArgumentNullException.ThrowIfNull(g);

        using var ig = new GraphicsAdapter(g, Utilities.Utils.Convert(clip));
        HtmlContainerInt.PerformPaint(ig);
    }

    public void HandleMouseDown(Control parent, MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(e);

        HtmlContainerInt.HandleMouseDown(new ControlAdapter(parent), Utilities.Utils.Convert(e.GetPosition(parent)));
    }

    public void HandleMouseUp(Control parent, MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(e);

        var mouseEvent = new RMouseEvent(e.ChangedButton == MouseButton.Left);
        HtmlContainerInt.HandleMouseUp(new ControlAdapter(parent), Utilities.Utils.Convert(e.GetPosition(parent)), mouseEvent);
    }

    public void HandleMouseDoubleClick(Control parent, MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(e);

        HtmlContainerInt.HandleMouseDoubleClick(new ControlAdapter(parent), Utilities.Utils.Convert(e.GetPosition(parent)));
    }

    public void HandleMouseMove(Control parent, Point mousePos)
    {
        ArgumentNullException.ThrowIfNull(parent);

        HtmlContainerInt.HandleMouseMove(new ControlAdapter(parent), Utilities.Utils.Convert(mousePos));
    }

    public void HandleMouseLeave(Control parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        HtmlContainerInt.HandleMouseLeave(new ControlAdapter(parent));
    }

    public void HandleKeyDown(Control parent, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(e);

        HtmlContainerInt.HandleKeyDown(new ControlAdapter(parent), CreateKeyEevent(e));
    }

    public void Dispose() => HtmlContainerInt.Dispose();

    private static RKeyEvent CreateKeyEevent(KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        return new RKeyEvent(control, e.Key == Key.A, e.Key == Key.C);
    }
}
