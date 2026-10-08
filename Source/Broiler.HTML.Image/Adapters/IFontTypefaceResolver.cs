using Broiler.Graphics.Text;

namespace Broiler.HTML.Image.Adapters;

internal interface IFontTypefaceResolver
{
    string? RegisterFontFile(string path, string? alias = null);

    object ResolveTypeface(string family, FontStyle style);

    /// <summary>
    /// The program to carry to a render backend for <paramref name="typeface"/> (one
    /// <see cref="ResolveTypeface"/> answered) when it is a font registered at runtime — an
    /// <c>@font-face</c> web font, or a file the host loaded — which no backend can find by family
    /// name; <see langword="null"/> for an installed or bundled face.
    /// </summary>
    BFontFace? GetRegisteredFace(object typeface);
}
