using System.Collections.Generic;

namespace Broiler.HTML.Core.Core.Entities;

/// <summary>
/// Represents a single keyframe stop inside a <c>@keyframes</c> rule.
/// </summary>
public sealed class CssKeyframeStop
{
    /// <summary>
    /// The progress offset (0.0 = 0%, 1.0 = 100%).
    /// </summary>
    public double Offset { get; init; }

    /// <summary>
    /// CSS property declarations at this keyframe stop.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>
/// Represents a parsed <c>@keyframes</c> rule with its name and keyframe stops.
/// </summary>
public sealed class CssKeyframeRule
{
    /// <summary>The animation name identifier.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Ordered list of keyframe stops (by offset ascending).</summary>
    public List<CssKeyframeStop> Stops { get; init; } = new();
}
