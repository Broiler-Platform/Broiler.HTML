using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Core.Core;

/// <summary>
/// Interface for image loading handlers used by CssBox.
/// Breaks the direct dependency between <c>CssBox</c> and the concrete
/// <c>ImageLoadHandler</c> class.
/// </summary>
internal interface IImageLoadHandler : IDisposable
{
    /// <summary>
    /// The loaded image, or null if not yet loaded or failed.
    /// </summary>
    RImage Image { get; }

    /// <summary>
    /// The sub-rectangle of the image to use, or <see cref="RectangleF.Empty"/> for the entire image.
    /// </summary>
    RectangleF Rectangle { get; }

    /// <summary>
    /// Initiates image loading from the specified source.
    /// </summary>
    void LoadImage(string src, Dictionary<string, string> attributes, Uri baseUrl);
}
