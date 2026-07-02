namespace Broiler.HTML.Image.Compat;

/// <summary>
/// Entry point discovered by reflection from <c>Broiler.HTML.Image</c> to install
/// the default compatibility provider. The former GDI+ implementation has been
/// replaced by an OS-free stub backend.
/// </summary>
public static class StubCompatBootstrapper
{
    public static void EnsureRegistered() => CompatProvider.InitializeDefault(new StubCompatProvider());
}
