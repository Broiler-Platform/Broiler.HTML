namespace Broiler.HTML.Core.Entities;

public sealed class FormInputElementData<T>(string id, string name, string type, string value, T rectangle)
{
    public string Id { get; } = id ?? string.Empty;
    public string Name { get; } = name ?? string.Empty;
    public string Type { get; } = type ?? string.Empty;
    public string Value { get; } = value ?? string.Empty;
    public T Rectangle { get; } = rectangle;

    public override string ToString() => $"Id: {Id}, Name: {Name}, Type: {Type}, Rectangle: {Rectangle}";
}
