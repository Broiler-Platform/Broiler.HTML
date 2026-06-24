using Broiler.HTML.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class FontFamilyAdapter(string familyName) : RFontFamily
{
    public override string Name => familyName;
}
