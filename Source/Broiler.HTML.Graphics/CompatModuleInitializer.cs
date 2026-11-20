using System.Runtime.CompilerServices;
using Broiler.HTML.Image.Compat;

namespace Broiler.HTML.Graphics;

public static class HtmlGraphicsRuntime
{
    [ModuleInitializer]
    internal static void Initialize() => EnsureInitialized();

    public static void EnsureInitialized() => StubCompatBootstrapper.EnsureRegistered();
}
