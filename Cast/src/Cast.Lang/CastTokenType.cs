namespace Cast.Lang;

/// <summary>
/// The kinds of token the Cast lexer produces. Operator ordering in the lexer
/// follows the grammar's maximal-munch rule: longer operators are matched before
/// their single-character prefixes (e.g. "::" before ":", "=>"/"=&" before "=").
/// </summary>
public enum CastTokenType
{
    // Literals
    Number,         // 5, 3.14, .5  (one numeric kind; no int/float split)
    String,         // '...'  ('' = embedded quote)
    Ident,          // identifier (may not start with '_'; '_' allowed after first char)

    // Sigils / value heads
    Dollar,         // $   variable
    At,             // @   (followed by scope letters — lexed as ScopeSigil below)
    ScopeSigil,     // @e, @np, @v, @t, @w, @s, ...  ('@' + [a-z]+)
    Hash,           // #   magnitude / count / abs
    Underscore,     // _   null / fallback sigil (bare _ = null; _value = fallback)

    // Brackets
    LParen, RParen,     // ( )
    LBracket, RBracket, // [ ]
    LBrace, RBrace,     // { }
    Lt, Gt,             // < >  (vectors only — Cast has no <,> comparison)

    // Arithmetic
    Plus, Minus, Star, Slash, Percent,   // + - * / %
    PlusPlus, MinusMinus,                // ++ --
    PlusEq, MinusEq, StarEq, SlashEq, PercentEq, // += -= *= /= %=

    // Comparison / logic
    Question,       // ?    comparison
    Bang,           // !    negation
    BangQuestion,   // !?   inequality
    AmpAmp,         // &&   and
    PipePipe,       // ||   or
    BangAmp,        // !&   nand
    BangPipe,       // !|   nor

    // Binding / write
    Eq,             // =    bind (eager)
    EqGt,           // =>   live/lazy binding
    EqAmp,          // =&   bidirectional alias
    PipeGt,         // |>   directed write

    // Vector prefixes
    Caret,          // ^    local / facing-relative
    Tilde,          // ~    relative-to-current
    Degree,         // °    rotation

    // Chains
    DotDot,         // ..   range
    Arrow,          // ->   placement
    TildeArrow,     // ~>   normalized directed step
    QuestionGt,     // ?>   conditional if-then
    QuestionQuestion,// ??  conditional else

    // Structural
    Colon,          // :    namespace / key-value
    ColonColon,     // ::   function-definition delimiter
    Dot,            // .    member access
    Pipe,           // |    pipe (into command)
    Semicolon,      // ;    statement separator
    Comma,          // ,

    // Keywords (globally reserved)
    In, Out, Collect, Iter, As, AtKw, Over,

    // Trivia / control
    Newline,        // statement separator (equivalent to ';')
    Comment,        // // ... (usually skipped, but tokenized for tooling)
    EndOfFile
}
