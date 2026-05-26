using System;
using System.Drawing;
using SkiaSharp;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpColor = SixLabors.ImageSharp.Color;
using ImageSharpPointF = SixLabors.ImageSharp.PointF;
using SixLaborsFont = SixLabors.Fonts.Font;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaTextShaper : ITextShaper
{
    private readonly ITextMetricsCompat _textMetricsCompat;
    private readonly ITextCanvasCompat _textCanvasCompat;

    internal SkiaTextShaper(ITextMetricsCompat? textMetricsCompat = null, ITextCanvasCompat? textCanvasCompat = null)
    {
        _textMetricsCompat = textMetricsCompat ?? SkiaTextMetricsCompat.Instance;
        _textCanvasCompat = textCanvasCompat ?? SkiaTextCanvasCompat.Instance;
    }

    public static SkiaTextShaper Instance { get; } = new();

    public SizeF MeasureString(FontAdapter font, string text)
    {
        if (font.TryGetBroilerLayoutFont(out var broilerFont) && CanUseBroilerMeasurement(broilerFont))
        {
            var broilerSize = MeasureTextSize(broilerFont, text);
            return new SizeF(broilerSize.Width, (float)font.Height);
        }

        var width = _textMetricsCompat.MeasureTextWidth(font, text);
        return new SizeF(width, (float)font.Height);
    }

    public void MeasureString(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;

        if (font.TryGetBroilerLayoutFont(out var broilerFont) && CanUseBroilerMeasurement(broilerFont))
        {
            for (int i = 1; i <= text.Length; i++)
            {
                var substring = text.Substring(0, i);
                var width = MeasureTextSize(broilerFont, substring).Width;
                if (width > maxWidth)
                    break;

                charFit = i;
                charFitWidth = width;
            }

            return;
        }

        _textMetricsCompat.MeasureTextWidth(font, text, maxWidth, out charFit, out charFitWidth);
    }

    public bool TryDrawString(BCanvas canvas, FontAdapter font, string text, Color color, PointF point)
    {
        if (!font.TryGetBroilerRenderFont(out var broilerFont))
            return false;
        if (!TextCompatConstants.IsDeterministicFixtureFont(broilerFont.Family.Name))
            return false;

        var rendered = RenderTextBitmap(
            broilerFont,
            text,
            static (context, options, textValue, colorValue) => context.DrawText(options, textValue, ToImageSharpColor(colorValue)),
            text,
            color);
        using var textBitmap = rendered.Bitmap;
        DrawBitmap(canvas, textBitmap, new PointF(point.X + rendered.DrawOffset.X, point.Y + rendered.DrawOffset.Y));
        return true;
    }

    public bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
    {
        if (!font.TryGetBroilerRenderFont(out var broilerFont))
            return false;
        if (!TextCompatConstants.IsDeterministicFixtureFont(broilerFont.Family.Name))
            return false;

        var measuredSize = MeasureTextSize(broilerFont, text);
        var rendered = RenderTextBitmap(
            broilerFont,
            text,
            (context, options, textValue, gradientState) =>
            {
                if (gradientState.Colors.Length == 1)
                {
                    context.DrawText(options, textValue, ToImageSharpColor(gradientState.Colors[0]));
                    return;
                }

                float shaderWidth = Math.Max(gradientState.Rect.Width, gradientState.MeasuredSize.Width);
                float shaderHeight = Math.Max(gradientState.Rect.Height > 0 ? gradientState.Rect.Height : gradientState.Size.Height, broilerFont.Size);
                var shaderRect = new RectangleF(
                    gradientState.Rect.X - gradientState.Point.X,
                    gradientState.Rect.Y - gradientState.Point.Y,
                    shaderWidth,
                    shaderHeight);
                var (startPoint, endPoint) = GetGradientEndpoints(shaderRect, gradientState.Angle);
                var stops = new ColorStop[gradientState.Colors.Length];
                for (int i = 0; i < gradientState.Colors.Length; i++)
                {
                    float offset = gradientState.Positions != null && i < gradientState.Positions.Length
                        ? Math.Clamp(gradientState.Positions[i], 0f, 1f)
                        : (float)i / (gradientState.Colors.Length - 1);
                    stops[i] = new ColorStop(offset, ToImageSharpColor(gradientState.Colors[i]));
                }

                var brush = new LinearGradientBrush(
                    new ImageSharpPointF(startPoint.X, startPoint.Y),
                    new ImageSharpPointF(endPoint.X, endPoint.Y),
                    GradientRepetitionMode.None,
                    stops);
                context.DrawText(options, textValue, brush);
            },
            text,
            (Rect: rect, Point: point, Size: size, Colors: colors, Positions: positions, Angle: angle, MeasuredSize: measuredSize));
        using var textBitmap = rendered.Bitmap;
        DrawBitmap(canvas, textBitmap, new PointF(point.X + rendered.DrawOffset.X, point.Y + rendered.DrawOffset.Y));
        return true;
    }

    public void DrawString(object canvas, FontAdapter font, string text, Color color, PointF point)
        => _textCanvasCompat.DrawString(canvas, font, font.RenderFont, text, color, point);

    public void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
        => _textCanvasCompat.DrawGradientString(canvas, font, font.RenderFont, text, rect, point, size, colors, positions, angle);

    private static void DrawBitmap(BCanvas canvas, BBitmap textBitmap, PointF point)
    {
        canvas.DrawBitmap(
            textBitmap,
            new RectangleF(point.X, point.Y, textBitmap.Width, textBitmap.Height),
            new RectangleF(0, 0, textBitmap.Width, textBitmap.Height));
    }

    private static (BBitmap Bitmap, PointF DrawOffset) RenderTextBitmap<TState>(
        SixLaborsFont font,
        string text,
        Action<IImageProcessingContext, RichTextOptions, string, TState> drawAction,
        string textValue,
        TState state)
    {
        var options = CreateTextOptions(font);
        var size = MeasureTextSize(font, text);
        var bounds = TextMeasurer.MeasureBounds(text, options);
        float originX = bounds.Left < 0 ? -bounds.Left : 0;
        float originY = bounds.Top < 0 ? -bounds.Top : 0;
        options.Origin = new ImageSharpPointF(originX, originY);
        int width = Math.Max(1, (int)Math.Ceiling(Math.Max(size.Width, bounds.Width)));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Max(size.Height, bounds.Height)));

        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height, ImageSharpColor.Transparent);
        image.Mutate(context => drawAction(context, options, textValue, state));
        return (BBitmap.CreateFromImageSharpImage(image), new PointF(-originX, -originY));
    }

    private static ImageSharpColor ToImageSharpColor(Color color) =>
        ImageSharpColor.FromRgba(color.R, color.G, color.B, color.A);

    private static RichTextOptions CreateTextOptions(SixLaborsFont font) =>
        new(font)
        {
            Origin = new ImageSharpPointF(0, 0),
            Dpi = 96,
            KerningMode = KerningMode.None,
        };

    private static SixLabors.Fonts.FontRectangle MeasureTextSize(SixLaborsFont font, string text) =>
        TextMeasurer.MeasureSize(text, CreateTextOptions(font));

    private static (PointF StartPoint, PointF EndPoint) GetGradientEndpoints(RectangleF rect, float angle)
    {
        var radians = angle * TextCompatConstants.DegreesToRadians;
        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        float halfDiag = Math.Max(rect.Width, rect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        return (
            new PointF(cx - sin * halfDiag, cy + cos * halfDiag),
            new PointF(cx + sin * halfDiag, cy - cos * halfDiag));
    }

    // The deterministic Ahem fixtures are the only text cases where the current
    // Broiler width measurement exactly matches the legacy Skia layout path.
    // Keep broader font measurement on the Skia compatibility path for the
    // remaining M5 cutover window while the text-fidelity gates in
    // TextFidelityThresholdTests stay pinned to the legacy layout baseline.
    private static bool CanUseBroilerMeasurement(SixLaborsFont font) =>
        TextCompatConstants.IsDeterministicFixtureFont(font.Family.Name);
}
