namespace Broiler.HTML.Core.Entities;

public sealed class LinkElementData<T>(string id, string href, T rectangle)
{
    public string Id { get; } = id;
    public string Href { get; } = href;
    public T Rectangle { get; } = rectangle;
    public override string ToString() => $"Id: {Id}, Href: {Href}, Rectangle: {Rectangle}";
}