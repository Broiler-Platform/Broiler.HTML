using Broiler.CSS;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.HTML.Core.IR;

/// <summary>Serializes the canonical shared CSS model to deterministic JSON.</summary>
public static class CssStyleSheetJsonDumper
{
    public static string ToJson(CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        var model = new CssStyleSheetJsonModel
        {
            cssText = CssSerializer.Serialize(styleSheet),
            diagnostics = styleSheet.Diagnostics.Select(static diagnostic => new CssDiagnosticJsonModel
            {
                Code = diagnostic.Code,
                Message = diagnostic.Message,
                severity = diagnostic.Severity.ToString(),
                Start = diagnostic.Range.Start,
                Length = diagnostic.Range.Length
            }).ToArray()
        };
        return JsonSerializer.Serialize(model, CssStyleSheetJsonContext.Default.CssStyleSheetJsonModel) + Environment.NewLine;
    }
}

internal sealed class CssStyleSheetJsonModel
{
    public required string cssText { get; init; }
    public required CssDiagnosticJsonModel[] diagnostics { get; init; }
}

internal sealed class CssDiagnosticJsonModel
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string severity { get; init; }
    public required int Start { get; init; }
    public required int Length { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CssStyleSheetJsonModel))]
internal partial class CssStyleSheetJsonContext : JsonSerializerContext;
