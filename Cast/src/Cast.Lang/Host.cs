#nullable enable
using System;
using System.Collections.Generic;

namespace Cast.Lang;

// ─────────────────────────────────────────────────────────────────────────────
// The host binding interface. A game integrates Cast by implementing IHost (or
// composing the smaller interfaces). Cast never references game-specific content;
// the host bridges from Cast's abstract surface to its concrete runtime. Matches
// the spec's "binding interface (what a host implements)" exactly.
//
// A "target" is an opaque handle the host understands (its own entity reference).
// Cast only ever touches a target through the host's property adapter, never by
// inspecting it directly — so it's typed as `object`.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>An opaque host target (entity, world singleton, tile, etc.).</summary>
public readonly struct CastTarget : IEquatable<CastTarget>
{
    public object Handle { get; }
    public CastTarget(object handle) => Handle = handle;
    public override string ToString() => Handle?.ToString() ?? "<target>";

    // Identity for edge-tracking: two Targets are the same thing if they wrap the
    // same handle instance (reference identity). Hosts that return fresh wrappers
    // per query should give handles that compare equal (e.g. the entity object).
    public bool Equals(CastTarget other) => ReferenceEquals(Handle, other.Handle) || Equals(Handle, other.Handle);
    public override bool Equals(object? obj) => obj is CastTarget t && Equals(t);
    public override int GetHashCode() => Handle?.GetHashCode() ?? 0;
}

/// <summary>
/// Context passed to scope handlers and command handlers: the narrowing arguments
/// from the scope chain, and the currently-active acting target (@s), so handlers
/// can resolve relative queries (nearest, self, etc.).
/// </summary>
public sealed class ScopeQuery
{
    /// <summary>The scope letter(s), e.g. "e", "np", "s" (sigil's '@' stripped).</summary>
    public string Letters { get; init; } = "";
    /// <summary>(selection) index items, already evaluated to Values, or null.</summary>
    public IReadOnlyList<CastValue>? Selection { get; init; }
    /// <summary>&lt;region&gt; vector, already evaluated, or null.</summary>
    public CastValue? Region { get; init; }
    /// <summary>The active acting target (@s) at query time, or null at top level.</summary>
    public CastTarget? Self { get; init; }
    /// <summary>
    /// Evaluate the [filter] predicate against a candidate target. The runtime
    /// supplies this so the host can apply the filter per candidate without knowing
    /// Cast's evaluation rules. Null when there is no filter.
    /// </summary>
    public Func<CastTarget, bool>? Filter { get; init; }
}

/// <summary>Resolves a scope letter to the set of targets it addresses.</summary>
public interface IScopeHandler
{
    /// <summary>The scope letters this handler serves (e.g. "e", "p", "s", "w", "n", "r").</summary>
    bool Handles(string letters);
    /// <summary>Return the targets for this scope query (selection/region/filter applied).</summary>
    IReadOnlyList<CastTarget> Resolve(ScopeQuery query);
}

/// <summary>Reads and writes properties on a target (dot-access and assignment).</summary>
public interface IPropertyAdapter
{
    bool TryGet(CastTarget target, string property, out CastValue value);
    bool TrySet(CastTarget target, string property, CastValue value);
}

/// <summary>A host-registered command, dispatched by name over the active targets.</summary>
public interface ICommandHandler
{
    bool Handles(string name);
    /// <summary>Run the command over each target with the given positional/named args.</summary>
    CastValue Invoke(string name, IReadOnlyList<CastTarget> targets,
                 IReadOnlyList<CastValue> args, IReadOnlyDictionary<string, CastValue> named);
}

/// <summary>Resolves a namespaced id (mod:type:name) to a runtime value.</summary>
public interface IIdResolver
{
    bool TryResolve(IReadOnlyList<string> segments, out CastValue value);
}

/// <summary>
/// Interprets a vector for a scope with non-spatial semantics (e.g. @t using
/// &lt;day, minute, hour&gt;). Optional; spatial scopes need none.
/// </summary>
public interface IVectorInterpreter
{
    bool Handles(string letters);
    /// <summary>Map a vector value to the scope's native meaning (host's choice of representation).</summary>
    void Apply(string letters, CastTarget target, VectorValue vector);
}

/// <summary>Persistence backend for save/load/saves/unsave. Optional.</summary>
public interface IPersistenceProvider
{
    void Write(string name, string buffer);
    string Read(string name);
    IReadOnlyList<string> List();
    void Delete(string name);
}

/// <summary>Where say/msg output is routed. Optional (falls back to log).</summary>
public interface IOutputChannels
{
    void Say(string message);
    void Msg(string message, VectorValue? position);
}

/// <summary>
/// File I/O backend for read/invoke/files/write. The host registers named
/// directories; Cast resolves paths within them. Optional — if not provided, file
/// commands error visibly. Paths are relative to a registered directory (the first
/// path segment names the directory, e.g. 'scripts/boss.cast').
/// </summary>
public interface IDirectoryProvider
{
    bool TryRead(string path, out string contents);
    bool TryWrite(string path, string contents);   // false if dir is read-only/unknown
    IReadOnlyList<string> List(string directory);   // file names in a registered dir
}

/// <summary>
/// Creates entities from a kind descriptor (the spawn command). Optional — if not
/// provided, spawn errors visibly. The runtime parses spawn's structured grammar
/// and hands the host the kind id, a count, an optional region/position, and
/// initial properties.
/// </summary>
public interface ISpawner
{
    /// <param name="kind">The namespaced id segments, e.g. ["shadebreaker","creature","wolf"].</param>
    /// <param name="count">How many to create (selection resolved to a count).</param>
    /// <param name="where">Position/region vector, or null.</param>
    /// <param name="properties">Initial property values keyed by name.</param>
    /// <returns>The created targets.</returns>
    IReadOnlyList<CastTarget> Spawn(IReadOnlyList<string> kind, int count,
                                CastValue? where, IReadOnlyDictionary<string, CastValue> properties);
}

/// <summary>
/// The full host. A host may implement this directly, or compose it from the
/// smaller interfaces via HostBuilder. Persistence, vector interpreters, and
/// output channels are optional (null = "not provided", and the corresponding
/// Cast features error visibly or fall back).
/// </summary>
public interface IHost
{
    IReadOnlyList<IScopeHandler> ScopeHandlers { get; }
    IPropertyAdapter Properties { get; }
    IReadOnlyList<ICommandHandler> CommandHandlers { get; }
    IIdResolver? IdResolver { get; }
    IReadOnlyList<IVectorInterpreter> VectorInterpreters { get; }
    IPersistenceProvider? Persistence { get; }
    IOutputChannels? Output { get; }
    IDirectoryProvider? Directories { get; }
    ISpawner? Spawner { get; }

    /// <summary>
    /// The ambient acting target (@s) when none is otherwise established — e.g. the
    /// player, or a world singleton. Used during cast ticks for condition-triggered
    /// casts that have no scope of their own. Null if the host has no default.
    /// </summary>
    CastTarget? AmbientSelf => null;
}
