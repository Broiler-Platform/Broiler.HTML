using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Utils.Core.Utils;

namespace Broiler.HTML.CSS.Core.Parse;


internal sealed class CssParser
{
    private static readonly char[] _cssBlockSplitters = ['}', ';'];
    private readonly IColorResolver _colorResolver;
    private readonly CssValueParser _valueParser;
    private static readonly char[] _cssClassTrimChars = ['\r', '\n', '\t', ' ', '-', '!', '<', '>'];
    private int _sourceOrder;

    public CssParser(IColorResolver colorResolver)
    {
        ArgumentNullException.ThrowIfNull(colorResolver);

        _valueParser = new CssValueParser(colorResolver);
        _colorResolver = colorResolver;
    }

    public CssData ParseStyleSheet(string stylesheet, CssData defaultCssData)
    {
        var cssData = defaultCssData != null ? defaultCssData.Clone() : new CssData();

        if (!string.IsNullOrEmpty(stylesheet))
            ParseStyleSheet(cssData, stylesheet);

        return cssData;
    }

    public void ParseStyleSheet(CssData cssData, string stylesheet)
    {
        if (!string.IsNullOrEmpty(stylesheet))
        {
            _sourceOrder = 0;
            stylesheet = RemoveStylesheetComments(stylesheet);

            ParseStyleBlocks(cssData, StripAtRules(stylesheet));
            ParseMediaStyleBlocks(cssData, stylesheet);
            ParseFontFaceBlocks(cssData, stylesheet);
        }
    }

    public CssBlock ParseCssBlock(string className, string blockSource) => ParseCssBlockImp(className, blockSource);
    public string ParseFontFamily(string value) => ParseFontFamilyProperty(value);
    public Color ParseColor(string colorStr) => _valueParser.GetActualColor(colorStr);


    /// <summary>
    /// Returns the index of the first unescaped occurrence of <paramref name="ch"/>
    /// starting from <paramref name="startIdx"/>, or -1 if not found.
    /// A character preceded by an odd number of backslashes is considered
    /// escaped and skipped (e.g. <c>\}</c> is escaped but <c>\\}</c> is not).
    /// </summary>
    private static int FindUnescapedChar(string text, char ch, int startIdx)
    {
        for (int i = startIdx; i < text.Length; i++)
        {
            if (text[i] == ch && !IsEscaped(text, i))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns <c>true</c> when the character at <paramref name="index"/>
    /// is preceded by an odd number of backslashes (i.e. it is escaped).
    /// </summary>
    private static bool IsEscaped(string text, int index)
    {
        int backslashes = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            backslashes++;
        return (backslashes & 1) != 0;
    }

    private static string RemoveStylesheetComments(string stylesheet)
    {
        StringBuilder sb = null;

        int prevIdx = 0, startIdx = 0;
        while (startIdx > -1 && startIdx < stylesheet.Length)
        {
            startIdx = stylesheet.IndexOf("/*", startIdx);
            if (startIdx > -1)
            {
                sb ??= new StringBuilder(stylesheet.Length);
                sb.Append(stylesheet.AsSpan(prevIdx, startIdx - prevIdx));

                var endIdx = stylesheet.IndexOf("*/", startIdx + 2);
                if (endIdx < 0)
                    endIdx = stylesheet.Length;

                prevIdx = startIdx = endIdx + 2;
            }
            else
            {
                sb?.Append(stylesheet.AsSpan(prevIdx));
            }
        }

        return sb != null ? sb.ToString() : stylesheet;
    }

    /// <summary>
    /// Remove @-rule blocks (e.g. <c>@media</c>) from the stylesheet so that
    /// <see cref="ParseStyleBlocks"/> does not treat rules inside them as
    /// top-level declarations.  The original stylesheet (with @-rules) is
    /// still passed to <see cref="ParseMediaStyleBlocks"/>.
    /// </summary>
    private static string StripAtRules(string stylesheet)
    {
        int nextAt = stylesheet.IndexOf('@');
        if (nextAt < 0)
            return stylesheet;

        var sb = new StringBuilder(stylesheet.Length);
        int pos = 0;

        while (nextAt >= 0)
        {
            sb.Append(stylesheet, pos, nextAt - pos);

            // Determine which kind of at-rule this is.  @import and
            // @charset are statement-style (terminated by ';'), while
            // @media, @font-face, @keyframes, etc. are block-style
            // (terminated by a balanced '{…}' pair).
            int nameEnd = nextAt + 1;
            while (nameEnd < stylesheet.Length && char.IsLetter(stylesheet[nameEnd]))
                nameEnd++;
            string atName = stylesheet.Substring(nextAt + 1, nameEnd - nextAt - 1).ToLowerInvariant();

            if (atName == "import" || atName == "charset" || atName == "namespace")
            {
                // Semicolon-terminated at-rule — skip to next ';'.
                int semi = stylesheet.IndexOf(';', nextAt);
                if (semi < 0)
                {
                    // Malformed — skip rest of stylesheet.
                    pos = stylesheet.Length;
                    break;
                }
                pos = semi + 1;
            }
            else
            {
                int braceStart = stylesheet.IndexOf('{', nextAt);
                if (braceStart < 0)
                {
                    pos = nextAt;
                    break;
                }

                int count = 1;
                int endIdx = braceStart + 1;
                while (count > 0 && endIdx < stylesheet.Length)
                {
                    if (stylesheet[endIdx] == '{')
                        count++;
                    else if (stylesheet[endIdx] == '}')
                        count--;
                    endIdx++;
                }

                pos = endIdx;
            }

            nextAt = pos < stylesheet.Length ? stylesheet.IndexOf('@', pos) : -1;
        }

        if (pos < stylesheet.Length)
            sb.Append(stylesheet, pos, stylesheet.Length - pos);

        return sb.ToString();
    }

    private void ParseStyleBlocks(CssData cssData, string stylesheet)
    {
        var startIdx = 0;
        int endIdx = 0;

        while (startIdx < stylesheet.Length && endIdx > -1)
        {
            endIdx = startIdx;
            while (endIdx + 1 < stylesheet.Length)
            {
                endIdx++;
                if (!IsEscaped(stylesheet, endIdx) && stylesheet[endIdx] == '}')
                    startIdx = endIdx + 1;
                if (!IsEscaped(stylesheet, endIdx) && stylesheet[endIdx] == '{')
                    break;
            }

            int midIdx = endIdx + 1;

            if (endIdx <= -1)
                continue;

            endIdx++;
            
            while (endIdx < stylesheet.Length)
            {
                if (!IsEscaped(stylesheet, endIdx) && stylesheet[endIdx] == '{')
                    startIdx = midIdx + 1;

                if (!IsEscaped(stylesheet, endIdx) && stylesheet[endIdx] == '}')
                    break;

                endIdx++;
            }

            if (endIdx < stylesheet.Length)
            {
                while (startIdx < stylesheet.Length && char.IsWhiteSpace(stylesheet[startIdx]))
                    startIdx++;

                if (startIdx < endIdx)
                {
                    var substring = stylesheet.Substring(startIdx, endIdx - startIdx + 1);
                    FeedStyleBlock(cssData, substring);
                }
            }

            startIdx = endIdx + 1;
        }
    }

    private void ParseMediaStyleBlocks(CssData cssData, string stylesheet)
    {
        int startIdx = 0;
        string atrule;

        while ((atrule = RegexParserUtils.GetCssAtRules(stylesheet, ref startIdx)) != null)
        {
            if (!atrule.StartsWith("@media", StringComparison.InvariantCultureIgnoreCase))
                continue;

            //Extract specified media types
            MatchCollection types = RegexParserUtils.Match(RegexParserUtils.CssMediaTypesRegex(), atrule);

            if (types.Count != 1)
                continue;

            string line = types[0].Value;

            if (!line.StartsWith("@media", StringComparison.InvariantCultureIgnoreCase) || !line.EndsWith("{"))
                continue;

            //Get specified media types in the at-rule
            string[] media = line.Substring(6, line.Length - 7).Split(' ');

            //Scan media types
            foreach (string t in media)
            {
                string mediaType = t.Trim();
                if (string.IsNullOrEmpty(mediaType))
                    continue;

                //Get blocks inside the at-rule
                var insideBlocks = RegexParserUtils.Match(RegexParserUtils.CssBlocksRegex(), atrule);

                //Scan blocks and feed them to the style sheet
                foreach (Match insideBlock in insideBlocks)
                {
                    // Treat @media screen rules as applicable to all
                    // (HTML-Renderer always renders for screen)
                    if (string.Equals(mediaType, "screen", StringComparison.OrdinalIgnoreCase))
                        FeedStyleBlock(cssData, insideBlock.Value);
                    else
                        FeedStyleBlock(cssData, insideBlock.Value, mediaType);
                }
            }
        }
    }

    /// <summary>
    /// Extracts @font-face rules from the stylesheet and adds them to
    /// <see cref="CssData.FontFaces"/>.
    /// </summary>
    private static void ParseFontFaceBlocks(CssData cssData, string stylesheet)
    {
        int startIdx = 0;
        string atrule;

        while ((atrule = RegexParserUtils.GetCssAtRules(stylesheet, ref startIdx)) != null)
        {
            if (!atrule.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase))
                continue;

            int braceOpen = atrule.IndexOf('{');
            int braceClose = atrule.LastIndexOf('}');
            if (braceOpen < 0 || braceClose <= braceOpen)
                continue;

            string body = atrule.Substring(braceOpen + 1, braceClose - braceOpen - 1);

            string family = null;
            string src = null;

            foreach (string decl in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = decl.IndexOf(':');
                if (colon < 0)
                    continue;

                string name = decl.Substring(0, colon).Trim().ToLowerInvariant();
                string value = decl.Substring(colon + 1).Trim();

                if (name == "font-family")
                {
                    family = value.Trim('"', '\'', ' ');
                }
                else if (name == "src")
                {
                    int urlStart = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
                    if (urlStart >= 0)
                    {
                        int pathStart = urlStart + 4;
                        int pathEnd = value.IndexOf(')', pathStart);
                        if (pathEnd > pathStart)
                        {
                            src = value.Substring(pathStart, pathEnd - pathStart).Trim('"', '\'', ' ');
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(family) && !string.IsNullOrEmpty(src))
            {
                cssData.FontFaces.Add(new CssFontFace { Family = family, Src = src });
            }
        }
    }

    private void FeedStyleBlock(CssData cssData, string block, string media = "all")
    {
        int startIdx = block.IndexOf('{');
        int endIdx = startIdx > -1 ? FindUnescapedChar(block, '}', startIdx + 1) : -1;

        if (startIdx <= -1 || endIdx <= -1)
            return;

        string blockSource = block.Substring(startIdx + 1, endIdx - startIdx - 1);
        var classes = block.Substring(0, startIdx).Split(',');

        foreach (string cls in classes)
        {
            string className = cls.Trim(_cssClassTrimChars);

            if (string.IsNullOrEmpty(className))
                continue;

            var newblock = ParseCssBlockImp(className, blockSource);
            if (newblock != null)
            {
                newblock.Specificity = CalculateSelectorSpecificity(className);
                newblock.SourceOrder = _sourceOrder++;
                cssData.AddCssBlock(media, newblock);
            }
        }
    }

    private static int CalculateSelectorSpecificity(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return 0;

        int a = 0, b = 0, c = 0;

        for (int i = 0; i < selector.Length; i++)
        {
            char ch = selector[i];
            switch (ch)
            {
                case '#':
                    a++;
                    i = SkipIdentifier(selector, i + 1) - 1;
                    break;
                case '.':
                    b++;
                    i = SkipIdentifier(selector, i + 1) - 1;
                    break;
                case '[':
                    b++;
                    i = SkipBalanced(selector, i + 1, '[', ']');
                    break;
                case ':':
                    if (i + 1 < selector.Length && selector[i + 1] == ':')
                    {
                        c++;
                        i = SkipIdentifier(selector, i + 2) - 1;
                        break;
                    }

                    b++;
                    i = SkipPseudoClass(selector, i + 1) - 1;
                    break;
                case '*':
                case ' ':
                case '>':
                case '+':
                case '~':
                case ',':
                    break;
                default:
                    if (IsIdentifierStart(ch))
                    {
                        c++;
                        i = SkipIdentifier(selector, i + 1) - 1;
                    }

                    break;
            }
        }

        return (a * 1_000_000) + (b * 1_000) + c;
    }

    private static int SkipPseudoClass(string selector, int start)
    {
        int i = SkipIdentifier(selector, start);
        if (i < selector.Length && selector[i] == '(')
            return SkipBalanced(selector, i + 1, '(', ')') + 1;

        return i;
    }

    private static int SkipBalanced(string text, int start, char open, char close)
    {
        int depth = 1;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '"' || text[i] == '\'')
            {
                char quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                        i++;
                    i++;
                }

                continue;
            }

            if (text[i] == open)
                depth++;
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return text.Length - 1;
    }

    private static int SkipIdentifier(string text, int start)
    {
        int i = start;
        while (i < text.Length)
        {
            char ch = text[i];
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_')
                break;

            i++;
        }

        return i;
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch is '_' or '-';

    /// <summary>
    /// Decodes CSS escape sequences in a string.  A backslash followed by
    /// 1–6 hex digits is replaced with the corresponding Unicode character;
    /// an optional single trailing whitespace character is consumed.  A
    /// backslash followed by any other character inserts that character.
    /// </summary>
    private static string DecodeCssEscapes(string input)
    {
        int backslash = input.IndexOf('\\');
        if (backslash < 0)
            return input;

        var sb = new System.Text.StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                i++;
                if (IsHexDigit(input[i]))
                {
                    var hex = new System.Text.StringBuilder(6);
                    while (i < input.Length && IsHexDigit(input[i]) && hex.Length < 6)
                    {
                        hex.Append(input[i]);
                        i++;
                    }
                    // Consume optional trailing whitespace (one character).
                    if (i < input.Length && (input[i] == ' ' || input[i] == '\t' || input[i] == '\n'
                        || input[i] == '\r' || input[i] == '\f'))
                        i++;
                    int codePoint = int.Parse(hex.ToString(), System.Globalization.NumberStyles.HexNumber);
                    if (codePoint > 0 && codePoint <= 0x10FFFF)
                        sb.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    sb.Append(input[i]);
                    i++;
                }
            }
            else
            {
                sb.Append(input[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    /// <summary>
    /// Lowercases only ASCII A–Z characters, leaving all other characters
    /// (including non-ASCII Unicode) unchanged.  CSS type selectors in HTML
    /// are case-insensitive only for the ASCII range (Selectors §3).
    /// </summary>
    private static string AsciiToLower(string input)
    {
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] >= 'A' && chars[i] <= 'Z')
                chars[i] = (char)(chars[i] + 32);
        return new string(chars);
    }

    private CssBlock ParseCssBlockImp(string className, string blockSource)
    {
        // Decode CSS Unicode escapes (e.g. \212A → U+212A) before
        // lowercasing so that non-ASCII characters are preserved.
        className = DecodeCssEscapes(className);

        // CSS Selectors §3: type selectors in HTML are ASCII
        // case-insensitive.  Use ASCII-only lowering to avoid
        // Unicode case-folding (e.g. U+212A Kelvin sign → 'k').
        className = AsciiToLower(className);

        // Strip attribute selectors: convert [class~=value] to .value,
        // and extract other attribute selectors as conditions.  This enables
        // CSS2.1 §5.8.1 attribute selectors used in the Acid2 test.
        List<CssAttributeCondition> attrConditions = null;
        className = StripAttributeSelectors(className, out attrConditions);
        if (string.IsNullOrEmpty(className))
            return null;

        // CSS2.1 §5.11: Pre-process structural pseudo-classes (:first-child,
        // :last-child) that may appear anywhere in the selector chain.
        // Extract the pseudo-class and rewrite the selector so the rest of
        // the parsing pipeline handles it correctly.
        string structuralPseudo = null;
        bool structuralOnTerminal = false;
        className = StripStructuralPseudoClasses(className, out structuralPseudo, out structuralOnTerminal);
        if (string.IsNullOrEmpty(className))
            return null;

        string psedoClass = null;
        string pseudoElement = null;
        bool descendantCombinatorBeforePseudo = false;
        var colonIdx = className.IndexOf(":", StringComparison.Ordinal);

        if (colonIdx > -1 && !className.StartsWith("::"))
        {
            var suffix = colonIdx < className.Length - 1 ? className.Substring(colonIdx + 1).Trim() : null;

            // CSS2.1 §5.12: Detect whether a descendant combinator (whitespace)
            // precedes the pseudo-element.  ".nose div :after" (with space) means
            // "the ::after pseudo of *descendants* of .nose div", whereas
            // ".nose div:after" (no space) means "::after on .nose div elements
            // themselves".
            var rawSelector = className.Substring(0, colonIdx);
            descendantCombinatorBeforePseudo = rawSelector.Length > 0 &&
                char.IsWhiteSpace(rawSelector[rawSelector.Length - 1]);
            className = rawSelector.Trim();

            // CSS2.1 §12.1 / CSS3: Normalise :before/:after and ::before/::after
            // to pseudo-element references so they can be stored and applied.
            if (suffix != null)
            {
                var normalised = suffix.TrimStart(':');
                if (normalised == "before" || normalised == "after")
                    pseudoElement = "::" + normalised;
                else
                    psedoClass = suffix;
            }
        }

        if (!string.IsNullOrEmpty(className))
        {
            if (pseudoElement != null)
            {
                var selectors = ParseCssBlockSelector(className, out string firstClass);

                // CSS2.1 §5.12: When a descendant combinator precedes the
                // pseudo-element (e.g. ".nose div :after" vs ".nose div:after"),
                // the pseudo applies to descendants of the matched element, not
                // the element itself.  Model this by requiring firstClass as an
                // additional ancestor in the selector chain.
                if (descendantCombinatorBeforePseudo)
                {
                    selectors ??= [];
                    selectors.Insert(0, new CssBlockSelectorItem(firstClass, false));
                }

                var (properties, importantProps) = ParseCssBlockProperties(blockSource);
                var block = new CssBlock(firstClass + pseudoElement, properties, selectors);
                MarkImportantProperties(block, importantProps);
                ApplyStructuralPseudo(block, structuralPseudo, structuralOnTerminal);
                if (attrConditions != null)
                    block.AttributeConditions = attrConditions;
                return block;
            }

            if (psedoClass == null || psedoClass == "link" || psedoClass == "hover")
            {
                var selectors = ParseCssBlockSelector(className, out string firstClass);
                var (properties, importantProps) = ParseCssBlockProperties(blockSource);

                var block = new CssBlock(firstClass, properties, selectors, psedoClass == "hover");
                MarkImportantProperties(block, importantProps);
                ApplyStructuralPseudo(block, structuralPseudo, structuralOnTerminal);
                if (attrConditions != null)
                    block.AttributeConditions = attrConditions;
                return block;
            }
        }

        return null;
    }

    /// <summary>
    /// CSS2.1 §5.11.1: Recognised structural pseudo-classes.
    /// Also includes <c>:lang()</c> (CSS2.1 §5.11.4) and <c>:open</c>
    /// (Selectors Level 4 open-state pseudo-class).
    /// </summary>
    private static bool IsStructuralPseudoClass(string name) =>
        (name is "first-child" or "last-child" or "only-child" or "root" or "open")
        || name.StartsWith("lang(", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scans <paramref name="selector"/> for structural pseudo-classes
    /// (e.g. <c>:first-child</c>) and removes them, returning the cleaned
    /// selector.  The extracted pseudo-class name and whether it was on the
    /// terminal simple selector are reported via the out parameters.
    /// <para>
    /// Standalone leading pseudo (":first-child + * .buckets p") is replaced
    /// with a universal selector ("* + * .buckets p") so that the rest of
    /// the parsing pipeline sees a well-formed selector.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Implements TODO-25 (acid3-compliance.md §11.5): ensures that
    /// <c>:first-child + *</c> followed by combinators is correctly
    /// transformed so that the adjacent-sibling (<c>+</c>) combinator is
    /// preserved in the parsed selector chain.  Also supports TODO-27
    /// for attached pseudo-classes like <c>h1:first-child</c>.
    /// Tests: <c>Acid3Todo24_28Tests.cs</c>.
    /// </remarks>
    private static string StripStructuralPseudoClasses(string selector, out string pseudo, out bool onTerminal)
    {
        pseudo = null;
        onTerminal = false;

        // Fast path: no colon means no pseudo-class.
        int colonIdx = selector.IndexOf(':');
        if (colonIdx < 0)
            return selector;

        // Skip pseudo-elements (::before, ::after).
        if (selector.Length > colonIdx + 1 && selector[colonIdx + 1] == ':')
            return selector;

        // Extract the pseudo-class name. Functional pseudo-classes like
        // :lang("en-gb") may contain punctuation that must be preserved
        // until the matching closing parenthesis.
        int nameStart = colonIdx + 1;
        int nameEnd = FindPseudoClassEnd(selector, nameStart);

        string pseudoName = selector.Substring(nameStart, nameEnd - nameStart);

        if (!IsStructuralPseudoClass(pseudoName))
            return selector; // not structural — leave unchanged for existing handling

        pseudo = pseudoName;

        string before = selector.Substring(0, colonIdx).TrimEnd();
        string after = selector.Substring(nameEnd).TrimStart();

        if (before.Length == 0)
        {
            // Standalone leading pseudo (":first-child + * .buckets p").
            // Replace with "* <rest>" — the "*" represents the element that
            // must satisfy the pseudo-class condition.
            // Special case: ":root" → replace with "html" so the type-
            // selector specificity allows it to override "html { … }" rules
            // when it appears later in document order (CSS 2.1 §6.4.1).
            // For ":root", onTerminal is always true so the PseudoClass is
            // stored on the block itself (not on a selector chain item),
            // ensuring the pseudo-class check runs on the matched element.
            string replacement = (pseudoName == "root") ? "html" : "*";
            onTerminal = (pseudoName == "root") || string.IsNullOrEmpty(after);
            return string.IsNullOrEmpty(after) ? replacement : replacement + " " + after;
        }
        else
        {
            // Attached to a selector ("h1:first-child", "#id:last-child").
            onTerminal = string.IsNullOrEmpty(after)
                || (!after.Contains(' ') && !after.Contains('>') && !after.Contains('+'));
            return string.IsNullOrEmpty(after) ? before : before + " " + after;
        }
    }

    private static int FindPseudoClassEnd(string selector, int nameStart)
    {
        int nameEnd = nameStart;
        int parenDepth = 0;
        char quote = '\0';

        while (nameEnd < selector.Length)
        {
            char ch = selector[nameEnd];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';

                nameEnd++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
                nameEnd++;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                nameEnd++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;

                nameEnd++;
                if (parenDepth == 0)
                    break;

                continue;
            }

            if (parenDepth == 0 &&
                (char.IsWhiteSpace(ch) || ch == '>' || ch == '+' || ch == ':'))
            {
                break;
            }

            nameEnd++;
        }

        return nameEnd;
    }

    /// <summary>
    /// After a CssBlock has been created, attach the structural pseudo-class
    /// information so that matching can verify the condition at cascade time.
    /// </summary>
    private static void ApplyStructuralPseudo(CssBlock block, string pseudo, bool onTerminal)
    {
        if (pseudo == null)
            return;

        if (onTerminal || block.Selectors == null || block.Selectors.Count == 0)
        {
            // Pseudo-class applies to the terminal selector element.
            block.PseudoClass = pseudo;
        }
        else
        {
            // Pseudo-class applies to the last selector item in the chain
            // (the element furthest from the terminal, at the start of the
            // original selector string).
            int lastIdx = block.Selectors.Count - 1;
            var item = block.Selectors[lastIdx];
            block.Selectors[lastIdx] = new CssBlockSelectorItem(
                item.Class, item.DirectParent, item.AdjacentSibling, pseudo);
        }
    }

    private static List<CssBlockSelectorItem> ParseCssBlockSelector(string className, out string firstClass)
    {
        List<CssBlockSelectorItem> selectors = null;

        firstClass = null;
        int endIdx = className.Length - 1;

        while (endIdx > -1)
        {
            bool directParent = false;
            bool adjacentSibling = false;

            while (endIdx > -1 && (char.IsWhiteSpace(className[endIdx]) || className[endIdx] == '>' || className[endIdx] == '+'))
            {
                directParent = directParent || className[endIdx] == '>';
                adjacentSibling = adjacentSibling || className[endIdx] == '+';
                endIdx--;
            }

            if (endIdx < 0)
                break;

            var startIdx = endIdx;

            while (startIdx > -1 && !char.IsWhiteSpace(className[startIdx]) && className[startIdx] != '>' && className[startIdx] != '+')
                startIdx--;

            if (startIdx > -1)
            {
                selectors ??= [];

                var subclass = className.Substring(startIdx + 1, endIdx - startIdx);

                if (firstClass == null)
                {
                    firstClass = subclass;
                }
                else
                {
                    while (startIdx > -1 && char.IsWhiteSpace(className[startIdx]))
                        startIdx--;

                    selectors.Add(new CssBlockSelectorItem(subclass, directParent, adjacentSibling));
                }
            }
            else if (firstClass != null)
            {
                selectors.Add(new CssBlockSelectorItem(className.Substring(0, endIdx + 1), directParent, adjacentSibling));
            }

            endIdx = startIdx;
        }

        firstClass = firstClass ?? className;
        return selectors;
    }

    private static void MarkImportantProperties(CssBlock block, HashSet<string> importantProperties)
    {
        if (importantProperties == null)
            return;
        foreach (var prop in importantProperties)
            block.MarkImportant(prop);
    }

    private (Dictionary<string, string> properties, HashSet<string> importantProperties) ParseCssBlockProperties(string blockSource)
    {
        var properties = new Dictionary<string, string>();
        HashSet<string> importantProperties = null;
        int startIdx = 0;

        while (startIdx < blockSource.Length)
        {
            int endIdx = blockSource.IndexOfAny(_cssBlockSplitters, startIdx);

            // If blockSource contains "data:image" then skip first semicolon since it is a part of image definition
            // example: "url('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAA......"
            if (startIdx >= 0 && endIdx - startIdx >= 10 && blockSource.Length - startIdx >= 10 && blockSource.IndexOf("data:image", startIdx, endIdx - startIdx) >= 0)
                endIdx = blockSource.IndexOfAny(_cssBlockSplitters, endIdx + 1);

            if (endIdx < 0)
                endIdx = blockSource.Length - 1;

            var splitIdx = blockSource.IndexOf(':', startIdx, endIdx - startIdx);
            if (splitIdx > -1)
            {
                //Extract property name and value
                startIdx = startIdx + (blockSource[startIdx] == ' ' ? 1 : 0);
                var adjEndIdx = endIdx - (blockSource[endIdx] == ' ' || blockSource[endIdx] == ';' ? 1 : 0);
                string propName = blockSource.Substring(startIdx, splitIdx - startIdx).Trim().ToLower();

                splitIdx = splitIdx + (blockSource[splitIdx + 1] == ' ' ? 2 : 1);

                if (adjEndIdx >= splitIdx)
                {
                    string propValue = blockSource.Substring(splitIdx, adjEndIdx - splitIdx + 1).Trim();

                    // CSS property values are case-insensitive for keywords,
                    // but URLs (including data: URIs with base64) are case-
                    // sensitive.  Only lowercase when the value contains no URL.
                    if (!propValue.StartsWith("url", StringComparison.InvariantCultureIgnoreCase)
                        && propValue.IndexOf("url(", StringComparison.InvariantCultureIgnoreCase) < 0)
                        propValue = propValue.ToLower();

                    AddProperty(propName, propValue, properties, ref importantProperties);
                }
            }

            startIdx = endIdx + 1;
        }

        return (properties, importantProperties);
    }

    private void AddProperty(string propName, string propValue, Dictionary<string, string> properties, ref HashSet<string> importantProperties)
    {
        // CSS2.1 §1.3.2: handle !important declarations.
        // If '!' is present but not followed by 'important', the
        // declaration is malformed and must be discarded (§4.1.7).
        bool isImportant = false;
        int bangIdx = propValue.IndexOf('!');
        if (bangIdx >= 0)
        {
            var afterBang = propValue.Substring(bangIdx + 1).Trim();
            if (afterBang.Equals("important", StringComparison.OrdinalIgnoreCase))
            {
                propValue = propValue.Substring(0, bangIdx).Trim();
                isImportant = true;
            }
            else
                return; // malformed !important — discard the entire declaration
        }

        if (!propName.StartsWith("--", StringComparison.Ordinal)
            && propValue.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            propValue = ResolveKnownCustomProperties(propValue, properties);
        }

        // Snapshot current property keys only for shorthand properties that
        // expand into multiple longhands — avoids HashSet allocation for the
        // common case of simple (non-shorthand) declarations.
        HashSet<string> keysBefore = null;
        if (isImportant && IsShorthandProperty(propName))
        {
            keysBefore = new HashSet<string>(properties.Keys, StringComparer.OrdinalIgnoreCase);
        }

        switch (propName)
        {
            case "width":
            case "height":
            case "lineheight":
                ParseLengthProperty(propName, propValue, properties);
                break;
            case "color":
            case "backgroundcolor":
            case "bordertopcolor":
            case "borderbottomcolor":
            case "borderleftcolor":
            case "borderrightcolor":
                ParseColorProperty(propName, propValue, properties);
                break;
            case "font":
                ParseFontProperty(propValue, properties);
                break;
            case "border":
                ParseBorderProperty(propValue, null, properties);
                break;
            case "border-left":
                ParseBorderProperty(propValue, "-left", properties);
                break;
            case "border-top":
                ParseBorderProperty(propValue, "-top", properties);
                break;
            case "border-right":
                ParseBorderProperty(propValue, "-right", properties);
                break;
            case "border-bottom":
                ParseBorderProperty(propValue, "-bottom", properties);
                break;
            case "border-inline":
                ParseBorderProperty(propValue, "-left", properties);
                ParseBorderProperty(propValue, "-right", properties);
                break;
            case "border-block":
                ParseBorderProperty(propValue, "-top", properties);
                ParseBorderProperty(propValue, "-bottom", properties);
                break;
            case "margin":
                ParseMarginProperty(propValue, properties);
                break;
            case "border-style":
                ParseBorderStyleProperty(propValue, properties);
                break;
            case "border-width":
                ParseBorderWidthProperty(propValue, properties);
                break;
            case "border-color":
                ParseBorderColorProperty(propValue, properties);
                break;
            case "padding":
                ParsePaddingProperty(propValue, properties);
                break;
            case "background-image":
                properties["background-image"] = ParseImageProperty(propValue);
                break;
            case "background":
                ParseBackgroundShorthand(propValue, properties);
                break;
            case "content":
                properties["content"] = ParseImageProperty(propValue);
                break;
            case "font-family":
                properties["font-family"] = ParseFontFamilyProperty(propValue);
                break;
            case "border-radius":
                properties["corner-radius"] = propValue;
                break;
            case "columns":
                // CSS Multi-column §3: 'columns' shorthand — a bare
                // integer sets column-count; we ignore column-width.
                {
                    var trimmed = propValue.Trim();
                    if (int.TryParse(trimmed, out _))
                        properties["column-count"] = trimmed;
                    else if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        properties["column-count"] = "auto";
                }
                break;
            default:
                // CSS2.1 §4.1.8: Ignore declarations with illegal values.
                // Validate enumerated CSS properties to reject unknown keywords
                // (e.g. "white-space: x-bogus" must be discarded).
                if (IsValidPropertyValue(propName, propValue))
                    properties[propName] = propValue;
                break;
        }

        // CSS2.1 §6.4.2: Record which longhand properties carry the
        // !important flag so that the cascade can give them priority.
        if (isImportant)
        {
            importantProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keysBefore != null)
            {
                // Shorthand: mark all newly-expanded longhand keys.
                foreach (var key in properties.Keys)
                {
                    if (!keysBefore.Contains(key))
                        importantProperties.Add(key);
                }
            }
            else
            {
                // Non-shorthand: mark the stored key directly.
                // Handle renamed properties (border-radius → corner-radius).
                if (properties.ContainsKey(propName))
                    importantProperties.Add(propName);
                else if (propName == "border-radius" && properties.ContainsKey("corner-radius"))
                    importantProperties.Add("corner-radius");
            }
        }
    }

    private static string ResolveKnownCustomProperties(string value, Dictionary<string, string> properties, int depth = 0)
    {
        if (string.IsNullOrEmpty(value)
            || depth >= 8
            || value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        bool changed = false;
        int position = 0;

        while (position < value.Length)
        {
            int varIndex = value.IndexOf("var(", position, StringComparison.OrdinalIgnoreCase);
            if (varIndex < 0)
            {
                sb.Append(value, position, value.Length - position);
                break;
            }

            sb.Append(value, position, varIndex - position);

            int openParenIndex = varIndex + 3;
            int closeParenIndex = FindMatchingClosingParen(value, openParenIndex);
            if (closeParenIndex < 0)
            {
                string inner = value[(openParenIndex + 1)..];
                string recovered = ResolveVarFunction(inner, properties, depth + 1);
                if (recovered == $"var({inner})")
                {
                    sb.Append(value, varIndex, value.Length - varIndex);
                }
                else
                {
                    sb.Append(recovered);
                    changed = true;
                }
                break;
            }

            string varFunction = value.Substring(varIndex, closeParenIndex - varIndex + 1);
            string replacement = ResolveVarFunction(
                value.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1),
                properties,
                depth + 1);

            if (replacement == varFunction)
            {
                sb.Append(varFunction);
            }
            else
            {
                sb.Append(replacement);
                changed = true;
            }

            position = closeParenIndex + 1;
        }

        return changed ? sb.ToString() : value;
    }

    private static string ResolveVarFunction(string inner, Dictionary<string, string> properties, int depth)
    {
        string propertyName = inner.Trim();
        string fallback = string.Empty;
        bool hasFallback = false;

        int commaIndex = FindTopLevelChar(inner, ',');
        if (commaIndex >= 0)
        {
            propertyName = inner[..commaIndex].Trim();
            fallback = inner[(commaIndex + 1)..].Trim();
            hasFallback = true;
        }

        if (!propertyName.StartsWith("--", StringComparison.Ordinal))
            return $"var({inner})";

        if (properties.TryGetValue(propertyName, out var propertyValue))
            return ResolveKnownCustomProperties(propertyValue, properties, depth);

        if (hasFallback)
            return ResolveKnownCustomProperties(fallback, properties, depth);

        return $"var({inner})";
    }

    private static int FindMatchingClosingParen(string value, int openParenIndex)
    {
        int depth = 0;
        for (int i = openParenIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static int FindTopLevelChar(string value, char target)
    {
        int depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
                depth--;
            else if (value[i] == target && depth == 0)
                return i;
        }

        return -1;
    }

    private static bool IsShorthandProperty(string propName) => propName switch
    {
        "font" or "border" or "border-left" or "border-top" or "border-right" or
        "border-bottom" or "border-inline" or "border-block" or
        "margin" or "border-style" or "border-width" or
        "border-color" or "padding" or "background" or "columns" => true,
        _ => false
    };

    /// <summary>
    /// CSS2.1 §4.1.8: Validates property values for CSS properties that accept
    /// only a fixed set of keywords. Returns <c>true</c> if the property is not
    /// an enumerated property or if the value is a valid keyword; <c>false</c>
    /// if the value is an unknown keyword for an enumerated property.
    /// Keyword sets include both CSS2.1 values and commonly-used CSS3 values
    /// (e.g. <c>flex</c>, <c>grid</c>, <c>sticky</c>) for forward compatibility.
    /// </summary>
    private static bool IsValidPropertyValue(string propName, string propValue)
    {
        var lower = propValue.ToLowerInvariant();

        // "inherit" and "initial" are valid for all CSS properties.
        if (lower is "inherit" or "initial" or "unset")
            return true;

        return propName switch
        {
            "white-space" => lower is "normal" or "pre" or "nowrap" or "pre-wrap" or "pre-line" or "break-spaces",
            "visibility" => lower is "visible" or "hidden" or "collapse",
            "overflow" or "overflow-x" or "overflow-y" => lower is "visible" or "hidden" or "scroll" or "auto",
            "display" => lower is "block" or "inline" or "inline-block" or "none" or "list-item"
                or "table" or "table-row" or "table-cell" or "table-row-group"
                or "table-header-group" or "table-footer-group" or "table-column"
                or "table-column-group" or "table-caption" or "flex" or "inline-flex"
                or "grid" or "inline-grid" or "contents" or "run-in",
            "position" => lower is "static" or "relative" or "absolute" or "fixed" or "sticky",
            "float" or "cssfloat" => lower is "left" or "right" or "none",
            "clear" => lower is "left" or "right" or "both" or "none",
            "text-align" => lower is "left" or "right" or "center" or "justify" or "start" or "end",
            "text-transform" => lower is "capitalize" or "uppercase" or "lowercase" or "none" or "full-width",
            "font-style" => lower is "normal" or "italic" or "oblique",
            "font-variant" => lower is "normal" or "small-caps",
            "list-style-type" => lower is "disc" or "circle" or "square" or "decimal"
                or "decimal-leading-zero" or "lower-roman" or "upper-roman"
                or "lower-alpha" or "upper-alpha" or "lower-latin" or "upper-latin"
                or "lower-greek" or "armenian" or "georgian" or "none",
            "list-style-position" => lower is "inside" or "outside",
            "border-collapse" => lower is "collapse" or "separate",
            "empty-cells" => lower is "show" or "hide",
            "table-layout" => lower is "auto" or "fixed",
            "caption-side" => lower is "top" or "bottom",
            "direction" => lower is "ltr" or "rtl",
            "unicode-bidi" => lower is "normal" or "embed" or "bidi-override" or "isolate" or "isolate-override" or "plaintext",
            "word-break" => lower is "normal" or "break-all" or "keep-all" or "break-word",
            "overflow-wrap" or "word-wrap" => lower is "normal" or "break-word" or "anywhere",
            "box-sizing" => lower is "content-box" or "border-box",
            "mix-blend-mode" => lower is "normal" or "multiply" or "screen" or "overlay" or "darken"
                or "lighten" or "color-dodge" or "color-burn" or "hard-light" or "soft-light"
                or "difference" or "exclusion" or "hue" or "saturation" or "color" or "luminosity"
                or "plus-lighter",
            "background-blend-mode" => lower is "normal" or "multiply" or "screen" or "overlay" or "darken"
                or "lighten" or "color-dodge" or "color-burn" or "hard-light" or "soft-light"
                or "difference" or "exclusion" or "hue" or "saturation" or "color" or "luminosity",
            "isolation" => lower is "auto" or "isolate",
            "background-origin" => lower is "border-box" or "padding-box" or "content-box",
            "background-clip" => lower is "border-box" or "padding-box" or "content-box" or "border-area" or "text",
            "filter" => lower is "none" || lower.Contains('('),
            "transform" => lower is "none" || lower.Contains('('),
            _ => true, // Unknown property — accept any value
        };
    }

    private static void ParseLengthProperty(string propName, string propValue, Dictionary<string, string> properties)
    {
        if (CssValueParser.IsValidLength(propValue) ||
            propValue.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase) ||
            propValue.Equals(CssConstants.Inherit, StringComparison.OrdinalIgnoreCase) ||
            propValue.Equals("initial", StringComparison.OrdinalIgnoreCase) ||
            propValue.Equals("unset", StringComparison.OrdinalIgnoreCase) ||
            IsValidAttrLengthExpression(propValue))
        properties[propName] = propValue;
    }

    private static readonly Regex LengthAttrFunctionPattern = new(
        @"attr\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+type\(\s*<length>\s*\)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsValidAttrLengthExpression(string propValue)
    {
        if (string.IsNullOrWhiteSpace(propValue) ||
            propValue.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var normalized = LengthAttrFunctionPattern.Replace(
            propValue,
            match =>
            {
                var fallback = match.Groups["fallback"].Success
                    ? match.Groups["fallback"].Value.Trim()
                    : string.Empty;

                return CssValueParser.IsValidLength(fallback) ? fallback : "1px";
            });

        return !ReferenceEquals(normalized, propValue) &&
               CssValueParser.IsValidLength(normalized);
    }

    /// <summary>
    /// Parses the CSS <c>background</c> shorthand into its individual longhand properties.
    /// CSS2.1 §14.2.1: <c>background: [color] [image] [repeat] [attachment] [position]</c>.
    /// Tokens can appear in any order (except position values, which are taken as a pair).
    /// </summary>
    /// <remarks>
    /// Implements TODO-24 (acid3-compliance.md §11.5): extracts <c>url(data:…)</c>
    /// first (handling nested parentheses and percent-encoded characters), then
    /// tokenises the remainder.  Color keywords (e.g. <c>white</c>) are validated
    /// via <c>CssValueParser.IsColorValid()</c>.
    /// See also: <c>DomBridge.Css.ExpandBackgroundShorthand()</c> for the CSSOM-path
    /// counterpart.  Tests: <c>Acid3Todo24_28Tests.cs</c>.
    /// </remarks>
    private void ParseBackgroundShorthand(string propValue, Dictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(propValue))
            return;

        // CSS3 Backgrounds §3.10: The background shorthand supports multiple
        // comma-separated layers.  Split on top-level commas first; if there
        // are multiple layers, parse each independently and produce
        // comma-separated longhand values.
        var layers = SplitOnTopLevelCommas(propValue);
        if (layers.Count > 1)
        {
            ParseMultiLayerBackground(layers, properties);
            return;
        }

        string? color = null;
        string? image = null;
        string? repeat = null;
        string? attachment = null;
        var positionParts = new List<string>();
        bool hasUnrecognizedToken = false;

        // Extract url(...) first, then tokenise the remainder.
        string remaining = propValue;
        int urlStart = remaining.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (urlStart >= 0)
        {
            int depth = 0;
            int urlEnd = urlStart + 4;
            bool closed = false;
            for (; urlEnd < remaining.Length; urlEnd++)
            {
                if (remaining[urlEnd] == '(') depth++;
                else if (remaining[urlEnd] == ')')
                {
                    if (depth == 0) { urlEnd++; closed = true; break; }
                    depth--;
                }
            }
            if (closed)
            {
                image = remaining.Substring(urlStart, urlEnd - urlStart);
                remaining = remaining.Substring(0, urlStart) + remaining.Substring(urlEnd);
            }
        }

        // Extract CSS gradient functions (linear-gradient, radial-gradient, etc.)
        // before tokenising so they are not rejected as unrecognised tokens.
        string? gradientFunc = null;
        if (image == null)
        {
            gradientFunc = ExtractGradientFunction(ref remaining);
            if (gradientFunc != null)
                image = gradientFunc;
        }

        // Tokenise the rest, respecting parenthesised groups (e.g. rgb(…), rgba(…), hsl(…)).
        string[] tokens = SplitBackgroundTokens(remaining);

        foreach (var token in tokens)
        {
            string t = token.Trim();
            if (string.IsNullOrEmpty(t))
                continue;

            // CSS3 background-size separator: skip '/' and any following
            // size values (e.g. "0em / 15438983.37cm auto").
            if (t == "/")
            {
                // Everything after '/' until the next non-size token is
                // background-size; consume but don't store (not rendered).
                continue;
            }

            // attachment (CSS3 adds 'local' to scroll|fixed)
            if (t.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                attachment = t.ToLowerInvariant();
                continue;
            }

            // CSS3 background-origin / background-clip box values —
            // accept but don't change rendering (not yet implemented).
            if (t.Equals("content-box", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("padding-box", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // repeat (CSS3 adds 'space' and 'round')
            if (t.Equals("repeat", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("repeat-x", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("repeat-y", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("no-repeat", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("space", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("round", StringComparison.OrdinalIgnoreCase))
            {
                repeat = t.ToLowerInvariant();
                continue;
            }

            // position keywords
            if (t.Equals("left", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("right", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("top", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("bottom", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                positionParts.Add(t.ToLowerInvariant());
                continue;
            }

            // Length or percentage (position value)
            if (CssValueParser.IsValidLength(t) || t.EndsWith("%"))
            {
                positionParts.Add(t);
                continue;
            }

            // none keyword (background-image: none)
            if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                image = "none";
                continue;
            }

            // CSS3 background-size keywords (after '/') — accept but
            // don't change rendering (cover/contain/auto not yet implemented).
            if (t.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("contain", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // inherit
            if (t.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                continue;

            // Try as color
            if (color == null && _valueParser.IsColorValid(t))
            {
                color = t;
                continue;
            }

            // CSS2.1 §4.1.7: Unrecognized or duplicate token makes the
            // entire declaration invalid — discard it.
            hasUnrecognizedToken = true;
        }

        if (hasUnrecognizedToken)
            return;

        // CSS2.1 §14.2.1: The 'background' shorthand resets ALL longhand
        // properties to their initial values, then overrides with any
        // values explicitly provided.  Without this reset, a later
        // 'background: none' would not clear an earlier 'background: red'.
        properties["background-color"] = color ?? "transparent";
        properties["background-image"] = image != null ? ParseImageProperty(image) : "none";
        properties["background-repeat"] = repeat ?? "repeat";
        properties["background-attachment"] = attachment ?? "scroll";
        properties["background-position"] = positionParts.Count > 0
            ? string.Join(" ", positionParts)
            : "0% 0%";

        // For uniform CSS gradients (e.g. linear-gradient(green, green)),
        // store the solid color as background-gradient so the paint walker
        // can render it within the element's box (the canvas propagation
        // path only looks at background-color).
        // For two-color linear gradients (e.g. linear-gradient(to bottom, green 50%, red 50%)),
        // store as background-color + background-gradient + background-gradient-angle
        // so the existing two-color gradient rendering pipeline can handle them.
        if (gradientFunc != null)
        {
            string? uniformColor = TryExtractUniformGradientColor(gradientFunc);
            if (uniformColor != null)
            {
                properties["background-gradient"] = uniformColor;
            }
            else
            {
                // Try to extract a two-color linear gradient.
                var twoColor = TryExtractTwoColorLinearGradient(gradientFunc);
                if (twoColor != null)
                {
                    properties["background-color"] = twoColor.Value.color1;
                    properties["background-gradient"] = twoColor.Value.color2;
                    properties["background-gradient-angle"] = twoColor.Value.angle.ToString("F0");
                }
            }
        }
    }

    /// <summary>
    /// CSS3 Backgrounds §3.10: Parses multiple comma-separated background layers.
    /// Each layer may contain an image/gradient plus per-layer properties (position,
    /// repeat, attachment).  Only the final layer may include a background-color.
    /// Produces comma-separated longhand values for multi-layer properties.
    /// </summary>
    private void ParseMultiLayerBackground(List<string> layers, Dictionary<string, string> properties)
    {
        var images = new List<string>();
        var positions = new List<string>();
        var repeats = new List<string>();
        var attachments = new List<string>();
        string? color = null;

        for (int i = 0; i < layers.Count; i++)
        {
            string layerStr = layers[i].Trim();
            if (string.IsNullOrEmpty(layerStr))
                continue;

            string? layerImage = null;
            string? layerRepeat = null;
            string? layerAttachment = null;
            var layerPosition = new List<string>();

            // Extract gradient function from this layer.
            string? gradientFunc = ExtractGradientFunction(ref layerStr);
            if (gradientFunc != null)
                layerImage = gradientFunc;

            // Extract url() if no gradient found.
            if (layerImage == null)
            {
                int urlStart = layerStr.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
                if (urlStart >= 0)
                {
                    int depth = 0;
                    int urlEnd = urlStart + 4;
                    bool closed = false;
                    for (; urlEnd < layerStr.Length; urlEnd++)
                    {
                        if (layerStr[urlEnd] == '(') depth++;
                        else if (layerStr[urlEnd] == ')')
                        {
                            if (depth == 0) { urlEnd++; closed = true; break; }
                            depth--;
                        }
                    }
                    if (closed)
                    {
                        layerImage = layerStr.Substring(urlStart, urlEnd - urlStart);
                        layerStr = layerStr.Substring(0, urlStart) + layerStr.Substring(urlEnd);
                    }
                }
            }

            // Tokenise remaining layer text.
            string[] tokens = SplitBackgroundTokens(layerStr);
            foreach (var token in tokens)
            {
                string t = token.Trim();
                if (string.IsNullOrEmpty(t))
                    continue;

                if (t == "/") continue;

                if (t.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("local", StringComparison.OrdinalIgnoreCase))
                {
                    layerAttachment = t.ToLowerInvariant();
                    continue;
                }

                if (t.Equals("content-box", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("padding-box", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("border-box", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (t.Equals("repeat", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("repeat-x", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("repeat-y", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("no-repeat", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("space", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("round", StringComparison.OrdinalIgnoreCase))
                {
                    layerRepeat = t.ToLowerInvariant();
                    continue;
                }

                if (t.Equals("left", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("right", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("top", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("bottom", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("center", StringComparison.OrdinalIgnoreCase))
                {
                    layerPosition.Add(t.ToLowerInvariant());
                    continue;
                }

                if (CssValueParser.IsValidLength(t) || t.EndsWith("%"))
                {
                    layerPosition.Add(t);
                    continue;
                }

                if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    layerImage = "none";
                    continue;
                }

                if (t.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("contain", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (t.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                    continue;

                // CSS3: background-color is only allowed in the final layer.
                if (i == layers.Count - 1 && color == null && _valueParser.IsColorValid(t))
                {
                    color = t;
                    continue;
                }
            }

            images.Add(layerImage ?? "none");
            positions.Add(layerPosition.Count > 0 ? string.Join(" ", layerPosition) : "0% 0%");
            repeats.Add(layerRepeat ?? "repeat");
            attachments.Add(layerAttachment ?? "scroll");
        }

        properties["background-color"] = color ?? "transparent";
        properties["background-image"] = string.Join(", ", images);
        properties["background-position"] = string.Join(", ", positions);
        properties["background-repeat"] = string.Join(", ", repeats);
        properties["background-attachment"] = string.Join(", ", attachments);
    }

    private void ParseColorProperty(string propName, string propValue, Dictionary<string, string> properties)
    {
        if (_valueParser.IsColorValid(propValue))
            properties[propName] = propValue;
    }

    /// <summary>
    /// Splits a CSS background shorthand value into whitespace-separated tokens,
    /// respecting parenthesised groups so that <c>rgb(150, 150, 150)</c> stays as
    /// a single token instead of being split at the commas and spaces.
    /// </summary>
    private static string[] SplitBackgroundTokens(string value)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if ((c == ' ' || c == '\t') && depth == 0)
            {
                if (sb.Length > 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString());
        return parts.ToArray();
    }

    /// <summary>
    /// CSS gradient function prefixes recognised in the <c>background</c> shorthand.
    /// </summary>
    private static readonly string[] GradientPrefixes =
    [
        "linear-gradient(",
        "radial-gradient(",
        "conic-gradient(",
        "repeating-linear-gradient(",
        "repeating-radial-gradient(",
        "repeating-conic-gradient(",
    ];

    /// <summary>
    /// Extracts a CSS gradient function (e.g. <c>linear-gradient(green, green)</c>)
    /// from <paramref name="remaining"/>, removing it from the string.
    /// Returns the full gradient token, or <c>null</c> if none was found.
    /// </summary>
    private static string? ExtractGradientFunction(ref string remaining)
    {
        foreach (var prefix in GradientPrefixes)
        {
            int start = remaining.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;

            // Walk forward, tracking nested parentheses.
            int depth = 0;
            int end = start + prefix.Length;
            bool closed = false;
            for (; end < remaining.Length; end++)
            {
                if (remaining[end] == '(') depth++;
                else if (remaining[end] == ')')
                {
                    if (depth == 0) { end++; closed = true; break; }
                    depth--;
                }
            }

            if (closed)
            {
                string func = remaining.Substring(start, end - start);
                remaining = remaining.Remove(start, end - start);
                return func;
            }
        }

        return null;
    }

    /// <summary>
    /// If <paramref name="gradientFunc"/> represents a uniform gradient (all
    /// color stops resolve to the same color), returns that color as a CSS
    /// string; otherwise returns <c>null</c>.
    /// <para>
    /// Example: <c>linear-gradient(green, green)</c> → <c>"green"</c>.
    /// </para>
    /// </summary>
    private string? TryExtractUniformGradientColor(string gradientFunc)
    {
        // Strip the function name and outer parentheses.
        int openParen = gradientFunc.IndexOf('(');
        if (openParen < 0) return null;

        string inner = gradientFunc.Substring(openParen + 1).TrimEnd(')').Trim();
        if (string.IsNullOrEmpty(inner)) return null;

        // Split on top-level commas (respecting nested parentheses like rgb()).
        var stops = SplitOnTopLevelCommas(inner);
        if (stops.Count < 2) return null;

        // The first token may be an angle/direction (e.g. "to right", "180deg").
        // Try the last two stops first; if they are the same color, that's our
        // uniform color.
        string lastStop = stops[^1].Trim();
        string prevStop = stops[^2].Trim();

        // Strip position hints (e.g. "green 50%" → "green").
        lastStop = StripPositionHint(lastStop);
        prevStop = StripPositionHint(prevStop);

        if (!_valueParser.IsColorValid(lastStop) || !_valueParser.IsColorValid(prevStop))
            return null;

        // Check if both colors resolve to the same value.
        Color c1 = _valueParser.GetActualColor(lastStop);
        Color c2 = _valueParser.GetActualColor(prevStop);
        if (c1 == c2)
            return lastStop;

        return null;
    }

    /// <summary>
    /// Extracts a two-color linear gradient into its component colors and
    /// angle.  Returns <c>null</c> for non-linear, single-color, or
    /// gradients with more than two distinct color stops.
    /// <para>
    /// Example: <c>linear-gradient(to bottom, green 50%, red 50%)</c>
    /// → (color1: "green", color2: "red", angle: 180).
    /// </para>
    /// </summary>
    private (string color1, string color2, double angle)? TryExtractTwoColorLinearGradient(string gradientFunc)
    {
        if (!gradientFunc.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase))
            return null;

        int openParen = gradientFunc.IndexOf('(');
        if (openParen < 0) return null;

        string inner = gradientFunc.Substring(openParen + 1).TrimEnd(')').Trim();
        if (string.IsNullOrEmpty(inner)) return null;

        var tokens = SplitOnTopLevelCommas(inner);
        if (tokens.Count < 2) return null;

        double angle = 180; // default: to bottom
        int colorStartIdx = 0;

        // First token may be a direction or angle.
        string first = tokens[0].Trim().ToLowerInvariant();
        if (first.StartsWith("to "))
        {
            colorStartIdx = 1;
            angle = first switch
            {
                "to top" => 0,
                "to right" => 90,
                "to bottom" => 180,
                "to left" => 270,
                "to top right" or "to right top" => 45,
                "to bottom right" or "to right bottom" => 135,
                "to bottom left" or "to left bottom" => 225,
                "to top left" or "to left top" => 315,
                _ => 180
            };
        }
        else if (first.EndsWith("deg"))
        {
            if (double.TryParse(first.AsSpan(0, first.Length - 3),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double deg))
            {
                angle = deg;
                colorStartIdx = 1;
            }
        }

        // Need exactly two color stops after the direction.
        var colorTokens = tokens.Skip(colorStartIdx).ToList();
        if (colorTokens.Count != 2) return null;

        string c1 = StripPositionHint(colorTokens[0].Trim());
        string c2 = StripPositionHint(colorTokens[1].Trim());

        if (!_valueParser.IsColorValid(c1) || !_valueParser.IsColorValid(c2))
            return null;

        return (c1, c2, angle);
    }

    /// <summary>
    private static List<string> SplitOnTopLevelCommas(string value)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (c == ',' && depth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>
    /// Removes trailing length/percentage position hints from a gradient
    /// color-stop token (e.g. <c>"green 50%"</c> → <c>"green"</c>).
    /// </summary>
    private static string StripPositionHint(string stop)
    {
        // A stop may be "color pos" or just "color". Split and take the
        // first token that is not a length/percentage.
        var parts = stop.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return stop;

        // Walk from the end; drop trailing length/percentage values.
        int end = parts.Length;
        while (end > 1 && (CssValueParser.IsValidLength(parts[end - 1]) || parts[end - 1].EndsWith("%")))
            end--;

        return string.Join(' ', parts[..end]);
    }

    /// <summary>
    /// Converts CSS2.1 attribute selectors to simpler equivalents:
    /// <c>[class~=value]</c> → <c>.value</c> (word-match on class attribute).
    /// Other attribute selectors are removed.  Returns <c>null</c> if the
    /// result is an invalid/empty selector (e.g. bare <c>[class=a b]</c>
    /// per CSS2.1 grammar).
    /// </summary>
    private static string StripAttributeSelectors(string selector, out List<CssAttributeCondition> conditions)
    {
        conditions = null;

        if (selector.IndexOf('[') < 0)
            return selector;

        var sb = new StringBuilder(selector.Length);
        var addedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < selector.Length)
        {
            if (selector[i] == '[')
            {
                int close = selector.IndexOf(']', i);
                if (close < 0) break;

                var inner = selector.Substring(i + 1, close - i - 1);
                i = close + 1;

                // [class~=value] → .value (if not already present)
                if (inner.StartsWith("class~=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = inner.Substring(7).Trim('"', '\'', ' ');
                    if (!string.IsNullOrEmpty(val) && addedClasses.Add(val))
                        sb.Append('.').Append(val);
                }
                // [class=value] — exact match
                else if (inner.StartsWith("class=", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = inner.Substring(6);
                    // CSS2.1 §4.1.3: backslash-escaped characters like "second\ two"
                    // are valid IDENTs. Normalize by removing backslashes before spaces.
                    var val = raw.Replace("\\ ", " ").Trim('"', '\'', ' ');
                    bool hasBackslashEscape = raw.Contains('\\');
                    bool isQuoted = raw.Contains('"') || raw.Contains('\'');
                    // Bare unquoted/unescaped space → invalid selector per CSS2.1 grammar
                    if (val.Contains(' ') && !isQuoted && !hasBackslashEscape)
                        return null;
                    // Convert to class selectors: "second two" → .second.two
                    if (!string.IsNullOrEmpty(val))
                    {
                        var words = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var w in words)
                        {
                            if (addedClasses.Add(w))
                                sb.Append('.').Append(w);
                        }
                    }
                }
                // Other attribute selectors (e.g. [dir="rtl"], [hidden]):
                // parse and store as attribute conditions so they can be
                // checked at match time instead of discarding the rule.
                else
                {
                    conditions ??= [];
                    var eqIdx = inner.IndexOfAny(['=', '~', '|', '^', '$', '*']);
                    if (eqIdx >= 0)
                    {
                        string attrName, attrOp, attrValue;
                        // Check for two-character operators (e.g., ~=, |=, ^=, $=, *=)
                        if (eqIdx > 0 && "~|^$*".Contains(inner[eqIdx]) && eqIdx + 1 < inner.Length && inner[eqIdx + 1] == '=')
                        {
                            attrName = inner[..eqIdx].Trim();
                            attrOp = inner.Substring(eqIdx, 2);
                            attrValue = inner[(eqIdx + 2)..].Trim().Trim('"', '\'');
                        }
                        else
                        {
                            attrName = inner[..eqIdx].Trim();
                            attrOp = "=";
                            attrValue = inner[(eqIdx + 1)..].Trim().Trim('"', '\'');
                        }
                        conditions.Add(new CssAttributeCondition(attrName, attrOp, attrValue));
                    }
                    else
                    {
                        // Presence-only: [hidden], [disabled]
                        conditions.Add(new CssAttributeCondition(inner.Trim(), null, null));
                    }
                    // Don't add to sb — strip from selector text
                }
            }
            else
            {
                sb.Append(selector[i]);
                i++;
            }
        }

        // Remove duplicate class parts: e.g. ".one.first.one" → ".first.one"
        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            return null;

        // Deduplicate class parts within each compound selector segment
        if (result.IndexOf('.') >= 0)
            result = DeduplicateClassParts(result);

        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// Removes duplicate class parts from compound selectors.
    /// E.g. ".one.first.one" → ".first.one".
    /// Preserves ordering and non-class content (spaces, combinators, tag names).
    /// </summary>
    private static string DeduplicateClassParts(string selector)
    {
        // Split by whitespace/combinators to handle multi-part selectors
        var sb = new StringBuilder(selector.Length);
        int i = 0;
        while (i < selector.Length)
        {
            if (selector[i] == ' ' || selector[i] == '>' || selector[i] == '+')
            {
                sb.Append(selector[i]);
                i++;
                continue;
            }

            // Find the end of this compound selector
            int start = i;
            while (i < selector.Length && selector[i] != ' ' && selector[i] != '>' && selector[i] != '+')
                i++;
            var compound = selector.Substring(start, i - start);

            // Deduplicate dot-separated parts
            var parts = compound.Split('.');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new StringBuilder();
            for (int p = 0; p < parts.Length; p++)
            {
                if (p == 0 && string.IsNullOrEmpty(parts[p]))
                {
                    // Leading dot produces empty first part
                    continue;
                }
                if (seen.Add(parts[p]))
                {
                    deduped.Append('.').Append(parts[p]);
                }
            }
            sb.Append(deduped);
        }
        return sb.ToString();
    }

    private void ParseFontProperty(string propValue, Dictionary<string, string> properties)
    {
        // CSS2.1 §15.8: 'font: inherit' sets all font sub-properties to inherit.
        if (propValue.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            properties["font-style"] = "inherit";
            properties["font-variant"] = "inherit";
            properties["font-weight"] = "inherit";
            properties["font-size"] = "inherit";
            properties["line-height"] = "inherit";
            properties["font-family"] = "inherit";
            return;
        }

        string mustBe = RegexParserUtils.Search(RegexParserUtils.CssFontSizeAndLineHeightRegex(), propValue, out int mustBePos);

        if (!string.IsNullOrEmpty(mustBe))
        {
            mustBe = mustBe.Trim();
            //Check for style||variant||weight on the left
            string leftSide = propValue.Substring(0, mustBePos);
            string fontStyle = RegexParserUtils.Search(RegexParserUtils.CssFontStyleRegex(), leftSide);
            string fontVariant = RegexParserUtils.Search(RegexParserUtils.CssFontVariantRegex(), leftSide);
            string fontWeight = RegexParserUtils.Search(RegexParserUtils.CssFontWeightRegex(), leftSide);

            //Check for family on the right
            string rightSide = propValue.Substring(mustBePos + mustBe.Length);
            string fontFamily = rightSide.Trim(); //Parser.Search(Parser.CssFontFamily, rightSide); //TODO: Would this be right?

            // CSS 2.1 §15.8: The font shorthand requires both font-size and
            // font-family.  If the font-family portion is empty after stripping
            // quotes (e.g. font: 48px ''), the declaration is invalid and the
            // entire shorthand must be discarded.  Commas are also stripped
            // because they are only font-family list separators, never part of
            // a font name, so "'', ''" is equally empty.
            string strippedFamily = fontFamily.Trim('"', '\'', ' ', ',');
            if (string.IsNullOrEmpty(strippedFamily))
                return;

            //Check for font-size and line-height
            string fontSize = mustBe;
            string lineHeight = string.Empty;

            if (mustBe.Contains("/") && mustBe.Length > mustBe.IndexOf("/", StringComparison.Ordinal) + 1)
            {
                int slashPos = mustBe.IndexOf("/", StringComparison.Ordinal);
                fontSize = mustBe.Substring(0, slashPos);
                lineHeight = mustBe.Substring(slashPos + 1);
            }

            if (!string.IsNullOrEmpty(fontFamily))
                properties["font-family"] = ParseFontFamilyProperty(fontFamily);

            // CSS 2.1 §15.8: The font shorthand resets all sub-properties.
            // When a component is omitted, it reverts to its initial value.
            properties["font-style"] = !string.IsNullOrEmpty(fontStyle) ? fontStyle : "normal";
            properties["font-variant"] = !string.IsNullOrEmpty(fontVariant) ? fontVariant : "normal";
            properties["font-weight"] = !string.IsNullOrEmpty(fontWeight) ? fontWeight : "normal";

            if (!string.IsNullOrEmpty(fontSize))
                properties["font-size"] = fontSize;

            if (!string.IsNullOrEmpty(lineHeight))
                properties["line-height"] = lineHeight;
        }
        else
        {
            // Check for: caption | icon | menu | message-box | small-caption | status-bar
            //TODO: Interpret font values of: caption | icon | menu | message-box | small-caption | status-bar
        }
    }

    private static string ParseImageProperty(string propValue)
    {
        var layers = SplitImageLayers(propValue);
        if (layers.Count <= 1)
            return ParseSingleImageLayer(propValue);

        for (int i = 0; i < layers.Count; i++)
            layers[i] = ParseSingleImageLayer(layers[i]);

        return string.Join(", ", layers);
    }

    private static string ParseSingleImageLayer(string propValue)
    {
        int startIdx = propValue.IndexOf("url(", StringComparison.InvariantCultureIgnoreCase);

        if (startIdx <= -1)
            return propValue.Trim();

        startIdx += 4;

        var endIdx = propValue.IndexOf(')', startIdx);
        if (endIdx > -1)
        {
            endIdx -= 1;

            while (startIdx < endIdx && (char.IsWhiteSpace(propValue[startIdx]) || propValue[startIdx] == '\'' || propValue[startIdx] == '"'))
                startIdx++;

            while (startIdx < endIdx && (char.IsWhiteSpace(propValue[endIdx]) || propValue[endIdx] == '\'' || propValue[endIdx] == '"'))
                endIdx--;

            if (startIdx <= endIdx)
                return propValue.Substring(startIdx, endIdx - startIdx + 1);
        }

        return propValue.Trim();
    }

    private static List<string> SplitImageLayers(string propValue)
    {
        var layers = new List<string>();
        if (string.IsNullOrWhiteSpace(propValue))
            return layers;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < propValue.Length; i++)
        {
            switch (propValue[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0)
                        depth--;
                    break;
                case ',' when depth == 0:
                    layers.Add(propValue[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        layers.Add(propValue[start..].Trim());
        return layers;
    }

    private string ParseFontFamilyProperty(string propValue)
    {
        int start = 0;

        while (start < propValue.Length)
        {
            while (start < propValue.Length && (char.IsWhiteSpace(propValue[start]) || propValue[start] == ',' || propValue[start] == '\'' || propValue[start] == '"'))
                start++;

            var end = propValue.IndexOf(',', start);
            if (end < 0)
                end = propValue.Length;

            var adjEnd = end - 1;
            while (char.IsWhiteSpace(propValue[adjEnd]) || propValue[adjEnd] == '\'' || propValue[adjEnd] == '"')
                adjEnd--;

            var font = propValue.Substring(start, adjEnd - start + 1);

            if (_colorResolver.IsFontExists(font))
                return font;

            start = end;
        }

        return CssConstants.Inherit;
    }

    private void ParseBorderProperty(string propValue, string direction, Dictionary<string, string> properties)
    {
        ParseBorder(propValue, out string borderWidth, out string borderStyle, out string borderColor);

        if (direction != null)
        {
            // CSS2.1 §8.5.1: The border shorthand resets ALL sub-properties.
            // When a component is omitted, reset to its initial value.
            properties["border" + direction + "-width"] = borderWidth ?? "medium";
            properties["border" + direction + "-style"] = borderStyle ?? "none";
            properties["border" + direction + "-color"] = borderColor ?? "black";
        }
        else
        {
            // CSS2.1 §8.5: The generic border shorthand resets ALL
            // sub-properties.  When a component (width, style, or
            // color) is omitted, use the initial value.
            ParseBorderWidthProperty(borderWidth ?? "medium", properties);
            ParseBorderStyleProperty(borderStyle ?? "none", properties);
            ParseBorderColorProperty(borderColor ?? "black", properties);
        }
    }

    private static void ParseMarginProperty(string propValue, Dictionary<string, string> properties)
    {
        SplitMultiDirectionValues(propValue, out string left, out string top, out string right, out string bottom);

        if (left != null)
            properties["margin-left"] = left;

        if (top != null)
            properties["margin-top"] = top;

        if (right != null)
            properties["margin-right"] = right;

        if (bottom != null)
            properties["margin-bottom"] = bottom;
    }

    private static void ParseBorderStyleProperty(string propValue, Dictionary<string, string> properties)
    {
        SplitMultiDirectionValues(propValue, out string left, out string top, out string right, out string bottom);

        if (left != null)
            properties["border-left-style"] = left;

        if (top != null)
            properties["border-top-style"] = top;

        if (right != null)
            properties["border-right-style"] = right;

        if (bottom != null)
            properties["border-bottom-style"] = bottom;
    }

    private static void ParseBorderWidthProperty(string propValue, Dictionary<string, string> properties)
    {
        SplitMultiDirectionValues(propValue, out string left, out string top, out string right, out string bottom);

        if (left != null)
            properties["border-left-width"] = left;

        if (top != null)
            properties["border-top-width"] = top;

        if (right != null)
            properties["border-right-width"] = right;

        if (bottom != null)
            properties["border-bottom-width"] = bottom;
    }

    private static void ParseBorderColorProperty(string propValue, Dictionary<string, string> properties)
    {
        SplitMultiDirectionValues(propValue, out string left, out string top, out string right, out string bottom);

        if (left != null)
            properties["border-left-color"] = left;

        if (top != null)
            properties["border-top-color"] = top;

        if (right != null)
            properties["border-right-color"] = right;

        if (bottom != null)
            properties["border-bottom-color"] = bottom;
    }

    private static void ParsePaddingProperty(string propValue, Dictionary<string, string> properties)
    {
        SplitMultiDirectionValues(propValue, out string left, out string top, out string right, out string bottom);

        if (left != null)
            properties["padding-left"] = left;

        if (top != null)
            properties["padding-top"] = top;

        if (right != null)
            properties["padding-right"] = right;

        if (bottom != null)
            properties["padding-bottom"] = bottom;
    }

    private static void SplitMultiDirectionValues(string propValue, out string left, out string top, out string right, out string bottom)
    {
        top = null;
        left = null;
        right = null;
        bottom = null;

        string[] values = SplitValues(propValue);

        switch (values.Length)
        {
            case 1:
                top = left = right = bottom = values[0];
                break;
            case 2:
                top = bottom = values[0];
                left = right = values[1];
                break;
            case 3:
                top = values[0];
                left = right = values[1];
                bottom = values[2];
                break;
            case 4:
                top = values[0];
                right = values[1];
                bottom = values[2];
                left = values[3];
                break;
        }
    }

    private static string[] SplitValues(string value, char separator = ' ')
    {
        if (string.IsNullOrEmpty(value))
            return [];

        var result = new List<string>();
        var current = new StringBuilder();
        int parenDepth = 0;
        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (inDoubleQuote)
            {
                current.Append(c);
                if (c == '\\' && i + 1 < value.Length)
                {
                    current.Append(value[++i]);
                }
                else if (c == '"')
                    inDoubleQuote = false;
            }
            else if (inSingleQuote)
            {
                current.Append(c);
                if (c == '\\' && i + 1 < value.Length)
                {
                    current.Append(value[++i]);
                }
                else if (c == '\'')
                    inSingleQuote = false;
            }
            else if (c == '"')
            {
                current.Append(c);
                inDoubleQuote = true;
            }
            else if (c == '\'')
            {
                current.Append(c);
                inSingleQuote = true;
            }
            else if (c == '(')
            {
                current.Append(c);
                parenDepth++;
            }
            else if (c == ')')
            {
                current.Append(c);
                if (parenDepth > 0)
                    parenDepth--;
            }
            else if (c == separator && parenDepth == 0)
            {
                var val = current.ToString().Trim();
                if (val.Length > 0)
                    result.Add(val);
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
            result.Add(last);

        return result.ToArray();
    }

    public void ParseBorder(string value, out string width, out string style, out string color)
    {
        width = style = color = null;
        if (!string.IsNullOrEmpty(value))
        {
            int idx = 0;
            while ((idx = GetNextBorderToken(value, idx, out int length)) > -1)
            {
                // CSS2.1 §8.5.1: Each token must match exactly one of width,
                // style, or color.  Use exclusive matching (width > style > color)
                // so that tokens like "1em" are not consumed as both a width
                // and a color (the fallback color resolver treats unknown names
                // as black, which would otherwise match every token).
                if (width == null)
                {
                    var w = ParseBorderWidth(value, idx, length);
                    if (w != null) { width = w; goto next; }
                }
                if (style == null)
                {
                    var s = ParseBorderStyle(value, idx, length);
                    if (s != null) { style = s; goto next; }
                }
                if (color == null)
                {
                    var c = ParseBorderColor(value, idx, length);
                    if (c != null) { color = c; goto next; }
                }

                next:
                idx = idx + length + 1;
            }
        }
    }

    /// <summary>
    /// Like <see cref="CommonUtils.GetNextSubString"/> but treats parenthesised
    /// groups (e.g. <c>hsla(-39 5% 68% / 7%)</c>) as a single token so that
    /// CSS Color Level 4 functional notation is not split by spaces.
    /// </summary>
    private static int GetNextBorderToken(string str, int idx, out int length)
    {
        while (idx < str.Length && char.IsWhiteSpace(str[idx]))
            idx++;

        if (idx >= str.Length)
        {
            length = 0;
            return -1;
        }

        var endIdx = idx;
        int depth = 0;
        while (endIdx < str.Length)
        {
            char ch = str[endIdx];
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth <= 0) { endIdx++; break; }
            }
            else if (char.IsWhiteSpace(ch) && depth <= 0) break;
            endIdx++;
        }

        length = endIdx - idx;
        return length > 0 ? idx : -1;
    }

    private static string ParseBorderWidth(string str, int idx, int length)
    {
        // CSS2.1: '0' is a valid <length> that requires no unit.
        if (length == 1 && str[idx] == '0')
            return "0";

        if ((length > 1 && char.IsDigit(str[idx])) || (length > 2 && str[idx] == '.'))
        {
            string unit = null;
            if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Px))
                unit = CssConstants.Px;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Pt))
                unit = CssConstants.Pt;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Em))
                unit = CssConstants.Em;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Ex))
                unit = CssConstants.Ex;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.In))
                unit = CssConstants.In;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Cm))
                unit = CssConstants.Cm;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Mm))
                unit = CssConstants.Mm;
            else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Pc))
                unit = CssConstants.Pc;

            if (unit != null)
            {
                if (CssValueParser.IsFloat(str, idx, length - 2))
                    return str.Substring(idx, length);
            }

            // Single-character units (e.g. Q — quarter-millimeter)
            if (unit == null && length > 1)
            {
                char lastChar = char.ToLowerInvariant(str[idx + length - 1]);
                if (lastChar == 'q' && CssValueParser.IsFloat(str, idx, length - 1))
                    return str.Substring(idx, length);
            }
        }
        else
        {
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Thin))
                return CssConstants.Thin;

            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Medium))
                return CssConstants.Medium;

            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Thick))
                return CssConstants.Thick;
        }

        return null;
    }

    private static string ParseBorderStyle(string str, int idx, int length)
    {
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.None))
            return CssConstants.None;

        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Solid))
            return CssConstants.Solid;

        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Hidden))
            return CssConstants.Hidden;

        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Dotted))
            return CssConstants.Dotted;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Dashed))
            return CssConstants.Dashed;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Double))
            return CssConstants.Double;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Groove))
            return CssConstants.Groove;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Ridge))
            return CssConstants.Ridge;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Inset))
            return CssConstants.Inset;
        
        if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Outset))
            return CssConstants.Outset;
        
        return null;
    }

    private string ParseBorderColor(string str, int idx, int length) => _valueParser.TryGetColor(str, idx, length, out _) ? str.Substring(idx, length) : null;
}
