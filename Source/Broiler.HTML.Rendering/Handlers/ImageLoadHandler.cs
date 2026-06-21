using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Drawing;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Adapters;
using Broiler.HTML.Utils;

namespace Broiler.HTML.Rendering.Handlers;

internal sealed class ImageLoadHandler : IImageLoadHandler
{
    private readonly IHtmlContainerInt _htmlContainer;
    private readonly ActionInt<RImage, RectangleF, bool> _loadCompleteCallback;
    private FileStream _imageFileStream;
    private RectangleF _imageRectangle;
    private bool _asyncCallback;
    private bool _releaseImageObject;
    private bool _disposed;

    public ImageLoadHandler(IHtmlContainerInt htmlContainer, ActionInt<RImage, RectangleF, bool> loadCompleteCallback)
    {
        ArgumentNullException.ThrowIfNull(htmlContainer);
        ArgumentNullException.ThrowIfNull(loadCompleteCallback);

        _htmlContainer = htmlContainer;
        _loadCompleteCallback = loadCompleteCallback;
    }

    public RImage Image { get; private set; }
    public RectangleF Rectangle => _imageRectangle;

    public void LoadImage(string src, Dictionary<string, string> attributes, Uri baseUrl)
    {
        try
        {
            var args = new HtmlImageLoadEventArgs(src, attributes, OnHtmlImageLoadEventCallback, baseUrl);
            _htmlContainer.RaiseHtmlImageLoadEvent(args);
            _asyncCallback = !_htmlContainer.AvoidAsyncImagesLoading;

            if (!args.Handled)
            {
                if (!string.IsNullOrEmpty(src))
                {
                    if (src.StartsWith("data:image", StringComparison.CurrentCultureIgnoreCase))
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


    private void OnHtmlImageLoadEventCallback(string path, object image, RectangleF imageRectangle, Uri baseUrl)
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

    private RImage GetImageFromData(string src)
    {
        var s = src.Substring(src.IndexOf(':') + 1).Split([','], 2);

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
        var uri = CommonUtils.TryGetUri(path);

        bool isRootRelativePath = path.StartsWith("/", StringComparison.Ordinal)
            && !path.StartsWith("//", StringComparison.Ordinal);
        if (uri != null
            && uri.IsAbsoluteUri == false
            && baseUrl != null
            && (isRootRelativePath || !Path.IsPathRooted(path)))
        {
            uri = new Uri(baseUrl, uri);
        }

        if (uri != null && uri.IsAbsoluteUri && uri.Scheme != "file")
        {
            SetImageFromUrl(uri);
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
            var imageFileStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            lock (_loadCompleteCallback)
            {
                _imageFileStream = imageFileStream;

                if (!_disposed)
                    Image = _htmlContainer.ImageFromStream(_imageFileStream);

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
        var filePath = CommonUtils.GetLocalfileName(source);
        if (filePath.Exists && filePath.Length > 0)
        {
            SetImageFromFile(filePath);
        }
        else
        {
            _htmlContainer.DownloadImage(source, filePath.FullName, !_htmlContainer.AvoidAsyncImagesLoading, OnDownloadImageCompleted);
        }
    }

    private void OnDownloadImageCompleted(Uri imageUri, string filePath, Exception error, bool canceled)
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

            _imageFileStream?.Dispose();
            _imageFileStream = null;
        }
    }
}
