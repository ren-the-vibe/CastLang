#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Cast.Lang;

/// <summary>
/// Hand-written lexer for Cast. Order of checks implements the grammar's
/// maximal-munch rule: multi-char operators are tested before their single-char
/// prefixes. Whitespace separates tokens; newlines become Newline tokens
/// (statement separators). Comments run to end of line.
/// </summary>
public sealed class CastLexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;
    private int _vectorDepth;  // nesting of <...>; inside it, '>' always closes (no '~>' etc.)

    // Globally reserved keywords (Finding 9): always keywords, never identifiers.
    private static readonly Dictionary<string, CastTokenType> Keywords = new()
    {
        ["in"] = CastTokenType.In,
        ["out"] = CastTokenType.Out,
        ["collect"] = CastTokenType.Collect,
        ["iter"] = CastTokenType.Iter,
        ["as"] = CastTokenType.As,
        ["at"] = CastTokenType.AtKw,
        ["over"] = CastTokenType.Over,
    };

    public CastLexer(string source) => _src = source ?? string.Empty;

    private bool AtEnd => _pos >= _src.Length;
    private char Cur => AtEnd ? '\0' : _src[_pos];
    private char Peek(int ahead = 1) => _pos + ahead >= _src.Length ? '\0' : _src[_pos + ahead];

    private char Advance()
    {
        char c = _src[_pos++];
        if (c == '\n') { _line++; _col = 1; }
        else _col++;
        return c;
    }

    private bool Match(string s)
    {
        if (_pos + s.Length > _src.Length) return false;
        for (int i = 0; i < s.Length; i++)
            if (_src[_pos + i] != s[i]) return false;
        return true;
    }

    /// <summary>Tokenize the whole input. Always ends with an EndOfFile token.</summary>
    public List<CastToken> Tokenize()
    {
        var tokens = new List<CastToken>();
        while (true)
        {
            var t = Next();
            tokens.Add(t);
            if (t.Type == CastTokenType.EndOfFile) break;
        }
        return tokens;
    }

    private CastToken Next()
    {
        SkipInlineWhitespace();

        int startLine = _line, startCol = _col, startPos = _pos;

        if (AtEnd)
            return new CastToken(CastTokenType.EndOfFile, "", startLine, startCol, startPos);

        char c = Cur;

        // Newline => statement separator
        if (c == '\n' || c == '\r')
        {
            // consume \r\n or \n or \r as one Newline
            if (c == '\r' && Peek() == '\n') { Advance(); Advance(); }
            else Advance();
            return new CastToken(CastTokenType.Newline, "\n", startLine, startCol, startPos);
        }

        // Comment: // ... to end of line
        if (c == '/' && Peek() == '/')
        {
            var sb = new StringBuilder();
            while (!AtEnd && Cur != '\n' && Cur != '\r') sb.Append(Advance());
            return new CastToken(CastTokenType.Comment, sb.ToString(), startLine, startCol, startPos);
        }

        // String literal: '...'  with '' => embedded '
        if (c == '\'')
            return LexString(startLine, startCol, startPos);

        // Scope sigil: '@' followed by one or more lowercase letters.
        // Validity of the letter combination is a runtime concern (Finding 8).
        if (c == '@')
            return LexScopeSigil(startLine, startCol, startPos);

        // Number: digits with optional single '.', or a leading '.' form (.5 == 0.5).
        // Note: a leading '-' is NOT folded here; it's a separate MINUS the parser's
        // Unary production handles (Finding 3/4).
        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek())))
            return LexNumber(startLine, startCol, startPos);

        // Operators and punctuation, longest-match first (maximal munch).
        var op = LexOperator(startLine, startCol, startPos);
        if (op.HasValue) return op.Value;

        // Identifier / keyword. Identifiers may NOT start with '_' (Finding 6);
        // a bare '_' is the null/fallback sigil, handled in LexOperator above.
        if (IsIdentStart(c))
            return LexIdentOrKeyword(startLine, startCol, startPos);

        throw new CastLexException($"unexpected character '{c}'", startLine, startCol);
    }

    private void SkipInlineWhitespace()
    {
        while (!AtEnd)
        {
            char c = Cur;
            if (c == ' ' || c == '\t') Advance();
            else break;
        }
    }

    private CastToken LexString(int line, int col, int pos)
    {
        Advance(); // opening '
        var raw = new StringBuilder("'");
        var val = new StringBuilder();
        while (true)
        {
            if (AtEnd || Cur == '\n' || Cur == '\r')
                throw new CastLexException("unterminated string", line, col);

            char c = Cur;
            if (c == '\'')
            {
                // '' => embedded single quote; a lone ' closes the string.
                if (Peek() == '\'')
                {
                    raw.Append("''");
                    val.Append('\'');
                    Advance(); Advance();
                    continue;
                }
                raw.Append(Advance()); // closing '
                return new CastToken(CastTokenType.String, raw.ToString(), val.ToString(), line, col, pos);
            }
            raw.Append(c);
            val.Append(c);
            Advance();
        }
    }

    private CastToken LexScopeSigil(int line, int col, int pos)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // '@'
        if (!char.IsLower(Cur))
        {
            // '@' not followed by a scope letter: emit a bare At token; the parser
            // will reject it where a scope is required. (Keeps '@' diagnosable.)
            return new CastToken(CastTokenType.At, "@", line, col, pos);
        }
        while (char.IsLower(Cur)) sb.Append(Advance());
        return new CastToken(CastTokenType.ScopeSigil, sb.ToString(), line, col, pos);
    }

    private CastToken LexNumber(int line, int col, int pos)
    {
        var sb = new StringBuilder();
        // Leading-dot form: .5
        if (Cur == '.')
        {
            sb.Append('0');           // normalize .5 -> 0.5 in the lexeme value
            sb.Append(Advance());     // '.'
            while (char.IsDigit(Cur)) sb.Append(Advance());
            return new CastToken(CastTokenType.Number, sb.ToString(), line, col, pos);
        }
        while (char.IsDigit(Cur)) sb.Append(Advance());
        if (Cur == '.' && char.IsDigit(Peek()))
        {
            sb.Append(Advance()); // '.'
            while (char.IsDigit(Cur)) sb.Append(Advance());
        }
        return new CastToken(CastTokenType.Number, sb.ToString(), line, col, pos);
    }

    /// <summary>
    /// Operator and punctuation matcher. CRITICAL: longer operators must be tested
    /// before their single-char prefixes (maximal munch). This ordering mirrors the
    /// grammar's lexer layer exactly.
    /// </summary>
    private CastToken? LexOperator(int line, int col, int pos)
    {
        // --- two-char (and the '::' delimiter) first ---
        // Inside a vector (<...>), an operator whose final char is '>' must NOT be
        // formed — '>' always closes the vector there (Finding 29). E.g. the trailing
        // bare '~' in <~, 10, ~> must lex as Tilde then Gt, not as TildeArrow.
        foreach (var (lexeme, type) in TwoChar)
        {
            if (Match(lexeme))
            {
                if (_vectorDepth > 0 && lexeme.EndsWith('>'))
                    break; // fall through to single-char handling; '>' closes the vector
                for (int i = 0; i < lexeme.Length; i++) Advance();
                return new CastToken(type, lexeme, line, col, pos);
            }
        }

        // --- one-char ---
        char c = Cur;
        CastTokenType? single = c switch
        {
            '$' => CastTokenType.Dollar,
            '#' => CastTokenType.Hash,
            '!' => CastTokenType.Bang,
            '?' => CastTokenType.Question,
            '*' => CastTokenType.Star,
            '+' => CastTokenType.Plus,
            '-' => CastTokenType.Minus,
            '/' => CastTokenType.Slash,
            '%' => CastTokenType.Percent,
            '^' => CastTokenType.Caret,
            '~' => CastTokenType.Tilde,
            '°' => CastTokenType.Degree,
            ':' => CastTokenType.Colon,
            '|' => CastTokenType.Pipe,
            '=' => CastTokenType.Eq,
            '.' => CastTokenType.Dot,
            ';' => CastTokenType.Semicolon,
            ',' => CastTokenType.Comma,
            '_' => CastTokenType.Underscore,
            '(' => CastTokenType.LParen,
            ')' => CastTokenType.RParen,
            '[' => CastTokenType.LBracket,
            ']' => CastTokenType.RBracket,
            '{' => CastTokenType.LBrace,
            '}' => CastTokenType.RBrace,
            '<' => CastTokenType.Lt,
            '>' => CastTokenType.Gt,
            _ => null
        };

        if (single is { } t)
        {
            Advance();
            if (t == CastTokenType.Lt) _vectorDepth++;
            else if (t == CastTokenType.Gt && _vectorDepth > 0) _vectorDepth--;
            return new CastToken(t, c.ToString(), line, col, pos);
        }
        return null;
    }

    // Two-char operators (plus '::'), ordered longest/most-specific first.
    // Each must precede any one-char prefix it shares (handled by checking these
    // before the single-char switch).
    private static readonly (string, CastTokenType)[] TwoChar =
    {
        ("::", CastTokenType.ColonColon),    // before ':'
        ("++", CastTokenType.PlusPlus),      // before '+'
        ("--", CastTokenType.MinusMinus),    // before '-'
        ("+=", CastTokenType.PlusEq),
        ("-=", CastTokenType.MinusEq),
        ("*=", CastTokenType.StarEq),
        ("/=", CastTokenType.SlashEq),
        ("%=", CastTokenType.PercentEq),
        ("=>", CastTokenType.EqGt),          // before '='
        ("=&", CastTokenType.EqAmp),         // before '='
        ("!?", CastTokenType.BangQuestion),  // before '!'
        ("!&", CastTokenType.BangAmp),       // before '!'
        ("!|", CastTokenType.BangPipe),      // before '!'
        ("&&", CastTokenType.AmpAmp),        // (bare '&' is not a token)
        ("||", CastTokenType.PipePipe),      // before '|'
        ("?>", CastTokenType.QuestionGt),    // before '?'
        ("??", CastTokenType.QuestionQuestion),
        ("->", CastTokenType.Arrow),         // before '-'
        ("~>", CastTokenType.TildeArrow),    // before '~'
        ("|>", CastTokenType.PipeGt),        // before '|'
        ("..", CastTokenType.DotDot),        // before '.'
        // note: '//' comment is handled earlier, before this table
    };

    private CastToken LexIdentOrKeyword(int line, int col, int pos)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // first char (letter)
        while (IsIdentCont(Cur)) sb.Append(Advance());
        string s = sb.ToString();
        if (Keywords.TryGetValue(s, out var kw))
            return new CastToken(kw, s, line, col, pos);
        return new CastToken(CastTokenType.Ident, s, line, col, pos);
    }

    // IdentStart = [A-Za-z]; identifiers may NOT begin with '_' (Finding 6).
    private static bool IsIdentStart(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    // IdentCont = [A-Za-z0-9_]; '_' permitted after the first character.
    private static bool IsIdentCont(char c) =>
        IsIdentStart(c) || (c >= '0' && c <= '9') || c == '_';
}
