using Broiler.Graphics.Text;

namespace Broiler.HTML.Image.Adapters;

internal interface IFontTypefaceResolver
{
    string RegisterFontFile(string path, string alias = null);

    object ResolveTypeface(string family, FontStyle style);
}
