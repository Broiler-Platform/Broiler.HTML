using System;
using System.Drawing;
using Broiler.HTML.Adapters;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Utils;
using Broiler.HTML.Utils;

namespace Broiler.HTML.Dom;

internal sealed class CssBoxHr : CssBox
{
    public CssBoxHr(CssBox parent, HtmlTag tag, Uri baseUrl) : base(parent, tag, baseUrl) => Display = CssConstants.Block;

    protected override void PerformLayoutImp(RGraphics g)
    {
        if (Display == CssConstants.None)
            return;

        RectanglesReset();

        var prevSibling = DomUtils.GetPreviousSibling(this);
        double left = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ActualMarginLeft + ContainingBlock.ActualBorderLeftWidth;
        double marginCollapse = MarginTopCollapse(prevSibling);
        double top = (prevSibling == null && ParentBox != null ? ParentBox.ClientTop : ParentBox == null ? Location.Y : 0) + marginCollapse + (prevSibling != null ? prevSibling.ActualBottom + prevSibling.ActualBorderBottomWidth : 0);
        Location = new PointF((float)left, (float)top);
        ActualBottom = top;

        //width at 100% (or auto)
        double minwidth = GetMinimumWidth();
        double width = ContainingBlock.Size.Width
                       - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                       - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth
                       - ActualMarginLeft - ActualMarginRight - ActualBorderLeftWidth - ActualBorderRightWidth;

        //Check width if not auto
        if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
            width = CssValueParser.ParseLength(Width, width, GetEmHeight());

        if (width < minwidth || width >= 9999)
            width = minwidth;

        double height = ActualHeight;

        if (height < 1)
            height = Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth;

        if (height < 1)
            height = 2;

        if (height <= 2 && ActualBorderTopWidth < 1 && ActualBorderBottomWidth < 1)
        {
            BorderTopStyle = BorderBottomStyle = CssConstants.Solid;
            BorderTopWidth = "1px";
            BorderBottomWidth = "1px";
        }

        Size = new SizeF((float)width, (float)height);

        ActualBottom = Location.Y + ActualPaddingTop + ActualPaddingBottom + height;
    }
}