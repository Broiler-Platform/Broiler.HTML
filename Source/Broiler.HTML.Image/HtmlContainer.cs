using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters;
using Broiler.HTML.Image.Adapters;
using Broiler.HTML.Primitives.Adapters.Entities;
using Broiler.HTML.Orchestration;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.IR;
using Broiler.HTML.Core;

namespace Broiler.HTML.Image;

public sealed class HtmlContainer : IDisposable
{
    public HtmlContainer()
    {
        HtmlContainerInt = new HtmlContainerInt(CompatProvider.ImageAdapter, HandlerFactory.Instance);
        HtmlContainerInt.SetMargins(0);
        HtmlContainerInt.PageSize = new SizeF(99999, 99999);
    }

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

    /// <summary>
    /// The most recent <see cref="Fragment"/> tree built after layout.
    /// Available after <see cref="PerformLayout"/> has been called.
    /// </summary>
    public Fragment? LatestFragmentTree => HtmlContainerInt.LatestFragmentTree;

    public bool AvoidAsyncImagesLoading
    {
        get => HtmlContainerInt.AvoidAsyncImagesLoading;
        set => HtmlContainerInt.AvoidAsyncImagesLoading = value;
    }

    public bool AvoidImagesLateLoading
    {
        get => HtmlContainerInt.AvoidImagesLateLoading;
        set => HtmlContainerInt.AvoidImagesLateLoading = value;
    }

    public bool IsSelectionEnabled
    {
        get => HtmlContainerInt.IsSelectionEnabled;
        set => HtmlContainerInt.IsSelectionEnabled = value;
    }

    public bool IsContextMenuEnabled
    {
        get => HtmlContainerInt.IsContextMenuEnabled;
        set => HtmlContainerInt.IsContextMenuEnabled = value;
    }

    public PointF ScrollOffset
    {
        get => HtmlContainerInt.ScrollOffset;
        set => HtmlContainerInt.ScrollOffset = value;
    }

    public SizeF MaxSize
    {
        get => HtmlContainerInt.MaxSize;
        set => HtmlContainerInt.MaxSize = value;
    }

    public SizeF ActualSize
    {
        get => HtmlContainerInt.ActualSize;
        internal set => HtmlContainerInt.ActualSize = value;
    }

    public string SelectedText => HtmlContainerInt.SelectedText;

    public string SelectedHtml => HtmlContainerInt.SelectedHtml;

    public void ClearSelection() => HtmlContainerInt.ClearSelection();

    public PointF Location
    {
        get => HtmlContainerInt.Location;
        set => HtmlContainerInt.Location = value;
    }

    /// <summary>
    /// Optional base URL used to resolve relative <c>href</c> values in links.
    /// When set, relative paths (e.g. <c>./page.html</c>, <c>../section/index.html</c>)
    /// are resolved against this URL before navigation.
    /// </summary>
    public string BaseUrl
    {
        get => HtmlContainerInt.BaseUrl;
        set => HtmlContainerInt.BaseUrl = value;
    }

    [Obsolete("Use SetHtmlWithStyleSet.")]
    public void SetHtml(string htmlSource, CssData baseCssData = null, string baseUrl = null) => HtmlContainerInt.SetHtml(htmlSource, baseCssData, baseUrl);

    public void SetHtmlWithStyleSet(string htmlSource, HtmlStyleSet baseStyleSet = null, string baseUrl = null) =>
        HtmlContainerInt.SetHtmlWithStyleSet(htmlSource, baseStyleSet, baseUrl);

    /// <summary>
    /// Binds a canonical DOM document directly to the renderer. The CSS-box
    /// snapshot is rebuilt lazily when the document version changes.
    /// </summary>
    [Obsolete("Use SetDocumentWithStyleSet.")]
    public void SetDocument(Broiler.Dom.DomDocument document, CssData baseCssData = null, string baseUrl = null) =>
        HtmlContainerInt.SetDocument(document, baseCssData, baseUrl);

    public void SetDocumentWithStyleSet(Broiler.Dom.DomDocument document, HtmlStyleSet baseStyleSet = null, string baseUrl = null) =>
        HtmlContainerInt.SetDocumentWithStyleSet(document, baseStyleSet, baseUrl);

    public void Clear() => HtmlContainerInt.Clear();

    public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline) => HtmlContainerInt.GetHtml(styleGen);

    public string GetAttributeAt(PointF location, string attribute) => HtmlContainerInt.GetAttributeAt(location, attribute);

    public string GetLinkAt(PointF location) => HtmlContainerInt.GetLinkAt(location);

    public void PerformLayout()
    {
        using var bitmap = new BBitmap(1, 1);
        PerformLayout(bitmap, new RectangleF(0, 0, 99999, 99999));
    }

    public void PerformLayout(BBitmap bitmap, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var g = bitmap.OpenGraphics(clip);
        HtmlContainerInt.PerformLayout(g);
    }

    /// <summary>
    /// Performs layout using an internal temporary surface so callers that only
    /// need layout side effects (for example <see cref="LatestFragmentTree"/> or
    /// <see cref="GetElementRectangle(string)"/>) do not need to construct backend objects.
    /// </summary>
    public void PerformLayout(RectangleF clip)
    {
        int width = Math.Max(1, (int)Math.Ceiling(clip.Width));
        int height = Math.Max(1, (int)Math.Ceiling(clip.Height));

        using var bitmap = new BBitmap(width, height);
        PerformLayout(bitmap, clip);
    }

    public void PerformPaint(BBitmap bitmap, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var g = bitmap.OpenGraphics(clip);
        HtmlContainerInt.PerformPaint(g);
    }

    public void PerformPaint(BBitmap bitmap, RectangleF clip, PointF translation)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var g = bitmap.OpenGraphics(clip, translation);
        HtmlContainerInt.PerformPaint(g);
    }

    public DisplayList CreateDisplayList() => HtmlContainerInt.CreateDisplayList();

    /// <summary>
    /// Returns the bounding rectangle of the element with the specified <paramref name="elementId"/>,
    /// or <c>null</c> if no such element exists.  Useful for scrolling to an anchor target
    /// (e.g.&nbsp;<c>#top</c>).
    /// Requires <see cref="SetHtml"/> and <see cref="PerformLayout"/> to have been called first.
    /// </summary>
    public RectangleF? GetElementRectangle(string elementId) => HtmlContainerInt.GetElementRectangle(elementId);

    public void ScrollToElement(string elementId)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var rect = GetElementRectangle(elementId);
        if (!rect.HasValue)
            return;

        ScrollToPoint(rect.Value.Location);
    }

    public void ScrollToPoint(PointF location) => ScrollToPoint(location.X, location.Y);

    public void ScrollToPoint(float x, float y) => ScrollOffset = new PointF(-x, -y);

    /// <summary>
    /// Returns all links found in the parsed HTML document.
    /// Requires <see cref="SetHtml"/> to have been called first.
    /// Each link includes its <c>id</c>, <c>href</c>, and bounding rectangle.
    /// </summary>
    public List<LinkElementData<RectangleF>> GetLinks() => HtmlContainerInt.GetLinks();

    public void HandleMouseDown(PointF location, bool leftButton = true, bool rightButton = false)
    {
        var control = CreateControl(location, leftButton, rightButton);
        HtmlContainerInt.HandleMouseDown(control, location);
    }

    public void HandleMouseUp(PointF location, bool leftButton = true, bool rightButton = false)
    {
        var control = CreateControl(location, leftButton, rightButton);
        HtmlContainerInt.HandleMouseUp(control, location, new RMouseEvent(leftButton));
    }

    public void HandleMouseDoubleClick(PointF location, bool leftButton = true, bool rightButton = false)
    {
        var control = CreateControl(location, leftButton, rightButton);
        HtmlContainerInt.HandleMouseDoubleClick(control, location);
    }

    public void HandleMouseMove(PointF mousePos, bool leftButton = false, bool rightButton = false)
    {
        var control = CreateControl(mousePos, leftButton, rightButton);
        HtmlContainerInt.HandleMouseMove(control, mousePos);
    }

    public void HandleMouseLeave()
    {
        var control = CreateControl(PointF.Empty, false, false);
        HtmlContainerInt.HandleMouseLeave(control);
    }

    public void HandleKeyDown(bool controlKey, bool aKeyCode, bool cKeyCode)
    {
        var control = CreateControl(PointF.Empty, false, false);
        HtmlContainerInt.HandleKeyDown(control, new RKeyEvent(controlKey, aKeyCode, cKeyCode));
    }

    /// <summary>
    /// Returns the computed background color of the root CSS box, or
    /// <see cref="Color.Empty"/> when the root has no explicit (non-transparent)
    /// background.  Requires <see cref="SetHtml"/> to have been called first.
    /// Per the CSS 2.1 canvas background model (§14.2), the canvas background
    /// is taken from the root element; if that is transparent, the body
    /// element's background is used instead.
    /// </summary>
    public Color GetRootBackgroundColor() => HtmlContainerInt.GetRootBackgroundColor();

    public void Dispose() => HtmlContainerInt.Dispose();

    private static ControlAdapter CreateControl(PointF mouseLocation, bool leftButton, bool rightButton) =>
        new(mouseLocation, leftButton, rightButton);
}
