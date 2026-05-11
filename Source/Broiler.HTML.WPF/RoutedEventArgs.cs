using System;
using System.Windows;

namespace Broiler.HTML.WPF;

public delegate void RoutedEventHandler<T>(object sender, RoutedEventArgs<T> args) where T : class;

public sealed class RoutedEventArgs<T> : RoutedEventArgs where T : class
{
    public RoutedEventArgs(RoutedEvent routedEvent, T data) : base(routedEvent)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    public RoutedEventArgs(RoutedEvent routedEvent, object source, T data) : base(routedEvent, source)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    public T Data { get; }

    public override string ToString() => $"RoutedEventArgs({Data})";
}