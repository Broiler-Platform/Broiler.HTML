using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GraphicsAdapter : RGraphics
{
    private readonly Func<object> _canvasFactory;
    private readonly BCanvas? _rasterCanvas;
    private readonly bool _disposeCanvas;
    private readonly bool _restoreOnDispose;
    private readonly Action? _onDispose;
    private readonly List<Action<object>> _deferredCanvasOperations = [];
    private readonly Stack<bool> _rasterLayerStack = new();
    private readonly ITextShaper _textShaper;
    private readonly ICanvasCompat _canvasCompat;
    private object? _canvas;
    private int _activeCompatLayerDepth;
    private bool _nextLayerCanUseRaster;

    public GraphicsAdapter(
        Func<object> canvasFactory,
        RectangleF initialClip,
        BCanvas? rasterCanvas = null,
        bool disposeCanvas = false,
        bool restoreOnDispose = false,
        Action? onDispose = null,
        ITextShaper? textShaper = null,
        ICanvasCompat? canvasCompat = null,
        Action<object, object?>? initialCanvasOperation = null,
        object? initialCanvasOperationState = null)
        : base(CompatProvider.ImageAdapter, initialClip)
    {
        _canvasFactory = canvasFactory ?? throw new ArgumentNullException(nameof(canvasFactory));
        _rasterCanvas = rasterCanvas;
        _disposeCanvas = disposeCanvas;
        _restoreOnDispose = restoreOnDispose;
        _onDispose = onDispose;
        _textShaper = textShaper ?? CompatProvider.TextShaper;
        _canvasCompat = canvasCompat ?? CompatProvider.CanvasCompat;
        if (initialCanvasOperation is not null)
            _deferredCanvasOperations.Add(canvas => initialCanvasOperation(canvas, initialCanvasOperationState));
    }

    internal bool HasMaterializedCanvas => _canvas is not null;

    public override void PopClip()
    {
        ApplyCanvasOperation(CompatCanvasOperations.Restore);
        _rasterCanvas?.PopClip();
        _clipStack.Pop();
    }

    public override void PushClip(RectangleF rect)
    {
        _clipStack.Push(rect);
        ApplyCanvasOperation(canvas =>
        {
            CompatCanvasOperations.Save(canvas);
            _canvasCompat.PushClip(canvas, rect);
        });
        _rasterCanvas?.PushClip(rect);
    }

    public override void PushClipExclude(RectangleF rect)
    {
        _clipStack.Push(_clipStack.Peek());
        ApplyCanvasOperation(canvas =>
        {
            CompatCanvasOperations.Save(canvas);
            _canvasCompat.PushClipExclude(canvas, rect);
        });
        _rasterCanvas?.PushClipExclude(rect);
    }

    public override void PushClipRounded(RectangleF rect,
        double cornerNw, double cornerNwY,
        double cornerNe, double cornerNeY,
        double cornerSe, double cornerSeY,
        double cornerSw, double cornerSwY)
    {
        _clipStack.Push(rect);
        ApplyCanvasOperation(canvas =>
        {
            CompatCanvasOperations.Save(canvas);
            _canvasCompat.ClipRounded(
                canvas,
                rect,
                cornerNw, cornerNwY,
                cornerNe, cornerNeY,
                cornerSe, cornerSeY,
                cornerSw, cornerSwY);
        });

        _rasterCanvas?.PushClipRounded(
            rect,
            cornerNw, cornerNwY,
            cornerNe, cornerNeY,
            cornerSe, cornerSeY,
            cornerSw, cornerSwY);
    }

    public override object SetAntiAliasSmoothingMode() => null;

    public override void ReturnPreviousSmoothingMode(object prevMode)
    {
    }

    public override SizeF MeasureString(string str, RFont font) =>
        _textShaper.MeasureString((FontAdapter)font, str);

    public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth) =>
        _textShaper.MeasureString((FontAdapter)font, str, maxWidth, out charFit, out charFitWidth);

    public override void DrawString(string str, RFont font, Color color, PointF point, SizeF size, bool rtl)
    {
        float glyphRotation = VerticalGlyphContext.RotationDeg;
        if (CanUseRaster && _textShaper.TryDrawString(_rasterCanvas!, (FontAdapter)font, str, color, point, glyphRotation))
            return;

        var canvas = EnsureCanvas();
        _textShaper.DrawString(canvas, (FontAdapter)font, str, color, point);
    }

    public override void DrawGradientString(string str, RFont font, RectangleF rect, PointF point, SizeF size, bool rtl, Color[] colors, float[] positions, float angle)
    {
        if (colors == null || colors.Length == 0)
            return;

        if (CanUseRaster && _textShaper.TryDrawGradientString(_rasterCanvas!, (FontAdapter)font, str, rect, point, size, colors, positions, angle))
            return;

        var canvas = EnsureCanvas();
        _textShaper.DrawGradientString(canvas, (FontAdapter)font, str, rect, point, size, colors, positions, angle);
    }

    public override RBrush GetTextureBrush(RImage image, RectangleF dstRect, PointF translateTransformLocation)
    {
        var imgAdapter = (ImageAdapter)image;
        return new BrushAdapter(
            () => _canvasCompat.CreateTexturePaint(imgAdapter.Bitmap, translateTransformLocation),
            dispose: true)
        {
            TextureBitmap = imgAdapter.Bitmap,
            TextureSourceRect = dstRect,
            TextureOrigin = translateTransformLocation,
        };
    }

    public override RGraphicsPath GetGraphicsPath() => new GraphicsPathAdapter();

    public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
    {
        var penAdapter = (PenAdapter)pen;
        if (CanUseRaster && penAdapter.HasSimpleStroke)
        {
            _rasterCanvas!.DrawLine(new PointF((float)x1, (float)y1), new PointF((float)x2, (float)y2), penAdapter.SolidColor!.Value, (float)pen.Width);
            return;
        }

        _canvasCompat.DrawLine(EnsureCanvas(), (float)x1, (float)y1, (float)x2, (float)y2, penAdapter.Paint);
    }

    public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
    {
        var penAdapter = (PenAdapter)pen;
        if (CanUseRaster && penAdapter.HasSimpleStroke)
        {
            _rasterCanvas!.DrawRectangleStroke(new RectangleF((float)x, (float)y, (float)width, (float)height), penAdapter.SolidColor!.Value, (float)pen.Width);
            return;
        }

        _canvasCompat.DrawRectangle(EnsureCanvas(), new RectangleF((float)x, (float)y, (float)width, (float)height), penAdapter.Paint);
    }

    public override void DrawRectangle(RBrush brush, double x, double y, double width, double height)
    {
        var brushAdapter = (BrushAdapter)brush;
        if (CanUseRaster
            && brushAdapter.TextureBitmap is BBitmap textureBitmap
            && brushAdapter.TextureSourceRect is RectangleF textureSourceRect
            && brushAdapter.TextureOrigin is PointF textureOrigin)
        {
            _rasterCanvas!.FillRectTiled(
                textureBitmap,
                new RectangleF((float)x, (float)y, (float)width, (float)height),
                textureSourceRect,
                textureOrigin);
            return;
        }

        if (CanUseRaster && brushAdapter.SolidColor is BColor solidColor)
        {
            _rasterCanvas!.FillRect(new RectangleF((float)x, (float)y, (float)width, (float)height), solidColor);
            return;
        }

        _canvasCompat.DrawRectangle(EnsureCanvas(), new RectangleF((float)x, (float)y, (float)width, (float)height), brushAdapter.Paint);
    }

    public override void DrawImage(RImage image, RectangleF destRect, RectangleF srcRect)
    {
        var imgAdapter = (ImageAdapter)image;
        if (CanUseRaster)
        {
            _rasterCanvas!.DrawBitmap(imgAdapter.Bitmap, destRect, srcRect);
            return;
        }

        _canvasCompat.DrawImage(EnsureCanvas(), imgAdapter.Bitmap, destRect, srcRect);
    }

    public override void DrawImage(RImage image, RectangleF destRect)
    {
        var imgAdapter = (ImageAdapter)image;
        if (CanUseRaster)
        {
            _rasterCanvas!.DrawBitmap(
                imgAdapter.Bitmap,
                destRect,
                new RectangleF(0, 0, imgAdapter.Bitmap.Width, imgAdapter.Bitmap.Height));
            return;
        }

        _canvasCompat.DrawImage(EnsureCanvas(), imgAdapter.Bitmap, destRect);
    }

    public override void DrawPath(RPen pen, RGraphicsPath path)
    {
        var penAdapter = (PenAdapter)pen;
        var pathAdapter = (GraphicsPathAdapter)path;
        if (CanUseRaster && penAdapter.HasSimpleStroke && pathAdapter.FlattenedPoints.Count > 1)
        {
            _rasterCanvas!.DrawPathStroke(pathAdapter.FlattenedPoints, penAdapter.SolidColor!.Value, (float)pen.Width);
            return;
        }

        _canvasCompat.DrawPath(EnsureCanvas(), pathAdapter, penAdapter.Paint);
    }

    public override void DrawPath(RBrush brush, RGraphicsPath path)
    {
        var brushAdapter = (BrushAdapter)brush;
        var pathAdapter = (GraphicsPathAdapter)path;
        if (CanUseRaster && brushAdapter.SolidColor is BColor solidColor && pathAdapter.FlattenedPoints.Count > 2)
        {
            _rasterCanvas!.FillPolygon([.. pathAdapter.FlattenedPoints], solidColor);
            return;
        }

        _canvasCompat.DrawPath(EnsureCanvas(), pathAdapter, brushAdapter.Paint);
    }

    public override void DrawPolygon(RBrush brush, PointF[] points)
    {
        if (points == null || points.Length == 0)
            return;

        var brushAdapter = (BrushAdapter)brush;
        if (CanUseRaster && brushAdapter.SolidColor is BColor solidColor)
        {
            _rasterCanvas!.FillPolygon(points, solidColor);
            return;
        }

        _canvasCompat.DrawPolygon(EnsureCanvas(), points, brushAdapter.Paint);
    }

    public override void HintNextLayerCanUseRaster(bool canUseRaster) =>
        _nextLayerCanUseRaster = canUseRaster;

    public override void SaveOpacityLayer(float opacity)
    {
        bool useRaster = _rasterCanvas is not null && _activeCompatLayerDepth == 0 && _nextLayerCanUseRaster;
        _nextLayerCanUseRaster = false;
        _rasterLayerStack.Push(useRaster);
        if (useRaster)
        {
            _rasterCanvas!.SaveOpacityLayer(opacity);
            return;
        }

        _activeCompatLayerDepth++;
        ApplyCanvasOperation(canvas => _canvasCompat.SaveOpacityLayer(canvas, opacity));
    }

    public override void RestoreOpacityLayer()
    {
        bool usedRaster = _rasterLayerStack.Count > 0 && _rasterLayerStack.Pop();
        if (usedRaster)
        {
            _rasterCanvas!.RestoreOpacityLayer();
            return;
        }

        ApplyCanvasOperation(CompatCanvasOperations.Restore);
        _activeCompatLayerDepth = Math.Max(0, _activeCompatLayerDepth - 1);
    }

    public override void SaveBlendLayer(string blendMode)
    {
        bool useRaster = _rasterCanvas is not null
            && _activeCompatLayerDepth == 0
            && _nextLayerCanUseRaster;
        _nextLayerCanUseRaster = false;
        _rasterLayerStack.Push(useRaster);
        if (useRaster)
        {
            _rasterCanvas!.SaveBlendLayer(blendMode);
            return;
        }

        _activeCompatLayerDepth++;
        ApplyCanvasOperation(canvas => _canvasCompat.SaveBlendLayer(canvas, blendMode));
    }

    public override void RestoreBlendLayer()
    {
        bool usedRaster = _rasterLayerStack.Count > 0 && _rasterLayerStack.Pop();
        if (usedRaster)
        {
            _rasterCanvas!.RestoreBlendLayer();
            return;
        }

        ApplyCanvasOperation(CompatCanvasOperations.Restore);
        _activeCompatLayerDepth = Math.Max(0, _activeCompatLayerDepth - 1);
    }

    public override void SaveTransformLayer(float[] matrix, float originX, float originY)
    {
        _activeCompatLayerDepth++;
        ApplyCanvasOperation(canvas => _canvasCompat.SaveTransformLayer(canvas, matrix, originX, originY));
    }

    public override void RestoreTransformLayer()
    {
        ApplyCanvasOperation(CompatCanvasOperations.Restore);
        _activeCompatLayerDepth = Math.Max(0, _activeCompatLayerDepth - 1);
    }

    public override RImage? CreateLinearGradientTile(int width, int height, Color[] colors, float[] positions, float angle)
    {
        if (width <= 0 || height <= 0 || colors == null || colors.Length == 0)
            return null;

        var bitmap = new BBitmap(width, height);
        using var tileCanvas = bitmap.OpenRasterCanvas();
        var gradientColors = new BColor[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            gradientColors[i] = new BColor(colors[i].R, colors[i].G, colors[i].B, colors[i].A);

        tileCanvas.FillLinearGradientRect(new RectangleF(0, 0, width, height), gradientColors, positions, angle);

        return new ImageAdapter(bitmap);
    }

    public override RImage? CreateRadialGradientTile(int width, int height, Color[] colors, float[] positions, float centerX, float centerY)
    {
        if (width <= 0 || height <= 0 || colors == null || colors.Length == 0)
            return null;

        var bitmap = new BBitmap(width, height);
        using var tileCanvas = bitmap.OpenRasterCanvas();
        var gradientColors = new BColor[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            gradientColors[i] = new BColor(colors[i].R, colors[i].G, colors[i].B, colors[i].A);

        tileCanvas.FillRadialGradientRect(new RectangleF(0, 0, width, height), gradientColors, positions, centerX, centerY);

        return new ImageAdapter(bitmap);
    }

    public override RImage? CreateConicGradientTile(int width, int height, Color[] colors, float[] positions, float centerX, float centerY, float fromAngle)
    {
        if (width <= 0 || height <= 0 || colors == null || colors.Length == 0)
            return null;

        var bitmap = new BBitmap(width, height);
        using var tileCanvas = bitmap.OpenRasterCanvas();
        var gradientColors = new BColor[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            gradientColors[i] = new BColor(colors[i].R, colors[i].G, colors[i].B, colors[i].A);

        tileCanvas.FillConicGradientRect(new RectangleF(0, 0, width, height), gradientColors, positions, centerX, centerY, fromAngle);

        return new ImageAdapter(bitmap);
    }

    public override void Dispose()
    {
        if (_restoreOnDispose)
        {
            if (_canvas is not null)
                CompatCanvasOperations.Restore(_canvas);
            _rasterCanvas?.Restore();
        }

        if (_disposeCanvas)
        {
            (_canvas as IDisposable)?.Dispose();
            _rasterCanvas?.Dispose();
        }

        _onDispose?.Invoke();
    }

    private bool CanUseRaster => _rasterCanvas is not null && _activeCompatLayerDepth == 0;

    private object EnsureCanvas()
    {
        if (_canvas is not null)
            return _canvas;

        _canvas = _canvasFactory();
        foreach (var operation in _deferredCanvasOperations)
            operation(_canvas);

        _deferredCanvasOperations.Clear();
        return _canvas;
    }

    private void ApplyCanvasOperation(Action<object> operation)
    {
        if (_canvas is not null)
        {
            operation(_canvas);
            return;
        }

        _deferredCanvasOperations.Add(operation);
    }
}
