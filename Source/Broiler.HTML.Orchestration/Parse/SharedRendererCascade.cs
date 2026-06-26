using System;
using System.Collections.Generic;
using System.Text;
using Broiler.HTML.Dom;
using Broiler.Layout;
using Broiler.HTML.Dom.Utils;

namespace Broiler.HTML.Orchestration.Parse;

/// <summary>
/// Phase 5 renderer cutover: drives the renderer's element cascade through the shared
/// <see cref="Broiler.CSS.Dom.CssStyleEngine"/> on the canonical
/// <see cref="Broiler.Dom.DomDocument"/>, in place of <see cref="DomParser"/>'s legacy
/// selector matching. Gated behind <see cref="UseSharedRendererCascade"/> (default
/// <c>false</c>) — the legacy cascade remains the observable rendering path until pixel
/// parity is verified (roadmap decision #10).
/// </summary>
/// <remarks>
/// The engine supplies only cascade-resolved <em>declared</em> longhands
/// (<see cref="Broiler.CSS.Dom.CssStyleEngine.GetCascadedStyle"/>): no inheritance or
/// initial-value backfill, so the renderer keeps its own <see cref="CssBox.InheritStyle"/>
/// pass and per-box defaults, and its presentational-attribute, inline-style, replaced-element,
/// pseudo-element, and animation handling all continue to run. Layout used-value resolution
/// (the <c>Actual*</c> getters) is untouched.
/// </remarks>
internal static class SharedRendererCascade
{
    /// <summary>
    /// When <c>true</c>, <see cref="DomParser"/> resolves each element box's cascaded
    /// style through the shared engine instead of its legacy selector matching.
    /// **Default <c>true</c> as of the Phase 5 cutover (2026-06-26)** — verified against the
    /// Acid3 + WPT pixel gates (no pass/fail regressions; fixes several important/border
    /// cascade tests). Set to <c>false</c> to roll back to the legacy
    /// <see cref="DomParser"/> selector matching if a regression surfaces.
    /// </summary>
    internal static bool UseSharedRendererCascade { get; set; } = true;

    /// <summary>
    /// The user-agent default stylesheet, parsed once. Registering it under
    /// <see cref="Broiler.CSS.Dom.CssOrigin.UserAgent"/> gives the engine the same UA
    /// display/margin/font defaults the legacy renderer gets from
    /// <see cref="Broiler.HTML.Core.CssDefaults.DefaultStyleSheet"/>, so an element with
    /// no author rule cascades <c>display:block</c> (etc.) rather than the bare
    /// <c>display:inline</c> initial.
    /// </summary>
    private static readonly Broiler.CSS.CssStyleSheet UserAgentSheet =
        new Broiler.CSS.CssParser().ParseStyleSheet(Broiler.HTML.Core.CssDefaults.DefaultStyleSheet);

    /// <summary>
    /// Builds a <see cref="Broiler.CSS.Dom.CssStyleEngine"/> for <paramref name="document"/>
    /// from the user-agent sheet plus the document's author <c>&lt;style&gt;</c> text, with the
    /// viewport applied. Returns <c>null</c> when there is no canonical document to cascade over.
    /// </summary>
    internal static Broiler.CSS.Dom.CssStyleEngine? BuildEngine(
        Broiler.Dom.DomDocument? document, int viewportWidth, int viewportHeight)
    {
        if (document is null)
            return null;

        var engine = new Broiler.CSS.Dom.CssStyleEngine();
        engine.AddStyleSheet(UserAgentSheet, Broiler.CSS.Dom.CssOrigin.UserAgent);

        var authorCss = CollectAuthorStyleText(document);
        if (!string.IsNullOrWhiteSpace(authorCss))
        {
            var sheet = new Broiler.CSS.CssParser().ParseStyleSheet(authorCss);
            engine.AddStyleSheet(sheet, Broiler.CSS.Dom.CssOrigin.Author);
        }

        engine.UpdateEnvironment(new Broiler.CSS.Dom.CssEnvironment(viewportWidth, viewportHeight));
        return engine;
    }

    /// <summary>
    /// Projects the engine's cascade-resolved declared longhands for <paramref name="box"/>'s
    /// <see cref="CssBox.SourceElement"/> onto the box through the renderer's own
    /// <see cref="CssUtils.SetPropertyValue"/> (which ignores names it does not model). Replaces
    /// the legacy per-element selector matching at the same point in the cascade, so it must run
    /// after <see cref="CssBox.InheritStyle"/> and before presentational attributes / inline style.
    /// </summary>
    internal static void ProjectCascadedStyle(CssBox box, Broiler.CSS.Dom.CssStyleEngine engine)
    {
        if (box.SourceElement is not { } element)
            return;

        foreach (var pair in engine.GetCascadedStyle(element))
            CssUtils.SetPropertyValue(box, pair.Key, pair.Value);
    }

    /// <summary>Finds the canonical <see cref="Broiler.Dom.DomDocument"/> behind a box tree, if any.</summary>
    internal static Broiler.Dom.DomDocument? FindCanonicalDocument(CssBox root)
    {
        if (root.SourceElement is { } element)
            return element.OwnerDocument as Broiler.Dom.DomDocument;

        foreach (var child in root.Boxes)
        {
            var document = FindCanonicalDocument(child);
            if (document != null)
                return document;
        }

        return null;
    }

    /// <summary>Concatenates the text of every <c>&lt;style&gt;</c> element in the document.</summary>
    private static string CollectAuthorStyleText(Broiler.Dom.DomDocument document)
    {
        if (document.DocumentElement is not { } root)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in root.InclusiveDescendants())
        {
            if (node is not Broiler.Dom.DomElement element ||
                !element.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var child in element.ChildNodes)
            {
                if (child is Broiler.Dom.DomText text)
                    sb.Append(text.Data);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
