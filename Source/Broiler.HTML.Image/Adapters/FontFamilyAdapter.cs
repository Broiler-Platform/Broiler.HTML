using Broiler.Graphics;

namespace Broiler.HTML.Image.Adapters;

internal sealed class FontFamilyAdapter(string familyName) : RFontFamily
{
    public override string Name => familyName;
}
