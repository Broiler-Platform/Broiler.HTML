using System.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.HTML.Dom.Parse;
using Broiler.HTML.Dom;
using HtmlConstants = Broiler.HTML.Utils.HtmlConstants;
using Broiler.HTML.Utils;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core;

using HtmlTag = Broiler.Layout.HtmlTag;
using BoxKind = Broiler.Layout.BoxKind;
using Broiler.CSS;
using CssConstants = Broiler.CSS.CssConstants;
using Broiler.Layout.Engine;
using Broiler.Graphics;
namespace Broiler.HTML.Orchestration.Parse;

internal sealed class DomParser
{
    private readonly IStylesheetLoader _stylesheetLoader;

    // HTML presentation attributes (cellspacing/cellpadding) are projected as
    // low-priority hints that, per the CSS cascade, outrank the UA origin but lose
    // to author/inline declarations. We record which CSS longhands each box took
    // from such a hint so the cascade projection can preserve them when the only
    // competing declaration is a user-agent rule (e.g. `td { padding: 1px }`).
    private readonly Dictionary<CssBox, HashSet<string>> _presentationalHints = new();

    // Author-origin-only cascade (stylesheets + inline), used to detect whether a
    // presentation-hint property is also claimed by an author declaration.
    private CSS.Dom.CssStyleEngine? _authorEngine;

    public DomParser(IStylesheetLoader stylesheetLoader)
    {
        ArgumentNullException.ThrowIfNull(stylesheetLoader);
        _stylesheetLoader = stylesheetLoader;
    }

    private void RecordPresentationalHint(CssBox box, params string[] cssLonghands)
    {
        if (!_presentationalHints.TryGetValue(box, out var set))
            _presentationalHints[box] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var longhand in cssLonghands)
            set.Add(longhand);
    }

    public CssBox GenerateCssTree(string html, HtmlContainerInt htmlContainer, ref HtmlStyleSet styleSet, Uri baseUrl)
    {
        var root = HtmlParser.ParseDocument(html, baseUrl);
        return PrepareCssTree(root, htmlContainer, ref styleSet, baseUrl);
    }

    public CssBox GenerateCssTree(Broiler.Dom.DomDocument document, HtmlContainerInt htmlContainer, ref HtmlStyleSet styleSet, Uri baseUrl)
    {
        var root = HtmlParser.ParseDocument(document, baseUrl);
        return PrepareCssTree(root, htmlContainer, ref styleSet, baseUrl);
    }

    private CssBox PrepareCssTree(CssBox root, HtmlContainerInt htmlContainer, ref HtmlStyleSet styleSet, Uri baseUrl)
    {
        if (root == null)
            return root;

        root.ContainerInt = htmlContainer;
        // Bind the layout environment at construction so font/colour and the
        // initial-containing-block inputs resolve through it (roadmap §4, Phase 4 prep).
        root.LayoutEnvironment = new HtmlLayoutEnvironment(htmlContainer);

        CascadeParseStyles(root, ref styleSet);

        // Resolve every stylesheet, inline declaration, generated pseudo-element,
        // animation, and ::selection rule through the shared model and style engine.
        var viewport = htmlContainer?.ViewportSize ?? default;
        var canonicalDocument = SharedRendererCascade.FindCanonicalDocument(root);
        Broiler.CSS.Dom.CssStyleEngine engine = SharedRendererCascade.BuildEngine(
            canonicalDocument,
            styleSet,
            (int)viewport.Width,
            (int)viewport.Height);
        _authorEngine = SharedRendererCascade.BuildAuthorEngine(
            canonicalDocument,
            styleSet,
            (int)viewport.Width,
            (int)viewport.Height);

        var combinedStyleSheet = styleSet.StyleSheet;
        CascadeApplyStyles(
            root,
            styleSet,
            baseUrl,
            engine,
            RendererStyleQueries.HasGeneratedPseudoElementRules(combinedStyleSheet, before: true),
            RendererStyleQueries.HasGeneratedPseudoElementRules(combinedStyleSheet, before: false));
        SetTextSelectionStyle(htmlContainer, root, engine);
        CorrectTextBoxes(root);
        CorrectImgBoxes(root, baseUrl);
        CorrectObjectBoxes(root);
        CorrectFramesetBoxes(root);

        bool followingBlock = true;
        CorrectLineBreaksBlocks(root, ref followingBlock);
        CorrectInlineBoxesParent(root, baseUrl);
        CorrectBlockInsideInline(root, baseUrl);
        CorrectInlineBoxesParent(root, baseUrl);

        return root;
    }

    private void CascadeParseStyles(CssBox box, ref HtmlStyleSet styleSet)
    {
        if (box.HtmlTag != null)
        {
            // CSSOM §2.3 / HTML §4.2.6: a disabled stylesheet does not apply.
            // `HTMLLinkElement.disabled` / `HTMLStyleElement.disabled` (set from
            // script) are reflected onto the element as a `disabled` attribute, so
            // skip collecting rules from a <link>/<style> that carries it.
            bool sheetDisabled = box.GetAttribute("disabled", null) != null;

            // Check for the <link rel=stylesheet> tag
            // Per CSS2.1 §6.4.1, the rel attribute is a space-separated list;
            // match if any token equals "stylesheet" (e.g. rel="appendix stylesheet").
            if (!sheetDisabled &&
                box.HtmlTag.Name.Equals("link", StringComparison.CurrentCultureIgnoreCase) &&
                ContainsStylesheetRel(box.GetAttribute("rel", string.Empty)))
            {
                _stylesheetLoader.LoadStylesheet(box.GetAttribute("href", string.Empty), (Dictionary<string, string>)box.HtmlTag.Attributes, out string stylesheet, out Broiler.CSS.CssStyleSheet stylesheetModel);
                if (stylesheet != null)
                    styleSet = styleSet.AppendAuthorStyleSheet(new Broiler.CSS.CssParser().ParseStyleSheet(stylesheet));
                else if (stylesheetModel != null)
                    styleSet = styleSet.AppendAuthorStyleSheet(stylesheetModel);
            }

            // Check for the <style> tag
            if (!sheetDisabled &&
                box.HtmlTag.Name.Equals("style", StringComparison.CurrentCultureIgnoreCase) && box.Boxes.Count > 0)
            {
                foreach (var child in box.Boxes)
                    styleSet = styleSet.AppendAuthorStyleSheet(
                        new Broiler.CSS.CssParser().ParseStyleSheet(StripCdataSection(child.Text.ToString())));
            }
        }

        foreach (var childBox in box.Boxes)
            CascadeParseStyles(childBox, ref styleSet);
    }


    private void CascadeApplyStyles(
        CssBox box,
        HtmlStyleSet styleSet,
        Uri baseUrl,
        CSS.Dom.CssStyleEngine engine,
        bool hasBeforeRules,
        bool hasAfterRules)
    {
        box.InheritStyle();

        if (box.HtmlTag != null)
        {
            // Presentation attributes are low-priority author hints. Project the shared
            // origin-aware stylesheet + inline cascade over them. Every element box carries
            // a SourceElement, so this is the only author/UA cascade path.
            TranslateAttributes(box.HtmlTag, box);

            if (engine != null && box.SourceElement != null)
            {
                _presentationalHints.TryGetValue(box, out var hintKeys);
                SharedRendererCascade.ProjectCascadedStyle(box, engine, _authorEngine, hintKeys);
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
        CssAnimationResolver.ResolveAnimations(box, styleSet.AuthorStyleSheet);

        foreach (var childBox in box.Boxes)
            CascadeApplyStyles(childBox, styleSet, baseUrl, engine, hasBeforeRules, hasAfterRules);

        if (box.HtmlTag != null)
            ApplyClosedDetailsVisibility(box);

        if (box.HtmlTag != null)
            ApplySummaryDisclosureMarker(box, baseUrl);

        // CSS2.1 §12.1: Generate ::before and ::after pseudo-element boxes
        // after child style cascading to avoid modifying the child list
        // during iteration.
        if (box.HtmlTag != null && (hasBeforeRules || hasAfterRules))
            ApplyPseudoElementBoxes(box, engine, baseUrl, hasBeforeRules, hasAfterRules);
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

    private static void SetTextSelectionStyle(
        HtmlContainerInt htmlContainer,
        CssBox root,
        Broiler.CSS.Dom.CssStyleEngine engine)
    {
        htmlContainer.SelectionForeColor = BColor.Empty;
        htmlContainer.SelectionBackColor = BColor.Empty;

        if (engine == null || SharedRendererCascade.FindCanonicalDocument(root)?.DocumentElement is not { } element)
            return;

        var style = engine.GetCascadedStyle(element, "::selection");
        if (style.TryGetValue("color", out var foreground))
            htmlContainer.SelectionForeColor = htmlContainer.ParseCssColor(foreground);

        if (style.TryGetValue("background-color", out var background))
            htmlContainer.SelectionBackColor = htmlContainer.ParseCssColor(background);
    }

    /// <summary>
    /// Creates generated-content boxes from the shared pseudo-element cascade.
    /// </summary>
    private static void ApplyPseudoElementBoxes(
        CssBox box,
        Broiler.CSS.Dom.CssStyleEngine engine,
        Uri baseUrl,
        bool hasBeforeRules,
        bool hasAfterRules)
    {
        if (engine == null || box.SourceElement == null)
            return;

        if (hasBeforeRules)
        {
            var before = engine.GetCascadedStyle(box.SourceElement, "::before");
            if (before.ContainsKey("content"))
                CreatePseudoElementBox(box, before, isBefore: true, baseUrl);
        }

        if (hasAfterRules)
        {
            var after = engine.GetCascadedStyle(box.SourceElement, "::after");
            if (after.ContainsKey("content"))
                CreatePseudoElementBox(box, after, isBefore: false, baseUrl);
        }
    }

    /// <summary>
    /// Creates a pseudo-element <see cref="CssBox"/> as a child of
    /// <paramref name="parentBox"/> with styles from <paramref name="properties"/>.
    /// For <c>::before</c>, the box is inserted as the first child;
    /// for <c>::after</c>, it is appended as the last child.
    /// </summary>
    private static void CreatePseudoElementBox(
        CssBox parentBox,
        IReadOnlyDictionary<string, string> properties,
        bool isBefore,
        Uri baseUrl)
    {
        // Determine content value — skip generation for "none" and "normal".
        string contentValue = null;
        if (properties.TryGetValue("content", out string cv))
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
        foreach (var prop in properties)
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
                    {
                        box.BorderLeftStyle = box.BorderTopStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                        // Legacy `<table border>` paints grey borders by default;
                        // previously supplied by a UA `table { border-color }`
                        // rule, removed because it blocked author shorthands
                        // (CssDefaults.cs). Apply the grey directly on the
                        // attribute path so author CSS is unaffected.
                        box.BorderLeftColor = box.BorderTopColor = box.BorderRightColor = box.BorderBottomColor = "#dfdfdf";
                    }
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
                    RecordPresentationalHint(box, "border-spacing");
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
                    box.FontFamily = RendererStyleQueries.UnescapeIdentifier(
                        value.Split(',')[0].Trim().Trim('"', '\''));
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
        return CSS.CssLengthParser.IsValidLength(htmlLength)
            ? htmlLength
            : $"{htmlLength}px";
    }

    // XHTML wraps inline <style> CSS in a CDATA section
    // (<![CDATA[ ... ]]>) so the markup validates as XML. When such a document
    // is parsed by the HTML tree builder the markers stay as literal text inside
    // the style element, and the CSS parser cannot tokenize "<![CDATA[" / "]]>"
    // — it drops the rules, so the whole stylesheet is silently lost (every
    // CDATA-wrapped CSS2 .xht reftest, WPT issue #1143). The markers are never
    // valid CSS, so strip them before parsing.
    private static string StripCdataSection(string css)
    {
        if (string.IsNullOrEmpty(css) || css.IndexOf("CDATA", StringComparison.Ordinal) < 0)
            return css;
        return css.Replace("<![CDATA[", string.Empty, StringComparison.Ordinal)
                  .Replace("]]>", string.Empty, StringComparison.Ordinal);
    }

    private static void ApplyTableBorder(CssBox table, string border) => SetForAllCells(table, cell =>
    {
        cell.BorderLeftStyle = cell.BorderTopStyle = cell.BorderRightStyle = cell.BorderBottomStyle = CssConstants.Solid;
        cell.BorderLeftWidth = cell.BorderTopWidth = cell.BorderRightWidth = cell.BorderBottomWidth = border;
        // Legacy `<table border>` cells render with the UA grey border color.
        // Previously this came from a blanket `td, th { border-color:#dfdfdf }`
        // UA rule, which was removed because it blocked author `border`
        // shorthands (CssDefaults.cs); set the grey here so the attribute path
        // keeps its default while author CSS on cells is unaffected.
        cell.BorderLeftColor = cell.BorderTopColor = cell.BorderRightColor = cell.BorderBottomColor = "#dfdfdf";
    });

    private void ApplyTablePadding(CssBox table, string padding)
    {
        var length = TranslateLength(padding);
        SetForAllCells(table, cell =>
        {
            cell.PaddingLeft = cell.PaddingTop = cell.PaddingRight = cell.PaddingBottom = length;
            RecordPresentationalHint(cell, "padding-left", "padding-top", "padding-right", "padding-bottom");
        });
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
    /// <summary>
    /// Lays out <c>&lt;frameset&gt;</c> / <c>&lt;frame&gt;</c> as a nested-browsing-
    /// context grid (HTML §"the frameset element"): the frameset partitions its area
    /// per its <c>cols</c>/<c>rows</c> attributes and each frame (or nested frameset)
    /// fills one cell.  A cell's document is rasterised by the image renderer.  The
    /// outermost frameset fills the viewport; nested framesets fill their parent cell.
    /// <c>&lt;noframes&gt;</c> fallback content is hidden because frames are supported.
    /// </summary>
    private static void CorrectFramesetBoxes(CssBox box)
    {
        bool isFrameset = box.HtmlTag != null
            && box.HtmlTag.Name.Equals("frameset", StringComparison.OrdinalIgnoreCase);
        bool parentIsFrameset = box.ParentBox?.HtmlTag != null
            && box.ParentBox.HtmlTag.Name.Equals("frameset", StringComparison.OrdinalIgnoreCase);

        if (isFrameset)
        {
            if (!parentIsFrameset)
            {
                // Outermost frameset: fill the viewport, overriding any inherited
                // body margin.  Fixed positioning resolves 100%/offsets against the
                // initial containing block (the viewport).
                box.Position = CssConstants.Fixed;
                box.Left = "0";
                box.Top = "0";
                box.Width = "100%";
                box.Height = "100%";
                box.MarginLeft = box.MarginTop = box.MarginRight = box.MarginBottom = "0";
            }
            LayoutFramesetChildren(box);
        }

        foreach (var child in box.Boxes)
            CorrectFramesetBoxes(child);
    }

    private static void LayoutFramesetChildren(CssBox frameset)
    {
        // Cells are <frame> and nested <frameset> children; everything else
        // (<noframes>, stray text) is fallback and must not paint.
        var cells = new List<CssBox>();
        foreach (var child in frameset.Boxes)
        {
            string name = child.HtmlTag?.Name;
            if (string.Equals(name, "frame", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "frameset", StringComparison.OrdinalIgnoreCase))
                cells.Add(child);
            else if (string.Equals(name, "noframes", StringComparison.OrdinalIgnoreCase))
                child.Display = CssConstants.None;
        }

        if (cells.Count == 0)
            return;

        // cols → columns, rows → rows; missing dimension is a single track.
        var colPercents = ParseFramesetSpec(frameset.GetAttribute("cols"), nominalTotal: 1024);
        var rowPercents = ParseFramesetSpec(frameset.GetAttribute("rows"), nominalTotal: 768);
        if (colPercents.Count == 0) colPercents = [100.0];
        if (rowPercents.Count == 0) rowPercents = [100.0];

        // Cells fill the grid row-major (HTML frameset layout order).
        int cols = colPercents.Count;
        int rows = rowPercents.Count;

        double[] colLeft = new double[cols];
        for (int c = 1; c < cols; c++)
            colLeft[c] = colLeft[c - 1] + colPercents[c - 1];
        double[] rowTop = new double[rows];
        for (int r = 1; r < rows; r++)
            rowTop[r] = rowTop[r - 1] + rowPercents[r - 1];

        for (int i = 0; i < cells.Count; i++)
        {
            int r = i / cols;
            int c = i % cols;
            if (r >= rows)
                break; // more frames than cells — extras are not rendered

            var cell = cells[i];
            cell.Position = CssConstants.Absolute;
            cell.Left = FormatPercent(colLeft[c]);
            cell.Top = FormatPercent(rowTop[r]);
            cell.Width = FormatPercent(colPercents[c]);
            cell.Height = FormatPercent(rowPercents[r]);
            cell.MarginLeft = cell.MarginTop = cell.MarginRight = cell.MarginBottom = "0";
        }
    }

    private static string FormatPercent(double value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Parses a frameset <c>cols</c>/<c>rows</c> spec (comma-separated
    /// <c>*</c> / <c>N*</c> / <c>N%</c> / <c>N</c>) into per-track percentages of
    /// the frameset that sum to ~100.  Pixel tracks are resolved against
    /// <paramref name="nominalTotal"/> (the viewport axis) since the final layout
    /// is expressed in percentages.
    /// </summary>
    private static List<double> ParseFramesetSpec(string spec, double nominalTotal)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(spec))
            return result;

        var entries = spec.Split(',');
        var kinds = new char[entries.Length];   // '*', '%', or 'p' (pixel)
        var values = new double[entries.Length];
        double reserved = 0;   // fraction of total consumed by fixed/percent tracks
        double starWeight = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i].Trim();
            if (e.Length == 0 || e == "*")
            {
                kinds[i] = '*';
                values[i] = 1;
                starWeight += 1;
            }
            else if (e.EndsWith("*", StringComparison.Ordinal))
            {
                kinds[i] = '*';
                values[i] = double.TryParse(e[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0 ? w : 1;
                starWeight += values[i];
            }
            else if (e.EndsWith("%", StringComparison.Ordinal)
                && double.TryParse(e[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                kinds[i] = '%';
                values[i] = Math.Max(0, pct);
                reserved += values[i] / 100.0;
            }
            else if (double.TryParse(e.TrimEnd('p', 'x', 'P', 'X'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px))
            {
                kinds[i] = 'p';
                values[i] = Math.Max(0, px);
                reserved += nominalTotal > 0 ? values[i] / nominalTotal : 0;
            }
            else
            {
                kinds[i] = '*';
                values[i] = 1;
                starWeight += 1;
            }
        }

        double remaining = Math.Max(0, 1.0 - reserved);
        for (int i = 0; i < entries.Length; i++)
        {
            double frac = kinds[i] switch
            {
                '%' => values[i] / 100.0,
                'p' => nominalTotal > 0 ? values[i] / nominalTotal : 0,
                _ => starWeight > 0 ? remaining * (values[i] / starWeight) : remaining,
            };
            result.Add(frac * 100.0);
        }

        return result;
    }

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

        // The <br> scan below recomputes followingBlock from the siblings that
        // precede each <br>, but a <br> at index 0 has no preceding sibling, so
        // it must fall back to this box's content-start context — a <br> at the
        // very start of a block generates a full empty line. Capture that value
        // now, before the recursive child walk mutates followingBlock to the
        // trailing-content state (which otherwise leaks in and suppresses the
        // empty line of a *leading* <br>, collapsing consecutive <br><br> to a
        // single line advance).
        bool entryFollowingBlock = followingBlock;

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
            // CSS2.1 §9.2.1/§10.8: An atomic inline-level box (inline-block,
            // inline-flex, inline-grid) is *inline* content even though it
            // carries no text words — it sits on the current line, so a
            // following <br> merely ends that line rather than producing a
            // spurious empty line.  Treat it like text (clears followingBlock)
            // so it is not mistaken for block-level content below.
            followingBlock = !IsAtomicInlineLevel(childBox)
                && childBox.Words.Count == 0
                && (followingBlock || childBox.IsBlock);
        }

        int lastBr = -1;
        CssBox brBox;

        do
        {
            // Re-scan from the block's content-start context each pass so the
            // run preceding *this* <br> is measured fresh; otherwise the value
            // left over from the previous <br> (or the child walk) misclassifies
            // a leading <br>.
            followingBlock = entryFollowingBlock;
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
                else if (box.Boxes[i].Words.Count > 0 || IsAtomicInlineLevel(box.Boxes[i]))
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

    /// <summary>
    /// Returns whether <paramref name="box"/> is an atomic inline-level box
    /// (<c>inline-block</c>, <c>inline-flex</c>, or <c>inline-grid</c>).  Such a
    /// box participates in the inline formatting context — it occupies the
    /// current line — so for <c>&lt;br&gt;</c> empty-line accounting it counts
    /// as inline content, not as a preceding block.
    /// </summary>
    private static bool IsAtomicInlineLevel(CssBox box)
        => box.Display == CssConstants.InlineBlock
           || box.Display == "inline-flex"
           || box.Display == "inline-grid";

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

            if (LayoutBoxUtils.ContainsInlinesOnly(box) && !ContainsInlinesOnlyDeep(box))
            {
                var tempRightBox = CorrectBlockInsideInlineImp(box, baseUrl);
                while (tempRightBox != null)
                {
                    // loop on the created temp right box for the fixed box until no more need (optimization remove recursion)
                    CssBox newTempRightBox = null;
                    if (LayoutBoxUtils.ContainsInlinesOnly(tempRightBox) && !ContainsInlinesOnlyDeep(tempRightBox))
                        newTempRightBox = CorrectBlockInsideInlineImp(tempRightBox, baseUrl);

                    tempRightBox.ParentBox.SetAllBoxes(tempRightBox);
                    tempRightBox.ParentBox = null;
                    tempRightBox = newTempRightBox;
                }
            }

            if (!LayoutBoxUtils.ContainsInlinesOnly(box))
            {
                foreach (var childBox in box.Boxes)
                    CorrectBlockInsideInline(childBox, baseUrl);
            }
        }
        catch (Exception ex)
        {
            ((IHtmlContainerInt)box.ContainerInt).ReportError(HtmlRenderErrorType.HtmlParsing, "Failed in block inside inline box correction", ex);
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

        if (!LayoutBoxUtils.ContainsInlinesOnly(box))
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
            // CSS2.1 §9.2.4: A 'display:none' box generates no box at all, so it
            // is transparent to the mixed-content test.  An invisible <style>,
            // <script>, or display:none <span> between inline-level siblings is
            // neither inline nor block ('none') and must not be counted as a
            // block — otherwise a run of inline-blocks separated by such hidden
            // boxes looks "mixed" and gets torn into stacked anonymous blocks
            // (mirrors the skip already in LayoutBoxUtils.ContainsInlinesOnly).
            if (box.Boxes[i].Display == CssConstants.None)
                continue;

            // CSS2.1 §9.5: Floats are out-of-flow — they do not create a
            // mixed inline/block situation that requires anonymous block
            // wrapping.
            if (box.Boxes[i].Float != CssConstants.None)
                continue;
            // CSS2.1 §9.2.4: A 'display:none' box generates no box, so it does
            // not create a mixed inline/block situation and must not trigger
            // anonymous-block wrapping of surrounding inline siblings.
            if (box.Boxes[i].Display == CssConstants.None)
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
