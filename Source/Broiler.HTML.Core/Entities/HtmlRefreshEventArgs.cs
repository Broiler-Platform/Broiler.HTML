using System;

namespace Broiler.HTML.Core.Entities;

public sealed class HtmlRefreshEventArgs(bool layout) : EventArgs
{
    public bool Layout { get; } = layout;
    public override string ToString() => $"Layout: {Layout}";
}