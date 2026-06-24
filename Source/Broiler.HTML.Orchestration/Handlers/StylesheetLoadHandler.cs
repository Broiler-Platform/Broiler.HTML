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

    public void LoadStylesheet(string src, Dictionary<string, string> attributes, out string stylesheet, out CssData stylesheetData)
    {
        stylesheet = null;
        stylesheetData = null;

        try
        {
            var args = new HtmlStylesheetLoadEventArgs(src, attributes);
            _htmlContainer.RaiseHtmlStylesheetLoadEvent(args);

            if (!string.IsNullOrEmpty(args.SetStyleSheet))
            {
                stylesheet = args.SetStyleSheet;
            }
            else if (args.SetStyleSheetData != null)
            {
                stylesheetData = args.SetStyleSheetData;
            }
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
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "Exception in handling stylesheet source", ex);
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
            return LoadStylesheetFromUri(uri);
        }
    }

    private string ResolveStylesheetSource(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return src;

        if (Uri.TryCreate(src, UriKind.Absolute, out _))
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
                _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "No stylesheet found by path: " + path);
            }
        }
        else
        {
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "Failed load image, invalid source: " + path);
        }

        return string.Empty;
    }

    private static readonly HttpClient SharedHttpClient = new();

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
            _htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "Error in correcting relative URL in loaded stylesheet", ex);
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
