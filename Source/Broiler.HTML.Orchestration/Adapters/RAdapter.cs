using Broiler.Graphics;
using Broiler.Graphics.Adapters;
using Broiler.HTML.Core;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Adapters;

public abstract class RAdapter : IColorResolver, IResourceFactory, IFontCreator, IAdapter
{
    private readonly ConcurrentDictionary<BColor, RBrush> _brushesCache = new();
    private readonly ConcurrentDictionary<BColor, RPen> _penCache = new();
    private readonly FontsHandler _fontsHandler;

    private RImage _loadImage;
    private RImage _errorImage;

    protected RAdapter() => _fontsHandler = new FontsHandler(this);

    public HtmlStyleSet DefaultStyleSet => HtmlStyleSet.Default;

    public BColor GetColor(string colorName)
    {
        ArgumentException.ThrowIfNullOrEmpty(colorName);
        return GetColorInt(colorName);
    }

    public RPen GetPen(BColor color) => _penCache.GetOrAdd(color, CreatePen);

    public RBrush GetSolidBrush(BColor color) => _brushesCache.GetOrAdd(color, CreateSolidBrush);

    public RBrush GetLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle) => CreateLinearGradientBrush(rect, color1, color2, angle);

    public RImage ConvertImage(object image) =>
        // TODO:a remove this by creating better API.
        ConvertImageInt(image);

    public RImage ImageFromStream(Stream memoryStream) => ImageFromStreamInt(memoryStream);

    public bool IsFontExists(string font) => _fontsHandler.IsFontExists(font);

    public void AddFontFamily(RFontFamily fontFamily) => _fontsHandler.AddFontFamily(fontFamily);

    public void AddFontFamilyMapping(string fromFamily, string toFamily) => _fontsHandler.AddFontFamilyMapping(fromFamily, toFamily);

    public RFont GetFont(string family, double size, Graphics.FontStyle style, string fontFeatures = null) => _fontsHandler.GetCachedFont(family, size, style, fontFeatures);

    public RImage GetLoadingImage()
    {
        if (_loadImage == null)
        {
            var stream = typeof(FontsHandler).Assembly.GetManifestResourceStream("TheArtOfDev.HtmlRenderer.Core.Utils.ImageLoad.png");

            if (stream != null)
                _loadImage = ImageFromStream(stream);
        }

        return _loadImage;
    }

    public RImage GetLoadingFailedImage()
    {
        if (_errorImage == null)
        {
            var stream = typeof(FontsHandler).Assembly.GetManifestResourceStream("TheArtOfDev.HtmlRenderer.Core.Utils.ImageError.png");

            if (stream != null)
                _errorImage = ImageFromStream(stream);
        }

        return _errorImage;
    }

    public object GetClipboardDataObject(string html, string plainText) => GetClipboardDataObjectInt(html, plainText);

    public void SetToClipboard(string text) => SetToClipboardInt(text);

    public void SetToClipboard(string html, string plainText) => SetToClipboardInt(html, plainText);

    public void SetToClipboard(RImage image) => SetToClipboardInt(image);

    public RContextMenu GetContextMenu() => CreateContextMenuInt();

    public void SaveToFile(RImage image, string name, string extension, RControl control = null) => SaveToFileInt(image, name, extension, control);

    RFont IFontCreator.CreateFont(string family, double size, Graphics.FontStyle style) => CreateFontInt(family, size, style);

    RFont IFontCreator.CreateFont(RFontFamily family, double size, Graphics.FontStyle style) => CreateFontInt(family, size, style);

    protected abstract BColor GetColorInt(string colorName);

    protected abstract RPen CreatePen(BColor color);

    protected abstract RBrush CreateSolidBrush(BColor color);

    protected abstract RBrush CreateLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle);

    protected abstract RImage ConvertImageInt(object image);

    protected abstract RImage ImageFromStreamInt(Stream memoryStream);

    protected abstract RFont CreateFontInt(string family, double size, Graphics.FontStyle style);

    protected abstract RFont CreateFontInt(RFontFamily family, double size, Graphics.FontStyle style);

    protected virtual object GetClipboardDataObjectInt(string html, string plainText) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(string text) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(string html, string plainText) => throw new NotImplementedException();

    protected virtual void SetToClipboardInt(RImage image) => throw new NotImplementedException();

    protected virtual RContextMenu CreateContextMenuInt() => throw new NotImplementedException();

    protected virtual void SaveToFileInt(RImage image, string name, string extension, RControl control = null) => throw new NotImplementedException();

    /// <summary>
    /// Loads a font from a file path and registers it as an available font family.
    /// Override in platform-specific adapters to implement font file loading.
    /// </summary>
    /// <param name="path">Absolute path to a .ttf or .otf font file.</param>
    /// <param name="mapFromName">Optional CSS family name to map to the loaded font.</param>
    /// <returns>The loaded font family name, or <c>null</c> if loading failed.</returns>
    public virtual string LoadFontFromFile(string path, string mapFromName = null) => null;
}
