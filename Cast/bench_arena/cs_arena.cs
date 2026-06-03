using System;
using System.Diagnostics;
using System.Collections.Generic;

// ── Deterministic benchmark arena (native C# reference) ───────────────────────
// Fully deterministic: a portable 32-bit LCG drives all randomness, fixed
// iteration order, no divergent float accumulation. The rules portion (kill OOB /
// birth-with-ancestry / curse-bloodline) is inline here; the Cast version runs the
// same rules through the interpreter. All five implementations must produce the
// same invariant (births/deaths/cursings/live/checksum).

class ArenaBench
{
    // grid world; positions are integers in [0,SIZE). walls+ceiling lethal.
    const int SIZE = 20;          // world is SIZE x SIZE (x,z); a 1-D "height" h in [0,HCEIL]
    const int HCEIL = 32;

    // LCG ---------------------------------------------------------------------
    static uint _s;
    static void Seed(uint s) => _s = s;
    static uint Next() { _s = (uint)(1664525u * _s + 1013904223u); return _s; }
    static int RInt(int n) => (int)(Next() % (uint)n);          // 0..n-1
    static int RSpan() => (int)(Next() % 3u) - 1;               // -1,0,+1

    // creatures as struct-of-arrays for cache-friendliness and easy porting -----
    static int N;
    static double[] px = null!, pz = null!, ph = null!;          // position
    static int[] vx = null!, vz = null!;                          // velocity (-1,0,+1 each axis)
    static double[] lineage = null!;                             // own lineage id
    static List<double>[] ancestors = null!;                      // ancestor id sets
    static bool[] alive = null!, isMage = null!, cursed = null!, oob = null!;
    static int capacity;

    static int births, deaths, cursings, liveCount;
    static double nextLineage;
    static double cursedLineageActive = -1;   // the one cursed bloodline id (or -1)

    static void Alloc(int cap)
    {
        capacity = cap;
        px = new double[cap]; pz = new double[cap]; ph = new double[cap];
        vx = new int[cap]; vz = new int[cap];
        lineage = new double[cap]; ancestors = new List<double>[cap];
        alive = new bool[cap]; isMage = new bool[cap]; cursed = new bool[cap]; oob = new bool[cap];
    }

    static int mageIdx;

    static void Build(int n0)
    {
        N = 0; nextLineage = 1; births = deaths = cursings = 0; cursedLineageActive = -1;
        for (int i = 0; i < n0; i++)
        {
            int idx = N++;
            px[idx] = 2 + RInt(SIZE - 4);
            pz[idx] = 2 + RInt(SIZE - 4);
            ph[idx] = HCEIL / 2;
            vx[idx] = RSpan(); vz[idx] = RSpan();
            lineage[idx] = nextLineage++;
            ancestors[idx] = new List<double> { lineage[idx] };
            alive[idx] = true; isMage[idx] = false; cursed[idx] = false; oob[idx] = false;
        }
        // the mage
        mageIdx = N++;
        px[mageIdx] = SIZE / 2; pz[mageIdx] = SIZE / 2; ph[mageIdx] = HCEIL / 2;
        vx[mageIdx] = RSpan(); vz[mageIdx] = RSpan();
        lineage[mageIdx] = nextLineage++;
        ancestors[mageIdx] = new List<double> { lineage[mageIdx] };
        alive[mageIdx] = true; isMage[mageIdx] = true; cursed[mageIdx] = false; oob[mageIdx] = false;
        liveCount = N;
    }

    static bool OutOfBounds(int i) =>
        px[i] < 0 || px[i] >= SIZE || pz[i] < 0 || pz[i] >= SIZE || ph[i] > HCEIL;

    // ── RULES (inline here; interpreted in the Cast version) ──────────────────
    static void RuleKillOob()
    {
        for (int i = 0; i < N; i++)
            if (alive[i] && oob[i] && !isMage[i]) { alive[i] = false; deaths++; liveCount--; }
    }
    static void RuleBirth(int p1, int p2)
    {
        if (liveCount > 400) return;          // bound the run
        if (N >= capacity) return;
        int c = N++;
        px[c] = (px[p1] + px[p2]) / 2; pz[c] = (pz[p1] + pz[p2]) / 2; ph[c] = HCEIL / 2;
        vx[c] = RSpan(); vz[c] = RSpan();
        lineage[c] = nextLineage++;
        var anc = new List<double> { lineage[c] };
        foreach (var a in ancestors[p1]) if (!anc.Contains(a)) anc.Add(a);
        foreach (var a in ancestors[p2]) if (!anc.Contains(a)) anc.Add(a);
        ancestors[c] = anc;
        alive[c] = true; isMage[c] = false; oob[c] = false;
        // a newborn under the active curse is tagged (cast re-fires "going forward")
        cursed[c] = cursedLineageActive >= 0 && anc.Contains(cursedLineageActive);
        births++; liveCount++;
    }
    static void RuleCurse(double offenderLineage)
    {
        cursings++;
        cursedLineageActive = offenderLineage;        // standing curse keyed on lineage id
        // teleport mage to center
        px[mageIdx] = SIZE / 2; pz[mageIdx] = SIZE / 2; ph[mageIdx] = HCEIL / 2;
        // tag the offender's subtree (everyone carrying the offender's own lineage id)
        for (int i = 0; i < N; i++)
            if (alive[i] && ancestors[i].Contains(offenderLineage)) cursed[i] = true;
    }
    static void RuleStandingCurseTick()
    {
        // the standing curse re-applies each tick (catches anything not yet tagged)
        if (cursedLineageActive < 0) return;
        for (int i = 0; i < N; i++)
            if (alive[i] && !cursed[i] && ancestors[i].Contains(cursedLineageActive)) cursed[i] = true;
    }

    // ── physics (identical native code in every language) ─────────────────────
    static void StepPhysics()
    {
        int mid = SIZE / 2;
        // 1. integrate motion with deterministic random steering, biased toward
        //    center so the population persists (walls stay reachable only via pushes
        //    and unlucky drift, which is what produces deaths and mage-evictions).
        for (int i = 0; i < N; i++)
        {
            if (!alive[i]) continue;
            int sx = RSpan(), sz = RSpan();
            if (px[i] < mid - 6) sx += 1; else if (px[i] > mid + 6) sx -= 1;
            if (pz[i] < mid - 6) sz += 1; else if (pz[i] > mid + 6) sz -= 1;
            vx[i] += sx; vz[i] += sz;
            if (vx[i] > 1) vx[i] = 1; if (vx[i] < -1) vx[i] = -1;
            if (vz[i] > 1) vz[i] = 1; if (vz[i] < -1) vz[i] = -1;
            px[i] += vx[i]; pz[i] += vz[i];
        }
        // 2. boundary: mage clamps, others flagged oob
        for (int i = 0; i < N; i++)
        {
            if (!alive[i] || !OutOfBounds(i)) continue;
            if (isMage[i]) { px[i] = Math.Clamp(px[i], 1, SIZE - 2); pz[i] = Math.Clamp(pz[i], 1, SIZE - 2); }
            else oob[i] = true;
        }
        // 3. collisions in fixed order: same cell -> push outward + (birth or mage-curse).
        //    N is captured before this phase so newborns do NOT collide in the same tick
        //    (they enter the simulation next tick). This keeps the dynamics identical and
        //    deterministic across all language ports.
        int count = N;
        for (int i = 0; i < count; i++)
        {
            if (!alive[i] || oob[i]) continue;
            for (int j = i + 1; j < count; j++)
            {
                if (!alive[j] || oob[j]) continue;
                if (px[i] != px[j] || pz[i] != pz[j]) continue;   // collision = same cell
                int dirx = px[j] >= mid ? 1 : -1;
                int dirz = pz[j] >= mid ? 1 : -1;
                px[j] += dirx * 2; pz[j] += dirz * 2;
                if (isMage[i] && OutOfBounds(i)) RuleCurse(lineage[j]);
                else if (isMage[j] && OutOfBounds(j)) RuleCurse(lineage[i]);
                else if (!isMage[i] && !isMage[j]) RuleBirth(i, j);
            }
        }
    }

    static (int births, int deaths, int cursings, int live, double checksum) Run(int n0, int ticks)
    {
        Build(n0);
        for (int t = 0; t < ticks; t++)
        {
            StepPhysics();
            RuleKillOob();
            RuleStandingCurseTick();
            for (int i = 0; i < N; i++) oob[i] = false;
        }
        // checksum: fold positions + lineage + cursed flags of the living
        double sum = 0;
        for (int i = 0; i < N; i++)
            if (alive[i]) sum += px[i] * 3 + pz[i] * 5 + ph[i] * 7 + lineage[i] * 11 + (cursed[i] ? 13 : 0);
        return (births, deaths, cursings, liveCount, sum);
    }

    static void Main(string[] args)
    {
        int N0    = args.Length > 0 ? int.Parse(args[0]) : 30;
        int Ticks = args.Length > 1 ? int.Parse(args[1]) : 300;
        uint seed = args.Length > 2 ? uint.Parse(args[2]) : 12345;

        Alloc(2000);
        // warmup
        Seed(seed); Run(N0, 10);
        // timed
        Seed(seed);
        var sw = Stopwatch.StartNew();
        var r = Run(N0, Ticks);
        sw.Stop();

        Console.WriteLine($"csharp\t{N0}\t{Ticks}\t{sw.Elapsed.TotalMilliseconds:F1}\t{r.births}\t{r.deaths}\t{r.cursings}\t{r.live}\t{r.checksum:F0}");
    }
}
