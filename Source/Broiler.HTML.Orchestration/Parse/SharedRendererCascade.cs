using System;
using System.Collections.Generic;
using Broiler.HTML.Core;
using Broiler.HTML.Dom;
using Broiler.Layout;
using Broiler.HTML.Dom.Utils;

namespace Broiler.HTML.Orchestration.Parse;

/// <summary>
/// Phase 5 renderer cutover: drives the renderer's element cascade through the shared
/// <see cref="Broiler.CSS.Dom.CssStyleEngine"/> on the canonical
/// <see cref="Broiler.Dom.DomDocument"/>. This is the sole renderer cascade path; the legacy
/// per-element selector matching in <see cref="DomParser"/> was retired in Phase 7 cleanup
/// (RF-CSS-1) after the 2026-06-26 cutover verified Acid3 + WPT pixel parity.
/// </summary>
/// <remarks>
/// The engine supplies only cascade-resolved <em>declared</em> longhands
/// (<see cref="Broiler.CSS.Dom.CssStyleEngine.GetCascadedStyle"/>): no inheritance or
/// initial-value backfill, so the renderer keeps its own <see cref="CssBox.InheritStyle"/>
/// pass and per-box defaults. Presentation attributes are applied first as low-priority hints;
/// stylesheet and inline declarations then share the engine's origin-aware cascade. Layout
/// used-value resolution (the <c>Actual*</c> getters) is untouched.
/// </remarks>
internal static class SharedRendererCascade
{
    /// <summary>
    /// Builds a <see cref="Broiler.CSS.Dom.CssStyleEngine"/> for <paramref name="document"/>
    /// from the origin-separated user-agent and author sheets, with the viewport applied.
    /// Returns <c>null</c> when there is no canonical document to cascade over.
    /// </summary>
    internal static Broiler.CSS.Dom.CssStyleEngine? BuildEngine(
        Broiler.Dom.DomDocument? document,
        HtmlStyleSet styleSet,
        int viewportWidth,
        int viewportHeight)
    {
        if (document is null)
            return null;

        var engine = new Broiler.CSS.Dom.CssStyleEngine();
        if (styleSet.UserAgentStyleSheet.Rules.Count > 0)
            engine.AddStyleSheet(styleSet.UserAgentStyleSheet, Broiler.CSS.Dom.CssOrigin.UserAgent);
        if (styleSet.AuthorStyleSheet.Rules.Count > 0)
            engine.AddStyleSheet(styleSet.AuthorStyleSheet, Broiler.CSS.Dom.CssOrigin.Author);

        engine.UpdateEnvironment(new Broiler.CSS.Dom.CssEnvironment(viewportWidth, viewportHeight));
        return engine;
    }

    /// <summary>
    /// Builds an engine seeded with the <em>author</em> origin only (stylesheets +
    /// inline declarations). Used to decide whether a property a presentation
    /// attribute set is also claimed by an author declaration: if it is not, the
    /// only competing rule is a UA default, and the presentation hint must win.
    /// Returns <c>null</c> when there are no author sheets at all.
    /// </summary>
    internal static Broiler.CSS.Dom.CssStyleEngine? BuildAuthorEngine(
        Broiler.Dom.DomDocument? document,
        HtmlStyleSet styleSet,
        int viewportWidth,
        int viewportHeight)
    {
        if (document is null || styleSet.AuthorStyleSheet.Rules.Count == 0)
            return null;

        var engine = new Broiler.CSS.Dom.CssStyleEngine();
        engine.AddStyleSheet(styleSet.AuthorStyleSheet, Broiler.CSS.Dom.CssOrigin.Author);
        engine.UpdateEnvironment(new Broiler.CSS.Dom.CssEnvironment(viewportWidth, viewportHeight));
        return engine;
    }

    /// <summary>
    /// Projects the engine's cascade-resolved declared longhands for <paramref name="box"/>'s
    /// <see cref="CssBox.SourceElement"/> onto the box through the renderer's own
    /// <see cref="CssUtils.SetPropertyValue"/> (which ignores names it does not model). Replaces
    /// the legacy per-element selector matching and inline parser at the same point in the
    /// renderer pipeline, after inheritance and presentation-attribute hints.
    /// </summary>
    internal static void ProjectCascadedStyle(
        CssBox box,
        Broiler.CSS.Dom.CssStyleEngine engine,
        Broiler.CSS.Dom.CssStyleEngine? authorEngine = null,
        IReadOnlySet<string>? presentationHintKeys = null)
    {
        if (box.SourceElement is not { } element)
            return;

        // Properties a presentation attribute supplied (cellspacing/cellpadding)
        // are kept only when no author declaration claims them; an author rule (any
        // origin above UA) still wins. Compute the author-claimed key set lazily.
        HashSet<string>? authorClaimed = null;
        if (presentationHintKeys is { Count: > 0 } && authorEngine is not null)
        {
            authorClaimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in authorEngine.GetCascadedStyle(element, includeInlineStyle: true))
                authorClaimed.Add(pair.Key);
        }

        foreach (var pair in engine.GetCascadedStyle(element, includeInlineStyle: true))
        {
            // The cascade winner here merges UA + author. When a presentation hint
            // set this property and no author rule claims it, the winner is a UA
            // default that must not clobber the hint — skip it.
            if (presentationHintKeys is not null
                && presentationHintKeys.Contains(pair.Key)
                && (authorClaimed is null || !authorClaimed.Contains(pair.Key)))
                continue;

            CssUtils.SetPropertyValue(box, pair.Key, pair.Value);
        }
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

}
