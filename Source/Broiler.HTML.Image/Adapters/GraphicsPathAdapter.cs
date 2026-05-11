using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Adapters.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GraphicsPathAdapter : RGraphicsPath
{
    private PointF _lastPoint;
    private readonly List<PointF> _flattenedPoints = [];
    private readonly List<Action<object>> _deferredPathOperations = [];
    private readonly IPathCompat _pathCompat;
    private object? _path;

    internal GraphicsPathAdapter(IPathCompat? pathCompat = null) =>
        _pathCompat = pathCompat ?? SkiaCompatProvider.PathCompat;

    public object Path => EnsurePath();

    public IReadOnlyList<PointF> FlattenedPoints => _flattenedPoints;

    internal bool HasMaterializedPath => _path is not null;

    public override void Start(double x, double y)
    {
        _lastPoint = new PointF((float)x, (float)y);
        _flattenedPoints.Clear();
        _flattenedPoints.Add(_lastPoint);

        _deferredPathOperations.Clear();
        if (_path is not null)
        {
            _pathCompat.Reset(_path);
            _pathCompat.MoveTo(_path, (float)x, (float)y);
            return;
        }

        _deferredPathOperations.Add(path => _pathCompat.MoveTo(path, (float)x, (float)y));
    }

    public override void LineTo(double x, double y)
    {
        if (_path is not null)
            _pathCompat.LineTo(_path, (float)x, (float)y);
        else
            _deferredPathOperations.Add(path => _pathCompat.LineTo(path, (float)x, (float)y));

        _lastPoint = new PointF((float)x, (float)y);
        _flattenedPoints.Add(_lastPoint);
    }

    public override void ArcTo(double x, double y, double size, Corner corner)
    {
        float left = (float)(Math.Min(x, _lastPoint.X) - (corner == Corner.TopRight || corner == Corner.BottomRight ? size : 0));
        float top = (float)(Math.Min(y, _lastPoint.Y) - (corner == Corner.BottomLeft || corner == Corner.BottomRight ? size : 0));
        float width = (float)size * 2;
        float height = (float)size * 2;
        if (_path is not null)
            _pathCompat.ArcTo(_path, left, top, width, height, GetStartAngle(corner), 90, false);
        else
            _deferredPathOperations.Add(path => _pathCompat.ArcTo(path, left, top, width, height, GetStartAngle(corner), 90, false));

        int segmentCount = Math.Max(4, (int)Math.Ceiling(size));
        float centerX = left + (float)size;
        float centerY = top + (float)size;
        float startAngle = GetStartAngle(corner);
        for (int i = 1; i <= segmentCount; i++)
        {
            float angle = startAngle + ((90f * i) / segmentCount);
            float radians = angle * (float)Math.PI / 180f;
            _flattenedPoints.Add(new PointF(
                centerX + ((float)Math.Cos(radians) * (float)size),
                centerY + ((float)Math.Sin(radians) * (float)size)));
        }

        _lastPoint = new PointF((float)x, (float)y);
    }

    public override void Dispose() => (_path as IDisposable)?.Dispose();

    private object EnsurePath()
    {
        if (_path is not null)
            return _path;

        _path = _pathCompat.CreatePath();
        foreach (var operation in _deferredPathOperations)
            operation(_path);

        return _path;
    }

    private static float GetStartAngle(Corner corner)
    {
        return corner switch
        {
            Corner.TopLeft => 180,
            Corner.TopRight => 270,
            Corner.BottomLeft => 90,
            Corner.BottomRight => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };
    }
}
