using System.Drawing;
using Broiler.HTML.Adapters.Adapters;

namespace Broiler.HTML.Dom.Core.Dom;

internal sealed class CssRectImage(CssBox owner) : CssRect(owner)
{
    private RImage _image;
    private RectangleF _imageRectangle;

    public override RImage Image
    {
        get { return _image; }
        set { _image = value; }
    }

    public override bool IsImage => true;

    public RectangleF ImageRectangle
    {
        get { return _imageRectangle; }
        set { _imageRectangle = value; }
    }

    public override string ToString() => "Image";
}