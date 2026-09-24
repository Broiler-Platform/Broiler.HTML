using Broiler.Graphics.Adapters;
using Broiler.Graphics.Color;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.Utils;
using Broiler.Net.Http;
using System;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Core;

/// <summary>
/// Interface abstracting the HTML container for use by <c>CssBox</c> and related
/// DOM types. Breaks the bidirectional dependency between <c>CssBox</c> and the
/// concrete <c>HtmlContainerInt</c> class.
/// </summary>
internal interface IHtmlContainerInt
{
    /// <summary>
    /// Reports an error during rendering.
    /// </summary>
    void ReportError(HtmlRenderErrorType type, string message, Exception? exception = null);

    /// <summary>
    /// The scroll offset of the container.
    /// </summary>
    PointF ScrollOffset { get; }

    /// <summary>
    /// The location of the root box.
    /// </summary>
    PointF RootLocation { get; }

    /// <summary>
    /// The actual rendered size of the content (get/set).
    /// </summary>
    SizeF ActualSize { get; set; }

    /// <summary>
    /// The page size used for paged rendering.
    /// </summary>
    SizeF PageSize { get; }

    /// <summary>
    /// The effective viewport dimensions.  This is the smaller of
    /// <see cref="PageSize"/> and the layout max-size, matching the
    /// initial containing block for <c>position:fixed</c> elements
    /// and viewport-relative units (<c>vh</c>/<c>vw</c>).
    /// </summary>
    SizeF ViewportSize { get; }

    /// <summary>
    /// Whether to avoid geometry anti-aliasing.
    /// </summary>
    bool AvoidGeometryAntialias { get; }

    /// <summary>
    /// The selection foreground colour.
    /// </summary>
    BColor SelectionForeColor { get; }

    /// <summary>
    /// The selection background colour.
    /// </summary>
    BColor SelectionBackColor { get; }

    /// <summary>
    /// Requests the container to refresh/repaint.
    /// </summary>
    void RequestRefresh(bool layout);

    /// <summary>
    /// Whether asynchronous image loading should be avoided.
    /// </summary>
    bool AvoidAsyncImagesLoading { get; }

    /// <summary>
    /// Whether late (deferred) image loading should be avoided.
    /// </summary>
    bool AvoidImagesLateLoading { get; }

    /// <summary>
    /// The top margin of the container (used for page-break calculations).
    /// </summary>
    int MarginTop { get; }

    /// <summary>
    /// Gets a cached font for the specified family, size, and style.
    /// Wraps the adapter's font creation/caching.
    /// </summary>
    BFont GetFont(string family, double size, Graphics.Text.FontStyle style, string? fontFeatures = null);

    /// <summary>
    /// Parses a colour string and returns the corresponding <see cref="BColor"/>.
    /// Wraps the CSS parser's colour resolution.
    /// </summary>
    BColor ParseColor(string colorStr);

    /// <summary>
    /// Raises the image-load event on the container.
    /// </summary>
    void RaiseHtmlImageLoadEvent(HtmlImageLoadEventArgs args);

    /// <summary>
    /// Converts a platform-specific image object to an <see cref="BImage"/>.
    /// </summary>
    BImage ConvertImage(object image);

    /// <summary>
    /// Creates an <see cref="BImage"/> from a stream.
    /// </summary>
    BImage? ImageFromStream(Stream stream);

    /// <summary>
    /// Downloads an image from a URI.
    /// </summary>
    void DownloadImage(Uri uri, string filePath, bool async, Action<Uri, string, Exception?, bool> callback);

    /// <summary>
    /// Whether the current render tree loads network images through the host's
    /// <see cref="Broiler.Net.Http.IBrowserRequestTransport"/> (see
    /// <see cref="DownloadImage(Uri, CorsSetting, bool, Action{Uri, byte[], Exception, bool})"/>)
    /// rather than the legacy cookie-less client and its disk cache.
    /// </summary>
    bool UsesRequestTransport { get; }

    /// <summary>
    /// Whether the current render tree may read images from the local file system: false for a web
    /// page (see <see cref="Handlers.SubresourceScope.AllowsLocalFiles"/>).
    /// </summary>
    bool AllowsLocalFiles { get; }

    /// <summary>
    /// Loads an image through the host's transport for the container's document, with the
    /// element's CORS setting. The callback receives the body, or the error, or a cancellation
    /// when the render tree was torn down first.
    /// </summary>
    void DownloadImage(Uri uri, CorsSetting crossOrigin, bool async, Action<Uri, byte[]?, Exception?, bool> callback);

    /// <summary>
    /// Creates a new <see cref="IImageLoadHandler"/> for loading images with
    /// the specified completion callback.
    /// </summary>
    /// <remarks>
    /// See ADR-008, Phase 2 prerequisites, item 3.
    /// </remarks>
    IImageLoadHandler CreateImageLoadHandler(ActionInt<BImage?, RectangleF, bool> loadCompleteCallback);

    /// <summary>The current origin-aware style set.</summary>
    HtmlStyleSet StyleSet { get; }

    /// <summary>The default origin-aware style set.</summary>
    HtmlStyleSet DefaultStyleSet { get; }
}
