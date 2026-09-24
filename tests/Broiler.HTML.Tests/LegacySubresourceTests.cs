using Broiler.Net.Http;

namespace Broiler.HTML.Tests;

/// <summary>
/// Without a transport (HtmlRender, the command-line tool, the WPT runner) a container loads through process-wide
/// clients. Those used to keep automatic cookie containers: hidden, unpartitioned jars shared by every document in
/// the process. They must send and store no cookies at all.
/// </summary>
public sealed class LegacySubresourceTests
{
    [Fact]
    public void NoTransportSendsNoCookiesAcrossContainersEvenAfterSetCookie()
    {
        using var server = new LoopbackHttpServer(request => request.Path switch
        {
            "/a.css" or "/b.css" => LoopbackResponse.Text("text/css", "#box { width: 3px; height: 3px; }", ("Set-Cookie", "sheet=1; Path=/")),
            "/a.woff2" or "/b.woff2" => LoopbackResponse.Ok("font/woff2", TestContent.Font, ("Set-Cookie", "font=1; Path=/")),
            "/a.png" or "/b.png" => LoopbackResponse.Ok("image/png", TestContent.Png, ("Set-Cookie", "image=1; Path=/")),
            _ => LoopbackResponse.NotFound(),
        });

        // A query unique to this run: the legacy image path still caches in %TEMP%\HtmlRenderer by URL.
        var run = Guid.NewGuid().ToString("N");
        string Html(string name, int pass) => $$"""
            <link rel="stylesheet" href="{{server.Url($"/{name}.css?{run}-{pass}")}}">
            <style>@font-face { font-family: Probe{{pass}}; src: url({{server.Url($"/{name}.woff2?{run}-{pass}")}}); }</style>
            <div id="box"></div><img src="{{server.Url($"/{name}.png?{run}-{pass}")}}">
            """;
        var page = server.Url("/page");

        using (var first = TestContent.Container())
        {
            Assert.Empty(TestContent.Render(first, Html("a", 1), page));
            Assert.Equal(3, first.GetElementRectangle("box")!.Value.Width);
            Assert.Empty(TestContent.Render(first, Html("a", 2), page));
        }

        using (var second = TestContent.Container())
            Assert.Empty(TestContent.Render(second, Html("b", 1), page));

        var requests = server.Requests;
        Assert.Equal(9, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Null(request.Cookie);
            Assert.Equal(BroilerUserAgent.Value, request.Header("User-Agent"));
        });
    }
}
