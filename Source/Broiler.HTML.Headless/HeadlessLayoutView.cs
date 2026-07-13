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
        BDom.DomDocument document, SizeF viewport, string baseUrl)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hasSnapshot
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
        _container.SetDocumentWithStyleSet(document, baseUrl: baseUrl);
        var snapshot = _container.GetLayoutGeometry(viewport);

        _snapshot = snapshot;
        _snapshotDocument = document;
        _snapshotVersion = document.Version;
        _snapshotViewport = viewport;
        _snapshotBaseUrl = baseUrl;
        _hasSnapshot = true;
        return snapshot;
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
