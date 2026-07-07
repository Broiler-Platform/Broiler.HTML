using Broiler.Dom.Html;
using Broiler.Dom;
using System;
using System.Collections.Generic;
using Broiler.Layout.Engine;
using Broiler.HTML.Utils;
using Broiler.Layout;
using System.Net;

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
    {
        var parsed = new HtmlDocumentParser().ParseDocument(source);
        return ParseDocument(parsed.Document, baseUrl);
    }

    public static CssBox ParseDocument(DomDocument document, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(document);

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
                    AppendCanonicalNode(child, root, baseUrl);
            else
                AppendCanonicalNode(documentElement, root, baseUrl);
        }

        return root;
    }

    private static void AppendCanonicalNode(DomNode node, CssBox parent, Uri baseUrl)
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

        Dictionary<string, string> attrs = null;
        if (element.Attributes.Count > 0)
        {
            attrs = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var attribute in element.Attributes.Values)
                attrs[attribute.QualifiedName] = WebUtility.HtmlDecode(attribute.Value);
        }

        var isSingle = HtmlUtils.IsSingleTag(element.LocalName);
        var tag = new HtmlTag(element.LocalName, isSingle, attrs);
        var box = CssBoxHelper.CreateBox(tag, baseUrl, parent);
        // Keep the link back to the canonical element so the shared Broiler.CSS.Dom
        // cascade can compute this box's style from the real DOM tree (Phase 5).
        box.SourceElement = element;
        if (element.LocalName.Equals("input", StringComparison.OrdinalIgnoreCase))
            AppendInputValueText(box, tag, baseUrl);

        foreach (var child in element.ChildNodes)
            AppendCanonicalNode(child, box, baseUrl);
    }

    private static void AppendInputValueText(CssBox inputBox, HtmlTag tag, Uri baseUrl)
    {
        var inputType = tag.TryGetAttribute("type")?.ToLowerInvariant() ?? "text";
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
