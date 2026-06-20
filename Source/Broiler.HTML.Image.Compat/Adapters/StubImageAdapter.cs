using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Broiler.HTML.Adapters;
using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;

namespace Broiler.HTML.Image.Adapters;

/// <summary>
/// Resource factory for the compatibility backend. Retains the OS-free managed
/// logic (CSS color parsing, SVG intrinsic sizing) and routes pens/brushes/fonts
/// and image decoding through the stub compat backend; raster output still comes
/// from the managed Broiler pipeline.
/// </summary>
internal sealed class StubImageAdapter : RAdapter
{
    private const int MinSvgRasterLongestSide = 128;
    private const int MaxSvgRasterScale = 8;

    private readonly IFontTypefaceResolver _typefaceResolver;
    private readonly IPaintCompatFactory _paintCompatFactory;

    internal StubImageAdapter(
        IFontTypefaceResolver typefaceResolver = null,
        IReadOnlyCollection<string> systemFonts = null,
        IPaintCompatFactory paintCompatFactory = null)
    {
        _typefaceResolver = typefaceResolver ?? CompatProvider.CreateFontTypefaceResolver();
        _paintCompatFactory = paintCompatFactory ?? CompatProvider.PaintCompatFactory;

        // Register system fonts first so we can probe availability below.
        var distinctSystemFonts = new HashSet<string>(
            systemFonts ?? BroilerFontRegistry.GetSystemFontFamilies(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var familyName in distinctSystemFonts)
        {
            AddFontFamily(new FontFamilyAdapter(familyName));
        }

        foreach (var mapping in FontFamilyFallbackPolicy.ResolveDefaultMappings(distinctSystemFonts))
        {
            AddFontFamilyMapping(mapping.Key, mapping.Value);
        }
    }

    public static StubImageAdapter Instance { get; } = new();

    internal bool HasDeferredLoadedTypefacePath(string family) =>
        _typefaceResolver.HasDeferredLoadedTypefacePath(family);

    internal bool HasMaterializedLoadedTypeface(string family) =>
        _typefaceResolver.HasMaterializedLoadedTypeface(family);

    /// <summary>
    /// Loads a TrueType/OpenType font from a file path and registers it as
    /// an available font family.  Optionally maps a CSS name to the loaded
    /// family (e.g. mapping "sans-serif" to a bundled reference font for
    /// deterministic test comparison).
    /// </summary>
    /// <param name="path">Absolute path to a .ttf or .otf font file.</param>
    /// <param name="mapFromName">
    /// When non-null, registers the font under this CSS family alias instead
    /// of eagerly probing the file for its embedded family name.
    /// </param>
    /// <returns>
    /// The registered family name (the alias when provided), or <c>null</c> on failure.
    /// </returns>
    public override string LoadFontFromFile(string path, string mapFromName = null)
    {
        var familyName = _typefaceResolver.RegisterFontFile(path, mapFromName);
        if (string.IsNullOrWhiteSpace(familyName))
            return null;

        BroilerFontRegistry.RegisterFontFile(path, familyName);
        AddFontFamily(new FontFamilyAdapter(familyName));
        return familyName;
    }

    protected override Color GetColorInt(string colorName)
    {
        if (TryParseHexColor(colorName, out var color))
            return color;

        if (CssSystemColors.TryResolve(colorName, out color))
            return color;

        // Fallback: try common color names (CSS 2.1 basic + CSS Color Level 3 extended)
        return colorName.ToLowerInvariant() switch
        {
            // CSS 2.1 §4.3.6 basic color keywords
            "white" => Color.FromArgb(255, 255, 255, 255),
            "black" => Color.FromArgb(255, 0, 0, 0),
            "red" => Color.FromArgb(255, 255, 0, 0),
            "green" => Color.FromArgb(255, 0, 128, 0),
            "blue" => Color.FromArgb(255, 0, 0, 255),
            "yellow" => Color.FromArgb(255, 255, 255, 0),
            "orange" => Color.FromArgb(255, 255, 165, 0),
            "purple" => Color.FromArgb(255, 128, 0, 128),
            "gray" or "grey" => Color.FromArgb(255, 128, 128, 128),
            "silver" => Color.FromArgb(255, 192, 192, 192),
            "maroon" => Color.FromArgb(255, 128, 0, 0),
            "olive" => Color.FromArgb(255, 128, 128, 0),
            "lime" => Color.FromArgb(255, 0, 255, 0),
            "aqua" or "cyan" => Color.FromArgb(255, 0, 255, 255),
            "teal" => Color.FromArgb(255, 0, 128, 128),
            "navy" => Color.FromArgb(255, 0, 0, 128),
            "fuchsia" or "magenta" => Color.FromArgb(255, 255, 0, 255),
            "transparent" => Color.FromArgb(0, 255, 255, 255),

            // CSS Color Level 3 extended color keywords (used by WPT tests)
            "lightgray" or "lightgrey" => Color.FromArgb(255, 211, 211, 211),
            "darkgray" or "darkgrey" => Color.FromArgb(255, 169, 169, 169),
            "dimgray" or "dimgrey" => Color.FromArgb(255, 105, 105, 105),
            "lightslategray" or "lightslategrey" => Color.FromArgb(255, 119, 136, 153),
            "slategray" or "slategrey" => Color.FromArgb(255, 112, 128, 144),
            "darkslategray" or "darkslategrey" => Color.FromArgb(255, 47, 79, 79),
            "gainsboro" => Color.FromArgb(255, 220, 220, 220),
            "whitesmoke" => Color.FromArgb(255, 245, 245, 245),
            "aliceblue" => Color.FromArgb(255, 240, 248, 255),
            "ghostwhite" => Color.FromArgb(255, 248, 248, 255),
            "snow" => Color.FromArgb(255, 255, 250, 250),
            "seashell" => Color.FromArgb(255, 255, 245, 238),
            "floralwhite" => Color.FromArgb(255, 255, 250, 240),
            "linen" => Color.FromArgb(255, 250, 240, 230),
            "antiquewhite" => Color.FromArgb(255, 250, 235, 215),
            "oldlace" => Color.FromArgb(255, 253, 245, 230),
            "papayawhip" => Color.FromArgb(255, 255, 239, 213),
            "blanchedalmond" => Color.FromArgb(255, 255, 235, 205),
            "bisque" => Color.FromArgb(255, 255, 228, 196),
            "peachpuff" => Color.FromArgb(255, 255, 218, 185),
            "navajowhite" => Color.FromArgb(255, 255, 222, 173),
            "moccasin" => Color.FromArgb(255, 255, 228, 181),
            "cornsilk" => Color.FromArgb(255, 255, 248, 220),
            "ivory" => Color.FromArgb(255, 255, 255, 240),
            "lemonchiffon" => Color.FromArgb(255, 255, 250, 205),
            "lightyellow" => Color.FromArgb(255, 255, 255, 224),
            "lightgoldenrodyellow" => Color.FromArgb(255, 250, 250, 210),
            "beige" => Color.FromArgb(255, 245, 245, 220),
            "wheat" => Color.FromArgb(255, 245, 222, 179),
            "sandybrown" => Color.FromArgb(255, 244, 164, 96),
            "goldenrod" => Color.FromArgb(255, 218, 165, 32),
            "darkgoldenrod" => Color.FromArgb(255, 184, 134, 11),
            "gold" => Color.FromArgb(255, 255, 215, 0),
            "khaki" => Color.FromArgb(255, 240, 230, 140),
            "darkkhaki" => Color.FromArgb(255, 189, 183, 107),
            "tan" => Color.FromArgb(255, 210, 180, 140),
            "burlywood" => Color.FromArgb(255, 222, 184, 135),
            "peru" => Color.FromArgb(255, 205, 133, 63),
            "chocolate" => Color.FromArgb(255, 210, 105, 30),
            "sienna" => Color.FromArgb(255, 160, 82, 45),
            "saddlebrown" => Color.FromArgb(255, 139, 69, 19),
            "brown" => Color.FromArgb(255, 165, 42, 42),
            "firebrick" => Color.FromArgb(255, 178, 34, 34),
            "darkred" => Color.FromArgb(255, 139, 0, 0),
            "indianred" => Color.FromArgb(255, 205, 92, 92),
            "rosybrown" => Color.FromArgb(255, 188, 143, 143),
            "lightcoral" => Color.FromArgb(255, 240, 128, 128),
            "salmon" => Color.FromArgb(255, 250, 128, 114),
            "darksalmon" => Color.FromArgb(255, 233, 150, 122),
            "lightsalmon" => Color.FromArgb(255, 255, 160, 122),
            "coral" => Color.FromArgb(255, 255, 127, 80),
            "tomato" => Color.FromArgb(255, 255, 99, 71),
            "orangered" => Color.FromArgb(255, 255, 69, 0),
            "darkorange" => Color.FromArgb(255, 255, 140, 0),
            "crimson" => Color.FromArgb(255, 220, 20, 60),
            "deeppink" => Color.FromArgb(255, 255, 20, 147),
            "hotpink" => Color.FromArgb(255, 255, 105, 180),
            "lightpink" => Color.FromArgb(255, 255, 182, 193),
            "pink" => Color.FromArgb(255, 255, 192, 203),
            "palevioletred" => Color.FromArgb(255, 219, 112, 147),
            "mediumvioletred" => Color.FromArgb(255, 199, 21, 133),
            "orchid" => Color.FromArgb(255, 218, 112, 214),
            "plum" => Color.FromArgb(255, 221, 160, 221),
            "violet" => Color.FromArgb(255, 238, 130, 238),
            "mediumpurple" => Color.FromArgb(255, 147, 112, 219),
            "darkorchid" => Color.FromArgb(255, 153, 50, 204),
            "darkviolet" => Color.FromArgb(255, 148, 0, 211),
            "darkmagenta" => Color.FromArgb(255, 139, 0, 139),
            "blueviolet" => Color.FromArgb(255, 138, 43, 226),
            "indigo" => Color.FromArgb(255, 75, 0, 130),
            "rebeccapurple" => Color.FromArgb(255, 102, 51, 153),
            "slateblue" => Color.FromArgb(255, 106, 90, 205),
            "darkslateblue" => Color.FromArgb(255, 72, 61, 139),
            "mediumslateblue" => Color.FromArgb(255, 123, 104, 238),
            "lavender" => Color.FromArgb(255, 230, 230, 250),
            "thistle" => Color.FromArgb(255, 216, 191, 216),
            "mistyrose" => Color.FromArgb(255, 255, 228, 225),
            "lavenderblush" => Color.FromArgb(255, 255, 240, 245),
            "honeydew" => Color.FromArgb(255, 240, 255, 240),
            "mintcream" => Color.FromArgb(255, 245, 255, 250),
            "azure" => Color.FromArgb(255, 240, 255, 255),
            "lightsteelblue" => Color.FromArgb(255, 176, 196, 222),
            "powderblue" => Color.FromArgb(255, 176, 224, 230),
            "lightblue" => Color.FromArgb(255, 173, 216, 230),
            "skyblue" => Color.FromArgb(255, 135, 206, 235),
            "lightskyblue" => Color.FromArgb(255, 135, 206, 250),
            "deepskyblue" => Color.FromArgb(255, 0, 191, 255),
            "dodgerblue" => Color.FromArgb(255, 30, 144, 255),
            "cornflowerblue" => Color.FromArgb(255, 100, 149, 237),
            "steelblue" => Color.FromArgb(255, 70, 130, 180),
            "royalblue" => Color.FromArgb(255, 65, 105, 225),
            "mediumblue" => Color.FromArgb(255, 0, 0, 205),
            "darkblue" => Color.FromArgb(255, 0, 0, 139),
            "midnightblue" => Color.FromArgb(255, 25, 25, 112),
            "cadetblue" => Color.FromArgb(255, 95, 158, 160),
            "paleturquoise" => Color.FromArgb(255, 175, 238, 238),
            "turquoise" => Color.FromArgb(255, 64, 224, 208),
            "mediumturquoise" => Color.FromArgb(255, 72, 209, 204),
            "darkturquoise" => Color.FromArgb(255, 0, 206, 209),
            "lightcyan" => Color.FromArgb(255, 224, 255, 255),
            "mediumaquamarine" => Color.FromArgb(255, 102, 205, 170),
            "aquamarine" => Color.FromArgb(255, 127, 255, 212),
            "darkseagreen" => Color.FromArgb(255, 143, 188, 143),
            "mediumseagreen" => Color.FromArgb(255, 60, 179, 113),
            "seagreen" => Color.FromArgb(255, 46, 139, 87),
            "darkcyan" => Color.FromArgb(255, 0, 139, 139),
            "lightseagreen" => Color.FromArgb(255, 32, 178, 170),
            "lightgreen" => Color.FromArgb(255, 144, 238, 144),
            "palegreen" => Color.FromArgb(255, 152, 251, 152),
            "springgreen" => Color.FromArgb(255, 0, 255, 127),
            "mediumspringgreen" => Color.FromArgb(255, 0, 250, 154),
            "lawngreen" => Color.FromArgb(255, 124, 252, 0),
            "chartreuse" => Color.FromArgb(255, 127, 255, 0),
            "greenyellow" => Color.FromArgb(255, 173, 255, 47),
            "yellowgreen" => Color.FromArgb(255, 154, 205, 50),
            "limegreen" => Color.FromArgb(255, 50, 205, 50),
            "forestgreen" => Color.FromArgb(255, 34, 139, 34),
            "darkgreen" => Color.FromArgb(255, 0, 100, 0),
            "olivedrab" => Color.FromArgb(255, 107, 142, 35),
            "darkolivegreen" => Color.FromArgb(255, 85, 107, 47),

            _ => ResolveExtendedColorName(colorName),
        };
    }

    /// <summary>
    /// Fallback for CSS named colors not in the primary switch. Uses
    /// <see cref="Color.FromName"/> which recognises .NET known colors
    /// (case-insensitive). Returns black if the name is unrecognised.
    /// </summary>
    private static Color ResolveExtendedColorName(string colorName)
    {
        var c = Color.FromName(colorName);
        if (c.IsKnownColor)
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        return Color.FromArgb(255, 0, 0, 0);
    }

    private static bool TryParseHexColor(string colorName, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(colorName) || colorName[0] != '#')
            return false;

        return colorName.Length switch
        {
            4 => TryParseHexColor(colorName, 1, 1, hasAlpha: false, out color),
            5 => TryParseHexColor(colorName, 1, 1, hasAlpha: true, out color),
            7 => TryParseHexColor(colorName, 1, 2, hasAlpha: false, out color),
            9 => TryParseHexColor(colorName, 1, 2, hasAlpha: true, out color),
            _ => false,
        };
    }

    private static bool TryParseHexColor(string colorName, int start, int digitsPerChannel, bool hasAlpha, out Color color)
    {
        color = Color.Empty;
        if (!TryParseHexChannel(colorName, start, digitsPerChannel, out var r)
            || !TryParseHexChannel(colorName, start + digitsPerChannel, digitsPerChannel, out var g)
            || !TryParseHexChannel(colorName, start + (digitsPerChannel * 2), digitsPerChannel, out var b))
        {
            return false;
        }

        var alpha = 255;
        if (hasAlpha
            && !TryParseHexChannel(colorName, start + (digitsPerChannel * 3), digitsPerChannel, out alpha))
        {
            return false;
        }

        color = Color.FromArgb(alpha, r, g, b);
        return true;
    }

    private static bool TryParseHexChannel(string colorName, int start, int digits, out int value)
    {
        value = 0;
        for (int i = 0; i < digits; i++)
        {
            var digit = ConvertHexDigit(colorName[start + i]);
            if (digit < 0)
                return false;

            value = (value << 4) + digit;
        }

        if (digits == 1)
            value = (value << 4) + value;

        return true;
    }

    private static int ConvertHexDigit(char c)
    {
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'a' and <= 'f')
            return c - 'a' + 10;
        if (c is >= 'A' and <= 'F')
            return c - 'A' + 10;

        return -1;
    }

    protected override RPen CreatePen(Color color)
    {
        return new PenAdapter(
            (strokeWidth, dashStyle) => _paintCompatFactory.CreatePenPaint(color, strokeWidth, dashStyle),
            (paint, strokeWidth, dashStyle) => _paintCompatFactory.UpdatePenPaint(paint, strokeWidth, dashStyle))
        {
            SolidColor = new BColor(color.R, color.G, color.B, color.A),
        };
    }

    protected override RBrush CreateSolidBrush(Color color)
    {
        return new BrushAdapter(
            () => _paintCompatFactory.CreateSolidBrushPaint(color),
            dispose: true)
        {
            SolidColor = new BColor(color.R, color.G, color.B, color.A),
        };
    }

    protected override RBrush CreateLinearGradientBrush(RectangleF rect, Color color1, Color color2, double angle)
    {
        return new BrushAdapter(
            () => _paintCompatFactory.CreateLinearGradientBrushPaint(rect, color1, color2, angle),
            dispose: true);
    }

    protected override RImage ConvertImageInt(object image) =>
        // There is no native bitmap type to convert without an OS graphics backend.
        // Callers should decode encoded image bytes through ImageFromStream instead.
        throw new NotSupportedException(
            "Converting a platform bitmap is not supported without an OS graphics backend; use ImageFromStream with encoded image data.");

    protected override RImage ImageFromStreamInt(Stream memoryStream)
    {
        // Read the stream into a byte array so we can inspect the content
        // before attempting a bitmap decode and can still route SVG input
        // through the dedicated Broiler rasterizer.
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
            return RasterizeSvg(data);
        }

        try
        {
            return new ImageAdapter(BBitmap.Decode(data));
        }
        catch (ArgumentException)
        {
            // Unrecognised or corrupt image data.
            return null;
        }
        catch (NotSupportedException)
        {
            // No image codec backend is registered (see Broiler.Graphics.BImageCodec).
            return null;
        }
    }

    /// <summary>
    /// Rasterizes SVG data to a backend-neutral bitmap through <see cref="BSvgRasterizer"/>.
    /// Parses width/height from the SVG root element to determine output
    /// dimensions.  Per the HTML spec, when the SVG does not specify both
    /// explicit width AND height the intrinsic size is 300×150 (the default
    /// replaced element size).  This matches browser behaviour (Chromium).
    /// </summary>
    private static RImage RasterizeSvg(byte[] data)
    {
        var svgContent = System.Text.Encoding.UTF8.GetString(data);

        // Parse the SVG root element's width, height and viewBox attributes.
        var (svgWidth, svgHeight, vbRatio, hasDegenerateViewBox) = ParseSvgIntrinsicDimensions(svgContent);
        bool preserveAspectRatioNone = HasPreserveAspectRatioNone(svgContent);

        bool parsedIntrinsicWidth = svgWidth > 0;
        bool parsedIntrinsicHeight = svgHeight > 0;
        int width, height;
        // Chrome's SVG sizing for <img> elements: only when BOTH explicit
        // width and height attributes are present does the SVG have true
        // intrinsic dimensions and an intrinsic aspect ratio.  When either
        // dimension is missing Chrome falls back to the 300×150 default
        // object size, regardless of viewBox or partial attributes.
        bool hasBothDimensions = parsedIntrinsicWidth && parsedIntrinsicHeight;
        if (hasBothDimensions)
        {
            width = (int)Math.Ceiling(svgWidth);
            height = (int)Math.Ceiling(svgHeight);
        }
        else
        {
            width = 300;
            height = 150;
        }

        bool suppressPartialIntrinsicDimensions =
            preserveAspectRatioNone && vbRatio > 0 && !hasBothDimensions;

        int rasterScale = GetSvgRasterizationScale(width, height, hasBothDimensions, vbRatio);
        int rasterWidth = width * rasterScale;
        int rasterHeight = height * rasterScale;

        BBitmap? bitmap;
        if (hasDegenerateViewBox)
        {
            bitmap = new BBitmap(rasterWidth, rasterHeight);
            bitmap.Erase(BColor.Transparent);
        }
        else if (suppressPartialIntrinsicDimensions)
        {
            const int fallbackRenderScale = 4;
            int renderWidth = rasterWidth * fallbackRenderScale;
            int renderHeight = rasterHeight * fallbackRenderScale;
            bool shouldInjectViewportForMissingWidth = !parsedIntrinsicWidth && parsedIntrinsicHeight;
            if (shouldInjectViewportForMissingWidth)
            {
                var svgForRender = EnsureSvgViewport(svgContent, svgWidth, svgHeight, rasterWidth, rasterHeight);
                bitmap = RenderSvgToBitmap(svgForRender, renderWidth, renderHeight);
            }
            else
            {
                bitmap = RenderSvgToBitmap(svgContent, renderWidth, renderHeight);
            }
            if (bitmap != null && !IsBitmapFullyTransparent(bitmap))
            {
                bitmap = NormalizeSvgContentBounds(bitmap, renderWidth, renderHeight);
            }
            else
            {
                bitmap?.Dispose();
                var svgForRender = EnsureSvgViewport(svgContent, svgWidth, svgHeight, rasterWidth, rasterHeight);
                bitmap = RenderSvgToBitmap(svgForRender, renderWidth, renderHeight);
            }
        }
        else
        {
            // SVGs that use percentage-based dimensions internally (e.g.
            // width="100%" on child elements) need explicit viewport dimensions
            // on the root <svg> element for percentage resolution.  Inject the
            // computed width/height before parsing when the root element is
            // missing one or both attributes.
            var svgForRender = EnsureSvgViewport(svgContent, svgWidth, svgHeight, rasterWidth, rasterHeight);
            bitmap = RenderSvgToBitmap(svgForRender, rasterWidth, rasterHeight);
            if (bitmap != null
                && rasterScale > 1
                && preserveAspectRatioNone
                && vbRatio > 0)
            {
                bitmap = NormalizeSvgContentBounds(bitmap, rasterWidth, rasterHeight);
            }
        }

        if (bitmap == null)
            return null;

        if (!hasDegenerateViewBox && TryParseSolidViewportFill(svgContent, out var solidFill))
            bitmap.Erase(solidFill);

        // Only SVGs with both explicit width and height have intrinsic
        // dimensions.  A viewBox exposes an intrinsic ratio only when the
        // root element preserves aspect ratio; preserveAspectRatio="none"
        // allows non-uniform scaling and therefore does not contribute an
        // intrinsic ratio for CSS background-size calculations.
        bool hasIntrinsicRatio = hasBothDimensions || (vbRatio > 0 && !preserveAspectRatioNone);
        double intrinsicRatio = hasBothDimensions
            ? svgWidth / svgHeight
            : (preserveAspectRatioNone ? 0 : vbRatio);
        return new ImageAdapter(bitmap,
            hasIntrinsicRatio: hasIntrinsicRatio,
            hasIntrinsicWidth: parsedIntrinsicWidth && !suppressPartialIntrinsicDimensions,
            hasIntrinsicHeight: parsedIntrinsicHeight && !suppressPartialIntrinsicDimensions,
            intrinsicAspectRatio: intrinsicRatio > 0 ? intrinsicRatio : null,
            intrinsicWidth: parsedIntrinsicWidth && !suppressPartialIntrinsicDimensions ? svgWidth : 0,
            intrinsicHeight: parsedIntrinsicHeight && !suppressPartialIntrinsicDimensions ? svgHeight : 0);
    }

    private static int GetSvgRasterizationScale(int width, int height, bool hasBothDimensions, double viewBoxRatio)
    {
        if (!hasBothDimensions || viewBoxRatio <= 0)
            return 1;

        int longestSide = Math.Max(width, height);
        if (longestSide >= MinSvgRasterLongestSide)
            return 1;

        return Math.Clamp((int)Math.Ceiling((double)MinSvgRasterLongestSide / longestSide), 1, MaxSvgRasterScale);
    }

    private static BBitmap? RenderSvgToBitmap(string svgContent, int width, int height)
        => BSvgRasterizer.RasterizeToBitmap(svgContent, width, height);

    private static BBitmap NormalizeSvgContentBounds(BBitmap bitmap, int width, int height)
    {
        var rowHasOpaque = new bool[bitmap.Height];
        var colHasOpaque = new bool[bitmap.Width];
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                    continue;

                rowHasOpaque[y] = true;
                colHasOpaque[x] = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY
            || (minX == 0 && minY == 0 && maxX == bitmap.Width - 1 && maxY == bitmap.Height - 1))
            return bitmap;

        int croppedWidth = maxX - minX + 1;
        int croppedHeight = maxY - minY + 1;
        var nonEmptyCols = new List<int>(croppedWidth);
        for (int x = minX; x <= maxX; x++)
        {
            if (colHasOpaque[x])
                nonEmptyCols.Add(x);
        }

        var nonEmptyRows = new List<int>(croppedHeight);
        for (int y = minY; y <= maxY; y++)
        {
            if (rowHasOpaque[y])
                nonEmptyRows.Add(y);
        }

        if (nonEmptyCols.Count == 0 || nonEmptyRows.Count == 0)
            return bitmap;

        using var condensed = new BBitmap(nonEmptyCols.Count, nonEmptyRows.Count);
        for (int destY = 0; destY < nonEmptyRows.Count; destY++)
        {
            int srcY = nonEmptyRows[destY];
            for (int destX = 0; destX < nonEmptyCols.Count; destX++)
            {
                condensed.SetPixel(destX, destY, bitmap.GetPixel(nonEmptyCols[destX], srcY));
            }
        }

        var normalized = condensed.ResizeNearest(width, height);
        bitmap.Dispose();
        return normalized;
    }

    /// <summary>
    /// Ensures the SVG root element has explicit width and height attributes
    /// so that percentage-based child dimensions (e.g. width="100%") can
    /// resolve correctly.  Returns the original content unchanged when both
    /// attributes are already present.
    /// </summary>
    private static string EnsureSvgViewport(
        string svgContent,
        double parsedWidth, double parsedHeight,
        int targetWidth, int targetHeight)
    {
        // Both intrinsic dimensions are present – nothing to inject.
        if (parsedWidth > 0 && parsedHeight > 0)
            return svgContent;

        int svgIdx = svgContent.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgIdx < 0)
            return svgContent;

        int tagEnd = svgContent.IndexOf('>', svgIdx);
        if (tagEnd < 0)
            return svgContent;

        var tag = svgContent.Substring(svgIdx, tagEnd - svgIdx + 1);
        var updatedTag = tag;
        bool needsViewportDimensions = parsedWidth <= 0 || parsedHeight <= 0;
        if (needsViewportDimensions)
        {
            // When either intrinsic dimension is missing, SVG rasterization needs a
            // concrete viewport size for percentage children. Preserve any
            // parsed absolute dimension on the other axis, but always inject
            // an explicit width/height pair so partial-dimension SVGs have a
            // concrete viewport.
            int effectiveWidth = parsedWidth > 0
                ? (int)Math.Ceiling(parsedWidth)
                : targetWidth;
            updatedTag = System.Text.RegularExpressions.Regex.Replace(
                updatedTag,
                @"\swidth\s*=\s*[""'][^""']*[""']",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            updatedTag = updatedTag.Insert(updatedTag.Length - 1, $" width=\"{effectiveWidth}\"");
        }

        if (needsViewportDimensions)
        {
            int effectiveHeight = parsedHeight > 0
                ? (int)Math.Ceiling(parsedHeight)
                : targetHeight;
            updatedTag = System.Text.RegularExpressions.Regex.Replace(
                updatedTag,
                @"\sheight\s*=\s*[""'][^""']*[""']",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            updatedTag = updatedTag.Insert(updatedTag.Length - 1, $" height=\"{effectiveHeight}\"");
        }

        return svgContent.Substring(0, svgIdx) + updatedTag + svgContent[(tagEnd + 1)..];
    }

    /// <summary>
    /// Parses the SVG root element to extract intrinsic width, height and
    /// viewBox aspect ratio.  Returns (width, height, viewBoxRatio,
    /// hasDegenerateViewBox) where
    /// values ≤ 0 indicate the attribute was absent or non-numeric.
    /// </summary>
    private static (double width, double height, double viewBoxRatio, bool hasDegenerateViewBox) ParseSvgIntrinsicDimensions(string svg)
    {
        double w = -1, h = -1, ratio = -1;
        bool hasDegenerateViewBox = false;

        // Find the <svg ...> opening tag.
        int svgIdx = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgIdx < 0) return (w, h, ratio, hasDegenerateViewBox);
        int tagEnd = svg.IndexOf('>', svgIdx);
        if (tagEnd < 0) return (w, h, ratio, hasDegenerateViewBox);
        var tag = svg.Substring(svgIdx, tagEnd - svgIdx + 1);

        // Parse width attribute (only absolute units / plain numbers).
        w = ParseSvgLengthAttribute(tag, "width");
        h = ParseSvgLengthAttribute(tag, "height");

        // Parse viewBox for aspect ratio.
        var vbMatch = System.Text.RegularExpressions.Regex.Match(
            tag, @"viewBox\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (vbMatch.Success)
        {
            var parts = vbMatch.Groups[1].Value.Split(
                new[] { ' ', ',', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double vbW)
                && double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double vbH))
            {
                if (vbW > 0 && vbH > 0)
                    ratio = vbW / vbH;
                else
                    hasDegenerateViewBox = true;
            }
        }

        return (w, h, ratio, hasDegenerateViewBox);
    }

    private static bool HasPreserveAspectRatioNone(string svg)
    {
        int svgIdx = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgIdx < 0) return false;
        int tagEnd = svg.IndexOf('>', svgIdx);
        if (tagEnd < 0) return false;
        var tag = svg.Substring(svgIdx, tagEnd - svgIdx + 1);

        var m = System.Text.RegularExpressions.Regex.Match(
            tag,
            @"(?<!\w)preserveAspectRatio\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        return m.Groups[1].Value.Trim().StartsWith("none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts a numeric length value from an SVG attribute.  Returns the
    /// value in pixels for plain numbers and "px" units; returns -1 for
    /// percentage values or absent attributes.
    /// </summary>
    private static double ParseSvgLengthAttribute(string tag, string name)
    {
        // Match name="value" but avoid matching longer attribute names
        // (e.g. "width" should not match "stroke-width").
        var m = System.Text.RegularExpressions.Regex.Match(
            tag, @"(?<!\w)" + name + @"\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return -1;

        var val = m.Groups[1].Value.Trim();
        // Ignore percentage values – they are not intrinsic dimensions.
        if (val.EndsWith('%')) return -1;
        // Strip "px" suffix.
        if (val.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            val = val[..^2];

        return double.TryParse(val, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? v : -1;
    }

    private static bool IsBitmapFullyTransparent(BBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0)
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseSolidViewportFill(string svgContent, out BColor color)
    {
        color = BColor.Transparent;

        var rectMatches = System.Text.RegularExpressions.Regex.Matches(
            svgContent,
            @"<rect\b[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (rectMatches.Count != 1)
            return false;

        var rectTag = rectMatches[0].Value;
        bool fillsViewport =
            System.Text.RegularExpressions.Regex.IsMatch(rectTag, @"\bwidth\s*=\s*[""']100%[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(rectTag, @"\bheight\s*=\s*[""']100%[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!fillsViewport)
            return false;

        var fillMatch = System.Text.RegularExpressions.Regex.Match(
            rectTag,
            @"\bfill\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!fillMatch.Success)
            return false;

        var fillValue = fillMatch.Groups[1].Value.Trim();

        // Managed parse (hex or known CSS/.NET color name); avoids the OS-dependent
        // System.Drawing.ColorTranslator.
        if (TryParseHexColor(fillValue, out var hex))
        {
            color = new BColor(hex.R, hex.G, hex.B, hex.A);
            return true;
        }

        var named = Color.FromName(fillValue);
        if (named.IsKnownColor)
        {
            color = new BColor(named.R, named.G, named.B, named.A);
            return true;
        }

        return false;
    }

    protected override RFont CreateFontInt(string family, double size, FontStyle style)
    {
        return new FontAdapter(family, size, style, () => _typefaceResolver.ResolveTypeface(family, style));
    }

    protected override RFont CreateFontInt(RFontFamily family, double size, FontStyle style) => CreateFontInt(family.Name, size, style);

    protected override object GetClipboardDataObjectInt(string html, string plainText) =>
        new ClipboardPayload(html, plainText);

    protected override void SetToClipboardInt(string text) =>
        LastClipboardPayload = new ClipboardPayload(null, text);

    protected override void SetToClipboardInt(string html, string plainText) =>
        LastClipboardPayload = new ClipboardPayload(html, plainText);

    protected override void SetToClipboardInt(RImage image) =>
        LastClipboardPayload = image;

    protected override RContextMenu CreateContextMenuInt() => new StubContextMenuAdapter();

    internal static object LastClipboardPayload { get; private set; }

    private sealed record ClipboardPayload(string Html, string PlainText);

    private sealed class StubContextMenuAdapter : RContextMenu
    {
        private int _itemsCount;

        public override int ItemsCount => _itemsCount;

        public override void AddDivider() => _itemsCount++;

        public override void AddItem(string text, bool enabled, EventHandler onClick) => _itemsCount++;

        public override void RemoveLastDivider()
        {
            if (_itemsCount > 0)
                _itemsCount--;
        }

        public override void Show(RControl parent, PointF location)
        {
            // Platform-neutral frontend: host applications can implement their own menu.
        }

        public override void Dispose()
        {
        }
    }
}
