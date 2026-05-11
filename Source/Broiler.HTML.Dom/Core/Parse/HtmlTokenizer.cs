using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.HTML.Dom.Core.Parse;

/// <summary>Identifies the kind of <see cref="HtmlToken"/>.</summary>
internal enum TokenType
{
    /// <summary>A DOCTYPE token.</summary>
    Doctype,
    /// <summary>A start-tag token.</summary>
    StartTag,
    /// <summary>An end-tag token.</summary>
    EndTag,
    /// <summary>Character data.</summary>
    Character,
    /// <summary>An HTML comment.</summary>
    Comment,
    /// <summary>End of the input stream.</summary>
    EndOfFile
}

/// <summary>A single token emitted by <see cref="HtmlTokenizer"/>.</summary>
/// <remarks>Creates a new <see cref="HtmlToken"/>.</remarks>
internal sealed class HtmlToken(TokenType type, string name = null, string data = null,
    bool selfClosing = false, Dictionary<string, string> attributes = null)
{
    /// <summary>The kind of token.</summary>
    public TokenType Type { get; } = type;
    /// <summary>Tag or doctype name (lower-cased).</summary>
    public string Name { get; } = name;
    /// <summary>Payload for character and comment tokens.</summary>
    public string Data { get; } = data;
    /// <summary>Whether the tag uses self-closing syntax.</summary>
    public bool SelfClosing { get; } = selfClosing;
    /// <summary>Attribute map (keys are lower-cased).</summary>
    public Dictionary<string, string> Attributes { get; } = attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Simplified WHATWG-aligned HTML tokenizer (§13.2.5) that processes an
/// HTML string character-by-character.
/// Shared between Broiler.HTML rendering pipeline and the DomBridge
/// JavaScript execution bridge.
/// </summary>
internal sealed class HtmlTokenizer
{
    private enum State
    {
        Data, TagOpen, EndTagOpen, TagName,
        BeforeAttributeName, AttributeName,
        BeforeAttributeValue, AttributeValueDoubleQuoted,
        AttributeValueSingleQuoted, AttributeValueUnquoted,
        AfterAttributeValueQuoted, SelfClosingStartTag,
        BogusComment, MarkupDeclarationOpen,
        CommentStart, Comment, CommentEndDash, CommentEnd,
        Doctype, RawText
    }

    // Raw text elements: content is treated as text until matching end tag
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style"
    };

    private string _rawTextTag; // tag name for raw text end tag matching

    private string _input;
    private int _pos;
    private State _state;
    private readonly StringBuilder _tag = new();
    private readonly StringBuilder _buf = new();
    private readonly StringBuilder _av = new();
    private Dictionary<string, string> _attrs;
    private string _an;
    private bool _selfClose, _isEnd;

    /// <summary>Tokenizes <paramref name="html"/> into a token sequence.</summary>
    public IEnumerable<HtmlToken> Tokenize(string html)
    {
        if (html == null) throw new ArgumentNullException(nameof(html));
        _input = html; _pos = 0; _state = State.Data;
        _buf.Clear(); _tag.Clear(); _av.Clear();
        _attrs = NewAttrs(); _an = null; _selfClose = _isEnd = false;

        while (true)
        {
            bool eof = _pos >= _input.Length;
            char c = eof ? '\0' : _input[_pos];

            switch (_state)
            {
            case State.Data:
                if (eof) { if (_buf.Length > 0) yield return CharTok(); yield return Eof(); yield break; }
                if (c == '<') { if (_buf.Length > 0) yield return CharTok(); _state = State.TagOpen; _pos++; }
                else { _buf.Append(c); _pos++; }
                break;

            case State.TagOpen:
                if (eof) { _buf.Append('<'); _state = State.Data; }
                else if (c == '!') { _pos++; _state = State.MarkupDeclarationOpen; }
                else if (c == '?') { _pos++; SkipProcessingInstruction(); _state = State.Data; }
                else if (c == '/') { _pos++; _state = State.EndTagOpen; }
                else if (char.IsLetter(c)) { Reset(false); _state = State.TagName; }
                else { _buf.Append('<'); _state = State.Data; }
                break;

            case State.EndTagOpen:
                if (eof) { _buf.Append("</"); _state = State.Data; }
                else if (char.IsLetter(c)) { Reset(true); _state = State.TagName; }
                else { _buf.Clear(); _state = State.BogusComment; }
                break;

            case State.TagName:
                if (eof) { _state = State.Data; }
                else if (char.IsWhiteSpace(c)) { _pos++; _state = State.BeforeAttributeName; }
                else if (c == '/') { _pos++; _state = State.SelfClosingStartTag; }
                else if (c == '>') { _pos++; yield return TagTok(); if (_state != State.RawText) _state = State.Data; }
                else { _tag.Append(char.ToLowerInvariant(c)); _pos++; }
                break;

            case State.BeforeAttributeName:
                if (eof) { Flush(); _state = State.Data; }
                else if (c == '>') { Flush(); _pos++; yield return TagTok(); if (_state != State.RawText) _state = State.Data; }
                else if (c == '/') { Flush(); _pos++; _state = State.SelfClosingStartTag; }
                else if (char.IsWhiteSpace(c)) { _pos++; }
                else { Flush(); _an = string.Empty; _av.Clear(); _state = State.AttributeName; }
                break;

            case State.AttributeName:
                if (eof || c == '>' || c == '/' || char.IsWhiteSpace(c)) { _state = State.BeforeAttributeName; }
                else if (c == '=') { _pos++; _state = State.BeforeAttributeValue; }
                else { _an += char.ToLowerInvariant(c); _pos++; }
                break;

            case State.BeforeAttributeValue:
                if (eof) { _state = State.Data; }
                else if (char.IsWhiteSpace(c)) { _pos++; }
                else if (c == '"') { _pos++; _state = State.AttributeValueDoubleQuoted; }
                else if (c == '\'') { _pos++; _state = State.AttributeValueSingleQuoted; }
                else { _state = State.AttributeValueUnquoted; }
                break;

            case State.AttributeValueDoubleQuoted:
                if (eof) { _state = State.Data; }
                else if (c == '"') { _pos++; _state = State.AfterAttributeValueQuoted; }
                else { _av.Append(c); _pos++; }
                break;

            case State.AttributeValueSingleQuoted:
                if (eof) { _state = State.Data; }
                else if (c == '\'') { _pos++; _state = State.AfterAttributeValueQuoted; }
                else { _av.Append(c); _pos++; }
                break;

            case State.AttributeValueUnquoted:
                if (eof) { Flush(); _state = State.Data; }
                else if (char.IsWhiteSpace(c)) { Flush(); _pos++; _state = State.BeforeAttributeName; }
                else if (c == '>') { Flush(); _pos++; yield return TagTok(); if (_state != State.RawText) _state = State.Data; }
                else { _av.Append(c); _pos++; }
                break;

            case State.AfterAttributeValueQuoted:
                Flush();
                if (eof) { _state = State.Data; }
                else if (char.IsWhiteSpace(c)) { _pos++; _state = State.BeforeAttributeName; }
                else if (c == '/') { _pos++; _state = State.SelfClosingStartTag; }
                else if (c == '>') { _pos++; yield return TagTok(); if (_state != State.RawText) _state = State.Data; }
                else { _state = State.BeforeAttributeName; }
                break;

            case State.SelfClosingStartTag:
                if (eof) { _state = State.Data; }
                else if (c == '>') { _selfClose = true; _pos++; yield return TagTok(); if (_state != State.RawText) _state = State.Data; }
                else { _state = State.BeforeAttributeName; }
                break;

            case State.RawText:
                // Read all content until matching </tagname>
                {
                    var endTag = "</" + _rawTextTag;
                    while (_pos < _input.Length)
                    {
                        if (_input[_pos] == '<' && _pos + 1 < _input.Length && _input[_pos + 1] == '/' &&
                            _pos + 2 + _rawTextTag.Length <= _input.Length &&
                            string.Compare(_input, _pos + 2, _rawTextTag, 0, _rawTextTag.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            var afterTag = _pos + 2 + _rawTextTag.Length;
                            if (afterTag < _input.Length && (_input[afterTag] == '>' || char.IsWhiteSpace(_input[afterTag]) || _input[afterTag] == '/'))
                            {
                                // Found the matching end tag - emit accumulated text
                                if (_buf.Length > 0) yield return CharTok();
                                // Skip to after the '>'
                                _pos = afterTag;
                                while (_pos < _input.Length && _input[_pos] != '>') _pos++;
                                if (_pos < _input.Length) _pos++; // skip '>'
                                // Emit the end tag
                                yield return new HtmlToken(TokenType.EndTag, name: _rawTextTag);
                                _rawTextTag = null;
                                _state = State.Data;
                                break;
                            }
                        }
                        _buf.Append(_input[_pos]);
                        _pos++;
                    }
                    if (_pos >= _input.Length && _state == State.RawText)
                    {
                        // EOF in raw text - emit whatever we have
                        if (_buf.Length > 0) yield return CharTok();
                        _state = State.Data;
                    }
                }
                break;

            case State.MarkupDeclarationOpen:
                if (Ahead("--")) { _pos += 2; _buf.Clear(); _state = State.CommentStart; }
                else if (AheadCI("DOCTYPE")) { _pos += 7; _tag.Clear(); _state = State.Doctype; }
                else { _buf.Clear(); _state = State.BogusComment; }
                break;

            case State.CommentStart:
                if (eof) { yield return ComTok(); _state = State.Data; }
                else if (c == '-') { _pos++; _state = State.CommentEndDash; }
                else if (c == '>') { _pos++; yield return ComTok(); _state = State.Data; }
                else { _state = State.Comment; }
                break;

            case State.Comment:
                if (eof) { yield return ComTok(); _state = State.Data; }
                else if (c == '-') { _pos++; _state = State.CommentEndDash; }
                else { _buf.Append(c); _pos++; }
                break;

            case State.CommentEndDash:
                if (eof) { yield return ComTok(); _state = State.Data; }
                else if (c == '-') { _pos++; _state = State.CommentEnd; }
                else { _buf.Append('-'); _buf.Append(c); _pos++; _state = State.Comment; }
                break;

            case State.CommentEnd:
                if (eof || c == '>') { if (!eof) _pos++; yield return ComTok(); _state = State.Data; }
                else if (c == '-') { _buf.Append('-'); _pos++; }
                else { _buf.Append("--"); _buf.Append(c); _pos++; _state = State.Comment; }
                break;

            case State.Doctype:
                if (eof) { yield return new HtmlToken(TokenType.Doctype); yield return Eof(); yield break; }
                else if (char.IsWhiteSpace(c)) { _pos++; }
                else if (c == '>') { _pos++; yield return new HtmlToken(TokenType.Doctype, name: _tag.ToString()); _state = State.Data; }
                else { ReadDoctypeName(); yield return new HtmlToken(TokenType.Doctype, name: _tag.ToString()); _state = State.Data; }
                break;

            case State.BogusComment:
                if (eof || c == '>') { if (!eof) _pos++; yield return ComTok(); _state = State.Data; }
                else { _buf.Append(c); _pos++; }
                break;
            }
        }
    }

    private static Dictionary<string, string> NewAttrs() =>
        new(StringComparer.OrdinalIgnoreCase);

    private void Reset(bool end)
    {
        _isEnd = end; _tag.Clear(); _selfClose = false;
        _attrs = NewAttrs(); _an = null; _av.Clear();
    }

    private void Flush()
    {
        if (_an != null && _an.Length > 0 && !_attrs.ContainsKey(_an))
            _attrs[_an] = _av.ToString();
        _an = null; _av.Clear();
    }

    private HtmlToken TagTok()
    {
        Flush();
        var tagName = _tag.ToString();
        var tok = new HtmlToken(_isEnd ? TokenType.EndTag : TokenType.StartTag,
            name: tagName, selfClosing: _selfClose, attributes: _attrs);
        // Switch to raw text mode for script/style start tags
        if (!_isEnd && !_selfClose && RawTextElements.Contains(tagName))
        {
            _rawTextTag = tagName;
            _state = State.RawText;
        }
        return tok;
    }

    private HtmlToken CharTok()
    {
        var t = new HtmlToken(TokenType.Character, data: _buf.ToString());
        _buf.Clear(); return t;
    }

    private HtmlToken ComTok()
    {
        var t = new HtmlToken(TokenType.Comment, data: _buf.ToString());
        _buf.Clear(); return t;
    }

    private static HtmlToken Eof() => new(TokenType.EndOfFile);

    private bool Ahead(string s) =>
        _pos + s.Length <= _input.Length && _input.AsSpan(_pos, s.Length).SequenceEqual(s.AsSpan());

    private bool AheadCI(string s) =>
        _pos + s.Length <= _input.Length &&
        string.Compare(_input, _pos, s, 0, s.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private void ReadDoctypeName()
    {
        _tag.Clear();
        while (_pos < _input.Length && _input[_pos] != '>' && !char.IsWhiteSpace(_input[_pos]))
        {
            _tag.Append(char.ToLowerInvariant(_input[_pos]));
            _pos++;
        }
        while (_pos < _input.Length && _input[_pos] != '>')
            _pos++;
        if (_pos < _input.Length)
            _pos++;
    }

    /// <summary>
    /// Skips an XML processing instruction (<c>&lt;?...?&gt;</c>), such as
    /// <c>&lt;?xml version="1.0" encoding="UTF-8"?&gt;</c>.
    /// </summary>
    private void SkipProcessingInstruction()
    {
        while (_pos < _input.Length)
        {
            if (_input[_pos] == '?' && _pos + 1 < _input.Length && _input[_pos + 1] == '>')
            {
                _pos += 2;
                return;
            }
            if (_input[_pos] == '>')
            {
                _pos++;
                return;
            }
            _pos++;
        }
    }
}
