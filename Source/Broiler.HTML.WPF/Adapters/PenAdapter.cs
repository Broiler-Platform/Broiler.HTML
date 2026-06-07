using DashStyle = Broiler.Graphics.DashStyle;
using System.Windows.Media;
using Broiler.HTML.Adapters.Adapters;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class PenAdapter(Brush brush) : RPen
{
    private System.Windows.Media.DashStyle _dashStyle = DashStyles.Solid;

    public override double Width { get; set; }

    public override DashStyle DashStyle
    {
        set
        {
            _dashStyle = value switch
            {
                DashStyle.Solid => DashStyles.Solid,
                DashStyle.Dash => DashStyles.Dash,
                DashStyle.Dot => DashStyles.Dot,
                DashStyle.DashDot => DashStyles.DashDot,
                DashStyle.DashDotDot => DashStyles.DashDotDot,
                _ => DashStyles.Solid,
            };
        }
    }

    public Pen CreatePen() => new(brush, Width) { DashStyle = _dashStyle };
}