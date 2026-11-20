using System;
using System.Buffers;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Graphics.Color;
using Broiler.Layout.IR;

namespace Broiler.HTML.Core.IR;

/// <summary>
/// Serializes computed-style snapshots from a fragment tree to deterministic JSON.
/// </summary>
public static class ComputedStyleJsonDumper
{
    private static readonly PropertyInfo[] StyleProperties = typeof(ComputedStyle)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .OrderBy(static property => property.Name, StringComparer.Ordinal)
        .ToArray();

    // Each box nests two levels — its object and its children array — so the serializer's default limit
    // of 64 fails at 32 nested boxes, which a real page passes (a Wikipedia article's tree is about
    // forty deep). The limit only guards against unbounded recursion, and a box tree is bounded by
    // its document.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        MaxDepth = 4096,
    };

    public static string ToJson(Fragment root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteFragment(writer, root);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + Environment.NewLine;
    }

    private static void WriteFragment(Utf8JsonWriter writer, Fragment fragment)
    {
        writer.WriteStartObject();
        writer.WriteString("tagName", fragment.Style.TagName);
        writer.WriteString("kind", fragment.Style.Kind.ToString());
        writer.WriteString("text", GetText(fragment));

        writer.WritePropertyName("style");
        WriteStyle(writer, fragment.Style);

        writer.WriteStartArray("children");
        foreach (var child in fragment.Children)
            WriteFragment(writer, child);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteStyle(Utf8JsonWriter writer, ComputedStyle style)
    {
        writer.WriteStartObject();
        foreach (var property in StyleProperties)
        {
            writer.WritePropertyName(property.Name);
            WriteNormalizedValue(writer, property.GetValue(style));
        }
        writer.WriteEndObject();
    }

    private static void WriteNormalizedValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case int integer:
                writer.WriteNumberValue(integer);
                break;
            case long integer:
                writer.WriteNumberValue(integer);
                break;
            case float number:
                WriteNullableNumber(writer, Round(number));
                break;
            case double number:
                WriteNullableNumber(writer, Round(number));
                break;
            case decimal number:
                writer.WriteNumberValue(decimal.Round(number, 2));
                break;
            case Enum enumValue:
                writer.WriteStringValue(enumValue.ToString());
                break;
            case BColor color:
                WriteStringOrNull(writer, ColorToString(color));
                break;
            case Color color:
                WriteStringOrNull(writer, ColorToString(color));
                break;
            case BoxEdges edges:
                writer.WriteStartObject();
                writer.WritePropertyName("bottom");
                WriteNullableNumber(writer, Round(edges.Bottom));
                writer.WritePropertyName("left");
                WriteNullableNumber(writer, Round(edges.Left));
                writer.WritePropertyName("right");
                WriteNullableNumber(writer, Round(edges.Right));
                writer.WritePropertyName("top");
                WriteNullableNumber(writer, Round(edges.Top));
                writer.WriteEndObject();
                break;
            default:
                WriteStringOrNull(writer, value.ToString());
                break;
        }
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, double? value)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string? value)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }

    private static string GetText(Fragment fragment)
    {
        if (fragment.Lines is null || fragment.Lines.Count == 0)
            return string.Empty;

        return string.Concat(fragment.Lines.SelectMany(line => line.Inlines).Select(inline => inline.Text ?? string.Empty));
    }

    private static double? Round(double value) => double.IsFinite(value) ? Math.Round(value, 2) : null;

    // Computed colors are BColor since layout moved to Broiler.Layout; without this they fell
    // through to ToString() and dumped as "#FF000000". Same format as the System.Drawing branch.
    private static string? ColorToString(BColor color)
    {
        if (color.IsEmpty)
            return null;

        if (color.A == 255)
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

        return string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static string? ColorToString(Color color)
    {
        if (color.IsEmpty)
            return null;

        if (color.A == 255)
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

        return string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}
