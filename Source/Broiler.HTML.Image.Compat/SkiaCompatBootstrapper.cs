namespace Broiler.HTML.Image.Compat;

public static class SkiaCompatBootstrapper
{
    public static void EnsureRegistered() =>
        Broiler.HTML.Image.SkiaCompatProvider.InitializeDefault(new Broiler.HTML.Image.DefaultSkiaCompatProvider());
}
