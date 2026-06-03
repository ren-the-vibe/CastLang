# Arena speed reference (mixed host + rules)

A second speed reference, complementing `bench/`. Where `bench/` is pure compute
(everything interpreted), this runs the **bloodline arena** — a realistic mixed
workload where the host does the heavy physics natively and only the *rules* are
scripted. That's how Cast is actually used in a game, so this number is the more
representative one.

## What's identical, what differs

Every implementation runs:
- the **same portable PRNG** — a 32-bit LCG (`state = 1664525*state + 1013904223 mod
  2^32`), verified bit-identical across C#, Python, JS, and Lua;
- the **same deterministic physics** — random-walk movement with a centering bias,
  grid-cell collisions with an outward push, wall/ceiling death planes, and a
  death-plane-immune mage — all native in each language;
- the **same rules** — kill out-of-bounds creatures, birth-with-ancestry on
  collision, and the mage's bloodline curse.

The only difference is *where the rules run*: inline in C#/Python/JS/Lua, through the
**Cast interpreter** in the Cast build. So the Cast row isolates interpreter overhead
on the scripted portion of a real workload.

The harness gates on a shared **invariant** — births, deaths, cursings, final live
count, and a position/lineage/curse checksum — which must be identical across all
five before any timing is reported. If they diverge, the run fails (the comparison
would be meaningless).

## Why this differs so much from `bench/`

In `bench/`, the whole workload is interpreted, and Cast lands ~500× off native. Here,
the O(N²) collision loop and movement integration are native C# in *every*
implementation; only the per-tick rule calls cross into the interpreter. So Cast pays
its tax on a small slice of the total work and lands close to native — and ahead of
the other interpreted languages, which run *everything* in their own runtime.

Read the two together: `bench/` is the worst case (all logic in Cast), `bench_arena/`
is the realistic case (native engine + scripted rules). Real Cast usage sits near the
arena number.

## Files

- `arena_rules.cast` — the scripted rules (`KillOob`, `MageCursed`, `ApplyCurse`, `StandingCurse`)
- `cast_arena.cs` — host with native physics + rules driven through Cast
- `cs_arena.cs` — native C# reference (rules inline); defines the invariant
- `py_arena.py`, `js_arena.js`, `lua_arena.lua` — same arena, rules inline
- `run.sh` — builds, runs all five, gates on the invariant, prints the table

## Running

```bash
./bench_arena/run.sh [N creatures] [ticks] [seed]   # defaults: 30, 300, 12345
```

## Representative result

`N=30, ticks=300, seed=12345` (invariant `1969/1976/3/24/50733`):

| language   |    ms | vs Cast |
|------------|------:|--------:|
| csharp     |  56.3 |    2.6× |
| **cast**   | 144.4 |    1.0× |
| javascript | 167.5 |    0.9× |
| lua        | 376.8 |    0.4× |
| python     | 986.7 |    0.1× |

Cast is ~2.6× off hand-written C# and faster than the other interpreted runtimes here,
because the physics is native in all five and only the rules are interpreted. (Numbers
vary run to run; ratios are the stable signal.)
