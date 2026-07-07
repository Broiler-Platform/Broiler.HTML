using System.Collections.Generic;
using System.Drawing;
using Broiler.Layout.IR;


namespace Broiler.HTML.Orchestration.IR;

// Display-item translation (position:fixed viewport offset, scroll offsets).
// Split out of PaintWalker.cs for size.
internal static partial class PaintWalker
{
    /// <summary>
    /// Offsets all display items starting at <paramref name="startIndex"/> by
    /// (<paramref name="dx"/>, <paramref name="dy"/>).  Used to reposition
    /// <c>position:fixed</c> fragments to viewport-relative coordinates and
    /// to apply scroll offsets during painting.
    /// </summary>
    internal static void OffsetDisplayItems(List<DisplayItem> items, int startIndex, float dx, float dy)
    {
        if (dx == 0 && dy == 0)
            return;

        for (int i = startIndex; i < items.Count; i++)
            items[i] = OffsetItem(items[i], dx, dy);
    }

    internal static DisplayItem OffsetItem(DisplayItem item, float dx, float dy)
    {
        var ob = OffsetRect(item.Bounds, dx, dy);
        return item switch
        {
            FillRectItem f => new FillRectItem { Bounds = ob, Color = f.Color },
            DrawBorderItem b => new DrawBorderItem
            {
                Bounds = ob,
                Widths = b.Widths,
                TopColor = b.TopColor,
                RightColor = b.RightColor,
                BottomColor = b.BottomColor,
                LeftColor = b.LeftColor,
                Style = b.Style,
                TopStyle = b.TopStyle,
                RightStyle = b.RightStyle,
                BottomStyle = b.BottomStyle,
                LeftStyle = b.LeftStyle,
                CornerNw = b.CornerNw,
                CornerNe = b.CornerNe,
                CornerSe = b.CornerSe,
                CornerSw = b.CornerSw,
            },
            DrawTextItem t => new DrawTextItem
            {
                Bounds = ob,
                Text = t.Text,
                FontFamily = t.FontFamily,
                FontSize = t.FontSize,
                FontWeight = t.FontWeight,
                Color = t.Color,
                Origin = new PointF(t.Origin.X + dx, t.Origin.Y + dy),
                FontHandle = t.FontHandle,
                IsRtl = t.IsRtl,
                TextShadowOffsetX = t.TextShadowOffsetX,
                TextShadowOffsetY = t.TextShadowOffsetY,
                TextShadowColor = t.TextShadowColor,
                GradientStops = t.GradientStops,
                GradientAngle = t.GradientAngle,
                GradientInterpolationSpace = t.GradientInterpolationSpace,
                GradientBounds = OffsetRect(t.GradientBounds, dx, dy),
            },
            DrawImageItem img => new DrawImageItem
            {
                Bounds = ob,
                ImageHandle = img.ImageHandle,
                SourceRect = img.SourceRect,
                DestRect = OffsetRect(img.DestRect, dx, dy),
            },
            DrawTiledImageItem ti => new DrawTiledImageItem
            {
                Bounds = ob,
                ImageHandle = ti.ImageHandle,
                SourceRect = ti.SourceRect,
                FillRect = OffsetRect(ti.FillRect, dx, dy),
                PositioningArea = OffsetRect(ti.PositioningArea, dx, dy),
                TileOrigin = new PointF(ti.TileOrigin.X + dx, ti.TileOrigin.Y + dy),
                Repeat = ti.Repeat,
                TileWidth = ti.TileWidth,
                TileHeight = ti.TileHeight,
            },
            DrawTiledGradientItem tg => new DrawTiledGradientItem
            {
                Bounds = ob,
                GradientFunction = tg.GradientFunction,
                TileWidth = tg.TileWidth,
                TileHeight = tg.TileHeight,
                FillRect = OffsetRect(tg.FillRect, dx, dy),
                TileOrigin = new PointF(tg.TileOrigin.X + dx, tg.TileOrigin.Y + dy),
                Repeat = tg.Repeat,
                Stops = tg.Stops,
                Angle = tg.Angle,
                InterpolationSpace = tg.InterpolationSpace,
                IsRadial = tg.IsRadial,
                IsConic = tg.IsConic,
                CenterX = tg.CenterX,
                CenterY = tg.CenterY,
                FromAngle = tg.FromAngle,
            },
            ClipItem c => new ClipItem
            {
                Bounds = ob,
                ClipRect = OffsetRect(c.ClipRect, dx, dy),
                CornerNw = c.CornerNw,
                CornerNwY = c.CornerNwY,
                CornerNe = c.CornerNe,
                CornerNeY = c.CornerNeY,
                CornerSe = c.CornerSe,
                CornerSeY = c.CornerSeY,
                CornerSw = c.CornerSw,
                CornerSwY = c.CornerSwY,
            },
            RestoreItem => new RestoreItem { Bounds = ob },
            OpacityItem o => new OpacityItem { Bounds = ob, Opacity = o.Opacity },
            RestoreOpacityItem => new RestoreOpacityItem { Bounds = ob },
            BlendModeItem bm => new BlendModeItem { Bounds = ob, Mode = bm.Mode },
            RestoreBlendModeItem => new RestoreBlendModeItem { Bounds = ob },
            TransformItem t => new TransformItem
            {
                Bounds = ob,
                Matrix = t.Matrix,
                OriginX = t.OriginX + dx,
                OriginY = t.OriginY + dy,
            },
            RestoreTransformItem => new RestoreTransformItem { Bounds = ob },
            DrawLineItem l => new DrawLineItem
            {
                Bounds = ob,
                Start = new PointF(l.Start.X + dx, l.Start.Y + dy),
                End = new PointF(l.End.X + dx, l.End.Y + dy),
                Color = l.Color,
                Width = l.Width,
                DashStyle = l.DashStyle,
            },
            _ => item,
        };
    }

    internal static RectangleF OffsetRect(RectangleF r, float dx, float dy)
        => new(r.X + dx, r.Y + dy, r.Width, r.Height);
}
