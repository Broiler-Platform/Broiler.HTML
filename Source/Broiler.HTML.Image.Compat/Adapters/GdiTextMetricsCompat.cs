using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiTextMetricsCompat : ITextMetricsCompat
{
    /// <summary>
    /// Ratio to convert typographic points to CSS pixels (96 DPI / 72 DPI).
    /// The layout font is created at point size but CSS layout uses pixel
    /// units, so every measurement must be scaled by this factor.
    /// </summary>
    private const float PtToCssPx = 96f / 72f;

    [ThreadStatic]
    private static Graphics _measureGraphics;

    public static ITextMetricsCompat Instance { get; } = new GdiTextMetricsCompat();

    public float MeasureTextWidth(FontAdapter font, string text) =>
        MeasureRaw((Font)font.Font, text) * PtToCssPx;

    public void MeasureTextWidth(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;
        if (string.IsNullOrEmpty(text))
            return;

        var gdiFont = (Font)font.Font;
        for (int i = 1; i <= text.Length; i++)
        {
            var substring = text.Substring(0, i);
            double width = MeasureRaw(gdiFont, substring) * PtToCssPx;
            if (width > maxWidth)
                break;

            charFit = i;
            charFitWidth = width;
        }
    }

    internal static float MeasureRaw(Font font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        var graphics = EnsureGraphics();
        using var format = CreateMeasureFormat();
        return graphics.MeasureString(text, font, new SizeF(float.MaxValue, float.MaxValue), format).Width;
    }

    internal static StringFormat CreateMeasureFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        format.Trimming = StringTrimming.None;
        return format;
    }

    private static Graphics EnsureGraphics()
    {
        if (_measureGraphics is not null)
            return _measureGraphics;

        var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        _measureGraphics = graphics;
        return graphics;
    }
}
