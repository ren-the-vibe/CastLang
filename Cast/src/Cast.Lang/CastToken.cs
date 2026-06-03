#nullable enable
using System;

namespace Cast.Lang;

/// <summary>
/// A lexed token. Carries its text, the kind, and source position for diagnostics.
/// For Number/String/Ident/ScopeSigil the Text is the raw lexeme; for String the
/// CastValue holds the decoded content (with '' collapsed to ').
/// </summary>
public readonly struct CastToken
{
    public CastTokenType Type { get; }
    public string Text { get; }
    /// <summary>Decoded value for strings (with '' -> '); otherwise same as Text.</summary>
    public string CastValue { get; }
    public int Line { get; }
    public int Column { get; }
    public int Position { get; }

    public CastToken(CastTokenType type, string text, string value, int line, int column, int position)
    {
        Type = type;
        Text = text;
        CastValue = value;
        Line = line;
        Column = column;
        Position = position;
    }

    public CastToken(CastTokenType type, string text, int line, int column, int position)
        : this(type, text, text, line, column, position) { }

    public override string ToString() => $"{Type}('{Text}') @ {Line}:{Column}";
}

/// <summary>Raised when the lexer hits input it cannot tokenize.</summary>
public sealed class CastLexException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public CastLexException(string message, int line, int column)
        : base($"Lex error at {line}:{column}: {message}")
    {
        Line = line;
        Column = column;
    }
}
