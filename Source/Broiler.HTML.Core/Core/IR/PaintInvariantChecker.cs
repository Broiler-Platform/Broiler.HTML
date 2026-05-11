using System.Collections.Generic;

namespace Broiler.HTML.Core.Core.IR;

/// <summary>
/// Walks a <see cref="DisplayList"/> and checks structural paint
/// invariants. Returns violations as plain strings — no test-framework
/// dependency.
/// </summary>
public static class PaintInvariantChecker
{
    /// <summary>
    /// Checks all paint invariants on the given display list.
    /// Returns a list of violation descriptions (empty = valid).
    /// </summary>
    public static IReadOnlyList<string> Check(DisplayList displayList)
    {
        var violations = new List<string>();

        int clipDepth = 0;

        for (int i = 0; i < displayList.Items.Count; i++)
        {
            var item = displayList.Items[i];
            var path = $"Items[{i}]";

            // Common: Bounds coordinates must be finite
            CheckFinite(item.Bounds.X, $"{path}.Bounds.X", violations);
            CheckFinite(item.Bounds.Y, $"{path}.Bounds.Y", violations);
            CheckFinite(item.Bounds.Width, $"{path}.Bounds.Width", violations);
            CheckFinite(item.Bounds.Height, $"{path}.Bounds.Height", violations);

            // Common: No negative dimensions on rect-based items
            if (item is FillRectItem or DrawBorderItem or DrawImageItem)
            {
                if (item.Bounds.Width < 0)
                    violations.Add($"{path}.Bounds.Width is negative ({item.Bounds.Width})");
                if (item.Bounds.Height < 0)
                    violations.Add($"{path}.Bounds.Height is negative ({item.Bounds.Height})");
            }

            switch (item)
            {
                case ClipItem clip:
                    clipDepth++;
                    CheckFinite(clip.ClipRect.X, $"{path}.ClipRect.X", violations);
                    CheckFinite(clip.ClipRect.Y, $"{path}.ClipRect.Y", violations);
                    CheckFinite(clip.ClipRect.Width, $"{path}.ClipRect.Width", violations);
                    CheckFinite(clip.ClipRect.Height, $"{path}.ClipRect.Height", violations);
                    if (clip.ClipRect.Width < 0)
                        violations.Add($"{path}.ClipRect.Width is negative ({clip.ClipRect.Width})");
                    if (clip.ClipRect.Height < 0)
                        violations.Add($"{path}.ClipRect.Height is negative ({clip.ClipRect.Height})");
                    break;

                case RestoreItem:
                    if (clipDepth <= 0)
                        violations.Add($"{path}: RestoreItem without matching ClipItem (unbalanced)");
                    else
                        clipDepth--;
                    break;

                case DrawTextItem text:
                    if (string.IsNullOrEmpty(text.Text))
                        violations.Add($"{path}: DrawTextItem has empty Text");
                    if (text.FontSize <= 0)
                        violations.Add($"{path}: DrawTextItem.FontSize is not positive ({text.FontSize})");
                    CheckFinite(text.FontSize, $"{path}.FontSize", violations);
                    CheckFinite(text.Origin.X, $"{path}.Origin.X", violations);
                    CheckFinite(text.Origin.Y, $"{path}.Origin.Y", violations);
                    break;

                case OpacityItem opacity:
                    CheckFinite(opacity.Opacity, $"{path}.Opacity", violations);
                    break;

                case DrawLineItem line:
                    CheckFinite(line.Start.X, $"{path}.Start.X", violations);
                    CheckFinite(line.Start.Y, $"{path}.Start.Y", violations);
                    CheckFinite(line.End.X, $"{path}.End.X", violations);
                    CheckFinite(line.End.Y, $"{path}.End.Y", violations);
                    CheckFinite(line.Width, $"{path}.Width", violations);
                    break;

                case DrawImageItem image:
                    CheckFinite(image.SourceRect.X, $"{path}.SourceRect.X", violations);
                    CheckFinite(image.SourceRect.Y, $"{path}.SourceRect.Y", violations);
                    CheckFinite(image.SourceRect.Width, $"{path}.SourceRect.Width", violations);
                    CheckFinite(image.SourceRect.Height, $"{path}.SourceRect.Height", violations);
                    CheckFinite(image.DestRect.X, $"{path}.DestRect.X", violations);
                    CheckFinite(image.DestRect.Y, $"{path}.DestRect.Y", violations);
                    CheckFinite(image.DestRect.Width, $"{path}.DestRect.Width", violations);
                    CheckFinite(image.DestRect.Height, $"{path}.DestRect.Height", violations);
                    break;
            }
        }

        // After iteration, all clips must be balanced
        if (clipDepth > 0)
            violations.Add($"Unbalanced clip stack: {clipDepth} ClipItem(s) without matching RestoreItem");

        return violations;
    }

    private static void CheckFinite(float value, string name, List<string> violations)
    {
        if (float.IsNaN(value))
            violations.Add($"{name} is NaN");
        else if (float.IsInfinity(value))
            violations.Add($"{name} is Infinity");
    }
}
