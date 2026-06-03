#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Cast.Lang;
using BloodlineArena;

class Runner
{
    static Arena BuildArena(int n, int seed)
    {
        var a = new Arena(seed);
        for (int i = 0; i < n; i++)
        {
            var c = new Creature
            {
                Uid = a.NextUid++,
                Lineage = a.NextLineage++,
                Pos = new[] { 2 + a.Rng.NextDouble() * (a.SizeX - 4),
                              a.Ceiling / 2,
                              2 + a.Rng.NextDouble() * (a.SizeZ - 4) },
                Vel = new[] { a.Rng.NextDouble() * 2 - 1, 0, a.Rng.NextDouble() * 2 - 1 },
            };
            c.Ancestors.Add(c.Lineage);
            a.Creatures.Add(c);
        }
        // the mage: persistent, death-plane-immune, starts at center
        var mage = new Creature
        {
            Uid = a.NextUid++, Lineage = a.NextLineage++, IsMage = true,
            Pos = (double[])a.Center.Clone(),
            Vel = new[] { a.Rng.NextDouble() * 2 - 1, 0, a.Rng.NextDouble() * 2 - 1 },
        };
        mage.Ancestors.Add(mage.Lineage);
        mage.Tags.Add("mage");
        a.Creatures.Add(mage);
        a.Mage = mage;
        return a;
    }

    static bool OutOfBounds(Arena a, double[] p) =>
        p[0] < 0 || p[0] > a.SizeX || p[2] < 0 || p[2] > a.SizeZ || p[1] > a.Ceiling;
    // (floor is solid; the box is open only at the floor — death planes are the four
    //  walls and the ceiling.)

    static void Main(string[] args)
    {
        int N      = args.Length > 0 ? int.Parse(args[0]) : 12;
        int Ticks  = args.Length > 1 ? int.Parse(args[1]) : 80;
        int Seed   = args.Length > 2 ? int.Parse(args[2]) : 3;
        bool Render = args.Contains("--render");

        var arena = BuildArena(N, Seed);
        var ev = new CastEvaluator(new ArenaHost(arena));

        // load rules + spawn-center vector for the mage teleport
        string rules = File.ReadAllText(FindFile("arena.cast"));
        ev.Run(rules);
        var ctr = arena.Center;
        ev.Run($"@v:spawn:center = <{ctr[0]}, {ctr[1]}, {ctr[2]}>");

        Console.WriteLine($"Bloodline Arena — {N} creatures + 1 mage, {Ticks} ticks, seed {Seed}");
        Console.WriteLine($"World box: {arena.SizeX}x{arena.SizeZ}, ceiling {arena.Ceiling}. Death planes: 4 walls + ceiling. Floor solid.");
        Console.WriteLine();

        for (int tick = 0; tick < Ticks; tick++)
        {
            StepPhysics(arena, ev);

            // Cast: kill anything the physics flagged out of bounds (non-mage).
            ev.Run("DeathPlanes");
            // clear the per-tick oob flags (dead ones are gone; survivors shouldn't keep it)
            foreach (var c in arena.Creatures) c.Tags.Remove("oob");

            // Advance one frame so every active standing cast fires this tick — this is
            // what makes the curse apply "going forward": descendants born after the
            // cursing get tagged when the curse cast re-fires on later ticks.
            ev.Tick();

            if (Render && (tick % Math.Max(1, Ticks / 12) == 0 || tick == Ticks - 1))
                RenderAscii(arena, tick);
        }

        Report(arena);
    }

    // One physics step: drift, wall/ceiling death detection, pairwise collision →
    // push + birth, and the special case of the mage being pushed out.
    static void StepPhysics(Arena a, CastEvaluator ev)
    {
        var live = a.Creatures.Where(c => c.Alive).ToList();

        // 1. integrate motion with a little random steering (the "random walk")
        foreach (var c in live)
        {
            c.Vel[0] += (a.Rng.NextDouble() * 2 - 1) * 0.4;
            c.Vel[2] += (a.Rng.NextDouble() * 2 - 1) * 0.4;
            double sp = Math.Sqrt(c.Vel[0]*c.Vel[0] + c.Vel[2]*c.Vel[2]);
            double max = 1.5;
            if (sp > max) { c.Vel[0] = c.Vel[0]/sp*max; c.Vel[2] = c.Vel[2]/sp*max; }
            c.Pos[0] += c.Vel[0];
            c.Pos[2] += c.Vel[2];
        }

        // 2. boundary check from free motion: non-mage out of bounds → flag 'oob'.
        //    The mage is immune; if it drifts out on its own we clamp it back in.
        foreach (var c in live)
        {
            if (!OutOfBounds(a, c.Pos)) continue;
            if (c.IsMage) ClampIn(a, c);
            else c.Tags.Add("oob");
        }

        // 3. pairwise collisions among still-in-bounds creatures: push apart + birth.
        var inField = live.Where(c => !c.Tags.Contains("oob")).ToList();
        for (int i = 0; i < inField.Count; i++)
            for (int j = i + 1; j < inField.Count; j++)
            {
                var c1 = inField[i]; var c2 = inField[j];
                if (!c1.Alive || !c2.Alive) continue;
                double dx = c2.Pos[0]-c1.Pos[0], dz = c2.Pos[2]-c1.Pos[2];
                double d2 = dx*dx + dz*dz;
                double rsum = c1.Radius + c2.Radius;
                if (d2 >= rsum*rsum || d2 == 0) continue;   // no contact

                double d = Math.Sqrt(d2);
                double overlap = rsum - d;
                double nx = dx/d, nz = dz/d;
                // push each out along the contact normal
                Push(c1, -nx, -nz, overlap/2);
                Push(c2,  nx,  nz, overlap/2);

                // mage pushed into a death plane?  (mage is one of the pair)
                HandleMagePush(a, ev, c1, c2);
                HandleMagePush(a, ev, c2, c1);

                // birth: two ordinary creatures colliding create a child carrying the
                // union of both ancestries. (Mage never breeds.)
                if (!c1.IsMage && !c2.IsMage && c1.Alive && c2.Alive)
                    Birth(a, ev, c1, c2);
            }
    }

    static void Push(Creature c, double nx, double nz, double amount)
    {
        c.Pos[0] += nx * amount;
        c.Pos[2] += nz * amount;
        // a push imparts a little velocity in the push direction
        c.Vel[0] += nx * 0.5; c.Vel[2] += nz * 0.5;
    }

    // If `maybeMage` is the mage and the push put it out of bounds, trigger the curse
    // against `other` (the creature that pushed it). Mage teleports to center; a
    // standing curse is raised over `other`'s lineage subtree.
    static void HandleMagePush(Arena a, CastEvaluator ev, Creature maybeMage, Creature other)
    {
        if (!maybeMage.IsMage) return;
        if (!OutOfBounds(a, maybeMage.Pos)) return;
        if (other.IsMage) return;

        a.MageCursings++;
        a.CursedLineage = other.Lineage;
        a.Events.Add($"  CURSE: creature #{other.Uid} pushed the mage out — bloodline {other.Lineage:F0} is cursed");
        // hand the offender's own lineage id to Cast, bind @s to the mage, fire the rule
        ev.Run($"@v:curse:lineage = {other.Lineage}");
        // @s is the mage via AmbientSelf; MageCursed teleports it and raises the cast
        ev.Run("MageCursed");
        // advance one frame so the freshly-raised standing curse cast fires now,
        // tagging existing descendants this tick (future births get tagged as the
        // cast re-fires on later ticks)
        ev.Tick();
    }

    static void Birth(Arena a, CastEvaluator ev, Creature p1, Creature p2)
    {
        // throttle: only breed if the world isn't already huge (keeps runs bounded)
        if (a.Creatures.Count(c => c.Alive) > 200) return;
        // child spawns between the parents, inheriting the union of both ancestor sets
        double mx = (p1.Pos[0]+p2.Pos[0])/2, mz = (p1.Pos[2]+p2.Pos[2])/2, my = a.Ceiling/2;
        var inherit = p1.Ancestors.Concat(p2.Ancestors).Distinct();
        string arr = "[" + string.Join(", ", inherit.Select(x => x.ToString("F0"))) + "]";
        ev.Run($"spawn arena:creature:child<{mx}, {my}, {mz}>[inherit: {arr}]");
        a.Events.Add($"  birth: #{p1.Uid}+#{p2.Uid} -> child (lineages {p1.Lineage:F0},{p2.Lineage:F0})");
    }

    static void ClampIn(Arena a, Creature c)
    {
        c.Pos[0] = Math.Clamp(c.Pos[0], 0.5, a.SizeX - 0.5);
        c.Pos[2] = Math.Clamp(c.Pos[2], 0.5, a.SizeZ - 0.5);
        if (c.Pos[1] > a.Ceiling) c.Pos[1] = a.Ceiling - 0.5;
    }

    // ── presentation ─────────────────────────────────────────────────────────────

    static void RenderAscii(Arena a, int tick)
    {
        const int W = 40, H = 20;
        var grid = new char[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) grid[y, x] = ' ';
        foreach (var c in a.Creatures.Where(c => c.Alive))
        {
            int gx = (int)(c.Pos[0] / a.SizeX * (W - 1));
            int gy = (int)(c.Pos[2] / a.SizeZ * (H - 1));
            gx = Math.Clamp(gx, 0, W - 1); gy = Math.Clamp(gy, 0, H - 1);
            char glyph = c.IsMage ? '@' : (c.Tags.Contains("cursed") ? 'x' : 'o');
            // mage and cursed take priority over plain
            if (grid[gy, gx] == ' ' || glyph == '@' || (glyph == 'x' && grid[gy, gx] == 'o'))
                grid[gy, gx] = glyph;
        }
        Console.WriteLine($"--- tick {tick} (live: {a.Creatures.Count(c => c.Alive)}) ---");
        Console.WriteLine("+" + new string('-', W) + "+");
        for (int y = 0; y < H; y++)
        {
            var sb = new StringBuilder("|");
            for (int x = 0; x < W; x++) sb.Append(grid[y, x]);
            sb.Append('|');
            Console.WriteLine(sb);
        }
        Console.WriteLine("+" + new string('-', W) + "+  (@ mage, o creature, x cursed)");
        Console.WriteLine();
    }

    static void Report(Arena a)
    {
        var live = a.Creatures.Where(c => c.Alive).ToList();
        int cursedAlive = live.Count(c => c.Tags.Contains("cursed"));
        int cursedEver = a.Creatures.Count(c => c.EverCursed);
        Console.WriteLine("=== final report ===");
        Console.WriteLine($"  total creatures ever:   {a.NextUid - 1}");
        Console.WriteLine($"  births:                 {a.Births}");
        Console.WriteLine($"  deaths (death planes):  {a.Deaths}");
        Console.WriteLine($"  mage cursings:          {a.MageCursings}");
        if (a.CursedLineage is { } cl)
            Console.WriteLine($"  cursed bloodline id:    {cl:F0}");
        Console.WriteLine($"  alive at end:           {live.Count}");
        Console.WriteLine($"  cursed — ever tagged:   {cursedEver}  (incl. those later killed)");
        Console.WriteLine($"  cursed — alive at end:  {cursedAlive}");
        Console.WriteLine($"  mage alive:             {a.Mage?.Alive}");
        Console.WriteLine();
        Console.WriteLine("  event log:");
        foreach (var e in a.Events.Take(40)) Console.WriteLine(e);
        if (a.Events.Count > 40) Console.WriteLine($"  ... and {a.Events.Count - 40} more events");
    }

    static string FindFile(string name)
    {
        foreach (var p in new[] { name,
            Path.Combine(AppContext.BaseDirectory, name),
            $"/home/claude/Cast/scenario/{name}" })
            if (File.Exists(p)) return p;
        throw new FileNotFoundException(name);
    }
}
