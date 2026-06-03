#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cast.Lang;

// Serializes/deserializes the persistent state that crosses the save boundary:
// the @v registry, @t timer slots, function definitions (as source), and active
// standing casts (as source) — EXCEPT casts whose own tokens reference a session
// local ($), which are dropped (and counted) per the spec's save-exclusion rule.
//
// Format is a simple line-based text protocol (self-contained, no external deps):
//   V <key> <value-literal>
//   FN <source-of-Name::...::>
//   CAST <source>
// Values are written as Cast literals so they round-trip through the parser.
public static class StateSerializer
{
    public sealed class SaveResult
    {
        public string Buffer = "";
        public int DroppedCasts;
    }

    public static SaveResult Save(
        CastRegistry v,
        IReadOnlyDictionary<string, FunctionValue> functions,
        IReadOnlyList<ActiveCast> casts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CAST-SAVE 1");

        foreach (var kv in v.Snapshot())
            sb.AppendLine($"V {Escape(kv.Key)} {Literal(kv.Value)}");

        foreach (var fn in functions.Values)
            sb.AppendLine($"FN {Inline(SourceOf(fn.Def))}");

        int dropped = 0;
        foreach (var c in casts)
        {
            if (c.ReferencesSessionLocal) { dropped++; continue; }
            sb.AppendLine($"CAST {Inline(SourceOf(c.Node))}");
        }

        return new SaveResult { Buffer = sb.ToString(), DroppedCasts = dropped };
    }

    // Re-load into a fresh evaluator via its public surface. Returns the cast source
    // lines and function source lines for the evaluator to re-register, plus the
    // @v entries to restore.
    public sealed class LoadedState
    {
        public List<(string key, CastValue value)> CastRegistry = new();
        public List<string> Functions = new();
        public List<string> Casts = new();
    }

    public static LoadedState Load(string buffer)
    {
        var ls = new LoadedState();
        foreach (var raw in buffer.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("CAST-SAVE")) continue;
            int sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var tag = line[..sp];
            var rest = line[(sp + 1)..];
            switch (tag)
            {
                case "V":
                    int vsp = rest.IndexOf(' ');
                    var key = Unescape(rest[..vsp]);
                    var valSrc = rest[(vsp + 1)..];
                    ls.CastRegistry.Add((key, ParseLiteral(valSrc)));
                    break;
                case "FN": ls.Functions.Add(Deinline(rest)); break;
                case "CAST": ls.Casts.Add(Deinline(rest)); break;
            }
        }
        return ls;
    }

    // ── value <-> literal ─────────────────────────────────────────────────────────

    private static string Literal(CastValue v) => v switch
    {
        NumberValue n => n.ToString(),
        StringValue s => "'" + s.S.Replace("'", "''") + "'",
        VectorValue vec => vec.ToString(),
        NullValue => "_",
        NamespacedIdValue id => string.Join(":", id.Segments),
        ArrayValue a => "[" + string.Join(", ", a.Items.Select(Literal)) + "]",
        _ => "_" // maps/sequences in @v are uncommon; extend if needed
    };

    private static CastValue ParseLiteral(string src)
    {
        // round-trip through the evaluator's expression parser
        var prog = new CastParser(src).ParseProgram();
        var node = ((ProgramNode)prog).Statements.FirstOrDefault();
        return node is null ? CastValue.Null : new CastEvaluator().Eval(node);
    }

    // ── source reconstruction ──────────────────────────────────────────────────────
    // Functions and casts are stored as source so they round-trip through the parser.
    // We reconstruct source from the AST via a small unparser.

    public static string SourceOf(FunctionDef fd) =>
        $"{fd.Name}:: {string.Join("; ", fd.Body.Select(Unparse))} ::";

    public static string SourceOf(CastNode cn)
    {
        var sb = new StringBuilder("cast ");
        if (cn.Count is not null) sb.Append(Unparse(cn.Count)).Append(' ');
        if (cn.Over is not null) sb.Append("over ").Append(Unparse(cn.Over)).Append(' ');
        if (cn.Trigger is not null) sb.Append(Unparse(cn.Trigger)).Append(' ');
        if (cn.Mods.As is not null) sb.Append("as ").Append(Unparse(cn.Mods.As)).Append(' ');
        if (cn.Mods.At is not null) sb.Append("at ").Append(Unparse(cn.Mods.At)).Append(' ');
        if (cn.Action is not null) sb.Append(Unparse(cn.Action));
        return sb.ToString();
    }

    // A minimal unparser sufficient for the constructs that appear in saved
    // functions and casts. (Round-trips through the parser; not pretty-printing.)
    public static string Unparse(Node n) => n switch
    {
        NumberNode num => num.Raw,
        StringNode s => "'" + s.CastValue.Replace("'", "''") + "'",
        VarNode v => "$" + v.Name,
        IdentNode id => id.Name,
        NullNode => "_",
        NamespacedIdNode ns => string.Join(":", ns.Segments),
        FallbackNode f => "_" + Unparse(f.CastValue),
        BinaryNode b => $"{Unparse(b.Left)} {b.Op} {Unparse(b.Right)}",
        UnaryNode u => $"{u.Op}{Unparse(u.Operand)}",
        PostfixNode p => $"{Unparse(p.Operand)}{p.Op}",
        RangeNode r => (r.Complement ? "!" : "") + (r.Low is null ? "" : Unparse(r.Low)) + ".." + (r.High is null ? "" : Unparse(r.High)),
        ConditionalNode c => $"{Unparse(c.Condition)} ?> {Unparse(c.Then)}" + (c.Else is null ? "" : $" ?? {Unparse(c.Else)}"),
        PlacementNode pl => $"{Unparse(pl.Mover)} {pl.Op} {Unparse(pl.CastTarget)}" + (pl.Magnitude is null ? "" : $" * {Unparse(pl.Magnitude)}"),
        PipeNode pp => $"{Unparse(pp.Left)} | {Unparse(pp.Right)}",
        MemberNode m => $"{Unparse(m.CastTarget)}.{m.Member}",
        IndexNode ix => $"{Unparse(ix.CastTarget)}[{string.Join(", ", ix.Args.Select(Unparse))}]",
        NamedIndexNode ni => $"{Unparse(ni.CastTarget)}{{{string.Join(", ", ni.Pairs.Select(p => Unparse(p.Key) + ": " + Unparse(p.Value)))}}}",
        SliceNode sl => $"{Unparse(sl.CastTarget)}({string.Join(", ", sl.Items.Select(Unparse))})",
        MembershipNode mm => $"in {Unparse(mm.Collection)} ? {Unparse(mm.Tested)}",
        CallNode call => call.Name
            + (call.PositionalArgs is null ? "" : "[" + string.Join(", ", call.PositionalArgs.Select(Unparse)) + "]")
            + (call.NamedArgs is null ? "" : "{" + string.Join(", ", call.NamedArgs.Select(p => Unparse(p.Key) + ": " + Unparse(p.Value))) + "}"),
        ArrayNode a => "[" + string.Join(", ", a.Elements.Select(Unparse)) + "]",
        MapNode mp => "{" + string.Join(", ", mp.Pairs.Select(p => Unparse(p.Key) + ": " + Unparse(p.Value))) + "}",
        SequenceNode sq => "(" + string.Join(", ", sq.Items.Select(Unparse)) + ")",
        GroupNode g => "(" + Unparse(g.Inner) + ")",
        VectorNode vec => vec.Shorthand switch {
            VectorShorthand.AllOpen => "<..>",
            VectorShorthand.AllRelative => "<~>",
            VectorShorthand.Empty => "<_>",
            _ => "<" + string.Join(", ", vec.Components.Select(Unparse)) + ">"
        },
        PrefixedComponentNode pc => pc.Prefix + (pc.CastValue is null ? "" : Unparse(pc.CastValue)),
        ScopeChain sc => UnparseScope(sc),
        CastNode cn => SourceOf(cn),
        SpawnNode sp => "spawn " + string.Join(":", sp.Kind)
            + (sp.Selection is null ? "" : "(" + Unparse(sp.Selection) + ")")
            + (sp.Where is null ? "" : Unparse(sp.Where))
            + (sp.Properties.Count == 0 ? "" : "[" + string.Join(", ", sp.Properties.Select(p => Unparse(p.Key) + ": " + Unparse(p.Value))) + "]"),
        FunctionDef fd => SourceOf(fd),
        OutNode o => "out" + (o.CastValue is null ? "" : " " + Unparse(o.CastValue)),
        CollectNode cl => "collect " + Unparse(cl.CastValue),
        IterationNode it => $"${it.VarName} in {Unparse(it.Collection)}[ {string.Join("; ", it.Body.Select(Unparse))} ]",
        _ => throw new CastRuntimeException($"cannot unparse {n.GetType().Name}")
    };

    private static string UnparseScope(ScopeChain sc)
    {
        var sb = new StringBuilder(sc.Sigil);
        if (sc.VPath is not null) foreach (var k in sc.VPath) sb.Append(':').Append(k.Text);
        if (sc.Selection is not null) sb.Append('(').Append(string.Join(", ", sc.Selection.Select(Unparse))).Append(')');
        if (sc.Region is not null) sb.Append(Unparse(sc.Region));
        if (sc.Filter is not null) sb.Append('[').Append(Unparse(sc.Filter)).Append(']');
        return sb.ToString();
    }

    // newline-free inline encoding for one-per-line storage
    private static string Inline(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n");
    private static string Deinline(string s) => s.Replace("\\n", "\n").Replace("\\\\", "\\");
    private static string Escape(string s) => s.Replace(" ", "\\s");
    private static string Unescape(string s) => s.Replace("\\s", " ");
}
