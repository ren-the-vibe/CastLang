#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Cast.Lang;

// ── A minimal mock host: an entity world for end-to-end testing ──────────────

class Entity
{
    public string Id = "";
    public double Health = 100;
    public double MaxHealth = 100;
    public double[] Pos = { 0, 0, 0 };
    public HashSet<string> Tags = new();
    public bool Alive = true;
}

class World
{
    public List<Entity> Entities = new();
    public Entity? Active; // the @s default at top level
}

class MockProps : IPropertyAdapter
{
    public bool TryGet(CastTarget t, string prop, out CastValue value)
    {
        value = CastValue.Null;
        if (t.Handle is not Entity e) return false;
        switch (prop)
        {
            case "health": value = new NumberValue(e.Health); return true;
            case "max_health": value = new NumberValue(e.MaxHealth); return true;
            case "id": value = new StringValue(e.Id); return true;
            case "position": value = new VectorValue(e.Pos.ToList()); return true;
            case "tags": value = new ArrayValue(e.Tags.Select(s => (CastValue)new StringValue(s)).ToList()); return true;
            default: return false;
        }
    }
    public bool TrySet(CastTarget t, string prop, CastValue value)
    {
        if (t.Handle is not Entity e) return false;
        switch (prop)
        {
            case "health": e.Health = ((NumberValue)value).N; return true;
            case "position": e.Pos = ((VectorValue)value).Components.ToArray(); return true;
            case "tags":
                e.Tags = ((ArrayValue)value).Items.OfType<StringValue>().Select(s => s.S).ToHashSet();
                return true;
            default: return false;
        }
    }
}

class MockScopes : IScopeHandler
{
    private readonly World _w;
    private readonly Random _rng = new(12345); // seeded for deterministic tests
    public MockScopes(World w) => _w = w;
    // e=entities, s=self, w=world, n/np=nearest(+player), r/rp=random(+player), p=players
    public bool Handles(string letters) =>
        letters is "e" or "s" or "w" or "n" or "r" or "p" or "np" or "rp" or "nc" or "rc";

    public IReadOnlyList<CastTarget> Resolve(ScopeQuery q)
    {
        if (q.Letters == "s")
            return q.Self is { } s ? new[] { s } : (_w.Active is { } a ? new[] { new CastTarget(a) } : Array.Empty<CastTarget>());

        if (q.Letters == "w")
            return new[] { new CastTarget(_w) };

        // Base candidate set, narrowed by kind letter and filter.
        IEnumerable<Entity> set = _w.Entities.Where(e => e.Alive);

        // kind letter (second char): p=player, c=creature (by tag, if present)
        char kind = q.Letters.Length > 1 ? q.Letters[^1] : '\0';
        if (kind == 'p') set = set.Where(e => e.Tags.Contains("player"));
        if (kind == 'c') set = set.Where(e => e.Tags.Contains("creature"));

        var list = set.ToList();

        // ordering: n=nearest (by distance from @s, or insertion order if no self),
        //           r=random order
        char order = q.Letters[0];
        if (order == 'n')
        {
            var origin = q.Self?.Handle as Entity;
            if (origin is not null)
                list = list.OrderBy(e => Dist(e.Pos, origin.Pos)).ToList();
        }
        else if (order == 'r')
        {
            list = list.OrderBy(_ => _rng.Next()).ToList();
        }

        var targets = list.Select(e => new CastTarget(e));
        if (q.Filter is { } f) targets = targets.Where(t => f(t));
        var result = targets.ToList();

        // (selection) slicing: indices/ranges into the ordered set
        if (q.Selection is { Count: > 0 })
            result = ApplySelection(result, q.Selection);

        return result;
    }

    static double Dist(double[] a, double[] b)
    {
        double s = 0; int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { var d = a[i] - b[i]; s += d * d; }
        return s; // squared distance is fine for ordering
    }

    static List<CastTarget> ApplySelection(List<CastTarget> ordered, IReadOnlyList<CastValue> sel)
    {
        var picked = new List<CastTarget>();
        foreach (var s in sel)
        {
            if (s is NumberValue n) { int idx = (int)n.N; if (idx >= 0 && idx < ordered.Count) picked.Add(ordered[idx]); }
            else if (s is RangeValue { Low: { } lo, High: { } hi })
                { for (int k = (int)lo; k <= (int)hi && k < ordered.Count; k++) if (k >= 0) picked.Add(ordered[k]); }
            else if (s is RangeValue { Low: { } lo2, High: null })
                { for (int j = (int)lo2; j < ordered.Count; j++) if (j >= 0) picked.Add(ordered[j]); }
        }
        return picked.Distinct().ToList();
    }
}

class MockCommands : ICommandHandler
{
    private readonly World _w;
    public MockCommands(World w) => _w = w;
    public bool Handles(string name) => name is "Kill" or "Tag";

    public CastValue Invoke(string name, IReadOnlyList<CastTarget> targets,
                        IReadOnlyList<CastValue> args, IReadOnlyDictionary<string, CastValue> named)
    {
        foreach (var t in targets)
        {
            if (t.Handle is not Entity e) continue;
            switch (name)
            {
                case "Heal": e.Health = Math.Min(e.MaxHealth, e.Health + Num(args, 0)); break;
                case "Hurt": e.Health -= Num(args, 0); if (e.Health <= 0) e.Alive = false; break;
                case "Kill": e.Alive = false; e.Health = 0; break;
                case "Tag": if (args.Count > 0 && args[0] is StringValue s) e.Tags.Add(s.S); break;
            }
        }
        return CastValue.Null;
    }
    static double Num(IReadOnlyList<CastValue> a, int i) => i < a.Count && a[i] is NumberValue n ? n.N : 0;
}

class MockPersistence : IPersistenceProvider
{
    public readonly Dictionary<string, string> Store = new();
    public void Write(string name, string buffer) => Store[name] = buffer;
    public string Read(string name) => Store.TryGetValue(name, out var b) ? b
        : throw new CastRuntimeException($"no save named '{name}'");
    public IReadOnlyList<string> List() => Store.Keys.ToList();
    public void Delete(string name) => Store.Remove(name);
}

// Captures say/msg output so tests can assert on it.
class MockOutput : IOutputChannels
{
    public readonly List<string> Said = new();
    public readonly List<(string text, VectorValue? pos)> Messages = new();
    public void Say(string message) => Said.Add(message);
    public void Msg(string message, VectorValue? position) => Messages.Add((message, position));
}

// In-memory file system over named directories: "dir/file" paths.
class MockDirectories : IDirectoryProvider
{
    public readonly Dictionary<string, string> Files = new();   // "scripts/boss.cast" -> contents
    private readonly HashSet<string> _writable = new();
    public MockDirectories(params string[] writableDirs) { foreach (var d in writableDirs) _writable.Add(d); }

    public bool TryRead(string path, out string contents) => Files.TryGetValue(path, out contents!);
    public bool TryWrite(string path, string contents)
    {
        var dir = path.Contains('/') ? path[..path.IndexOf('/')] : "";
        if (!_writable.Contains(dir)) return false;
        Files[path] = contents; return true;
    }
    public IReadOnlyList<string> List(string directory) =>
        Files.Keys.Where(k => k.StartsWith(directory + "/"))
                  .Select(k => k[(directory.Length + 1)..]).ToList();
}

// Creates entities of a kind into the world.
class MockSpawner : ISpawner
{
    private readonly World _w; private int _seq;
    public MockSpawner(World w) => _w = w;
    public IReadOnlyList<CastTarget> Spawn(IReadOnlyList<string> kind, int count,
        CastValue? where, IReadOnlyDictionary<string, CastValue> properties)
    {
        var made = new List<CastTarget>();
        var pos = where is VectorValue v ? v.Components.ToArray() : new double[] { 0, 0, 0 };
        for (int i = 0; i < count; i++)
        {
            var e = new Entity { Id = $"{kind[^1]}_{_seq++}", Pos = (double[])pos.Clone() };
            e.Tags.Add(kind[^1]);  // tag with the kind name
            if (properties.TryGetValue("health", out var h) && h is NumberValue hn) { e.Health = hn.N; e.MaxHealth = hn.N; }
            if (properties.TryGetValue("name", out var n) && n is StringValue ns) e.Id = ns.S;
            _w.Entities.Add(e); made.Add(new CastTarget(e));
        }
        return made;
    }
}

// Resolves namespaced ids to a value (here: the id as a string handle).
class MockIdResolver : IIdResolver
{
    public bool TryResolve(IReadOnlyList<string> segments, out CastValue value)
    {
        // a real host maps mod:type:name to a material byte / ScriptableObject / etc.
        // the mock resolves shadebreaker:phys:* to a small integer id.
        if (segments.Count == 3 && segments[0] == "shadebreaker" && segments[1] == "phys")
        {
            value = new NumberValue(segments[2] switch { "stone" => 1, "water" => 2, "lava" => 3, _ => 0 });
            return true;
        }
        value = CastValue.Null; return false;
    }
}

// Interprets a vector for @t as <day, minute, hour> instead of spatial xyz.
class MockTimeInterpreter : IVectorInterpreter
{
    public readonly List<(int day, int minute, int hour)> Applied = new();
    public bool Handles(string letters) => letters == "t";
    public void Apply(string letters, CastTarget target, VectorValue vector)
    {
        var c = vector.Components;
        Applied.Add(((int)c.ElementAtOrDefault(0), (int)c.ElementAtOrDefault(1), (int)c.ElementAtOrDefault(2)));
    }
}

class MockHost : IHost
{
    private readonly World _w;
    public MockHost(World w,
                    IPersistenceProvider? persistence = null,
                    IOutputChannels? output = null,
                    IDirectoryProvider? directories = null,
                    ISpawner? spawner = null,
                    IIdResolver? idResolver = null,
                    IReadOnlyList<IVectorInterpreter>? vectorInterpreters = null)
    {
        _w = w;
        ScopeHandlers = new[] { (IScopeHandler)new MockScopes(w) };
        Properties = new MockProps();
        CommandHandlers = new[] { (ICommandHandler)new MockCommands(w) };
        VectorInterpreters = vectorInterpreters ?? Array.Empty<IVectorInterpreter>();
        Persistence = persistence;
        Output = output;
        Directories = directories;
        Spawner = spawner;
        IdResolver = idResolver;
    }
    public IReadOnlyList<IScopeHandler> ScopeHandlers { get; }
    public IPropertyAdapter Properties { get; }
    public IReadOnlyList<ICommandHandler> CommandHandlers { get; }
    public IIdResolver? IdResolver { get; }
    public IReadOnlyList<IVectorInterpreter> VectorInterpreters { get; }
    public IPersistenceProvider? Persistence { get; }
    public IOutputChannels? Output { get; }
    public IDirectoryProvider? Directories { get; }
    public ISpawner? Spawner { get; }
    public CastTarget? AmbientSelf => _w.Active is { } a ? new CastTarget(a) : null;
}
