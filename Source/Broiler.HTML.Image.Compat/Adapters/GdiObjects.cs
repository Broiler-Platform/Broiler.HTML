using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Image.Adapters;

/// <summary>
/// A GDI+ drawing surface wrapper exposing the parameterless
/// <c>Save()</c>/<c>Restore()</c>/<c>Translate()</c> shape that the
/// backend-neutral canvas operations expect (System.Drawing.Graphics uses a
/// <see cref="GraphicsState"/> token for save/restore instead).
/// </summary>
internal sealed class GdiCanvas : IDisposable
{
    private readonly Stack<GraphicsState> _states = new();

    public GdiCanvas(Graphics graphics)
    {
        Graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Graphics.InterpolationMode = InterpolationMode.Bilinear;
        Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
    }

    public Graphics Graphics { get; }

    public void Save() => _states.Push(Graphics.Save());

    public void Restore()
    {
        if (_states.Count > 0)
            Graphics.Restore(_states.Pop());
    }

    public void Translate(float dx, float dy) => Graphics.TranslateTransform(dx, dy);

    public void Dispose() => Graphics.Dispose();
}

/// <summary>
/// A GDI+ "paint": either a fill <see cref="System.Drawing.Brush"/> or a stroke
/// <see cref="System.Drawing.Pen"/>, mirroring the single backend paint object the
/// compatibility interfaces pass around.
/// </summary>
internal sealed class GdiPaint : IDisposable
{
    public Brush? Brush { get; set; }

    public Pen? Pen { get; set; }

    public bool IsPen => Pen is not null;

    public void Dispose()
    {
        Brush?.Dispose();
        Pen?.Dispose();
    }
}

/// <summary>A resolved font family + style pair used to build GDI+ fonts.</summary>
internal sealed record GdiTypeface(FontFamily Family, FontStyle Style);

/// <summary>
/// A GDI+ <see cref="GraphicsPath"/> together with the current pen position,
/// because GraphicsPath has no implicit move-to cursor of its own.
/// </summary>
internal sealed class GdiPathState : IDisposable
{
    public GraphicsPath Path { get; } = new();

    public PointF Current { get; set; }

    public bool HasCurrent { get; set; }

    public void Dispose() => Path.Dispose();
}
