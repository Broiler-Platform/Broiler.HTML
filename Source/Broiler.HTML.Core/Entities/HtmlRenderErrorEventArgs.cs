using System;

namespace Broiler.HTML.Core.Entities;

/// <summary>
/// A problem the renderer met and recovered from: an image it could not load, a stylesheet it could not
/// read, a layout pass that threw.
/// </summary>
/// <remarks>
/// Every reporter inside the renderer says which resource or which step failed, and hands over the
/// exception when there was one. Both used to be dropped on the way to this event, which left a host
/// with "an image failed" and no way to learn which image or why.
/// </remarks>
public sealed class HtmlRenderErrorEventArgs : EventArgs
{
    public HtmlRenderErrorEventArgs(HtmlRenderErrorType type)
        : this(type, message: null, exception: null)
    {
    }

    public HtmlRenderErrorEventArgs(HtmlRenderErrorType type, string? message, Exception? exception = null)
    {
        Type = type;
        Message = message;
        Exception = exception;
    }

    public HtmlRenderErrorType Type { get; }

    /// <summary>
    /// What failed, as the reporter described it — which image, which stylesheet, which step — or
    /// <see langword="null"/> when it said nothing more than its type.
    /// </summary>
    public string? Message { get; }

    /// <summary>The exception the renderer caught, when the failure was one.</summary>
    public Exception? Exception { get; }

    public override string ToString()
    {
        var text = Message is null ? $"Type: {Type}" : $"Type: {Type}, {Message}";
        return Exception is null ? text : $"{text} ({Exception.GetType().Name}: {Exception.Message})";
    }
}