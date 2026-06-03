# Cast

A portable command language for game runtimes. Every command *casts* an action over a scope.

## Contents

- `cast_spec.md` — the language specification (authoritative)
- `cast_guide.md` — a script-writer's guide (reader-oriented, organized by path through the language)
- `cast_grammar.md` — the formal PEG grammar, in four layers (lexer, expressions, commands/statements/cast, value-literals/slots), with all parse findings resolved or confirmed
- `Cast/` — the C# implementation (in progress)

## C# implementation status

- **Lexer** — complete and tested. `Cast/src/Cast.Lang/{CastTokenType,CastToken,CastLexer}.cs`. Implements the grammar's maximal-munch token rules (including the vector-depth `>`-closing rule); validated by `Cast/tests/lex_driver.cs` (18 spec snippets + 15 targeted assertions, all green).
- **Parser** — complete and tested. `Cast/src/Cast.Lang/{Ast,CastParser}.cs`. Recursive descent for statements/commands/scope-chains plus a precedence-climbing expression core; the three grammar carve-outs (range-complement `!`, `~>` magnitude, `cast` prefix) are handled by dedicated productions. Validated by `Cast/tests/parse_driver.cs` (18 spec programs + 15 structural assertions, all green).
- **Evaluator (host-free core)** — complete and tested. `Cast/src/Cast.Lang/{CastValue,CastEnvironment,CastTimers,CastEvaluator}.cs`. Implements the evaluation model for everything that doesn't need a host: arithmetic and precedence, truthiness, all comparison modes (equality, range membership, range-complement, type-witness, glob, inequality), logic, the binding family (`=`/`=>`/`=&`/compound/`++`), the language-owned `@v` registry and `@t` timer/counter system (sign semantics, null-on-crossing, pause/speed), functions (`arg`/`param`/`out`, dual-call form), iteration (`collect`/`iter`/early `out`), membership, and vectors operated on as wholes. Validated by `Cast/tests/eval_driver.cs` (60+ assertions, all green). Anything host-dependent (entity/world scopes, property get/set, world-acting commands, cast firing) routes through a single `NeedsHost(...)` boundary that throws until the host layer is wired.
- **Host binding** — interface complete and tested end-to-end. `Cast/src/Cast.Lang/Host.cs` defines the eight-piece contract from the spec (scope handlers, property adapter, command handlers, id resolver, vector interpreters, persistence, output channels). The evaluator resolves scope chains to host targets, dispatches commands with per-target `@s` auto-iteration, reads/writes properties (dot-access and assignment), applies the two-stage filter rule, and runs placement/step against host positions with relative-component resolution. Validated by `Cast/tests/host_driver.cs` against a mock entity world (`Cast/tests/mock_host.cs`): property read/write, scoped commands, filtered commands, tag filters, placement with `<~,5,~>` relative components, normalized `~>` step, and `as`-redirect — all green.
- **Cast subscription mechanism** — complete and tested. `Cast/src/Cast.Lang/CastRuntime.cs` plus the evaluator's cast handling. Implements fire-and-forget (run-once, or N times), level-triggered standing casts (fire every tick the target fulfills the scope), edge-triggered casts (count N: fire on entry, fresh edge on re-entry), condition triggers (`@v:cond`), `over N` frame-spread, the `@w.casts` lifecycle (`cast` returns an id, `casts` lists, `uncast[id]` removes), and the session-local save exclusion (a cast referencing `$` in its own tokens is dropped from save; one referencing `@v` is saveable — scoped to own tokens, not the call graph). The host advances frames via `Evaluator.Tick()`. Validated by `Cast/tests/cast_driver.cs`, all green.

## Test suites

Nine drivers, all green: `lex_driver`, `parse_driver`, `eval_driver`, `host_driver`, `cast_driver`, `stdlib_driver`, `persist_driver`, `pipe_driver`, `extras_driver` (spawn, say/msg, file I/O, id resolution, vector interpreters, ordered-scope selection). Run any with `./build.sh tests/<name>.cs`.

The pipe operators are pure language (no host): `|>` is the source-first form of `=` (`value |> $var` writes and passes the value through), and `|` sends a value into a command as its primary argument (`arg[0]`), iterating per element when the value is a set scope (`@e | Heal[5]`). Mid-chain capture (`5 | Double |> $captured`) works via the `|`/`|>` precedence interaction.

## Standard library & persistence

Standard library: intrinsic math (`Floor`/`Ceil`/`Round`/`Abs`/`Min`/`Max`/`Clamp`/`Sqrt`/`Pow`/`Sin`/`Cos`/`Atan2`) is native; the entity-acting functions (`Heal`/`Hurt`/`SetHealth`) are a Cast-source prelude loaded at startup, so they run through the real parse+eval+`=&` dual-call+property path; `rng`/`def`/`clear`/`tag`/`untag` are language built-ins. Command args use brackets (`tag['boss']`, `Heal[50]`, `Heal{amount: 50}`); `cast` and `spawn` keep their own structured grammar.

Persistence: `save`/`load`/`qsave`/`qload`/`saves`/`unsave` via the host's `IPersistenceProvider`. `StateSerializer.cs` round-trips `@v`, user functions, and saveable casts (functions/casts stored as re-parseable source via an AST unparser). The session-local save exclusion is enforced and reported.

## Building

The normal path is `dotnet build` against the `.csproj` files. In a sandbox without
NuGet access, use `Cast/build.sh`, which compiles directly with the Roslyn compiler
against the .NET 8 reference assemblies bundled with the SDK (no package restore):

```
cd Cast
./build.sh                      # builds + runs the lexer test driver
./build.sh tests/some_other.cs  # build + run a different driver
```

Requires the .NET 8 SDK. The script locates `csc.dll` and the `net8.0` reference
assemblies under `/usr/lib/dotnet` automatically.

## Speed reference

`bench/` holds a five-way benchmark of the same poison-tick workload (Cast, native
C#, Python, Node, Lua), with a checksum gate that proves all five do identical
work. Run `./bench/run.sh [N] [T]`. See `bench/README.md` for methodology and the
interpreter-overhead caveat (Cast runs on the tree-walking C# evaluator).

## Optimization history

The tree-walking evaluator was profiled (time-isolation per operation, not just call
counts) and the hot paths optimized without changing semantics — all test suites and
both benchmark invariants stay green throughout:

- **Property access fast paths** (~27%): `@s.health` reads/writes resolved straight
  off the current self, skipping the `ArrayValue`+`TargetValue`+`List` wrapping that
  a general scope-chain value would allocate only to be unwrapped immediately.
- **`@v` key caching** (~10%): the colon-joined registry key (e.g. `score:player1`)
  is computed once per AST node instead of `string.Join` on every access.
- **Zero-arg call + intrinsic cleanup**: shared empty `arg`/`param` singletons for
  no-argument calls; intrinsics (`Clamp`/`Min`/…) checked before the rarer built-ins.

Net: the pure-compute `bench/` workload went from ~500× off native C# to ~290×
(~36× → ~20× off CPython). Things *not* found to be bottlenecks (verified, not
assumed): re-parsing (<1% of time — the benchmark re-parses each tick and it barely
registers) and allocation pressure on its own (removing allocations only helped where
it also removed work on the hottest path). The realistic `bench_arena/` workload
moved less (~7%) because most of its time is native host physics, not interpreter.
