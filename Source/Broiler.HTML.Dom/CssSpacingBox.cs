using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Collections.Generic;

namespace Broiler.HTML.Dom;

internal sealed class CssSpacingBox : CssBox
{
    public CssSpacingBox(CssBox tableBox, ref CssBox extendedBox, int startRow, Uri baseUrl)
        : base(tableBox, new HtmlTag("none", false, new Dictionary<string, string> { { "colspan", "1" } }), baseUrl)
    {
        ExtendedBox = extendedBox;
        Display = CssConstants.None;

        StartRow = startRow;
        EndRow = startRow + int.Parse(extendedBox.GetAttribute("rowspan", "1")) - 1;
    }

    public CssBox ExtendedBox { get; }
    public int StartRow { get; }
    public int EndRow { get; }
}