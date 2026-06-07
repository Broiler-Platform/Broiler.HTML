using System;
using System.Reflection;
using System.Threading;
using Broiler.HTML.Adapters;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

internal interface ICompatProvider
{
    RAdapter ImageAdapter { get; }

    ITextShaper TextShaper { get; }

    ICanvasCompat CanvasCompat { get; }

    IPathCompat PathCompat { get; }

    IFontCompatFactory FontCompatFactory { get; }

    IPaintCompatFactory PaintCompatFactory { get; }

    IFontTypefaceResolver CreateFontTypefaceResolver();

    IBitmapCompatSurface CreateBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object? initialBitmap = null,
        bool ownsBitmap = true);
}

internal static class CompatProvider
{
    private const string CompatAssemblyName = "Broiler.HTML.Image.Compat";
    private const string CompatBootstrapperTypeName = "Broiler.HTML.Image.Compat.GdiCompatBootstrapper";
    private const string CompatBootstrapperMethodName = "EnsureRegistered";

    private static readonly AsyncLocal<ICompatProvider?> ProviderOverride = new();
    private static ICompatProvider? _defaultProvider;
    private static int _defaultLoadAttempted;

    internal static RAdapter ImageAdapter => Current.ImageAdapter;

    internal static ITextShaper TextShaper => Current.TextShaper;

    internal static ICanvasCompat CanvasCompat => Current.CanvasCompat;

    internal static IPathCompat PathCompat => Current.PathCompat;

    internal static IFontCompatFactory FontCompatFactory => Current.FontCompatFactory;

    internal static IPaintCompatFactory PaintCompatFactory => Current.PaintCompatFactory;

    internal static IDisposable OverrideForCurrentThread(ICompatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var previous = ProviderOverride.Value;
        ProviderOverride.Value = provider;
        return new ProviderOverrideScope(previous);
    }

    internal static IFontTypefaceResolver CreateFontTypefaceResolver() =>
        Current.CreateFontTypefaceResolver();

    internal static IBitmapCompatSurface CreateBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object? initialBitmap = null,
        bool ownsBitmap = true) =>
        Current.CreateBitmapCompatSurface(width, height, readPrimaryPixel, writePrimaryPixel, initialBitmap, ownsBitmap);

    internal static void InitializeDefault(ICompatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _defaultProvider = provider;
    }

    private static ICompatProvider Current => ProviderOverride.Value ?? EnsureDefaultProvider();

    private static ICompatProvider EnsureDefaultProvider()
    {
        if (_defaultProvider is not null)
            return _defaultProvider;

        TryLoadDefaultProvider();
        return _defaultProvider
            ?? throw new InvalidOperationException(
                "No compatibility provider is registered. Reference Broiler.HTML.Image.Compat so the default provider can be loaded.");
    }

    private static void TryLoadDefaultProvider()
    {
        if (Interlocked.Exchange(ref _defaultLoadAttempted, 1) != 0)
            return;

        var assembly = Assembly.Load(CompatAssemblyName);
        var bootstrapperType = assembly.GetType(CompatBootstrapperTypeName, throwOnError: true)
            ?? throw new InvalidOperationException($"Could not find {CompatBootstrapperTypeName} in {CompatAssemblyName}.");
        var method = bootstrapperType.GetMethod(
            CompatBootstrapperMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find {CompatBootstrapperTypeName}.{CompatBootstrapperMethodName}.");
        method.Invoke(null, null);
    }

    private sealed class ProviderOverrideScope(ICompatProvider? previous) : IDisposable
    {
        public void Dispose() => ProviderOverride.Value = previous;
    }
}
