using System;
using System.Drawing;
using Broiler.Graphics;
using Broiler.Graphics.Adapters;

namespace Broiler.HTML.Orchestration;

public static class HtmlRendererUtils
{
    public static SizeF MeasureHtmlByRestrictions(BGraphics g, HtmlContainerInt htmlContainer, SizeF minSize, SizeF maxSize)
    {
        // first layout without size restriction to know html actual size
        htmlContainer.PerformLayout(g);

        if (maxSize.Width > 0 && maxSize.Width < htmlContainer.ActualSize.Width)
        {
            // to allow the actual size be smaller than max we need to set max size only if it is really larger
            htmlContainer.MaxSize = new SizeF(maxSize.Width, 0);
            htmlContainer.PerformLayout(g);
        }

        // restrict the final size by min/max
        var finalWidth = Math.Max(maxSize.Width > 0 ? Math.Min(maxSize.Width, (int)htmlContainer.ActualSize.Width) : (int)htmlContainer.ActualSize.Width, minSize.Width);

        // if the final width is larger than the actual we need to re-layout so the html can take the full given width.
        if (finalWidth > htmlContainer.ActualSize.Width)
        {
            htmlContainer.MaxSize = new SizeF(finalWidth, 0);
            htmlContainer.PerformLayout(g);
        }

        var finalHeight = Math.Max(maxSize.Height > 0 ? Math.Min(maxSize.Height, (int)htmlContainer.ActualSize.Height) : (int)htmlContainer.ActualSize.Height, minSize.Height);

        return new SizeF(finalWidth, finalHeight);
    }
}