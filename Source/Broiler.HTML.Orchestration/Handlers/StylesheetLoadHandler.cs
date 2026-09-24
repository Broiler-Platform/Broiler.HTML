using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.Handlers;
using Broiler.HTML.Core.Utils;
using Broiler.Net.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Broiler.HTML.Orchestration.Handlers;

internal sealed class StylesheetLoadHandler : IStylesheetLoader
{
    private readonly HtmlContainerInt _htmlContainer;

    public StylesheetLoadHandler(HtmlContainerInt htmlContainer)
    {
        ArgumentNullException.ThrowIfNull(htmlContainer);
        _htmlContainer = htmlContainer;
    }

    public void LoadStylesheet(string src, Dictionary<string, string> attributes, out string? stylesheet, out Broiler.CSS.CssStyleSheet? styleSheet)
    {
        stylesheet = null;
        styleSheet = null;

        // The tree being built: its transport, its document and the cancellation of its loads.
        var scope = _htmlContainer.SubresourceScope;

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
                stylesheet = LoadStylesheet(args.SetSrc, attributes, scope);
            }
            else
            {
                stylesheet = LoadStylesheet(src, attributes, scope);
            }
        }
        catch (OperationCanceledException) when (scope?.Token.IsCancellationRequested == true)
        {
            // The tree this sheet was for was torn down; that is not a CSS error.
        }
        catch (Exception)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }
    }


    private string LoadStylesheet(string src, Dictionary<string, string> attributes, SubresourceScope? scope)
    {
        // Handle data: URIs (e.g. data:text/css,.picture%20%7B...%7D)
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return LoadStylesheetFromDataUri(src);
        }

        src = ResolveStylesheetSource(src);

        // An http(s) URL is taken the way a browser's URL parser takes it, which percent-encodes what
        // Uri.IsWellFormedUriString refuses -- the '|' of a Google Fonts v1 family list, an unescaped
        // space -- rather than treated as a local path (and, for a web page, refused as one).
        var uri = Uri.TryCreate(src, UriKind.Absolute, out var parsed) &&
                  (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed
            : CommonUtils.TryGetUri(src);

        if (uri == null || !uri.IsAbsoluteUri || uri.Scheme == "file")
        {
            // A web page's sheet never comes off the file system: see SubresourceScope.AllowsLocalFiles.
            if (scope is { AllowsLocalFiles: false })
            {
                _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
                return string.Empty;
            }

            return LoadStylesheetFromFile(uri is { IsAbsoluteUri: true } ? uri.LocalPath : src);
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

            if (scope is { UsesTransport: true })
                return LoadStylesheetThroughTransport(scope, uri, SubresourceScope.GetCrossOrigin(attributes));

            return LoadStylesheetFromUri(uri, scope?.Token ?? CancellationToken.None);
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

        if (TryGetDocumentBaseUri(out var baseUri))
            return new Uri(baseUri, src).AbsoluteUri;

        return src;
    }

    /// <summary>
    /// The URL a relative stylesheet href resolves against: the document's own
    /// <c>&lt;base href&gt;</c> when it declared one (HTML §4.2.3, published by the parse
    /// before it collects the sheets), otherwise the document URL the embedder supplied.
    /// </summary>
    private bool TryGetDocumentBaseUri([NotNullWhen(true)] out Uri? baseUri)
    {
        baseUri = null;
        if (_htmlContainer.DocumentBaseUrl is { IsAbsoluteUri: true } documentBaseUrl)
        {
            baseUri = documentBaseUrl;
            return true;
        }

        return !string.IsNullOrWhiteSpace(_htmlContainer.BaseUrl)
            && Uri.TryCreate(_htmlContainer.BaseUrl, UriKind.Absolute, out baseUri);
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
    //
    // Used only when the host supplies no transport, and without cookies (see LegacySubresourceClient).
    private static readonly HttpClient SharedHttpClient = LegacySubresourceClient.Create(TimeSpan.FromSeconds(5));

    // The same five seconds for a sheet loaded through the host's transport.
    private static readonly TimeSpan TransportBudget = SharedHttpClient.Timeout;

    // A bound on a body the transport path holds in memory (and may cache); no real sheet comes near it.
    private const long MaxStylesheetBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Loads a <c>&lt;link&gt;</c> sheet through the host's transport as a style request for the container's
    /// document, with the element's CORS setting, and rebases its relative <c>url()</c> references on the
    /// response's final URL: after a redirect that is where the sheet lives, not where it was asked for.
    /// </summary>
    private string LoadStylesheetThroughTransport(SubresourceScope scope, Uri uri, CorsSetting crossOrigin)
    {
        // Read here, on the thread that is parsing the document: the flag is the parse's.
        var quirksMode = Broiler.CSS.CssDocumentMode.QuirksMode;
        var response = scope.Fetch(
            uri,
            RequestDestination.Style,
            crossOrigin,
            TransportBudget,
            MaxStylesheetBytes,
            acceptResponse: head => IsAcceptableStyleSheet(head, quirksMode));

        string stylesheet;
        using (var reader = new StreamReader(response.OpenBody()))
            stylesheet = reader.ReadToEnd();

        try
        {
            stylesheet = CorrectRelativeUrls(stylesheet, response.FinalUrl);
        }
        catch (Exception)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }

        return stylesheet;
    }

    /// <summary>
    /// Whether a <c>&lt;link&gt;</c> sheet's response may be applied, per HTML's processing of a
    /// stylesheet link and Fetch's nosniff check: its <c>Content-Type</c> must be <c>text/css</c>. The one
    /// exception is a quirks-mode document reading a CORS-same-origin response (basic or CORS tainting),
    /// whose type is ignored; <c>X-Content-Type-Options: nosniff</c> removes even that.
    /// </summary>
    /// <remarks>
    /// The rule is what keeps a page from reading another site's credentialed HTML or JSON as CSS: a
    /// <c>&lt;link&gt;</c> without <c>crossorigin</c> is a no-cors request that carries the user's
    /// cookies, and a body parsed as a sheet leaks its text through the rules it happens to form
    /// (a <c>url()</c> the attacker opened around a secret is then fetched with the secret in it).
    /// </remarks>
    internal static bool IsAcceptableStyleSheet(SubresourceResponseHead head, bool quirksMode)
    {
        if (string.Equals(head.MediaType?.Trim(), "text/css", StringComparison.OrdinalIgnoreCase))
            return true;

        return quirksMode && !head.NoSniff && head.Tainting is ResponseTainting.Basic or ResponseTainting.Cors;
    }

    private string LoadStylesheetFromUri(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = SharedHttpClient.Send(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);
        var stylesheet = reader.ReadToEnd();

        try
        {
            stylesheet = CorrectRelativeUrls(stylesheet, uri);
        }
        catch (Exception)
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing);
        }

        return stylesheet;
    }

    /// <summary>
    /// Rebases every relative <c>url()</c> reference in <paramref name="stylesheet"/> on
    /// <paramref name="baseUri"/>, the URL the sheet was served from, so the references still resolve
    /// once the sheet's text is merged into the document's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet is read the way CSS Syntax tokenizes it, as far as a <c>url()</c> is concerned:
    /// comments and strings are skipped, so a <c>url(</c> inside either is text and not a reference;
    /// <c>url</c> is only a function name when it is a whole identifier. A quoted argument --
    /// <c>url("font.woff2")</c>, the usual form of an <c>@font-face</c> source -- ends at its closing
    /// quote; one that meets an unescaped newline first is a bad string, and the reference is left as
    /// it is. An unquoted argument ends at <c>)</c>; one that meets a quote, a <c>(</c>, or whitespace
    /// before the <c>)</c> is a bad URL, which CSS consumes up to its <c>)</c> and ignores, and which
    /// is left as it is too. Whitespace around the argument is kept.
    /// </para>
    /// <para>
    /// CSS escapes in the argument are decoded before it is resolved, and the rebased URL is written
    /// back in the argument's own form -- quoted with the same quote, or unquoted -- with whatever that
    /// form needs escaped escaped, so the result cannot end the string or the function early. Absolute
    /// URLs (<c>data:</c> included) are left as they are.
    /// </para>
    /// </remarks>
    internal static string CorrectRelativeUrls(string stylesheet, Uri baseUri)
    {
        var result = new System.Text.StringBuilder(stylesheet.Length);
        var copied = 0;
        var i = 0;

        while (i < stylesheet.Length)
        {
            var c = stylesheet[i];

            // A comment: nothing in it is a token.
            if (c == '/' && i + 1 < stylesheet.Length && stylesheet[i + 1] == '*')
            {
                var endComment = stylesheet.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = endComment < 0 ? stylesheet.Length : endComment + 2;
                continue;
            }

            // A string outside url(): its text is not a reference.
            if (c is '"' or '\'')
            {
                i = ReadString(stylesheet, i, out _, out _);
                continue;
            }

            // An escape outside a string is part of an identifier; it cannot start url(.
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (!IsUrlFunctionAt(stylesheet, i))
            {
                i++;
                continue;
            }

            var argument = i + 4;
            while (argument < stylesheet.Length && IsCssWhitespace(stylesheet[argument]))
                argument++;

            if (argument >= stylesheet.Length)
                break;

            var quote = stylesheet[argument];
            if (quote is '"' or '\'')
            {
                // url("...") is a function with a string argument.
                var afterString = ReadString(stylesheet, argument, out var value, out var bad);
                if (!bad && TryRebase(value, baseUri, out var rebased))
                {
                    result.Append(stylesheet, copied, argument - copied);
                    result.Append(quote).Append(EscapeForString(rebased, quote)).Append(quote);
                    copied = afterString;
                }

                i = afterString;
                continue;
            }

            // An unquoted url( is a single url token.
            var end = ReadUnquotedUrl(stylesheet, argument, out var raw, out var badUrl);
            if (!badUrl)
            {
                var valueEnd = argument + raw.Length;
                if (TryRebase(DecodeEscapes(raw), baseUri, out var rebased))
                {
                    result.Append(stylesheet, copied, argument - copied);
                    result.Append(EscapeForUnquotedUrl(rebased));
                    copied = valueEnd;
                }
            }

            i = end;
        }

        result.Append(stylesheet, copied, stylesheet.Length - copied);
        return result.ToString();
    }

    private static bool TryRebase(string value, Uri baseUri, out string rebased)
    {
        rebased = string.Empty;
        if (value.Length == 0 || !Uri.TryCreate(value, UriKind.Relative, out var relative))
            return false;

        rebased = new Uri(baseUri, relative).AbsoluteUri;
        return true;
    }

    /// <summary>Whether an identifier <c>url</c> immediately followed by <c>(</c> starts at <paramref name="i"/>.</summary>
    private static bool IsUrlFunctionAt(string text, int i)
    {
        if (i + 4 > text.Length ||
            string.Compare(text, i, "url(", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        // A whole identifier: not the tail of a longer name (my-url(), \75rl()).
        if (i == 0)
            return true;

        var before = text[i - 1];
        return !(char.IsLetterOrDigit(before) || before is '-' or '_' or '\\' || before > 0x7F);
    }

    /// <summary>
    /// Reads a CSS string starting at the quote at <paramref name="start"/>; answers the index after it,
    /// its decoded value, and whether it was a bad string (an unescaped newline before the closing quote,
    /// which ends it without consuming the newline).
    /// </summary>
    private static int ReadString(string text, int start, out string value, out bool bad)
    {
        var quote = text[start];
        var decoded = new System.Text.StringBuilder();
        var i = start + 1;
        bad = false;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == quote)
            {
                value = decoded.ToString();
                return i + 1;
            }

            if (c is '\n' or '\r' or '\f')
            {
                bad = true;
                value = decoded.ToString();
                return i;
            }

            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    i++;
                    continue;
                }

                // An escaped newline continues the string and adds nothing to it.
                if (text[i + 1] is '\n' or '\f')
                {
                    i += 2;
                    continue;
                }

                if (text[i + 1] == '\r')
                {
                    i += i + 2 < text.Length && text[i + 2] == '\n' ? 3 : 2;
                    continue;
                }

                i = ReadEscape(text, i, decoded);
                continue;
            }

            decoded.Append(c);
            i++;
        }

        // End of the sheet inside a string: CSS closes it there.
        value = decoded.ToString();
        return i;
    }

    /// <summary>
    /// Reads an unquoted url token's argument from <paramref name="start"/> (just past any leading
    /// whitespace); answers the index after its <c>)</c>, the raw argument without trailing whitespace,
    /// and whether it was a bad URL.
    /// </summary>
    private static int ReadUnquotedUrl(string text, int start, out string raw, out bool bad)
    {
        var i = start;
        var valueEnd = start;
        bad = false;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == ')')
            {
                raw = text[start..valueEnd];
                return i + 1;
            }

            if (IsCssWhitespace(c))
            {
                var next = i;
                while (next < text.Length && IsCssWhitespace(text[next]))
                    next++;

                if (next >= text.Length || text[next] == ')')
                {
                    i = next;
                    continue;
                }

                bad = true;
                break;
            }

            if (c is '"' or '\'' or '(' || char.IsControl(c))
            {
                bad = true;
                break;
            }

            if (c == '\\')
            {
                if (i + 1 >= text.Length || text[i + 1] is '\n' or '\r' or '\f')
                {
                    bad = true;
                    break;
                }

                i += 2;
                valueEnd = i;
                continue;
            }

            i++;
            valueEnd = i;
        }

        raw = text[start..valueEnd];
        if (!bad)
            return i;

        // The remnants of a bad URL, up to and including its ')'.
        while (i < text.Length && text[i] != ')')
            i += text[i] == '\\' && i + 1 < text.Length ? 2 : 1;
        return Math.Min(i + 1, text.Length);
    }

    /// <summary>Decodes the CSS escapes in an unquoted url argument.</summary>
    private static string DecodeEscapes(string raw)
    {
        if (!raw.Contains('\\'))
            return raw;

        var decoded = new System.Text.StringBuilder(raw.Length);
        var i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i = ReadEscape(raw, i, decoded);
                continue;
            }

            decoded.Append(raw[i]);
            i++;
        }

        return decoded.ToString();
    }

    /// <summary>
    /// Decodes the escape whose backslash is at <paramref name="backslash"/> into <paramref name="decoded"/>
    /// and answers the index after it: up to six hex digits and one optional whitespace, or the next
    /// character as itself.
    /// </summary>
    private static int ReadEscape(string text, int backslash, System.Text.StringBuilder decoded)
    {
        var i = backslash + 1;
        var digits = 0;
        var codePoint = 0;
        while (i < text.Length && digits < 6 && Uri.IsHexDigit(text[i]))
        {
            codePoint = (codePoint * 16) + Convert.ToInt32(text[i].ToString(), 16);
            i++;
            digits++;
        }

        if (digits == 0)
        {
            decoded.Append(text[i]);
            return i + 1;
        }

        if (i < text.Length && IsCssWhitespace(text[i]))
            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;

        if (codePoint == 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
            codePoint = 0xFFFD;
        decoded.Append(char.ConvertFromUtf32(codePoint));
        return i;
    }

    /// <summary>A URL written inside a string quoted with <paramref name="quote"/>.</summary>
    private static string EscapeForString(string url, char quote)
    {
        var escaped = new System.Text.StringBuilder(url.Length);
        foreach (var c in url)
        {
            if (c == quote || c == '\\')
                escaped.Append('\\').Append(c);
            else if (c is '\n' or '\r' or '\f')
                escaped.Append('\\').Append(((int)c).ToString("x")).Append(' ');
            else
                escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>A URL written as an unquoted url token's argument.</summary>
    private static string EscapeForUnquotedUrl(string url)
    {
        var escaped = new System.Text.StringBuilder(url.Length);
        foreach (var c in url)
        {
            if (c is '(' or ')' or '"' or '\'' or '\\' || IsCssWhitespace(c) || char.IsControl(c))
                escaped.Append('\\').Append(((int)c).ToString("x")).Append(' ');
            else
                escaped.Append(c);
        }

        return escaped.ToString();
    }

    private static bool IsCssWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
}
