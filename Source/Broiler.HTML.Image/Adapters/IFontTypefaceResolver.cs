namespace Broiler.HTML.Image.Adapters;

internal interface IFontTypefaceResolver
{
    string RegisterFontFile(string path, string alias = null);

    bool HasDeferredLoadedTypefacePath(string family);

    bool HasMaterializedLoadedTypeface(string family);

    object ResolveTypeface(string family, Graphics.FontStyle style);
}
