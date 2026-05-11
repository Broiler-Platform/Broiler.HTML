using Broiler.HTML.Adapters.Adapters;
using System.Globalization;
using System.Linq;
using System.Windows.Markup;
using System.Windows.Media;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class FontFamilyAdapter(FontFamily fontFamily) : RFontFamily
{
    private static readonly XmlLanguage _xmlLanguage = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

    public FontFamily FontFamily { get; } = fontFamily;

    public override string Name => FontFamily.FamilyNames.TryGetValue(_xmlLanguage, out var name) ? name : FontFamily.FamilyNames.FirstOrDefault().Value;
}