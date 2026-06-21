using System;

namespace Broiler.HTML.Core.Entities;

public sealed class HtmlRenderErrorEventArgs(HtmlRenderErrorType type, string message, Exception exception = null) : EventArgs
{
    public HtmlRenderErrorType Type { get; } = type;
    public string Message { get; } = message;
    public Exception Exception { get; } = exception;
    public override string ToString() => $"Type: {Type}";
}