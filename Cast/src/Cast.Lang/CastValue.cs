#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cast.Lang;

// ─────────────────────────────────────────────────────────────────────────────
// Runtime value model. One value kind per spec Overview:
//   Number (one numeric type), String, Array, Map, Sequence, Vector, Range,
//   NamespacedId, Function, Null. Scopes are addressed, not constructed, and are
//   handled by the evaluator/host — not represented here as a plain value kind
//   (a resolved scope result is an Array of targets at the host boundary).
// ─────────────────────────────────────────────────────────────────────────────

public abstract record CastValue
{
    // Truthiness (spec Evaluation Model): falsey = null, 0, '', [], {}, all-zero
    // vector. Everything else is truthy. "Absence/emptiness false; presence true."
    public abstract bool IsTruthy { get; }

    public static readonly NullValue Null = new();
}

public sealed record NullValue : CastValue
{
    public override bool IsTruthy => false;
    public override string ToString() => "_";
}

public sealed record NumberValue(double N) : CastValue
{
    public override bool IsTruthy => N != 0;
    public override string ToString() =>
        N == Math.Floor(N) && !double.IsInfinity(N)
            ? ((long)N).ToString(CultureInfo.InvariantCulture)
            : N.ToString(CultureInfo.InvariantCulture);
}

public sealed record StringValue(string S) : CastValue
{
    public override bool IsTruthy => S.Length != 0;
    public override string ToString() => $"'{S}'";
}

public sealed record ArrayValue(IReadOnlyList<CastValue> Items) : CastValue
{
    public override bool IsTruthy => Items.Count != 0;
    public override string ToString() => "[" + string.Join(", ", Items) + "]";
}

public sealed record MapValue(IReadOnlyDictionary<CastValue, CastValue> Entries) : CastValue
{
    public override bool IsTruthy => Entries.Count != 0;
    public override string ToString() =>
        "{" + string.Join(", ", Entries.Select(kv => $"{kv.Key}: {kv.Value}")) + "}";
}

public sealed record SequenceValue(IReadOnlyList<CastValue> Items) : CastValue
{
    public override bool IsTruthy => Items.Count != 0;
    public override string ToString() => "(" + string.Join(", ", Items) + ")";
}

public sealed record VectorValue(IReadOnlyList<double> Components) : CastValue
{
    // All-zero vector is falsey (spec).
    public override bool IsTruthy => Components.Any(c => c != 0);
    public int Arity => Components.Count;
    public override string ToString() =>
        "<" + string.Join(", ", Components.Select(c =>
            c == Math.Floor(c) ? ((long)c).ToString(CultureInfo.InvariantCulture)
                               : c.ToString(CultureInfo.InvariantCulture))) + ">";
}

// A range value. Open ends are null. Numeric ranges; char ranges handled by the
// evaluator when bounds are single-char strings. Complement flips membership.
public sealed record RangeValue(double? Low, double? High, bool Complement) : CastValue
{
    public override bool IsTruthy => true; // a range is a present value
    public bool Contains(double x)
    {
        bool inside = (Low is null || x >= Low) && (High is null || x <= High);
        return Complement ? !inside : inside;
    }
    public override string ToString() =>
        (Complement ? "!" : "") +
        (Low?.ToString(CultureInfo.InvariantCulture) ?? "") + ".." +
        (High?.ToString(CultureInfo.InvariantCulture) ?? "");
}

public sealed record NamespacedIdValue(IReadOnlyList<string> Segments) : CastValue
{
    public override bool IsTruthy => true;
    public override string ToString() => string.Join(":", Segments);
}

// A function value: its definition (name + body) captured for later invocation.
public sealed record FunctionValue(FunctionDef Def) : CastValue
{
    public override bool IsTruthy => true;
    public override string ToString() => $"<fn {Def.Name}>";
}

// A host target carried as a value (a resolved scope element). Cast never inspects
// the handle; it only passes it back to the host's property adapter / commands.
public sealed record TargetValue(CastTarget CastTarget) : CastValue
{
    public override bool IsTruthy => true;
    public override string ToString() => CastTarget.ToString();
}

// Equality for use as map keys: value-based. Records give structural equality for
// scalars; collections need element-wise. We implement a helper for map keying.
public static class ValueEquality
{
    public static bool Equal(CastValue a, CastValue b)
    {
        return (a, b) switch
        {
            (NullValue, NullValue) => true,
            (NumberValue x, NumberValue y) => x.N == y.N,
            (StringValue x, StringValue y) => x.S == y.S,
            (NamespacedIdValue x, NamespacedIdValue y) => x.Segments.SequenceEqual(y.Segments),
            (VectorValue x, VectorValue y) => x.Components.SequenceEqual(y.Components),
            (ArrayValue x, ArrayValue y) => x.Items.Count == y.Items.Count &&
                x.Items.Zip(y.Items, Equal).All(t => t),
            (SequenceValue x, SequenceValue y) => x.Items.Count == y.Items.Count &&
                x.Items.Zip(y.Items, Equal).All(t => t),
            (MapValue x, MapValue y) => MapsEqual(x, y),
            _ => false
        };
    }

    private static bool MapsEqual(MapValue x, MapValue y)
    {
        if (x.Entries.Count != y.Entries.Count) return false;
        foreach (var kv in x.Entries)
        {
            var match = y.Entries.FirstOrDefault(e => Equal(e.Key, kv.Key));
            if (match.Key is null && !y.Entries.Any(e => Equal(e.Key, kv.Key))) return false;
            if (!y.Entries.Any(e => Equal(e.Key, kv.Key) && Equal(e.Value, kv.Value))) return false;
        }
        return true;
    }
}

// A value-keyed dictionary for MapValue construction (uses structural equality).
public sealed class ValueKeyDictionary : Dictionary<CastValue, CastValue>
{
    private sealed class Cmp : IEqualityComparer<CastValue>
    {
        public bool Equals(CastValue? a, CastValue? b) => a is not null && b is not null && ValueEquality.Equal(a, b);
        public int GetHashCode(CastValue v) => v switch
        {
            NumberValue n => n.N.GetHashCode(),
            StringValue s => s.S.GetHashCode(),
            NamespacedIdValue id => string.Join(":", id.Segments).GetHashCode(),
            NullValue => 0,
            _ => v.ToString()!.GetHashCode()
        };
    }
    public ValueKeyDictionary() : base(new Cmp()) { }
}
