#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Cast.Lang;

// ── Cast benchmark arena ──────────────────────────────────────────────────────
// IDENTICAL native physics to cs_arena.cs; the rule portion (KillOob, curse
// application, standing curse) runs through the Cast interpreter via arena_rules.cast.
// Must reproduce the cross-language invariant (births/deaths/cursings/live/checksum).

namespace CastArena;

class World
{
    public const int SIZE = 20, HCEIL = 32, CAP = 2000;
    public double[] px = new double[CAP], pz = new double[CAP], ph = new double[CAP];
    public int[] vx = new int[CAP], vz = new int[CAP];
    public double[] lineage = new double[CAP];
    public List<double>[] ancestors = new List<double>[CAP];
    public bool[] alive = new bool[CAP], isMage = new bool[CAP], cursed = new bool[CAP], oob = new bool[CAP];
    public int N, births, deaths, cursings, live, mage;
    public double nextL = 1, cursedL = -1;

    public uint S;
    public uint Next() { S = (uint)(1664525u * S + 1013904223u); return S; }
    public int RInt(int n) => (int)(Next() % (uint)n);
    public int RSpan() => (int)(Next() % 3u) - 1;

    public bool Oob(int i) => px[i] < 0 || px[i] >= SIZE || pz[i] < 0 || pz[i] >= SIZE || ph[i] > HCEIL;

    public void Build(int n0)
    {
        N = 0; nextL = 1; births = deaths = cursings = 0; cursedL = -1;
        for (int k = 0; k < n0; k++)
        {
            int i = N++;
            px[i] = 2 + RInt(SIZE - 4); pz[i] = 2 + RInt(SIZE - 4); ph[i] = HCEIL / 2;
            vx[i] = RSpan(); vz[i] = RSpan();
            lineage[i] = nextL++; ancestors[i] = new List<double> { lineage[i] };
            alive[i] = true; isMage[i] = false; cursed[i] = false; oob[i] = false;
        }
        mage = N++;
        px[mage] = SIZE / 2; pz[mage] = SIZE / 2; ph[mage] = HCEIL / 2;
        vx[mage] = RSpan(); vz[mage] = RSpan();
        lineage[mage] = nextL++; ancestors[mage] = new List<double> { lineage[mage] };
        alive[mage] = true; isMage[mage] = true; cursed[mage] = false; oob[mage] = false;
        live = N;
    }
}

// Property adapter over the SoA world. @s carries the active creature index in a
// thin handle.
class Handle { public int I; public Handle(int i) => I = i; }

class ArenaProps : IPropertyAdapter
{
    readonly World _w;
    public ArenaProps(World w) => _w = w;
    public bool TryGet(CastTarget t, string prop, out CastValue value)
    {
        value = CastValue.Null;
        if (t.Handle is not Handle h) return false;
        int i = h.I;
        switch (prop)
        {
            case "oob":       value = new NumberValue(_w.oob[i] ? 1 : 0); return true;
            case "is_mage":   value = new NumberValue(_w.isMage[i] ? 1 : 0); return true;
            case "lineage":   value = new NumberValue(_w.lineage[i]); return true;
            case "ancestors": value = new ArrayValue(_w.ancestors[i].Select(a => (CastValue)new NumberValue(a)).ToList()); return true;
            case "cursed":    value = new NumberValue(_w.cursed[i] ? 1 : 0); return true;
            case "tags":      value = new ArrayValue(_w.cursed[i] ? new CastValue[] { new StringValue("cursed") } : Array.Empty<CastValue>()); return true;
            default: return false;
        }
    }
    public bool TrySet(CastTarget t, string prop, CastValue value)
    {
        if (t.Handle is not Handle h) return false;
        int i = h.I;
        if (prop == "tags" && value is ArrayValue a)
        {
            _w.cursed[i] = a.Items.OfType<StringValue>().Any(s => s.S == "cursed");
            return true;
        }
        return false;
    }
}

class ArenaScopes : IScopeHandler
{
    readonly World _w;
    public ArenaScopes(World w) => _w = w;
    public bool Handles(string letters) => letters is "e" or "s";
    public IReadOnlyList<CastTarget> Resolve(ScopeQuery q)
    {
        if (q.Letters == "s")
            return q.Self is { } s ? new[] { s } : Array.Empty<CastTarget>();
        var targets = new List<CastTarget>();
        for (int i = 0; i < _w.N; i++)
            if (_w.alive[i]) targets.Add(new CastTarget(new Handle(i)));
        IEnumerable<CastTarget> seq = targets;
        if (q.Filter is { } f) seq = seq.Where(t => f(t));
        return seq.ToList();
    }
}

class ArenaCommands : ICommandHandler
{
    readonly World _w;
    public ArenaCommands(World w) => _w = w;
    public bool Handles(string name) => name == "Kill";
    public CastValue Invoke(string name, IReadOnlyList<CastTarget> targets,
        IReadOnlyList<CastValue> args, IReadOnlyDictionary<string, CastValue> named)
    {
        foreach (var t in targets)
            if (t.Handle is Handle h && _w.alive[h.I] && !_w.isMage[h.I])
            { _w.alive[h.I] = false; _w.deaths++; _w.live--; }
        return CastValue.Null;
    }
}

class ArenaHost : IHost
{
    readonly World _w;
    public ArenaHost(World w)
    {
        _w = w;
        ScopeHandlers = new[] { (IScopeHandler)new ArenaScopes(w) };
        Properties = new ArenaProps(w);
        CommandHandlers = new[] { (ICommandHandler)new ArenaCommands(w) };
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

class Program
{
    static World _w = null!;
    static CastEvaluator _ev = null!;

    // physics — identical to cs_arena.cs
    static void StepPhysics()
    {
        var w = _w; int mid = World.SIZE / 2;
        for (int i = 0; i < w.N; i++)
        {
            if (!w.alive[i]) continue;
            int sx = w.RSpan(), sz = w.RSpan();
            if (w.px[i] < mid - 6) sx += 1; else if (w.px[i] > mid + 6) sx -= 1;
            if (w.pz[i] < mid - 6) sz += 1; else if (w.pz[i] > mid + 6) sz -= 1;
            w.vx[i] += sx; w.vz[i] += sz;
            if (w.vx[i] > 1) w.vx[i] = 1; if (w.vx[i] < -1) w.vx[i] = -1;
            if (w.vz[i] > 1) w.vz[i] = 1; if (w.vz[i] < -1) w.vz[i] = -1;
            w.px[i] += w.vx[i]; w.pz[i] += w.vz[i];
        }
        for (int i = 0; i < w.N; i++)
        {
            if (!w.alive[i] || !w.Oob(i)) continue;
            if (w.isMage[i]) { w.px[i] = Math.Clamp(w.px[i], 1, World.SIZE - 2); w.pz[i] = Math.Clamp(w.pz[i], 1, World.SIZE - 2); }
            else w.oob[i] = true;
        }
        int count = w.N;
        for (int i = 0; i < count; i++)
        {
            if (!w.alive[i] || w.oob[i]) continue;
            for (int j = i + 1; j < count; j++)
            {
                if (!w.alive[j] || w.oob[j]) continue;
                if (w.px[i] != w.px[j] || w.pz[i] != w.pz[j]) continue;
                int dirx = w.px[j] >= mid ? 1 : -1, dirz = w.pz[j] >= mid ? 1 : -1;
                w.px[j] += dirx * 2; w.pz[j] += dirz * 2;
                if (w.isMage[i] && w.Oob(i)) Curse(w.lineage[j]);
                else if (w.isMage[j] && w.Oob(j)) Curse(w.lineage[i]);
                else if (!w.isMage[i] && !w.isMage[j]) Birth(i, j);
            }
        }
    }

    // Birth is host-side bookkeeping (physics knows the pair + location), identical to
    // native — including the at-birth curse tag, so the dynamics match exactly.
    static void Birth(int p1, int p2)
    {
        var w = _w;
        if (w.live > 400 || w.N >= World.CAP) return;
        int c = w.N++;
        w.px[c] = (w.px[p1] + w.px[p2]) / 2; w.pz[c] = (w.pz[p1] + w.pz[p2]) / 2; w.ph[c] = World.HCEIL / 2;
        w.vx[c] = w.RSpan(); w.vz[c] = w.RSpan();
        w.lineage[c] = w.nextL++;
        var anc = new List<double> { w.lineage[c] };
        foreach (var a in w.ancestors[p1]) if (!anc.Contains(a)) anc.Add(a);
        foreach (var a in w.ancestors[p2]) if (!anc.Contains(a)) anc.Add(a);
        w.ancestors[c] = anc;
        w.alive[c] = true; w.isMage[c] = false; w.oob[c] = false;
        w.cursed[c] = w.cursedL >= 0 && anc.Contains(w.cursedL);
        w.births++; w.live++;
    }

    // Mage pushed out: host teleports the mage + sets @v:curse:lineage, then the
    // SCRIPTED rule MageCursed applies the curse through the interpreter.
    static void Curse(double offenderLineage)
    {
        var w = _w;
        w.cursings++; w.cursedL = offenderLineage;
        w.px[w.mage] = World.SIZE / 2; w.pz[w.mage] = World.SIZE / 2; w.ph[w.mage] = World.HCEIL / 2;
        _ev.Run($"@v:curse:lineage = {offenderLineage}");
        _ev.Run("MageCursed");
    }

    static void Main(string[] args)
    {
        int N0    = args.Length > 0 ? int.Parse(args[0]) : 30;
        int Ticks = args.Length > 1 ? int.Parse(args[1]) : 300;
        uint seed = args.Length > 2 ? uint.Parse(args[2]) : 12345;

        var (b, d, c, l, chk, ms) = RunTimed(N0, Ticks, seed);
        Console.WriteLine($"cast\t{N0}\t{Ticks}\t{ms:F1}\t{b}\t{d}\t{c}\t{l}\t{chk:F0}");
    }

    static (int,int,int,int,double,double) RunTimed(int n0, int ticks, uint seed)
    {
        string rules = File.ReadAllText(FindFile("arena_rules.cast"));
        // warmup
        Setup(rules, n0, seed); RunLoop(10);
        // timed
        Setup(rules, n0, seed);
        var sw = Stopwatch.StartNew();
        RunLoop(ticks);
        sw.Stop();
        double chk = 0;
        for (int i = 0; i < _w.N; i++)
            if (_w.alive[i]) chk += _w.px[i]*3 + _w.pz[i]*5 + _w.ph[i]*7 + _w.lineage[i]*11 + (_w.cursed[i] ? 13 : 0);
        return (_w.births, _w.deaths, _w.cursings, _w.live, chk, sw.Elapsed.TotalMilliseconds);
    }

    static void Setup(string rules, int n0, uint seed)
    {
        _w = new World { S = seed };
        _ev = new CastEvaluator(new ArenaHost(_w));
        _ev.Run(rules);
        _ev.Run("@v:has_curse = 0");
        _w.Build(n0);
    }

    static void RunLoop(int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            StepPhysics();
            _ev.Run("KillOob");
            _ev.Run("StandingCurse");
            for (int i = 0; i < _w.N; i++) _w.oob[i] = false;
        }
    }

    static string FindFile(string name)
    {
        foreach (var p in new[] { name, Path.Combine(AppContext.BaseDirectory, name),
            $"/home/claude/Cast/bench_arena/{name}" })
            if (File.Exists(p)) return p;
        throw new FileNotFoundException(name);
    }
}
