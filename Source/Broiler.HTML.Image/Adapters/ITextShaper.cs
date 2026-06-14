using System.Drawing;

namespace Broiler.HTML.Image.Adapters;

internal interface ITextShaper
{
    SizeF MeasureString(FontAdapter font, string text);
    void MeasureString(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth);
    bool TryDrawString(BCanvas canvas, FontAdapter font, string text, Color color, PointF point, float glyphRotationDeg = 0f);
    bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle);
    void DrawString(object canvas, FontAdapter font, string text, Color color, PointF point);
    void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle);
}
