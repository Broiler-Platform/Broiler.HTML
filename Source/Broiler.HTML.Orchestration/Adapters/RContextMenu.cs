using System;
using System.Drawing;

namespace Broiler.HTML.Adapters;


public abstract class RContextMenu : IDisposable
{
    public abstract int ItemsCount { get; }
    public abstract void AddDivider();
    public abstract void AddItem(string text, bool enabled, EventHandler onClick);
    public abstract void RemoveLastDivider();
    public abstract void Show(RControl parent, PointF location);
    public abstract void Dispose();
}