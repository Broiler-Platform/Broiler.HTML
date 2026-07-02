using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
using Broiler.Graphics;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// Background colour, background-image, and background-clip emission.
// Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    private static void EmitBackground(Fragment fragment, List<DisplayItem> items)
    {
        var style = fragment.Style;

        // CSS Backgrounds Level 4: background-clip:text — the background colour
        // is not painted normally; it is applied through text shapes instead.
        if (string.Equals(style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
            return;

        // Determine the set of rectangles to paint: per-line rects for inline elements,
        // or the single fragment bounds for block elements.
        var rects = GetPaintRects(fragment);

        foreach (var rect in rects)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            // CSS Backgrounds §2.11.4: background-clip determines the painting area.
            // Default is border-box; padding-box clips to inside borders;
            // content-box clips to inside padding.
            var effectiveBackgroundClip = GetEffectiveBackgroundClip(fragment, style.BackgroundClip);
            var fillRect = GetBackgroundClipRect(rect, fragment, effectiveBackgroundClip);
            if (fillRect.Width <= 0 || fillRect.Height <= 0)
                continue;

            BColor bgColor;
            // Background gradient
            if (style.ActualBackgroundGradient.A > 0 &&
                style.ActualBackgroundGradient != style.ActualBackgroundColor)
            {
                bgColor = style.ActualBackgroundColor.A > 0
                    ? style.ActualBackgroundColor
                    : style.ActualBackgroundGradient;
            }
            else if (style.ActualBackgroundColor.A > 0)
            {
                bgColor = style.ActualBackgroundColor;
            }
            else
            {
                continue;
            }

            // CSS Backgrounds Level 4: background-clip: border-area — paint
            // the background colour only within the border area (4 strips).
            bool hasRoundedClip = TryCreateRoundedBackgroundClipItem(rect, fragment, effectiveBackgroundClip, out var roundedClip);
            if (hasRoundedClip)
                items.Add(roundedClip);

            if (effectiveBackgroundClip.Equals("border-area", StringComparison.OrdinalIgnoreCase))
            {
                EmitBorderAreaBorder(rect, fragment, items, bgColor);
            }
            else
            {
                items.Add(new FillRectItem { Bounds = fillRect, Color = bgColor });
            }

            if (hasRoundedClip)
                items.Add(new RestoreItem { Bounds = fillRect });
        }
    }

    /// <summary>
    /// CSS Backgrounds Level 4 <c>background-clip: border-area</c>:
    /// Emits a border-shaped fill using the same per-side styles as normal
    /// border painting so <c>hidden</c>, <c>dashed</c>, <c>dotted</c>, etc.
    /// match the corresponding WPT reference rendering.
    /// </summary>
    private static void EmitBorderAreaBorder(RectangleF bounds, Fragment fragment, List<DisplayItem> items, BColor color)
    {
        var style = fragment.Style;
        var border = fragment.Border;
        bool hasTop = HasBorder(style.BorderTopStyle, border.Top);
        bool hasRight = HasBorder(style.BorderRightStyle, border.Right);
        bool hasBottom = HasBorder(style.BorderBottomStyle, border.Bottom);
        bool hasLeft = HasBorder(style.BorderLeftStyle, border.Left);

        if (!hasTop && !hasRight && !hasBottom && !hasLeft)
            return;

        items.Add(new DrawBorderItem
        {
            Bounds = bounds,
            Widths = border,
            TopColor = hasTop ? color : BColor.Empty,
            RightColor = hasRight ? color : BColor.Empty,
            BottomColor = hasBottom ? color : BColor.Empty,
            LeftColor = hasLeft ? color : BColor.Empty,
            Style = style.BorderTopStyle ?? "solid",
            TopStyle = style.BorderTopStyle ?? "none",
            RightStyle = style.BorderRightStyle ?? "none",
            BottomStyle = style.BorderBottomStyle ?? "none",
            LeftStyle = style.BorderLeftStyle ?? "none",
            CornerNw = style.ActualCornerNw,
            CornerNe = style.ActualCornerNe,
            CornerSe = style.ActualCornerSe,
            CornerSw = style.ActualCornerSw,
        });
    }

    private static void EmitBackgroundImage(Fragment fragment, List<DisplayItem> items, RectangleF viewport = default)
    {
        // CSS Backgrounds Level 4: background-clip:text — background image
        // is not painted normally (it is clipped to text shapes).
        if (string.Equals(fragment.Style.BackgroundClip, "text", StringComparison.OrdinalIgnoreCase))
            return;

        // CSS3: handle gradient background layers even without a url-based image.
        if (fragment.BackgroundImageHandle == null)
        {
            if (HasGradientBackgroundImage(fragment.Style.BackgroundImage))
            {
                EmitElementGradientLayers(fragment, items, viewport);
            }
            return;
        }

        var backgroundLayers = SplitOnTopLevelCommas(fragment.Style.BackgroundImage ?? "none");
        if (backgroundLayers.Count == 0)
            backgroundLayers.Add("none");

        var repeats = SplitOnTopLevelCommas(fragment.Style.BackgroundRepeat ?? "repeat");
        var attachments = SplitOnTopLevelCommas(fragment.Style.BackgroundAttachment ?? "scroll");
        var positions = SplitOnTopLevelCommas(fragment.Style.BackgroundPosition ?? "0% 0%");
        var sizes = SplitOnTopLevelCommas(fragment.Style.BackgroundSize ?? "auto");
        var origins = SplitOnTopLevelCommas(fragment.Style.BackgroundOrigin ?? "padding-box");
        var clips = SplitOnTopLevelCommas(fragment.Style.BackgroundClip ?? "border-box");
        var layerHandles = NormalizeBackgroundImageHandles(fragment.BackgroundImageHandle, backgroundLayers.Count);

        // Use GetPaintRects to handle inline elements (which may have zero
        // Size but non-empty InlineRects from per-line-box layout).
        var rects = GetPaintRects(fragment);

        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            for (int i = backgroundLayers.Count - 1; i >= 0; i--)
            {
                var layerValue = backgroundLayers[i].Trim();
                if (string.IsNullOrEmpty(layerValue)
                    || layerValue.Equals("none", StringComparison.OrdinalIgnoreCase)
                    || layerValue.Contains("gradient(", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EmitBackgroundImageLayer(
                    fragment,
                    bounds,
                    layerHandles[i],
                    repeats.Count > 0 ? repeats[i % repeats.Count].Trim() : "repeat",
                    attachments.Count > 0 ? attachments[i % attachments.Count].Trim() : "scroll",
                    positions.Count > 0 ? positions[i % positions.Count].Trim() : "0% 0%",
                    sizes.Count > 0 ? sizes[i % sizes.Count].Trim() : "auto",
                    origins.Count > 0 ? origins[i % origins.Count].Trim() : "padding-box",
                    clips.Count > 0 ? clips[i % clips.Count].Trim() : "border-box",
                    items,
                    viewport);
            }
        }
    }

    private static void EmitBackgroundImageLayer(
        Fragment fragment,
        RectangleF bounds,
        object? imageHandle,
        string repeat,
        string attachment,
        string position,
        string size,
        string origin,
        string clip,
        List<DisplayItem> items,
        RectangleF viewport)
    {
        if (imageHandle is not RImage image)
            return;

        var effectiveBackgroundClip = GetEffectiveBackgroundClip(fragment, clip);
        var clipRect = GetBackgroundClipRect(bounds, fragment, effectiveBackgroundClip);
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return;

        var originRect = GetBackgroundPositioningAreaRect(bounds, fragment, origin);
        bool isFixed = attachment == "fixed"
            && viewport.Width > 0
            && viewport.Height > 0
            && !fragment.HasTransformAncestor;
        bool isLocal = attachment == "local";
        var positioningArea = isFixed
            ? viewport
            : isLocal
                ? GetLocalBackgroundPositioningAreaRect(bounds, fragment, originRect)
                : originRect;
        bool hasRoundedClip = TryCreateRoundedBackgroundClipItem(bounds, fragment, effectiveBackgroundClip, out var roundedClip);

        ParseBackgroundSizeForImage(
            size,
            positioningArea.Width,
            positioningArea.Height,
            (float)image.IntrinsicWidth,
            (float)image.IntrinsicHeight,
            image.HasIntrinsicRatio,
            (float)image.IntrinsicAspectRatio,
            image.HasIntrinsicWidth,
            image.HasIntrinsicHeight,
            out float tileW,
            out float tileH);

        var tileOrigin = new PointF(positioningArea.X, positioningArea.Y);
        ApplyBackgroundPositionOffset(ref tileOrigin, position, positioningArea.Width, positioningArea.Height, tileW > 0 ? tileW : (float)image.Width, tileH > 0 ? tileH : (float)image.Height, GetPositionEmSize(fragment.Style));

        bool hasBgBlend = !string.IsNullOrEmpty(fragment.Style.BackgroundBlendMode)
            && !fragment.Style.BackgroundBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase);
        if (hasRoundedClip)
            items.Add(roundedClip);
        if (hasBgBlend)
            items.Add(new BlendModeItem { Bounds = clipRect, Mode = fragment.Style.BackgroundBlendMode });

        if (effectiveBackgroundClip.Equals("border-area", StringComparison.OrdinalIgnoreCase))
        {
            if (image.TryGetUniformColor(out var uniformColor))
                EmitBorderAreaBorder(bounds, fragment, items, uniformColor);
            else
                EmitBorderAreaTiledImage(bounds, image, fragment, items, tileOrigin, tileW, tileH, repeat);
        }
        else
        {
            items.Add(new DrawTiledImageItem
            {
                Bounds = clipRect,
                ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty,
                FillRect = clipRect,
                PositioningArea = positioningArea,
                TileOrigin = tileOrigin,
                Repeat = repeat,
                TileWidth = tileW,
                TileHeight = tileH,
            });
        }

        if (hasBgBlend)
            items.Add(new RestoreBlendModeItem { Bounds = clipRect });
        if (hasRoundedClip)
            items.Add(new RestoreItem { Bounds = clipRect });
    }

    private static void ApplyBackgroundPositionOffset(ref PointF tileOrigin, string position, float containerWidth, float containerHeight, float imageWidth, float imageHeight, float emSize)
    {
        if (string.IsNullOrEmpty(position))
            return;

        var parts = position.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? xVal = null, yVal = null;
        foreach (var p in parts)
        {
            if (IsHorizontalKeyword(p))
                xVal = p;
            else if (IsVerticalKeyword(p))
                yVal = p;
            else if (p.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                if (xVal == null) xVal = p;
                else yVal ??= p;
            }
            else
            {
                if (xVal == null) xVal = p;
                else yVal ??= p;
            }
        }

        tileOrigin.X += ParsePositionValue(xVal, containerWidth, imageWidth, emSize);
        tileOrigin.Y += ParsePositionValue(yVal, containerHeight, imageHeight, emSize);
    }

    /// <summary>
    /// Emits gradient layer display items for a non-canvas element's background.
    /// </summary>
    private static void EmitElementGradientLayers(Fragment fragment, List<DisplayItem> items, RectangleF viewport)
    {
        var rects = GetPaintRects(fragment);
        foreach (var bounds in rects)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            var imgRect = GetBackgroundClipRect(bounds, fragment, fragment.Style.BackgroundClip);

            if (imgRect.Width <= 0 || imgRect.Height <= 0)
                continue;

            EmitGradientLayers(fragment, imgRect, viewport.Width > 0 ? viewport : imgRect, items);
        }
    }

    /// <summary>Parses a single CSS background-position value (keyword, px, %, or length).</summary>
    /// <param name="val">The position token (e.g. "right", "50%", "10px").</param>
    /// <param name="containerSize">Width or height of the positioning area.</param>
    /// <param name="imageSize">Width or height of the background image.</param>
    /// <returns>Offset in pixels from the origin.</returns>
    private static float ParsePositionValue(string val, float containerSize, float imageSize, float emSize)
    {
        if (string.IsNullOrEmpty(val)) return 0;

        // CSS2.1 §14.2.1 keyword equivalences.
        if (val.Equals("left", StringComparison.OrdinalIgnoreCase)
            || val.Equals("top", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (val.Equals("right", StringComparison.OrdinalIgnoreCase)
            || val.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            return containerSize - imageSize;
        if (val.Equals("center", StringComparison.OrdinalIgnoreCase))
            return (containerSize - imageSize) / 2f;

        if (val.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(val.AsSpan(0, val.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return px;
        }
        else if (val.EndsWith('%'))
        {
            // CSS2.1 §14.2.1: percentage positions use (container - image) as
            // the reference length so that 100% places the image flush-right.
            if (float.TryParse(val.AsSpan(0, val.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return (containerSize - imageSize) * pct / 100f;
        }
        else if (CssLengthParser.IsValidLength(val))
        {
            return (float)CssLengthParser.ParseLength(val, hundredPercent: 0, emFactor: emSize, defaultUnit: null);
        }
        else if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float raw))
        {
            return raw;
        }
        return 0;
    }

    private static float GetPositionEmSize(ComputedStyle style)
    {
        float fontSize;
        if (CssLengthParser.IsValidLength(style.FontSize))
        {
            fontSize = (float)CssLengthParser.ParseLength(
                style.FontSize,
                hundredPercent: 0,
                emFactor: DefaultFontSize,
                defaultUnit: null);
        }
        else
        {
            // ParseFontSize returns values in CSS points (matching CssConstants.FontSize = 12pt).
            // Convert pt -> px so that em-based positions match browser rendering (12pt = 16px).
            fontSize = (float)(ParseFontSize(style.FontSize) * (96.0 / 72.0));
        }

        return fontSize > 0 ? fontSize : DefaultFontSize;
    }

    private static bool IsHorizontalKeyword(string val) =>
        val.Equals("left", StringComparison.OrdinalIgnoreCase)
        || val.Equals("right", StringComparison.OrdinalIgnoreCase);

    private static bool IsVerticalKeyword(string val) =>
        val.Equals("top", StringComparison.OrdinalIgnoreCase)
        || val.Equals("bottom", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CSS Backgrounds Level 4 <c>background-clip: border-area</c>:
    /// Emits 4 tiled-image items, one for each border strip.
    /// </summary>
    private static void EmitBorderAreaTiledImage(RectangleF bounds, object? imageHandle, Fragment fragment, List<DisplayItem> items,
        PointF tileOrigin, float tileW, float tileH, string repeat)
    {
        var border = fragment.Border;
        float bLeft = (float)border.Left, bTop = (float)border.Top;
        float bRight = (float)border.Right, bBottom = (float)border.Bottom;

        // Top strip
        if (bTop > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Y, bounds.Width, bTop);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Bottom strip
        if (bBottom > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Bottom - bBottom, bounds.Width, bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Left strip (between top and bottom)
        if (bLeft > 0)
        {
            var strip = new RectangleF(bounds.X, bounds.Y + bTop, bLeft, bounds.Height - bTop - bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
        // Right strip (between top and bottom)
        if (bRight > 0)
        {
            var strip = new RectangleF(bounds.X + bounds.Width - bRight, bounds.Y + bTop, bRight, bounds.Height - bTop - bBottom);
                items.Add(new DrawTiledImageItem
                {
                Bounds = strip, ImageHandle = imageHandle,
                SourceRect = RectangleF.Empty, FillRect = strip, PositioningArea = strip,
                TileOrigin = tileOrigin, Repeat = repeat, TileWidth = tileW, TileHeight = tileH,
            });
        }
    }
}
