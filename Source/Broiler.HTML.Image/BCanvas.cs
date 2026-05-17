using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.HTML.Image;

internal sealed class BCanvas : IDisposable
{
    private readonly BBitmap _rootBitmap;
    private readonly Stack<CanvasState> _stateStack = new();
    private readonly Stack<LayerState> _layerStack = new();
    private readonly List<ClipOperation> _clipOperations = [];
    private PointF _translation;

    public BCanvas(BBitmap bitmap)
    {
        _rootBitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    public void Save() => _stateStack.Push(new CanvasState(_translation, _clipOperations.Count));

    public void Restore()
    {
        if (_stateStack.Count == 0)
            return;

        var state = _stateStack.Pop();
        _translation = state.Translation;

        while (_clipOperations.Count > state.ClipOperationCount)
            _clipOperations.RemoveAt(_clipOperations.Count - 1);
    }

    public void Translate(float dx, float dy) =>
        _translation = new PointF(_translation.X + dx, _translation.Y + dy);

    public void Clear(BColor color)
    {
        CurrentTarget.ErasePixels(color);
    }

    public void PushClip(RectangleF rect) => _clipOperations.Add(ClipOperation.Include(Translate(rect)));

    public void PushClipExclude(RectangleF rect) => _clipOperations.Add(ClipOperation.Exclude(Translate(rect)));

    public void PushClipRounded(
        RectangleF rect,
        double cornerNw,
        double cornerNwY,
        double cornerNe,
        double cornerNeY,
        double cornerSe,
        double cornerSeY,
        double cornerSw,
        double cornerSwY) =>
        _clipOperations.Add(ClipOperation.IncludeRounded(
            Translate(rect),
            (float)cornerNw, (float)cornerNwY,
            (float)cornerNe, (float)cornerNeY,
            (float)cornerSe, (float)cornerSeY,
            (float)cornerSw, (float)cornerSwY));

    public void PopClip()
    {
        if (_clipOperations.Count > 0)
            _clipOperations.RemoveAt(_clipOperations.Count - 1);
    }

    public void FillRect(RectangleF rect, BColor color)
    {
        var translated = Translate(rect);
        int minX = Math.Max(0, (int)Math.Floor(translated.Left));
        int minY = Math.Max(0, (int)Math.Floor(translated.Top));
        int maxX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(translated.Right) - 1);
        int maxY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(translated.Bottom) - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                BlendPixel(CurrentTarget, x, y, color, blendMode: "normal");
            }
        }
    }

    public void DrawLine(PointF start, PointF end, BColor color, float strokeWidth = 1f)
    {
        var p1 = Translate(start);
        var p2 = Translate(end);
        float radius = Math.Max(0.5f, strokeWidth / 2f);

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(p1.X, p2.X) - radius));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(p1.Y, p2.Y) - radius));
        int maxX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(Math.Max(p1.X, p2.X) + radius));
        int maxY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(Math.Max(p1.Y, p2.Y) + radius));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                float distance = DistanceToSegment(x + 0.5f, y + 0.5f, p1, p2);
                if (distance <= radius)
                    BlendPixel(CurrentTarget, x, y, color, blendMode: "normal");
            }
        }
    }

    public void DrawRectangleStroke(RectangleF rect, BColor color, float strokeWidth = 1f)
    {
        strokeWidth = Math.Max(1f, strokeWidth);
        FillRect(new RectangleF(rect.X, rect.Y, rect.Width, strokeWidth), color);
        FillRect(new RectangleF(rect.X, rect.Bottom - strokeWidth, rect.Width, strokeWidth), color);
        FillRect(new RectangleF(rect.X, rect.Y, strokeWidth, rect.Height), color);
        FillRect(new RectangleF(rect.Right - strokeWidth, rect.Y, strokeWidth, rect.Height), color);
    }

    public void FillPolygon(PointF[] points, BColor color)
    {
        if (points == null || points.Length < 3)
            return;

        var translated = new PointF[points.Length];
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < points.Length; i++)
        {
            var point = Translate(points[i]);
            translated[i] = point;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        int startX = Math.Max(0, (int)Math.Floor(minX));
        int startY = Math.Max(0, (int)Math.Floor(minY));
        int endX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(maxX));
        int endY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(maxY));

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                if (ContainsPolygonPoint(translated, x + 0.5f, y + 0.5f))
                    BlendPixel(CurrentTarget, x, y, color, blendMode: "normal");
            }
        }
    }

    public void DrawBitmap(BBitmap source, RectangleF destRect, RectangleF srcRect)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (destRect.Width <= 0 || destRect.Height <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0)
            return;

        var translatedDest = Translate(destRect);
        int startX = Math.Max(0, (int)Math.Floor(translatedDest.Left));
        int startY = Math.Max(0, (int)Math.Floor(translatedDest.Top));
        int endX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(translatedDest.Right) - 1);
        int endY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(translatedDest.Bottom) - 1);

        if (startX > endX || startY > endY)
            return;

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                float normalizedX = ((x + 0.5f) - translatedDest.Left) / translatedDest.Width;
                float normalizedY = ((y + 0.5f) - translatedDest.Top) / translatedDest.Height;
                if (normalizedX < 0f || normalizedX >= 1f || normalizedY < 0f || normalizedY >= 1f)
                    continue;

                int srcX = Math.Clamp((int)Math.Floor(srcRect.Left + (normalizedX * srcRect.Width)), 0, source.Width - 1);
                int srcY = Math.Clamp((int)Math.Floor(srcRect.Top + (normalizedY * srcRect.Height)), 0, source.Height - 1);
                BlendPixel(CurrentTarget, x, y, source.GetPixel(srcX, srcY), blendMode: "normal");
            }
        }
    }

    public void DrawPathStroke(IReadOnlyList<PointF> points, BColor color, float strokeWidth = 1f)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 1; i < points.Count; i++)
            DrawLine(points[i - 1], points[i], color, strokeWidth);
    }

    public void FillRectTiled(BBitmap source, RectangleF destRect, RectangleF srcRect, PointF tileOrigin)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (destRect.Width <= 0 || destRect.Height <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0)
            return;

        var translatedDest = Translate(destRect);
        var translatedOrigin = Translate(tileOrigin);
        int minX = Math.Max(0, (int)Math.Floor(translatedDest.Left));
        int minY = Math.Max(0, (int)Math.Floor(translatedDest.Top));
        int maxX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(translatedDest.Right) - 1);
        int maxY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(translatedDest.Bottom) - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                float sampleX = x + 0.5f;
                float sampleY = y + 0.5f;
                int srcX = Math.Clamp(
                    (int)Math.Floor(srcRect.Left + PositiveModulo(sampleX - translatedOrigin.X, srcRect.Width)),
                    0,
                    source.Width - 1);
                int srcY = Math.Clamp(
                    (int)Math.Floor(srcRect.Top + PositiveModulo(sampleY - translatedOrigin.Y, srcRect.Height)),
                    0,
                    source.Height - 1);
                BlendPixel(CurrentTarget, x, y, source.GetPixel(srcX, srcY), blendMode: "normal");
            }
        }
    }

    public void FillLinearGradientRect(RectangleF rect, IReadOnlyList<BColor> colors, IReadOnlyList<float>? positions, float angle)
    {
        if (colors == null || colors.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (colors.Count == 1)
        {
            FillRect(rect, colors[0]);
            return;
        }

        var translatedRect = Translate(rect);
        int minX = Math.Max(0, (int)Math.Floor(translatedRect.Left));
        int minY = Math.Max(0, (int)Math.Floor(translatedRect.Top));
        int maxX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(translatedRect.Right) - 1);
        int maxY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(translatedRect.Bottom) - 1);
        var normalizedPositions = NormalizeGradientPositions(colors.Count, positions);
        var (startPoint, endPoint) = GetGradientEndpoints(translatedRect, angle);
        float gradientX = endPoint.X - startPoint.X;
        float gradientY = endPoint.Y - startPoint.Y;
        float gradientLengthSquared = (gradientX * gradientX) + (gradientY * gradientY);

        if (gradientLengthSquared <= 0f)
        {
            FillRect(rect, colors[^1]);
            return;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                float sampleX = x + 0.5f;
                float sampleY = y + 0.5f;
                float t = (((sampleX - startPoint.X) * gradientX) + ((sampleY - startPoint.Y) * gradientY)) / gradientLengthSquared;
                var color = SampleGradientColor(colors, normalizedPositions, Math.Clamp(t, 0f, 1f));
                BlendPixel(CurrentTarget, x, y, color, blendMode: "normal");
            }
        }
    }

    /// <summary>
    /// Fills a rectangle with a radial gradient.  The gradient centre is given as
    /// normalised fractions of the rectangle dimensions (<paramref name="centerX"/>
    /// and <paramref name="centerY"/> are in the range 0.0–1.0).  The gradient
    /// radius extends to the farthest corner of the rectangle from the centre,
    /// matching the CSS <c>farthest-corner</c> keyword behaviour.
    /// </summary>
    public void FillRadialGradientRect(RectangleF rect, IReadOnlyList<BColor> colors, IReadOnlyList<float>? positions, float centerX, float centerY)
    {
        if (colors == null || colors.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (colors.Count == 1)
        {
            FillRect(rect, colors[0]);
            return;
        }

        var translatedRect = Translate(rect);
        int minX = Math.Max(0, (int)Math.Floor(translatedRect.Left));
        int minY = Math.Max(0, (int)Math.Floor(translatedRect.Top));
        int maxX = Math.Min(CurrentTarget.Width - 1, (int)Math.Ceiling(translatedRect.Right) - 1);
        int maxY = Math.Min(CurrentTarget.Height - 1, (int)Math.Ceiling(translatedRect.Bottom) - 1);

        var normalizedPositions = NormalizeGradientPositions(colors.Count, positions);

        float cx = translatedRect.Left + (centerX * translatedRect.Width);
        float cy = translatedRect.Top + (centerY * translatedRect.Height);

        // Radii to farthest corner (elliptical, one per axis).
        float rx = Math.Max(Math.Abs(cx - translatedRect.Left), Math.Abs(cx - translatedRect.Right));
        float ry = Math.Max(Math.Abs(cy - translatedRect.Top), Math.Abs(cy - translatedRect.Bottom));

        if (rx <= 0 || ry <= 0)
            return;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!IsVisible(x, y))
                    continue;

                float dx = (x + 0.5f - cx) / rx;
                float dy = (y + 0.5f - cy) / ry;
                float t = Math.Clamp((float)Math.Sqrt((dx * dx) + (dy * dy)), 0f, 1f);
                var color = SampleGradientColor(colors, normalizedPositions, t);
                BlendPixel(CurrentTarget, x, y, color, blendMode: "normal");
            }
        }
    }

    public void SaveOpacityLayer(float opacity)
    {
        _layerStack.Push(new LayerState(new BBitmap(_rootBitmap.Width, _rootBitmap.Height), opacity, "normal"));
    }

    public void RestoreOpacityLayer()
    {
        if (_layerStack.Count == 0)
            return;

        var layer = _layerStack.Pop();
        CompositeLayer(layer);
    }

    public void SaveBlendLayer(string blendMode)
    {
        _layerStack.Push(new LayerState(new BBitmap(_rootBitmap.Width, _rootBitmap.Height), 1f, blendMode ?? "normal"));
    }

    public void RestoreBlendLayer()
    {
        if (_layerStack.Count == 0)
            return;

        var layer = _layerStack.Pop();
        CompositeLayer(layer);
    }

    public void Dispose()
    {
        while (_layerStack.Count > 0)
            _layerStack.Pop().Bitmap.Dispose();
    }

    private BBitmap CurrentTarget => _layerStack.Count > 0 ? _layerStack.Peek().Bitmap : _rootBitmap;

    private RectangleF Translate(RectangleF rect) =>
        new(rect.X + _translation.X, rect.Y + _translation.Y, rect.Width, rect.Height);

    private PointF Translate(PointF point) =>
        new(point.X + _translation.X, point.Y + _translation.Y);

    private bool IsVisible(int x, int y)
    {
        float sampleX = x + 0.5f;
        float sampleY = y + 0.5f;

        foreach (var operation in _clipOperations)
        {
            bool contains = operation.Contains(sampleX, sampleY);
            if (operation.IsExclude)
            {
                if (contains)
                    return false;
            }
            else if (!contains)
            {
                return false;
            }
        }

        return true;
    }

    private void CompositeLayer(LayerState layer)
    {
        var destination = CurrentTarget;
        for (int y = 0; y < destination.Height; y++)
        {
            for (int x = 0; x < destination.Width; x++)
            {
                var source = layer.Bitmap.GetPixel(x, y);
                if (source.A == 0)
                    continue;

                if (layer.Opacity < 1f)
                    source = ApplyOpacity(source, layer.Opacity);

                BlendPixel(destination, x, y, source, layer.BlendMode);
            }
        }

        layer.Bitmap.Dispose();
    }

    private static BColor ApplyOpacity(BColor color, float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return color with { A = alpha };
    }

    private static void BlendPixel(BBitmap bitmap, int x, int y, BColor source, string blendMode)
    {
        if (source.A == 0)
            return;

        var destination = bitmap.GetPixel(x, y);
        var blendedSource = ApplyBlendMode(source, destination, blendMode);
        bitmap.SetPixel(x, y, CompositeSourceOver(blendedSource, destination));
    }

    private static BColor ApplyBlendMode(BColor source, BColor destination, string blendMode)
    {
        if (string.Equals(blendMode, "multiply", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                // +127 is the integer equivalent of adding 0.5 before dividing by 255.
                (byte)((source.R * destination.R + 127) / 255),
                (byte)((source.G * destination.G + 127) / 255),
                (byte)((source.B * destination.B + 127) / 255),
                source.A);
        }

        if (string.Equals(blendMode, "screen", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                (byte)(255 - (((255 - source.R) * (255 - destination.R) + 127) / 255)),
                (byte)(255 - (((255 - source.G) * (255 - destination.G) + 127) / 255)),
                (byte)(255 - (((255 - source.B) * (255 - destination.B) + 127) / 255)),
                source.A);
        }

        if (string.Equals(blendMode, "darken", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                Math.Min(source.R, destination.R),
                Math.Min(source.G, destination.G),
                Math.Min(source.B, destination.B),
                source.A);
        }

        if (string.Equals(blendMode, "lighten", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                Math.Max(source.R, destination.R),
                Math.Max(source.G, destination.G),
                Math.Max(source.B, destination.B),
                source.A);
        }

        if (string.Equals(blendMode, "overlay", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                OverlayChannel(source.R, destination.R),
                OverlayChannel(source.G, destination.G),
                OverlayChannel(source.B, destination.B),
                source.A);
        }

        if (string.Equals(blendMode, "difference", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                (byte)Math.Abs(source.R - destination.R),
                (byte)Math.Abs(source.G - destination.G),
                (byte)Math.Abs(source.B - destination.B),
                source.A);
        }

        if (string.Equals(blendMode, "plus-lighter", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                AdditiveClampChannel(source.R, destination.R),
                AdditiveClampChannel(source.G, destination.G),
                AdditiveClampChannel(source.B, destination.B),
                source.A);
        }

        return source;
    }

    private static float[] NormalizeGradientPositions(int colorCount, IReadOnlyList<float>? positions)
    {
        var normalized = new float[colorCount];
        if (positions == null || positions.Count != colorCount)
        {
            if (colorCount == 1)
            {
                normalized[0] = 0f;
                return normalized;
            }

            for (int i = 0; i < colorCount; i++)
                normalized[i] = (float)i / (colorCount - 1);

            return normalized;
        }

        normalized[0] = Math.Clamp(positions[0], 0f, 1f);
        for (int i = 1; i < colorCount; i++)
            normalized[i] = Math.Max(normalized[i - 1], Math.Clamp(positions[i], 0f, 1f));

        return normalized;
    }

    private static (PointF StartPoint, PointF EndPoint) GetGradientEndpoints(RectangleF rect, float angle)
    {
        var radians = angle * Math.PI / 180.0;
        float cx = rect.X + (rect.Width / 2f);
        float cy = rect.Y + (rect.Height / 2f);
        float halfDiag = Math.Max(rect.Width, rect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        return (
            new PointF(cx - (sin * halfDiag), cy + (cos * halfDiag)),
            new PointF(cx + (sin * halfDiag), cy - (cos * halfDiag)));
    }

    private static BColor SampleGradientColor(IReadOnlyList<BColor> colors, IReadOnlyList<float> positions, float t)
    {
        if (t <= positions[0])
            return colors[0];

        for (int i = 1; i < colors.Count; i++)
        {
            if (t > positions[i])
                continue;

            float start = positions[i - 1];
            float end = positions[i];
            if (end <= start)
                return colors[i];

            float localT = (t - start) / (end - start);
            return Lerp(colors[i - 1], colors[i], localT);
        }

        return colors[^1];
    }

    private static BColor Lerp(BColor start, BColor end, float t) =>
        new(
            LerpChannel(start.R, end.R, t),
            LerpChannel(start.G, end.G, t),
            LerpChannel(start.B, end.B, t),
            LerpChannel(start.A, end.A, t));

    private static byte LerpChannel(byte start, byte end, float t) =>
        (byte)Math.Clamp((int)Math.Round(start + ((end - start) * t)), 0, 255);

    private static float PositiveModulo(float value, float modulus)
    {
        float result = value % modulus;
        if (result < 0)
            result += modulus;
        return result;
    }

    private static BColor CompositeSourceOver(BColor source, BColor destination)
    {
        float srcA = source.A / 255f;
        float dstA = destination.A / 255f;
        float outA = srcA + dstA * (1f - srcA);

        if (outA <= 0f)
            return BColor.Transparent;

        byte r = CompositeChannel(source.R, destination.R, srcA, dstA, outA);
        byte g = CompositeChannel(source.G, destination.G, srcA, dstA, outA);
        byte b = CompositeChannel(source.B, destination.B, srcA, dstA, outA);
        byte a = (byte)Math.Clamp((int)Math.Round(outA * 255f), 0, 255);

        return new BColor(r, g, b, a);
    }

    private static byte CompositeChannel(byte source, byte destination, float srcA, float dstA, float outA)
    {
        float value = (source * srcA + destination * dstA * (1f - srcA)) / outA;
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static byte OverlayChannel(byte source, byte destination)
    {
        if (destination < 128)
            return (byte)Math.Clamp((2 * source * destination + 127) / 255, 0, 255);

        return (byte)Math.Clamp(
            255 - ((2 * (255 - source) * (255 - destination) + 127) / 255),
            0,
            255);
    }

    private static byte AdditiveClampChannel(byte source, byte destination) =>
        (byte)Math.Min(255, source + destination);

    private static float DistanceToSegment(float px, float py, PointF start, PointF end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;

        if (Math.Abs(dx) < float.Epsilon && Math.Abs(dy) < float.Epsilon)
            return Distance(px, py, start.X, start.Y);

        float t = ((px - start.X) * dx + (py - start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0f, 1f);

        float nearestX = start.X + t * dx;
        float nearestY = start.Y + t * dy;
        return Distance(px, py, nearestX, nearestY);
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ContainsPolygonPoint(PointF[] polygon, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            bool intersects = ((pi.Y > y) != (pj.Y > y))
                              && (x < (pj.X - pi.X) * (y - pi.Y) / ((pj.Y - pi.Y) + float.Epsilon) + pi.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private readonly record struct CanvasState(PointF Translation, int ClipOperationCount);

    private sealed record LayerState(BBitmap Bitmap, float Opacity, string BlendMode);

    private readonly record struct ClipOperation(
        RectangleF Rect,
        bool IsExclude,
        bool IsRounded,
        float CornerNw,
        float CornerNwY,
        float CornerNe,
        float CornerNeY,
        float CornerSe,
        float CornerSeY,
        float CornerSw,
        float CornerSwY)
    {
        public static ClipOperation Include(RectangleF rect) => new(rect, false, false, 0, 0, 0, 0, 0, 0, 0, 0);

        public static ClipOperation Exclude(RectangleF rect) => new(rect, true, false, 0, 0, 0, 0, 0, 0, 0, 0);

        public static ClipOperation IncludeRounded(
            RectangleF rect,
            float cornerNw,
            float cornerNwY,
            float cornerNe,
            float cornerNeY,
            float cornerSe,
            float cornerSeY,
            float cornerSw,
            float cornerSwY) =>
            new(rect, false, true, cornerNw, cornerNwY, cornerNe, cornerNeY, cornerSe, cornerSeY, cornerSw, cornerSwY);

        public bool Contains(float x, float y)
        {
            if (!Rect.Contains(x, y))
                return false;

            if (!IsRounded)
                return true;

            return ContainsRounded(x, y);
        }

        private bool ContainsRounded(float x, float y)
        {
            float left = Rect.Left;
            float right = Rect.Right;
            float top = Rect.Top;
            float bottom = Rect.Bottom;

            if (x >= left + CornerNw && x <= right - CornerNe)
                return true;

            if (x >= left + CornerSw && x <= right - CornerSe)
                return true;

            if (y >= top + CornerNwY && y <= bottom - CornerSwY)
                return true;

            if (y >= top + CornerNeY && y <= bottom - CornerSeY)
                return true;

            if (CornerNw > 0 && CornerNwY > 0 && InEllipse(x, y, left + CornerNw, top + CornerNwY, CornerNw, CornerNwY))
                return true;

            if (CornerNe > 0 && CornerNeY > 0 && InEllipse(x, y, right - CornerNe, top + CornerNeY, CornerNe, CornerNeY))
                return true;

            if (CornerSe > 0 && CornerSeY > 0 && InEllipse(x, y, right - CornerSe, bottom - CornerSeY, CornerSe, CornerSeY))
                return true;

            if (CornerSw > 0 && CornerSwY > 0 && InEllipse(x, y, left + CornerSw, bottom - CornerSwY, CornerSw, CornerSwY))
                return true;

            return false;
        }

        private static bool InEllipse(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
        {
            float dx = (x - centerX) / radiusX;
            float dy = (y - centerY) / radiusY;
            return dx * dx + dy * dy <= 1f;
        }
    }
}
