using System.Collections.Concurrent;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using Broiler.Net.Http;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Broiler.HTML.Tests;

/// <summary>One <see cref="IBrowserRequestTransport.Send"/> call and how it ended.</summary>
internal sealed class TransportCall(Uri url, RequestContext context, CancellationToken token)
{
    private readonly TaskCompletionSource<Exception?> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri Url { get; } = url;
    public RequestContext Context { get; } = context;

    /// <summary>The token the container handed the transport.</summary>
    public CancellationToken Token { get; } = token;

    public int? StatusCode { get; private set; }

    /// <summary>Completes with the exception the call threw, or <see langword="null"/> when it returned a response.</summary>
    public Task<Exception?> Outcome => _outcome.Task;

    internal void Returned(int status)
    {
        StatusCode = status;
        _outcome.TrySetResult(null);
    }

    internal void Threw(Exception error) => _outcome.TrySetResult(error);
}

/// <summary>Passes every request to a real session and records what the container asked for and what came of it.</summary>
internal sealed class RecordingTransport(IBrowserRequestTransport inner) : IBrowserRequestTransport
{
    private readonly ConcurrentQueue<TransportCall> _calls = new();

    public IReadOnlyList<TransportCall> Calls => _calls.ToArray();

    public TransportCall Single(string path)
    {
        var matching = _calls.Where(c => c.Url.AbsolutePath == path).ToArray();
        Assert.True(matching.Length == 1, $"Expected one transport call for {path}, got {matching.Length}.");
        return matching[0];
    }

    public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default) =>
        inner.SendAsync(request, context, cancellationToken);

    public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default)
    {
        var call = new TransportCall(request.RequestUri!, context, cancellationToken);
        _calls.Enqueue(call);
        try
        {
            var response = inner.Send(request, context, cancellationToken);
            call.Returned(response.StatusCode);
            return response;
        }
        catch (Exception error)
        {
            call.Threw(error);
            throw;
        }
    }
}

internal static class TestContent
{
    /// <summary>A 1x1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    /// <summary>Bytes served as a font. The renderer's parser rejects them, which a load through the network does not see.</summary>
    public static readonly byte[] Font = "wOF2 not really a font"u8.ToArray();

    /// <summary>A container that loads images synchronously during layout, as the Broiler browser's does.</summary>
    public static HtmlContainer Container(IBrowserRequestTransport? transport = null, DocumentRequestContext? document = null) => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        RequestTransport = transport,
        DocumentContext = document,
    };

    /// <summary>Parses and lays out <paramref name="html"/>, returning the render errors it reported.</summary>
    public static List<HtmlRenderErrorType> Render(HtmlContainer container, string html, string baseUrl)
    {
        var errors = new List<HtmlRenderErrorType>();
        void OnError(object? sender, HtmlRenderErrorEventArgs e)
        {
            lock (errors)
                errors.Add(e.Type);
        }

        container.RenderError += OnError;
        try
        {
            container.SetHtmlWithStyleSet(html, null, baseUrl);
            container.PerformLayout();
        }
        finally
        {
            container.RenderError -= OnError;
        }

        return errors;
    }

    /// <summary>Stores the profile cookie the way a real one arrives: on a top-level navigation response.</summary>
    public static void Navigate(IBrowserRequestTransport session, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = session.Send(request, RequestContext.TopLevelNavigation(null));
        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>The cookies the store holds for <paramref name="document"/>, as document.cookie would read them.</summary>
    public static string DocumentCookies(BrowserNetworkSession session, DocumentRequestContext document)
    {
        Assert.True(session.TryGetCookie(document, out var cookies));
        return cookies;
    }
}
