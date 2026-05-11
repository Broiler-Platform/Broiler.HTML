using Broiler.HTML.Adapters.Adapters;
using System.Windows;
using System.Windows.Media;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class GraphicsPathAdapter : RGraphicsPath
{
    private readonly StreamGeometry _geometry = new();
    private readonly StreamGeometryContext _geometryContext;

    public GraphicsPathAdapter() => _geometryContext = _geometry.Open();

    public override void Start(double x, double y) => _geometryContext.BeginFigure(new Point(x, y), true, false);

    public override void LineTo(double x, double y) => _geometryContext.LineTo(new Point(x, y), true, true);

    public override void ArcTo(double x, double y, double size, Corner corner) => _geometryContext.ArcTo(new Point(x, y), new Size(size, size), 0, false, SweepDirection.Clockwise, true, true);

    public StreamGeometry GetClosedGeometry()
    {
        _geometryContext.Close();
        _geometry.Freeze();

        return _geometry;
    }

    public override void Dispose()
    { }
}