using Broiler.HTML.Core.Entities;
using Broiler.Net.Http;

namespace Broiler.HTML.Tests;

/// <summary>
/// A container given the profile's <see cref="IBrowserRequestTransport"/> and its document's
/// <see cref="DocumentRequestContext"/> loads images, link stylesheets and fonts through that transport, as Fetch
/// subresource requests of that document. Every test here runs a real <see cref="BrowserNetworkSession"/> against a
/// loopback server and reads the cookies off the wire.
/// </summary>
public sealed class SubresourceTransportTests
{
    private const string Profile = "sid=profile";

    private static LoopbackResponse Login() =>
        LoopbackResponse.Text("text/html", "<p>signed in</p>", ("Set-Cookie", Profile + "; Path=/"));

    [Fact]
    public void ImageStylesheetAndFontRequestsCarryTheProfileCookieAndStoreTheirSetCookie()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/login" => Login(),
            "/style.css" => LoopbackResponse.Text("text/css", "#box { width: 123px; height: 7px; }", ("Set-Cookie", "css=1; Path=/")),
            "/font.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font, ("Set-Cookie", "font=1; Path=/")),
            "/img.png" => LoopbackResponse.Ok("image/png", TestContent.Png, ("Set-Cookie", "img=1; Path=/")),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        TestContent.Navigate(session, server.Url("/login"));
        var page = server.Url("/page");
        var document = DocumentRequestContext.CreateTopLevel(new Uri(page));
        using var container = TestContent.Container(session, document);

        var errors = TestContent.Render(container, """
            <html><head>
            <link rel="stylesheet" href="/style.css">
            <style>@font-face { font-family: Probe; src: url(/font.woff2); }</style>
            </head><body><div id="box"></div><img src="/img.png"></body></html>
            """, page);

        Assert.Empty(errors);
        Assert.Equal(123, container.GetElementRectangle("box")!.Value.Width);

        // The sheet loads while the tree is built, the font right after it and the image during layout, so the
        // image request also shows that what the first two responses stored went out with the next request.
        Assert.Equal(Profile, server.Single("/style.css").Cookie);
        Assert.Equal(Profile + "; css=1", server.Single("/font.woff2").Cookie);
        Assert.Equal(Profile + "; css=1; font=1", server.Single("/img.png").Cookie);

        var cookies = TestContent.DocumentCookies(session, document);
        Assert.Contains("css=1", cookies);
        Assert.Contains("font=1", cookies);
        Assert.Contains("img=1", cookies);
    }

    [Fact]
    public void RequestsUseHtmlsCorsSettingsMapping()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/login" => Login(),
            "/style.css" => LoopbackResponse.Text("text/css", "p { color: red; }"),
            "/font.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            "/img.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            "/cors.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var recording = new RecordingTransport(session);
        var page = server.Url("/page");
        var document = DocumentRequestContext.CreateTopLevel(new Uri(page));
        using var container = TestContent.Container(recording, document);

        TestContent.Render(container, """
            <link rel="stylesheet" href="/style.css">
            <style>@font-face { font-family: Probe; src: url(/font.woff2); }</style>
            <img src="/img.png"><img crossorigin="use-credentials" src="/cors.png">
            """, page);

        AssertContext(recording.Single("/style.css"), RequestDestination.Style, RequestMode.NoCors, CredentialsMode.Include);
        AssertContext(recording.Single("/font.woff2"), RequestDestination.Font, RequestMode.Cors, CredentialsMode.SameOrigin);
        AssertContext(recording.Single("/img.png"), RequestDestination.Image, RequestMode.NoCors, CredentialsMode.Include);
        AssertContext(recording.Single("/cors.png"), RequestDestination.Image, RequestMode.Cors, CredentialsMode.Include);

        void AssertContext(TransportCall call, RequestDestination destination, RequestMode mode, CredentialsMode credentials)
        {
            Assert.Equal(destination, call.Context.Destination);
            Assert.Equal(mode, call.Context.Mode);
            Assert.Equal(credentials, call.Context.Credentials);
            Assert.Same(document, call.Context.Client);
        }
    }

    [Fact]
    public void CrossOriginAnonymousImageCarriesNoCookieAndStoresNone()
    {
        using var site = new LoopbackHttpServer(request => request.Path == "/login" ? Login() : LoopbackResponse.NotFound());

        // Another port of the same host: cross-origin but same-site, so only the credentials mode keeps the
        // cookie off the anonymous request.
        using var other = new LoopbackHttpServer(request => request.Path switch
        {
            "/plain.png" => LoopbackResponse.Ok("image/png", TestContent.Png, ("Set-Cookie", "plain=1; Path=/")),
            "/anonymous.png" => LoopbackResponse.Ok("image/png", TestContent.Png,
                ("Access-Control-Allow-Origin", "*"), ("Set-Cookie", "anonymous=1; Path=/")),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        TestContent.Navigate(session, site.Url("/login"));
        var page = site.Url("/page");
        var document = DocumentRequestContext.CreateTopLevel(new Uri(page));
        using var container = TestContent.Container(session, document);

        var errors = TestContent.Render(container, $"""
            <img src="{other.Url("/plain.png")}">
            <img crossorigin="anonymous" src="{other.Url("/anonymous.png")}">
            """, page);

        Assert.Empty(errors);
        Assert.Equal(Profile, other.Single("/plain.png").Cookie);

        var anonymous = other.Single("/anonymous.png");
        Assert.Null(anonymous.Cookie);
        Assert.Equal(site.Origin, anonymous.Header("Origin"));

        var cookies = TestContent.DocumentCookies(session, document);
        Assert.Contains("plain=1", cookies);
        Assert.DoesNotContain("anonymous=1", cookies);
    }

    [Fact]
    public async Task CrossOriginFontsNeedCorsAndNeverCarryCookies()
    {
        using var site = new LoopbackHttpServer(request => request.Path == "/login" ? Login() : LoopbackResponse.NotFound());
        using var fonts = new LoopbackHttpServer(request => request.Path switch
        {
            "/login" => LoopbackResponse.Text("text/html", "<p>other site</p>", ("Set-Cookie", "other=1; Path=/")),
            "/closed.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            "/open.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font, ("Access-Control-Allow-Origin", site.Origin)),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        TestContent.Navigate(session, site.Url("/login"));

        // localhost is another site than 127.0.0.1, and holds a cookie of its own that a font must not carry.
        TestContent.Navigate(session, fonts.LocalhostUrl("/login"));
        var recording = new RecordingTransport(session);
        var page = site.Url("/page");
        using var container = TestContent.Container(recording, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        TestContent.Render(container, $$"""
            <style>
            @font-face { font-family: Closed; src: url({{fonts.LocalhostUrl("/closed.woff2")}}); }
            @font-face { font-family: Open; src: url({{fonts.LocalhostUrl("/open.woff2")}}); }
            </style>
            <p style="font-family: Closed">closed</p><p style="font-family: Open">open</p>
            """, page);

        var closed = recording.Single("/closed.woff2");
        var open = recording.Single("/open.woff2");
        Assert.Equal(TransportError.Cors, Assert.IsType<TransportException>(await closed.Outcome).Error);
        Assert.Null(await open.Outcome);
        Assert.Equal(200, open.StatusCode);

        foreach (var path in new[] { "/closed.woff2", "/open.woff2" })
        {
            var request = fonts.Single(path);
            Assert.Null(request.Cookie);
            Assert.Equal(site.Origin, request.Header("Origin"));
        }

        // Only the font that passed the CORS check reached the renderer (and its cache).
        Assert.Equal(1, container.HtmlContainerInt.CachedSubresourceCount);
    }

    [Fact]
    public void RedirectedStylesheetRebasesItsUrlsOnTheFinalUrl()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/old/style.css" => LoopbackResponse.Redirect("/sub/style.css"),
            "/sub/style.css" => LoopbackResponse.Text("text/css",
                "@font-face { font-family: Moved; src: url(font.woff2); } #box { width: 5px; height: 5px; background-image: url(bg.png); }"),
            "/sub/font.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            "/sub/bg.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        TestContent.Render(container, """
            <link rel="stylesheet" href="/old/style.css"><div id="box" style="font-family: Moved">x</div>
            """, page);

        server.Single("/sub/font.woff2");
        Assert.Empty(server.RequestsFor("/old/font.woff2"));
        Assert.Empty(server.RequestsFor("/old/bg.png"));
    }

    [Fact]
    public void CacheSurvivesReparsesAndIsDroppedWhenTheDocumentChanges()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/style.css" => LoopbackResponse.Text("text/css", "#box { width: 9px; height: 9px; }"),
            "/font.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            "/img.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));
        const string html = """
            <link rel="stylesheet" href="/style.css">
            <style>@font-face { font-family: Probe; src: url(/font.woff2); }</style>
            <div id="box"></div><img src="/img.png">
            """;

        TestContent.Render(container, html, page);
        TestContent.Render(container, html, page);
        AssertRequestCount(1);
        Assert.Equal(9, container.GetElementRectangle("box")!.Value.Width);

        // A navigation that reuses the container: the old document's responses must not answer the new one.
        container.DocumentContext = DocumentRequestContext.CreateTopLevel(new Uri(page));
        TestContent.Render(container, html, page);
        AssertRequestCount(2);

        void AssertRequestCount(int expected)
        {
            foreach (var path in new[] { "/style.css", "/font.woff2", "/img.png" })
                Assert.Equal(expected, server.RequestsFor(path).Length);
        }
    }

    [Fact]
    public void TransportWithoutDocumentContextLoadsNothing()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/style.css" => LoopbackResponse.Text("text/css", "p { color: red; }"),
            "/font.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            "/img.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, document: null);

        var errors = TestContent.Render(container, """
            <link rel="stylesheet" href="/style.css">
            <style>@font-face { font-family: Probe; src: url(/font.woff2); }</style>
            <img src="/img.png">
            """, page);

        // No request at all: a base URL is not a document identity, and the legacy clients are not a fallback.
        Assert.Empty(server.Requests);
        Assert.Contains(HtmlRenderErrorType.CssParsing, errors);
        Assert.Contains(HtmlRenderErrorType.Image, errors);
    }

    [Fact]
    public void HostLoadEventsStillComeBeforeTheTransport()
    {
        using var server = new LoopbackHttpServer(_ => LoopbackResponse.NotFound());
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));
        container.StylesheetLoad += (_, e) => e.SetStyleSheet = "#box { width: 42px; height: 1px; }";
        container.ImageLoad += (_, e) => e.Handled = true;

        TestContent.Render(container, """
            <link rel="stylesheet" href="/style.css"><div id="box"></div><img src="/img.png">
            """, page);

        Assert.Empty(server.Requests);
        Assert.Equal(42, container.GetElementRectangle("box")!.Value.Width);
    }
}
