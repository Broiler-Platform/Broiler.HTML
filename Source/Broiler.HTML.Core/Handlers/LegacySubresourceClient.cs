using System;
using System.Net.Http;
using Broiler.Net.Http;

namespace Broiler.HTML.Core.Handlers;

/// <summary>
/// Builds the process-wide clients a container loads images, stylesheets and fonts through when its host supplies no
/// <see cref="IBrowserRequestTransport"/>: <c>HtmlRender</c>, the command-line tool, the WPT runner and tests.
/// </summary>
/// <remarks>
/// They follow redirects and carry the Broiler User-Agent as before, but never manage cookies. A default
/// <see cref="HttpClientHandler"/> keeps an automatic cookie container, which made each of these static clients a
/// hidden jar shared by every document in the process, whatever profile or site it belonged to. Cookies belong to
/// the host's transport, and a host without one sends none.
/// </remarks>
internal static class LegacySubresourceClient
{
    /// <summary>A client with <paramref name="timeout"/> and no cookie handling. Owned by the process; never disposed.</summary>
    public static HttpClient Create(TimeSpan timeout) =>
        BroilerUserAgent.Apply(new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = timeout });
}
