using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using Broiler.Net.Http;

namespace Broiler.HTML.Core.Handlers;

public delegate void DownloadFileAsyncCallback(Uri imageUri, string filePath, Exception? error, bool canceled);

/// <summary>Completion of an image loaded through the host's transport: the body, or the error, or a cancellation.</summary>
internal delegate void TransportImageCallback(Uri imageUri, byte[]? body, Exception? error, bool canceled);

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
    //
    // Used only when the host supplies no transport, and without cookies (see LegacySubresourceClient).
    private static readonly HttpClient SharedHttpClient = LegacySubresourceClient.Create(TimeSpan.FromSeconds(5));

    // The same five seconds for a load through the host's transport, whose own timeout is a navigation's.
    private static readonly TimeSpan TransportBudget = SharedHttpClient.Timeout;

    // Far above any image a page displays, and still a bound: the body is streamed to disk, and
    // a response that does not stop would otherwise fill it.
    private const long MaxImageBytes = 64L * 1024 * 1024;

    private readonly SubresourceScope _scope;
    private readonly Dictionary<string, List<DownloadFileAsyncCallback>> _imageDownloadCallbacks = [];
    private readonly Dictionary<string, List<TransportImageCallback>> _transportCallbacks = [];

    /// <param name="scope">
    /// The render tree's scope. Its cancellation stops this downloader's requests; the container cancels it when it
    /// tears the tree down, before it disposes the downloader.
    /// </param>
    public ImageDownloader(SubresourceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    public void DownloadImage(Uri imageUri, string filePath, bool async, DownloadFileAsyncCallback cachedFileCallback)
    {
        ArgumentNullException.ThrowIfNull(imageUri);
        ArgumentNullException.ThrowIfNull(cachedFileCallback);

        if (!Enlist(_imageDownloadCallbacks, filePath, cachedFileCallback))
            return;

        if (async)
            ThreadPool.QueueUserWorkItem(_ => DownloadImageFromUrl(imageUri, filePath), null);
        else
            DownloadImageFromUrl(imageUri, filePath);
    }

    /// <summary>
    /// Loads an image through the host's transport as an image request with the element's CORS setting. The body stays
    /// in memory: nothing is written to the shared disk cache, which is keyed by URL alone and would hand one profile's
    /// or document's response to another.
    /// </summary>
    public void DownloadImage(Uri imageUri, CorsSetting crossOrigin, bool async, TransportImageCallback callback)
    {
        ArgumentNullException.ThrowIfNull(imageUri);
        ArgumentNullException.ThrowIfNull(callback);

        // One request per distinct request: the same URL with another CORS setting has other credentials.
        var key = ((int)crossOrigin).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + imageUri.AbsoluteUri;
        if (!Enlist(_transportCallbacks, key, callback))
            return;

        if (async)
            ThreadPool.QueueUserWorkItem(_ => LoadImageThroughTransport(imageUri, crossOrigin, key), null);
        else
            LoadImageThroughTransport(imageUri, crossOrigin, key);
    }

    public void Dispose()
    {
        // Cancelling in-flight requests is the scope's job: the container cancels it before this runs, and a
        // request still in flight has nowhere left to deliver to. Aborting one surfaces on its thread-pool thread as
        // `IOException: Unable to read data from the transport connection` (SocketError.OperationAborted), which
        // is the price of stopping work whose result is already worthless.
        //
        // Under the lock the download path already takes.  The clear ran unsynchronised while
        // DownloadImage and OnDownloadImageCompleted were free to be inside TryGetValue/Add on
        // another thread, which is a torn Dictionary rather than merely a lost entry.
        lock (_imageDownloadCallbacks)
        {
            _imageDownloadCallbacks.Clear();
            _transportCallbacks.Clear();
        }
    }

    // True when the caller is the first to ask for this key and so has to start the download.
    private bool Enlist<TCallback>(Dictionary<string, List<TCallback>> callbacks, string key, TCallback callback)
    {
        lock (_imageDownloadCallbacks)
        {
            if (callbacks.TryGetValue(key, out List<TCallback>? waiting))
            {
                waiting.Add(callback);
                return false;
            }

            callbacks[key] = [callback];
            return true;
        }
    }

    private List<TCallback>? TakeCallbacks<TCallback>(Dictionary<string, List<TCallback>> callbacks, string key)
    {
        lock (_imageDownloadCallbacks)
        {
            if (callbacks.Remove(key, out var waiting))
                return waiting;
        }

        return null;
    }

    private void LoadImageThroughTransport(Uri source, CorsSetting crossOrigin, string key)
    {
        byte[]? body = null;
        Exception? error = null;
        bool cancelled = false;

        try
        {
            body = _scope.Fetch(source, RequestDestination.Image, crossOrigin, TransportBudget, MaxImageBytes, IsImageMediaType).Body;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        var callbacks = TakeCallbacks(_transportCallbacks, key);
        if (callbacks == null)
            return;

        foreach (var callback in callbacks)
        {
            try
            {
                callback(source, body, error, cancelled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HtmlRenderer] ImageDownloader callback error: {ex.Message}");
            }
        }
    }

    private static bool IsImageMediaType(string? mediaType) =>
        mediaType != null && mediaType.StartsWith("image", StringComparison.OrdinalIgnoreCase);

    private void DownloadImageFromUrl(Uri source, string filePath)
    {
        string? tempPath = null;
        Exception? error = null;
        bool cancelled = false;
        var token = _scope.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);

            // Headers first. Reading the whole response before looking at it buffered the body in
            // memory, however large, only to discard it when it turned out not to be an image.
            using var response = SharedHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            string? contentType = response.Content.Headers.ContentType?.MediaType;

            if (!IsImageMediaType(contentType))
                throw new InvalidDataException("Failed to load image, not image content type: " + contentType);

            if (response.Content.Headers.ContentLength > MaxImageBytes)
                throw new InvalidDataException($"Failed to load image, larger than {MaxImageBytes} bytes");

            // Read this way, the client's Timeout ends with the headers, so the body gets the same
            // budget of its own. A synchronous read cannot observe a token; cancelling aborts the
            // response under it instead.
            using var bodyBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
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
        catch (Exception) when (token.IsCancellationRequested)
        {
            // The tree was torn down and the aborted read threw something else.
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        OnDownloadImageCompleted(source, tempPath, filePath, error, cancelled);
    }

    private void OnDownloadImageCompleted(Uri source, string? tempPath, string filePath, Exception? error, bool cancelled)
    {
        if (!cancelled && error == null)
        {
            try
            {
                // A download that neither failed nor was cancelled has written its temp file.
                File.Move(tempPath!, filePath, overwrite: true);
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

        var callbacksList = TakeCallbacks(_imageDownloadCallbacks, filePath);
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

    private static void CopyBounded(Stream source, FileStream destination)
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
