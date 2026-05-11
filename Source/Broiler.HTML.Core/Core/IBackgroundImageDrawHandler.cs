using System.Drawing;
using Broiler.HTML.Adapters.Adapters;

namespace Broiler.HTML.Core.Core;

/// <summary>
/// Read-only view of the background-image CSS properties that drawing handlers require.
/// Implemented by <c>CssBoxProperties</c> to decouple handlers from the DOM tree.
/// </summary>
internal interface IBackgroundRenderData
{
    string BackgroundPosition { get; }
    string BackgroundRepeat { get; }
    string BackgroundAttachment { get; }
}

/// <summary>
/// Interface for background-image drawing handlers used by CssBox.
/// Breaks the direct static dependency between <c>CssBox</c> and the concrete
/// <c>BackgroundImageDrawHandler</c> class.
/// </summary>
internal interface IBackgroundImageDrawHandler
{
    /// <summary>
    /// Draws a background image within the specified rectangle.
    /// </summary>
    void DrawBackgroundImage(RGraphics g, IBackgroundRenderData box, IImageLoadHandler imageHandler, RectangleF rectangle);
}
