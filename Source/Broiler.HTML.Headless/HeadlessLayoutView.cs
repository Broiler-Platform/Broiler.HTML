using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout;
using BDom = Broiler.Dom;

namespace Broiler.HTML.Headless;

/// <summary>
/// <see cref="ILayoutView"/> implementation that drives the renderer's real layout engine
/// headlessly (no paint backend) to answer the script bridge's element-geometry queries,
/// replacing the coarse estimators. The canonical document is laid out once per
/// (document, version, viewport, baseUrl) snapshot and the per-element
/// <see cref="BoxGeometry"/> map is cached; layout re-runs only when one of those changes.
/// </summary>
/// <remarks>
/// Lives in <c>Broiler.HTML.Headless</c> (which references <c>Broiler.HTML.Image</c>) so the
/// bridge's <c>DomBridge</c> can consume it through the neutral <see cref="ILayoutView"/>
/// contract in <c>Broiler.Layout</c> without taking a direct dependency on
/// <see cref="HtmlContainer"/> or the image-rendering stack.
/// </remarks>
public sealed class HeadlessLayoutView : ILayoutView
{
    private readonly HtmlContainer _container = new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
    };

    private IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? _snapshot;
    private BDom.DomDocument? _snapshotDocument;
    private ulong _snapshotVersion;
    private SizeF _snapshotViewport;
    private string? _snapshotBaseUrl;
    private bool _hasSnapshot;
    private bool _disposed;

    /// <summary>
    /// Returns the per-element geometry map for <paramref name="document"/> at its current
    /// <see cref="BDom.DomDocument.Version"/> and the given <paramref name="viewport"/>,
    /// laying out only when the cached snapshot is stale for the
    /// (document, version, viewport, baseUrl) key.
    /// </summary>
    public IReadOnlyDictionary<BDom.DomElement, BoxGeometry> GetGeometry(
        BDom.DomDocument document, SizeF viewport, string baseUrl,
        Func<BDom.DomElement, BDom.DomDocument?>? contentDocumentResolver = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // When a content-document resolver is present (materialised iframe/object sub-documents),
        // the (document, version, viewport, baseUrl) key does not capture sub-document mutations —
        // a severed sub-document has its own DomDocument.Version, invisible to the main document's.
        // Bypass the snapshot cache in that case so a mutated sub-document always re-lays-out; the
        // bridge still caps this to at most once per read pass via its own per-pass snapshot.
        if (contentDocumentResolver is null
            && _hasSnapshot
            && ReferenceEquals(_snapshotDocument, document)
            && _snapshotVersion == document.Version
            && _snapshotViewport == viewport
            && string.Equals(_snapshotBaseUrl, baseUrl, StringComparison.Ordinal))
        {
            return _snapshot!;
        }

        // A layout fault is not swallowed here: it propagates to the caller (the bridge's
        // BuildSharedGeometrySnapshot degrades the whole pass to an empty map), so the cause
        // is not hidden behind a per-provider catch-all. The cache is only updated on
        // success, so a transient failure does not poison a later query.
        _container.ContentDocumentResolver = contentDocumentResolver;
        _container.SetDocumentWithStyleSet(document, baseUrl: baseUrl);

        // Phase 5 LayoutSnapshot endgame: lay out with the engine's native anchor-positioning
        // post-pass on, so the geometry the script bridge reads (offsetLeft/getBoundingClientRect
        // during script) carries engine-resolved position-area / anchor() / anchor-size() boxes
        // instead of the pre-bake 0×0/static placement — the read-model side of moving anchor
        // placement into Broiler.Layout. Thread-static, so save/restore keeps concurrent layouts
        // unaffected. PositionTryRules is the out-of-band channel for the @position-try fallback
        // pass: the engine consumes cascaded box properties, never the stylesheet, so the rule
        // bodies (a stylesheet at-rule) are handed in here and the native post-pass applies the
        // first non-overflowing fallback — so a position-try box carries its resolved FALLBACK
        // placement in the snapshot, not merely its base.
        var previousEnabled = Broiler.Layout.Engine.NativeAnchorPlacement.Enabled;
        var previousRules = Broiler.Layout.Engine.NativeAnchorPlacement.PositionTryRules;
        Broiler.Layout.Engine.NativeAnchorPlacement.Enabled = true;
        Broiler.Layout.Engine.NativeAnchorPlacement.PositionTryRules = ParsePositionTryRules(document);
        IReadOnlyDictionary<BDom.DomElement, BoxGeometry> snapshot;
        try
        {
            snapshot = _container.GetLayoutGeometry(viewport);
        }
        finally
        {
            Broiler.Layout.Engine.NativeAnchorPlacement.Enabled = previousEnabled;
            Broiler.Layout.Engine.NativeAnchorPlacement.PositionTryRules = previousRules;
        }

        if (contentDocumentResolver is null)
        {
            _snapshot = snapshot;
            _snapshotDocument = document;
            _snapshotVersion = document.Version;
            _snapshotViewport = viewport;
            _snapshotBaseUrl = baseUrl;
            _hasSnapshot = true;
        }
        else
        {
            // Do not serve a resolver-built snapshot from the plain-document cache on a later pass.
            _hasSnapshot = false;
        }

        return snapshot;
    }

    /// <summary>
    /// Extracts the document's <c>@position-try</c> at-rules (name → declarations) from its
    /// <c>&lt;style&gt;</c> elements, using the canonical <c>Broiler.CSS.PositionTryRule</c>
    /// parser — the same model the bridge's resolver and the WPT runner use, so the native
    /// fallback pass sees the identical rule bodies. Later duplicates win, in document order.
    /// Returns <c>null</c> when the document declares no <c>@position-try</c> rules, matching the
    /// channel's "no fallback rules available" contract.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ParsePositionTryRules(
        BDom.DomDocument document)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>>? result = null;
        foreach (var styleEl in document.GetElementsByTagName("style"))
        {
            var css = StyleElementText(styleEl);
            if (css.Length == 0 || css.IndexOf("@position-try", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            foreach (var rule in Broiler.CSS.PositionTryRule.Parse(css))
                (result ??= new(StringComparer.Ordinal))[rule.Key] = rule.Value;
        }

        return result;
    }

    /// <summary>Concatenates the direct text-node children of a <c>&lt;style&gt;</c> element
    /// (its CSS source), mirroring the bridge's <c>GetStyleElementSourceText</c> accessor.</summary>
    private static string StyleElementText(BDom.DomElement styleEl)
    {
        string? single = null;
        System.Text.StringBuilder? many = null;
        foreach (var child in styleEl.ChildNodes)
        {
            if (child.NodeType != BDom.DomNodeType.Text || child.NodeValue is not { Length: > 0 } text)
                continue;

            if (single is null && many is null)
                single = text;
            else
                (many ??= new System.Text.StringBuilder(single)).Append(text);
        }

        return many?.ToString() ?? single ?? string.Empty;
    }

    /// <summary>Releases the internal renderer container.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _snapshot = null;
        _snapshotDocument = null;
        _container.Dispose();
    }
}
