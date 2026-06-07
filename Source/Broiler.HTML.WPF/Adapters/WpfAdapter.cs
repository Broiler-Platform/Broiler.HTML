using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using FontStyle = Broiler.Graphics.FontStyle;
using Color = System.Drawing.Color;
using Microsoft.Win32;
using RectangleF = System.Drawing.RectangleF;
using Broiler.HTML.Adapters;
using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Image;
using Broiler.HTML.WPF.Utilities;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class WpfAdapter : RAdapter
{
    private static readonly List<string> ValidColorNamesLc;

    static WpfAdapter()
    {
        ValidColorNamesLc = [];
        var colorList = new List<PropertyInfo>(typeof(Colors).GetProperties());

        foreach (var colorProp in colorList)
            ValidColorNamesLc.Add(colorProp.Name.ToLower());
    }

    private WpfAdapter()
    {
        var systemFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            try
            {
                AddFontFamily(new FontFamilyAdapter(family));
                systemFonts.Add(family.Source);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HtmlRenderer] WpfAdapter failed to add font family: {ex.Message}");
            }
        }

        foreach (var mapping in FontFamilyFallbackPolicy.ResolveDefaultMappings(systemFonts))
        {
            AddFontFamilyMapping(mapping.Key, mapping.Value);
        }
    }

    public static WpfAdapter Instance { get; } = new();

    protected override Color GetColorInt(string colorName)
    {
        if (CssSystemColors.TryResolve(colorName, out var color))
            return color;

        // check if color name is valid to avoid ColorConverter throwing an exception
        if (!ValidColorNamesLc.Contains(colorName.ToLower()))
            return Color.Empty;

        var convertFromString = ColorConverter.ConvertFromString(colorName) ?? Colors.Black;
        return Utilities.Utils.Convert((System.Windows.Media.Color)convertFromString);
    }

    protected override RPen CreatePen(Color color) => new PenAdapter(GetSolidColorBrush(color));

    protected override RBrush CreateSolidBrush(Color color) => new BrushAdapter(GetSolidColorBrush(color));

    protected override RBrush CreateLinearGradientBrush(RectangleF rect, Color color1, Color color2, double angle)
    {
        var startColor = angle <= 180 ? Utilities.Utils.Convert(color1) : Utilities.Utils.Convert(color2);
        var endColor = angle <= 180 ? Utilities.Utils.Convert(color2) : Utilities.Utils.Convert(color1);
        angle = angle <= 180 ? angle : angle - 180;

        double x = angle < 135 ? Math.Max((angle - 45) / 90, 0) : 1;
        double y = angle <= 45 ? Math.Max(0.5 - angle / 90, 0) : angle > 135 ? Math.Abs(1.5 - angle / 90) : 0;
        return new BrushAdapter(new LinearGradientBrush(startColor, endColor, new Point(x, y), new Point(1 - x, 1 - y)));
    }

    protected override RImage ConvertImageInt(object image) => image != null ? new ImageAdapter((BitmapImage)image) : null;

    protected override RImage ImageFromStreamInt(Stream memoryStream)
    {
        byte[] data;
        if (memoryStream is MemoryStream ms)
        {
            data = ms.ToArray();
        }
        else if (memoryStream.CanSeek)
        {
            data = new byte[memoryStream.Length - memoryStream.Position];
            _ = memoryStream.Read(data, 0, data.Length);
        }
        else
        {
            using var copy = new MemoryStream();
            memoryStream.CopyTo(copy);
            data = copy.ToArray();
        }

        if (BSvgRasterizer.IsSvgData(data))
        {
            using var bitmap = BSvgRasterizer.RasterizeToBitmap(data);
            return bitmap != null ? CreateBitmapImage(bitmap.Encode(BImageFormat.Png, 100)) : null;
        }

        return CreateBitmapImage(data);
    }

    private static ImageAdapter CreateBitmapImage(byte[] data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(data);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        return new ImageAdapter(bitmap);
    }

    protected override RFont CreateFontInt(string family, double size, FontStyle style)
    {
        var fontFamily = (FontFamily)new FontFamilyConverter().ConvertFromString(family) ?? new FontFamily();
        return new FontAdapter(new Typeface(fontFamily, GetWpfFontStyle(style), GetFontWidth(style), FontStretches.Normal), size);
    }

    protected override RFont CreateFontInt(RFontFamily family, double size, FontStyle style) => new FontAdapter(new Typeface(((FontFamilyAdapter)family).FontFamily, GetWpfFontStyle(style), GetFontWidth(style), FontStretches.Normal), size);

    protected override object GetClipboardDataObjectInt(string html, string plainText) => ClipboardHelper.CreateDataObject(html, plainText);

    protected override void SetToClipboardInt(string text) => ClipboardHelper.CopyToClipboard(text);

    protected override void SetToClipboardInt(string html, string plainText) => ClipboardHelper.CopyToClipboard(html, plainText);

    protected override void SetToClipboardInt(RImage image) => Clipboard.SetImage(((ImageAdapter)image).Image);

    protected override RContextMenu CreateContextMenuInt() => new ContextMenuAdapter();

    protected override void SaveToFileInt(RImage image, string name, string extension, RControl control = null)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "Images|*.png;*.bmp;*.jpg;*.tif;*.gif;*.wmp;",
            FileName = name,
            DefaultExt = extension
        };

        var dialogResult = saveDialog.ShowDialog();
        
        if (!dialogResult.GetValueOrDefault())
            return;

        var encoder = Utilities.Utils.GetBitmapEncoder(Path.GetExtension(saveDialog.FileName));
        encoder.Frames.Add(BitmapFrame.Create(((ImageAdapter)image).Image));
        using FileStream stream = new(saveDialog.FileName, FileMode.OpenOrCreate);
        encoder.Save(stream);
    }

    private static Brush GetSolidColorBrush(Color color)
    {
        Brush solidBrush;
        if (color == Color.White)
            solidBrush = Brushes.White;
        else if (color == Color.Black)
            solidBrush = Brushes.Black;
        else if (color.A < 1)
            solidBrush = Brushes.Transparent;
        else
            solidBrush = new SolidColorBrush(Utilities.Utils.Convert(color));
        return solidBrush;
    }

    private static System.Windows.FontStyle GetWpfFontStyle(FontStyle style)
    {
        if ((style & FontStyle.Italic) == FontStyle.Italic)
            return FontStyles.Italic;

        return FontStyles.Normal;
    }

    private static FontWeight GetFontWidth(FontStyle style)
    {
        if ((style & FontStyle.Bold) == FontStyle.Bold)
            return FontWeights.Bold;

        return FontWeights.Normal;
    }
}
