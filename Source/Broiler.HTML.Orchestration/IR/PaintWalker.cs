using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

/// <summary>
/// Walks a <see cref="Fragment"/> tree and produces a flat <see cref="DisplayList"/>
/// of drawing primitives. This decouples paint from the DOM (<see cref="Dom.CssBox"/>).
/// </summary>
internal static partial class PaintWalker
{

    // Match PaintWalker.ParseFontSize's existing medium/default font-size fallback.
    private const float DefaultFontSize = 12f;

    /// <summary>Default selection highlight color (semi-transparent blue).</summary>
    private static readonly BColor SelectionHighlightColor = BColor.FromArgb(0x69, 0x33, 0x99, 0xFF);

    /// <summary>
    /// Sentinel value indicating that a selection offset is not constrained
    /// (i.e. the entire inline is selected on that side). Matches the convention
    /// used by <c>CssRect.SelectedStartOffset</c> / <c>SelectedEndOffset</c>.
    /// </summary>
    private const double FullSelectionOffset = -1;
}
