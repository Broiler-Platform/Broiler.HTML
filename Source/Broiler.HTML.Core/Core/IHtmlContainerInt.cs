using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Core.Core;

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
    void ReportError(HtmlRenderErrorType type, string message, Exception exception = null);

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
    Color SelectionForeColor { get; }

    /// <summary>
    /// The selection background colour.
    /// </summary>
    Color SelectionBackColor { get; }

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
    RFont GetFont(string family, double size, FontStyle style, string fontFeatures = null);

    /// <summary>
    /// Parses a colour string and returns the corresponding <see cref="Color"/>.
    /// Wraps the CSS parser's colour resolution.
    /// </summary>
    Color ParseColor(string colorStr);

    /// <summary>
    /// Raises the image-load event on the container.
    /// </summary>
    void RaiseHtmlImageLoadEvent(HtmlImageLoadEventArgs args);

    /// <summary>
    /// Converts a platform-specific image object to an <see cref="RImage"/>.
    /// </summary>
    RImage ConvertImage(object image);

    /// <summary>
    /// Creates an <see cref="RImage"/> from a stream.
    /// </summary>
    RImage ImageFromStream(Stream stream);

    /// <summary>
    /// Gets the loading placeholder image.
    /// </summary>
    RImage GetLoadingImage();

    /// <summary>
    /// Gets the error placeholder image.
    /// </summary>
    RImage GetLoadingFailedImage();

    /// <summary>
    /// Downloads an image from a URI.
    /// </summary>
    void DownloadImage(Uri uri, string filePath, bool async, Action<Uri, string, Exception, bool> callback);

    /// <summary>
    /// Creates a new <see cref="IImageLoadHandler"/> for loading images with
    /// the specified completion callback.
    /// </summary>
    /// <remarks>
    /// See ADR-008, Phase 2 prerequisites, item 3.
    /// </remarks>
    IImageLoadHandler CreateImageLoadHandler(ActionInt<RImage, RectangleF, bool> loadCompleteCallback);

    /// <summary>
    /// Registers a hover box/block pair for hover-state CSS handling.
    /// </summary>
    /// <remarks>
    /// See ADR-008, Phase 2 prerequisites, item 2.
    /// </remarks>
    void AddHoverBox(object box, CssBlock block);

    /// <summary>
    /// The current CSS data for the rendered document.
    /// </summary>
    CssData CssData { get; }

    /// <summary>
    /// The default CSS data from the adapter.
    /// </summary>
    CssData DefaultCssData { get; }

    /// <summary>
    /// Parses a CSS block from inline style text.
    /// </summary>
    CssBlock ParseCssBlock(string className, string blockSource);
}
