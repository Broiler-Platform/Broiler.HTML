using System;
using System.Drawing;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Orchestration;

public static class HtmlRendererUtils
{
    public static SizeF MeasureHtmlByRestrictions(RGraphics g, HtmlContainerInt htmlContainer, SizeF minSize, SizeF maxSize)
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

    public static SizeF Layout(RGraphics g, HtmlContainerInt htmlContainer, SizeF size, SizeF minSize, SizeF maxSize, bool autoSize, bool autoSizeHeightOnly)
    {
        if (autoSize)
            htmlContainer.MaxSize = new SizeF(0, 0);
        else if (autoSizeHeightOnly)
            htmlContainer.MaxSize = new SizeF(size.Width, 0);
        else
            htmlContainer.MaxSize = size;

        htmlContainer.PerformLayout(g);

        SizeF newSize = size;

        if (!autoSize && !autoSizeHeightOnly)
            return newSize;

        if (autoSize)
        {
            if (maxSize.Width > 0 && maxSize.Width < htmlContainer.ActualSize.Width)
            {
                // to allow the actual size be smaller than max we need to set max size only if it is really larger
                htmlContainer.MaxSize = maxSize;
                htmlContainer.PerformLayout(g);
            }
            else if (minSize.Width > 0 && minSize.Width > htmlContainer.ActualSize.Width)
            {
                // if min size is larger than the actual we need to re-layout so all 100% layouts will be correct
                htmlContainer.MaxSize = new SizeF(minSize.Width, 0);
                htmlContainer.PerformLayout(g);
            }
            newSize = htmlContainer.ActualSize;
        }
        else if (Math.Abs(size.Height - htmlContainer.ActualSize.Height) > 0.01)
        {
            var prevWidth = size.Width;

            // make sure the height is not lower than min if given
            newSize.Height = minSize.Height > 0 && minSize.Height > htmlContainer.ActualSize.Height
                ? minSize.Height
                : htmlContainer.ActualSize.Height;

            // handle if changing the height of the label affects the desired width and those require re-layout
            if (Math.Abs(prevWidth - size.Width) > 0.01)
                return Layout(g, htmlContainer, size, minSize, maxSize, false, true);
        }

        return newSize;
    }
}