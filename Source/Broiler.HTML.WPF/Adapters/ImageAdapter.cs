using Broiler.HTML.Adapters;
using System.Windows.Media.Imaging;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class ImageAdapter(BitmapImage image) : RImage
{
    public BitmapImage Image { get; } = image;

    public override double Width => Image.PixelWidth;

    public override double Height => Image.PixelHeight;

    public override bool TryGetSampledColor(System.Drawing.RectangleF sourceRect, out System.Drawing.Color color)
    {
        color = System.Drawing.Color.Empty;
        return false;
    }

    public override void Dispose() => Image.StreamSource?.Dispose();
}
