#nullable enable

using System;
using System.Linq;
using System.Text.Json;

namespace Broiler.HTML.Core.IR;

/// <summary>Serializes the canonical shared CSS model to deterministic JSON.</summary>
public static class CssStyleSheetJsonDumper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToJson(Broiler.CSS.CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        var model = new
        {
            cssText = Broiler.CSS.CssSerializer.Serialize(styleSheet),
            diagnostics = styleSheet.Diagnostics.Select(static diagnostic => new
            {
                diagnostic.Code,
                diagnostic.Message,
                severity = diagnostic.Severity.ToString(),
                diagnostic.Range.Start,
                diagnostic.Range.Length
            }).ToArray()
        };
        return JsonSerializer.Serialize(model, JsonOptions) + Environment.NewLine;
    }
}

/// <summary>Compatibility entry point for the former CssData dumper.</summary>
[Obsolete("Use CssStyleSheetJsonDumper with Broiler.CSS.CssStyleSheet.")]
public static class CssDataJsonDumper
{
    public static string ToJson(CssData cssData)
    {
        ArgumentNullException.ThrowIfNull(cssData);
        return CssStyleSheetJsonDumper.ToJson(cssData.StyleSheet);
    }
}
