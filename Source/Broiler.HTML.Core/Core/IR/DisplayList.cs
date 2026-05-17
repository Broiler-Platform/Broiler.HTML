using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace Broiler.HTML.Core.Core.IR;

/// <summary>
/// Flat, ordered list of drawing primitives.
/// Produced by paint; consumed by raster. No DOM/style references.
/// </summary>
public sealed class DisplayList
{
    public IReadOnlyList<DisplayItem> Items { get; init; } = [];

    /// <summary>
    /// Serialises this display list to deterministic, indented JSON.
    /// Coordinates are rounded to 2 decimal places; platform-specific handles are excluded.
    /// </summary>
    public string ToJson() => DisplayListJsonDumper.ToJson(this);
}

/// <summary>
/// Base class for all display list drawing primitives.
/// </summary>
[JsonDerivedType(typeof(FillRectItem), "FillRect")]
[JsonDerivedType(typeof(DrawBorderItem), "DrawBorder")]
[JsonDerivedType(typeof(DrawTextItem), "DrawText")]
[JsonDerivedType(typeof(DrawImageItem), "DrawImage")]
[JsonDerivedType(typeof(DrawTiledImageItem), "DrawTiledImage")]
[JsonDerivedType(typeof(ClipItem), "Clip")]
[JsonDerivedType(typeof(RestoreItem), "Restore")]
[JsonDerivedType(typeof(OpacityItem), "Opacity")]
[JsonDerivedType(typeof(RestoreOpacityItem), "RestoreOpacity")]
[JsonDerivedType(typeof(DrawLineItem), "DrawLine")]
[JsonDerivedType(typeof(DrawSvgRectItem), "DrawSvgRect")]
[JsonDerivedType(typeof(DrawSvgEllipseItem), "DrawSvgEllipse")]
[JsonDerivedType(typeof(DrawSvgTextItem), "DrawSvgText")]
[JsonDerivedType(typeof(DrawSvgLineItem), "DrawSvgLine")]
[JsonDerivedType(typeof(DrawSvgPolygonItem), "DrawSvgPolygon")]
[JsonDerivedType(typeof(DrawSvgPolylineItem), "DrawSvgPolyline")]
[JsonDerivedType(typeof(BlendModeItem), "BlendMode")]
[JsonDerivedType(typeof(RestoreBlendModeItem), "RestoreBlendMode")]
[JsonDerivedType(typeof(DrawTiledGradientItem), "DrawTiledGradient")]
public abstract class DisplayItem
{
    public RectangleF Bounds { get; init; }
}

/// <summary>Fills a rectangle with a solid color.</summary>
public sealed class FillRectItem : DisplayItem
{
    public Color Color { get; init; }
}

/// <summary>Draws a border around a rectangle.</summary>
public sealed class DrawBorderItem : DisplayItem
{
    public BoxEdges Widths { get; init; } = BoxEdges.Zero;
    public Color TopColor { get; init; }
    public Color RightColor { get; init; }
    public Color BottomColor { get; init; }
    public Color LeftColor { get; init; }
    public string Style { get; init; } = "solid";

    /// <summary>Per-side border styles (Phase 3). These are the authoritative style values used by <c>RGraphicsRasterBackend</c>.</summary>
    public string TopStyle { get; init; } = "solid";
    public string RightStyle { get; init; } = "solid";
    public string BottomStyle { get; init; } = "solid";
    public string LeftStyle { get; init; } = "solid";

    /// <summary>Corner radii for rounded borders (Phase 3).</summary>
    public double CornerNw { get; init; }
    public double CornerNe { get; init; }
    public double CornerSe { get; init; }
    public double CornerSw { get; init; }
}

/// <summary>Draws a text string at a given origin.</summary>
public sealed class DrawTextItem : DisplayItem
{
    public string Text { get; init; } = string.Empty;
    public string FontFamily { get; init; } = string.Empty;
    public float FontSize { get; init; }
    public string FontWeight { get; init; } = "normal";
    public Color Color { get; init; }
    public PointF Origin { get; init; }

    /// <summary>Platform-specific font handle for rendering (Phase 3).</summary>
    public object? FontHandle { get; init; }

    /// <summary>Whether text is right-to-left (Phase 3).</summary>
    public bool IsRtl { get; init; }

    /// <summary>Text shadow horizontal offset in pixels (0 = no shadow).</summary>
    public float TextShadowOffsetX { get; init; }

    /// <summary>Text shadow vertical offset in pixels (0 = no shadow).</summary>
    public float TextShadowOffsetY { get; init; }

    /// <summary>Text shadow color. Empty means no shadow.</summary>
    public Color TextShadowColor { get; init; }

    /// <summary>Optional gradient color stops used for background-clip:text.</summary>
    public IReadOnlyList<GradientStop>? GradientStops { get; init; }

    /// <summary>Gradient angle in degrees (0 = to top, 90 = to right, 180 = to bottom).</summary>
    public float GradientAngle { get; init; } = 180f;

    /// <summary>Color-interpolation space for the gradient ("srgb", "hsl", "oklch").</summary>
    public string GradientInterpolationSpace { get; init; } = "srgb";

    /// <summary>Background painting bounds used to align background-clip:text gradients.</summary>
    public RectangleF GradientBounds { get; init; }
}

/// <summary>Draws an image into a destination rectangle.</summary>
public sealed class DrawImageItem : DisplayItem
{
    public object? ImageHandle { get; init; }
    public RectangleF SourceRect { get; init; }
    public RectangleF DestRect { get; init; }
}

/// <summary>Draws a tiled (repeated) background image within a clip region.</summary>
public sealed class DrawTiledImageItem : DisplayItem
{
    public object? ImageHandle { get; init; }
    /// <summary>Source rectangle within the image (Empty = full image).</summary>
    public RectangleF SourceRect { get; init; }
    /// <summary>Rectangle to fill with the tiled pattern.</summary>
    public RectangleF FillRect { get; init; }
    /// <summary>Background positioning area used to place or space tiles.</summary>
    public RectangleF PositioningArea { get; init; }
    /// <summary>Tile origin (top-left of first tile).</summary>
    public PointF TileOrigin { get; init; }
    /// <summary>CSS background-repeat value.</summary>
    public string Repeat { get; init; } = "repeat";
    /// <summary>Visual tile width from CSS background-size (0 = use source image width).</summary>
    public float TileWidth { get; init; }
    /// <summary>Visual tile height from CSS background-size (0 = use source image height).</summary>
    public float TileHeight { get; init; }
}

/// <summary>Pushes a clip rectangle onto the clip stack.</summary>
public sealed class ClipItem : DisplayItem
{
    public RectangleF ClipRect { get; init; }
    /// <summary>Corner radii for rounded clips (0 = sharp corners).</summary>
    public double CornerNw { get; init; }
    public double CornerNwY { get; init; }
    public double CornerNe { get; init; }
    public double CornerNeY { get; init; }
    public double CornerSe { get; init; }
    public double CornerSeY { get; init; }
    public double CornerSw { get; init; }
    public double CornerSwY { get; init; }
}

/// <summary>Pops the most recent clip from the clip stack.</summary>
public sealed class RestoreItem : DisplayItem { }

/// <summary>Applies an opacity value to subsequent items until restored.</summary>
public sealed class OpacityItem : DisplayItem
{
    public float Opacity { get; init; }
}

/// <summary>Restores from an opacity layer pushed by <see cref="OpacityItem"/>.</summary>
public sealed class RestoreOpacityItem : DisplayItem { }

/// <summary>Begins a compositing layer with the specified CSS blend mode.</summary>
public sealed class BlendModeItem : DisplayItem
{
    /// <summary>CSS mix-blend-mode value (e.g. "multiply", "screen", "overlay").</summary>
    public string Mode { get; init; } = "normal";
}

/// <summary>Restores from a blend mode layer pushed by <see cref="BlendModeItem"/>.</summary>
public sealed class RestoreBlendModeItem : DisplayItem { }

/// <summary>
/// Draws a tiled CSS gradient (e.g. linear-gradient) at a specified tile size.
/// The rendering backend creates a gradient bitmap on-the-fly and tiles it.
/// </summary>
public sealed class DrawTiledGradientItem : DisplayItem
{
    /// <summary>CSS gradient function string (e.g. "linear-gradient(rgba(0,255,0,0.5), rgba(0,0,255,0.5))").</summary>
    public string GradientFunction { get; init; } = string.Empty;
    /// <summary>Width of each gradient tile in pixels.</summary>
    public float TileWidth { get; init; }
    /// <summary>Height of each gradient tile in pixels.</summary>
    public float TileHeight { get; init; }
    /// <summary>Rectangle to fill with the tiled pattern.</summary>
    public RectangleF FillRect { get; init; }
    /// <summary>Tile origin (top-left of first tile).</summary>
    public PointF TileOrigin { get; init; }
    /// <summary>CSS background-repeat value.</summary>
    public string Repeat { get; init; } = "repeat";
    /// <summary>Pre-parsed gradient color stops (color, position 0..1 pairs).</summary>
    public IReadOnlyList<GradientStop>? Stops { get; init; }
    /// <summary>Gradient angle in degrees (0 = to top, 90 = to right, 180 = to bottom).</summary>
    public float Angle { get; init; } = 180f;
    /// <summary>Color-interpolation space for the gradient ("srgb", "hsl", "oklch").</summary>
    public string InterpolationSpace { get; init; } = "srgb";
}

/// <summary>A single color stop in a CSS gradient.</summary>
public sealed class GradientStop
{
    public Color Color { get; init; }
    /// <summary>Position along the gradient line (0.0 = start, 1.0 = end).</summary>
    public float Position { get; init; }
}

/// <summary>Draws a line between two points (Phase 3).</summary>
public sealed class DrawLineItem : DisplayItem
{
    public PointF Start { get; init; }
    public PointF End { get; init; }
    public Color Color { get; init; }
    public float Width { get; init; } = 1;
    public string DashStyle { get; init; } = "solid";
}

/// <summary>Draws an SVG rectangle.</summary>
public sealed class DrawSvgRectItem : DisplayItem
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public Color Fill { get; init; }
    public Color Stroke { get; init; }
    public float StrokeWidth { get; init; }
}

/// <summary>Draws an SVG circle or ellipse.</summary>
public sealed class DrawSvgEllipseItem : DisplayItem
{
    public float Cx { get; init; }
    public float Cy { get; init; }
    public float Rx { get; init; }
    public float Ry { get; init; }
    public Color Fill { get; init; }
    public Color Stroke { get; init; }
    public float StrokeWidth { get; init; }
}

/// <summary>Draws SVG text.</summary>
public sealed class DrawSvgTextItem : DisplayItem
{
    public string Text { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float FontSize { get; init; }
    public string FontFamily { get; init; } = string.Empty;
    public Color Fill { get; init; }
    /// <summary>Platform-specific font handle for rendering.</summary>
    public object? FontHandle { get; init; }
}

/// <summary>Draws an SVG line.</summary>
public sealed class DrawSvgLineItem : DisplayItem
{
    public float X1 { get; init; }
    public float Y1 { get; init; }
    public float X2 { get; init; }
    public float Y2 { get; init; }
    public Color Stroke { get; init; }
    public float StrokeWidth { get; init; }
}

/// <summary>Draws an SVG polygon.</summary>
public sealed class DrawSvgPolygonItem : DisplayItem
{
    public IReadOnlyList<PointF> Points { get; init; } = [];
    public Color Fill { get; init; }
    public Color Stroke { get; init; }
    public float StrokeWidth { get; init; }
}

/// <summary>Draws an SVG polyline.</summary>
public sealed class DrawSvgPolylineItem : DisplayItem
{
    public IReadOnlyList<PointF> Points { get; init; } = [];
    public Color Fill { get; init; }
    public Color Stroke { get; init; }
    public float StrokeWidth { get; init; }
}
