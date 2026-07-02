using System;
using System.Drawing;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using ImageContainer = Broiler.HTML.Image.HtmlContainer;
using Broiler.Graphics;

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

    public void SetHtmlWithStyleSet(string htmlSource, HtmlStyleSet? baseStyleSet = null, string? baseUrl = null) =>
        _inner.SetHtmlWithStyleSet(htmlSource, baseStyleSet, baseUrl);

    public string GetAttributeAt(PointF location, string attribute) => _inner.GetAttributeAt(location, attribute);

    public FormInputElementData<RectangleF> GetEditableInputAt(PointF location) =>
        _inner.GetEditableInputAt(location);

    public FormInputElementData<RectangleF> GetEditableInputAtDocumentPoint(PointF documentLocation) =>
        _inner.GetEditableInputAtDocumentPoint(documentLocation);

    public bool SetEditableInputValueAtDocumentPoint(PointF documentLocation, string value) =>
        _inner.SetEditableInputValueAtDocumentPoint(documentLocation, value);

    public string GetLinkAt(PointF location) => _inner.GetLinkAt(location);

    public void PerformLayout(RectangleF clip) => _inner.PerformLayout(clip);

    public HtmlGraphicsRenderList CreateRenderList(Broiler.Graphics.IBroilerRenderer renderer, RectangleF clip)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        return HtmlGraphicsRenderListBuilder.Build(renderer, _inner.CreateDisplayList(), clip);
    }

    public RectangleF? GetElementRectangle(string elementId) => _inner.GetElementRectangle(elementId);

    public BColor GetRootBackgroundColor() => _inner.GetRootBackgroundColor();

    public void HandleMouseDown(PointF location, bool leftButton = true, bool rightButton = false) =>
        _inner.HandleMouseDown(location, leftButton, rightButton);

    public void HandleMouseUp(PointF location, bool leftButton = true, bool rightButton = false) =>
        _inner.HandleMouseUp(location, leftButton, rightButton);

    public void HandleMouseMove(PointF mousePos, bool leftButton = false, bool rightButton = false) =>
        _inner.HandleMouseMove(mousePos, leftButton, rightButton);

    public void HandleMouseLeave() => _inner.HandleMouseLeave();

    public void HandleKeyDown(bool controlKey, bool aKeyCode, bool cKeyCode) =>
        _inner.HandleKeyDown(controlKey, aKeyCode, cKeyCode);

    public void Dispose() => _inner.Dispose();
}
