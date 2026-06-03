#nullable enable
using System;
using System.Collections.Generic;

namespace Cast.Lang;

// ─────────────────────────────────────────────────────────────────────────────
// AST for Cast. Records are immutable nodes. The hierarchy mirrors the grammar:
// a Program is a list of Statements; a Statement is a FunctionDef, a Command, or
// a bare expression. Expressions follow the precedence table. Scope chains,
// vectors, ranges, and the cast envelope have dedicated nodes.
// Every node carries a source position (the leading token) for diagnostics.
// ─────────────────────────────────────────────────────────────────────────────

public abstract record Node
{
    public int Line { get; init; }
    public int Column { get; init; }
}

// ── Top level ───────────────────────────────────────────────────────────────

public sealed record ProgramNode(IReadOnlyList<Node> Statements) : Node;

public sealed record FunctionDef(string Name, IReadOnlyList<Node> Body) : Node;

// Iteration:  $name in collection<bracket> body </bracket>
public sealed record IterationNode(
    string VarName,
    Node Collection,
    char BodyBracket,            // '[', '{', '<', '('
    IReadOnlyList<Node> Body) : Node;

// Membership test:  in collection ? value   (an expression, handled in primary)
public sealed record MembershipNode(Node Collection, Node Tested) : Node;

// Loop-body keywords
public sealed record OutNode(Node? CastValue) : Node;        // out [value]
public sealed record CollectNode(Node CastValue) : Node;     // collect value

// ── Commands ──────────────────────────────────────────────────────────────────
// A command applies an action within an optional scope chain, with optional
// as/at context modifiers.

public sealed record CommandNode(
    ScopeChain? Scope,
    ContextMods Mods,
    Node Action) : Node;

public sealed record ContextMods(ScopeChain? As, ScopeChain? At) : Node
{
    public bool IsEmpty => As is null && At is null;
}

// The namesake. count/over are the only cast-specific envelope pieces; the
// trigger scope is an ordinary scope chain; mods redirect the running context.
public sealed record CastNode(
    Node? Count,        // repeat count (a bare number expr), or null
    Node? Over,         // frames to spread over (after `over`), or null
    ScopeChain? Trigger,// the trigger-and-context scope, or null (fire-and-forget)
    ContextMods Mods,   // as/at
    Node? Action        // the command to run, or null (scope-only loop-spread cast)
) : Node;

// ── Scope chains ───────────────────────────────────────────────────────────────
// @scope(selection)<region>[filter]

public sealed record ScopeChain(
    string Sigil,                       // e.g. "@np", "@v", "@t", "@s"
    IReadOnlyList<Node>? Selection,     // (a..b, c, d..) index ranges, or null
    VectorNode? Region,                 // <...> spatial narrowing, or null
    Node? Filter,                       // [ expr ] predicate, or null
    IReadOnlyList<RegistryKey>? VPath   // for @v:score:player1 -> ["score","player1"]
) : Node
{
    // Cached colon-joined @v key (e.g. "score:player1"), computed once on first use.
    // The evaluator hits @v keys in hot loops; recomputing string.Join each access is
    // wasteful since VPath is fixed at parse time.
    public string? VKeyCache;
}

// A single colon-segment of an @v registry path (literal key text).
public sealed record RegistryKey(string Text) : Node;

// ── Calls and access ────────────────────────────────────────────────────────────

public sealed record CallNode(
    string Name,
    IReadOnlyList<Node>? PositionalArgs, // [a, b] form
    IReadOnlyList<PairNode>? NamedArgs   // {k: v} form
) : Node;

public sealed record MemberNode(Node CastTarget, string Member) : Node;     // a.b
public sealed record IndexNode(Node CastTarget, IReadOnlyList<Node> Args) : Node;  // a[...]
public sealed record NamedIndexNode(Node CastTarget, IReadOnlyList<PairNode> Pairs) : Node; // a{...}
public sealed record SliceNode(Node CastTarget, IReadOnlyList<Node> Items) : Node; // a(...)

// ── Expressions: binary / unary / postfix ───────────────────────────────────────

public sealed record BinaryNode(string Op, Node Left, Node Right) : Node;
public sealed record UnaryNode(string Op, Node Operand) : Node;
public sealed record PostfixNode(string Op, Node Operand) : Node;       // $x++, $x--

// Conditional: cond ?> then (?? else)?
public sealed record ConditionalNode(Node Condition, Node Then, Node? Else) : Node;

// Range: lo..hi, ..hi, lo.. , and complement (!range)
public sealed record RangeNode(Node? Low, Node? High, bool Complement) : Node;

// Placement / step
public sealed record PlacementNode(string Op, Node Mover, Node CastTarget, Node? Magnitude) : Node;

// Pipe: left | right
public sealed record PipeNode(Node Left, Node Right) : Node;

// ── Literals and primaries ──────────────────────────────────────────────────────

public sealed record NumberNode(double CastValue, string Raw) : Node;
public sealed record StringNode(string CastValue) : Node;
public sealed record IdentNode(string Name) : Node;
public sealed record VarNode(string Name) : Node;                       // $name
public sealed record NamespacedIdNode(IReadOnlyList<string> Segments) : Node; // mod:type:name
public sealed record NullNode : Node;                                   // bare _
public sealed record FallbackNode(Node CastValue) : Node;                   // _value
public sealed record ArrayNode(IReadOnlyList<Node> Elements) : Node;    // [..]
public sealed record MapNode(IReadOnlyList<PairNode> Pairs) : Node;     // {..}
public sealed record SequenceNode(IReadOnlyList<Node> Items) : Node;    // (a, b, c)
public sealed record GroupNode(Node Inner) : Node;                      // ( expr )
// An already-evaluated value spliced into the AST (used by the pipe operator to
// inject the flowing value as a command's primary argument). Internal to the
// evaluator; never produced by the parser.
// spawn <id>(selection)<region>[properties] — create entities of a kind.
// Kind is a namespaced id; Selection is a count expression (number or range); Where
// is a vector/region; Properties are initial named values.
public sealed record SpawnNode(
    IReadOnlyList<string> Kind,
    Node? Selection,
    Node? Where,
    IReadOnlyList<PairNode> Properties) : Node;

public sealed record InjectedValueNode(CastValue Value) : Node;

public sealed record PairNode(Node Key, Node Value) : Node;

// Vector: components may be number/range/prefixed/_/~; or a shorthand body.
public sealed record VectorNode(
    IReadOnlyList<Node> Components,
    VectorShorthand Shorthand) : Node;

public enum VectorShorthand { None, AllOpen /* <..> */, AllRelative /* <~> */, Empty /* <_> */ }

// Prefixed vector component: ~5, ^2, °30, or bare ~
public sealed record PrefixedComponentNode(string Prefix, Node? CastValue) : Node;
