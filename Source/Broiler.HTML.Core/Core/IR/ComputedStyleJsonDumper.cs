#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Broiler.HTML.Core.Core.IR;

/// <summary>
/// Serializes computed-style snapshots from a fragment tree to deterministic JSON.
/// </summary>
public static class ComputedStyleJsonDumper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ToJson(Fragment root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return JsonSerializer.Serialize(WriteFragment(root), JsonOptions) + Environment.NewLine;
    }

    private static Dictionary<string, object?> WriteFragment(Fragment fragment)
    {
        var result = new Dictionary<string, object?>
        {
            ["tagName"] = fragment.Style.TagName,
            ["kind"] = fragment.Style.Kind.ToString(),
            ["text"] = GetText(fragment),
            ["style"] = WriteStyle(fragment.Style),
            ["children"] = fragment.Children.Select(WriteFragment).ToList()
        };
        return result;
    }

    private static SortedDictionary<string, object?> WriteStyle(ComputedStyle style)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in typeof(ComputedStyle)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            result[property.Name] = NormalizeValue(property.GetValue(style));
        }
        return result;
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool boolean => boolean,
        int integer => integer,
        long integer => integer,
        float number => Round(number),
        double number => Round(number),
        decimal number => decimal.Round(number, 2),
        Enum enumValue => enumValue.ToString(),
        Color color => ColorToString(color),
        BoxEdges edges => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bottom"] = Round(edges.Bottom),
            ["left"] = Round(edges.Left),
            ["right"] = Round(edges.Right),
            ["top"] = Round(edges.Top)
        },
        _ => value.ToString()
    };

    private static string GetText(Fragment fragment)
    {
        if (fragment.Lines is null || fragment.Lines.Count == 0)
            return string.Empty;

        return string.Concat(fragment.Lines.SelectMany(line => line.Inlines).Select(inline => inline.Text ?? string.Empty));
    }

    private static double? Round(double value) => double.IsFinite(value) ? Math.Round(value, 2) : null;

    private static string? ColorToString(Color color)
    {
        if (color.IsEmpty)
            return null;
        if (color.A == 255)
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        return string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}
