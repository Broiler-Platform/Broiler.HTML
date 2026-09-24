using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Drawing;
using Broiler.HTML.Core.Entities;
using Broiler.Graphics.Adapters;
using Broiler.HTML.Core.Utils;
using Broiler.Net.Http;

namespace Broiler.HTML.Core.Handlers;

internal sealed class ImageLoadHandler : IImageLoadHandler
{
    private readonly IHtmlContainerInt _htmlContainer;
    private readonly ActionInt<BImage?, RectangleF, bool> _loadCompleteCallback;
    private RectangleF _imageRectangle;
    private IReadOnlyDictionary<string, string>? _attributes;
    private bool _asyncCallback;
    private bool _releaseImageObject;
    private bool _disposed;

    public ImageLoadHandler(IHtmlContainerInt htmlContainer, ActionInt<BImage?, RectangleF, bool> loadCompleteCallback)
    {
        ArgumentNullException.ThrowIfNull(htmlContainer);
        ArgumentNullException.ThrowIfNull(loadCompleteCallback);

        _htmlContainer = htmlContainer;
        _loadCompleteCallback = loadCompleteCallback;
    }

    public BImage? Image { get; private set; }
    public RectangleF Rectangle => _imageRectangle;

    public void LoadImage(string src, Dictionary<string, string> attributes, Uri baseUrl)
    {
        try
        {
            // Kept for a network load, which takes the element's crossorigin setting from them.
            _attributes = attributes;
            var args = new HtmlImageLoadEventArgs(src, attributes, OnHtmlImageLoadEventCallback, baseUrl);
            _htmlContainer.RaiseHtmlImageLoadEvent(args);
            _asyncCallback = !_htmlContainer.AvoidAsyncImagesLoading;

            if (!args.Handled)
            {
                if (!string.IsNullOrEmpty(src))
                {
                    if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    {
                        SetFromInlineData(src);
                    }
                    else
                    {
                        SetImageFromPath(src, baseUrl);
                    }
                }
                else
                {
                    ImageLoadComplete(false);
                }
            }
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Exception in handling image source", ex);
            ImageLoadComplete(false);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseObjects();
    }


    private void OnHtmlImageLoadEventCallback(string? path, object? image, RectangleF imageRectangle, Uri baseUrl)
    {
        if (_disposed)
            return;

        _imageRectangle = imageRectangle;

        if (image != null)
        {
            Image = _htmlContainer.ConvertImage(image);
            ImageLoadComplete(_asyncCallback);
        }
        else if (!string.IsNullOrEmpty(path))
        {
            SetImageFromPath(path, baseUrl);
        }
        else
        {
            ImageLoadComplete(_asyncCallback);
        }
    }

    private void SetFromInlineData(string src)
    {
        Image = GetImageFromData(src);

        if (Image == null)
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed extract image from inline data");

        _releaseImageObject = true;
        ImageLoadComplete(false);
    }

    private BImage? GetImageFromData(string src)
    {
        var s = src[(src.IndexOf(':') + 1)..].Split([','], 2);

        if (s.Length != 2)
            return null;

        int imagePartsCount = 0, base64PartsCount = 0;
        foreach (var part in s[0].Split([';']))
        {
            var pPart = part.Trim();

            if (pPart.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase))
                imagePartsCount++;

            if (pPart.Equals("base64", StringComparison.InvariantCultureIgnoreCase))
                base64PartsCount++;
        }

        if (imagePartsCount <= 0)
            return null;

        byte[] imageData = base64PartsCount > 0
            ? Convert.FromBase64String(Uri.UnescapeDataString(s[1].Trim()))
            : new UTF8Encoding().GetBytes(Uri.UnescapeDataString(s[1].Trim()));

        return _htmlContainer.ImageFromStream(new MemoryStream(imageData));
    }

    private void SetImageFromPath(string path, Uri baseUrl)
    {
        var uri = CommonUtils.TryGetUri(path) ?? ParseAsBrowserWould(path, baseUrl);

        bool isRootRelativePath = path.StartsWith('/')
            && !path.StartsWith("//", StringComparison.Ordinal);

        if (uri != null
            && uri.IsAbsoluteUri == false
            && baseUrl != null
            && (isRootRelativePath || !Path.IsPathRooted(path) || !_htmlContainer.AllowsLocalFiles))
        {
            uri = new Uri(baseUrl, uri);
        }

        if (uri != null && uri.IsAbsoluteUri && uri.Scheme != "file")
        {
            SetImageFromUrl(uri);
        }
        else if (!_htmlContainer.AllowsLocalFiles)
        {
            // A web page's image never comes off the file system, nor off a UNC share a rooted path names.
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Refused to load a local image into a network document: " + path);
            ImageLoadComplete(false);
        }
        else
        {
            var fileInfo = CommonUtils.TryGetFileInfo((uri != null && uri.IsAbsoluteUri) ? uri.AbsolutePath : path);
            if (fileInfo != null)
            {
                SetImageFromFile(fileInfo);
            }
            else
            {
                _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed load image, invalid source: " + path);
                ImageLoadComplete(false);
            }
        }
    }

    /// <summary>
    /// A source <see cref="CommonUtils.TryGetUri"/> refused, parsed the way a browser's URL parser takes
    /// it: an <c>http(s)</c> URL with an unescaped space or <c>|</c>, which it percent-encodes and
    /// loads, and -- for a document that may not read local files, where the source can only be a URL
    /// -- anything that resolves against the document's base. <see langword="null"/> otherwise, which
    /// leaves a local document's source to the file-path handling it has always had.
    /// </summary>
    private Uri? ParseAsBrowserWould(string path, Uri? baseUrl)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute;

        return !_htmlContainer.AllowsLocalFiles &&
               baseUrl is { IsAbsoluteUri: true } &&
               Uri.TryCreate(baseUrl, path, out var resolved)
            ? resolved
            : null;
    }

    private void SetImageFromFile(FileInfo source)
    {
        if (source.Exists)
        {
            if (_htmlContainer.AvoidAsyncImagesLoading)
                LoadImageFromFile(source.FullName);
            else
                ThreadPool.QueueUserWorkItem(state => LoadImageFromFile(source.FullName));
        }
        else
        {
            ImageLoadComplete();
        }
    }

    private void LoadImageFromFile(string source)
    {
        try
        {
            // Scoped to the decode. ImageFromStream materialises the encoded bytes before it
            // returns and the decoded image never reads the stream again, so there is nothing to
            // hold the handle open for. Keeping it — as this used to — left the renderer owning an
            // OS handle on every image sub-resource it had loaded for as long as the box tree that
            // loaded it stayed alive, which on Windows blocks deleting or renaming that file.
            using var imageFileStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            lock (_loadCompleteCallback)
            {
                if (!_disposed)
                    Image = _htmlContainer.ImageFromStream(imageFileStream);

                _releaseImageObject = true;
            }

            ImageLoadComplete();
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to load image from disk: " + source, ex);
            ImageLoadComplete();
        }
    }

    private void SetImageFromUrl(Uri source)
    {
        if (_htmlContainer.UsesRequestTransport)
        {
            // The host's transport sends the profile's cookies for the container's document, so the
            // shared %TEMP% cache below - keyed by URL alone, shared by every process, container and
            // profile - must neither answer nor record the load. The container keeps its own cache.
            _htmlContainer.DownloadImage(
                source,
                SubresourceScope.GetCrossOrigin(_attributes),
                !_htmlContainer.AvoidAsyncImagesLoading,
                OnImageLoadedThroughTransport);
            return;
        }

        var filePath = CommonUtils.GetLocalfileName(source);
        if (filePath == null)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed load image, invalid source: " + source);
            ImageLoadComplete(false);
        }
        else if (filePath.Exists && filePath.Length > 0)
        {
            SetImageFromFile(filePath);
        }
        else
        {
            _htmlContainer.DownloadImage(source, filePath.FullName, !_htmlContainer.AvoidAsyncImagesLoading, OnDownloadImageCompleted);
        }
    }

    private void OnDownloadImageCompleted(Uri imageUri, string filePath, Exception? error, bool canceled)
    {
        if (canceled || _disposed)
            return;

        if (error == null)
        {
            LoadImageFromFile(filePath);
        }
        else
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to load image from URL: " + imageUri, error);
            ImageLoadComplete();
        }
    }

    private void OnImageLoadedThroughTransport(Uri imageUri, byte[]? body, Exception? error, bool canceled)
    {
        if (canceled || _disposed)
            return;

        if (error != null || body == null)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to load image from URL: " + imageUri, error);
            ImageLoadComplete();
            return;
        }

        try
        {
            using var stream = new MemoryStream(body, writable: false);

            lock (_loadCompleteCallback)
            {
                if (!_disposed)
                    Image = _htmlContainer.ImageFromStream(stream);

                _releaseImageObject = true;
            }

            ImageLoadComplete();
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to decode image from URL: " + imageUri, ex);
            ImageLoadComplete();
        }
    }

    private void ImageLoadComplete(bool async = true)
    {
        // can happen if some operation return after the handler was disposed
        if (_disposed)
            ReleaseObjects();
        else
            _loadCompleteCallback(Image, _imageRectangle, async);
    }

    private void ReleaseObjects()
    {
        lock (_loadCompleteCallback)
        {
            if (_releaseImageObject && Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
}
