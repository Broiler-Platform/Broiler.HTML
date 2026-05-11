using Broiler.HTML.Adapters;
using System;
using System.Windows;
using System.Windows.Controls;
using PointF = System.Drawing.PointF;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class ContextMenuAdapter : RContextMenu
{
    private readonly ContextMenu _contextMenu;

    public ContextMenuAdapter() => _contextMenu = new ContextMenu();

    public override int ItemsCount => _contextMenu.Items.Count;

    public override void AddDivider() => _contextMenu.Items.Add(new Separator());

    public override void AddItem(string text, bool enabled, EventHandler onClick)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(onClick);

        var item = new MenuItem { Header = text, IsEnabled = enabled };
        item.Click += new RoutedEventHandler(onClick);
        _contextMenu.Items.Add(item);
    }

    public override void RemoveLastDivider()
    {
        if (_contextMenu.Items[_contextMenu.Items.Count - 1].GetType() == typeof(Separator))
            _contextMenu.Items.RemoveAt(_contextMenu.Items.Count - 1);
    }

    public override void Show(RControl parent, PointF location)
    {
        _contextMenu.PlacementTarget = ((ControlAdapter)parent).Control;
        _contextMenu.PlacementRectangle = new Rect(Utilities.Utils.ConvertRound(location), Size.Empty);
        _contextMenu.IsOpen = true;
    }

    public override void Dispose()
    {
        _contextMenu.IsOpen = false;
        _contextMenu.PlacementTarget = null;
        _contextMenu.Items.Clear();
    }
}