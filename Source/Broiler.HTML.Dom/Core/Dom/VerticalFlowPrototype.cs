using System;

namespace Broiler.HTML.Dom.Core.Dom;

/// <summary>
/// PROTOTYPE feature gate for experimental vertical writing-mode flow
/// (Stage 1: logical layout + post-layout coordinate transform).
///
/// Enabled only when the environment variable <c>BROILER_VERTICAL_FLOW</c> is
/// set to <c>1</c>, so the default rendering path is completely unaffected.
/// This is an exploratory spike: it rotates box/line/word *positions* from the
/// horizontal layout frame into vertical-lr / vertical-rl physical space, but
/// does NOT yet rotate glyphs (Stage 2) or do per-glyph vertical advance
/// (Stage 3).  Multi-glyph runs therefore still render horizontally at their
/// rotated origin — correct for square fonts (e.g. Ahem) and a positional
/// approximation otherwise.
/// </summary>
internal static class VerticalFlowPrototype
{
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("BROILER_VERTICAL_FLOW") == "1";

    public static bool Enabled => _enabled;
}
