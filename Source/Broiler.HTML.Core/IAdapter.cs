using Broiler.Graphics;
using Broiler.Graphics.Adapters;
using Broiler.Graphics.Text;
using System.IO;

namespace Broiler.HTML.Core;

/// <summary>
/// Interface abstracting the platform adapter for use by the orchestration layer.
/// Breaks the dependency between <c>HtmlContainerInt</c> (in Orchestration)
/// and the concrete <c>RAdapter</c> class (in the façade).
/// </summary>
internal interface IAdapter : IColorResolver
{
    HtmlStyleSet DefaultStyleSet { get; }

    /// <summary>
    /// Gets a cached font for the specified family, size, and style.
    /// </summary>
    BFont GetFont(string family, double size, FontStyle style, string fontFeatures = null);

    /// <summary>
    /// Converts a platform-specific image object to an <see cref="RImage"/>.
    /// </summary>
    BImage ConvertImage(object image);

    /// <summary>
    /// Creates an <see cref="RImage"/> from a stream.
    /// </summary>
    BImage ImageFromStream(Stream stream);

    /// <summary>
    /// Gets the loading placeholder image.
    /// </summary>
    BImage GetLoadingImage();

    /// <summary>
    /// Gets the error placeholder image.
    /// </summary>
    BImage GetLoadingFailedImage();

    /// <summary>
    /// Loads a font from a file path and registers it as an available font family.
    /// </summary>
    /// <param name="path">Absolute path to a font file.</param>
    /// <param name="mapFromName">Optional CSS family name to map to the loaded font.</param>
    /// <returns>The loaded font family name, or <c>null</c> if loading failed.</returns>
    string LoadFontFromFile(string path, string mapFromName = null);
}
