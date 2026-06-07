namespace Broiler.HTML.Image.Compat;

public static class GdiCompatBootstrapper
{
    public static void EnsureRegistered() =>
        Broiler.HTML.Image.CompatProvider.InitializeDefault(new Broiler.HTML.Image.GdiCompatProvider());
}
