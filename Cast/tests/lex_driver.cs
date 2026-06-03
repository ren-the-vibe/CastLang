using System;
using System.Linq;
using System.Collections.Generic;
using Cast.Lang;

// CastLexer test driver. Runs the real C# lexer against spec snippets and prints
// the token-type stream for each, plus a few hard assertions on the tricky cases.

string[] samples =
{
    "@e<~-5..~5, ~0, ~-5..~5>[in tags ? 'undead'] Hurt[10]",
    "cast @t<0,0,12> as @e[id ? @v:bell] Ring",
    "@s.position -> <~, 10, ~>",
    "$amount = arg[0] =& param{'amount'}",
    "@v:poison{@s.id} += arg[0]",
    "$x ? 5 ?> ($m ? ..10 ?> collect 'small') ?? collect 'five'",
    "Heal:: @s.health = @s.health + $amount ::",
    "@s ~> $enemy * 5",
    "$wave in (1..3)[ cast @t<~0, ~0, ~(iter * 7 + 8)> as @s OnWave[$wave] ]",
    "@t[0] = -10",
    "!2..5",
    "!$flag",
    ".5",
    "@t{'kills'} ++",
    "$a =& $b =& $c",
    "@v:score:player1 ++",
    "'it''s a test'",
    "cast @e<region> Kill",
    "@e[in tags ? 'ally' && health ? ..max_health && #(position - @s.position) ? ..8] Heal[15]",
};

int failures = 0;

foreach (var s in samples)
{
    try
    {
        var toks = new CastLexer(s).Tokenize();
        var stream = string.Join(" ", toks
            .Where(t => t.Type != CastTokenType.EndOfFile)
            .Select(t => t.Type.ToString()));
        Console.WriteLine($"OK  | {s}");
        Console.WriteLine($"    | {stream}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"ERR | {s}");
        Console.WriteLine($"    | {e.Message}");
        failures++;
    }
}

Console.WriteLine();
Console.WriteLine("=== assertions ===");

void Assert(string label, bool cond) {
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")} | {label}");
    if (!cond) failures++;
}

CastTokenType[] Types(string src) =>
    new CastLexer(src).Tokenize().Where(t => t.Type != CastTokenType.EndOfFile).Select(t => t.Type).ToArray();

// Maximal munch: '::' is one token, not two ':'
Assert("'::' lexes as ColonColon",
    Types("a::b").Contains(CastTokenType.ColonColon) && !Types("a::b").Contains(CastTokenType.Colon));

// '=&' is one token, not '=' '&'
Assert("'=&' lexes as EqAmp",
    Types("$a =& $b").Contains(CastTokenType.EqAmp));

// '..' is one token, not two '.'
Assert("'..' lexes as DotDot",
    Types("1..5").Contains(CastTokenType.DotDot) && !Types("1..5").Contains(CastTokenType.Dot));

// '~>' is one token
Assert("'~>' lexes as TildeArrow",
    Types("@s ~> x").Contains(CastTokenType.TildeArrow));

// '!?' before '!'
Assert("'!?' lexes as BangQuestion",
    Types("a !? b").Contains(CastTokenType.BangQuestion));

// '!2..5' : Bang, Number, DotDot, Number (range-complement handled by parser)
Assert("'!2..5' = Bang Number DotDot Number",
    Types("!2..5").SequenceEqual(new[] { CastTokenType.Bang, CastTokenType.Number, CastTokenType.DotDot, CastTokenType.Number }));

// '!$flag' : Bang Dollar Ident (no DotDot — parser won't take complement branch)
Assert("'!$flag' = Bang Dollar Ident",
    Types("!$flag").SequenceEqual(new[] { CastTokenType.Bang, CastTokenType.Dollar, CastTokenType.Ident }));

// '.5' is a single Number
Assert("'.5' is one Number",
    Types(".5").SequenceEqual(new[] { CastTokenType.Number }));

// leading '_' is NOT an identifier: '_foo' = Underscore Ident
Assert("'_foo' = Underscore Ident",
    Types("_foo").SequenceEqual(new[] { CastTokenType.Underscore, CastTokenType.Ident }));

// 'my_var' IS one identifier ('_' allowed after first char)
Assert("'my_var' is one Ident",
    Types("my_var").SequenceEqual(new[] { CastTokenType.Ident }));

// keywords are reserved
Assert("'over' is keyword Over",
    Types("over").SequenceEqual(new[] { CastTokenType.Over }));

// scope sigil greedy: '@np' is one ScopeSigil
Assert("'@np' is one ScopeSigil",
    Types("@np").SequenceEqual(new[] { CastTokenType.ScopeSigil }));

// string with doubled quote decodes to embedded '
{
    var t = new CastLexer("'it''s'").Tokenize()[0];
    Assert("'it''s' decodes to it's", t.Type == CastTokenType.String && t.CastValue == "it's");
}

// '++' before '+'
Assert("'++' lexes as PlusPlus",
    Types("$x ++").Contains(CastTokenType.PlusPlus));

// '+=' before '+' and '='
Assert("'+=' lexes as PlusEq",
    Types("$x += 1").Contains(CastTokenType.PlusEq));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
