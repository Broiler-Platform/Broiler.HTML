using System;
using System.Globalization;
using System.Reflection;

namespace Broiler.HTML.Image;

internal static class CompatCanvasOperations
{
    internal static void Save(object canvas) => InvokeVoid(canvas, "Save");

    internal static void Restore(object canvas) => InvokeVoid(canvas, "Restore");

    internal static void Translate(object canvas, float x, float y) => InvokeVoid(canvas, "Translate", x, y);

    private static void InvokeVoid(object canvas, string methodName, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var types = new Type[args.Length];
        for (int i = 0; i < args.Length; i++)
            types[i] = args[i].GetType();

        var method = canvas.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types,
            modifiers: null);

        if (method is null)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "Canvas compat object '{0}' does not expose method '{1}'.",
                canvas.GetType().FullName,
                methodName));
        }

        method.Invoke(canvas, args);
    }
}
