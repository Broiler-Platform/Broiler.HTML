using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using PointF = System.Drawing.PointF;
using SizeF = System.Drawing.SizeF;
using RectangleF = System.Drawing.RectangleF;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class GraphicsAdapter : RGraphics
{
    private readonly DrawingContext _g;
    private readonly bool _releaseGraphics;

    public GraphicsAdapter(DrawingContext g, RectangleF initialClip, bool releaseGraphics = false) : base(WpfAdapter.Instance, initialClip)
    {
        ArgumentNullException.ThrowIfNull(g);

        _g = g;
        _releaseGraphics = releaseGraphics;
    }

    public GraphicsAdapter() : base(WpfAdapter.Instance, RectangleF.Empty)
    {
        _g = null;
        _releaseGraphics = false;
    }

    public override void PopClip()
    {
        _g.Pop();
        _clipStack.Pop();
    }

    public override void PushClip(RectangleF rect)
    {
        _clipStack.Push(rect);
        _g.PushClip(new RectangleGeometry(Utilities.Utils.Convert(rect)));
    }

    public override void PushClipExclude(RectangleF rect)
    {
        var geometry = new CombinedGeometry
        {
            Geometry1 = new RectangleGeometry(Utilities.Utils.Convert(_clipStack.Peek())),
            Geometry2 = new RectangleGeometry(Utilities.Utils.Convert(rect)),
            GeometryCombineMode = GeometryCombineMode.Exclude
        };

        _clipStack.Push(_clipStack.Peek());
        _g.PushClip(geometry);
    }

    public override object SetAntiAliasSmoothingMode() => null;

    public override void ReturnPreviousSmoothingMode(object prevMode)
    { }

    public override SizeF MeasureString(string str, RFont font)
    {
        double width = 0;
        GlyphTypeface glyphTypeface = ((FontAdapter)font).GlyphTypeface;
        
        if (glyphTypeface != null)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (glyphTypeface.CharacterToGlyphMap.ContainsKey(str[i]))
                {
                    ushort glyph = glyphTypeface.CharacterToGlyphMap[str[i]];
                    double advanceWidth = glyphTypeface.AdvanceWidths[glyph];
                    width += advanceWidth;
                }
                else
                {
                    width = 0;
                    break;
                }
            }
        }

        if (width <= 0)
        {
            var formattedText = new FormattedText(str, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ((FontAdapter)font).Font, 96d / 72d * font.Size, Brushes.Red, 1.0);
            return new SizeF((float)formattedText.WidthIncludingTrailingWhitespace, (float)formattedText.Height);
        }

        return new SizeF((float)(width * font.Size * 96d / 72d), (float)font.Height);
    }

    public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;
        bool handled = false;
        GlyphTypeface glyphTypeface = ((FontAdapter)font).GlyphTypeface;
        
        if (glyphTypeface != null)
        {
            handled = true;
            double width = 0;
            
            for (int i = 0; i < str.Length; i++)
            {
                if (glyphTypeface.CharacterToGlyphMap.ContainsKey(str[i]))
                {
                    ushort glyph = glyphTypeface.CharacterToGlyphMap[str[i]];
                    double advanceWidth = glyphTypeface.AdvanceWidths[glyph] * font.Size * 96d / 72d;

                    if (!(width + advanceWidth < maxWidth))
                    {
                        charFit = i;
                        charFitWidth = width;
                        break;
                    }
                    width += advanceWidth;
                }
                else
                {
                    handled = false;
                    break;
                }
            }
        }

        if (!handled)
        {
            var formattedText = new FormattedText(str, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ((FontAdapter)font).Font, 96d / 72d * font.Size, Brushes.Red, 1.0);
            charFit = str.Length;
            charFitWidth = formattedText.WidthIncludingTrailingWhitespace;
        }
    }

    public override void DrawString(string str, RFont font, Color color, PointF point, SizeF size, bool rtl)
    {
        var colorConv = ((BrushAdapter)_adapter.GetSolidBrush(color)).Brush;

        bool glyphRendered = false;
        GlyphTypeface glyphTypeface = ((FontAdapter)font).GlyphTypeface;
        
        if (glyphTypeface != null)
        {
            double width = 0;
            ushort[] glyphs = new ushort[str.Length];
            double[] widths = new double[str.Length];

            int i = 0;
            for (; i < str.Length; i++)
            {
                if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(str[i], out ushort glyph))
                    break;

                glyphs[i] = glyph;
                width += glyphTypeface.AdvanceWidths[glyph];
                widths[i] = 96d / 72d * font.Size * glyphTypeface.AdvanceWidths[glyph];
            }

            if (i >= str.Length)
            {
                point.Y += (float)(glyphTypeface.Baseline * font.Size * 96d / 72d);

                glyphRendered = true;
                var wpfPoint = Utilities.Utils.ConvertRound(point);

                // Always use bidiLevel 0 (LTR) because the CSS layout engine
                // (CssLayoutEngine.ApplyRightToLeft) already handles word-level
                // RTL positioning.  Using bidiLevel 1 here would reverse the
                // individual characters within a word, turning e.g. "Broiler"
                // into "reliorB".
                var glyphRun = new GlyphRun(glyphTypeface, 0,
                    false, 96d / 72d * font.Size, 1.0f, glyphs,
                    wpfPoint, widths, null, null, null, null, null, null);

                var guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(wpfPoint.X);
                guidelines.GuidelinesY.Add(wpfPoint.Y);
                _g.PushGuidelineSet(guidelines);
                _g.DrawGlyphRun(colorConv, glyphRun);
                _g.Pop();
            }
        }

        if (!glyphRendered)
        {
            // Always use LeftToRight flow direction — the layout engine has
            // already positioned each word correctly for RTL contexts.
            var formattedText = new FormattedText(str, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ((FontAdapter)font).Font, 96d / 72d * font.Size, colorConv, 1.0);
            _g.DrawText(formattedText, Utilities.Utils.ConvertRound(point));
        }
    }

    public override void DrawGradientString(string str, RFont font, RectangleF rect, PointF point, SizeF size, bool rtl, Color[] colors, float[] positions, float angle)
    {
        if (colors == null || colors.Length == 0)
            return;

        if (colors.Length == 1)
        {
            DrawString(str, font, colors[0], point, size, rtl);
            return;
        }

        float shaderWidth = rect.Width > 0 ? rect.Width : MeasureString(str, font).Width;
        float shaderHeight = Math.Max(rect.Height > 0 ? rect.Height : size.Height, (float)font.Size);
        var shaderRect = new RectangleF(rect.X, rect.Y, shaderWidth, shaderHeight);

        var radians = angle * Math.PI / 180.0;
        float cx = shaderRect.X + shaderRect.Width / 2f;
        float cy = shaderRect.Y + shaderRect.Height / 2f;
        float halfDiag = Math.Max(shaderRect.Width, shaderRect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        var startPoint = new Point(cx - sin * halfDiag, cy + cos * halfDiag);
        var endPoint = new Point(cx + sin * halfDiag, cy - cos * halfDiag);

        var gradientStops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++)
        {
            double offset = positions != null && i < positions.Length
                ? Math.Clamp(positions[i], 0f, 1f)
                : (double)i / (colors.Length - 1);
            gradientStops.Add(new GradientStop(Utilities.Utils.Convert(colors[i]), offset));
        }

        var brush = new LinearGradientBrush(gradientStops, startPoint, endPoint)
        {
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        brush.Freeze();

        bool glyphRendered = false;
        GlyphTypeface glyphTypeface = ((FontAdapter)font).GlyphTypeface;

        if (glyphTypeface != null)
        {
            ushort[] glyphs = new ushort[str.Length];
            double[] widths = new double[str.Length];

            int i = 0;
            for (; i < str.Length; i++)
            {
                if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(str[i], out ushort glyph))
                    break;

                glyphs[i] = glyph;
                widths[i] = 96d / 72d * font.Size * glyphTypeface.AdvanceWidths[glyph];
            }

            if (i >= str.Length)
            {
                point.Y += (float)(glyphTypeface.Baseline * font.Size * 96d / 72d);

                glyphRendered = true;
                var wpfPoint = Utilities.Utils.ConvertRound(point);
                var glyphRun = new GlyphRun(glyphTypeface, 0,
                    false, 96d / 72d * font.Size, 1.0f, glyphs,
                    wpfPoint, widths, null, null, null, null, null, null);

                var guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(wpfPoint.X);
                guidelines.GuidelinesY.Add(wpfPoint.Y);
                _g.PushGuidelineSet(guidelines);
                _g.DrawGlyphRun(brush, glyphRun);
                _g.Pop();
            }
        }

        if (!glyphRendered)
        {
            // Always use LeftToRight flow direction — the layout engine has
            // already positioned each word correctly for RTL contexts.
            var formattedText = new FormattedText(str, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ((FontAdapter)font).Font, 96d / 72d * font.Size, brush, 1.0);
            _g.DrawText(formattedText, Utilities.Utils.ConvertRound(point));
        }
    }

    public override RBrush GetTextureBrush(RImage image, RectangleF dstRect, PointF translateTransformLocation)
    {
        var brush = new ImageBrush(((ImageAdapter)image).Image)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.Tile,
            Viewport = Utilities.Utils.Convert(dstRect),
            ViewportUnits = BrushMappingMode.Absolute,
            Transform = new TranslateTransform(translateTransformLocation.X, translateTransformLocation.Y)
        };
        brush.Freeze();
        return new BrushAdapter(brush);
    }

    public override RGraphicsPath GetGraphicsPath() => new GraphicsPathAdapter();

    public override void Dispose()
    {
        if (_releaseGraphics)
            _g.Close();
    }


    public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
    {
        x1 = (int)x1;
        x2 = (int)x2;
        y1 = (int)y1;
        y2 = (int)y2;

        var adj = pen.Width;
        if (Math.Abs(x1 - x2) < .1 && Math.Abs(adj % 2 - 1) < .1)
        {
            x1 += .5;
            x2 += .5;
        }
        if (Math.Abs(y1 - y2) < .1 && Math.Abs(adj % 2 - 1) < .1)
        {
            y1 += .5;
            y2 += .5;
        }

        _g.DrawLine(((PenAdapter)pen).CreatePen(), new Point(x1, y1), new Point(x2, y2));
    }

    public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
    {
        var adj = pen.Width;
        if (Math.Abs(adj % 2 - 1) < .1)
        {
            x += .5;
            y += .5;
        }
        
        _g.DrawRectangle(null, ((PenAdapter)pen).CreatePen(), new Rect(x, y, width, height));
    }

    public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) => _g.DrawRectangle(((BrushAdapter)brush).Brush, null, new Rect(x, y, width, height));

    public override void DrawImage(RImage image, RectangleF destRect, RectangleF srcRect)
    {
        CroppedBitmap croppedImage = new(((ImageAdapter)image).Image, new Int32Rect((int)srcRect.X, (int)srcRect.Y, (int)srcRect.Width, (int)srcRect.Height));
        _g.DrawImage(croppedImage, Utilities.Utils.ConvertRound(destRect));
    }

    public override void DrawImage(RImage image, RectangleF destRect) => _g.DrawImage(((ImageAdapter)image).Image, Utilities.Utils.ConvertRound(destRect));

    public override void DrawPath(RPen pen, RGraphicsPath path) => _g.DrawGeometry(null, ((PenAdapter)pen).CreatePen(), ((GraphicsPathAdapter)path).GetClosedGeometry());

    public override void DrawPath(RBrush brush, RGraphicsPath path) => _g.DrawGeometry(((BrushAdapter)brush).Brush, null, ((GraphicsPathAdapter)path).GetClosedGeometry());

    public override void DrawPolygon(RBrush brush, PointF[] points)
    {
        if (points == null || points.Length == 0)
            return;

        var g = new StreamGeometry();
        using (var context = g.Open())
        {
            context.BeginFigure(Utilities.Utils.Convert(points[0]), true, true);
            for (int i = 1; i < points.Length; i++)
                context.LineTo(Utilities.Utils.Convert(points[i]), true, true);
        }
        g.Freeze();

        _g.DrawGeometry(((BrushAdapter)brush).Brush, null, g);
    }
}
