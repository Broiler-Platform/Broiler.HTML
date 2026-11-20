using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Broiler.Graphics.Color;
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

    IBitmapCompatSurface CreateBitmapCompatSurface(int width, int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object? initialBitmap = null,
        bool ownsBitmap = true);
}

internal static class CompatProvider
{
    private const string CompatAssemblyName = "Broiler.HTML.Image.Compat";
    private const string CompatBootstrapperTypeName = "Broiler.HTML.Image.Compat.StubCompatBootstrapper";
    private const string CompatBootstrapperMethodName = "EnsureRegistered";

    private static readonly object DefaultProviderSync = new();
    private static ICompatProvider? _defaultProvider;
    private static int _defaultLoadAttempted;

    internal static RAdapter ImageAdapter => Current.ImageAdapter;

    internal static ITextShaper TextShaper => Current.TextShaper;

    internal static ICanvasCompat CanvasCompat => Current.CanvasCompat;

    internal static IPathCompat PathCompat => Current.PathCompat;

    internal static IFontCompatFactory FontCompatFactory => Current.FontCompatFactory;

    internal static IPaintCompatFactory PaintCompatFactory => Current.PaintCompatFactory;

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

    internal static void InitializeDefaultIfUnset(ICompatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Interlocked.CompareExchange(ref _defaultProvider, provider, null);
    }

    private static ICompatProvider Current => EnsureDefaultProvider();

    private static ICompatProvider EnsureDefaultProvider()
    {
        if (_defaultProvider is not null)
            return _defaultProvider;

        TryLoadDefaultProvider();
        return _defaultProvider
            ?? throw new InvalidOperationException(
                "No compatibility provider is registered. Reference Broiler.HTML.Image.Compat and call StubCompatBootstrapper.EnsureRegistered() when publishing with NativeAOT.");
    }

    private static void TryLoadDefaultProvider()
    {
        if (_defaultProvider is not null)
            return;

        lock (DefaultProviderSync)
        {
            if (_defaultProvider is not null || _defaultLoadAttempted != 0)
                return;

            _defaultLoadAttempted = 1;
            if (!RuntimeFeature.IsDynamicCodeSupported)
                return;

            TryLoadDynamicCompatProvider();
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Dynamic fallback is only executed when RuntimeFeature.IsDynamicCodeSupported is true; NativeAOT callers register StubCompatBootstrapper statically.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "Dynamic fallback is only executed when RuntimeFeature.IsDynamicCodeSupported is true; NativeAOT callers register StubCompatBootstrapper statically.")]
    private static void TryLoadDynamicCompatProvider()
    {
        var assembly = Assembly.Load(CompatAssemblyName);
        var bootstrapperType = assembly.GetType(CompatBootstrapperTypeName, throwOnError: true)
            ?? throw new InvalidOperationException($"Could not find {CompatBootstrapperTypeName} in {CompatAssemblyName}.");
        var method = bootstrapperType.GetMethod(
            CompatBootstrapperMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find {CompatBootstrapperTypeName}.{CompatBootstrapperMethodName}.");
        method.Invoke(null, null);
    }
}
