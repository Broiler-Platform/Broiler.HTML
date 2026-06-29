using System.Drawing;

namespace Broiler.HTML.Orchestration;

/// <summary>
/// Read-only box geometry for a single laid-out element, in the renderer's
/// document coordinate space. The three rectangles correspond to the CSS
/// box-model levels: the border box (used by <c>offsetWidth/Height</c> and
/// <c>getBoundingClientRect</c>), the padding box, and the content box (used by
/// <c>clientWidth/Height</c>).
/// </summary>
/// <remarks>
/// Produced by <see cref="HtmlContainerInt.CollectLayoutGeometry"/> from the real
/// layout tree, so a consumer (for example the script bridge) can read accurate
/// element geometry instead of re-deriving it from computed style. Scroll extents
/// (<c>scrollWidth/Height</c>) are intentionally not included yet — they require
/// descendant-overflow computation and are added in a later increment.
/// </remarks>
public readonly record struct BoxGeometry(
    RectangleF BorderBox,
    RectangleF PaddingBox,
    RectangleF ContentBox);
