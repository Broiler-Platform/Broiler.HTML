namespace Broiler.HTML.Image;

/// <summary>
/// Broiler-owned RGBA color used to reduce direct SkiaSharp exposure in
/// rendering-facing APIs.
/// </summary>
public readonly record struct BColor(byte R, byte G, byte B, byte A = byte.MaxValue)
{
    /// <summary>
    /// Compatibility alias for <see cref="R"/>. Prefer <see cref="R"/> in new code.
    /// </summary>
    public byte Red => R;

    /// <summary>
    /// Compatibility alias for <see cref="G"/>. Prefer <see cref="G"/> in new code.
    /// </summary>
    public byte Green => G;

    /// <summary>
    /// Compatibility alias for <see cref="B"/>. Prefer <see cref="B"/> in new code.
    /// </summary>
    public byte Blue => B;

    /// <summary>
    /// Compatibility alias for <see cref="A"/>. Prefer <see cref="A"/> in new code.
    /// </summary>
    public byte Alpha => A;

    public static BColor White { get; } = new(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

    public static BColor Transparent { get; } = new(0, 0, 0, 0);
}
