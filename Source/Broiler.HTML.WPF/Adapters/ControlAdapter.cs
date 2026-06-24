using Broiler.HTML.Adapters;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PointF = System.Drawing.PointF;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class ControlAdapter : RControl
{
    public ControlAdapter(Control control) : base(WpfAdapter.Instance)
    {
        ArgumentNullException.ThrowIfNull(control);

        Control = control;
    }

    public Control Control { get; }

    public override PointF MouseLocation => Utilities.Utils.Convert(Control.PointFromScreen(Mouse.GetPosition(Control)));

    public override bool LeftMouseButton => Mouse.LeftButton == MouseButtonState.Pressed;

    public override bool RightMouseButton => Mouse.RightButton == MouseButtonState.Pressed;

    public override void SetCursorDefault() => Control.Cursor = Cursors.Arrow;

    public override void SetCursorHand() => Control.Cursor = Cursors.Hand;

    public override void SetCursorIBeam() => Control.Cursor = Cursors.IBeam;

    public override void DoDragDropCopy(object dragDropData) => DragDrop.DoDragDrop(Control, dragDropData, DragDropEffects.Copy);

    public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
    {
        var g = new GraphicsAdapter();
        g.MeasureString(str, font, maxWidth, out charFit, out charFitWidth);
    }

    public override void Invalidate() => Control.InvalidateVisual();
}