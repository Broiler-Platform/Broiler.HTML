using System;
using System.Collections.Generic;
using System.Text;
using Broiler.CSS;
using Broiler.Dom.Html;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.Utils;
using Broiler.Layout.Engine;

namespace Broiler.HTML.Dom.Utils;

/// <summary>
/// The renderer's HTML export (<see cref="DomUtils.GenerateHtml"/>): the rendered box tree as markup,
/// for <c>GetHtml</c> and for copying a selection. It builds a view of the boxes as element, text and
/// comment nodes and hands it to the shared <see cref="HtmlSerializer"/>, which escapes text and
/// attributes, writes raw-text elements' text as it stands, writes no end tag or children for a void
/// element, and keeps a foreign element's name as written (HTML §13.3).
/// </summary>
/// <remarks>
/// <para>
/// The view carries the export's own policy, which no DOM serialization can: an element's text is its
/// rendered words (collapsed white space, generated content included), not its source text; a selection
/// exports the selected boxes with the ancestors that hold them, slices the words at the selection's
/// ends, and marks the selection root with <c>&lt;!--StartFragment--&gt;</c> /
/// <c>&lt;!--EndFragment--&gt;</c>; <see cref="HtmlGenerationStyle.Inline"/> writes the authored inline
/// declarations as the <c>style</c> attribute, and <see cref="HtmlGenerationStyle.InHeader"/> puts the
/// style sheet in a <c>&lt;head&gt;</c> right after <c>&lt;html&gt;</c>, ahead of the document's own.
/// An anonymous box is transparent: its words and children belong to the element around it, so the
/// text of a <c>&lt;style&gt;</c> or <c>&lt;xmp&gt;</c> is that element's own raw text.
/// </para>
/// <para>
/// It differs from the recursive writer it replaces where that writer was wrong: void elements end with
/// <c>&gt;</c>, not <c>/&gt;</c>, and drop their children, so an <c>&lt;input&gt;</c>'s generated label is no
/// longer written as stray text after it; raw-text elements' text is no longer escaped, so a style sheet's
/// <c>'</c> stays a quote; the inline <c>style</c> attribute no longer ends with <c>;</c>; and the table
/// layout's spacing boxes (written as <c>&lt;none colspan="1"&gt;</c>) and nested documents' sub-viewports
/// (written as <c>&lt;#subdoc-root&gt;</c>, with the other document inside) are left out. The view is
/// built, and the selection collected, with explicit stacks, and the serializer is iterative, so a deep
/// box tree does not exhaust the call stack here.
/// </para>
/// </remarks>
internal static class RendererHtmlExport
{
    private static readonly HtmlSerializationAdapter<ExportNode> Adapter = new(
        GetKind: static node => node.Kind,
        GetName: static node => node.Name,
        GetChildren: static node => node.Children ?? (IEnumerable<ExportNode>)Array.Empty<ExportNode>(),
        GetAttributes: static node => node.Attributes ?? (IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>(),
        GetStyles: static node => node.Styles ?? (IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>(),
        GetText: static node => node.Text,
        GetRawInnerHtml: static _ => null)
    {
        GetNamespace = static node => node.Namespace,
    };

    public static string Generate(CssBox? root, HtmlGenerationStyle style, bool onlySelected)
    {
        if (root == null)
            return string.Empty;

        Dictionary<CssBox, bool>? selectedBoxes = null;
        CssBox? selectionRoot = null;
        if (onlySelected)
        {
            selectedBoxes = CollectSelectedBoxes(root);
            selectionRoot = GetSelectionRoot(root, selectedBoxes);
        }

        var view = BuildView(root, style, selectedBoxes, selectionRoot);
        return HtmlSerializer.Serialize(view, Adapter);
    }

    private static ExportNode BuildView(CssBox root, HtmlGenerationStyle style, Dictionary<CssBox, bool>? selectedBoxes, CssBox? selectionRoot)
    {
        var document = new ExportNode(HtmlSerializationNodeKind.Fragment);
        var pending = new Stack<(CssBox? Box, ExportNode Target)>();
        pending.Push((root, document));

        while (pending.Count > 0)
        {
            var (box, target) = pending.Pop();

            // A null box closes the selection root: its end comment follows everything it holds.
            if (box == null)
            {
                target.Add(new ExportNode(HtmlSerializationNodeKind.Comment) { Text = "EndFragment" });
                continue;
            }

            if (IsLeftOut(box) || (box.HtmlTag != null && selectedBoxes != null && !selectedBoxes.ContainsKey(box)))
                continue;

            var holder = target;
            if (box.HtmlTag != null && !IsUnwrittenLink(box))
            {
                holder = CreateElement(box, style);
                target.Add(holder);

                if (box == selectionRoot)
                {
                    holder.Add(new ExportNode(HtmlSerializationNodeKind.Comment) { Text = "StartFragment" });
                    pending.Push((null, holder));
                }

                if (style == HtmlGenerationStyle.InHeader && box.HtmlTag.Name == "html")
                    AddStyleSheetHead(holder, ((IHtmlContainerInt)box.ContainerInt).StyleSet);
            }

            if (box.Words.Count > 0)
            {
                var text = new StringBuilder();
                foreach (var word in box.Words)
                {
                    if (selectedBoxes == null || word.Selected)
                        text.Append(DomUtils.GetSelectedWord(word, selectedBoxes != null));
                }

                if (text.Length > 0)
                    holder.Add(new ExportNode(HtmlSerializationNodeKind.Text) { Text = text.ToString() });
            }

            for (var index = box.Boxes.Count - 1; index >= 0; index--)
                pending.Push((box.Boxes[index], holder));
        }

        return document;
    }

    /// <summary>
    /// Boxes that are not the document's markup: the table layout's spacing boxes, which stand in for the
    /// rows a cell spans into, and a nested browsing context's sub-viewport, which holds another document.
    /// </summary>
    private static bool IsLeftOut(CssBox box) => box is CssSpacingBox || box.IsNestedBrowsingContextRoot;

    /// <summary>
    /// A <c>&lt;link&gt;</c> whose <c>href</c> names a <c>property</c> or <c>method</c>: not written, as the
    /// writer this replaces did not write it, though what it holds is.
    /// </summary>
    private static bool IsUnwrittenLink(CssBox box) =>
        box.HtmlTag!.Name == "link"
        && box.HtmlTag.Attributes.TryGetValue("href", out var href)
        && (href.StartsWith("property", StringComparison.Ordinal) || href.StartsWith("method", StringComparison.Ordinal));

    private static ExportNode CreateElement(CssBox box, HtmlGenerationStyle style)
    {
        var tag = box.HtmlTag!;
        var element = new ExportNode(HtmlSerializationNodeKind.Element)
        {
            Name = tag.Name,
            Namespace = box.SourceElement?.NamespaceUri,
        };

        if (!tag.HasAttributes())
            return element;

        element.Attributes = new List<KeyValuePair<string, string>>(tag.Attributes.Count);
        foreach (var attribute in tag.Attributes)
        {
            // The authored inline declarations, written back as the style attribute; style sheet rules
            // stay represented by the class and id attributes.
            if (style == HtmlGenerationStyle.Inline && attribute.Key == HtmlConstants.Style)
            {
                element.Styles ??= [];
                foreach (var declaration in new CssParser().ParseDeclarations(attribute.Value).Declarations)
                    element.Styles.Add(new KeyValuePair<string, string>(declaration.Name, declaration.Value.Text));
            }
            else
            {
                element.Attributes.Add(attribute);
            }
        }

        return element;
    }

    /// <summary>
    /// <see cref="HtmlGenerationStyle.InHeader"/>: a <c>&lt;head&gt;</c> holding the style sheet, with the
    /// line breaks the export has always put around it.
    /// </summary>
    private static void AddStyleSheetHead(ExportNode html, HtmlStyleSet styleSet)
    {
        var newLine = Environment.NewLine;
        var sheet = new ExportNode(HtmlSerializationNodeKind.Element)
        {
            Name = "style",
            Attributes = [new KeyValuePair<string, string>("type", "text/css")],
        };
        sheet.Add(new ExportNode(HtmlSerializationNodeKind.Text) { Text = newLine + CssSerializer.Serialize(styleSet.StyleSheet) + newLine });

        var head = new ExportNode(HtmlSerializationNodeKind.Element) { Name = "head" };
        head.Add(new ExportNode(HtmlSerializationNodeKind.Text) { Text = newLine });
        head.Add(sheet);
        head.Add(new ExportNode(HtmlSerializationNodeKind.Text) { Text = newLine });

        html.Add(head);
        html.Add(new ExportNode(HtmlSerializationNodeKind.Text) { Text = newLine });
    }

    /// <summary>
    /// The boxes a selection exports: every box with a selected word, every ancestor of one, and every
    /// element box that closes between two selected words (so an element with no words of its own, such as
    /// a <c>&lt;br&gt;</c>, inside the selected range is kept).
    /// </summary>
    /// <remarks>
    /// The recursive walk this replaces, with an explicit stack: a frame's children are visited in order,
    /// and a frame finishes, recording its box as a candidate, after the last of them.
    /// </remarks>
    private static Dictionary<CssBox, bool> CollectSelectedBoxes(CssBox root)
    {
        var selectedBoxes = new Dictionary<CssBox, bool>();
        var maybeBoxes = new Dictionary<CssBox, bool>();
        var frames = new Stack<SelectionFrame>();
        frames.Push(Enter(root, selectedBoxes, maybeBoxes));

        while (frames.Count > 0)
        {
            var frame = frames.Peek();
            if (frame.NextChild < frame.Box.Boxes.Count)
            {
                var child = frame.Box.Boxes[frame.NextChild++];
                frames.Push(Enter(child, selectedBoxes, maybeBoxes));
                continue;
            }

            frames.Pop();
            if (frame.Box.HtmlTag != null && selectedBoxes.Count > 0)
                maybeBoxes[frame.Box] = true;

            if (frame.IsInSelection && frames.Count > 0)
            {
                var parent = frames.Peek();
                selectedBoxes[parent.Box] = true;
                parent.IsInSelection = true;
            }
        }

        return selectedBoxes;
    }

    private static SelectionFrame Enter(CssBox box, Dictionary<CssBox, bool> selectedBoxes, Dictionary<CssBox, bool> maybeBoxes)
    {
        var frame = new SelectionFrame(box);
        foreach (var word in box.Words)
        {
            if (!word.Selected)
                continue;

            selectedBoxes[box] = true;
            foreach (var maybe in maybeBoxes)
                selectedBoxes[maybe.Key] = maybe.Value;

            maybeBoxes.Clear();
            frame.IsInSelection = true;
        }

        return frame;
    }

    private sealed class SelectionFrame(CssBox box)
    {
        public CssBox Box { get; } = box;
        public int NextChild { get; set; }
        public bool IsInSelection { get; set; }
    }

    private static CssBox GetSelectionRoot(CssBox root, Dictionary<CssBox, bool> selectedBoxes)
    {
        var selectionRoot = root;
        var selectionRootRun = root;

        while (true)
        {
            bool foundRoot = false;
            CssBox? selectedChild = null;

            foreach (var childBox in selectionRootRun.Boxes)
            {
                if (!selectedBoxes.ContainsKey(childBox))
                    continue;

                if (selectedChild != null)
                {
                    foundRoot = true;
                    break;
                }

                selectedChild = childBox;
            }

            if (foundRoot || selectedChild == null)
                break;

            selectionRootRun = selectedChild;

            // the actual selection root must be a box with html tag
            if (selectionRootRun.HtmlTag != null)
                selectionRoot = selectionRootRun;
        }

        // if the selection root doesn't contained any named boxes in it then we must go one level up, otherwise we will miss the selection root box formatting
        if (ContainsNamedBox(selectionRoot))
            return selectionRoot;

        // The tree root has no parent to go up to; it then stays the selection root.
        selectionRootRun = selectionRoot.ParentBox;
        while (selectionRootRun is { ParentBox: not null, HtmlTag: null })
            selectionRootRun = selectionRootRun.ParentBox;

        if (selectionRootRun?.HtmlTag != null)
            selectionRoot = selectionRootRun;

        return selectionRoot;
    }

    /// <summary>Whether any box below <paramref name="box"/> has an element.</summary>
    private static bool ContainsNamedBox(CssBox box)
    {
        var pending = new Stack<CssBox>(box.Boxes);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next.HtmlTag != null)
                return true;

            foreach (var child in next.Boxes)
                pending.Push(child);
        }

        return false;
    }

    /// <summary>A node of the export view: an element, a run of text, a comment, or the document.</summary>
    private sealed class ExportNode(HtmlSerializationNodeKind kind)
    {
        public HtmlSerializationNodeKind Kind { get; } = kind;
        public string Name { get; init; } = string.Empty;
        public string? Namespace { get; init; }
        public string? Text { get; init; }
        public List<KeyValuePair<string, string>>? Attributes { get; set; }
        public List<KeyValuePair<string, string>>? Styles { get; set; }
        public List<ExportNode>? Children { get; private set; }

        public void Add(ExportNode child) => (Children ??= []).Add(child);
    }
}
