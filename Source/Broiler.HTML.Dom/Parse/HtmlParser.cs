using Broiler.Dom.Html;
using Broiler.HTML.Dom.Utils;
using Broiler.HTML.Utils;
using System;
using System.Collections.Generic;

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
    /// <summary>
    /// Elements that implicitly close an open <c>&lt;p&gt;</c> element
    /// per HTML 4 DTD / HTML5 §12.2.6.4.7.
    /// </summary>
    private static readonly HashSet<string> _pClosingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "details", "div", "dl",
        "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hgroup", "hr", "li", "main", "nav", "ol",
        "p", "pre", "section", "summary", "table", "ul"
    };

    public static CssBox ParseDocument(string source, Uri baseUrl)
    {
        var parsed = new HtmlDocumentParser().ParseDocument(source);
        return ParseDocument(parsed.Document, baseUrl);
    }

    public static CssBox ParseDocument(Broiler.Dom.DomDocument document, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = CssBoxHelper.CreateBlock(baseUrl);
        if (document.DocumentElement is { } documentElement)
            AppendCanonicalNode(documentElement, root, baseUrl);

        return root;
    }

    private static void AppendCanonicalNode(Broiler.Dom.DomNode node, CssBox parent, Uri baseUrl)
    {
        if (node is Broiler.Dom.DomText text)
        {
            if (text.Data.Length > 0)
            {
                var textBox = CssBoxHelper.CreateBox(parent, baseUrl);
                textBox.Text = text.Data.AsMemory();
            }
            return;
        }

        if (node is not Broiler.Dom.DomElement element)
            return;

        Dictionary<string, string> attrs = null;
        if (element.Attributes.Count > 0)
        {
            attrs = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var attribute in element.Attributes.Values)
                attrs[attribute.QualifiedName] = HtmlUtils.DecodeHtml(attribute.Value);
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

    /// <summary>
    /// If the parsed tree lacks an explicit <c>&lt;html&gt;</c> or
    /// <c>&lt;body&gt;</c> element, create them and re-parent the
    /// existing children to match the structure browsers produce.
    /// </summary>
    private static void EnsureImplicitStructure(CssBox root, Uri baseUrl)
    {
        CssBox htmlBox = null;
        foreach (var child in root.Boxes)
        {
            if (child.HtmlTag != null &&
                child.HtmlTag.Name.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                htmlBox = child;
                break;
            }
        }

        if (htmlBox != null)
        {
            // <html> exists — make sure it contains a <body>.
            EnsureBodyElement(htmlBox, baseUrl);
            return;
        }

        // No <html> element — create one and move all children under it.
        // Snapshot existing children before mutating the tree.
        var children = new List<CssBox>(root.Boxes);

        // Detach all children from root.
        foreach (var child in children)
            child.ParentBox = null;

        // Build the html > head + body structure under root.
        htmlBox = CssBoxHelper.CreateBox(new HtmlTag("html", false), baseUrl, root);
        htmlBox.Display = CssConstants.Block;

        var headBox = CssBoxHelper.CreateBox(new HtmlTag("head", false), baseUrl, htmlBox);
        headBox.Display = CssConstants.None;

        // If one of the original children is already an explicit <body>,
        // reuse it instead of creating a new wrapper.  This prevents the
        // double-body nesting (implicit body wrapping explicit body) that
        // causes margins/padding to be applied twice.
        CssBox bodyBox = null;
        foreach (var child in children)
        {
            if (child.HtmlTag != null &&
                child.HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                bodyBox = child;
                break;
            }
        }

        if (bodyBox == null)
        {
            bodyBox = CssBoxHelper.CreateBox(new HtmlTag("body", false), baseUrl, htmlBox);
            bodyBox.Display = CssConstants.Block;
        }
        else
        {
            bodyBox.ParentBox = htmlBox;
        }

        // Sort original children into head vs body content.
        foreach (var child in children)
        {
            // Skip the explicit <body> — it is already parented above.
            if (child == bodyBox)
                continue;

            bool isHeadContent = child.HtmlTag != null &&
                _headElements.Contains(child.HtmlTag.Name);

            child.ParentBox = isHeadContent ? headBox : bodyBox;
        }
    }

    /// <summary>
    /// Ensures an existing <c>&lt;html&gt;</c> box contains a
    /// <c>&lt;body&gt;</c> element. If not, wraps non-head children
    /// in a <c>&lt;body&gt;</c>.
    /// </summary>
    private static void EnsureBodyElement(CssBox htmlBox, Uri baseUrl)
    {
        foreach (var child in htmlBox.Boxes)
        {
            if (child.HtmlTag != null &&
                child.HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
                return; // <body> already exists
        }

        // No <body> — create one and move non-head children into it.
        var bodyBox = CssBoxHelper.CreateBox(new HtmlTag("body", false), baseUrl);
        bodyBox.Display = CssConstants.Block;

        var children = new List<CssBox>(htmlBox.Boxes);
        foreach (var child in children)
        {
            if (child.HtmlTag != null &&
                (child.HtmlTag.Name.Equals("head", StringComparison.OrdinalIgnoreCase) ||
                 _headElements.Contains(child.HtmlTag.Name)))
                continue;

            child.ParentBox = bodyBox;
        }

        bodyBox.ParentBox = htmlBox;
    }

    /// <summary>
    /// Elements that belong in <c>&lt;head&gt;</c> rather than <c>&lt;body&gt;</c>.
    /// </summary>
    private static readonly HashSet<string> _headElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "link", "meta", "title", "base", "script", "noscript"
    };
}
