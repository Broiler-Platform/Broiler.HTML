using System;

namespace Broiler.HTML.Core.Entities;

public sealed class HtmlRenderErrorEventArgs(HtmlRenderErrorType type) : EventArgs
{
    public HtmlRenderErrorType Type { get; } = type;

    public override string ToString() => $"Type: {Type}";
}