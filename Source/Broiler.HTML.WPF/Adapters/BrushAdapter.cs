using Broiler.HTML.Adapters.Adapters;
using System.Windows.Media;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class BrushAdapter(Brush brush) : RBrush
{
    public Brush Brush { get; } = brush;

    public override void Dispose()
    { }
}