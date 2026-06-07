using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiTextShaper : ITextShaper
{
    private readonly ITextMetricsCompat _textMetricsCompat;
    private readonly ITextCanvasCompat _textCanvasCompat;

    internal GdiTextShaper(ITextMetricsCompat textMetricsCompat = null, ITextCanvasCompat textCanvasCompat = null)
    {
        _textMetricsCompat = textMetricsCompat ?? GdiTextMetricsCompat.Instance;
        _textCanvasCompat = textCanvasCompat ?? GdiTextCanvasCompat.Instance;
    }

    public static GdiTextShaper Instance { get; } = new();

    public SizeF MeasureString(FontAdapter font, string text) =>
        new(_textMetricsCompat.MeasureTextWidth(font, text), (float)font.Height);

    public void MeasureString(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth) =>
        _textMetricsCompat.MeasureTextWidth(font, text, maxWidth, out charFit, out charFitWidth);

    public bool TryDrawString(BCanvas canvas, FontAdapter font, string text, Color color, PointF point)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        using var textBitmap = RenderText(font, text, (graphics, renderFont, format) =>
        {
            using var brush = new SolidBrush(color);
            graphics.DrawString(text, renderFont, brush, 0, 0, format);
        });
        DrawBitmap(canvas, textBitmap, point);
        return true;
    }

    public bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
    {
        if (colors is null || colors.Length == 0)
            return true;
        if (string.IsNullOrEmpty(text))
            return true;

        using var textBitmap = RenderText(font, text, (graphics, renderFont, format) =>
        {
            if (colors.Length == 1)
            {
                using var solid = new SolidBrush(colors[0]);
                graphics.DrawString(text, renderFont, solid, 0, 0, format);
                return;
            }

            float shaderWidth = Math.Max(rect.Width, GdiTextMetricsCompat.MeasureRaw(renderFont, text));
            float shaderHeight = Math.Max(rect.Height > 0 ? rect.Height : size.Height, (float)font.Size);
            // The glyphs are drawn at the bitmap origin, so shift the gradient
            // rectangle into the bitmap's local coordinate space.
            var shaderRect = new RectangleF(
                rect.X - point.X,
                rect.Y - point.Y,
                Math.Max(1f, shaderWidth),
                Math.Max(1f, shaderHeight));
            using var brush = TextGradientBrush.Create(shaderRect, colors, positions, angle);
            graphics.DrawString(text, renderFont, brush, 0, 0, format);
        });
        DrawBitmap(canvas, textBitmap, point);
        return true;
    }

    public void DrawString(object canvas, FontAdapter font, string text, Color color, PointF point)
        => _textCanvasCompat.DrawString(canvas, font, font.RenderFont, text, color, point);

    public void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
        => _textCanvasCompat.DrawGradientString(canvas, font, font.RenderFont, text, rect, point, size, colors, positions, angle);

    private static BBitmap RenderText(FontAdapter font, string text, Action<Graphics, Font, StringFormat> draw)
    {
        var renderFont = (Font)font.RenderFont;
        using var format = GdiTextMetricsCompat.CreateMeasureFormat();

        float width = GdiTextMetricsCompat.MeasureRaw(renderFont, text);
        float height = renderFont.GetHeight();
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width) + 1);
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height) + 1);

        var bitmap = new Bitmap(pixelWidth, pixelHeight, PixelFormat.Format32bppArgb);
        try
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                draw(graphics, renderFont, format);
            }

            return BBitmap.FromGdiImage(bitmap);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static void DrawBitmap(BCanvas canvas, BBitmap textBitmap, PointF point)
    {
        canvas.DrawBitmap(
            textBitmap,
            new RectangleF(point.X, point.Y, textBitmap.Width, textBitmap.Height),
            new RectangleF(0, 0, textBitmap.Width, textBitmap.Height));
    }
}
