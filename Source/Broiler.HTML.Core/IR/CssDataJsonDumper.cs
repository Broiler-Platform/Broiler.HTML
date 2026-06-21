#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Entities;

namespace Broiler.HTML.Core.IR;

/// <summary>
/// Serializes parsed CSS data to deterministic JSON for parser and cascade tests.
/// </summary>
public static class CssDataJsonDumper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ToJson(CssData cssData)
    {
        ArgumentNullException.ThrowIfNull(cssData);
        return JsonSerializer.Serialize(WriteCssData(cssData), JsonOptions) + Environment.NewLine;
    }

    private static SortedDictionary<string, object?> WriteCssData(CssData cssData)
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fontFaces"] = cssData.FontFaces
                .OrderBy(static face => face.Family, StringComparer.Ordinal)
                .ThenBy(static face => face.Src, StringComparer.Ordinal)
                .Select(WriteFontFace)
                .ToList(),
            ["fontFeatureValues"] = WriteFontFeatureValues(cssData.FontFeatureValues),
            ["keyframes"] = cssData.Keyframes
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => WriteKeyframes(pair.Value), StringComparer.Ordinal),
            ["mediaBlocks"] = cssData.MediaBlocks
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => WriteMediaBlocks(pair.Value), StringComparer.Ordinal)
        };
    }

    private static SortedDictionary<string, object?> WriteFontFace(CssFontFace face) =>
        new(StringComparer.Ordinal)
        {
            ["family"] = face.Family,
            ["src"] = face.Src,
            ["featureSettings"] = face.FeatureSettings
        };

    private static SortedDictionary<string, object?> WriteFontFeatureValues(
        Dictionary<string, Dictionary<string, Dictionary<string, int[]>>> values)
    {
        var families = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var family in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var types = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var type in family.Value.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                types[type.Key] = type.Value
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            }
            families[family.Key] = types;
        }
        return families;
    }

    private static List<object?> WriteKeyframes(CssKeyframeRule rule) =>
        [.. rule.Stops
            .OrderBy(static stop => stop.Offset)
            .Select(static stop => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["offset"] = Math.Round(stop.Offset, 4),
                ["properties"] = SortProperties(stop.Properties)
            })
            .Cast<object?>()];

    private static SortedDictionary<string, object?> WriteMediaBlocks(Dictionary<string, List<CssBlock>> blocks)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var selector in blocks.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            result[selector.Key] = selector.Value
                .OrderBy(static block => block.SourceOrder)
                .ThenBy(static block => block.Specificity)
                .Select(WriteBlock)
                .ToList();
        }
        return result;
    }

    private static SortedDictionary<string, object?> WriteBlock(CssBlock block) =>
        new(StringComparer.Ordinal)
        {
            ["selector"] = block.Class,
            ["specificity"] = block.Specificity,
            ["sourceOrder"] = block.SourceOrder,
            ["isUserAgent"] = block.IsUserAgent,
            ["hover"] = block.Hover,
            ["pseudoClass"] = block.PseudoClass,
            ["properties"] = SortProperties(block.Properties),
            ["importantProperties"] = block.ImportantProperties.OrderBy(static property => property, StringComparer.Ordinal).ToList(),
            ["attributeConditions"] = block.AttributeConditions?
                .OrderBy(static condition => condition.Name, StringComparer.Ordinal)
                .ThenBy(static condition => condition.Op, StringComparer.Ordinal)
                .ThenBy(static condition => condition.Value, StringComparer.Ordinal)
                .Select(WriteAttributeCondition)
                .ToList(),
            ["selectors"] = block.Selectors?
                .Select(WriteSelectorItem)
                .ToList()
        };

    private static SortedDictionary<string, string> SortProperties(IDictionary<string, string> properties) =>
        CreateSortedStringDictionary(properties);

    private static SortedDictionary<string, string> CreateSortedStringDictionary(IDictionary<string, string> properties)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            sorted[pair.Key] = pair.Value;
        return sorted;
    }

    private static SortedDictionary<string, object?> WriteAttributeCondition(CssAttributeCondition condition) =>
        new(StringComparer.Ordinal)
        {
            ["name"] = condition.Name,
            ["op"] = condition.Op,
            ["value"] = condition.Value
        };

    private static SortedDictionary<string, object?> WriteSelectorItem(CssBlockSelectorItem selector) =>
        new(StringComparer.Ordinal)
        {
            ["selector"] = selector.Class,
            ["directParent"] = selector.DirectParent,
            ["adjacentSibling"] = selector.AdjacentSibling,
            ["pseudoClass"] = selector.PseudoClass
        };
}
