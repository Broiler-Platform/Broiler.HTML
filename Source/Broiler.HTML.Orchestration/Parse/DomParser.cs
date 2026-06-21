using System.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.HTML.Core.Core;
using Broiler.HTML.CSS.Core.Dom;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Parse;
using Broiler.HTML.Dom.Utils;
using Broiler.HTML.Dom;
using Broiler.HTML.Utils;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.IR;

namespace Broiler.HTML.Orchestration.Parse;

internal sealed class DomParser
{
    private readonly CssParser _cssParser;
    private readonly IStylesheetLoader _stylesheetLoader;

    public DomParser(CssParser cssParser, IStylesheetLoader stylesheetLoader)
    {
        ArgumentNullException.ThrowIfNull(cssParser);
        ArgumentNullException.ThrowIfNull(stylesheetLoader);

        _cssParser = cssParser;
        _stylesheetLoader = stylesheetLoader;
    }

    public CssBox GenerateCssTree(string html, HtmlContainerInt htmlContainer, ref CssData cssData, Uri baseUrl)
    {
        var root = HtmlParser.ParseDocument(html, baseUrl);
        if (root == null)
            return root;

        root.ContainerInt = htmlContainer;

        bool cssDataChanged = false;
        CascadeParseStyles(root, htmlContainer, ref cssData, ref cssDataChanged);
        CascadeApplyStyles(root, cssData, baseUrl);
        SetTextSelectionStyle(htmlContainer, cssData);
        CorrectTextBoxes(root);
        CorrectImgBoxes(root, baseUrl);
        CorrectObjectBoxes(root);

        bool followingBlock = true;
        CorrectLineBreaksBlocks(root, ref followingBlock);
        CorrectInlineBoxesParent(root, baseUrl);
        CorrectBlockInsideInline(root, baseUrl);
        CorrectInlineBoxesParent(root, baseUrl);

        return root;
    }

    private void CascadeParseStyles(CssBox box, HtmlContainerInt htmlContainer, ref CssData cssData, ref bool cssDataChanged)
    {
        if (box.HtmlTag != null)
        {
            // Check for the <link rel=stylesheet> tag
            // Per CSS2.1 §6.4.1, the rel attribute is a space-separated list;
            // match if any token equals "stylesheet" (e.g. rel="appendix stylesheet").
            if (box.HtmlTag.Name.Equals("link", StringComparison.CurrentCultureIgnoreCase) &&
                ContainsStylesheetRel(box.GetAttribute("rel", string.Empty)))
            {
                CloneCssData(ref cssData, ref cssDataChanged);
                _stylesheetLoader.LoadStylesheet(box.GetAttribute("href", string.Empty), box.HtmlTag.Attributes, out string stylesheet, out CssData stylesheetData);
                if (stylesheet != null)
                    _cssParser.ParseStyleSheet(cssData, stylesheet);
                else if (stylesheetData != null)
                    cssData.Combine(stylesheetData);
            }

            // Check for the <style> tag
            if (box.HtmlTag.Name.Equals("style", StringComparison.CurrentCultureIgnoreCase) && box.Boxes.Count > 0)
            {
                CloneCssData(ref cssData, ref cssDataChanged);
                foreach (var child in box.Boxes)
                    _cssParser.ParseStyleSheet(cssData, child.Text.ToString());
            }
        }

        foreach (var childBox in box.Boxes)
            CascadeParseStyles(childBox, htmlContainer, ref cssData, ref cssDataChanged);
    }


    private void CascadeApplyStyles(CssBox box, CssData cssData, Uri baseUrl)
    {
        box.InheritStyle();

        if (box.HtmlTag != null)
        {
            // CSS2.1 §6.4.3 specificity: Apply rules in increasing specificity.
            // Bare '*' = (0,0,0), tag = (0,0,1), and standalone universal
            // selectors with pseudo-classes / attributes (e.g. ':open',
            // ':lang(fr)', '[hidden]') are class-level specificity and must not
            // be lumped in with the bare universal pass.
            AssignCssBlocks(box, cssData, "*", filter: static block =>
                block.Selectors == null
                && (block.AttributeConditions == null || block.AttributeConditions.Count == 0)
                && block.PseudoClass == null);
            AssignCssBlocks(box, cssData, box.HtmlTag.Name);
            AssignCssBlocks(box, cssData, "*", qualifiedOnly: true);

            if (box.HtmlTag.HasAttribute("class"))
                AssignClassCssBlocks(box, cssData);

            AssignCssBlocks(box, cssData, "*", filter: static block =>
                block.Selectors == null
                && ((block.AttributeConditions != null && block.AttributeConditions.Count > 0)
                    || block.PseudoClass != null));

            if (box.HtmlTag.HasAttribute("id"))
            {
                var id = box.HtmlTag.TryGetAttribute("id");
                AssignCssBlocks(box, cssData, "#" + id);
                AssignCssBlocks(box, cssData, box.HtmlTag.Name + "#" + id);

                // CSS2.1 §5.8.3: compound selectors like #id.class must
                // match elements that have the given ID AND all specified classes.
                if (box.HtmlTag.HasAttribute("class"))
                {
                    var classes = box.HtmlTag.TryGetAttribute("class");
                    var idPrefix = "#" + id;
                    var classWords = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var classList = new List<string>(classWords.Length);
                    foreach (var w in classWords)
                        classList.Add("." + w);

                    foreach (var cls in classList)
                    {
                        AssignCssBlocks(box, cssData, idPrefix + cls);
                        AssignCssBlocks(box, cssData, box.HtmlTag.Name + idPrefix + cls);
                    }

                    // Try all 2-class permutations — mirrors the existing
                    // compound class logic in AssignClassCssBlocks which
                    // handles order-independent matching (CSS selectors
                    // #id.a.b and #id.b.a are equivalent).
                    if (classList.Count >= 2)
                    {
                        for (int i = 0; i < classList.Count; i++)
                        {
                            for (int j = 0; j < classList.Count; j++)
                            {
                                if (i == j) continue;
                                AssignCssBlocks(box, cssData, idPrefix + classList[i] + classList[j]);
                            }
                        }
                    }
                }
            }

            TranslateAttributes(box.HtmlTag, box);

            if (box.HtmlTag.HasAttribute("style"))
            {
                var block = _cssParser.ParseCssBlock(box.HtmlTag.Name, box.HtmlTag.TryGetAttribute("style"));
                if (block != null)
                    AssignCssBlock(box, block);
            }

            // Phase 2: Populate BoxKind and DOM-attribute properties on the box
            // so layout code can use these instead of accessing HtmlTag directly.
            AssignBoxKindAndAttributes(box);

            // HTML5 §4.8.9: <video> and <audio> are replaced elements. Browsers
            // that support these media types never display the fallback content
            // between the tags; they render the poster frame or first frame
            // instead.  Since this renderer cannot decode media streams, render
            // them as inline-block boxes with the default intrinsic dimensions
            // (300×150 for video, 300×32 for audio) and a black background.
            bool isVideo = box.HtmlTag.Name.Equals("video", StringComparison.OrdinalIgnoreCase);
            bool isAudio = !isVideo && box.HtmlTag.Name.Equals("audio", StringComparison.OrdinalIgnoreCase);
            if (isVideo || isAudio)
            {
                box.Display = CssConstants.InlineBlock;

                // Honour explicit width/height HTML attributes; fall back to the
                // default intrinsic size per the HTML spec.
                if (string.IsNullOrEmpty(box.Width) || box.Width == CssConstants.Auto)
                {
                    var attrW = box.HtmlTag.TryGetAttribute("width");
                    box.Width = !string.IsNullOrEmpty(attrW) ? attrW + "px" : "300px";
                }
                if (string.IsNullOrEmpty(box.Height) || box.Height == CssConstants.Auto)
                {
                    var attrH = box.HtmlTag.TryGetAttribute("height");
                    box.Height = !string.IsNullOrEmpty(attrH) ? attrH + "px" : (isVideo ? "150px" : "32px");
                }

                // Black background to approximate the default media player frame.
                if (string.IsNullOrEmpty(box.BackgroundColor) ||
                    box.BackgroundColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                {
                    box.BackgroundColor = "black";
                }

                // Hide all children (fallback content, <source>, <track>, etc.)
                foreach (var child in box.Boxes)
                    child.Display = CssConstants.None;
            }

            // SVG §7.1: Inline <svg> elements are replaced elements rendered as
            // inline-block boxes.  Their child elements (rect, circle, path, etc.)
            // are not CSS-visible — the SVG subtree is serialised to markup and
            // rendered later by SvgRenderer via PaintWalker.
            if (!isVideo && !isAudio &&
                box.HtmlTag.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
            {
                box.Display = CssConstants.InlineBlock;

                // Honour width/height from CSS style or HTML attributes.
                if (string.IsNullOrEmpty(box.Width) || box.Width == CssConstants.Auto)
                {
                    var attrW = box.HtmlTag.TryGetAttribute("width");
                    box.Width = !string.IsNullOrEmpty(attrW)
                        ? NormaliseDimensionAttribute(attrW)
                        : "300px";
                }
                if (string.IsNullOrEmpty(box.Height) || box.Height == CssConstants.Auto)
                {
                    var attrH = box.HtmlTag.TryGetAttribute("height");
                    box.Height = !string.IsNullOrEmpty(attrH)
                        ? NormaliseDimensionAttribute(attrH)
                        : "150px";
                }

                // Overflow hidden to clip SVG content at the element bounds.
                if (string.IsNullOrEmpty(box.Overflow) || box.Overflow == CssConstants.Visible)
                    box.Overflow = CssConstants.Hidden;

                // Hide child boxes so the CSS layout engine ignores SVG internals.
                foreach (var child in box.Boxes)
                    child.Display = CssConstants.None;
            }
        }

        // CSS2.1 §9.7: Relationships between 'display', 'position', and 'float'.
        // When 'float' is not 'none', the computed value of 'display' is adjusted
        // so that inline-level elements become block-level.  This must happen
        // after all CSS properties are resolved (including 'inherit') and before
        // child style cascading so children see the correct parent display value.
        if (box.Display != CssConstants.None && box.Float != CssConstants.None)
        {
            if (box.Display == CssConstants.Inline || box.Display == CssConstants.InlineBlock)
                box.Display = CssConstants.Block;
        }

        if (box.TextDecoration != string.Empty && box.Text.IsEmpty)
        {
            foreach (var childBox in box.Boxes)
                childBox.TextDecoration = box.TextDecoration;

            box.TextDecoration = string.Empty;
        }

        // CSS Animations §3: Resolve animation keyframe values for static
        // rendering.  After all CSS rules and inline styles are applied,
        // check if the box has an animation-name that references a known
        // @keyframes rule and apply the computed animated values.
        CssAnimationResolver.ResolveAnimations(box, cssData);

        foreach (var childBox in box.Boxes)
            CascadeApplyStyles(childBox, cssData, baseUrl);

        if (box.HtmlTag != null)
            ApplyClosedDetailsVisibility(box);

        if (box.HtmlTag != null)
            ApplySummaryDisclosureMarker(box, baseUrl);

        // CSS2.1 §12.1: Generate ::before and ::after pseudo-element boxes
        // after child style cascading to avoid modifying the child list
        // during iteration.
        if (box.HtmlTag != null)
            ApplyPseudoElementBoxes(box, cssData, baseUrl);
    }

    private static void ApplyClosedDetailsVisibility(CssBox box)
    {
        // HTML §4.11.1: Closed <details> elements expose their first
        // <summary> but keep the rest of the subtree hidden until the open
        // attribute is present.
        if (!box.HtmlTag.Name.Equals("details", StringComparison.OrdinalIgnoreCase) ||
            box.HtmlTag.HasAttribute("open"))
        {
            return;
        }

        bool seenSummary = false;
        foreach (var child in box.Boxes)
        {
            if (child.HtmlTag != null &&
                child.HtmlTag.Name.Equals("summary", StringComparison.OrdinalIgnoreCase) &&
                !seenSummary)
            {
                seenSummary = true;
                continue;
            }

            child.Display = CssConstants.None;
        }
    }

    private static void ApplySummaryDisclosureMarker(CssBox box, Uri baseUrl)
    {
        if (!box.HtmlTag.Name.Equals("summary", StringComparison.OrdinalIgnoreCase) ||
            box.ParentBox?.HtmlTag == null ||
            !box.ParentBox.HtmlTag.Name.Equals("details", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (box.Boxes.Count > 0 &&
            box.Boxes[0].HtmlTag == null &&
            box.Boxes[0].Text.Length > 0 &&
            (box.Boxes[0].Text.Span.SequenceEqual("▸ ".AsSpan()) ||
             box.Boxes[0].Text.Span.SequenceEqual("▾ ".AsSpan())))
        {
            return;
        }

        var markerText = box.ParentBox.HtmlTag.HasAttribute("open") ? "▾ " : "▸ ";
        var markerBox = box.Boxes.Count > 0
            ? CssBoxHelper.CreateBox(box, baseUrl, before: box.Boxes[0])
            : CssBoxHelper.CreateBox(box, baseUrl);
        markerBox.Display = CssConstants.Inline;
        markerBox.Text = markerText.AsMemory();
    }

    private void SetTextSelectionStyle(HtmlContainerInt htmlContainer, CssData cssData)
    {
        htmlContainer.SelectionForeColor = Color.Empty;
        htmlContainer.SelectionBackColor = Color.Empty;

        if (!cssData.ContainsCssBlock("::selection"))
            return;

        var blocks = cssData.GetCssBlock("::selection");
        foreach (var block in blocks)
        {
            if (block.Properties.TryGetValue("color", out string value))
                htmlContainer.SelectionForeColor = _cssParser.ParseColor(value);

            if (block.Properties.TryGetValue("background-color", out string value1))
                htmlContainer.SelectionBackColor = _cssParser.ParseColor(value1);
        }
    }

    private static void AssignClassCssBlocks(CssBox box, CssData cssData)
    {
        var classes = box.HtmlTag.TryGetAttribute("class");
        var classList = new List<string>();
        var startIdx = 0;

        while (startIdx < classes.Length)
        {
            while (startIdx < classes.Length && classes[startIdx] == ' ')
                startIdx++;

            if (startIdx >= classes.Length)
                continue;

            var endIdx = classes.IndexOf(' ', startIdx);

            if (endIdx < 0)
                endIdx = classes.Length;

            var cls = "." + classes.Substring(startIdx, endIdx - startIdx);
            classList.Add(cls);
            AssignCssBlocks(box, cssData, cls);
            AssignCssBlocks(box, cssData, box.HtmlTag.Name + cls);

            startIdx = endIdx + 1;
        }

        // CSS2.1 §5.8.3: compound class selectors like .first.one must
        // match elements that have ALL specified classes.  Generate lookup
        // keys for all 2-class combinations so that rules stored under
        // compound keys are found.
        if (classList.Count < 2)
            return;

        for (int i = 0; i < classList.Count; i++)
        {
            for (int j = 0; j < classList.Count; j++)
            {
                if (i == j) continue;
                var compound = classList[i] + classList[j];
                AssignCssBlocks(box, cssData, compound);
                AssignCssBlocks(box, cssData, box.HtmlTag.Name + compound);
            }
        }
    }

    private static void AssignCssBlocks(CssBox box, CssData cssData, string className, bool? qualifiedOnly = null, Func<CssBlock, bool> filter = null)
    {
        var blocks = cssData.GetCssBlock(className);
        foreach (var block in blocks)
        {
            // When qualifiedOnly is specified, filter by whether the block has
            // ancestor/sibling selectors (which increase specificity).
            if (qualifiedOnly.HasValue)
            {
                bool hasSelectors = block.Selectors != null;
                if (qualifiedOnly.Value != hasSelectors)
                    continue;
            }

            if (filter != null && !filter(block))
                continue;

            if (IsBlockAssignableToBox(box, block))
                AssignCssBlock(box, block);
        }
    }

    private static bool IsBlockAssignableToBox(CssBox box, CssBlock block)
    {
        bool assignable = true;
        if (block.Selectors != null)
        {
            assignable = IsBlockAssignableToBoxWithSelector(box, block);
        }
        else if (box.HtmlTag.Name.Equals("a", StringComparison.OrdinalIgnoreCase) && block.Class.Equals("a", StringComparison.OrdinalIgnoreCase) && !box.HtmlTag.HasAttribute("href"))
        {
            assignable = false;
        }

        // Check attribute conditions (from CSS attribute selectors)
        if (assignable && block.AttributeConditions != null && block.AttributeConditions.Count > 0)
        {
            if (box.HtmlTag == null || !MatchesAttributeConditions(box.HtmlTag, block.AttributeConditions))
                assignable = false;
        }

        // CSS2.1 §5.11: Check structural pseudo-class on the terminal selector.
        if (assignable && block.PseudoClass != null)
        {
            if (!MatchesPseudoClass(box, block.PseudoClass))
                assignable = false;
        }

        if (assignable && block.Hover)
        {
            box.ContainerInt.AddHoverBox(box, block);
            assignable = false;
        }

        return assignable;
    }

    private static bool IsBlockAssignableToBoxWithSelector(CssBox box, CssBlock block)
    {
        foreach (var selector in block.Selectors)
        {
            if (selector.AdjacentSibling)
            {
                // Adjacent sibling combinator: the immediately preceding element
                // sibling of the current box must match the selector.
                box = GetPreviousElementSibling(box);
                if (box == null)
                    return false;

                if (!MatchesSelectorItem(box, selector.Class))
                    return false;

                // CSS2.1 §5.11: Check structural pseudo-class on this selector item.
                if (selector.PseudoClass != null && !MatchesPseudoClass(box, selector.PseudoClass))
                    return false;
            }
            else
            {
                bool matched = false;
                while (!matched)
                {
                    box = box.ParentBox;
                    while (box != null && box.HtmlTag == null)
                        box = box.ParentBox;

                    if (box == null)
                        return false;

                    matched = MatchesSelectorItem(box, selector.Class);

                    // CSS2.1 §5.11: Also verify structural pseudo-class.
                    if (matched && selector.PseudoClass != null && !MatchesPseudoClass(box, selector.PseudoClass))
                        matched = false;

                    if (!matched && selector.DirectParent)
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when the given HTML tag satisfies all attribute
    /// selector conditions (e.g. [type="text"], [hidden]).
    /// </summary>
    private static bool MatchesAttributeConditions(HtmlTag tag, List<CssAttributeCondition> conditions)
    {
        foreach (var cond in conditions)
        {
            if (cond.Op == null)
            {
                // Presence check: [hidden]
                if (!tag.HasAttribute(cond.Name))
                    return false;
            }
            else
            {
                var attrVal = tag.TryGetAttribute(cond.Name);
                if (attrVal == null)
                    return false;

                switch (cond.Op)
                {
                    case "=":
                        if (!string.Equals(attrVal, cond.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case "~=":
                        if (!(" " + attrVal + " ").Contains(" " + cond.Value + " ", StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case "|=":
                        if (!string.Equals(attrVal, cond.Value, StringComparison.OrdinalIgnoreCase) &&
                            !attrVal.StartsWith(cond.Value + "-", StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case "^=":
                        if (!attrVal.StartsWith(cond.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case "$=":
                        if (!attrVal.EndsWith(cond.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case "*=":
                        if (!attrVal.Contains(cond.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    default:
                        if (!string.Equals(attrVal, cond.Value, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="box"/> matches
    /// the given CSS selector item (tag name, .class, #id, or compound .c1.c2).
    /// </summary>
    private static bool MatchesSelectorItem(CssBox box, string selectorClass)
    {
        if (box.HtmlTag == null)
            return false;

        // CSS2.1 §5.3: The universal selector '*' matches any element type.
        if (selectorClass == "*")
            return true;

        // CSS Selectors §3: type selectors in HTML are ASCII case-insensitive.
        // Use OrdinalIgnoreCase to avoid Unicode case-folding (e.g. U+212A ≠ 'k').
        if (box.HtmlTag.Name.Equals(selectorClass, StringComparison.OrdinalIgnoreCase))
            return true;

        if (box.HtmlTag.HasAttribute("class"))
        {
            var className = box.HtmlTag.TryGetAttribute("class");

            // Single class match: ".foo" matches class="foo"
            if (selectorClass.Equals("." + className, StringComparison.InvariantCultureIgnoreCase)
                || selectorClass.Equals(box.HtmlTag.Name + "." + className, StringComparison.InvariantCultureIgnoreCase))
                return true;

            // Compound class match: ".foo.bar" matches class="foo bar" or "bar foo"
            if (selectorClass.StartsWith(".") && selectorClass.IndexOf('.', 1) > 0)
            {
                var parts = selectorClass.Split('.');
                var classWords = (" " + className + " ").ToLower();
                bool allMatch = true;
                for (int i = 1; i < parts.Length; i++) // skip first empty part from leading "."
                {
                    if (string.IsNullOrEmpty(parts[i])) continue;
                    if (!classWords.Contains(" " + parts[i] + " "))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) return true;
            }
        }

        if (box.HtmlTag.HasAttribute("id"))
        {
            var id = box.HtmlTag.TryGetAttribute("id");
            if (selectorClass.Equals("#" + id, StringComparison.InvariantCultureIgnoreCase))
                return true;

            // Compound #id.class selector: "#foo.bar" matches id="foo" class="bar"
            var idStr = "#" + id;
            if (selectorClass.StartsWith(idStr, StringComparison.InvariantCultureIgnoreCase))
            {
                var rest = selectorClass.Substring(idStr.Length);
                if (rest.Length > 0 && rest[0] == '.')
                {
                    if (!box.HtmlTag.HasAttribute("class"))
                        return false;

                    var className = box.HtmlTag.TryGetAttribute("class");
                    var classParts = rest.Split('.');
                    var classWords = (" " + className + " ").ToLower();
                    bool allMatch = true;
                    for (int i = 1; i < classParts.Length; i++)
                    {
                        if (string.IsNullOrEmpty(classParts[i])) continue;
                        if (!classWords.Contains(" " + classParts[i].ToLower() + " "))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch) return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the immediately preceding element sibling (a box with a
    /// non-null <see cref="CssBox.HtmlTag"/>) in the document tree,
    /// or <c>null</c> if there is none. Text-only and anonymous boxes
    /// are skipped.
    /// </summary>
    private static CssBox GetPreviousElementSibling(CssBox box)
    {
        if (box.ParentBox == null)
            return null;

        int index = box.ParentBox.Boxes.IndexOf(box);
        for (int i = index - 1; i >= 0; i--)
        {
            var sib = box.ParentBox.Boxes[i];
            if (sib.HtmlTag != null)
                return sib;
        }
        return null;
    }

    /// <summary>
    /// CSS2.1 §5.11.1: Checks whether <paramref name="box"/> satisfies a
    /// structural pseudo-class condition.
    /// </summary>
    /// <remarks>
    /// Implements TODO-25 and TODO-27 (acid3-compliance.md §11.5):
    /// <c>:first-child</c> matching is used by both the <c>:first-child + *</c>
    /// complex selector (TODO-25) and the <c>h1:first-child</c> attached
    /// pseudo-class (TODO-27).  Tests: <c>Acid3Todo24_28Tests.cs</c>.
    /// </remarks>
    private static bool MatchesPseudoClass(CssBox box, string pseudoClass)
    {
        if (box.HtmlTag == null)
            return false;

        // CSS2.1 §5.11.4: :lang(xx) matches elements whose language is
        // determined by the 'lang' attribute (or xml:lang) on the element
        // or any ancestor.
        if (pseudoClass.StartsWith("lang(", StringComparison.OrdinalIgnoreCase)
            && pseudoClass.EndsWith(")"))
        {
            string langArg = pseudoClass.Substring(5, pseudoClass.Length - 6).Trim();
            return MatchesLangPseudoClass(box, langArg);
        }

        // :root needs no parent — handle it before the ParentBox null check.
        if (pseudoClass == "root")
            return string.Equals(box.HtmlTag.Name, "html", StringComparison.OrdinalIgnoreCase);

        if (box.ParentBox == null)
            return false;

        switch (pseudoClass)
        {
            case "open":
                return MatchesOpenPseudoClass(box);

            case "first-child":
                // CSS2.1 §5.11.1: :first-child matches an element that is the
                // first child element of its parent.
                foreach (var child in box.ParentBox.Boxes)
                {
                    if (child.HtmlTag != null)
                        return ReferenceEquals(child, box);
                }
                return false;

            case "last-child":
                // CSS3 §6.6.5.7: :last-child matches an element that is the
                // last child element of its parent.
                for (int i = box.ParentBox.Boxes.Count - 1; i >= 0; i--)
                {
                    var child = box.ParentBox.Boxes[i];
                    if (child.HtmlTag != null)
                        return ReferenceEquals(child, box);
                }
                return false;

            case "only-child":
                // CSS3 §6.6.5.9: :only-child matches when the element is
                // both first-child and last-child.
                int elementChildCount = 0;
                foreach (var child in box.ParentBox.Boxes)
                {
                    if (child.HtmlTag != null)
                    {
                        elementChildCount++;
                        if (elementChildCount > 1)
                            return false;
                    }
                }
                return elementChildCount == 1;

            default:
                return false;
        }
    }

    /// <summary>
    /// CSS2.1 §5.11.4: Determines whether the given box matches the
    /// <c>:lang(xx)</c> pseudo-class.  Walks ancestor elements looking
    /// for a <c>lang</c> (or <c>xml:lang</c>) attribute whose value
    /// equals or is a hyphen-separated sub-tag prefix of <paramref name="lang"/>.
    /// </summary>
    private static bool MatchesLangPseudoClass(CssBox box, string lang)
    {
        if (!TryGetElementLanguage(box, out var elementLanguage))
            return false;

        if (!TryGetValidLangPseudoArguments(lang, out var ranges))
            return false;

        foreach (var range in ranges)
            if (MatchesLanguageRange(elementLanguage, range))
                return true;

        return false;
    }

    private static string NormalizeLangPseudoArgument(string lang)
    {
        lang = lang.Trim();
        if (lang.Length >= 2)
        {
            char first = lang[0];
            char last = lang[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                lang = lang.Substring(1, lang.Length - 2).Trim();
        }

        return lang;
    }

    private static bool TryGetElementLanguage(CssBox box, out string language)
    {
        for (var current = box; current != null; current = current.ParentBox)
        {
            if (current.HtmlTag == null)
                continue;

            var attrLang = current.HtmlTag.TryGetAttribute("lang")
                        ?? current.HtmlTag.TryGetAttribute("xml:lang");
            if (!string.IsNullOrWhiteSpace(attrLang))
            {
                language = attrLang.Trim();
                return true;
            }
        }

        language = string.Empty;
        return false;
    }

    private static bool TryGetValidLangPseudoArguments(string lang, out List<string> ranges)
    {
        ranges = [];
        foreach (var part in lang.Split(','))
        {
            var normalized = NormalizeLangPseudoArgument(part);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (!IsValidLanguageRange(normalized))
                return false;

            ranges.Add(normalized);
        }

        return ranges.Count > 0;
    }

    private static bool MatchesLanguageRange(string elementLanguage, string languageRange)
    {
        var tagSubtags = SplitLanguageSubtags(elementLanguage);
        var rangeSubtags = SplitLanguageSubtags(languageRange);
        if (tagSubtags.Length == 0 || rangeSubtags.Length == 0)
            return false;

        if (!rangeSubtags.Contains("*", StringComparer.Ordinal))
            return MatchesLanguagePrefix(tagSubtags, rangeSubtags);

        return MatchesExtendedLanguageRange(tagSubtags, rangeSubtags);
    }

    private static string[] SplitLanguageSubtags(string language)
    {
        return language
            .Split(['-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .ToArray();
    }

    private static bool MatchesLanguagePrefix(string[] tagSubtags, string[] rangeSubtags)
    {
        if (rangeSubtags.Length > tagSubtags.Length)
            return false;

        for (int i = 0; i < rangeSubtags.Length; i++)
        {
            if (!string.Equals(tagSubtags[i], rangeSubtags[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool MatchesExtendedLanguageRange(string[] tagSubtags, string[] rangeSubtags)
    {
        int tagIndex = 0;
        int rangeIndex = 0;

        while (rangeIndex < rangeSubtags.Length)
        {
            var rangeSubtag = rangeSubtags[rangeIndex];
            if (rangeSubtag == "*")
            {
                rangeIndex++;
                if (rangeIndex >= rangeSubtags.Length)
                    return true;

                var nextRangeSubtag = rangeSubtags[rangeIndex];
                while (tagIndex < tagSubtags.Length &&
                       !string.Equals(tagSubtags[tagIndex], nextRangeSubtag, StringComparison.Ordinal))
                {
                    tagIndex++;
                }

                if (tagIndex >= tagSubtags.Length)
                    return false;

                continue;
            }

            if (tagIndex >= tagSubtags.Length ||
                !string.Equals(tagSubtags[tagIndex], rangeSubtag, StringComparison.Ordinal))
            {
                return false;
            }

            tagIndex++;
            rangeIndex++;
        }

        return true;
    }

    private static bool IsValidLanguageRange(string languageRange)
    {
        var subtags = languageRange.Split(['-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (subtags.Length == 0)
            return false;

        for (var i = 0; i < subtags.Length; i++)
        {
            var subtag = subtags[i];
            if (subtag == "*")
                continue;

            if (subtag.Length is < 1 or > 8)
                return false;

            if (i == 0 && !subtag.All(IsAsciiLetter))
                return false;

            if (!subtag.All(IsAsciiLetterOrDigit))
                return false;
        }

        return true;
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsAsciiLetterOrDigit(char c) =>
        IsAsciiLetter(c) || (c >= '0' && c <= '9');

    private static bool MatchesOpenPseudoClass(CssBox box)
    {
        if (box.HtmlTag == null)
            return false;

        if (!box.HtmlTag.Name.Equals("details", StringComparison.OrdinalIgnoreCase)
            && !box.HtmlTag.Name.Equals("dialog", StringComparison.OrdinalIgnoreCase))
            return false;

        return box.HtmlTag.TryGetAttribute("open") != null;
    }

    private static void AssignCssBlock(CssBox box, CssBlock block)
    {
        foreach (var prop in block.Properties)
        {
            var value = prop.Value;

            if (prop.Value == CssConstants.Inherit
                && box.ParentBox != null
                && prop.Key is not ("width" or "height" or "min-width" or "max-width" or "min-height" or "max-height"))
                value = CssUtils.GetPropertyValue(box.ParentBox, prop.Key);

            bool newIsImportant = block.ImportantProperties.Contains(prop.Key);

            // CSS2.1 §6.4.2: A property previously set with !important
            // can only be overridden by another !important declaration.
            if (box.ImportantProperties != null
                && box.ImportantProperties.Contains(prop.Key)
                && !newIsImportant)
                continue;

            // CSS2.1 §6.4.1: Author-origin declarations override
            // user-agent declarations regardless of specificity.
            // Check after !important but before IsStyleOnElementAllowed
            // so that a failed author IsStyleOnElementAllowed check does
            // not leave a gap that lets a later UA rule through.
            if (block.IsUserAgent
                && box.AuthorProperties != null
                && box.AuthorProperties.Contains(prop.Key))
                continue;

            if (IsStyleOnElementAllowed(box, prop.Key, value))
            {
                CssUtils.SetPropertyValue(box, prop.Key, value);

                if (newIsImportant)
                    box.MarkPropertyImportant(prop.Key);

                // Track author-origin properties so UA rules at higher
                // specificity cannot overwrite them.
                if (!block.IsUserAgent)
                    box.MarkPropertyAuthor(prop.Key);
            }
        }
    }

    // ── CSS2.1 §12.1: ::before / ::after pseudo-element generation ──

    /// <summary>
    /// Creates <c>::before</c> and <c>::after</c> pseudo-element child
    /// boxes when the CSS data contains matching pseudo-element blocks.
    /// </summary>
    private static void ApplyPseudoElementBoxes(CssBox box, CssData cssData, Uri baseUrl)
    {
        var beforeBlock = FindPseudoElementBlock(box, cssData, "::before");
        if (beforeBlock != null)
            CreatePseudoElementBox(box, beforeBlock, isBefore: true, baseUrl);

        var afterBlock = FindPseudoElementBlock(box, cssData, "::after");
        if (afterBlock != null)
            CreatePseudoElementBox(box, afterBlock, isBefore: false, baseUrl);
    }

    /// <summary>
    /// Searches <paramref name="cssData"/> for a pseudo-element block
    /// matching <paramref name="box"/> and <paramref name="pseudoElement"/>
    /// (e.g. <c>"::before"</c>).
    /// </summary>
    private static CssBlock FindPseudoElementBlock(CssBox box, CssData cssData, string pseudoElement)
    {
        CssBlock? merged = null;

        void MergeMatchingBlocks(string key)
        {
            foreach (var block in cssData.GetCssBlock(key))
            {
                if (block.Selectors != null && !IsBlockAssignableToBoxWithSelector(box, block))
                    continue;

                if (merged == null)
                    merged = block.Clone();
                else
                    merged.Merge(block);
            }
        }

        // General-to-specific merge order so later, more specific matching
        // pseudo-element rules override earlier ones like the normal cascade.
        MergeMatchingBlocks("*" + pseudoElement);
        MergeMatchingBlocks(box.HtmlTag.Name + pseudoElement);

        // Class-level: e.g. ".nose::before", "p.nose::before"
        if (box.HtmlTag.HasAttribute("class"))
        {
            var classes = box.HtmlTag.TryGetAttribute("class");
            var startIdx = 0;

            while (startIdx < classes.Length)
            {
                while (startIdx < classes.Length && classes[startIdx] == ' ')
                    startIdx++;
                if (startIdx >= classes.Length) break;

                var endIdx = classes.IndexOf(' ', startIdx);
                if (endIdx < 0) endIdx = classes.Length;

                var cls = classes.Substring(startIdx, endIdx - startIdx);
                MergeMatchingBlocks("." + cls + pseudoElement);
                MergeMatchingBlocks(box.HtmlTag.Name + "." + cls + pseudoElement);
                startIdx = endIdx + 1;
            }
        }

        // ID-level: e.g. "#myid::before"
        if (box.HtmlTag.HasAttribute("id"))
        {
            var id = box.HtmlTag.TryGetAttribute("id");
            MergeMatchingBlocks("#" + id + pseudoElement);
        }

        return merged;
    }

    /// <summary>
    /// Creates a pseudo-element <see cref="CssBox"/> as a child of
    /// <paramref name="parentBox"/> with styles from <paramref name="block"/>.
    /// For <c>::before</c>, the box is inserted as the first child;
    /// for <c>::after</c>, it is appended as the last child.
    /// </summary>
    private static void CreatePseudoElementBox(CssBox parentBox, CssBlock block, bool isBefore, Uri baseUrl)
    {
        // Determine content value — skip generation for "none" and "normal".
        string contentValue = null;
        if (block.Properties.TryGetValue("content", out string cv))
            contentValue = cv;

        if (contentValue == null || contentValue == "none" || contentValue == "normal")
            return;

        // Create the pseudo-element box and inherit from parent.
        CssBox pseudoBox;
        if (isBefore && parentBox.Boxes.Count > 0)
        {
            var firstChild = parentBox.Boxes[0];
            pseudoBox = CssBoxHelper.CreateBox(parentBox, before: firstChild, baseUrl: baseUrl);
        }
        else
        {
            pseudoBox = CssBoxHelper.CreateBox(parentBox, baseUrl);
        }

        // Apply pseudo-element CSS declarations.
        foreach (var prop in block.Properties)
        {
            var value = prop.Value;
            if (value == CssConstants.Inherit)
                value = CssUtils.GetPropertyValue(parentBox, prop.Key);
            CssUtils.SetPropertyValue(pseudoBox, prop.Key, value);
        }

        if (TryExtractPseudoElementImageUrl(contentValue, out var imageUrl))
        {
            // The image is rendered by the nested CssBoxImage below. Reset the
            // wrapper box's content value so the extracted URL is not retained as
            // generic generated content on the wrapper, which would otherwise
            // make later pseudo-box handling treat the wrapper as still owning
            // the original url(...) payload instead of the nested image box.
            pseudoBox.Content = CssConstants.Normal;

            var imageTag = new HtmlTag(
                HtmlConstants.Img,
                true,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["src"] = imageUrl
                });
            _ = new CssBoxImage(pseudoBox, imageTag, baseUrl);
            return;
        }

        // Set text content (strip surrounding quotes from CSS content value).
        var text = contentValue.Trim('\'', '"');
        if (text.Length > 0)
            pseudoBox.Text = text.AsMemory();
    }

    private static bool TryExtractPseudoElementImageUrl(string contentValue, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(contentValue))
            return false;

        var trimmed = contentValue.Trim();
        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            imageUrl = trimmed[4..^1].Trim();
        }
        else if (trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("./", StringComparison.Ordinal)
            || trimmed.StartsWith("../", StringComparison.Ordinal)
            || trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            imageUrl = trimmed;
        }
        else
        {
            return false;
        }
        if (imageUrl.Length >= 2 &&
            ((imageUrl[0] == '\'' && imageUrl[^1] == '\'') ||
             (imageUrl[0] == '"' && imageUrl[^1] == '"')))
        {
            imageUrl = imageUrl[1..^1];
        }

        return imageUrl.Length > 0;
    }

    private static bool IsStyleOnElementAllowed(CssBox box, string key, string value)
    {
        if (box.HtmlTag == null || key != HtmlConstants.Display)
            return true;

        return box.HtmlTag.Name switch
        {
            HtmlConstants.Table => value == CssConstants.Table,
            HtmlConstants.Tr => value == CssConstants.TableRow,
            HtmlConstants.Tbody => value == CssConstants.TableRowGroup,
            HtmlConstants.Thead => value == CssConstants.TableHeaderGroup,
            HtmlConstants.Tfoot => value == CssConstants.TableFooterGroup,
            HtmlConstants.Col => value == CssConstants.TableColumn,
            HtmlConstants.Colgroup => value == CssConstants.TableColumnGroup,
            HtmlConstants.Td or HtmlConstants.Th => value == CssConstants.TableCell,
            HtmlConstants.Caption => value == CssConstants.TableCaption,
            _ => true,
        };
    }

    private static void CloneCssData(ref CssData cssData, ref bool cssDataChanged)
    {
        if (cssDataChanged)
            return;

        cssDataChanged = true;
        cssData = cssData.Clone();
    }

    /// <summary>
    /// Returns <c>true</c> when the space-separated <c>rel</c> attribute value
    /// contains the token <c>stylesheet</c> (case-insensitive).
    /// This allows <c>&lt;link rel="appendix stylesheet"&gt;</c> to be recognised
    /// as a stylesheet link, as required by CSS2.1 §6.4.1 and the Acid2 test.
    /// </summary>
    private static bool ContainsStylesheetRel(string relValue)
    {
        if (string.IsNullOrEmpty(relValue))
            return false;

        foreach (var token in relValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Phase 2: Sets <see cref="CssBoxProperties.Kind"/>, list attributes
    /// (<see cref="CssBoxProperties.ListStart"/>, <see cref="CssBoxProperties.ListReversed"/>),
    /// and <see cref="CssBoxProperties.ImageSource"/> based on the HTML tag.
    /// This allows layout code to consume these properties instead of reading
    /// <see cref="HtmlTag"/> attributes directly.
    /// </summary>
    private static void AssignBoxKindAndAttributes(CssBox box)
    {
        var tag = box.HtmlTag;
        if (tag == null)
            return;

        box.Kind = tag.Name.ToLowerInvariant() switch
        {
            HtmlConstants.Img => BoxKind.ReplacedImage,
            HtmlConstants.Iframe => BoxKind.ReplacedIframe,
            HtmlConstants.Table => BoxKind.Table,
            HtmlConstants.Tr => BoxKind.TableRow,
            HtmlConstants.Td or HtmlConstants.Th => BoxKind.TableCell,
            HtmlConstants.Li => BoxKind.ListItem,
            HtmlConstants.Ol => BoxKind.OrderedList,
            HtmlConstants.Ul => BoxKind.UnorderedList,
            HtmlConstants.Hr => BoxKind.HorizontalRule,
            HtmlConstants.Br => BoxKind.LineBreak,
            HtmlConstants.A => BoxKind.Anchor,
            HtmlConstants.Font => BoxKind.Font,
            HtmlConstants.Input => BoxKind.Input,
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => BoxKind.Heading,
            "object" when box is CssBoxImage => BoxKind.ReplacedImage,
            _ => BoxKind.Anonymous,
        };

        // Populate list attributes for <ol> elements
        if (box.Kind == BoxKind.OrderedList)
        {
            box.ListReversed = tag.HasAttribute("reversed");
            if (int.TryParse(tag.TryGetAttribute("start"), out int start))
                box.ListStart = start;
        }

        // Populate image source for <img> and <object> image elements
        if (box.Kind == BoxKind.ReplacedImage)
            box.ImageSource = tag.TryGetAttribute("src") ?? tag.TryGetAttribute("data");
    }

    private void TranslateAttributes(HtmlTag tag, CssBox box)
    {
        if (!tag.HasAttributes())
            return;

        foreach (string att in tag.Attributes.Keys)
        {
            string value = tag.Attributes[att];

            switch (att)
            {
                case HtmlConstants.Align:
                    if (value == HtmlConstants.Left || value == HtmlConstants.Center || value == HtmlConstants.Right || value == HtmlConstants.Justify)
                        box.TextAlign = value.ToLower();
                    else
                        box.VerticalAlign = value.ToLower();
                    break;
                case HtmlConstants.Background:
                    box.BackgroundImage = value.ToLower();
                    break;
                case HtmlConstants.Bgcolor:
                    box.BackgroundColor = value.ToLower();
                    break;
                case HtmlConstants.Border:
                    if (!string.IsNullOrEmpty(value) && value != "0")
                        box.BorderLeftStyle = box.BorderTopStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                    box.BorderLeftWidth = box.BorderTopWidth = box.BorderRightWidth = box.BorderBottomWidth = TranslateLength(value);

                    if (tag.Name == HtmlConstants.Table)
                    {
                        if (value != "0")
                            ApplyTableBorder(box, "1px");
                    }
                    else
                    {
                        box.BorderTopStyle = box.BorderLeftStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                    }
                    break;
                case HtmlConstants.Bordercolor:
                    box.BorderLeftColor = box.BorderTopColor = box.BorderRightColor = box.BorderBottomColor = value.ToLower();
                    break;
                case HtmlConstants.Cellspacing:
                    box.BorderSpacing = TranslateLength(value);
                    break;
                case HtmlConstants.Cellpadding:
                    ApplyTablePadding(box, value);
                    break;
                case HtmlConstants.Color:
                    box.Color = value.ToLower();
                    break;
                case HtmlConstants.Dir:
                    box.Direction = value.ToLower();
                    break;
                case HtmlConstants.Face:
                    box.FontFamily = _cssParser.ParseFontFamily(value);
                    break;
                case HtmlConstants.Height:
                    box.Height = TranslateLength(value);
                    break;
                case HtmlConstants.Hspace:
                    box.MarginRight = box.MarginLeft = TranslateLength(value);
                    break;
                case HtmlConstants.Nowrap:
                    box.WhiteSpace = CssConstants.NoWrap;
                    break;
                case HtmlConstants.Size:
                    if (tag.Name.Equals(HtmlConstants.Hr, StringComparison.OrdinalIgnoreCase))
                        box.Height = TranslateLength(value);
                    else if (tag.Name.Equals(HtmlConstants.Font, StringComparison.OrdinalIgnoreCase))
                        box.FontSize = value;
                    else if (tag.Name.Equals(HtmlConstants.Input, StringComparison.OrdinalIgnoreCase))
                    {
                        // HTML5 §4.10.5.3.7: The size attribute on <input>
                        // specifies the visible width in average-character
                        // units.  Approximate using ~8px per character
                        // (roughly 1ex at 13.3333px Arial, matching
                        // Chromium's default rendering of size=20 → ~173px).
                        const double AvgCharWidthPx = 8.05;
                        const double InputPaddingBorderPx = 12; // padding + border on both sides
                        if (int.TryParse(value, out int chars) && chars > 0)
                            box.Width = $"{chars * AvgCharWidthPx + InputPaddingBorderPx}px";
                    }
                    break;
                case HtmlConstants.Valign:
                    box.VerticalAlign = value.ToLower();
                    break;
                case HtmlConstants.Vspace:
                    box.MarginTop = box.MarginBottom = TranslateLength(value);
                    break;
                case HtmlConstants.Width:
                    box.Width = TranslateLength(value);
                    break;
            }
        }
    }

    private static string TranslateLength(string htmlLength)
    {
        CssLength len = new(htmlLength);

        if (len.HasError)
            return $"{htmlLength}px";

        return htmlLength;
    }

    private static void ApplyTableBorder(CssBox table, string border) => SetForAllCells(table, cell =>
    {
        cell.BorderLeftStyle = cell.BorderTopStyle = cell.BorderRightStyle = cell.BorderBottomStyle = CssConstants.Solid;
        cell.BorderLeftWidth = cell.BorderTopWidth = cell.BorderRightWidth = cell.BorderBottomWidth = border;
    });

    private static void ApplyTablePadding(CssBox table, string padding)
    {
        var length = TranslateLength(padding);
        SetForAllCells(table, cell => cell.PaddingLeft = cell.PaddingTop = cell.PaddingRight = cell.PaddingBottom = length);
    }

    private static void SetForAllCells(CssBox table, ActionInt<CssBox> action)
    {
        foreach (var l1 in table.Boxes)
        {
            foreach (var l2 in l1.Boxes)
            {
                if (l2.HtmlTag != null && l2.HtmlTag.Name == "td")
                {
                    action(l2);
                }
                else
                {
                    foreach (var l3 in l2.Boxes)
                    {
                        action(l3);
                    }
                }
            }
        }
    }

    private static void CorrectTextBoxes(CssBox box)
    {
        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            var childBox = box.Boxes[i];
            if (!childBox.Text.IsEmpty)
            {
                // is the box has text
                var keepBox = !childBox.Text.Span.IsWhiteSpace();

                // is the box is pre-formatted
                keepBox = keepBox || childBox.WhiteSpace == CssConstants.Pre || childBox.WhiteSpace == CssConstants.PreWrap;

                // is the box is only one in the parent
                keepBox = keepBox || box.Boxes.Count == 1;

                // is it a whitespace between two inline boxes
                keepBox = keepBox || (i > 0 && i < box.Boxes.Count - 1 && box.Boxes[i - 1].IsInline && box.Boxes[i + 1].IsInline);

                // is first/last box where is in inline box and it's next/previous box is inline
                keepBox = keepBox || (i == 0 && box.Boxes.Count > 1 && box.Boxes[1].IsInline && box.IsInline) || (i == box.Boxes.Count - 1 && box.Boxes.Count > 1 && box.Boxes[i - 1].IsInline && box.IsInline);

                if (keepBox)
                {
                    // valid text box, parse it to words
                    childBox.ParseToWords();
                }
                else
                {
                    // remove text box that has no 
                    childBox.ParentBox.Boxes.RemoveAt(i);
                }
            }
            else
            {
                // recursive
                CorrectTextBoxes(childBox);
            }
        }
    }

    private static void CorrectImgBoxes(CssBox box, Uri baseUrl)
    {
        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            var childBox = box.Boxes[i];
            if (childBox is CssBoxImage && childBox.Display == CssConstants.Block)
            {
                var block = CssBoxHelper.CreateBlock(childBox.ParentBox, baseUrl, null, childBox);
                childBox.ParentBox = block;
                childBox.Display = CssConstants.Inline;
            }
            else
            {
                // recursive
                CorrectImgBoxes(childBox, baseUrl);
            }
        }
    }

    /// <summary>
    /// Implements the <c>&lt;object&gt;</c> fallback chain (HTML4 §13.3):
    /// when an <c>&lt;object&gt;</c> element's <c>data</c> attribute points to a
    /// supported image (<c>data:image/…</c>), it is rendered as a replaced image
    /// and its children (fallback content) are removed.  Otherwise, children
    /// are kept as fallback content.
    /// </summary>
    private static void CorrectObjectBoxes(CssBox box)
    {
        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            var childBox = box.Boxes[i];

            if (childBox is CssBoxImage &&
                childBox.HtmlTag != null &&
                childBox.HtmlTag.Name.Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                // This <object> was promoted to CssBoxImage because its data
                // attribute contains a data:image URI.  Remove fallback children
                // so only the image renders.
                childBox.Boxes.Clear();
            }

            // Recurse into all children (including non-object boxes)
            CorrectObjectBoxes(childBox);
        }
    }

    private static void CorrectLineBreaksBlocks(CssBox box, ref bool followingBlock)
    {
        followingBlock = followingBlock || box.IsBlock;

        foreach (var childBox in box.Boxes)
        {
            // CSS2.1 §9.6: Out-of-flow positioned elements do not participate
            // in the in-flow block/inline sequence that governs <br> heights.
            // Process their subtrees with an isolated state so a block-level
            // absolutely-positioned element does not make a following <br>
            // generate a spurious empty line.
            if (childBox.Position is CssConstants.Absolute or CssConstants.Fixed)
            {
                bool isolated = false;
                CorrectLineBreaksBlocks(childBox, ref isolated);
                continue;
            }

            CorrectLineBreaksBlocks(childBox, ref followingBlock);
            followingBlock = childBox.Words.Count == 0 && (followingBlock || childBox.IsBlock);
        }

        int lastBr = -1;
        CssBox brBox;

        do
        {
            brBox = null;
            for (int i = 0; i < box.Boxes.Count && brBox == null; i++)
            {
                if (i > lastBr && box.Boxes[i].IsBrElement)
                {
                    brBox = box.Boxes[i];
                    lastBr = i;
                }
                else if (box.Boxes[i].Position is CssConstants.Absolute or CssConstants.Fixed)
                {
                    // Out-of-flow: transparent to the in-flow block/inline run.
                }
                else if (box.Boxes[i].Words.Count > 0)
                {
                    followingBlock = false;
                }
                else if (box.Boxes[i].IsBlock)
                {
                    followingBlock = true;
                }
            }

            if (brBox != null)
            {
                brBox.Display = CssConstants.Block;
                if (followingBlock)
                    brBox.Height = ".95em"; // TODO:a check the height to min-height when it is supported
            }
        } while (brBox != null);
    }

    private static void CorrectBlockInsideInline(CssBox box, Uri baseUrl)
    {
        try
        {
            // CSS Flexbox §4 / CSS Grid §7: All direct children of a
            // flex/grid container become flex/grid items — they must NOT
            // be rearranged by the block-inside-inline correction (which
            // wraps block children in anonymous boxes and merges them).
            //
            // CSS2.1 §9.2.1.1 / §10.3.9: Inline-block boxes establish a
            // new block formatting context for their children.  Block-level
            // children inside an inline-block are valid and must NOT be
            // split out by the block-inside-inline correction.  Without
            // this skip, <span style="display:inline-block"> containing a
            // <span style="display:block"> is incorrectly unwrapped,
            // causing the block child to expand to the full container width
            // instead of being constrained by the inline-block's
            // shrink-to-fit sizing (e.g. Google.de button wrappers).
            if (box.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                or CssConstants.InlineBlock)
            {
                // Still recurse into children — the children themselves
                // may contain nested inline contexts that need correction.
                foreach (var childBox in box.Boxes)
                    CorrectBlockInsideInline(childBox, baseUrl);
                return;
            }

            if (DomUtils.ContainsInlinesOnly(box) && !ContainsInlinesOnlyDeep(box))
            {
                var tempRightBox = CorrectBlockInsideInlineImp(box, baseUrl);
                while (tempRightBox != null)
                {
                    // loop on the created temp right box for the fixed box until no more need (optimization remove recursion)
                    CssBox newTempRightBox = null;
                    if (DomUtils.ContainsInlinesOnly(tempRightBox) && !ContainsInlinesOnlyDeep(tempRightBox))
                        newTempRightBox = CorrectBlockInsideInlineImp(tempRightBox, baseUrl);

                    tempRightBox.ParentBox.SetAllBoxes(tempRightBox);
                    tempRightBox.ParentBox = null;
                    tempRightBox = newTempRightBox;
                }
            }

            if (!DomUtils.ContainsInlinesOnly(box))
            {
                foreach (var childBox in box.Boxes)
                    CorrectBlockInsideInline(childBox, baseUrl);
            }
        }
        catch (Exception ex)
        {
            box.ContainerInt.ReportError(HtmlRenderErrorType.HtmlParsing, "Failed in block inside inline box correction", ex);
        }
    }

    /// <summary>
    /// Rearrange the DOM of the box to have block box with boxes before the inner block box and after.
    /// </summary>
    /// <param name="box">the box that has the problem</param>
    private static CssBox CorrectBlockInsideInlineImp(CssBox box, Uri baseUrl)
    {
        // CSS2.1 §9.2.1.1: When an inline element contains a block-level
        // child, the inline is broken around the block into anonymous block
        // boxes.  If the inline had position:relative/absolute/fixed (i.e.
        // established a containing block for absolutely-positioned descendants),
        // the hoisted blocks lose their parent–child relationship in the box
        // tree.  Record the original positioned ancestor so that
        // FindPositionedContainingBlock() can still find it.
        bool wasPositioned = box.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed;
        // Also inherit any split-positioned-ancestor from a higher level.
        CssBox splitAncestor = wasPositioned ? box : box.SplitPositionedAncestor;
        if (box.Display == CssConstants.Inline)
            box.Display = CssConstants.Block;

        if (box.Boxes.Count > 1 || box.Boxes[0].Boxes.Count > 1)
        {
            var leftBlock = CssBoxHelper.CreateBlock(box, baseUrl);

            while (ContainsInlinesOnlyDeep(box.Boxes[0]))
                box.Boxes[0].ParentBox = leftBlock;
            leftBlock.SetBeforeBox(box.Boxes[0]);

            var splitBox = box.Boxes[1];
            splitBox.ParentBox = null;

            CorrectBlockSplitBadBoxCore(box, splitBox, leftBlock, baseUrl, splitAncestor);

            // remove block that did not get any inner elements
            if (leftBlock.Boxes.Count < 1)
                leftBlock.ParentBox = null;

            // Propagate the positioned ancestor link to hoisted children.
            if (splitAncestor != null)
            {
                foreach (var child in box.Boxes)
                    PropagateSplitPositionedAncestor(child, splitAncestor);
            }

            int minBoxes = leftBlock.ParentBox != null ? 2 : 1;
            if (box.Boxes.Count > minBoxes)
            {
                // create temp box to handle the tail elements and then get them back so no deep hierarchy is created
                var tempRightBox = CssBoxHelper.CreateBox(box, baseUrl, null, box.Boxes[minBoxes]);
                while (box.Boxes.Count > minBoxes + 1)
                    box.Boxes[minBoxes + 1].ParentBox = tempRightBox;

                if (splitAncestor != null)
                    PropagateSplitPositionedAncestor(tempRightBox, splitAncestor);

                return tempRightBox;
            }
        }
        else if (box.Boxes[0].Display == CssConstants.Inline)
        {
            box.Boxes[0].Display = CssConstants.Block;
        }

        return null;
    }

    /// <summary>
    /// Recursively propagate <see cref="CssBox.SplitPositionedAncestor"/>
    /// down through a subtree that was hoisted out of a positioned inline
    /// by the block-inside-inline correction.  Stops recursing when it
    /// reaches a box that already has its own positioned role.
    /// </summary>
    private static void PropagateSplitPositionedAncestor(CssBox box, CssBox ancestor)
    {
        // Don't override if the box itself is positioned — it forms its
        // own containing block.
        if (box.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed)
            return;

        box.SplitPositionedAncestor ??= ancestor;

        foreach (var child in box.Boxes)
            PropagateSplitPositionedAncestor(child, ancestor);
    }

    private static void CorrectBlockSplitBadBox(CssBox parentBox, CssBox badBox, CssBox leftBlock, Uri baseUrl)
    {
        CorrectBlockSplitBadBoxCore(parentBox, badBox, leftBlock, baseUrl, null);
    }

    /// <summary>
    /// Core implementation of block-inside-inline split.  <paramref name="posAncestor"/>
    /// tracks the closest positioned ancestor that was stripped away during
    /// recursive splitting so that hoisted blocks can carry a
    /// <see cref="CssBox.SplitPositionedAncestor"/> reference.
    /// </summary>
    private static void CorrectBlockSplitBadBoxCore(CssBox parentBox, CssBox badBox, CssBox leftBlock, Uri baseUrl, CssBox posAncestor)
    {
        // If the box being split is positioned, it becomes the reference
        // for any blocks hoisted out of its subtree.
        if (badBox.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed)
            posAncestor = badBox;

        CssBox leftbox = null;
        while (badBox.Boxes[0].IsInline && ContainsInlinesOnlyDeep(badBox.Boxes[0]))
        {
            if (leftbox == null)
            {
                // if there is no elements in the left box there is no reason to keep it
                leftbox = CssBoxHelper.CreateBox(leftBlock, baseUrl, badBox.HtmlTag);
                leftbox.InheritStyle(badBox, true);
            }
            badBox.Boxes[0].ParentBox = leftbox;
        }

        // If badBox is the positioned ancestor being split, register the
        // left-side copy as a fragment so GetInlineBoundingBox can find it.
        if (leftbox != null && posAncestor == badBox)
            posAncestor.AddSplitFragment(leftbox);

        var splitBox = badBox.Boxes[0];
        if (!ContainsInlinesOnlyDeep(splitBox))
        {
            CorrectBlockSplitBadBoxCore(parentBox, splitBox, leftBlock, baseUrl, posAncestor);
            splitBox.ParentBox = null;
        }
        else
        {
            splitBox.ParentBox = parentBox;
            // The block being hoisted to parentBox was originally a
            // descendant of a positioned inline.  Record the link.
            if (posAncestor != null)
                SetSplitAncestorDeep(splitBox, posAncestor);
        }

        if (badBox.Boxes.Count > 0)
        {
            CssBox rightBox;
            if (splitBox.ParentBox != null || parentBox.Boxes.Count < 3)
            {
                rightBox = CssBoxHelper.CreateBox(parentBox, baseUrl, badBox.HtmlTag);
                rightBox.InheritStyle(badBox, true);

                if (parentBox.Boxes.Count > 2)
                    rightBox.SetBeforeBox(parentBox.Boxes[1]);

                if (splitBox.ParentBox != null)
                    splitBox.SetBeforeBox(rightBox);
            }
            else
            {
                rightBox = parentBox.Boxes[2];
            }

            rightBox.SetAllBoxes(badBox);

            // Register the right-side copy as a fragment of the
            // positioned ancestor so GetInlineBoundingBox includes it.
            if (posAncestor == badBox)
                posAncestor.AddSplitFragment(rightBox);

            // Also tag the right-side anonymous block.
            if (posAncestor != null)
                SetSplitAncestorDeep(rightBox, posAncestor);
        }
        else if (splitBox.ParentBox != null && parentBox.Boxes.Count > 1)
        {
            splitBox.SetBeforeBox(parentBox.Boxes[1]);
            if (splitBox.HtmlTag != null && splitBox.HtmlTag.Name == "br" && (leftbox != null || leftBlock.Boxes.Count > 1))
                splitBox.Display = CssConstants.Inline;
        }
    }

    /// <summary>
    /// Set <see cref="CssBox.SplitPositionedAncestor"/> on a box and all
    /// its descendants, stopping at boxes that are themselves positioned.
    /// </summary>
    private static void SetSplitAncestorDeep(CssBox box, CssBox ancestor)
    {
        if (box.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed)
            return;
        box.SplitPositionedAncestor ??= ancestor;
        foreach (var child in box.Boxes)
            SetSplitAncestorDeep(child, ancestor);
    }

    private static void CorrectInlineBoxesParent(CssBox box, Uri baseUrl)
    {
        // CSS Flexbox §4 / CSS Grid §7: All direct children of a
        // flex/grid container are flex/grid items — do not wrap inline
        // children in anonymous block boxes.
        //
        // CSS2.1 §9.2.1.1 / §10.3.9: Inline-block boxes establish a new
        // block formatting context — their children (block or inline) are
        // laid out internally and must not be rearranged by this
        // correction.
        if (box.Display is not ("flex" or "inline-flex" or "grid" or "inline-grid"
                or CssConstants.InlineBlock)
            && ContainsVariantBoxes(box))
        {
            for (int i = 0; i < box.Boxes.Count; i++)
            {
                if (box.Boxes[i].IsInline)
                {
                    var newbox = CssBoxHelper.CreateBlock(box, baseUrl, null, box.Boxes[i++]);
                    while (i < box.Boxes.Count && box.Boxes[i].IsInline)
                        box.Boxes[i].ParentBox = newbox;
                }
            }
        }

        if (!DomUtils.ContainsInlinesOnly(box))
        {
            foreach (var childBox in box.Boxes)
                CorrectInlineBoxesParent(childBox, baseUrl);
        }
    }

    private static bool ContainsInlinesOnlyDeep(CssBox box)
    {
        foreach (var childBox in box.Boxes)
        {
            // CSS2.1 §9.5: Floats are out-of-flow and should not trigger
            // block-inside-inline corrections.  Skip them when checking
            // whether a box contains only inline content.
            if (childBox.Float != CssConstants.None)
                continue;

            // CSS2.1 §9.6: Absolutely and fixed positioned elements are also
            // out of flow — they are blockified (§9.7) but, like floats, do
            // not break the surrounding inline formatting context.  Their
            // static position is resolved during inline layout, so they must
            // not trigger the block-inside-inline correction either.
            if (childBox.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            if (!childBox.IsInline)
                return false;

            // CSS2.1 §9.2.1.1 / §10.3.9: Inline-block boxes establish a
            // new block formatting context.  Their block-level children are
            // contained within the inline-block and do NOT constitute
            // "block inside inline" at the outer level.  Stop recursing
            // into inline-block children so that, e.g., <span display:
            // inline-block> containing <span display:block> does not
            // trigger the block-inside-inline correction on the parent.
            //
            // Same applies to flex/grid containers — their children are
            // laid out internally and must not be inspected here.
            if (childBox.Display is CssConstants.InlineBlock
                or "flex" or "inline-flex" or "grid" or "inline-grid")
                continue;

            if (!ContainsInlinesOnlyDeep(childBox))
                return false;
        }

        return true;
    }

    private static bool ContainsVariantBoxes(CssBox box)
    {
        bool hasBlock = false;
        bool hasInline = false;

        for (int i = 0; i < box.Boxes.Count && (!hasBlock || !hasInline); i++)
        {
            // CSS2.1 §9.5: Floats are out-of-flow — they do not create a
            // mixed inline/block situation that requires anonymous block
            // wrapping.
            if (box.Boxes[i].Float != CssConstants.None)
                continue;
            var isBlock = !box.Boxes[i].IsInline;
            hasBlock = hasBlock || isBlock;
            hasInline = hasInline || !isBlock;
        }

        return hasBlock && hasInline;
    }

    /// <summary>
    /// Normalises an HTML dimension attribute value (width/height) to a CSS
    /// length.  Pure numeric values (e.g. "100") get a "px" suffix; values
    /// that already carry a unit or percentage (e.g. "100%", "10em") are
    /// returned unchanged after trimming.
    /// </summary>
    private static string NormaliseDimensionAttribute(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return "0px";

        // If the value already ends with a known unit or %, keep it as-is.
        if (trimmed[^1] == '%'
            || trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("em", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("pt", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("vw", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        // Otherwise treat as a unitless pixel value.
        return trimmed + "px";
    }
}
