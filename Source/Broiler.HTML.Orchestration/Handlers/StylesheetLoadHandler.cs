using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace Broiler.HTML.Orchestration.Handlers;

internal sealed class StylesheetLoadHandler : IStylesheetLoader
{
    private readonly HtmlContainerInt _htmlContainer;

    public StylesheetLoadHandler(HtmlContainerInt htmlContainer)
    {
        ArgumentNullException.ThrowIfNull(htmlContainer);
        _htmlContainer = htmlContainer;
    }

    public void LoadStylesheet(string src, Dictionary<string, string> attributes, out string stylesheet, out Broiler.CSS.CssStyleSheet styleSheet)
    {
        stylesheet = null;
        styleSheet = null;

        try
        {
            var args = new HtmlStylesheetLoadEventArgs(src, attributes);
            _htmlContainer.RaiseHtmlStylesheetLoadEvent(args);

            if (!string.IsNullOrEmpty(args.SetStyleSheet))
            {
                stylesheet = args.SetStyleSheet;
            }
            else if (args.SetStyleSheetModel != null)
            {
                styleSheet = args.SetStyleSheetModel;
            }
#pragma warning disable CS0618
            else if (args.SetStyleSheetData != null)
            {
                styleSheet = args.SetStyleSheetData.StyleSheet;
            }
#pragma warning restore CS0618
            else if (args.SetSrc != null)
            {
                stylesheet = LoadStylesheet(args.SetSrc);
            }
            else
            {
                stylesheet = LoadStylesheet(src);
            }
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }
    }


    private string LoadStylesheet(string src)
    {
        // Handle data: URIs (e.g. data:text/css,.picture%20%7B...%7D)
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return LoadStylesheetFromDataUri(src);
        }

        src = ResolveStylesheetSource(src);
        var uri = CommonUtils.TryGetUri(src);

        if (uri == null || !uri.IsAbsoluteUri)
        {
            return LoadStylesheetFromFile(src);
        }
        else if (uri.Scheme == "file")
        {
            return LoadStylesheetFromFile(uri.LocalPath);
        }
        else
        {
            // The host declines URLs it knows cannot resolve to anything (Broiler.Layout's
            // OfflineSubresources; unset for a renderer with a network, so this is inert there).
            // The cap below bounds an unreachable sheet, it does not make one free: a conformance
            // run whose corpus is a directory on disk still spends five seconds per off-corpus
            // <link> being told what it already knew, and a document with three of them —
            // css/CSS2/cascade-import/cascade-import-009.xht links a CGI that pauses 2, 5 and 8
            // seconds by design — spends half a thirty-second budget in this one method, once per
            // container the document is laid out in.
            if (Broiler.Layout.Engine.OfflineSubresources.Denies(src))
                return string.Empty;

            return LoadStylesheetFromUri(uri);
        }
    }

    private string ResolveStylesheetSource(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return src;

        // Only a src that is absolute *and carries a real scheme* is already resolved. The
        // UriKind.Absolute test alone is not that question on Unix: there, "/style.css" parses
        // absolute, as file:///style.css, so a root-relative href looked already-resolved, was
        // never rebased on the document URL, and was then read off the local filesystem instead of
        // being fetched from the page's origin — the sheet silently did not apply (www.7-zip.org
        // links its stylesheet that way). On Windows the same call returns false, which is why the
        // page styled correctly there. A genuine file: URL still resolves as before, because the
        // rebase below leaves an absolute file: src unchanged.
        if (Uri.TryCreate(src, UriKind.Absolute, out var absolute)
            && absolute.Scheme != Uri.UriSchemeFile)
            return src;

        if (!string.IsNullOrWhiteSpace(_htmlContainer.BaseUrl) &&
            Uri.TryCreate(_htmlContainer.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, src).AbsoluteUri;
        }

        return src;
    }

    /// <summary>
    /// Extracts CSS content from a <c>data:</c> URI.
    /// Supports both percent-encoded (<c>data:text/css,.picture%20%7B...%7D</c>)
    /// and base64-encoded (<c>data:text/css;base64,...</c>) payloads.
    /// </summary>
    private static string LoadStylesheetFromDataUri(string src)
    {
        // Format: data:[<mediatype>][;base64],<data>
        var commaIdx = src.IndexOf(',');
        if (commaIdx < 0)
            return string.Empty;

        var meta = src.Substring(5, commaIdx - 5); // between "data:" and ","
        var payload = src.Substring(commaIdx + 1);

        if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Convert.FromBase64String(payload);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        return Uri.UnescapeDataString(payload);
    }

    private string LoadStylesheetFromFile(string path)
    {
        var fileInfo = CommonUtils.TryGetFileInfo(path);
        if (fileInfo != null)
        {
            if (fileInfo.Exists)
            {
                using var sr = new StreamReader(fileInfo.FullName);
                return sr.ReadToEnd();
            }
            else
            {
                _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
            }
        }
        else
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }

        return string.Empty;
    }

    // Synchronous external stylesheet fetch on the render path.  The default
    // HttpClient timeout is 100 s, so a single unreachable http(s) <link> (the
    // norm in the sandboxed WPT/headless environment) blocks the render far past
    // any per-test budget — a hang, not a render.  Cap it short so an
    // unreachable sheet fails fast and the page renders without it (WPT #1147
    // Timeout cluster, e.g. CSS2/cascade-import-* linking delayed-file CGI URLs).
    //
    // Identified, too: HttpClient sends no User-Agent unless given one, and a host that refuses an
    // unidentified request refuses the sheet rather than serving a different one — the load.php
    // stylesheets of a mediawiki.org page came back 403 Forbidden, so the page rendered unstyled.
    private static readonly HttpClient SharedHttpClient =
        Broiler.Layout.Net.BroilerUserAgent.Apply(new HttpClient { Timeout = TimeSpan.FromSeconds(5) });

    private string LoadStylesheetFromUri(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = SharedHttpClient.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);
        var stylesheet = reader.ReadToEnd();

        try
        {
            stylesheet = CorrectRelativeUrls(stylesheet, uri);
        }
        catch (Exception ex)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }

        return stylesheet;
    }

    private static string CorrectRelativeUrls(string stylesheet, Uri baseUri)
    {
        int idx = 0;
        while (idx >= 0 && idx < stylesheet.Length)
        {
            idx = stylesheet.IndexOf("url(", idx, StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                continue;

            int endIdx = stylesheet.IndexOf(')', idx);

            if (endIdx > idx + 4)
            {
                var offset1 = 4 + (stylesheet[idx + 4] == '\'' ? 1 : 0);
                var offset2 = stylesheet[endIdx - 1] == '\'' ? 1 : 0;
                var urlStr = stylesheet.Substring(idx + offset1, endIdx - idx - offset1 - offset2);

                if (Uri.TryCreate(urlStr, UriKind.Relative, out Uri url))
                {
                    url = new Uri(baseUri, url);
                    stylesheet = stylesheet.Remove(idx + 4, endIdx - idx - 4);
                    stylesheet = stylesheet.Insert(idx + 4, url.AbsoluteUri);
                    idx += url.AbsoluteUri.Length + 4;
                }
                else
                {
                    idx = endIdx + 1;
                }
            }
            else
            {
                idx += 4;
            }
        }

        return stylesheet;
    }
}
