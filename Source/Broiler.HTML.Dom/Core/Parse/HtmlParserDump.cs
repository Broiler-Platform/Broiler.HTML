using Broiler.HTML.Dom.Core.Dom;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Broiler.HTML.Dom.Core.Parse;

/// <summary>
/// Deterministic JSON dump helpers for parser compliance tests.
/// </summary>
public static class HtmlParserDump
{
    private static readonly JsonSerializerOptions ResourceLogJsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DumpTokensAsJson(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var tokenizer = new HtmlTokenizer();
        var sb = new StringBuilder();
        sb.AppendLine("[");

        var tokens = tokenizer.Tokenize(html).ToList();
        for (var index = 0; index < tokens.Count; index++)
        {
            WriteToken(sb, tokens[index], 2);
            sb.AppendLine(index < tokens.Count - 1 ? "," : string.Empty);
        }

        sb.AppendLine("]");
        return sb.ToString();
    }

    public static string DumpDomAsJson(string html, string baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        var resolvedBaseUrl = !string.IsNullOrWhiteSpace(baseUrl)
            ? new Uri(baseUrl, UriKind.Absolute)
            : new Uri("about:blank");
        var root = HtmlParser.ParseDocument(html, resolvedBaseUrl);

        var sb = new StringBuilder();
        WriteBox(sb, root, 0);
        sb.AppendLine();
        return sb.ToString();
    }

    public static string DumpResourceLogAsJson(string html, string baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        var initialBaseUrl = !string.IsNullOrWhiteSpace(baseUrl)
            ? new Uri(baseUrl, UriKind.Absolute)
            : new Uri("about:blank");
        var root = HtmlParser.ParseDocument(html, initialBaseUrl);
        var documentBaseUrl = FindDocumentBaseUrl(root, initialBaseUrl);
        var resources = new List<ResourceLogEntry>();

        CollectResources(root, documentBaseUrl, initialBaseUrl, resources);

        for (var index = 0; index < resources.Count; index++)
            resources[index].Index = index;

        var document = new ResourceLogDocument
        {
            BaseUrl = ToPortableUriString(initialBaseUrl, initialBaseUrl),
            DocumentBaseUrl = ToPortableUriString(documentBaseUrl, initialBaseUrl),
            Resources = resources,
            Summary = new ResourceLogSummary
            {
                Total = resources.Count,
                LocalFileExists = resources.Count(static item => item.Classification == "local-file-exists"),
                LocalFileMissing = resources.Count(static item => item.Classification == "local-file-missing"),
                DataUri = resources.Count(static item => item.Classification == "data-uri"),
                RemoteNetworkBlocked = resources.Count(static item => item.Classification == "remote-network-blocked"),
                Empty = resources.Count(static item => item.Classification == "empty"),
                Unsupported = resources.Count(static item => item.Classification == "unsupported-scheme")
            }
        };

        return JsonSerializer.Serialize(document, ResourceLogJsonOptions) + Environment.NewLine;
    }

    private static void WriteToken(StringBuilder sb, HtmlToken token, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);

        sb.Append(pad).AppendLine("{");
        sb.Append(pad2).Append("\"type\": ").AppendJson(token.Type.ToString()).AppendLine(",");
        sb.Append(pad2).Append("\"name\": ").AppendJsonOrNull(token.Name).AppendLine(",");
        sb.Append(pad2).Append("\"data\": ").AppendJsonOrNull(token.Data).AppendLine(",");
        sb.Append(pad2).Append("\"selfClosing\": ").Append(token.SelfClosing ? "true" : "false").AppendLine(",");
        sb.Append(pad2).Append("\"attributes\": ");
        WriteAttributes(sb, token.Attributes, indent + 2);
        sb.AppendLine();
        sb.Append(pad).Append('}');
    }

    private static void WriteBox(StringBuilder sb, CssBox box, int indent)
    {
        var pad = new string(' ', indent);
        var pad2 = new string(' ', indent + 2);
        var tag = box.HtmlTag;
        var isText = tag is null && !box.Text.IsEmpty;

        sb.Append(pad).AppendLine("{");
        sb.Append(pad2).Append("\"kind\": ").AppendJson(isText ? "text" : tag is null ? "root" : "element").AppendLine(",");
        sb.Append(pad2).Append("\"name\": ").AppendJsonOrNull(tag?.Name).AppendLine(",");
        sb.Append(pad2).Append("\"text\": ").AppendJsonOrNull(isText ? box.Text.ToString() : null).AppendLine(",");
        sb.Append(pad2).Append("\"isSingle\": ").Append(tag?.IsSingle == true ? "true" : "false").AppendLine(",");
        sb.Append(pad2).Append("\"attributes\": ");
        WriteAttributes(sb, tag?.Attributes, indent + 2);
        sb.AppendLine(",");
        sb.Append(pad2).Append("\"children\": ");
        WriteChildren(sb, box.Boxes, indent + 2);
        sb.AppendLine();
        sb.Append(pad).Append('}');
    }

    private static void WriteChildren(StringBuilder sb, IReadOnlyList<CssBox> children, int indent)
    {
        if (children.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        var pad = new string(' ', indent);
        sb.AppendLine("[");
        for (var index = 0; index < children.Count; index++)
        {
            WriteBox(sb, children[index], indent + 2);
            sb.AppendLine(index < children.Count - 1 ? "," : string.Empty);
        }
        sb.Append(pad).Append(']');
    }

    private static void WriteAttributes(StringBuilder sb, IReadOnlyDictionary<string, string> attributes, int indent)
    {
        if (attributes is null || attributes.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        var pad = new string(' ', indent);
        var entries = attributes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToList();
        sb.AppendLine("{");
        for (var index = 0; index < entries.Count; index++)
        {
            var pair = entries[index];
            sb.Append(pad).Append("  ").AppendJson(pair.Key).Append(": ").AppendJson(pair.Value);
            sb.AppendLine(index < entries.Count - 1 ? "," : string.Empty);
        }
        sb.Append(pad).Append('}');
    }

    private static Uri FindDocumentBaseUrl(CssBox box, Uri fallbackBaseUrl)
    {
        if (box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("base", StringComparison.OrdinalIgnoreCase) &&
            box.HtmlTag.TryGetAttribute("href") is { } href &&
            TryResolveUri(href, fallbackBaseUrl, out var baseUrl))
        {
            return baseUrl;
        }

        foreach (var child in box.Boxes)
        {
            var childBase = FindDocumentBaseUrl(child, fallbackBaseUrl);
            if (!childBase.Equals(fallbackBaseUrl))
                return childBase;
        }

        return fallbackBaseUrl;
    }

    private static void CollectResources(CssBox box, Uri baseUrl, Uri referenceBaseUrl, List<ResourceLogEntry> resources)
    {
        if (box.HtmlTag != null)
            AddTagResources(box.HtmlTag, baseUrl, referenceBaseUrl, resources);

        foreach (var child in box.Boxes)
            CollectResources(child, baseUrl, referenceBaseUrl, resources);
    }

    private static void AddTagResources(HtmlTag tag, Uri baseUrl, Uri referenceBaseUrl, List<ResourceLogEntry> resources)
    {
        var element = tag.Name.ToLowerInvariant();

        switch (element)
        {
            case "img":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "image");
                AddSrcsetResources(tag, baseUrl, referenceBaseUrl, resources, element, "srcset", "image-candidate");
                break;
            case "input":
                if (string.Equals(tag.TryGetAttribute("type"), "image", StringComparison.OrdinalIgnoreCase))
                    AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "image-submit");
                break;
            case "source":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "media-source");
                AddSrcsetResources(tag, baseUrl, referenceBaseUrl, resources, element, "srcset", "source-candidate");
                break;
            case "iframe":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "nested-document");
                break;
            case "embed":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "embedded-content");
                break;
            case "object":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "data", "object-data");
                break;
            case "video":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "poster", "media-poster");
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "media-source");
                break;
            case "audio":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "media-source");
                break;
            case "track":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "text-track");
                break;
            case "link":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "href", GetLinkRole(tag));
                break;
            case "script":
                AddAttributeResource(tag, baseUrl, referenceBaseUrl, resources, element, "src", "script");
                break;
        }
    }

    private static string GetLinkRole(HtmlTag tag)
    {
        var rel = tag.TryGetAttribute("rel") ?? string.Empty;
        return rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(static token => token.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
            ? "stylesheet"
            : "link-resource";
    }

    private static void AddAttributeResource(
        HtmlTag tag,
        Uri baseUrl,
        Uri referenceBaseUrl,
        List<ResourceLogEntry> resources,
        string element,
        string attribute,
        string role)
    {
        if (tag.TryGetAttribute(attribute) is not { } raw)
            return;

        resources.Add(CreateResourceLogEntry(element, attribute, role, raw, raw, null, tag, baseUrl, referenceBaseUrl));
    }

    private static void AddSrcsetResources(
        HtmlTag tag,
        Uri baseUrl,
        Uri referenceBaseUrl,
        List<ResourceLogEntry> resources,
        string element,
        string attribute,
        string role)
    {
        if (tag.TryGetAttribute(attribute) is not { } raw)
            return;

        foreach (var candidate in ParseSrcset(raw))
            resources.Add(CreateResourceLogEntry(element, attribute, role, raw, candidate.Url, candidate.Descriptor, tag, baseUrl, referenceBaseUrl));
    }

    private static IEnumerable<SrcsetCandidate> ParseSrcset(string raw)
    {
        foreach (var part in raw.Split(','))
        {
            var candidate = part.Trim();
            if (candidate.Length == 0)
                continue;

            var pieces = candidate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0)
                continue;

            yield return new SrcsetCandidate
            {
                Url = pieces[0],
                Descriptor = pieces.Length > 1 ? string.Join(" ", pieces.Skip(1)) : null
            };
        }
    }

    private static ResourceLogEntry CreateResourceLogEntry(
        string element,
        string attribute,
        string role,
        string raw,
        string resource,
        string descriptor,
        HtmlTag tag,
        Uri baseUrl,
        Uri referenceBaseUrl)
    {
        var entry = new ResourceLogEntry
        {
            Element = element,
            Attribute = attribute,
            Role = role,
            Raw = raw,
            Resource = resource,
            Descriptor = descriptor,
            Type = tag.TryGetAttribute("type"),
            Media = tag.TryGetAttribute("media")
        };

        ClassifyResource(entry, baseUrl, referenceBaseUrl);
        entry.RendererDisposition = GetRendererDisposition(entry);
        return entry;
    }

    private static void ClassifyResource(ResourceLogEntry entry, Uri baseUrl, Uri referenceBaseUrl)
    {
        var resource = entry.Resource?.Trim();
        if (string.IsNullOrEmpty(resource))
        {
            entry.UrlKind = "empty";
            entry.Classification = "empty";
            entry.LoadsNetwork = false;
            entry.Exists = false;
            return;
        }

        if (resource.StartsWith("#", StringComparison.Ordinal))
        {
            entry.ResolvedUrl = resource;
            entry.UrlKind = "fragment";
            entry.Classification = "fragment-only";
            entry.LoadsNetwork = false;
            entry.Exists = true;
            return;
        }

        if (resource.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            entry.ResolvedUrl = resource;
            entry.UrlKind = "data";
            entry.Classification = "data-uri";
            entry.MimeGuess = GuessDataUriMime(resource);
            entry.LoadsNetwork = false;
            entry.Exists = true;
            return;
        }

        if (!TryResolveUri(resource, baseUrl, out var resolvedUri))
        {
            entry.UrlKind = "invalid";
            entry.Classification = "invalid-url";
            entry.LoadsNetwork = false;
            entry.Exists = false;
            return;
        }

        entry.ResolvedUrl = ToPortableUriString(resolvedUri, referenceBaseUrl);
        entry.MimeGuess = GuessMimeFromPath(resolvedUri.LocalPath);

        if (resolvedUri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = resolvedUri.LocalPath;
            entry.UrlKind = "file";
            entry.LocalPath = ToPortableLocalPath(localPath, referenceBaseUrl);
            entry.Exists = File.Exists(localPath);
            entry.Classification = entry.Exists ? "local-file-exists" : "local-file-missing";
            entry.LoadsNetwork = false;
            return;
        }

        if (resolvedUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
            resolvedUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            entry.UrlKind = "remote";
            entry.Classification = "remote-network-blocked";
            entry.LoadsNetwork = false;
            entry.Exists = false;
            return;
        }

        entry.UrlKind = resolvedUri.Scheme;
        entry.Classification = "unsupported-scheme";
        entry.LoadsNetwork = false;
        entry.Exists = false;
    }

    private static string GetRendererDisposition(ResourceLogEntry entry)
    {
        if (entry.Classification == "remote-network-blocked")
            return "blocked-network";
        if (entry.Classification is "local-file-missing" or "invalid-url" or "empty" or "unsupported-scheme")
            return "unavailable";
        if (entry.Attribute == "srcset")
            return "candidate-only";
        if (entry.Element == "img" && entry.Attribute == "src")
            return "loadable-image";
        if (entry.Element == "object" && entry.Attribute == "data" && IsImageMime(entry.MimeGuess))
            return "loadable-object-image";
        if (entry.Element == "source")
            return "candidate-only";
        if (entry.Element is "video" or "audio" or "track")
            return "static-media-metadata";
        if (entry.Element == "iframe")
            return "static-iframe-placeholder";
        if (entry.Element == "link" && entry.Role == "stylesheet")
            return "loadable-stylesheet";
        return "logged-resource";
    }

    private static bool TryResolveUri(string raw, Uri baseUrl, out Uri resolvedUri)
    {
        resolvedUri = null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absoluteUri))
        {
            resolvedUri = absoluteUri;
            return true;
        }

        if (baseUrl is null)
            return false;

        return Uri.TryCreate(baseUrl, raw, out resolvedUri);
    }

    private static string GuessDataUriMime(string resource)
    {
        var commaIndex = resource.IndexOf(',');
        var metadata = commaIndex >= 0 ? resource[5..commaIndex] : resource[5..];
        var semicolonIndex = metadata.IndexOf(';');
        return semicolonIndex >= 0 ? metadata[..semicolonIndex] : metadata;
    }

    private static string GuessMimeFromPath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".gif" => "image/gif",
            ".htm" or ".html" => "text/html",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".js" => "text/javascript",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".vtt" => "text/vtt",
            ".webp" => "image/webp",
            ".xml" => "application/xml",
            _ => null
        };
    }

    private static bool IsImageMime(string mime) =>
        !string.IsNullOrEmpty(mime) && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ToPortableUriString(Uri uri, Uri referenceBaseUrl)
    {
        if (uri is null)
            return null;

        if (!uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) ||
            referenceBaseUrl is null ||
            !referenceBaseUrl.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        return "file:" + ToPortableLocalPath(uri.LocalPath, referenceBaseUrl);
    }

    private static string ToPortableLocalPath(string localPath, Uri referenceBaseUrl)
    {
        if (string.IsNullOrEmpty(localPath) ||
            referenceBaseUrl is null ||
            !referenceBaseUrl.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return localPath;
        }

        var referencePath = referenceBaseUrl.LocalPath;
        var referenceDirectory = Directory.Exists(referencePath)
            ? referencePath
            : Path.GetDirectoryName(referencePath);
        if (string.IsNullOrEmpty(referenceDirectory))
            return localPath.Replace('\\', '/');

        return Path.GetRelativePath(referenceDirectory, localPath).Replace('\\', '/');
    }

    private static StringBuilder AppendJson(this StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb;
    }

    private static StringBuilder AppendJsonOrNull(this StringBuilder sb, string value)
    {
        return value is null ? sb.Append("null") : sb.AppendJson(value);
    }

    private sealed class ResourceLogDocument
    {
        public string BaseUrl { get; init; }
        public string DocumentBaseUrl { get; init; }
        public List<ResourceLogEntry> Resources { get; init; }
        public ResourceLogSummary Summary { get; init; }
    }

    private sealed class ResourceLogEntry
    {
        public int Index { get; set; }
        public string Element { get; init; }
        public string Attribute { get; init; }
        public string Role { get; init; }
        public string Raw { get; init; }
        public string Resource { get; init; }
        public string Descriptor { get; init; }
        public string Type { get; init; }
        public string Media { get; init; }
        public string ResolvedUrl { get; set; }
        public string UrlKind { get; set; }
        public string Classification { get; set; }
        public string MimeGuess { get; set; }
        public string LocalPath { get; set; }
        public bool Exists { get; set; }
        public bool LoadsNetwork { get; set; }
        public string RendererDisposition { get; set; }
    }

    private sealed class ResourceLogSummary
    {
        public int Total { get; init; }
        public int LocalFileExists { get; init; }
        public int LocalFileMissing { get; init; }
        public int DataUri { get; init; }
        public int RemoteNetworkBlocked { get; init; }
        public int Empty { get; init; }
        public int Unsupported { get; init; }
    }

    private sealed class SrcsetCandidate
    {
        public string Url { get; init; }
        public string Descriptor { get; init; }
    }
}
