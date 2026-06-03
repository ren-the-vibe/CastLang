# Cast speed reference

A five-way benchmark of the same game-logic workload, to establish where Cast
sits performance-wise. The point is a **reference number for Cast**, not a
language shootout.

## What it measures (and the caveat)

Cast is a command language interpreted by a tree-walking evaluator written in C#.
So the Cast row measures **interpreter overhead** — the cost of expressing logic
in Cast and running it through the evaluator — against the same logic written
natively (C#) or run on a mature interpreter/JIT (Node, Lua, Python). It is *not*
a comparison of language design or theoretical ceilings; a bytecode VM or a
JIT for Cast would land somewhere else entirely. Read the Cast number as "what it
costs today to run this in the reference tree-walker," and the others as the
range of what hand-writing the same logic looks like.

## The workload

A "poison field" tick over `N` entities, run for `T` ticks — representative game
logic mixing iteration, conditional branching, property reads/writes, arithmetic,
and `Clamp`/`Min`. Per entity per tick:

- **poisoned** → take `Clamp(max_health * 0.05, 1, 25)` damage; if health falls to
  ≤0, die (clear poison, health 0); accumulate damage.
- else if **health ≤ max_health * 0.30** → regenerate `Min(max_health*0.02,
  max_health - health)`.
- always → add health to a running checksum.

The world is built deterministically (same seed formula in every language), so all
five process byte-identical state. The harness **verifies every implementation
produced the same checksum** before reporting — if they diverge, the run fails,
because the comparison would be meaningless.

## Files

- `workload.cast` — the Cast implementation (the reference; helpers `ApplyPoison`,
  `Die`, `Regen`, driver `PoisonTick`, run via `@e PoisonTick` each tick)
- `cast_bench.cs` — host + harness that loads the workload and ticks it
- `cs_native.cs` — hand-written C# baseline (no interpreter)
- `py_bench.py`, `js_bench.js`, `lua_bench.lua` — the same logic in each language
- `run.sh` — builds, runs all five, verifies checksums, prints the table

## Running

```bash
./bench/run.sh [N] [T]      # defaults: 2000 entities, 200 ticks
```

Requires `dotnet` (.NET 8), `python3`, `node`, and `lua5.4` on PATH.

## Representative result

`N=2000, T=200` (400,000 entity-ticks), one sample machine:

| language   |     ms | Mops/s | vs Cast |
|------------|-------:|-------:|--------:|
| csharp     |    5.2 |   77.2 |    292× |
| javascript |   14.6 |   27.5 |    104× |
| lua        |   21.3 |   18.8 |     71× |
| python     |   77.3 |    5.2 |     20× |
| **cast**   | 1516.5 |   0.26 |      1× |

The tree-walker runs this workload at ~0.26 M entity-ticks/sec — about 290× off
hand-written C# and ~20× off CPython. (An earlier version was ~500×/36×; targeted
hot-path work on property access and `@v` keys closed roughly a third of the gap —
see "Optimization history" in the top-level README.) The remaining gap is the
tree-walk itself plus host-binding round-trips on every property access; it's the
headroom a future bytecode/JIT path would target. Numbers vary run to run; the
*ratios* are the stable signal.
