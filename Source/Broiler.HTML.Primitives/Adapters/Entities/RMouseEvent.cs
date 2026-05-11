namespace Broiler.HTML.Primitives.Adapters.Entities;

public sealed class RMouseEvent(bool leftButton)
{
    public bool LeftButton => leftButton;
}