using Broiler.HTML.Core.Entities;

using Broiler.Layout;
namespace Broiler.HTML.Dom;

internal sealed class HoverBoxBlock(CssBox cssBox, CssBlock cssBlock)
{
    public CssBox CssBox { get; } = cssBox;
    public CssBlock CssBlock { get; } = cssBlock;
}