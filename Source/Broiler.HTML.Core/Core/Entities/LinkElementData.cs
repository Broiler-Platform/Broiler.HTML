namespace Broiler.HTML.Core.Core.Entities;

public sealed class LinkElementData<T>(string id, string href, T rectangle)
{
    public string Id { get; } = id;
    public string Href { get; } = href;
    public T Rectangle { get; } = rectangle;
    public bool IsAnchor => Href.Length > 0 && Href[0] == '#';
    public bool IsTopOfPageAnchor => Href == "#";
    public string AnchorId => IsAnchor && Href.Length > 1 ? Href.Substring(1) : string.Empty;

    public override string ToString() => $"Id: {Id}, Href: {Href}, Rectangle: {Rectangle}";
}