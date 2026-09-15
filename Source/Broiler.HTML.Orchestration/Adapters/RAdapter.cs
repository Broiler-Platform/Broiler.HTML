using Broiler.Graphics;
using Broiler.Graphics.Adapters;
using Broiler.Graphics.Color;
using Broiler.Graphics.Text;
using Broiler.HTML.Core;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Adapters;

public abstract class RAdapter : IColorResolver, IResourceFactory, IFontCreator, IAdapter
{
    private readonly ConcurrentDictionary<BColor, BBrush> _brushesCache = new();
    private readonly ConcurrentDictionary<BColor, BPen> _penCache = new();
    private readonly FontsHandler _fontsHandler;

    protected RAdapter() => _fontsHandler = new FontsHandler(this);

    public HtmlStyleSet DefaultStyleSet => HtmlStyleSet.Default;

    public BColor GetColor(string colorName)
    {
        ArgumentException.ThrowIfNullOrEmpty(colorName);
        return GetColorInt(colorName);
    }

    public BPen GetPen(BColor color) => _penCache.GetOrAdd(color, CreatePen);

    public BBrush GetSolidBrush(BColor color) => _brushesCache.GetOrAdd(color, CreateSolidBrush);

    public BBrush GetLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle) => CreateLinearGradientBrush(rect, color1, color2, angle);

    public BImage ConvertImage(object image) =>
        // TODO:a remove this by creating better API.
        ConvertImageInt(image);

    public BImage ImageFromStream(Stream memoryStream) => ImageFromStreamInt(memoryStream);

    public bool IsFontExists(string font) => _fontsHandler.IsFontExists(font);

    public void AddFontFamily(BFontFamily fontFamily) => _fontsHandler.AddFontFamily(fontFamily);

    public void AddFontFamilyMapping(string fromFamily, string toFamily) => _fontsHandler.AddFontFamilyMapping(fromFamily, toFamily);

    public BFont GetFont(string family, double size, FontStyle style, string fontFeatures = null) => _fontsHandler.GetCachedFont(family, size, style, fontFeatures);

    public object GetClipboardDataObject(string html, string plainText) => GetClipboardDataObjectInt(html, plainText);

    public void SetToClipboard(string text) => SetToClipboardInt(text);

    public void SetToClipboard(string html, string plainText) => SetToClipboardInt(html, plainText);

    public void SetToClipboard(BImage image) => SetToClipboardInt(image);

    public RContextMenu GetContextMenu() => CreateContextMenuInt();

    public void SaveToFile(BImage image, string name, string extension, RControl control = null) => SaveToFileInt(image, name, extension, control);

    BFont IFontCreator.CreateFont(string family, double size, FontStyle style) => CreateFontInt(family, size, style);

    BFont IFontCreator.CreateFont(BFontFamily family, double size, FontStyle style) => CreateFontInt(family, size, style);

    protected abstract BColor GetColorInt(string colorName);

    protected abstract BPen CreatePen(BColor color);

    protected abstract BBrush CreateSolidBrush(BColor color);

    protected abstract BBrush CreateLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle);

    protected abstract BImage ConvertImageInt(object image);

    protected abstract BImage ImageFromStreamInt(Stream memoryStream);

    protected abstract BFont CreateFontInt(string family, double size, FontStyle style);

    protected abstract BFont CreateFontInt(BFontFamily family, double size, FontStyle style);

    protected virtual object GetClipboardDataObjectInt(string html, string plainText) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(string text) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(string html, string plainText) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(BImage image) => throw new NotImplementedException();

    protected virtual RContextMenu CreateContextMenuInt() => throw new NotImplementedException();

    protected virtual void SaveToFileInt(BImage image, string name, string extension, RControl control = null) => throw new NotImplementedException();

    /// <summary>
    /// Loads a font from a file path and registers it as an available font family.
    /// Override in platform-specific adapters to implement font file loading.
    /// </summary>
    /// <param name="path">Absolute path to a .ttf or .otf font file.</param>
    /// <param name="mapFromName">Optional CSS family name to map to the loaded font.</param>
    /// <returns>The loaded font family name, or <c>null</c> if loading failed.</returns>
    public virtual string LoadFontFromFile(string path, string mapFromName = null) => null;
}
