using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.HTML.Core.Entities;

public delegate void HtmlImageLoadCallback(string path, object image, RectangleF imageRectangle, Uri baseUrl);

public sealed class HtmlImageLoadEventArgs : EventArgs
{
    private readonly HtmlImageLoadCallback _callback;

    internal HtmlImageLoadEventArgs(string src, Dictionary<string, string> attributes, HtmlImageLoadCallback callback, Uri baseUrl)
    {
        Src = src;
        Attributes = attributes;
        _callback = callback;
        BaseUri = baseUrl;
    }

    public string Src { get; }
    public Dictionary<string, string> Attributes { get; }
    public bool Handled { get; set; }
    public Uri BaseUri { get; set; }

    public void Callback()
    {
        Handled = true;
        _callback(null, null, new RectangleF(), BaseUri);
    }

    public void Callback(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Handled = true;
        _callback(path, null, RectangleF.Empty, BaseUri);
    }

    public void Callback(string path, double x, double y, double width, double height)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Handled = true;
        _callback(path, null, new RectangleF((float)x, (float)y, (float)width, (float)height), BaseUri);
    }

    public void Callback(object image)
    {
        ArgumentNullException.ThrowIfNull(image);

        Handled = true;
        _callback(null, image, RectangleF.Empty, BaseUri);
    }

    public void Callback(object image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);

        Handled = true;
        _callback(null, image, new RectangleF((float)x, (float)y, (float)width, (float)height), BaseUri);
    }
}