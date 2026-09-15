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

    // Far above any image a page displays, and still a bound: the body is streamed to disk, and
    // a response that does not stop would otherwise fill it.
    private const long MaxImageBytes = 64L * 1024 * 1024;

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

        if (async)
            ThreadPool.QueueUserWorkItem(_ => DownloadImageFromUrl(imageUri, filePath), null);
        else
            DownloadImageFromUrl(imageUri, filePath);
    }

    public void Dispose()
    {
        // Cancelling is the point, not a side effect: the render tree these callbacks target is
        // being torn down, so a download still in flight has nowhere left to deliver to.  It does
        // abort that request's socket, and the aborted read surfaces as `IOException: Unable to
        // read data from the transport connection` (SocketError.OperationAborted) on a
        // thread-pool thread — the price of stopping work whose result is already worthless.
        _cts.Cancel();

        // Deliberately not disposed.  DownloadImageFromUrl hands _cts.Token to HttpClient.Send on
        // a thread-pool thread, and disposing the source under an in-flight send throws
        // ObjectDisposedException out of the token's registration — a race as wide as whatever the
        // request has left to run.  A cancelled source with no timer and no WaitHandle holds
        // nothing that needs reclaiming on this schedule; it is collected with the downloader once
        // those sends finish.  Dispose stays idempotent, since Cancel on a cancelled source is a
        // no-op.

        // Under the lock the download path already takes.  The clear ran unsynchronised while
        // DownloadImage and OnDownloadImageCompleted were free to be inside TryGetValue/Add on
        // another thread, which is a torn Dictionary rather than merely a lost entry.
        lock (_imageDownloadCallbacks)
            _imageDownloadCallbacks.Clear();
    }

    private void DownloadImageFromUrl(Uri source, string filePath)
    {
        string tempPath = null;
        Exception error = null;
        bool cancelled = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);

            // Headers first. Reading the whole response before looking at it buffered the body in
            // memory, however large, only to discard it when it turned out not to be an image.
            using var response = SharedHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
            response.EnsureSuccessStatusCode();

            string contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Failed to load image, not image content type: " + contentType);
            if (response.Content.Headers.ContentLength > MaxImageBytes)
                throw new InvalidDataException($"Failed to load image, larger than {MaxImageBytes} bytes");

            // Read this way, the client's Timeout ends with the headers, so the body gets the same
            // budget of its own. A synchronous read cannot observe a token; cancelling aborts the
            // response under it instead.
            using var bodyBudget = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            bodyBudget.CancelAfter(SharedHttpClient.Timeout);
            using var abortRead = bodyBudget.Token.Register(response.Dispose);

            tempPath = Path.GetTempFileName();
            using var body = response.Content.ReadAsStream(bodyBudget.Token);
            using var file = File.Create(tempPath);
            CopyBounded(body, file);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception) when (_cts.IsCancellationRequested)
        {
            // Dispose cancelled the downloader and the aborted read threw something else.
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        OnDownloadImageCompleted(source, tempPath, filePath, error, cancelled);
    }

    private void OnDownloadImageCompleted(Uri source, string tempPath, string filePath, Exception error, bool cancelled)
    {
        if (!cancelled && error == null)
        {
            try
            {
                File.Move(tempPath, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                error = new Exception("Failed to move downloaded image from temp to cache location", ex);
            }

            error = File.Exists(filePath) ? null : (error ?? new Exception("Failed to download image, unknown error"));
        }

        // The temp file exists from the moment the body starts, so every download that stopped
        // short of the move - an HTTP error, a cancellation, a failed move - used to leave it behind.
        if (tempPath != null)
            TryDeleteFile(tempPath);

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

    private static void CopyBounded(Stream source, Stream destination)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxImageBytes)
                throw new InvalidDataException($"Failed to load image, larger than {MaxImageBytes} bytes");

            destination.Write(buffer, 0, read);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HtmlRenderer] ImageDownloader could not delete '{path}': {ex.Message}");
        }
    }
}
