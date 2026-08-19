using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Broiler.HTML.Rendering.Handlers;

public delegate void DownloadFileAsyncCallback(Uri imageUri, string filePath, Exception error, bool canceled);

internal sealed class ImageDownloader : IDisposable
{
    // Synchronous image fetch on the render path (HtmlRender sets AvoidAsyncImagesLoading, so
    // DownloadImageFromUrl runs inline on the layout thread). The default HttpClient timeout is
    // 100 s, so a single unreachable http(s) <img> blocks the render for more than three times any
    // per-test budget — a hang, not a render — and does it again on the next layout pass, since a
    // failed URL is not remembered. Cap it short so an unreachable image fails fast and the page
    // renders without it.
    //
    // This is the same fix StylesheetLoadHandler already carries for <link> (WPT #1147 Timeout
    // cluster); images were simply missed when that one was made. It is what times out
    // conformance-checkers/html/elements/img/src-isvalid.html, whose 88 <img> include IP literals
    // and documentation addresses (http://192.0x00A80001, http://[2001::1]) that black-hole on a
    // CI runner with real internet.
    //
    // Identified, too: HttpClient sends no User-Agent unless given one, and a host that refuses an
    // unidentified request refuses the image rather than serving a different one — every
    // upload.wikimedia.org image on a mediawiki.org page came back 403 Forbidden.
    private static readonly HttpClient SharedHttpClient =
        Broiler.Layout.Net.BroilerUserAgent.Apply(new HttpClient { Timeout = TimeSpan.FromSeconds(5) });
    private readonly Dictionary<string, List<DownloadFileAsyncCallback>> _imageDownloadCallbacks = [];
    private readonly CancellationTokenSource _cts = new();

    public void DownloadImage(Uri imageUri, string filePath, bool async, DownloadFileAsyncCallback cachedFileCallback)
    {
        ArgumentNullException.ThrowIfNull(imageUri);
        ArgumentNullException.ThrowIfNull(cachedFileCallback);

        // to handle if the file is already been downloaded
        bool download = true;

        lock (_imageDownloadCallbacks)
        {
            if (_imageDownloadCallbacks.TryGetValue(filePath, out List<DownloadFileAsyncCallback> value))
            {
                download = false;
                value.Add(cachedFileCallback);
            }
            else
            {
                _imageDownloadCallbacks[filePath] = [cachedFileCallback];
            }
        }

        if (!download)
            return;

        var tempPath = Path.GetTempFileName();

        if (async)
            ThreadPool.QueueUserWorkItem(_ => DownloadImageFromUrl(imageUri, tempPath, filePath), null);
        else
            DownloadImageFromUrl(imageUri, tempPath, filePath);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _imageDownloadCallbacks.Clear();
    }

    private void DownloadImageFromUrl(Uri source, string tempPath, string filePath)
    {
        string contentType = null;
        Exception error = null;
        bool cancelled = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            using var response = SharedHttpClient.Send(request, _cts.Token);
            response.EnsureSuccessStatusCode();
            contentType = response.Content.Headers.ContentType?.MediaType;
            using var fs = File.Create(tempPath);
            response.Content.ReadAsStream().CopyTo(fs);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        OnDownloadImageCompleted(contentType, source, tempPath, filePath, error, cancelled);
    }

    private void OnDownloadImageCompleted(string contentType, Uri source, string tempPath, string filePath, Exception error, bool cancelled)
    {
        if (!cancelled)
        {
            if (error == null)
            {
                if (contentType == null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
                    error = new Exception("Failed to load image, not image content type: " + contentType);
            }

            if (error == null)
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Move(tempPath, filePath);
                    }
                    catch (Exception ex)
                    {
                        error = new Exception("Failed to move downloaded image from temp to cache location", ex);
                    }
                }

                error = File.Exists(filePath) ? null : (error ?? new Exception("Failed to download image, unknown error"));
            }
        }

        List<DownloadFileAsyncCallback> callbacksList;
        lock (_imageDownloadCallbacks)
        {
            if (_imageDownloadCallbacks.TryGetValue(filePath, out callbacksList))
                _imageDownloadCallbacks.Remove(filePath);
        }

        if (callbacksList == null)
            return;

        foreach (var cachedFileCallback in callbacksList)
        {
            try
            {
                cachedFileCallback(source, filePath, error, cancelled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HtmlRenderer] ImageDownloader callback error: {ex.Message}");
            }
        }
    }
}
