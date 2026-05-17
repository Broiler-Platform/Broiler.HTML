using Broiler.HTML.Core.Core.IR;
using Broiler.HTML.Dom.Core.Dom;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace Broiler.HTML.Orchestration.Core.IR;

/// <summary>
/// Walks a <see cref="CssBox"/> tree after layout and builds a read-only
/// <see cref="Fragment"/> tree that snapshots the layout geometry.
/// </summary>
internal static class FragmentTreeBuilder
{
    /// <summary>
    /// Builds a <see cref="Fragment"/> tree from the given root <see cref="CssBox"/>.
    /// Should be called after <c>PerformLayout</c> has completed.
    /// </summary>
    public static Fragment Build(CssBox root) => BuildFragment(root, parentHasTransform: false);

    private static Fragment BuildFragment(CssBox box, bool parentHasTransform)
    {
        var style = ComputedStyleBuilder.FromBox(box, box.HtmlTag?.Name);
        bool hasTransformAncestor = parentHasTransform
            || (!string.IsNullOrEmpty(style.Transform)
            && !style.Transform.Equals("none", StringComparison.OrdinalIgnoreCase));

        var children = new List<Fragment>(box.Boxes.Count);
        foreach (var child in box.Boxes)
            children.Add(BuildFragment(child, hasTransformAncestor));

        List<LineFragment>? lines = null;
        if (box.LineBoxes.Count > 0)
        {
            lines = new List<LineFragment>(box.LineBoxes.Count);
            foreach (var lineBox in box.LineBoxes)
                lines.Add(BuildLineFragment(lineBox));
        }

        // Phase 3: Capture background image handle for the new paint path
        object? bgImage = box.LoadedBackgroundImage;

        // Phase 3: Capture replaced image handle (e.g. <img> elements)
        object? imgHandle = null;
        RectangleF imgSourceRect = RectangleF.Empty;
        string svgContent = null;
        if (box is CssBoxImage imgBox)
        {
            imgHandle = imgBox.Image;
            // CssBoxImage stores source rect on its internal CssRectImage word
            if (imgBox.Words.Count > 0 && imgBox.Words[0] is CssRectImage rectImage)
                imgSourceRect = rectImage.ImageRectangle;
        }

        // Check for <object> elements referencing SVG content.  When the
        // image loader cannot decode the data (e.g. SVG, which is not a
        // raster format), imgHandle will be null.  If the data attribute
        // ends with ".svg" or is a data:image/svg+xml URI, try to load
        // the SVG content so that PaintWalker can render it via SvgRenderer.
        if (imgHandle == null && box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            string dataAttr = box.GetAttribute("data");
            if (!string.IsNullOrEmpty(dataAttr))
            {
                svgContent = TryLoadSvgContent(dataAttr, box.BaseUrl);
            }
        }

        // Inline <svg> elements: serialise the SVG subtree back to markup
        // so that PaintWalker can render it via SvgRenderer.
        if (svgContent == null && box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
        {
            svgContent = SerializeSvgSubtree(box);
        }

        // Capture per-line-box rectangles for inline elements (used for backgrounds/borders)
        List<RectangleF>? inlineRects = null;
        if (box.Rectangles.Count > 0)
        {
            inlineRects = new List<RectangleF>(box.Rectangles.Values);
        }

        // CssBox.Size.Height is never set for block-level boxes during layout;
        // the actual rendered height is tracked via ActualBottom instead.
        // Compute the correct border-box height so that PaintWalker can
        // draw backgrounds and borders (which skip Height <= 0 rects).
        var size = box.Size;

        // Sanitise NaN width: auto-width absolutely positioned elements
        // may still have NaN if ComputeShrinkToFitWidth could not resolve
        // a finite value (e.g. deeply nested inline objects).  Fall back
        // to ActualRight - Location.X which is layout-computed.
        if (float.IsNaN(size.Width))
        {
            float layoutWidth = (float)(box.ActualRight - box.Location.X);
            size = new SizeF(layoutWidth > 0 ? layoutWidth : 0, size.Height);
        }

        float layoutHeight = (float)(box.ActualBottom - box.Location.Y);
        if (layoutHeight > size.Height)
            size = new SizeF(size.Width, layoutHeight);

        return new Fragment
        {
            Location = box.Location,
            Size = size,
            Margin = style.Margin,
            Border = style.Border,
            Padding = style.Padding,
            Lines = lines,
            Children = children,
            Style = style,
            CreatesStackingContext = IsStackingContext(box),
            StackLevel = GetStackLevel(box),
            HasTransformAncestor = hasTransformAncestor,
            BackgroundImageHandle = bgImage,
            ImageHandle = imgHandle,
            ImageSourceRect = imgSourceRect,
            SvgContent = svgContent,
            InlineRects = inlineRects,
        };
    }

    private static LineFragment BuildLineFragment(CssLineBox lineBox)
    {
        var inlines = new List<InlineFragment>();

        foreach (var word in lineBox.Words)
        {
            var ownerStyle = ComputedStyleBuilder.FromBox(word.OwnerBox);
            inlines.Add(new InlineFragment
            {
                X = (float)word.Left,
                Y = (float)word.Top,
                Width = (float)word.Width,
                Height = (float)word.Height,
                Text = word.IsSpaces
                    ? (word.OwnerBox.WhiteSpace is CssConstants.Pre or CssConstants.PreWrap
                        ? word.Text   // CSS2.1 §16.6: preserve space sequences in pre/pre-wrap
                        : " ")
                    : word.Text,
                Style = ownerStyle,
                FontHandle = word.OwnerBox.ActualFont,
                Selected = word.Selected,
                SelectedStartOffset = word.SelectedStartOffset,
                SelectedEndOffset = word.SelectedEndOffset,
            });
        }

        // Compute line bounds from all rectangles in this line box
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxR = float.MinValue, maxB = float.MinValue;

        foreach (var rect in lineBox.Rectangles.Values)
        {
            if (rect.X < minX) minX = rect.X;
            if (rect.Y < minY) minY = rect.Y;
            if (rect.Right > maxR) maxR = rect.Right;
            if (rect.Bottom > maxB) maxB = rect.Bottom;
        }

        if (lineBox.Rectangles.Count == 0)
            minX = minY = maxR = maxB = 0;

        return new LineFragment
        {
            X = minX,
            Y = minY,
            Width = maxR - minX,
            Height = maxB - minY,
            Baseline = 0,
            Inlines = inlines,
        };
    }

    private static bool IsStackingContext(CssBox box)
    {
        // A box creates a stacking context if it is positioned with a z-index,
        // or has opacity < 1, or is a fixed/absolute-positioned element.
        if (box.Position == CssConstants.Absolute || box.Position == CssConstants.Fixed)
            return true;

        // CSS2.1 §9.9.1: A positioned element with a computed z-index
        // other than 'auto' establishes a new stacking context.
        if (box.Position == CssConstants.Relative
            && box.ZIndex != null && box.ZIndex != CssConstants.Auto
            && int.TryParse(box.ZIndex, out _))
            return true;

        if (double.TryParse(box.Opacity, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var opacity) && opacity < 1.0)
            return true;

        // CSS Compositing §3: Elements with a mix-blend-mode other than 'normal'
        // must create a stacking context.
        if (!string.IsNullOrEmpty(box.MixBlendMode)
            && !box.MixBlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Compositing §2.2: 'isolation: isolate' creates a stacking context.
        if (!string.IsNullOrEmpty(box.Isolation)
            && box.Isolation.Equals("isolate", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Filter Effects §2: A filter other than 'none' creates a stacking context.
        if (!string.IsNullOrEmpty(box.Filter)
            && !box.Filter.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        // CSS Transforms §6.1: An element with a transform other than 'none'
        // creates a stacking context and a containing block.
        if (!string.IsNullOrEmpty(box.Transform)
            && !box.Transform.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Returns the computed stack level (z-index) for a box.
    /// CSS2.1 §9.9.1: 'auto' computes to 0 for painting order.
    /// </summary>
    private static int GetStackLevel(CssBox box)
    {
        if (box.ZIndex != null && box.ZIndex != CssConstants.Auto && int.TryParse(box.ZIndex, out int z))
            return z;

        return 0;
    }

    /// <summary>
    /// Attempts to load SVG content from a <c>data</c> attribute value.
    /// Supports <c>data:image/svg+xml</c> URIs and local <c>.svg</c> file references.
    /// </summary>
    private static string TryLoadSvgContent(string dataAttr, Uri baseUrl)
    {
        // data:image/svg+xml,<svg>...</svg>
        const string svgDataPrefix = "data:image/svg+xml";
        if (dataAttr.StartsWith(svgDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int comma = dataAttr.IndexOf(',');
            if (comma >= 0 && comma + 1 < dataAttr.Length)
                return Uri.UnescapeDataString(dataAttr[(comma + 1)..]);

            // base64 variant
            int semi = dataAttr.IndexOf(';');
            if (semi >= 0)
            {
                string encoding = dataAttr[(semi + 1)..];
                int commaB64 = encoding.IndexOf(',');
                if (commaB64 >= 0 && encoding[..commaB64].Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(encoding[(commaB64 + 1)..]);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    catch { /* invalid base64 — fall through */ }
                }
            }

            return null;
        }

        // Local .svg file reference
        if (dataAttr.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && baseUrl != null)
        {
            try
            {
                string basePath = baseUrl.IsAbsoluteUri && baseUrl.IsFile
                    ? baseUrl.LocalPath
                    : baseUrl.OriginalString;
                string dir = Path.GetDirectoryName(basePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string svgPath = Path.GetFullPath(Path.Combine(dir, dataAttr));
                    if (File.Exists(svgPath))
                        return File.ReadAllText(svgPath);
                }
            }
            catch { /* path resolution failure — skip */ }
        }

        return null;
    }

    /// <summary>
    /// Serialises an inline <c>&lt;svg&gt;</c> CssBox subtree back to SVG
    /// markup so that <see cref="PaintWalker"/> can render it via
    /// <see cref="SvgRenderer"/>.
    /// </summary>
    private static string SerializeSvgSubtree(CssBox svgBox)
    {
        var sb = new StringBuilder();
        SerializeSvgBox(svgBox, sb);
        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static void SerializeSvgBox(CssBox box, StringBuilder sb)
    {
        if (box.HtmlTag != null)
        {
            sb.Append('<').Append(box.HtmlTag.Name);
            if (box.HtmlTag.HasAttributes())
            {
                foreach (var attr in box.HtmlTag.Attributes)
                {
                    sb.Append(' ').Append(attr.Key).Append("=\"");
                    AppendXmlEscaped(sb, attr.Value);
                    sb.Append('"');
                }
            }

            if (box.HtmlTag.IsSingle)
            {
                sb.Append("/>");
                return;
            }

            sb.Append('>');
        }

        if (!box.Text.IsEmpty)
            AppendXmlEscaped(sb, box.Text.ToString());

        foreach (var child in box.Boxes)
            SerializeSvgBox(child, sb);

        if (box.HtmlTag != null && !box.HtmlTag.IsSingle)
            sb.Append("</").Append(box.HtmlTag.Name).Append('>');
    }

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="sb"/>, escaping
    /// the five XML special characters: <c>&amp;</c>, <c>&lt;</c>,
    /// <c>&gt;</c>, <c>&quot;</c>, and <c>&apos;</c>.
    /// </summary>
    private static void AppendXmlEscaped(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&':  sb.Append("&amp;");  break;
                case '<':  sb.Append("&lt;");   break;
                case '>':  sb.Append("&gt;");   break;
                case '"':  sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:   sb.Append(ch);       break;
            }
        }
    }
}
