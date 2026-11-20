using System;
using System.Globalization;

namespace Broiler.HTML.Image;

internal static class CompatCanvasOperations
{
    internal static void Save(object canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (canvas is ICompatCanvas compatCanvas)
        {
            compatCanvas.Save();
            return;
        }

        throw CreateUnsupportedCanvasException(canvas, nameof(Save));
    }

    internal static void Restore(object canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (canvas is ICompatCanvas compatCanvas)
        {
            compatCanvas.Restore();
            return;
        }

        throw CreateUnsupportedCanvasException(canvas, nameof(Restore));
    }

    internal static void Translate(object canvas, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (canvas is ICompatCanvas compatCanvas)
        {
            compatCanvas.Translate(x, y);
            return;
        }

        throw CreateUnsupportedCanvasException(canvas, nameof(Translate));
    }

    private static InvalidOperationException CreateUnsupportedCanvasException(object canvas, string methodName) =>
        new(string.Format(
            CultureInfo.InvariantCulture,
            "Canvas compat object '{0}' does not implement ICompatCanvas for operation '{1}'.",
            canvas.GetType().FullName,
            methodName));
}
