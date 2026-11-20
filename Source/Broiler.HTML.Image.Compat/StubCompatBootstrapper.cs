using System.Runtime.CompilerServices;

namespace Broiler.HTML.Image.Compat;

/// <summary>
/// Entry point to install the default compatibility provider. The former GDI+
/// implementation has been replaced by an OS-free stub backend.
/// </summary>
public static class StubCompatBootstrapper
{
    [ModuleInitializer]
    internal static void InitializeModule() => EnsureRegistered();

    public static void EnsureRegistered() => CompatProvider.InitializeDefaultIfUnset(new StubCompatProvider());
}
