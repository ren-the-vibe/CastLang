#nullable enable
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using Cast.Lang;

// ── Self-contained world + host for the benchmark ─────────────────────────────

class BEntity
{
    public double Health, MaxHealth;
    public double Poisoned;        // 0/1 flag
    public double[] Pos = { 0, 0, 0 };
}

class BWorld { public List<BEntity> Entities = new(); }

class BProps : IPropertyAdapter
{
    public bool TryGet(CastTarget t, string prop, out CastValue value)
    {
        value = CastValue.Null;
        if (t.Handle is not BEntity e) return false;
        switch (prop)
        {
            case "health":      value = new NumberValue(e.Health); return true;
            case "max_health":  value = new NumberValue(e.MaxHealth); return true;
            case "poisoned":    value = new NumberValue(e.Poisoned); return true;
            default: return false;
        }
    }
    public bool TrySet(CastTarget t, string prop, CastValue value)
    {
        if (t.Handle is not BEntity e) return false;
        switch (prop)
        {
            case "health":   e.Health = ((NumberValue)value).N; return true;
            case "poisoned": e.Poisoned = ((NumberValue)value).N; return true;
            default: return false;
        }
    }
}

class BScopes : IScopeHandler
{
    readonly BWorld _w;
    public BScopes(BWorld w) => _w = w;
    public bool Handles(string letters) => letters is "e" or "s";
    public IReadOnlyList<CastTarget> Resolve(ScopeQuery q)
    {
        if (q.Letters == "s")
            return q.Self is { } s ? new[] { s } : Array.Empty<CastTarget>();
        var targets = _w.Entities.Select(e => new CastTarget(e));
        if (q.Filter is { } f) targets = targets.Where(t => f(t));
        return targets.ToList();
    }
}

class BCommands : ICommandHandler
{
    public bool Handles(string name) => false;
    public CastValue Invoke(string n, IReadOnlyList<CastTarget> t, IReadOnlyList<CastValue> a, IReadOnlyDictionary<string, CastValue> m) => CastValue.Null;
}

class BHost : IHost
{
    public BHost(BWorld w)
    {
        ScopeHandlers = new[] { (IScopeHandler)new BScopes(w) };
        Properties = new BProps();
        CommandHandlers = new[] { (ICommandHandler)new BCommands() };
        VectorInterpreters = Array.Empty<IVectorInterpreter>();
    }
    public IReadOnlyList<IScopeHandler> ScopeHandlers { get; }
    public IPropertyAdapter Properties { get; }
    public IReadOnlyList<ICommandHandler> CommandHandlers { get; }
    public IIdResolver? IdResolver => null;
    public IReadOnlyList<IVectorInterpreter> VectorInterpreters { get; }
    public IPersistenceProvider? Persistence => null;
    public IOutputChannels? Output => null;
    public IDirectoryProvider? Directories => null;
    public ISpawner? Spawner => null;
    public CastTarget? AmbientSelf => null;
}

// ── Harness ───────────────────────────────────────────────────────────────────

class Program
{
    static BWorld BuildWorld(int n)
    {
        var w = new BWorld();
        // Deterministic seeding so every language builds the identical world.
        for (int i = 0; i < n; i++)
        {
            double mh = 50 + (i % 100);                 // 50..149
            double hp = ((i * 37) % (int)mh) + 1;       // 1..mh, deterministic
            w.Entities.Add(new BEntity {
                MaxHealth = mh,
                Health = hp,
                Poisoned = (i % 3 == 0) ? 1 : 0          // every third poisoned
            });
        }
        return w;
    }

    static void Main(string[] args)
    {
        int N = args.Length > 0 ? int.Parse(args[0]) : 2000;
        int T = args.Length > 1 ? int.Parse(args[1]) : 200;

        var world = BuildWorld(N);
        var ev = new CastEvaluator(new BHost(world));
        // load the workload function + zero the accumulators
        string workload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "workload.cast"));
        // fall back to a known path if not next to the dll
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "workload.cast")))
            workload = File.ReadAllText("/home/claude/Cast/bench/workload.cast");
        ev.Run(workload);
        ev.Run("@v:total_damage = 0");
        ev.Run("@v:checksum = 0");

        // Warm up the interpreter (JIT, caches) with a few untimed ticks.
        for (int i = 0; i < 3; i++) ev.Run("@e PoisonTick");
        // reset world + accumulators after warmup
        world = BuildWorld(N);
        ev = new CastEvaluator(new BHost(world));
        ev.Run(workload);
        ev.Run("@v:total_damage = 0");
        ev.Run("@v:checksum = 0");

        var sw = Stopwatch.StartNew();
        for (int tick = 0; tick < T; tick++)
            ev.Run("@e PoisonTick");
        sw.Stop();

        double checksum = ((NumberValue)ev.Run("@v:checksum")).N;
        double totalDmg = ((NumberValue)ev.Run("@v:total_damage")).N;
        long ops = (long)N * T;
        Console.WriteLine($"cast\t{N}\t{T}\t{sw.Elapsed.TotalMilliseconds:F1}\t{ops / sw.Elapsed.TotalSeconds / 1e6:F3}\t{checksum:F0}\t{totalDmg:F0}");
    }
}
