using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Broiler.Net.Http;

namespace Broiler.HTML.Core.Handlers;

/// <summary>
/// One render tree's view of its container's network settings: the <see cref="IBrowserRequestTransport"/> and
/// <see cref="DocumentRequestContext"/> the container held when the tree was built, the container's subresource
/// cache, and the cancellation every load the tree starts observes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a snapshot.</b> Stylesheets and fonts load while the tree is built and images while it is laid out, possibly
/// on thread-pool workers. A host that swaps the transport or the document context in between (a new navigation
/// reusing the container) must not have the old tree's remaining loads sent for the new document, nor the new
/// document's loads sent with the old one's cookies.
/// </para>
/// <para>
/// <b>Why a cancellation per tree.</b> The container cancels the scope whenever the tree is torn down (a reparse, a
/// bound-document rebuild, <c>Clear</c> or <c>Dispose</c>), so a load for a discarded tree stops instead of
/// finishing, and exchanging cookies, for a document nobody renders any more. The source is never disposed: loads
/// hand its token to the transport on other threads, and disposing it under one of them throws out of the token's
/// registration. A cancelled source with no timer holds nothing that needs reclaiming on this schedule.
/// </para>
/// <para>
/// <b>No fallback identity.</b> A transport without a <see cref="DocumentRequestContext"/> loads nothing: the context is
/// the request's client, whose origin decides CORS and same-origin credentials and whose ancestors decide the
/// cookies' site. A base URL is not a document identity, so the scope never derives one from it.
/// </para>
/// </remarks>
internal sealed class SubresourceScope
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SubresourceCache _cache;

    /// <param name="transport">The host's transport, or <see langword="null"/> for the legacy cookie-less clients.</param>
    /// <param name="document">The document the tree renders; required for a load through <paramref name="transport"/>.</param>
    /// <param name="cache">The container's cache, which outlives this tree while the transport and document stay the same.</param>
    public SubresourceScope(IBrowserRequestTransport? transport, DocumentRequestContext? document, SubresourceCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        Transport = transport;
        Document = document;
        _cache = cache;
    }

    /// <summary>The host's transport, or <see langword="null"/> when this tree loads through the legacy clients.</summary>
    public IBrowserRequestTransport? Transport { get; }

    /// <summary>The document every request of this tree is made for (the request's client).</summary>
    public DocumentRequestContext? Document { get; }

    /// <summary>Whether network loads of this tree go through <see cref="Transport"/>.</summary>
    public bool UsesTransport => Transport != null;

    /// <summary>
    /// Whether this tree may read stylesheets, images and fonts from the local file system (a <c>file:</c> URL, a
    /// rooted or UNC path): only for a <c>file:</c> document, or for a host that gave the container no document
    /// identity and no transport (a tool rendering local markup).
    /// </summary>
    /// <remarks>
    /// A web page must not reach the file system through its subresources. A local file would be read and applied to
    /// a document of another origin, and on Windows a UNC path (<c>\\host\share</c>, which <c>file://host/share</c>
    /// and a protocol-relative <c>//host/share</c> both become) opens an SMB session that authenticates as the user.
    /// </remarks>
    public bool AllowsLocalFiles => LocalFilesAllowed(Transport, Document);

    /// <summary>The rule behind <see cref="AllowsLocalFiles"/>, for a container that has no render tree yet.</summary>
    public static bool LocalFilesAllowed(IBrowserRequestTransport? transport, DocumentRequestContext? document) =>
        document != null ? document.DocumentUrl.IsFile : transport == null;

    /// <summary>Cancelled when the container tears this tree down.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>Stops every load this tree started. Idempotent.</summary>
    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException ex)
        {
            // A registration threw while aborting its request. Tearing the tree down must not fail over it.
            Debug.WriteLine($"[HtmlRenderer] SubresourceScope cancellation callback failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads <paramref name="url"/> through <see cref="Transport"/> for <see cref="Document"/> with HTML's CORS settings
    /// mapping for <paramref name="destination"/>, or answers from the container's cache.
    /// </summary>
    /// <param name="url">The absolute http(s) URL of the subresource.</param>
    /// <param name="destination">Image, style or font.</param>
    /// <param name="crossOrigin">The element's <c>crossorigin</c> setting; fonts are always CORS with same-origin credentials.</param>
    /// <param name="budget">How long the load may take, headers and body together.</param>
    /// <param name="maxBytes">The largest body accepted.</param>
    /// <param name="acceptMediaType">Rejects a response by its media type before its body is read; <see langword="null"/> accepts any.</param>
    /// <param name="acceptResponse">
    /// Rejects a response by its media type, tainting and <c>X-Content-Type-Options</c> before its body is read, for a
    /// destination whose rule depends on more than the media type (a stylesheet); <see langword="null"/> accepts any.
    /// </param>
    /// <returns>The final URL (after redirects), the media type and the body.</returns>
    /// <exception cref="OperationCanceledException">The tree was torn down.</exception>
    /// <exception cref="TimeoutException"><paramref name="budget"/>, or the transport's own timeout, ran out.</exception>
    /// <exception cref="TransportException">A network error, including a failed CORS check.</exception>
    /// <exception cref="HttpRequestException">The response status is not 2xx.</exception>
    /// <exception cref="InvalidDataException">The media type or the size is not acceptable.</exception>
    /// <exception cref="InvalidOperationException">There is no transport, or no document context to send it for.</exception>
    public SubresourceResponse Fetch(
        Uri url,
        RequestDestination destination,
        CorsSetting crossOrigin,
        TimeSpan budget,
        long maxBytes,
        Func<string?, bool>? acceptMediaType = null,
        Func<SubresourceResponseHead, bool>? acceptResponse = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        var transport = Transport ?? throw new InvalidOperationException("This render tree has no request transport.");
        var document = Document ?? throw new InvalidOperationException(
            "RequestTransport is set without a DocumentContext; subresources are not loaded without the document they are for.");

        var context = RequestContext.Subresource(document, destination, crossOrigin);
        var key = new SubresourceCacheKey(url.AbsoluteUri, destination, context.Mode, context.Credentials);
        if (_cache.TryGet(key, out var cached))
        {
            // A cached response meets this caller's checks as a fresh one would: whether a response is
            // acceptable can depend on the requester as well as on the response (a stylesheet's type
            // check depends on the requesting document's mode), and the cache is shared by every
            // container of the document, and across documents through ShareSubresourceCacheWith.
            Accept(cached.Head, destination, acceptMediaType, acceptResponse);
            if (cached.Body.LongLength > maxBytes)
                throw new InvalidDataException($"The response is larger than {maxBytes} bytes.");
            return cached;
        }

        Token.ThrowIfCancellationRequested();

        using var budgetSource = CancellationTokenSource.CreateLinkedTokenSource(Token);
        budgetSource.CancelAfter(budget);
        var token = budgetSource.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = transport.Send(request, context, token);

            // The transport's status is privileged: an opaque (no-cors, cross-origin) image still renders, so its real
            // status decides, not the 0 a script would see.
            if (response.StatusCode is < 200 or > 299)
            {
                throw new HttpRequestException(
                    $"Response status code does not indicate success: {response.StatusCode}.",
                    null,
                    (HttpStatusCode)response.StatusCode);
            }

            var content = response.Message.Content;
            var mediaType = content.Headers.ContentType?.MediaType;
            var head = new SubresourceResponseHead(mediaType, response.Tainting, IsNoSniff(response));
            Accept(head, destination, acceptMediaType, acceptResponse);

            if (content.Headers.ContentLength > maxBytes)
                throw new InvalidDataException($"The response is larger than {maxBytes} bytes.");

            // The transport returns at the response headers, so the body gets what is left of the same budget. A
            // synchronous read cannot observe a token; cancelling disposes the response under it instead.
            using var abortRead = token.Register(response.Dispose);
            using var body = content.ReadAsStream(token);
            var bytes = ReadBounded(body, maxBytes);
            token.ThrowIfCancellationRequested();

            var result = new SubresourceResponse(response.FinalUrl, mediaType, bytes) { Head = head };
            _cache.Add(key, result);
            return result;
        }
        catch (Exception ex) when (Token.IsCancellationRequested)
        {
            if (ex is OperationCanceledException)
                throw;

            // Aborting the response surfaced as something else (an IOException from the read, most often).
            throw new OperationCanceledException("The render tree that started this load was torn down.", ex, Token);
        }
        catch (Exception ex) when (budgetSource.IsCancellationRequested || ex is OperationCanceledException)
        {
            // Out of time, on this load's budget or the transport's own timeout. That is a failed load: only a
            // torn-down tree counts as cancelled, and a cancelled image never reports completion.
            throw new TimeoutException($"Loading {url} did not complete within its time limit.", ex);
        }
    }

    /// <summary>
    /// Applies a caller's checks on a response head: <paramref name="acceptMediaType"/> on its media type,
    /// then <paramref name="acceptResponse"/> on the whole head. Throws <see cref="InvalidDataException"/> for
    /// a response either refuses.
    /// </summary>
    private static void Accept(
        SubresourceResponseHead head,
        RequestDestination destination,
        Func<string?, bool>? acceptMediaType,
        Func<SubresourceResponseHead, bool>? acceptResponse)
    {
        if (acceptMediaType != null && !acceptMediaType(head.MediaType))
            throw new InvalidDataException($"Unexpected content type for {destination}: {head.MediaType}");

        if (acceptResponse != null && !acceptResponse(head))
            throw new InvalidDataException($"The response is not acceptable for {destination}: {head.MediaType}, {head.Tainting}.");
    }

    /// <summary>
    /// The element's CORS settings attribute (<c>crossorigin</c>), looked up case-insensitively.
    /// </summary>
    public static CorsSetting GetCrossOrigin(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes == null)
            return CorsSetting.None;

        if (attributes.TryGetValue("crossorigin", out var value))
            return CorsSettings.Parse(value);

        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, "crossorigin", StringComparison.OrdinalIgnoreCase))
                return CorsSettings.Parse(attribute.Value);
        }

        return CorsSetting.None;
    }

    /// <summary>
    /// Fetch's "determine nosniff": the first value of the response's <c>X-Content-Type-Options</c> fields, split on
    /// commas, is an ASCII case-insensitive match for <c>nosniff</c>.
    /// </summary>
    internal static bool IsNoSniff(TransportResponse response)
    {
        foreach (var header in response.Headers)
        {
            if (!string.Equals(header.Key, "X-Content-Type-Options", StringComparison.OrdinalIgnoreCase))
                continue;

            var comma = header.Value.IndexOf(',');
            var first = (comma < 0 ? header.Value : header.Value[..comma]).Trim(' ', '\t');
            return string.Equals(first, "nosniff", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static byte[] ReadBounded(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException($"The response is larger than {maxBytes} bytes.");

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

/// <summary>What a subresource response says about itself before its body is read.</summary>
/// <param name="MediaType">The media type of its <c>Content-Type</c>, if it declared one.</param>
/// <param name="Tainting">
/// The response's tainting: <see cref="ResponseTainting.Basic"/> and <see cref="ResponseTainting.Cors"/> are HTML's
/// "CORS-same-origin", the only responses whose content the document is entitled to read.
/// </param>
/// <param name="NoSniff">Whether the response carried <c>X-Content-Type-Options: nosniff</c>.</param>
internal readonly record struct SubresourceResponseHead(string? MediaType, ResponseTainting Tainting, bool NoSniff);

/// <summary>A subresource loaded through the host's transport.</summary>
/// <param name="FinalUrl">The URL of the final response, after redirects; relative URLs in a stylesheet resolve against it.</param>
/// <param name="MediaType">The response's media type, if it declared one.</param>
/// <param name="Body">The body. Shared through the cache, so never written to.</param>
internal sealed record SubresourceResponse(Uri FinalUrl, string? MediaType, byte[] Body)
{
    /// <summary>
    /// What the response said about itself, kept with it so a later request answered from the cache is
    /// checked against it as the first one was.
    /// </summary>
    public SubresourceResponseHead Head { get; init; } = new(MediaType, ResponseTainting.Basic, NoSniff: false);

    /// <summary>A read-only stream over <see cref="Body"/>.</summary>
    public MemoryStream OpenBody() => new(Body, writable: false);
}
