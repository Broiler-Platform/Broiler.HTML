using System;
using System.Collections.Generic;
using System.Text;
using Broiler.HTML.Dom;
using Broiler.HTML.Dom.Utils;

namespace Broiler.HTML.Orchestration.Parse;

/// <summary>
/// Phase 5 dual-run scaffold: computes box styles through the shared
/// <see cref="Broiler.CSS.Dom.CssStyleEngine"/> on the canonical
/// <see cref="Broiler.Dom.DomDocument"/>, instead of the legacy
/// <see cref="DomParser"/> selector/cascade. Gated behind
/// <see cref="UseSharedRendererCascade"/> (default <c>false</c>) — the legacy
/// cascade remains the observable rendering path until pixel parity is verified
/// (roadmap decision #10). The engine produces fully-resolved
/// <see cref="Broiler.CSS.Dom.CssComputedStyle"/> values; this scaffold only
/// projects them onto <see cref="CssBox"/> via the renderer's own
/// <see cref="CssUtils.SetPropertyValue"/>. Layout-owned used-value resolution
/// (the <c>Actual*</c> getters) is unchanged.
/// </summary>
internal static class SharedRendererCascade
{
    /// <summary>
    /// When <c>true</c>, <see cref="DomParser"/> additionally projects shared
    /// computed styles onto the box tree after the legacy cascade. Off by default;
    /// this is the dual-run switch the renderer cutover flips once pixel-verified.
    /// </summary>
    internal static bool UseSharedRendererCascade { get; set; }

    /// <summary>
    /// Builds a <see cref="Broiler.CSS.Dom.CssStyleEngine"/> from the document's
    /// author <c>&lt;style&gt;</c> text and projects the engine's computed style onto
    /// every box that carries a <see cref="CssBox.SourceElement"/>.
    /// </summary>
    internal static void Apply(CssBox root, Broiler.Dom.DomDocument document, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (document is null)
            return;

        var engine = new Broiler.CSS.Dom.CssStyleEngine();
        var authorCss = CollectAuthorStyleText(document);
        if (!string.IsNullOrWhiteSpace(authorCss))
        {
            var sheet = new Broiler.CSS.CssParser().ParseStyleSheet(authorCss);
            engine.AddStyleSheet(sheet, Broiler.CSS.Dom.CssOrigin.Author);
        }

        engine.UpdateEnvironment(new Broiler.CSS.Dom.CssEnvironment(viewportWidth, viewportHeight));
        ApplyToBoxTree(root, engine);
    }

    private static void ApplyToBoxTree(CssBox box, Broiler.CSS.Dom.CssStyleEngine engine)
    {
        // The engine matches selectors against the canonical element; only boxes
        // built from a DomDocument carry one (the legacy HTML-string path does not).
        if (box.SourceElement is { } element)
            ApplyComputedStyle(box, engine.GetComputedStyle(element));

        foreach (var child in box.Boxes)
            ApplyToBoxTree(child, engine);
    }

    /// <summary>
    /// The <see cref="Broiler.CSS.Dom.CssComputedStyle"/> → <see cref="CssBoxProperties"/>
    /// map: writes each computed (property-name, value) pair onto the box through the
    /// renderer's existing property setter, which silently ignores names it does not
    /// model. Computed values already include inheritance, shorthand expansion, custom
    /// properties, and initial values, so no further cascade is needed here.
    /// </summary>
    internal static void ApplyComputedStyle(CssBox box, Broiler.CSS.Dom.CssComputedStyle computed)
    {
        foreach (var pair in computed.Properties)
            CssUtils.SetPropertyValue(box, pair.Key, pair.Value);
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
