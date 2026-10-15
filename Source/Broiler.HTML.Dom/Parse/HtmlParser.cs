using Broiler.Dom.Html;
using Broiler.Dom;
using System;
using System.Collections.Generic;
using Broiler.Layout.Engine;
using Broiler.Layout;

namespace Broiler.HTML.Dom.Parse;

/// <summary>
/// Parses an HTML string into a <see cref="CssBox"/> tree for CSS layout.
/// Uses the shared WHATWG-aligned <see cref="HtmlTokenizer"/> (originally
/// from the DomBridge in Broiler.App) instead of a hand-rolled scanner,
/// giving more accurate tokenisation of tags, attributes, comments,
/// raw-text elements (<c>&lt;style&gt;</c> / <c>&lt;script&gt;</c>), and
/// processing instructions.
/// </summary>
internal static class HtmlParser
{
    public static CssBox ParseDocument(string source, Uri baseUrl)
        => ParseDocument(source, baseUrl, out _);

    /// <summary>
    /// Parses <paramref name="source"/> and projects it into boxes, against the document base
    /// URL its first <c>&lt;base href&gt;</c> settles (<see cref="DocumentBaseUrl"/>), with
    /// <paramref name="fallbackBaseUrl"/> as the URL of a document that declares none.
    /// </summary>
    public static CssBox ParseDocument(string source, Uri fallbackBaseUrl, out Uri documentBaseUrl)
    {
        var parsed = HtmlDocumentParser.ParseDocument(source);
        return ParseDocument(parsed.Document, fallbackBaseUrl, contentDocumentResolver: null, out documentBaseUrl);
    }

    public static CssBox ParseDocument(DomDocument document, Uri baseUrl)
        => ParseDocument(document, baseUrl, contentDocumentResolver: null, out _);

    public static CssBox ParseDocument(DomDocument document, Uri baseUrl, Func<DomElement, DomDocument?>? contentDocumentResolver)
        => ParseDocument(document, baseUrl, contentDocumentResolver, out _);

    public static CssBox ParseDocument(DomDocument document, Uri fallbackBaseUrl, Func<DomElement, DomDocument?>? contentDocumentResolver, out Uri documentBaseUrl)
        => ParseDocument(document, fallbackBaseUrl, contentDocumentResolver, formState: null, out documentBaseUrl);

    /// <summary>
    /// Builds a <see cref="CssBox"/> tree from <paramref name="document"/>.
    /// <paramref name="contentDocumentResolver"/> (HtmlBridge Phase 4 P4.4b) lets a host
    /// project a nested browsing context (an <c>&lt;iframe&gt;</c>/<c>&lt;object&gt;</c>/
    /// <c>&lt;frame&gt;</c>) whose sub-document is <em>not</em> an in-tree child: for each
    /// element the walker asks the resolver for a referenced content
    /// <see cref="DomDocument"/> and, if one is returned, synthesises a sub-viewport box
    /// (flagged <see cref="CssBox.IsNestedBrowsingContextRoot"/>) under the frame box and
    /// projects that document's tree into it. <c>null</c> keeps the legacy behaviour where
    /// the sub-document is materialised as an in-tree subtree.
    /// </summary>
    /// <remarks>
    /// Every box is created against the base URL of the document it belongs to, settled from
    /// the DOM before the first box exists: <paramref name="documentBaseUrl"/> for the document
    /// itself, resolved against <paramref name="fallbackBaseUrl"/>, and for a nested sub-document
    /// (in-tree or resolver-supplied) its own first <c>&lt;base href&gt;</c>, with its parent's
    /// base as the fallback. A sub-document's base therefore stays in the sub-document.
    /// <para>
    /// A control <paramref name="formState"/> holds a dirty value for shows that value, as it
    /// shows its default value otherwise (see <see cref="AppendCanonicalNode"/>).
    /// </para>
    /// </remarks>
    public static CssBox ParseDocument(DomDocument document, Uri fallbackBaseUrl, Func<DomElement, DomDocument?>? contentDocumentResolver, HtmlFormState? formState, out Uri documentBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(document);
        var projection = new Projection(contentDocumentResolver, formState);

        documentBaseUrl = DocumentBaseUrl.Resolve(document, fallbackBaseUrl);
        var baseUrl = documentBaseUrl;
        var root = CssBoxHelper.CreateBlock(baseUrl);
        if (document.DocumentElement is { } documentElement)
        {
            // The script-bridge typed hand-off (RF-BRIDGE-1b) exposes a synthetic
            // "#document" wrapper element as DocumentElement, whereas the renderer's own
            // parse exposes <html>. "#document" is not a rendered element; appending it as
            // a box would make it an inline (unknown-tag default) box of zero width,
            // collapsing block-width propagation to <body> so every descendant lays out at
            // 0×0. Descend into its children so they attach directly to the block document
            // root, matching the <html>-rooted structure the string parse produces.
            if (documentElement.LocalName == "#document")
                foreach (var child in documentElement.ChildNodes)
                    AppendCanonicalNode(child, root, baseUrl, projection);
            else
                AppendCanonicalNode(documentElement, root, baseUrl, projection);
        }

        return root;
    }

    /// <summary>What a projection carries down the tree besides the base URL.</summary>
    private sealed record Projection(Func<DomElement, DomDocument?>? ContentDocumentResolver, HtmlFormState? FormState);

    private static void AppendCanonicalNode(DomNode node, CssBox parent, Uri baseUrl, Projection projection)
    {
        // Match text by canonical node type, not concrete class, and read it through
        // DomNode.NodeValue. The renderer's own parse path produces Broiler.Dom.DomText,
        // but the script bridge models text as its own DomElement subtype with
        // NodeType==Text; both expose their content via NodeValue, so this handles the
        // typed hand-off from either source (RF-BRIDGE-1b).
        if (node.NodeType == DomNodeType.Text)
        {
            var text = node.NodeValue ?? string.Empty;
            if (text.Length > 0)
            {
                // Coalesce consecutive text nodes into a single text box. The DOM
                // keeps text split into separate nodes when a non-rendered node —
                // most commonly an HTML comment — sits between two runs of text
                // (e.g. "\n<!-- c -->\n" between block siblings). Comments are not
                // appended as boxes, so without coalescing each surrounding run
                // becomes its own whitespace text box and CSS white-space
                // processing collapses them independently, yielding a spurious
                // extra space between elements (and an uncollapsed leading space at
                // the start of a block) that shifts all following content — a major
                // cause of the WPT "MissingContent" pixel mismatches in
                // comment-heavy tests. Joining them reconstructs the single inline
                // white-space run the spec collapses.
                if (parent.Boxes.Count > 0
                    && parent.Boxes[^1] is { HtmlTag: null } prevText
                    && !prevText.Text.IsEmpty)
                {
                    prevText.Text = string.Concat(prevText.Text.Span, text.AsSpan()).AsMemory();
                }
                else
                {
                    var textBox = CssBoxHelper.CreateBox(parent, baseUrl);
                    textBox.Text = text.AsMemory();
                }
            }
            return;
        }

        if (node is not DomElement element)
            return;

        Dictionary<string, string>? attrs = null;
        if (element.Attributes.Count > 0)
        {
            attrs = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            // Taken as they stand: the tokenizer decodes an attribute value's character references
            // where every other consumer of the DOM sees them (WHATWG §13.2.5). Decoding again here
            // would eat a second level of escaping — `title="&amp;amp;"` means a literal `&amp;`,
            // not `&`.
            foreach (var attribute in element.Attributes.Values)
                attrs[attribute.QualifiedName] = attribute.Value;
        }

        // The tree builder's own classification: an element it closes at once is one the
        // renderer's exports write as a single tag.
        // HTML §4.10.5.4 / §4.10.11: what the user typed into a field is its dirty value, held in
        // the form state; the value attribute and a textarea's text are its default value, which
        // typing leaves alone. The box shows the dirty value where there is one, and the box's own
        // attribute copy carries it, so the renderer's export writes what is on screen.
        string? dirtyValue = null;
        var isDirty = projection.FormState?.TryGetDirtyValue(element, out dirtyValue) == true;
        var isTextArea = element.LocalName.Equals("textarea", StringComparison.OrdinalIgnoreCase);
        if (isDirty && element.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            attrs ??= new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            attrs["value"] = dirtyValue ?? string.Empty;
        }

        var isSingle = HtmlSerializer.VoidElements.Contains(element.LocalName);
        var tag = new HtmlTag(element.LocalName, isSingle, attrs);
        var box = CssBoxHelper.CreateBox(tag, baseUrl, parent);
        // Keep the link back to the canonical element so the shared Broiler.CSS.Dom
        // cascade can compute this box's style from the real DOM tree (Phase 5).
        box.SourceElement = element;

        // HtmlBridge Phase 4 (P4.4b): a legacy in-tree "#subdoc-root" element roots a nested
        // browsing context. Flag its box so CssBox.LayoutNestedBrowsingContexts finds it by the
        // neutral flag rather than the fake tag name (the sever below produces the same box
        // without any #subdoc-root element in the tree). Its subtree is another document, with
        // a base URL of its own.
        if (DocumentBaseUrl.IsNestedDocumentRoot(element))
        {
            box.IsNestedBrowsingContextRoot = true;
            baseUrl = DocumentBaseUrl.Resolve(element, baseUrl);
            box.BaseUrl = baseUrl;
        }

        if (element.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase))
            AppendInputValueText(box, element, tag, baseUrl);

        if (isDirty && isTextArea)
        {
            if (!string.IsNullOrEmpty(dirtyValue))
                CssBoxHelper.CreateBox(box, baseUrl).Text = dirtyValue.AsMemory();
        }
        else
        {
            foreach (var child in element.ChildNodes)
                AppendCanonicalNode(child, box, baseUrl, projection);
        }

        // HtmlBridge Phase 4 (P4.4b) sever: if the host references a content document for this
        // element (an <iframe>/<object>/<frame> whose sub-document is no longer an in-tree
        // #subdoc-root child), synthesise the sub-viewport box under the frame box and project
        // the referenced document's tree into it. The box carries no SourceElement (it is not a
        // script-visible element); the sub-document's own elements keep theirs, so their geometry
        // is keyed and cascaded exactly as when they were in-tree.
        if (projection.ContentDocumentResolver?.Invoke(element) is { } contentDocument)
        {
            var contentBaseUrl = DocumentBaseUrl.Resolve(contentDocument, baseUrl);
            var subViewportTag = new HtmlTag(DocumentBaseUrl.NestedDocumentRootName, false, null);
            var subViewport = CssBoxHelper.CreateBox(subViewportTag, contentBaseUrl, box);
            subViewport.IsNestedBrowsingContextRoot = true;

            if (contentDocument.DocumentElement is { } contentRoot)
                AppendCanonicalNode(contentRoot, subViewport, contentBaseUrl, projection);
        }
    }

    /// <remarks>
    /// The type is the input's state (HTML §4.10.5, <see cref="HtmlFormQueries.GetInputType"/>): a
    /// missing or invalid <c>type</c> is the Text state, so <c>type="foo"</c> shows its value as a text
    /// field does. Lowercasing the raw attribute left such an input showing nothing.
    /// </remarks>
    private static void AppendInputValueText(CssBox inputBox, DomElement element, HtmlTag tag, Uri baseUrl)
    {
        var inputType = HtmlFormQueries.GetInputType(element);
        var value = tag.TryGetAttribute("value");
        if (inputType is "submit" or "button" or "reset")
            value ??= inputType == "submit" ? "Submit" : inputType == "reset" ? "Reset" : "";

        if (inputType is "submit" or "button" or "reset" or "text" or "search" or "email" or "url" or "tel" or "number" or "password" &&
            !string.IsNullOrEmpty(value))
        {
            var textBox = CssBoxHelper.CreateBox(inputBox, baseUrl);
            textBox.Text = value.AsMemory();
        }
    }

}
