using Broiler.Graphics;
using Broiler.Graphics.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class FontFamilyAdapter(string familyName) : BFontFamily
{
    public override string Name => familyName;
}
