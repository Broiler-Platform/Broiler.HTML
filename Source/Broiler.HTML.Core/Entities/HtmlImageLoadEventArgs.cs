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

    public void Callback(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Handled = true;
        _callback(path, null, RectangleF.Empty, BaseUri);
    }
}