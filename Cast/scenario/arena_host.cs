#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Cast.Lang;

namespace BloodlineArena;

// ── World model ───────────────────────────────────────────────────────────────

class Creature
{
    public int Uid;                 // engine handle (unique, never reused)
    public double Lineage;          // this creature's own lineage id
    public List<double> Ancestors = new();  // [own] + union of parents' ancestor sets
    public double[] Pos = { 0, 0, 0 };
    public double[] Vel = { 0, 0, 0 };
    public HashSet<string> Tags = new();
    public bool Alive = true;
    public bool IsMage;
    public double Radius = 1.0;
    public bool EverCursed;   // sticky: set the first time 'cursed' is applied
}

class Arena
{
    public double SizeX = 40, SizeZ = 40, Ceiling = 20;   // box: x,z in [0,Size], y in [0,Ceiling]
    public List<Creature> Creatures = new();
    public Creature? Mage;
    public int NextUid = 1;
    public double NextLineage = 1;
    public Random Rng;

    // event log for the headless report
    public int Births, Deaths, MageCursings;
    public double? CursedLineage;
    public List<string> Events = new();

    public Arena(int seed) => Rng = new Random(seed);

    public double[] Center => new[] { SizeX / 2, Ceiling / 2, SizeZ / 2 };
}

// ── Property adapter ────────────────────────────────────────────────────────────

class ArenaProps : IPropertyAdapter
{
    public bool TryGet(CastTarget t, string prop, out CastValue value)
    {
        value = CastValue.Null;
        if (t.Handle is not Creature c) return false;
        switch (prop)
        {
            case "position":  value = new VectorValue(c.Pos.ToList()); return true;
            case "lineage":   value = new NumberValue(c.Lineage); return true;
            case "ancestors": value = new ArrayValue(c.Ancestors.Select(a => (CastValue)new NumberValue(a)).ToList()); return true;
            case "tags":      value = new ArrayValue(c.Tags.Select(s => (CastValue)new StringValue(s)).ToList()); return true;
            case "is_mage":   value = new NumberValue(c.IsMage ? 1 : 0); return true;
            case "uid":       value = new NumberValue(c.Uid); return true;
            default: return false;
        }
    }
    public bool TrySet(CastTarget t, string prop, CastValue value)
    {
        if (t.Handle is not Creature c) return false;
        switch (prop)
        {
            case "position": c.Pos = ((VectorValue)value).Components.ToArray(); return true;
            case "tags":
                c.Tags = (value as ArrayValue)?.Items.OfType<StringValue>().Select(s => s.S).ToHashSet() ?? c.Tags;
                if (c.Tags.Contains("cursed")) c.EverCursed = true;
                return true;
            default: return false;
        }
    }
}

// ── Scope handler: @e (creatures), @s (self), @w (world) ─────────────────────────

class ArenaScopes : IScopeHandler
{
    readonly Arena _a;
    public ArenaScopes(Arena a) => _a = a;
    public bool Handles(string letters) => letters is "e" or "s" or "w";
    public IReadOnlyList<CastTarget> Resolve(ScopeQuery q)
    {
        if (q.Letters == "s")
            return q.Self is { } s ? new[] { s } : Array.Empty<CastTarget>();
        if (q.Letters == "w")
            return new[] { new CastTarget(_a) };
        var targets = _a.Creatures.Where(c => c.Alive).Select(c => new CastTarget(c));
        if (q.Filter is { } f) targets = targets.Where(t => f(t));
        return targets.ToList();
    }
}

// ── Command handler: Kill (host death semantics: remove from world) ──────────────

class ArenaCommands : ICommandHandler
{
    readonly Arena _a;
    public ArenaCommands(Arena a) => _a = a;
    public bool Handles(string name) => name is "Kill";
    public CastValue Invoke(string name, IReadOnlyList<CastTarget> targets,
        IReadOnlyList<CastValue> args, IReadOnlyDictionary<string, CastValue> named)
    {
        if (name == "Kill")
            foreach (var t in targets)
                if (t.Handle is Creature c && c.Alive && !c.IsMage)  // death planes never kill the mage
                {
                    c.Alive = false;
                    _a.Deaths++;
                    _a.Events.Add($"  death: creature #{c.Uid} (lineage {c.Lineage:F0}) hit a death plane");
                }
        return CastValue.Null;
    }
}

// ── Spawner: births carry ancestry (union of parents + own new lineage id) ───────

class ArenaSpawner : ISpawner
{
    readonly Arena _a;
    public ArenaSpawner(Arena a) => _a = a;
    public IReadOnlyList<CastTarget> Spawn(IReadOnlyList<string> kind, int count,
        CastValue? where, IReadOnlyDictionary<string, CastValue> properties)
    {
        var made = new List<CastTarget>();
        for (int i = 0; i < count; i++)
        {
            var child = new Creature
            {
                Uid = _a.NextUid++,
                Lineage = _a.NextLineage++,
                Pos = where is VectorValue v ? v.Components.ToArray() : (double[])_a.Center.Clone()
            };
            child.Ancestors.Add(child.Lineage);   // its own id first
            // parents passed as an 'ancestors' property: a flattened array of ids to inherit
            if (properties.TryGetValue("inherit", out var inh) && inh is ArrayValue arr)
                foreach (var a in arr.Items.OfType<NumberValue>().Select(n => n.N).Distinct())
                    if (!child.Ancestors.Contains(a)) child.Ancestors.Add(a);
            // small random initial velocity
            child.Vel = new[] { _a.Rng.NextDouble() * 2 - 1, 0, _a.Rng.NextDouble() * 2 - 1 };
            _a.Creatures.Add(child);
            _a.Births++;
            made.Add(new CastTarget(child));
        }
        return made;
    }
}

// ── Output channel (say/msg) routed into the event log ───────────────────────────

class ArenaOutput : IOutputChannels
{
    readonly Arena _a;
    public ArenaOutput(Arena a) => _a = a;
    public void Say(string message) => _a.Events.Add($"  say: {message}");
    public void Msg(string message, VectorValue? position) =>
        _a.Events.Add($"  msg: {message}" + (position is null ? "" : $" @ <{string.Join(",", position.Components.Select(x => x.ToString("F0")))}>"));
}

// ── Host ─────────────────────────────────────────────────────────────────────────

class ArenaHost : IHost
{
    readonly Arena _a;
    public ArenaHost(Arena a)
    {
        _a = a;
        ScopeHandlers = new[] { (IScopeHandler)new ArenaScopes(a) };
        Properties = new ArenaProps();
        CommandHandlers = new[] { (ICommandHandler)new ArenaCommands(a) };
        VectorInterpreters = Array.Empty<IVectorInterpreter>();
        Output = new ArenaOutput(a);
        Spawner = new ArenaSpawner(a);
    }
    public IReadOnlyList<IScopeHandler> ScopeHandlers { get; }
    public IPropertyAdapter Properties { get; }
    public IReadOnlyList<ICommandHandler> CommandHandlers { get; }
    public IIdResolver? IdResolver => null;
    public IReadOnlyList<IVectorInterpreter> VectorInterpreters { get; }
    public IPersistenceProvider? Persistence => null;
    public IOutputChannels? Output { get; }
    public IDirectoryProvider? Directories => null;
    public ISpawner? Spawner { get; }
    // @s defaults to the mage when nothing else is bound (used by mage-curse cast)
    public CastTarget? AmbientSelf => _a.Mage is { Alive: true } m ? new CastTarget(m) : null;
}
