using Broiler.Graphics.Adapters;

namespace Broiler.HTML.Image.Adapters;

/// <summary>
/// An image known by its size alone, read from its header: what a container that is measured and never
/// painted loads (<see cref="HtmlContainer.ImageSizesOnly"/>). It has no pixels to draw.
/// </summary>
internal sealed class ImageSizeAdapter(int width, int height) : BImage
{
    public override double Width { get; } = width;

    public override double Height { get; } = height;

    public override void Dispose()
    {
    }
}
