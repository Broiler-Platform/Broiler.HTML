using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using Broiler.HTML.Utils;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core;

using HtmlTag = Broiler.Layout.HtmlTag;
using Broiler.Layout;
using HtmlUtils = Broiler.HTML.Utils.HtmlUtils;
using CommonUtils = Broiler.HTML.Utils.CommonUtils;
using HtmlConstants = Broiler.HTML.Utils.HtmlConstants;
using CssConstants = Broiler.CSS.CssConstants;
namespace Broiler.HTML.Dom.Utils;

internal sealed class DomUtils
{
    public static bool IsInBox(CssBox box, PointF location)
    {
        foreach (var line in box.Rectangles)
        {
            if (line.Value.Contains(location))
                return true;
        }

        foreach (var childBox in box.Boxes)
        {
            if (IsInBox(childBox, location))
                return true;
        }

        return false;
    }


    public static CssBox FindParent(CssBox root, string tagName, CssBox box)
    {
        if (box == null)
        {
            return root;
        }
        else if (box.HtmlTag != null && box.HtmlTag.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase))
        {
            return box.ParentBox ?? root;
        }
        else
        {
            return FindParent(root, tagName, box.ParentBox);
        }
    }





    public static string GetAttribute(CssBox box, string attribute)
    {
        string value = null;

        while (box != null && value == null)
        {
            value = box.GetAttribute(attribute, null);
            box = box.ParentBox;
        }

        return value;
    }

    public static CssBox GetCssBox(CssBox box, PointF location, bool visible = true)
    {
        if (box == null || visible && box.Visibility != CssConstants.Visible || !box.Bounds.IsEmpty && !box.Bounds.Contains(location))
            return null;

        foreach (var childBox in box.Boxes)
        {
            if (CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds).Contains(location))
                return GetCssBox(childBox, location) ?? childBox;
        }

        return null;
    }

    public static void GetAllLinkBoxes(CssBox box, List<CssBox> linkBoxes)
    {
        if (box == null)
            return;

        if (box.IsClickable && box.Visibility == CssConstants.Visible)
            linkBoxes.Add(box);

        foreach (var childBox in box.Boxes)
            GetAllLinkBoxes(childBox, linkBoxes);
    }

    public static CssBox GetLinkBox(CssBox box, PointF location)
    {
        if (box == null)
            return null;

        if (box.IsClickable && box.Visibility == CssConstants.Visible)
        {
            if (IsInBox(box, location))
                return box;
        }

        if (box.ClientRectangle.IsEmpty || box.ClientRectangle.Contains(location))
        {
            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetLinkBox(childBox, location);
                if (foundBox != null)
                    return foundBox;
            }
        }

        return null;
    }

    public static CssBox GetBoxById(CssBox box, string id)
    {
        if (box == null || string.IsNullOrEmpty(id))
            return null;

        if (box.HtmlTag != null && id.Equals(box.HtmlTag.TryGetAttribute("id"), StringComparison.OrdinalIgnoreCase))
            return box;

        foreach (var childBox in box.Boxes)
        {
            var foundBox = GetBoxById(childBox, id);
            if (foundBox != null)
                return foundBox;
        }

        return null;
    }

    public static CssLineBox GetCssLineBox(CssBox box, PointF location)
    {
        CssLineBox line = null;

        if (box == null)
            return line;

        if (box.LineBoxes.Count > 0)
        {
            if (box.HtmlTag == null || box.HtmlTag.Name != "td" || box.Bounds.Contains(location))
            {
                foreach (var lineBox in box.LineBoxes)
                {
                    foreach (var rect in lineBox.Rectangles)
                    {
                        if (rect.Value.Top <= location.Y)
                            line = lineBox;

                        if (rect.Value.Top > location.Y)
                            return line;
                    }
                }
            }
        }

        foreach (var childBox in box.Boxes)
            line = GetCssLineBox(childBox, location) ?? line;

        return line;
    }

    public static CssRect GetCssBoxWord(CssBox box, PointF location)
    {
        if (box == null || box.Visibility != CssConstants.Visible)
            return null;

        if (box.LineBoxes.Count > 0)
        {
            foreach (var lineBox in box.LineBoxes)
            {
                var wordBox = GetCssBoxWord(lineBox, location);
                if (wordBox != null)
                    return wordBox;
            }
        }

        if (box.ClientRectangle.IsEmpty || box.ClientRectangle.Contains(location))
        {
            foreach (var childBox in box.Boxes)
            {
                var foundWord = GetCssBoxWord(childBox, location);
                if (foundWord != null)
                    return foundWord;
            }
        }

        return null;
    }

    public static CssRect GetCssBoxWord(CssLineBox lineBox, PointF location)
    {
        foreach (var rects in lineBox.Rectangles)
        {
            foreach (var word in rects.Key.Words)
            {
                // add word spacing to word width so sentence won't have hols in it when moving the mouse
                var rect = word.Rectangle;
                rect.Width += (float)word.OwnerBox.ActualWordSpacing;
                if (rect.Contains(location))
                    return word;
            }
        }

        return null;
    }

    public static CssLineBox GetCssLineBoxByWord(CssRect word)
    {
        var box = word.OwnerBox;
        while (box.LineBoxes.Count == 0)
            box = box.ParentBox;

        foreach (var lineBox in box.LineBoxes)
        {
            foreach (var lineWord in lineBox.Words)
            {
                if (lineWord == word)
                    return lineBox;
            }
        }

        return box.LineBoxes[0];
    }

    public static string GetSelectedPlainText(CssBox root)
    {
        var sb = new StringBuilder();
        var lastWordIndex = GetSelectedPlainText(sb, root);
        return sb.ToString(0, lastWordIndex).Trim();
    }

    public static string GenerateHtml(CssBox root, HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline, bool onlySelected = false)
    {
        var sb = new StringBuilder();

        if (root != null)
        {
            var selectedBoxes = onlySelected ? CollectSelectedBoxes(root) : null;
            var selectionRoot = onlySelected ? GetSelectionRoot(root, selectedBoxes) : null;
            WriteHtml(sb, root, styleGen, selectedBoxes, selectionRoot);
        }

        return sb.ToString();
    }

    public static string GenerateBoxTree(CssBox root)
    {
        var sb = new StringBuilder();
        GenerateBoxTree(root, sb, 0);
        return sb.ToString();
    }

    private static int GetSelectedPlainText(StringBuilder sb, CssBox box)
    {
        int lastWordIndex = 0;
        foreach (var boxWord in box.Words)
        {
            // append the text of selected word (handle partial selected words)
            if (boxWord.Selected)
            {
                sb.Append(GetSelectedWord(boxWord, true));
                lastWordIndex = sb.Length;
            }
        }

        // empty span box
        if (box.Boxes.Count < 1 && box.Text.Length > 0 && box.Text.Span.IsWhiteSpace())
            sb.Append(' ');

        // deep traversal
        if (box.Visibility != CssConstants.Hidden && box.Display != CssConstants.None)
        {
            foreach (var childBox in box.Boxes)
            {
                var innerLastWordIdx = GetSelectedPlainText(sb, childBox);
                lastWordIndex = Math.Max(lastWordIndex, innerLastWordIdx);
            }
        }

        if (sb.Length <= 0)
            return lastWordIndex;

        // convert hr to line of dashes
        if (box.HtmlTag != null && box.HtmlTag.Name == "hr")
        {
            if (sb.Length > 1 && sb[sb.Length - 1] != '\n')
                sb.AppendLine();

            sb.AppendLine(new string('-', 80));
        }

        // new line for css block
        if (box.Display == CssConstants.Block || box.Display == CssConstants.ListItem || box.Display == CssConstants.TableRow)
        {
            if (!(box.IsBrElement && sb.Length > 1 && sb[sb.Length - 1] == '\n'))
                sb.AppendLine();
        }

        // space between table cells
        if (box.Display == CssConstants.TableCell)
            sb.Append(' ');

        // paragraphs has additional newline for nice formatting
        if (box.HtmlTag != null && box.HtmlTag.Name == "p")
        {
            int newlines = 0;

            for (int i = sb.Length - 1; i >= 0 && char.IsWhiteSpace(sb[i]); i--)
                newlines += sb[i] == '\n' ? 1 : 0;

            if (newlines < 2)
                sb.AppendLine();
        }

        return lastWordIndex;
    }

    private static Dictionary<CssBox, bool> CollectSelectedBoxes(CssBox root)
    {
        var selectedBoxes = new Dictionary<CssBox, bool>();
        var maybeBoxes = new Dictionary<CssBox, bool>();

        CollectSelectedBoxes(root, selectedBoxes, maybeBoxes);

        return selectedBoxes;
    }

    private static bool CollectSelectedBoxes(CssBox box, Dictionary<CssBox, bool> selectedBoxes, Dictionary<CssBox, bool> maybeBoxes)
    {
        bool isInSelection = false;

        foreach (var word in box.Words)
        {
            if (!word.Selected)
                continue;

            selectedBoxes[box] = true;

            foreach (var maybeTag in maybeBoxes)
                selectedBoxes[maybeTag.Key] = maybeTag.Value;

            maybeBoxes.Clear();
            isInSelection = true;
        }

        foreach (var childBox in box.Boxes)
        {
            var childInSelection = CollectSelectedBoxes(childBox, selectedBoxes, maybeBoxes);
            if (childInSelection)
            {
                selectedBoxes[box] = true;
                isInSelection = true;
            }
        }

        if (box.HtmlTag != null && selectedBoxes.Count > 0)
            maybeBoxes[box] = true;

        return isInSelection;
    }

    private static CssBox GetSelectionRoot(CssBox root, Dictionary<CssBox, bool> selectedBoxes)
    {
        var selectionRoot = root;
        var selectionRootRun = root;

        while (true)
        {
            bool foundRoot = false;
            CssBox selectedChild = null;

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

        selectionRootRun = selectionRoot.ParentBox;
        while (selectionRootRun.ParentBox != null && selectionRootRun.HtmlTag == null)
            selectionRootRun = selectionRootRun.ParentBox;

        if (selectionRootRun.HtmlTag != null)
            selectionRoot = selectionRootRun;

        return selectionRoot;
    }

    private static bool ContainsNamedBox(CssBox box)
    {
        foreach (var childBox in box.Boxes)
        {
            if (childBox.HtmlTag != null || ContainsNamedBox(childBox))
                return true;
        }

        return false;
    }

    private static void WriteHtml(StringBuilder sb, CssBox box, HtmlGenerationStyle styleGen, Dictionary<CssBox, bool> selectedBoxes, CssBox selectionRoot)
    {
        if (box.HtmlTag != null && selectedBoxes != null && !selectedBoxes.ContainsKey(box))
            return;

        if (box.HtmlTag != null)
        {
            if (box.HtmlTag.Name != "link" || !box.HtmlTag.Attributes.TryGetValue("href", out string value) ||
                (!value.StartsWith("property") && !value.StartsWith("method")))
            {
                WriteHtmlTag(sb, box, styleGen);
                if (box == selectionRoot)
                    sb.Append("<!--StartFragment-->");
            }

            if (styleGen == HtmlGenerationStyle.InHeader && box.HtmlTag.Name == "html")
            {
                sb.AppendLine("<head>");
                WriteStylesheet(sb, ((IHtmlContainerInt)box.ContainerInt).StyleSet);
                sb.AppendLine("</head>");
            }
        }

        if (box.Words.Count > 0)
        {
            foreach (var word in box.Words)
            {
                if (selectedBoxes == null || word.Selected)
                {
                    var wordText = GetSelectedWord(word, selectedBoxes != null);
                    sb.Append(HtmlUtils.EncodeHtml(wordText));
                }
            }
        }

        foreach (var childBox in box.Boxes)
            WriteHtml(sb, childBox, styleGen, selectedBoxes, selectionRoot);

        if (box.HtmlTag != null && !box.HtmlTag.IsSingle)
        {
            if (box == selectionRoot)
                sb.Append("<!--EndFragment-->");
            sb.AppendFormat($"</{box.HtmlTag.Name}>");
        }
    }

    private static void WriteHtmlTag(StringBuilder sb, CssBox box, HtmlGenerationStyle styleGen)
    {
        sb.AppendFormat($"<{box.HtmlTag.Name}");

        // Preserve authored inline declarations. Stylesheet rules remain represented by
        // their class/id attributes; the renderer no longer maintains a second selector
        // index solely for HTML serialization.
        var tagStyles = new Dictionary<string, string>();

        if (box.HtmlTag.HasAttributes())
        {
            sb.Append(' ');
            foreach (var att in box.HtmlTag.Attributes)
            {
                // handle image tags by inserting the image using base64 data
                if (styleGen == HtmlGenerationStyle.Inline && att.Key == HtmlConstants.Style)
                {
                    var declarations = new Broiler.CSS.CssParser().ParseDeclarations(att.Value);
                    foreach (var declaration in declarations.Declarations)
                        tagStyles[declaration.Name] = declaration.Value.Text;
                }
                else
                {
                    sb.AppendFormat($"{att.Key}=\"{att.Value}\" ");
                }
            }

            sb.Remove(sb.Length - 1, 1);
        }

        // if inline style insert the style tag with all collected style properties
        if (styleGen == HtmlGenerationStyle.Inline && tagStyles.Count > 0)
        {
            if (tagStyles.Count > 0)
            {
                sb.Append(" style=\"");
                foreach (var style in tagStyles)
                    sb.AppendFormat($"{style.Key}: {style.Value}; ");
                sb.Remove(sb.Length - 1, 1);
                sb.Append('"');
            }
        }

        sb.AppendFormat($"{(box.HtmlTag.IsSingle ? "/" : "")}>");
    }

    private static void WriteStylesheet(StringBuilder sb, HtmlStyleSet styleSet)
    {
        sb.AppendLine("<style type=\"text/css\">");
        sb.AppendLine(Broiler.CSS.CssSerializer.Serialize(styleSet.StyleSheet));
        sb.AppendLine("</style>");
    }

    private static string GetSelectedWord(CssRect rect, bool selectedText)
    {
        if (selectedText && rect.SelectedStartIndex > -1 && rect.SelectedEndIndexOffset > -1)
        {
            return rect.Text.Substring(rect.SelectedStartIndex, rect.SelectedEndIndexOffset - rect.SelectedStartIndex);
        }
        else if (selectedText && rect.SelectedStartIndex > -1)
        {
            return rect.Text.Substring(rect.SelectedStartIndex) + (rect.HasSpaceAfter ? " " : "");
        }
        else if (selectedText && rect.SelectedEndIndexOffset > -1)
        {
            return rect.Text.Substring(0, rect.SelectedEndIndexOffset);
        }
        else
        {
            var whitespaceBefore = rect.OwnerBox.Words[0] == rect ? LayoutBoxUtils.IsBoxHasWhitespace(rect.OwnerBox) : rect.HasSpaceBefore;
            return (whitespaceBefore ? " " : "") + rect.Text + (rect.HasSpaceAfter ? " " : "");
        }
    }

    private static void GenerateBoxTree(CssBox box, StringBuilder builder, int indent)
    {
        builder.AppendFormat($"{new string(' ', 2 * indent)}<{box.Display}");

        if (box.HtmlTag != null)
            builder.AppendFormat($" element=\"{(box.HtmlTag != null ? box.HtmlTag.Name : string.Empty)}\"");

        if (box.Words.Count > 0)
            builder.AppendFormat($" words=\"{box.Words.Count}\"");

        builder.AppendFormat($"{(box.Boxes.Count > 0 ? "" : "/")}>\r\n");

        if (box.Boxes.Count > 0)
        {
            foreach (var childBox in box.Boxes)
                GenerateBoxTree(childBox, builder, indent + 1);

            builder.AppendFormat($"{new string(' ', 2 * indent)}</{box.Display}>\r\n");
        }
    }
}
