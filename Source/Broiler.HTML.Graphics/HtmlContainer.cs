using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.Core.Core.IR;
using GraphicsBitmap = Broiler.Graphics.BBitmap;
using ImageBitmap = Broiler.HTML.Image.BBitmap;
using ImageContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.HTML.Graphics;

/// <summary>
/// HTML container that paints into <see cref="Broiler.Graphics.BBitmap"/> targets.
/// </summary>
public sealed class HtmlContainer : IDisposable
{
    private readonly ImageContainer _inner = new();

    public event EventHandler LoadComplete
    {
        add { _inner.LoadComplete += value; }
        remove { _inner.LoadComplete -= value; }
    }

    public event EventHandler<HtmlLinkClickedEventArgs> LinkClicked
    {
        add { _inner.LinkClicked += value; }
        remove { _inner.LinkClicked -= value; }
    }

    public event EventHandler<HtmlRefreshEventArgs> Refresh
    {
        add { _inner.Refresh += value; }
        remove { _inner.Refresh -= value; }
    }

    public event EventHandler<HtmlScrollEventArgs> ScrollChange
    {
        add { _inner.ScrollChange += value; }
        remove { _inner.ScrollChange -= value; }
    }

    public event EventHandler<HtmlRenderErrorEventArgs> RenderError
    {
        add { _inner.RenderError += value; }
        remove { _inner.RenderError -= value; }
    }

    public event EventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad
    {
        add { _inner.StylesheetLoad += value; }
        remove { _inner.StylesheetLoad -= value; }
    }

    public event EventHandler<HtmlImageLoadEventArgs> ImageLoad
    {
        add { _inner.ImageLoad += value; }
        remove { _inner.ImageLoad -= value; }
    }

    public Fragment? LatestFragmentTree => _inner.LatestFragmentTree;

    public CssData CssData => _inner.CssData;

    public bool AvoidAsyncImagesLoading
    {
        get => _inner.AvoidAsyncImagesLoading;
        set => _inner.AvoidAsyncImagesLoading = value;
    }

    public bool AvoidImagesLateLoading
    {
        get => _inner.AvoidImagesLateLoading;
        set => _inner.AvoidImagesLateLoading = value;
    }

    public bool IsSelectionEnabled
    {
        get => _inner.IsSelectionEnabled;
        set => _inner.IsSelectionEnabled = value;
    }

    public bool IsContextMenuEnabled
    {
        get => _inner.IsContextMenuEnabled;
        set => _inner.IsContextMenuEnabled = value;
    }

    public PointF ScrollOffset
    {
        get => _inner.ScrollOffset;
        set => _inner.ScrollOffset = value;
    }

    public SizeF MaxSize
    {
        get => _inner.MaxSize;
        set => _inner.MaxSize = value;
    }

    public SizeF ActualSize => _inner.ActualSize;

    public string SelectedText => _inner.SelectedText;

    public string SelectedHtml => _inner.SelectedHtml;

    public void ClearSelection() => _inner.ClearSelection();

    public PointF Location
    {
        get => _inner.Location;
        set => _inner.Location = value;
    }

    public string BaseUrl
    {
        get => _inner.BaseUrl;
        set => _inner.BaseUrl = value;
    }

    public HtmlContainer()
    {
        _inner.AvoidAsyncImagesLoading = true;
        _inner.AvoidImagesLateLoading = true;
    }

    public void SetHtml(string htmlSource, CssData? baseCssData = null, string? baseUrl = null) =>
        _inner.SetHtml(htmlSource, baseCssData, baseUrl);

    public void Clear() => _inner.Clear();

    public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline) => _inner.GetHtml(styleGen);

    public string GetAttributeAt(PointF location, string attribute) => _inner.GetAttributeAt(location, attribute);

    public string GetLinkAt(PointF location) => _inner.GetLinkAt(location);

    public void PerformLayout(GraphicsBitmap bitmap, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using ImageBitmap image = HtmlRender.ToImageBitmap(bitmap);
        _inner.PerformLayout(image, clip);
    }

    public void PerformLayout(RectangleF clip) => _inner.PerformLayout(clip);

    public void PerformLayout() => _inner.PerformLayout();

    public void PerformPaint(GraphicsBitmap bitmap, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using ImageBitmap image = HtmlRender.ToImageBitmap(bitmap);
        _inner.PerformPaint(image, clip);
        HtmlRender.CopyToGraphicsBitmap(image, bitmap);
    }

    public void PerformPaint(GraphicsBitmap bitmap, RectangleF clip, PointF translation)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using ImageBitmap image = HtmlRender.ToImageBitmap(bitmap);
        _inner.PerformPaint(image, clip, translation);
        HtmlRender.CopyToGraphicsBitmap(image, bitmap);
    }

    public HtmlGraphicsRenderList CreateRenderList(Broiler.Graphics.IBroilerRenderer renderer, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        return HtmlGraphicsRenderListBuilder.Build(renderer, _inner.CreateDisplayList(), clip);
    }

    public RectangleF? GetElementRectangle(string elementId) => _inner.GetElementRectangle(elementId);

    public void ScrollToElement(string elementId) => _inner.ScrollToElement(elementId);

    public void ScrollToPoint(PointF location) => _inner.ScrollToPoint(location);

    public void ScrollToPoint(float x, float y) => _inner.ScrollToPoint(x, y);

    public List<LinkElementData<RectangleF>> GetLinks() => _inner.GetLinks();

    public Color GetRootBackgroundColor() => _inner.GetRootBackgroundColor();

    public void HandleMouseDown(PointF location, bool leftButton = true, bool rightButton = false) =>
        _inner.HandleMouseDown(location, leftButton, rightButton);

    public void HandleMouseUp(PointF location, bool leftButton = true, bool rightButton = false) =>
        _inner.HandleMouseUp(location, leftButton, rightButton);

    public void HandleMouseDoubleClick(PointF location, bool leftButton = true, bool rightButton = false) =>
        _inner.HandleMouseDoubleClick(location, leftButton, rightButton);

    public void HandleMouseMove(PointF mousePos, bool leftButton = false, bool rightButton = false) =>
        _inner.HandleMouseMove(mousePos, leftButton, rightButton);

    public void HandleMouseLeave() => _inner.HandleMouseLeave();

    public void HandleKeyDown(bool controlKey, bool aKeyCode, bool cKeyCode) =>
        _inner.HandleKeyDown(controlKey, aKeyCode, cKeyCode);

    public void Dispose() => _inner.Dispose();
}
