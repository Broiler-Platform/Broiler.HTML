using System;
using System.Collections.Generic;
using Broiler.Dom;
using Broiler.HTML.Core.Utils;

namespace Broiler.HTML.Dom.Parse;

/// <summary>
/// HTML §4.2.3: the first <c>&lt;base&gt;</c> element that carries an <c>href</c> gives the
/// document its base URL, and every relative URL in the document — a stylesheet
/// <c>href</c>, an image <c>src</c>, a <c>url()</c> inside an inline sheet — resolves against
/// it, including the references that appear before the <c>&lt;base&gt;</c> in source order.
/// The embedder's document URL is only the fallback, for a document that sets no base.
/// </summary>
/// <remarks>
/// <para>
/// This reads the canonical DOM, before any box exists, so the projection builds every box
/// against the settled base instead of stamping it on afterwards. It used to scan the box tree,
/// which differed from the standard in three ways: a first base that resolved to the fallback
/// URL (<c>href=""</c>, or the document's own URL) read as "no base" and let a later one win;
/// an unresolvable first base was skipped the same way; and the scan walked into the
/// sub-viewports projected under an <c>&lt;iframe&gt;</c>, so a sub-document's
/// <c>&lt;base&gt;</c> could become its parent's.
/// </para>
/// <para>
/// It does not use <c>HtmlDocumentQueries.GetEffectiveBaseHref</c>: that query passes over a
/// blank <c>href</c>, which the standard does not, and it cannot stop at a nested browsing
/// context, which only the renderer's tree carries. Parsed template contents need no guard here
/// for the same reason they need none there: they are not among a template's child nodes.
/// </para>
/// </remarks>
internal static class DocumentBaseUrl
{
    /// <summary>
    /// The name of the legacy in-tree element that roots a nested browsing context's
    /// sub-document. Its subtree is another document, so its <c>&lt;base&gt;</c> is not this one's.
    /// </summary>
    internal const string NestedDocumentRootName = "#subdoc-root";

    /// <summary>
    /// The base URL of the document whose tree is rooted at <paramref name="root"/> (a document,
    /// or the root element of a nested sub-document), resolved against
    /// <paramref name="fallbackBaseUrl"/>. Returns that fallback when the tree declares no base,
    /// and also when its first base cannot serve as one.
    /// </summary>
    public static Uri Resolve(DomNode root, Uri fallbackBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fallbackBaseUrl);

        return FindFirstBaseHref(root) is { } href
            ? Freeze(href, fallbackBaseUrl)
            : fallbackBaseUrl;
    }

    /// <summary>
    /// The <c>href</c> of the first HTML <c>&lt;base&gt;</c> in tree order that has one, whatever
    /// its value, or <see langword="null"/>. Elements below a nested document root other than
    /// <paramref name="root"/> itself are not this document's and are not visited.
    /// </summary>
    internal static string? FindFirstBaseHref(DomNode root)
    {
        var pending = new Stack<DomNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is DomElement element)
            {
                if (element != root && IsNestedDocumentRoot(element))
                    continue;

                if (IsHtmlBase(element) && element.TryGetAttributeByQualifiedName("href", out var href))
                    return href;
            }

            var children = node.ChildNodes;
            for (var index = children.Count - 1; index >= 0; index--)
                pending.Push(children[index]);
        }

        return null;
    }

    internal static bool IsNestedDocumentRoot(DomElement element) =>
        element.LocalName.Equals(NestedDocumentRootName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The frozen base URL of a <c>&lt;base&gt;</c> whose <c>href</c> is <paramref name="href"/>
    /// (HTML §4.2.3 "set the frozen base URL"): the value parsed against the fallback, or the
    /// fallback itself when the value does not parse or names a <c>data:</c> or
    /// <c>javascript:</c> URL.
    /// </summary>
    private static Uri Freeze(string href, Uri fallbackBaseUrl)
    {
        // The URL parser strips leading and trailing C0 controls and spaces before it reads anything.
        var trimmed = href.Trim(UrlTrimCharacters);
        if (CommonUtils.TryResolveUri(trimmed, fallbackBaseUrl, out var baseUrl)
            && !baseUrl.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.Scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl;
        }

        return fallbackBaseUrl;
    }

    private static bool IsHtmlBase(DomElement element) =>
        element.LocalName.Equals("base", StringComparison.OrdinalIgnoreCase)
        && (element.NamespaceUri is null || element.NamespaceUri == DomNamespaces.Html);

    private static readonly char[] UrlTrimCharacters = BuildUrlTrimCharacters();

    private static char[] BuildUrlTrimCharacters()
    {
        var characters = new char[0x21];
        for (var index = 0; index <= 0x20; index++)
            characters[index] = (char)index;
        return characters;
    }
}
