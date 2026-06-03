using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Cast.Lang;

// CastParser test driver: parse real spec programs, print a compact AST sketch, and
// assert structural facts on the tricky productions (carve-outs, precedence).

int failures = 0;

string Sketch(Node n) => n switch
{
    ProgramNode p => string.Join("\n", p.Statements.Select(Sketch)),
    FunctionDef f => $"fn {f.Name}{{ {string.Join("; ", f.Body.Select(Sketch))} }}",
    CastNode c => $"cast(count={(c.Count is null ? "-" : Sketch(c.Count))}, over={(c.Over is null ? "-" : Sketch(c.Over))}, " +
                  $"trig={(c.Trigger is null ? "-" : Sketch(c.Trigger))}, as={(c.Mods.As is null ? "-" : Sketch(c.Mods.As))}, " +
                  $"action={(c.Action is null ? "-" : Sketch(c.Action))})",
    CommandNode cmd => $"cmd(scope={(cmd.Scope is null ? "-" : Sketch(cmd.Scope))}, as={(cmd.Mods.As is null ? "-" : Sketch(cmd.Mods.As))}, " +
                       $"do={Sketch(cmd.Action)})",
    ScopeChain s => $"@{s.Sigil.TrimStart('@')}" +
                    (s.VPath is null ? "" : ":" + string.Join(":", s.VPath.Select(k => k.Text))) +
                    (s.Selection is null ? "" : "(" + string.Join(",", s.Selection.Select(Sketch)) + ")") +
                    (s.Region is null ? "" : Sketch(s.Region)) +
                    (s.Filter is null ? "" : "[" + Sketch(s.Filter) + "]"),
    PlacementNode pl => $"{Sketch(pl.Mover)} {pl.Op} {Sketch(pl.CastTarget)}" + (pl.Magnitude is null ? "" : $" *{Sketch(pl.Magnitude)}"),
    ConditionalNode c => $"({Sketch(c.Condition)} ?> {Sketch(c.Then)}" + (c.Else is null ? "" : $" ?? {Sketch(c.Else)}") + ")",
    BinaryNode b => $"({Sketch(b.Left)} {b.Op} {Sketch(b.Right)})",
    UnaryNode u => $"{u.Op}{Sketch(u.Operand)}",
    PostfixNode pf => $"{Sketch(pf.Operand)}{pf.Op}",
    RangeNode r => (r.Complement ? "!" : "") + $"{(r.Low is null ? "" : Sketch(r.Low))}..{(r.High is null ? "" : Sketch(r.High))}",
    PipeNode pp => $"({Sketch(pp.Left)} | {Sketch(pp.Right)})",
    MemberNode m => $"{Sketch(m.CastTarget)}.{m.Member}",
    IndexNode ix => $"{Sketch(ix.CastTarget)}[{string.Join(",", ix.Args.Select(Sketch))}]",
    NamedIndexNode ni => $"{Sketch(ni.CastTarget)}{{{string.Join(",", ni.Pairs.Select(pr => Sketch(pr.Key) + ":" + Sketch(pr.Value)))}}}",
    SliceNode sl => $"{Sketch(sl.CastTarget)}({string.Join(",", sl.Items.Select(Sketch))})",
    CallNode call => call.Name +
        (call.PositionalArgs is null ? "" : "[" + string.Join(",", call.PositionalArgs.Select(Sketch)) + "]") +
        (call.NamedArgs is null ? "" : "{" + string.Join(",", call.NamedArgs.Select(pr => Sketch(pr.Key) + ":" + Sketch(pr.Value))) + "}"),
    VectorNode v => v.Shorthand switch {
        VectorShorthand.AllOpen => "<..>",
        VectorShorthand.AllRelative => "<~>",
        VectorShorthand.Empty => "<_>",
        _ => "<" + string.Join(",", v.Components.Select(Sketch)) + ">"
    },
    PrefixedComponentNode pc => pc.Prefix + (pc.CastValue is null ? "" : Sketch(pc.CastValue)),
    NumberNode num => num.Raw,
    StringNode st => $"'{st.CastValue}'",
    IdentNode id => id.Name,
    VarNode vr => "$" + vr.Name,
    NamespacedIdNode ns => string.Join(":", ns.Segments),
    NullNode => "_",
    FallbackNode fb => "_" + Sketch(fb.CastValue),
    ArrayNode a => "[" + string.Join(",", a.Elements.Select(Sketch)) + "]",
    MapNode mp => "{" + string.Join(",", mp.Pairs.Select(pr => Sketch(pr.Key) + ":" + Sketch(pr.Value))) + "}",
    SequenceNode sq => "(" + string.Join(",", sq.Items.Select(Sketch)) + ")",
    GroupNode g => "(" + Sketch(g.Inner) + ")",
    _ => n.GetType().Name
};

void Parse(string label, string src)
{
    try
    {
        var prog = new CastParser(src).ParseProgram();
        Console.WriteLine($"OK  | {src}");
        foreach (var line in Sketch(prog).Split('\n'))
            Console.WriteLine($"    | {line}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"ERR | {src}");
        Console.WriteLine($"    | {e.Message}");
        failures++;
    }
}

void Assert(string label, bool cond)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")} | {label}");
    if (!cond) failures++;
}

Node First(string src) => ((ProgramNode)new CastParser(src).ParseProgram()).Statements[0];

Console.WriteLine("=== parses ===");
Parse("death plane", "cast @e<region> Kill");
Parse("curse bloodline", "cast @e[in ancestors ? @v:cursed:bloodline] Wither");
Parse("scheduled bell", "cast @t<0,0,12> as @e[id ? @v:bell] Ring");
Parse("fire-and-forget", "@s cast Heal[50]");
Parse("count over", "@s cast 3 over 45 Pulse");
Parse("heal func", "Heal:: @s.health = @s.health + $amount ::");
Parse("dual arg", "$amount = arg[0] =& param{'amount'}");
Parse("set y keep xz", "@s.position -> <~, 10, ~>");
Parse("step toward", "@s ~> $enemy * 5");
Parse("nested conditional", "$x ? 5 ?> ($m ? ..10 ?> collect 'small') ?? collect 'five'");
Parse("filtered heal", "@e<region>[health ? ..50] Heal[50]");
Parse("timer set", "@t[0] = -10");
Parse("timer incr", "@t[0] += 5");
Parse("wave loop", "$wave in (1..3)[ cast @t<~0, ~0, ~(iter * 7 + 8)> as @s OnWave[$wave] ]");
Parse("range complement", "@e[health !? 0] Hurt[1]");
Parse("precedence", "$x + 1 ? 5 && $y");
Parse("per-entity poison", "@v:poison{@s.id} += arg[0]");
Parse("nested fn", "Outer:: Inner:: log 'hi' :: ::");

Console.WriteLine();
Console.WriteLine("=== assertions ===");

// $x + 1 ? 5 && $y  =>  (($x + 1) ? 5) && $y
{
    var n = First("$x + 1 ? 5 && $y");
    Assert("precedence: top is &&",
        n is BinaryNode { Op: "&&", Left: BinaryNode { Op: "?", Left: BinaryNode { Op: "+" } } });
}

// #$a + $b  =>  (#$a) + $b
{
    var n = First("#$a + $b");
    Assert("prefix binds before +",
        n is BinaryNode { Op: "+", Left: UnaryNode { Op: "#" } });
}

// $x ? 1..10  =>  $x ? (1..10)
{
    var n = First("$x ? 1..10");
    Assert("range built before comparison",
        n is BinaryNode { Op: "?", Right: RangeNode });
}

// !2..5  =>  complement range
{
    var n = First("@e[!2..5] x");
    Assert("range-complement parses as complemented range",
        n is CommandNode { Scope.Filter: RangeNode { Complement: true } });
}

// !$flag  =>  unary !, NOT a range-complement
{
    var n = First("!$flag");
    Assert("!$flag is plain unary negation",
        n is UnaryNode { Op: "!", Operand: VarNode });
}

// ~> magnitude binds to the step, not the target
{
    var n = First("@s ~> $enemy * 5");
    Assert("~> magnitude binds to step",
        n is CommandNode { Action: PlacementNode { Op: "~>", Magnitude: NumberNode } }
        || n is PlacementNode { Op: "~>", Magnitude: NumberNode });
}

// ~> magnitude is a full additive expr: * 5 + 1
{
    var n = First("@s ~> $enemy * 5 + 1");
    bool ok = (n is CommandNode { Action: PlacementNode { Magnitude: BinaryNode { Op: "+" } } })
           || (n is PlacementNode { Magnitude: BinaryNode { Op: "+" } });
    Assert("~> magnitude is additive (5 + 1)", ok);
}

// =& right-assoc: $a =& $b =& $c => $a =& ($b =& $c)
{
    var n = First("$a =& $b =& $c");
    Assert("=& right-assoc",
        n is BinaryNode { Op: "=&", Right: BinaryNode { Op: "=&" } });
}

// =& binds before arithmetic: $a =& $b + 1 => $a =& ($b + 1)? NO — alias is L3,
// tighter than additive (L5). So $a =& $b + 1 => ($a =& $b) + 1.
{
    var n = First("$a =& $b + 1");
    Assert("=& (L3) binds tighter than + (L5): ($a =& $b) + 1",
        n is BinaryNode { Op: "+", Left: BinaryNode { Op: "=&" } });
}

// cast with count
{
    var n = First("@s cast 3 over 45 Pulse");
    Assert("cast count/over parsed",
        n is CommandNode { Action: CastNode { Count: NumberNode, Over: NumberNode } }
        || n is CastNode { Count: NumberNode, Over: NumberNode });
}

// fire-and-forget cast: no trigger scope
{
    var n = First("@s cast Heal[50]");
    bool ok = n is CommandNode { Action: CastNode { Trigger: null, Action: not null } }
           || n is CastNode { Trigger: null, Action: not null };
    Assert("fire-and-forget cast has no trigger scope", ok);
}

// comparison non-assoc: $a ? $b ? $c must error
{
    bool threw = false;
    try { First("$a ? $b ? $c"); } catch (CastParseException) { threw = true; }
    Assert("comparison does not chain (throws)", threw);
}

// vector shorthand
{
    var n = First("@s.position -> <~>");
    bool ok = n is PlacementNode { CastTarget: VectorNode { Shorthand: VectorShorthand.AllRelative } }
           || n is CommandNode { Action: PlacementNode { CastTarget: VectorNode { Shorthand: VectorShorthand.AllRelative } } };
    Assert("<~> is all-relative shorthand", ok);
}

// nested function definition allowed
{
    var n = First("Outer:: Inner:: log 'hi' :: ::");
    Assert("nested function def allowed",
        n is FunctionDef { Body: var b } && b.Count == 1 && b[0] is FunctionDef);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
