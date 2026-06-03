# Cast — Language Specification

Cast is a portable command language for game runtimes. Files use the `.cast` extension. Every command *casts* an action over a scope — the verb captures the language's core operation.

## Overview

Cast operates on **values** (data with a definite kind) and **scopes** (addressable things in the world). Every value has exactly one kind, identified by its constructor; the runtime always knows the kind of every value and which operations are valid on it. There is no untyped data.

### Value kinds

| Kind | Constructor | Notes |
|---|---|---|
| Number | `5`, `3.14`, `-2` | one numeric type — no separate integer/float |
| String | `'...'` | `''` for an embedded quote; `**` inside is a glob wildcard |
| Array | `[ ]` | ordered, indexed from 0 |
| Map | `{ }` | keyed by any value |
| Sequence | `( )` | ordered value list (also grouping/precedence) |
| Vector | `< >` | fixed arity (2–4), numeric or range components |
| Range | `..` | `2..5`, `..50`, `5..` — selection shape |
| Namespaced ID | `mod:type:name` | runtime-resolved name (`shadebreaker:phys:stone`) |
| Function | `Name:: body ::` | a stored, callable body; referenced by name, passable to `cast` |
| Empty / null | `_` | the absent value; placeable wherever a value goes (e.g. `<_, ~5, ^2>`) |

Components and elements compose freely: a vector's components may carry prefixes (`<_, ~5, ^2>`), an array may hold any kind, a map value may be any kind.

### Scopes

Scopes are the addressable things in the world — entities, players, self, world, time, the persistent registry. They are not constructed like values; they are addressed with `@` and a scope letter, then narrowed and acted on. The scope on the left of a command determines how its operands are interpreted: the same vector is a position under `@s` and a moment under `@t`. See the Scopes section.

### Core rules

- **One numeric type.** All numbers are the same kind; no int/float distinction.
- **Kinds are fixed and explicit.** Each value's kind comes from its constructor. Operations are defined per kind; an undefined combination (adding an array to a vector) errors.
- **Scope interprets components, not kind.** A vector handed to a different scope is reinterpreted, never converted — its kind stays "vector."
- **Strict access by default**, with `_` as the explicit tolerance marker. See Evaluation Model.
- **Runtime is compile time** — names, kinds, and scopes resolve at evaluation; there is no separate static phase. Typos and kind mismatches surface as runtime errors, which is why access is strict by default.

---

## Quick Reference

| Symbol | Meaning | Group |
|---|---|---|
| `$` | variable (create + access) | sigil |
| `@` | scope operator | sigil |
| `#` | length / count / magnitude | sigil |
| `_` | null/empty value; null-fallback marker | sigil |
| `:` | namespace separator; key-value separator in maps | sigil |
| `.` | member access | sigil |
| `+` | add (binary) | arithmetic |
| `-` | subtract (binary); negation (unary) | arithmetic |
| `*` | multiply / scale | arithmetic |
| `/` | divide | arithmetic |
| `%` | modulo | arithmetic |
| `++` | postfix increment (`$x++`) | arithmetic |
| `--` | postfix decrement (`$x--`) | arithmetic |
| `+=` `-=` `*=` `/=` `%=` | compound assign | arithmetic |
| `?` | comparison | comparison & logic |
| `!` | negation | comparison & logic |
| `!?` | inequality | comparison & logic |
| `&&` | logical and | comparison & logic |
| `\|\|` | logical or | comparison & logic |
| `!&` | nand | comparison & logic |
| `!\|` | nor | comparison & logic |
| `=` | bind (eager) | binding |
| `=>` | live/lazy binding | binding |
| `=&` | bidirectional alias (two slots, one storage) | binding |
| `\|>` | directed write | binding |
| `^` | local / relative-to-facing | vector prefix |
| `~` | relative to current | vector prefix |
| `°` | rotation (vector or scalar) | vector prefix |
| `()` | grouping / precedence; scope selection slice | bracket |
| `[]` | array literal, index, positional call, filter | bracket |
| `{}` | map (any-value key), named call | bracket |
| `<>` | vectors (numeric/range components) | bracket |
| `..` | range constructor | chain |
| `->` | vector placement ("go to") | chain |
| `~>` | normalized directed step | chain |
| `?>` | conditional if-then | chain |
| `??` | conditional else (paired with `?>`) | chain |
| `\|` | pipe (into command) | flow |
| `;` | command separator | flow |
| `//` | line comment | flow |
| `'...'` | string literal (`''` for embedded quote; `**` glob) | string |
| `Name:: body ::` | declare function/macro | function |
| `Name[a, b]` | positional call | function |
| `Name{k: v}` | named call | function |

### Scopes
The base scopes (`@e`, `@p`, `@s`, `@n`, `@r`, `@w`, `@t`, `@v`) and their narrowing grammar are documented in the Scopes section. Canonical chain order: `@scope(selection)<region>[condition]`.

### Keywords

| Keyword | Meaning |
|---|---|
| `in` | collection iteration (`$name in $coll[ body ]`) or membership test (`in $coll ? value`) |
| `out` | exit the enclosing layer (loop or function) with an optional value |
| `collect` | append a value to the loop's implicit `collected` array |
| `iter` | current loop iteration count (zero-based, read-only) |
| `as` | execution context: change who is acting |
| `at` | execution context: change where the command is positioned |
| `over` | (in `cast`) spread firings over N frames |

### Built-in commands

| Command | Meaning |
|---|---|
| `log` | write to developer console |
| `clear` | clear developer console |
| `rng` | random number |
| `def` | binding-existence check |
| `cast` | run a command whenever a scope-state is reached |
| `casts` `uncast` | list active casts / remove one by id |
| `tag` `untag` | add / remove an entity tag |
| `say` | player-facing output (host-bound) |
| `msg` | spatial world-position message (host-bound) |
| `spawn` | create entities from a kind descriptor (host-bound) |
| `save` `load` `qsave` `qload` `saves` `unsave` | persistence (host-bound) |
| `read` `invoke` `files` `write` | file I/O (host-bound) |

### Deliberately excluded
- Bitwise operators (`&` `|` `^` `~` as bit ops, `<<` `>>`): Cast has no use for them; the symbols are spent on other meanings.

---

## Evaluation Model

### Access

**Strict by default:** direct access (`.field`, `[i]`, `{k}`, `$name`) errors visibly if the target doesn't exist. The error states what was missing and where.

**Filter context relaxes:** inside `[condition]` on a scope, missing properties are soft no-matches (the entity simply doesn't match the filter); no error.

**Explicit tolerance via `_`:** `_fallback` operand on `?`, `=`, `=>`, `|>`, direct access provides a value to use instead of erroring.

### Truthiness

For operators that consume booleans (`&&`, `||`, `!`, `!&`, `!|`, `?>`, `!?`) and contexts that test for truth (loop conditions, filters), the following values are *falsey* — they're treated as `false`:

- `_` (null)
- `0` (zero)
- `''` (empty string)
- `[]` (empty array)
- `{}` (empty map)
- Any vector where all components are zero (`<0, 0, 0>`)

Every other value is *truthy*. Boolean operators short-circuit on truthiness as documented in their sections.

The rule is "absence and emptiness are false; presence is true." Numerically zero values count as absence.

### Errors

**Errors fail the statement.** When a runtime error occurs (strict-access failure, type mismatch, division by zero, malformed call, etc.), the current statement aborts. The error message is sent to the developer console (`log`-style output). Execution continues with the *next* statement.

What counts as "the next statement":
- Top-level commands separated by `;` or newline — the next one runs.
- Function-body statements — the next statement in the body runs. The function isn't aborted; only the failing statement.
- Loop body statements — the next statement in the body runs for the current iteration. If the failing statement is the only one, the iteration effectively no-ops. The loop continues with the next element.

There's no try/catch construct in Cast. Errors are surfaced via the console and don't propagate as catchable values. This is deliberate: a live command language where errors are noisy and recoverable suits the use case better than structured exception handling.

**For tolerable errors that shouldn't surface as console output**, use the `_fallback` operand at access sites: `$x = $maybe _0` doesn't error if `$maybe` is unbound; it falls back to 0 silently. The fallback mechanism is the language's "I expect this might fail, handle it gracefully" pattern; the error model is for unexpected failures.

**Errors in a fired `cast`** surface to the console at the point of execution, not where `cast` was invoked. The original `cast` statement completed; the command ran later when its scope-state was reached. Errors include enough context (which cast, when fired) to trace back.

### Name resolution

Every name in the language belongs to a *namespace*, and the sigil at the use site identifies which. This makes resolution unambiguous at parse time without a separate type-check phase.

**Sigil-qualified names:**
- `$name` — local variable in the current variable environment
- `@scope.field` — property of a scope target (`@s.health`, `@np.position`, `@w.difficulty`)
- `arg[n]` — positional argument inside a function body
- `param{'k'}` — named argument inside a function body

**Bare identifiers** are reserved for function/command names at the start of an invocation. Inside expressions or command arguments, bare identifiers are *not valid* — qualify with the appropriate sigil.

**Inside filter brackets `[condition]`:** all sigil-qualified forms work as elsewhere, plus bare names get a shortcut.
- Bare names refer to the *immediate (innermost) filter's scope target* properties. `@e[health ? ..50]` reads `health` as each entity's health.
- `$var` resolves in the outer variable environment (lets you parameterize filters).
- `@scope` references work normally and *open new sub-scopes*, enabling chained/nested queries within a filter: `@e[@c[tag ? "ally"].count ? ..3]` filters entities by a property of a sub-query.
- For nested filters, bare names belong to the most-immediate enclosing filter. To reach outer scopes from a nested filter, use explicit `@scope.field` references.

**Two-stage active scope.** A filter *selects* what the active scope becomes for the command that follows — but during the filter's own evaluation, the active scope is still the *caller's*. So `@s` inside a filter refers to the calling scope (the body or scope that wrote the filter), while bare names refer to the entity being tested. Only after the filter resolves does `@s` rebind to each matched target for the subsequent command.

For example, in `@e[#(position - @s.position) ? ..8] Heal[15]` written inside a function whose `@s` is a shrine:
- During filter evaluation: `position` is each tested entity's position; `@s.position` is the shrine's position (caller's scope unchanged).
- After the filter: `Heal[15]` runs once per matched entity, with `@s` rebound to each one.

Two different `@s` referents on one line, at two evaluation stages, both correct.

**`@s` is "the active scope's target"** — the entity (or scope target) the current command/body is operating on. Two equivalent readings of the same referent:
- From the entity's perspective: "self" — me.
- From a function body's perspective: "the calling scope's target" — whoever called this and what they were scoped to.

Both framings describe the same runtime value. `@s` always means "whatever target the active scope holds right now," resolved dynamically at execution. When a player types `@s heal_command`, `@s` is the player. If `heal_command` is a user function and inside its body it uses `@s.health`, that's still the player — the scope wasn't lost on entry; `@s` keeps pointing at it because that's what `@s` is.

`@np MyFunc[]` makes `@s` inside `MyFunc` refer to the nearest player, because the call established `@np`'s target as the active scope. Same rule.

This matches the stored-tokens execution model: names re-resolve against the current environment at execution time, so there's no "captured scope at definition" question — the body runs in whatever scope is active when it runs.

**Local variables and scope properties never collide** by name because their sigils make them different namespaces. `$health` (local) and `@s.health` (scope property) are unambiguously different references; no shadowing rule needed.

### Variable lifetimes

Three lifetimes for named values, distinguished by syntax at the use site:

**Function-local `$name`** — bound inside a function body. Lives in the call frame, cleaned up when the call returns. `$amount` inside `Heal::...::` is function-local. Distinct local environments for distinct calls; nested calls don't see each other's locals.

**Session-local `$name`** — bound at the prompt (or at top-level execution). Lives in the session's top-level frame, cleaned up when the session ends. The same `$` sigil; the difference from function-local is lexical (where the binding happens), not syntactic. Session-locals are *not* saved — they belong to the session, not across sessions.

**Persistent `@v:name`** — bound through the `@v` registry scope. Lives across sessions when save/load are used. Inspectable, manageable, namespaced. The persistent counterpart to scoreboards in Minecraft, but cleanly integrated into the language's scope and selector mechanics. A cast that needs to outlive a reload references `@v` (not `$`), since only persistent state crosses the save boundary.

### Precedence and associativity

Operators are listed tightest-binding first. Within a level, associativity is the tiebreaker for adjacent operators of equal precedence: *left* groups left-to-right, *right* groups right-to-left, *non-assoc* means the operators don't chain at that level and must be parenthesized.

| Level | Operators | Assoc |
|---|---|---|
| 1 | `.` member access; postfix `[]` `{}` `()` (index / call / slice) | left |
| 2 | prefixes `#` `!` `~` `^` `°`, unary `-` | right |
| 3 | `=&` bidirectional alias | right |
| 4 | `*` `/` `%` | left |
| 5 | `+` `-` (binary) | left |
| 6 | `..` range | non-assoc |
| 7 | `?` `!?` comparison | non-assoc |
| 8 | `&&` `!&` | left |
| 9 | `\|\|` `!\|` | left |
| 10 | `?>` `??` conditional | right |
| 11 | `->` `~>` placement | non-assoc |
| 12 | `=` `=>` `\|>` binding / directed write | right |
| 13 | `\|` pipe | left |

Worked readings:
- `$x + 1 ? 5 && $y` → `(($x + 1) ? 5) && $y` — arithmetic, then comparison, then logic.
- `#$a + $b` → `(#$a) + $b` — the prefix binds before the addition.
- `$x ? 1..10` → `$x ? (1..10)` — the range is built before the comparison reads it.
- `$a =& $b + 1` → the `=&` alias is established (level 3) before the `+` runs, so the aliased storage is wired up before any arithmetic touches it.
- `$result = $a ? 5 ?> $a ?? 0` → binding (level 12) is loosest, so the whole conditional evaluates and its result is bound.
- `$a =& $b =& $c` → right-associative: `$a =& ($b =& $c)`; all three share one storage (the relation is symmetric, so the grouping direction doesn't change the result).

Where two operators' intended grouping differs from this table, parenthesize with `()`.

Two constructs are parsed by their own productions, outside this table:
- **`~>`'s trailing `* magnitude`** — the `* magnitude` after a `~>` step is part of the step production and binds to the whole step, not the target. `@s ~> $enemy * 5` is "(step toward `$enemy`) at magnitude 5," not "step toward (`$enemy * 5`)."
- **Range-complement `!`** — `!` before a range complements the whole range. `!2..5` is `!(2..5)` (outside the range), not `(!2)..5`.

### Known underspecified

These points are deliberately left to the interpreter or host, not oversights. They're collected here so implementers know where the latitude is:

- **`|>` passthrough** — whether a value piped through `|>` continues flowing to the next `|` stage or is consumed (next stage reads the variable) is interpreter-defined.
- **Pipe primary-input slot** — which argument position a piped value lands in (default arg slot, typically `arg[0]`) is interpreter-defined.
- **`~>` with no magnitude, or toward a coincident target** — direction with no scale, or a zero-length direction (mover already at target), is interpreter choice (no-op or error).
- **`()` on unordered scopes** — slicing an unordered scope (`@e`, `@p`) has no defined ordering; error or "any subset" is a runtime choice.
- **`Round` half-rounding** — the reference implementation rounds half to even (banker's rounding): `Round[2.5]` is 2, `Round[3.5]` is 4. A host overriding the math intrinsics may choose half-up instead.
- **Mismatched-arity vector arithmetic, vector-of-arrays nesting, multi-character range bounds** (`'foo'..'bar'`) — undefined; likely errors.

---

## Scopes

A scope is opened with `@` and a scope letter, optionally narrowed, then a command operates on whatever remains in scope. Canonical chain order: `@scope(selection)<region>[condition] command`. See the `@` operator in Detailed Definitions for the grammar of selection, region, and filter narrowing; this section documents the base scopes themselves.

### The base scope set

The language assumes any host provides these scopes in some form:

- **Sets (unordered):** `@e` entities, `@p` players. Host may register more (e.g. `@c` creatures, `@o` objects).
- **Sets (ordered):** `@n` entities by nearness, `@r` entities in random order. Both compose with kind letters from the registered set (`@np`, `@rp` always available; `@nc`, `@no` etc. exist only if the host registers `c`, `o`, etc.). Slice with `()`: `@np(0..2)` for the three nearest players, `@np(0)` for just the nearest, `@np(3..7)` to skip the first three, `@np(0..2, 5, 10..)` for multiple slices unioned.
- **Singles:** `@s` self / active scope's target.
- **Globals:** `@w` world, `@t` time, `@v` persistent variables registry.

`@s`, `@t`, `@w` are singletons and don't accept `()`. Additional scope letters are host extensions registered through the binding interface.

### `@v` — persistent variables registry

The `@v` scope and the registry that backs it are *language-owned* — the host doesn't provide anything for `@v` to work. Reads, writes, and queries all happen in the language's own memory. Persistence (surviving across sessions) is a separate concern handled by `save`/`load`, which serialize the registry to a buffer the host stores externally.

**`@v` semantics:**

- **Flat dictionary keyed by namespace-structured names.** `@v:score:player1`, `@v:boss:phase`, `@v:spawn:last_used` are three independent slots. The `:` segments are *part of the key*, not nested access. `@v:score` and `@v:score:player1` are unrelated slots; one doesn't inherit or contain the other.
- **No schema, no declaration.** Any key path you write to becomes a slot. The runtime doesn't pre-define structure; convention is up to users.
- **Strict access on read.** `@v:missing:thing` errors visibly if unwritten, unless an `_fallback` is supplied. Reading is exact-key.
- **Writes are tolerant.** Any key path is fine to write to; it just creates the slot. Typos create silent extra slots — the cost of no-schema dictionaries.
- **Values are any language value.** Numbers, strings, vectors, arrays, maps, ids, even other scopes-as-references — all fine. Value-internal access via `.`: `@v:player1.health` reads the `health` property of whatever's stored at slot `player1` (if that value is a map or has properties).
- **Queryable like any scope:**
  - `@v` — the registry as a value (its full set of slots)
  - `@v[name ? 'score:**']` — slots whose key starts with `score:` (glob match on key strings)
  - `#@v` — number of slots
  - `@v[name ? 'boss:**'] | unset` — example of composing a filter with a command to clean up all `boss:*` slots
- **The `:` separator is part of the registry-key structure, not access into nested data.** It parallels namespaced ids (`mod:type:name`), which are also structured names without inheritance.
- **`.` after `@v:name`** accesses *into the stored value*, not further into the registry. `@v:player1.health` is "the `health` property of the value at slot `player1`," not "the slot at `player1.health`."

**Example uses:**

```
@v:score:player1 = 100                  // write a score
@v:score:player1 ++                     // increment it
@v:spawn:default = <0, 10, 0>           // store a spawn vector
@s -> @v:spawn:default                  // teleport self there (bare-scope sugar)

// Inspect the registry
@v[name ? 'score:**']                   // show all score slots
#@v                                     // total slot count
```

`@v` is base language and entirely language-owned. The registry lives in the language's runtime; the host doesn't need to know about it for normal reads/writes. The host only gets involved when `save`/`load` cross session boundaries — at that point, the host stores and retrieves the serialized buffer.

### `@t` — time, timers, and counters

**Global timers and counters on `@t`.** The `@t` scope is also a *timer/counter registry*: any index or named key on `@t` is a slot that automatically ticks. Numeric indices use array syntax (`@t[N]`), named slots use map syntax (`@t{'name'}`).

Slots store a number. The runtime increments every slot once per (scaled) second. The user writes the value they want stored; the sign of that value determines whether the slot behaves as a timer or a counter:

- **Negative value = timer.** `@t[0] = -10` stores `-10`. The runtime ticks it toward 0; when the runtime's tick crosses from negative to 0, the slot nulls itself.
- **Zero or positive value = counter.** `@t{'kills'} = 0` stores 0. Ticks up indefinitely. `@t[12] = 500` starts a counter already at 500 — ticks up from there.

The semantics are honest: `@t[0] = -10` reads as "t-zero equals minus ten" — and a value at -10 ticking up toward 0 is exactly what countdown looks like.

```
@t[0] = -10                                 // timer: 10 sec to expiry
@t{'boss_cooldown'} = -30                   // named timer
@t{'kills'} = 0                             // counter from zero
@t[12] = 500                                // counter already at 500
```

**Reading.** `@t[N]` returns the current value, or `_` if unset/expired. The sign tells the type: negative = active timer, zero-or-positive = counter, `_` = unset/expired.

```
@t[0] ? _                                   // true if expired/unset
@t[0] ? ..0                                 // true if it's a timer (negative)
@t[0] ? 0..                                 // true if it's a counter
#@t[0]                                      // absolute value: remaining (timer) or elapsed (counter)
```

**Null condition.** A slot nulls itself when its value transitions from negative to 0-or-above, regardless of cause — ticking, arithmetic, or direct assignment. `@t[0] = -1` then `@t[0] += 10` → the slot crosses from negative to positive, so it nulls (ends up `_`, not 9). This makes "timer expired" reliable regardless of how the decrement happened.

**Arithmetic.** `+=` advances progress (timer closer to expiry, counter further along); `-=` reverses.

```
@t[0] += 5                                  // timer at -10 → -5; counter at 7 → 12
@t[0] -= 5                                  // timer at -10 → -15; counter at 7 → 2
@t[0] = -10                                 // explicitly set as a timer
@t[0] = 0                                   // explicitly set as a counter at 0
```

**Control properties** apply both per-slot and globally on `@t` itself:

- `@t[N].pause` (or `@t{'name'}.pause`) — halt ticking for that slot; the slot keeps its current value
- `@t[N].resume` — restart ticking from the current value
- `@t[N].speed = N` — ticks per second for that slot (default 1.0 = real-time; 2.0 is double-speed; 0.5 is half-speed; 0 is equivalent to `.pause`)
- `@t.pause` / `@t.resume` / `@t.speed = N` — global versions, affecting all slots at once. Useful for pausing the entire game-time domain (cutscenes, menus) or running everything in slow-motion / fast-forward.

A scheduled `cast @t{'name'} ? _ Func` waiting on a paused timer doesn't change behavior — the cast is still scheduled, but the condition (`@t{'name'} ? _`) won't become true while the timer is paused (the timer can't expire if it isn't ticking). Casts effectively pause along with their condition timers, without any extra mechanism.

All timer/counter state — values, paused states, speed settings, global pause/speed — saves and loads with the rest of the language state.

```
cast @t{'boss_cooldown'} ? _ SpawnBoss      // schedule SpawnBoss when timer expires
@t{'speedrun'} = 0                          // start a counter
@t{'speedrun'}.speed = 2                    // run it at double-speed
@t{'speedrun'}.pause                        // pause it
@t{'speedrun'}.resume                       // resume
@t{'speedrun'} = _                          // stop and clear

@t.pause                                    // pause all timers (e.g. for a menu)
@t.speed = 0.5                              // run all timers at half speed (slow-mo)
@t.resume                                   // resume normal time
```

### Execution context — `as` and `at`

A command line establishes an execution context: *who* is acting (the active scope's target — `@s`) and *where* the command is positioned (the active scope's position — what relative vectors like `<~>` resolve against). By default, the leading scope sets both. `as` and `at` keywords let you separate them.

- **`as @scope`** — change who is acting. `@s` inside subsequent execution becomes the target of `@scope`. The position is unchanged.
- **`at @scope`** — change where the command is positioned. Relative coordinates resolve against `@scope`'s position. The actor is unchanged.

Both keywords go between the leading scope and the command, in canonical order **`as` before `at`** (who first, then where).

**Examples:**

```
@s as @np command                       // run command as nearest player, at self's position
@s at @np command                       // run command as self, but positioned at nearest player
@s as @np at @w<0,0,0> command          // as nearest player, at world origin
@e[in tags ? 'enemy'] as @s command     // each enemy: command runs as self, with the enemy's position context retained as the loop iteration
@e[in tags ? 'enemy'] at @np :: @s.position ~> @np.position * 0.1 ::   // each enemy steps toward nearest player
```

`as` and `at` chain left-to-right; each replaces the corresponding piece of context. The command at the end runs once per branch — if `as` selects multiple targets, the command iterates per target with `@s` rebinding for each.

---
## Iteration over collections

Scopes iterate automatically (`@e damage 5` runs `damage 5` per entity). When a scope-iterating command is used as an expression, it yields the *scope* it acted on (the matched set), not the per-entity command results — so `$hit = @e[health ? ..50] Hurt[10]` binds `$hit` to the set that was hurt, ready to act on again (`$hit Heal[5]`). To collect per-entity *values* instead, use an `in`-loop with `collect`. Iteration over *non-scope collections* uses the `in` keyword to bind each element to a variable. The collection can be any value — variable, literal, or inline group — and the body bracket matches the collection's natural type.

**`in` has two forms** distinguished by token order:

- **Iteration** (starts with bound name): `$name in $collection<body-bracket> body </body-bracket>` — bind each element to `$name`, run body per element. The collection type bracket determines the body bracket.
- **Membership** (starts with `in`): `in $collection ? value` — boolean expression, true if `value` is contained in `$collection`. For arrays: is the value an element. For maps: is it a key. For vectors: is it a component value. For sequences: same as array.

Examples of membership:

```
in $array ? 2                                   // is 2 in $array?
in $map ? 'name'                                // does $map have key 'name'?
in [1, 2, 3] ? $x                               // is $x in this literal array?
in $array ? 2 && in $other ? 5                  // composes with &&
```

Token order disambiguates: iteration leads with `$name`, membership leads with `in`. No grammar collision.

Iteration syntax (body-present) below:

**Syntax:** `$name in $collection<body-bracket> body </body-bracket>`

The body bracket matches the collection's type bracket:

- **Array** (`[]`): `$x in $array[ body ]` — `$x` binds each element value
- **Map** (`{}`): `$x in $map{ body }` — `$x` binds each entry's value; bare `key` available inside the body
- **Vector** (`<>`): `$x in $vector< body >` — `$x` binds each component value
- **Inline sequence** (`()`): `$x in (a, b, c..d)[ body ]` — `$x` walks each value (ranges expand to their members)

The collection can be a variable (`$array`), a literal (`[1, 2, 3]`), an inline group (`(1, 3..8, 12)`), or any expression that yields a collection. The bracket type after `in` declares what kind of iteration is happening, mirroring how that collection type is accessed elsewhere in the language.

**Examples:**

```
$x in $array[ log $x ]                          // array variable
$x in [1, 2, 3][ log $x ]                       // array literal
$x in $array(0..2)[ log $x ]                    // sliced array (selection on array)
$x in $array[ $x ? 3.. ?> log $x ]              // filter via conditional
$x in $array[ collect [iter, $x] ]              // numbered list via iter + collect

$v in $map{ log $v }                            // map values
$v in $map{ key ? 'score:**' ?> log $v }        // values whose key matches glob

$c in $position< log $c >                       // vector components

$x in (1, 3..8, 12)[ $x ? ..7 ?> out 'small' ]  // inline sequence with ranges
```

**Inside loop bodies, three implicit names are available:**
- `iter` — current iteration count, zero-based, increments on every body execution (independent of conditional filtering inside the body). Local to each loop; nested loops have their own `iter`.
- `key` — for map iteration only, the current entry's key.
- `collected` — array of values appended via `collect`. Auto-created when `collect` is used; local to each loop.

To access an outer loop's `iter` (or `key`, or `collected`) from an inner loop, bind it to a variable before entering the inner loop:

```
$x in $array[
    $outer = iter ;
    $y in $array2[ log [$outer, iter, $x, $y] ]
]
```

The variable is captured in the outer scope and visible to the inner loop body via normal variable lookup. No special outer-iter syntax — same mechanism as any captured variable.

**Inline sequences via `()`.** `()` groups a comma-separated list of values (scalars, ranges, expressions) into an iterable sequence. Ranges expand to their members; scalars are themselves. The result is sequence-shaped (ordered values), so the body bracket is `[]` to match.

**Write semantics.** `$name` is a *snapshot* of the element value, not a reference. Writes to `$name` don't propagate to the collection. To modify a collection element during iteration, index directly (`$array[$i] = ...`) — typically using a parallel index iteration — or use `=&` to alias explicitly (`$x =& $array[$i]`).

**Map iteration order** is insertion order. `(selection)` doesn't apply to map iteration (maps have no ordered positional index); filter by key inside the body using `?>`.

**Loops are expressions.** A loop yields a value, determined by what happened inside the body:

- If `out $value` fires: loop exits immediately with `$value`. Any prior `collect` calls are discarded.
- If `out collected` fires (or `out` doesn't fire and `collect` was used): loop yields the `collected` array.
- If `out` (bare) fires: loop yields null, discards `collected`.
- If no `out` and no `collect`: loop runs to completion, yields null.

`out` exits the *immediately enclosing layer* — inside a loop body, that's the loop itself, not the surrounding function.

**Building collections with `collect`.** Use `collect $value` inside a loop body to append `$value` to an implicit `collected` array. Common patterns:

```
// Build a list of all values
$scores = $x in $array[ collect $x.score ]

// Filtered map (values matching a condition)
$high = $x in $array[ $x.score ? 50.. ?> collect $x.score ]

// Map values from a map into an array
$names = $v in $map{ collect $v.name }

// Build until a sentinel, then return what's collected
$x in $array[
    $x ? 'stop' ?> out collected ;
    collect $x
]
```

`collect` is meaningful only inside a loop body. Outside one, it errors. The `collected` array is local to each loop; nested loops have independent `collected` arrays.

**Find-first idiom (still works as before — `out` before any `collect`):**

```
$found = $x in $array[ $x ? $target ?> out $x ]
```

The first match triggers `out $x`, yielding `$x` from the loop. The `collected` array (empty in this case) is discarded.

**Early exit inside a function** uses two `out`s — one to exit the loop, one to exit the function:

```
FindMatch::
    $target = arg[0] =& param{'target'} ;
    $hit = $x in $array[ $x ? $target ?> out $x ] ;
    out $hit
::
```

The inner `out $x` exits the loop (binding `$hit` to the matching element or null). The outer `out $hit` exits the function with that value.

---

## Host Integration

### Strictly defined by the base language

- The grammar (`@scope(selection)<region>[condition] command args`).
- All operators, brackets, sigils, ranges, vectors, rotations, conditionals, and chains.
- The base scope set (`@e`, `@p`, `@s`, `@w`, `@t`, `@n`, `@r`, `@v`) — documented in the Scopes section.
- Name resolution rules and the strict-access-by-default policy with `_` fallback.
- Function/macro definition and call syntax (`Name:: body ::`, `Name[args]`, `Name{args}`).
- The `=` family (eager `=`, lazy `=>`, bidirectional `=&`, unbind `= _`).
- Stored-tokens execution model.
- String literals, glob matching, arithmetic operators, increment/decrement, compound assignment.

### Defined by the host

- **Additional scope letters.** `@c` creatures and `@o` objects are common *examples* for games that distinguish those entity kinds, but they're not base — a different game might register `@x` for enemies, `@i` for items, whatever fits its model.
- **Properties on entities and other scope targets.** What `@s.health` reads, what `@s.position` reads, what `@w.difficulty` exposes — all host-defined. The language commits to dot-access syntax but not to specific property names.
- **Vector interpretation per scope.** `<day, minute, hour>` for `@t` is a host convention; a host with a different time model might use `<seconds>` (1-component) or `<year, day, second>` (3-component but different ordering).
- **Commands.** `heal`, `damage`, `set`, `paint` — all host-registered. The language provides the call-and-dispatch mechanism; what commands exist and what they do is the host's job.
- **Namespaced ID resolution.** `shadebreaker:phys:stone` is a literal in the language; what it resolves to (a material byte, a ScriptableObject, etc.) is host code.
- **What "world" and "time" actually are at runtime.** The language assumes `@w` and `@t` exist; the host provides the singletons.

### The binding interface (what a host implements)

A host integrates by implementing a small interface:

- **Scope handlers** — one function per registered scope letter, returning the set of targets matching that scope (with narrowing arguments applied).
- **Target adapters** — for each kind of target a scope returns, a uniform `TryGetProperty` / `TrySetProperty` interface so dot-access and assignment work.
- **Command handlers** — registered by name, taking the active scope's targets and args.
- **ID resolver** — for namespaced IDs, mapping `namespace:type:name` to a runtime value.
- **Vector interpreters** — optional, for scopes with non-spatial vector semantics (like `@t` using `<day, minute, hour>`).
- **Persistence provider** — for `save`/`load`/`saves`/`unsave` to work. Four operations: `Write(name, buffer)`, `Read(name) → buffer`, `List() → string[]`, `Delete(name)`. Optional — if not provided, save/load commands error visibly.
- **Directory registration** — for `read`/`invoke`/`files`/`write` to work. The host registers named directories (e.g. `RegisterDirectory("scripts", "Assets/CastScripts/", readonly: false)`); Cast handles file I/O within those directories internally. Optional — if no directory is registered, file commands error visibly.
- **Entity spawner** — for `spawn` to work. The host implements entity creation from a kind id, count, location, and initial properties. Optional — if no spawner is registered, `spawn` errors visibly.
- **Say channel** — where `say` output is routed. Falls back to `log` if not provided.
- **Msg channel** — where `msg` output (text + world position) is routed. Falls back to `log` with location appended if not provided.

A game integrating the language registers these once at startup. The language never references game-specific content; the host bridges from the language's abstract surface to its concrete runtime.

### Portability

A host that only implements the base scopes and minimum bindings gets a usable Cast runtime with `@e`, `@p`, `@s`, `@w`, `@t`, `@n`, `@r`, `@v`. A host with richer needs registers additional scope letters, commands, and properties. A *2D platformer* and a *voxel sandbox* can both use Cast with entirely different scope letters, commands, and properties registered — the language doesn't care which game it's running in, as long as the binding interface is fulfilled.

---
## Reserved names

The following identifiers are reserved by the language. Users cannot redefine them as variables or functions:

**Keywords:**
- `in` — collection iteration (`$name in $collection[ body ]`) or membership test (`in $collection ? value`, returns boolean)
- `out` — exit the immediately enclosing layer (loop or function) with an optional value
- `collect` — append a value to the loop's implicit `collected` array
- `iter` — the current loop's iteration count (zero-based, read-only)
- `as` — execution context: change who is acting, without changing where
- `at` — execution context: change where the command is positioned, without changing who is acting
- `over` — reserved keyword; used in `cast` to spread the cast's firings over N frames (`cast 3 over 45 ...`)

**Implicit variables (inside function bodies):**
- `arg` — positional arguments array
- `param` — named arguments map

**Implicit variables (inside loop bodies):**
- `collected` — array of values appended via `collect`. Auto-created when `collect` is used. Local to each loop; nested loops have independent `collected` arrays.

**Built-in commands:**
- `log` — write to developer console
- `clear` — clear developer console
- `rng` — random number generator
- `def` — binding existence check
- `cast` — run a command in a scope-context whenever its state is reached (standing subscription)
- `casts` — list active casts
- `uncast` — remove an active cast by id
- `tag`, `untag` — entity tagging
- `say` — player-facing output
- `msg` — spatial world-position message
- `spawn` — create entities from a kind descriptor
- `save`, `load`, `qsave`, `qload`, `saves`, `unsave` — persistence family
- `read`, `invoke`, `files`, `write` — file I/O family

**Standard library functions** (names the standard library binds, host-overridable but not user-redefinable inside Cast):
- Entity-acting: `Heal`, `Hurt`, `SetHealth`, `Kill`
- Math: `Floor`, `Ceil`, `Round`, `Abs`, `Min`, `Max`, `Clamp`, `Sqrt`, `Pow`, `Sin`, `Cos`, `Atan2`

**Scope letters** (the letters themselves are reserved when used after `@`):
- `e`, `p`, `s`, `n`, `r`, `t`, `w`, `v` (base) — plus any letters the host registers (`c`, `o`, etc.)

Attempting to bind any reserved name (`log = 5`, `cast:: ... ::`, `$arg = ...` outside a function body context, etc.) is a runtime error. Standard library functions can be *overridden by the host* via the binding interface but not redefined from within Cast scripts.

---

## Syntax highlighting

Canonical color scheme for the Cast console and any editor integration. Highlighting is presentational, not semantic — these are conventions for shared visual language across tools.

**Type-driven (by value kind):**
- Numbers — red
- Strings — dark orange
- Vectors — yellow
- Namespaced IDs — light blue
- Variables (`$name`) — colored by the type of the value they currently hold (red if a number, yellow if a vector, etc.). Updates live as bindings change.

**Structural:**
- Scope references (`@scope`) — bold, white
- Function names (definitions and calls) — bold, orange
- Keywords (`in`, `out`, `collect`, `iter`) — bold, blue
- Comments (`//...`) — italic, green
- Operators (`+`, `*`, `?`, `->`, etc.) — muted (grey or default text color), low visual weight so they recede

**Brackets** — depth-cycled across all four types (`()`, `[]`, `{}`, `<>`). The depth counter is shared across bracket types, so `({[<>]})` cycles through colors regardless of which bracket type opens or closes. The cycle: purple → pink → orange → green → blue → cyan → repeat.

Live console implementations should update variable colors as their bound values change. Static editor integrations may infer from binding-site type or fall back to a neutral color for variables when type isn't statically determinable.
## Detailed Definitions

### Sigils & access

#### `$` — variable
**Operands:** prefix on an identifier (`$name`).
**Behavior:** resolves the identifier in the current variable environment. On the right of `=` or `=>`, creates/rebinds; elsewhere, reads. `$` variables are *transient* — function-local inside a body, session-local at top-level, and not saved. For persistent values, see `@v`.
**Example:** `$health = 100`, then `$health` returns 100.
**Composes with:** `=`, `=>`, `|>` for assignment forms; `.`/`[]`/`{}` for accessing fields/elements of the resolved value.
**Edge cases:**
- Reading an unbound `$name` errors visibly unless an `_`-marked fallback is supplied.
- `$` is a reference sigil, never a binding marker — binding is always done with `=`/`=>`/`|>`.
- Inside strings, `$name` interpolates (the same deref).
- For values that need to survive across sessions, use `@v:name` (persistent registry) rather than `$name` (transient, not saved).

#### `@` — scope operator
**Operands:** `@scope(selection)<region>[condition]` then a command. All bracketed parts optional.
**Behavior:** opens a scope (entities, time, world, etc.); narrowing brackets restrict it; the command after operates within whatever remains in scope. `@n` and `@r` scopes return *ordered* sets (nearest-first and random respectively); `(selection)` picks indices from that ordering, where the selection is one or more comma-separated range expressions whose union is taken.
**Example:** `@np(0..2)<region>[health ? ..50] heal 50` — heal the three nearest low-health players in the region. `@np(0, 10..)` — nearest player plus everything from the eleventh on. (Examples using `@no`/`@nc` assume the host has registered `o`/`c` as scope letters.)
**Composes with:** `()` (index selection for ordered scopes; comma-separated list of ranges), `<>` (spatial narrowing), `[]` (predicate filter), `|` (the scope's result can feed into commands).
**Edge cases:**
- Canonical order is `@scope(selection)<region>[condition]` — shortest operand first, longest last (range list → vector → arbitrary predicate). This keeps the terse parameters quick to scan up front and detail at the end.
- `()` is meaningful only for ordered scopes (`@n`, `@r` and their kind-narrowed forms). On unordered scopes (`@e`, `@p`, or host-registered kinds), `()` has no defined ordering to slice from — either an error or interpreted as "any subset" depending on runtime choice.
- `()` accepts a comma-separated list of range expressions; the resulting selection is their union. Single scalar (`(0)`) is a degenerate one-element range. Single range (`(0..2)`) selects that slice. Multiple (`(0..2, 5, 10..)`) selects all listed indices.
- `()` for a scope is the *grouping* sense already given to parens — a parenthesized expression list — applied here as "evaluate these as the selection."
- `@s`, `@t`, `@w` don't accept `()` — they're singletons, not sets.
- The scope determines which commands are meaningful (`set daytime` only valid in `@t`). Which commands exist on which scopes is host-defined.
- `<>` is interpreted in the scope's units — spatial for entity/terrain scopes, host-defined components for `@t` (commonly `<day, minute, hour>`) and other custom scopes.
- The base language requires hosts to provide `@e`, `@p`, `@s`, `@w`, `@t`, `@n`, `@r`. Additional scope letters (like `@c`, `@o`) are host extensions registered through the binding interface.
- Filtering by `id` selects one specific thing rather than a category: `@e[id ? $target]` is exactly the entity whose identifier matches `$target`, anywhere it exists. A category filter (`@np`, `<region>`, `[health ? ..50]`) addresses things by their circumstances; an `id` filter addresses a thing by its identity. Acting precisely on a single thing therefore requires holding its identifier — without it, only category-addressing is possible.

#### `#` — length / count / magnitude
**Operands:** prefix on a value.
**Behavior:** returns the magnitude of the value — array length, map count, string length, vector magnitude (length), argument count (`#arg`), and the absolute value of a number. One meaning: "how much" — size for collections, length for vectors, absolute value for scalars.
**Example:** `#$inventory` = number of items; `#<3, 4, 0>` = 5 (vector magnitude); `#-7` = 7 (absolute value).
**Composes with:** any value with a defined magnitude — collections, strings, vectors, numbers.
**Edge cases:**
- On a number, `#` is absolute value: `#-7` is 7, `#5` is 5. (This is why `#@t[0]` gives a timer/counter slot's absolute value — the slot holds a number.)
- For vectors with range components, magnitude isn't well-defined; using `#` on a region is a type error.
- Maximal-munch matters when adjacent to other operators: `!#$x` is "not (magnitude of x)," not a `!#` chain.

#### `_` — null/empty / fallback marker
**Operands:** standalone (`_` = null value) or prefix on a value (`_x` = "x as the null-fallback for this operation").
**Behavior:** represents absence as a first-class value, and marks a fallback operand for operations that could fail on missing-target.
**Example:** `$x ? 5 _false` (comparison with fallback); `$x = $maybe _0` (binding with fallback); `$result = _` (explicitly nullify).
**Composes with:** any operation that can fail on missing access (`?`, `=`, `=>`, `|>`, direct access).
**Edge cases:**
- `_value` syntax requires no whitespace between `_` and the value, distinguishing it from `_` (alone) followed by a value.
- The fallback is supplied *positionally* — the operation knows which slot is the fallback by the `_` prefix, not by a separator.
- `_` alone is the null literal; `$x = _` deliberately nulls a variable.

#### `:` — namespace separator / key-value
**Operands:** binary `a:b` (or `a:b:c` for namespaces).
**Behavior:** in identifiers, separates namespace segments (`namespace:type:name`); in map literals, separates key from value (`{name: 'stone'}`).
**Example:** `@w set @w<position> shadebreaker:phys:stone` (the namespaced ID is host-defined — what `shadebreaker:phys:stone` resolves to is the host's job, the language just sees a structured identifier); `$config = {difficulty: 'hard', spawnrate: 0.5}`.
**Composes with:** identifiers (namespaces), map literals.
**Edge cases:**
- The two roles disambiguate by context: inside `{}` it's key:value; in a bare identifier it's namespace.
- Triple-colon namespaced IDs are by ACS convention (`namespace:type:name`), not enforced by syntax — `mod:thing` (two segments) is also valid; meaning is up to the resolver.
- `::` (two colons) is the function body delimiter — maximal munch grabs `::` before parsing single `:`.

#### `.` — member access
**Operands:** `value.fieldname` or `@scope.property`.
**Behavior:** accesses a *declared, fixed* member — a field of a value (map/struct) or a property of a scope target — with the name known at parse time. `@s.health`, `@np.position`, `@w.difficulty`, `@w.casts` are all member access on scope targets.
**Example:** `$config.difficulty` reads a map field; `@s.health` reads the self target's health property; `@w.casts` reads the world's active-casts array.
**Composes with:** chained access (`$a.b.c`), scope-target properties (`@s.position`), `_` fallback for missing member.
**Edge cases:**
- `.` requires the member name to be a literal identifier at parse time — `$x.$y` (dynamic name) is not valid; use `[]` or `{}` for runtime-keyed access.
- Accessing a member the value or scope doesn't expose errors visibly unless `_fallback` supplied. `@t.daytime` errors not because `.` can't apply to `@t`, but because the time scope exposes no `daytime` property — time is addressed by slots (`@t[N]`, `@t{'name'}`). The properties `@t` *does* expose work fine: `@t.pause`, `@t.speed`, `@t.resume`.
- `.` does not reach into a vector's axes — vectors have no named or indexed component access (no `$vec.x`); see `<>`.

### Arithmetic

#### Arithmetic operators
**Operators:** `+` (add), `-` (subtract / unary negate), `*` (multiply), `/` (divide), `%` (modulo).
**Behavior:** standard math on numbers. Per-type semantics:
- Numbers: standard arithmetic
- Vectors: component-wise for `+`/`-`; scalar multiplier for `*`
- Strings: `+` concatenates, `-` removes a substring (no-op if absent)
- `*` is also the scaling operator in `~>` motion and applies rotation when one operand is a `°`-prefixed value; context disambiguates.

**Example:** `$x + 5`, `$pos - <0, 1, 0>` (vector subtract), `$name + ' the Brave'` (string concat), `'hello world' - 'world'` (string removal), `$health * 2`, `$tick % 10` (every tenth tick).
**Composes with:** numbers, vectors, strings, and any type the runtime defines arithmetic for.
**Edge cases:**
- Precedence: `*`/`/`/`%` tighter than `+`/`-`. Use `()` to override.
- Unary `-` is negation: `-$x` is "negative x." Lexer disambiguates by position (after an operator or at expression start → unary; between values → binary).
- Vector arithmetic with mismatched arity is undefined.
- Division by zero, modulo by zero: runtime errors.
- String `-` is no-op if the substring isn't present; doesn't error.
- No bitwise operators by design — game commands don't need them.

#### `*` — multiply / scale
**Operands:** binary `*` between values.
**Behavior:** numeric multiplication on numbers; scalar scaling on vectors; applies a rotation to a vector when one operand is `°`-prefixed.
**Example:** `5 * 3` (multiply); `^$target * 5` (direction scaled by 5); `<10, 0, 0> * °<0, 90, 0>` (rotate the vector by 90 yaw).
**Composes with:** numbers, vectors, rotation-vectors.
**Edge cases:**
- `*` is purely arithmetic. It's *not* a wildcard — unconstrained vector components are expressed as ranges (`<.., .., ..>` or shorthand `<..>`).
- String-content wildcards are `**` inside string literals, not `*`.

#### Increment / decrement
**Operators:** `$x++` (postfix increment), `$x--` (postfix decrement).
**Behavior:** modify the variable in place and yield the new value. Equivalent to `$x = $x + 1` and `$x = $x - 1`.
**Example:** `$cooldown--` ticks down, `$score++` after a hit.
**Composes with:** any numeric variable.
**Edge cases:**
- Postfix only; no prefix form (`++$x` is not defined, would parse as `+(+$x)` if anything).
- Yields the *new* value, not the old (unlike C's distinction).
- On non-numeric variables: runtime error.
- `++` and `--` lex as single tokens — maximal munch over `+` `+` or `-` `-`.

#### Compound assignment
**Operators:** `+=`, `-=`, `*=`, `/=`, `%=`.
**Behavior:** `$x op= y` is `$x = $x op y`. Modifies the variable in place by the right operand.
**Example:** `$health += 10` (heal by 10), `$cooldown -= 1` (tick), `$multiplier *= 2`, `$index %= #$inventory` (wrap to inventory size).
**Composes with:** any binary arithmetic operator + variable.
**Edge cases:**
- Lex as single tokens — `+=` before `+` then `=`.
- The variable must already be bound; compound assigning to an unbound variable errors (same as direct access).
- Vector compound assignment is component-wise where the operator is component-wise (`$pos += <1, 0, 0>`).

### Comparison & logic

#### `?` — comparison
**Operands:** `left ? right`, where right is a value, range, pattern, or empty-bracket type witness. Optionally followed by `_fallback`.
**Behavior:** "match against the right operand's shape." Returns true/false. What kind of match depends on the right operand:
- Scalar (number, string without wildcards, etc.) → exact equality
- Numeric range (`2..5`) → numeric membership
- Character range (`'a'..'z'`) → character membership
- String with `**` wildcards → glob match
- Vector with range components → component-wise membership (region)
- Empty bracket `[]`, `{}`, `<>`, `()` → type check ("is left a value of that bracket's type")
- Populated array/map/vector literal → exact structural equality

**Example:** `$x ? 5` (equality); `$x ? 2..5` (numeric range); `$name ? 'Boss_**'` (starts with Boss_); `$pos ? <-5..5, 0..10, -5..5>` (in region); `$x ? []` (is $x an array?); `$x ? {}` (is $x a map?); `$x ? <>` (is $x a vector?); `$x ? [_]` (is $x equal to the empty array?); `$x ? 5 _false` (with null-fallback).
**Composes with:** `!` (forms `!?`), ranges (`..`), `_` (fallback for missing target).
**Edge cases:**
- If `left` doesn't exist, errors visibly unless `_fallback` supplied.
- Inside selector `[...]` filters: missing properties are soft no-matches, not errors (filter context relaxes the strict access rule).
- Comparison against a region (vector with ranges) is component-wise membership.
- String equality and glob matching distinguish by presence of `**` in the right operand — `'foo'` is exact, `'**foo**'` is glob.
- **Type witness vs. empty-value distinction.** Bare empty bracket (`[]`, `{}`, `<>`, `()`) is a *type witness* — the right operand asks "is left this kind of thing?" To check equality against an actual empty container, use the `_`-marked form: `[_]` is the literal empty array, `{_}` is the literal empty map, `<_>` is the literal empty vector, `(_)` is the literal empty sequence. So `$x ? []` is "is x an array?" and `$x ? [_]` is "is x equal to the empty array specifically?"
- Membership against a collection is *not* `?` — use `in` for that. `in $array ? 2` is "is 2 in $array"; `$x ? $array` would be array-vs-value equality (rare, usually not what you mean).

#### `!` — negation
**Operands:** prefix on a boolean/expression, or on a range (range-complement).
**Behavior:** logical negation of predicates and equality results. Applied to a *range*, `!` is range-complement: `!..5` is "above 5" (the complement of `..5`), `!2..5` is "outside 2 to 5." Range-complement is recognized at range-construction level, not as the general boolean prefix — `!2..5` is `!(2..5)`, the complement of the whole range, not `(!2)..5`.
**Example:** `[health !? 0]` = health not equal to 0; `[$pos !? <0..10, 0..10, 0..10>]` = position outside region; `$x ? !..5` = x is above 5.
**Composes with:** `?` (forms `!?`), ranges (range-complement `!..5`, `!2..5`), any boolean expression.
**Edge cases:**
- On a range, `!` complements the range and binds to the whole range (`!2..5` = outside 2–5), an exception to the general prefix-binds-tightest rule — range-complement is part of range construction.
- Position-sensitive: `!` prefix on a value vs. as part of `!?` chain — maximal-munch grabs `!?` when followed by a comparison context.
- Doesn't negate plain non-boolean values (negating a bare `5` is undefined; use arithmetic). Negating a *range* is defined (complement), as above.

#### `!?` — inequality
**Operands:** `left !? right`.
**Behavior:** the negation of `?` (equality/membership). True if left is not equal to (or not in) right.
**Example:** `$x !? 5` — x is not 5; `$x !? 2..5` — x is outside the range; `[health !? 0]` — entities whose health is not zero.
**Composes with:** same operands as `?`; supports `_fallback`.
**Edge cases:**
- Identical in semantics to `!($x ? right)` — provided as a chain for terseness, not new behavior.
- Maximal-munch: lexer grabs `!?` before parsing `!` then `?`.

#### `&&` — logical and
**Operands:** `left && right` — both boolean expressions.
**Behavior:** true if both operands are true. Short-circuits — if `left` is false, `right` is not evaluated.
**Example:** `@e[health ? ..50 && tag ? "boss"]` — entities with low health *and* boss tag; `($a && $b)` — both flags true.
**Composes with:** `||`, `!`, `?`, `!?`, ternaries (`?>`), filter conditions.
**Edge cases:**
- Short-circuit: side effects in `right` only fire if `left` is true.
- Precedence relative to `||` is conventional: `&&` binds tighter — `$a && $b || $c` reads as `($a && $b) || $c`.
- Non-boolean operands follow the Truthiness rules (Evaluation Model): `_`, `0`, `''`, `[]`, `{}`, all-zero vectors are falsey; everything else is truthy.

#### `||` — logical or
**Operands:** `left || right` — both boolean expressions.
**Behavior:** true if either operand is true. Short-circuits — if `left` is true, `right` is not evaluated.
**Example:** `@e[tag ? "boss" || tag ? "elite"]` — bosses or elites; `($cooldown ? 0 || $emergency)` — either condition.
**Composes with:** same as `&&`.
**Edge cases:**
- Short-circuit: side effects in `right` only fire if `left` is false.
- Looser than `&&` in precedence — group with `()` when mixing without relying on memorized precedence.
- Lexes as one token (maximal munch): `||` over `|` + `|`.

#### `!&` — nand
**Operands:** `left !& right`.
**Behavior:** negation of `&&` — true unless both operands are true.
**Example:** `($a !& $b)` — at least one is false.
**Composes with:** logical operators; reads as "not (a and b)."
**Edge cases:**
- Semantically equivalent to `!($a && $b)` — provided for terseness.
- Maximal-munch: `!&` lexes as one token before `!` then `&`.
- Short-circuit semantics inverted from `&&`: if `left` is false, result is true and `right` is skipped.

#### `!|` — nor
**Operands:** `left !| right`.
**Behavior:** negation of `||` — true only when both operands are false.
**Example:** `($a !| $b)` — both are false.
**Composes with:** logical operators; reads as "not (a or b)."
**Edge cases:**
- Semantically equivalent to `!($a || $b)`.
- Maximal-munch: `!|` lexes as one token.
- XOR / XNOR are *not* given symbols; rare in command-language use. Compose if needed: `xor` is `($a || $b) && !($a && $b)`.

### Binding

#### `=` — bind (eager)
**Operands:** `$name = value` (optional `_fallback`).
**Behavior:** evaluates `value` *immediately at bind time* and stores the result. `$name` then holds that static snapshot until rebound. A scope is a value like any other — `$marked = @e[in tags ? 'cursed']` captures *those specific entities* (the set that matched at bind time), and `$marked Kill` later acts on exactly them, even if they've since stopped matching.
**Example:** `$snapshot = @e[health ? ..50]` — captures the current set of low-health entities; doesn't update as health changes. `$snapshot Heal[20]` later heals exactly that captured set.
**Composes with:** `_` for null-fallback (`$x = $maybe _0`); unbinding is `$x = _` (binding to null).
**Edge cases:**
- Member of the binding family — see `=>` (lazy) and `=&` (alias) for variants with different update semantics.
- **Scope capture is snapshot under `=`, live under `=>`.** `$m = @e[...]` binds the resolved set (those entities, frozen); `$m => @e[...]` binds the *query*, re-evaluated each use (whoever matches when `$m` is acted on). Eager = a named cohort; lazy = a standing condition.
- Editing the right-hand expression *later* doesn't affect already-bound `$name` — the binding is value, not reference.
- `$x = _` deliberately nulls/unbinds — uses no special operator, just composes `=` and the null value.

#### `=>` — live/lazy binding
**Operands:** `$name => expression`.
**Behavior:** binds `$name` to the *expression itself*, which is re-evaluated *every time* `$name` is read. The right side is never evaluated at bind time.
**Example:** `$BossBloodied => @e[tag ? "boss"].health ? ..50` — `$BossBloodied` is always the current truth about boss health, freshly computed on each read.
**Composes with:** any expression that returns a value.
**Edge cases:**
- Member of the binding family — see `=` (eager snapshot) and `=&` (alias, push on change).
- Re-evaluates on *every* access — for hot paths, this is expensive. Eager `=` is the alternative for one-shot capture; `=&` is the push-on-change alternative.
- The expression's references are looked up at access time, not bind time — so a `=>` referring to `$x` follows `$x`'s current value, not what `$x` was when the binding was created.
- Side effects inside a `=>` expression fire on every read (this is rarely desired; keep `=>` expressions pure).
- A `=>` expression that errors fires the error on every read until the underlying issue is fixed.

#### `=&` — bidirectional alias
**Operands:** `slotA =& slotB` — *both operands must be storage locations* (a variable, a scope-target property, an arg slot), never a computed value. `=&` is not an assignment and has no direction: it fuses two storage locations into one, so there is no "assign a to b" and nothing to reverse — the two names simply become one slot.
**Behavior:** fuses two slots into one storage. Reads from either side see the same value; writes to either side propagate to both. The two names become two views of the same underlying location. Because it identifies storage rather than moving a value, both sides must be addressable storage — aliasing a computed expression (`#$x`, `$a + 1`) has no slot to fuse and errors.
**Evaluation order:** `=&` binds early — before arithmetic, comparison, and the other binding operators (precedence level 3) — but *after* member access and postfix (level 1), because the alias needs the storage *location* resolved before it can wire it up. So `@s.health =& $x` resolves `@s.health` to a location first, then aliases; and `$amount = arg[0] =& param{'amount'}` establishes the `=&` alias before the `=` snapshots. The alias is in place before any arithmetic or comparison in the statement reads the slots.
**Example:** `$displayed_health =& @s.health` — UI variable and entity health are the same slot; either updates both. `arg[0] =& param{'amount'}` — positional and named views of a non-target argument unified within a function body. `$amount = arg[0] =& param{'amount'}` — `=&` evaluates first to unify the arg slots, then `=` snapshots the unified value into the local `$amount`.
**Composes with:** any two slots that can be writable storage; commonly used for ui-state mirroring, function argument unification (positional/named), and any "two names, one truth" pattern.
**Edge cases:**
- Third member of the binding family alongside `=` (eager snapshot) and `=>` (lazy re-evaluation). The coupling axis: zero (`=`), pull on read (`=>`), full bidirectional (`=&`).
- For read-only consumers (UI displays a value, doesn't modify it), the bidirectionality is unused — observationally equivalent to one-way push.
- Aliasing a literal or expression result is meaningless — there must be a writable target. Aliasing two literals errors.
- Unbinding (`slotA = _`) breaks the alias on that side; the other slot retains its value but is no longer linked.
- Aliases form a graph; circular aliases (`$a =& $b`, `$b =& $a` then `=& $c`, etc.) need detection. Direct circles between two names are a no-op (they're already the same slot).
- Function-body usage: `arg[n] =& param{'name'}` inside a body unifies the positional and named views of argument slot `n` for the duration of that call. Combined with `$local = arg[n] =& param{'name'}` (the `=&` fuses the arg slots at level 3, then `=` snapshots into a local), the body can accept both `Heal[@s, 50]` and `Heal{target: @s, amount: 50}` calls uniformly. The local is a clean snapshot, not part of the alias union.
- `=&` binding early (level 3) means it pairs correctly with the loose binding operators: `$a =& $b` then a later `$a = 5` assigns 5 to both, because the alias is wired before the assignment runs. `# a =& b` is `(# a) =& b` — the prefix resolves its operand first, then the alias; aliasing a computed value (rather than a writable location) errors.

#### `|>` — directed write
**Operands:** `value |> $variable`.
**Behavior:** writes the value flowing through (from a pipe or expression) into a variable. Used mid-chain or terminally.
**Example:** `@e[...] | filter |> $filtered | damage 5` — capture filtered set into `$filtered`, continue piping to damage.
**Composes with:** `|` (pipe chains), `=` (alternative destination-first form).
**Edge cases:**
- Whether the value passes through `|>` (still flowing into next `|`) or is consumed (next stage reads `$variable`) is interpreter-defined.
- The right operand must be a variable (writable destination), not a command — that's `|`'s job.
- `|>` is the source-first form of `=`; `$x = value` and `value |> $x` are equivalent in result, different in reading direction.

### Vector prefixes

#### `^` — local / relative-to-facing
**Operands:** prefix on a scalar (`^5`) or whole vector (`^$target`).
**Behavior:** interprets the value in the executor's local frame — relative to *facing direction*, not just position. Distributes over vector components.
**Example:** `^5` = 5 units forward; `^$target` = the target expressed in self's facing-relative frame.
**Composes with:** vector literals (`<^1, ^0, ^2>`), `->` and `~>` for movement, `~` (sibling relative-prefix).
**Edge cases:**
- Requires an executor with orientation (`@s` must have a facing); otherwise undefined.
- `^$vector` distributes — `^<1, 0, 5>` is `<^1, ^0, ^5>`, not a single "local-flag" on the whole.
- Inside a vector with mixed prefixes: `<^1, 5, ~3>` is local-x, absolute-y, relative-z — each component carries its own framing.

#### `~` — relative to current
**Operands:** prefix on a scalar (`~5`) or whole vector (`~$target`).
**Behavior:** interprets the value as offset from the executor's current value (world position, not facing). Distributes over vector components.
**Example:** `~5` = current + 5; `~<1, 0, 0>` = one unit east; `~$target` = world-relative to current position.
**Composes with:** same as `^` — vectors, `->`, `~>`, mixed with absolute/local in vector components.
**Edge cases:**
- The "current value" depends on context — usually the executor's position for spatial scopes, but could mean current time component in `@t`.
- `~` and `^` differ subtly: `~` is "offset in world frame," `^` is "offset in *facing-rotated* frame." `~5 ~0 ~0` is east by 5; `^5 ^0 ^0` is 5 in whatever direction you're facing.

#### `°` — rotation
**Operands:** prefix on a scalar (`°90`) or whole vector (`°<90, 0, 0>`). Components can be ranges (`°<..90, 0, 0>` = some pitch in 0-90) or fully open (`°<..>` = any rotation).
**Behavior:** interprets the value as a *rotation* rather than a position/translation. Components represent rotation around their respective axes (rotation around x, y, z). Distributes over vector components.
**Example:** `°<0, 90, 0>` — rotation of 90 around the y-axis (yaw); `°<45, 0, 0>` — pitch up 45; `<~5, °90, 0>` — translate 5 along x and rotate 90 around y in one vector; `°<..>` — any rotation (used for rotationally-symmetric shapes like spheres).
**Composes with:** vector literals (per-component mixing with `~`/`^`/bare); `->` and `~>` for applying rotation; combines with other prefixes (`°~<0, 30, 0>` = relative rotation, 30 added to current rotation); `*` to apply a rotation to a vector (`<10, 0, 0> * °<0, 90, 0>` rotates a vector by 90 yaw).
**Edge cases:**
- The vector's components are rotation values around the corresponding axis, not Euler-angle ordering by some convention. Runtime decides the convention (degrees vs radians, axis ordering).
- Bare `°` (rotation only) composes with `->` for setting absolute orientation; `°~` (rotation + relative) gives relative rotation.
- Mixing translation and rotation in one vector: `<~5, °90, ~0>` is a single transform applied component-wise — each component carries its own frame.
- Range-valued rotations express *families of rotations*: `°<..>` is "any rotation," used in derived shapes like spheres (a length-10 vector under any rotation is a length-10 sphere shell).
- `vector * rotation` applies the rotation to the vector (same `*` as scalar multiplication, dispatched on operand type).
- `°` is the canonical symbol; on keyboards where it isn't directly typeable, users bind a key combination at the input layer. The language definition doesn't change.

### Brackets

#### `()` — grouping / precedence
**Operands:** `(expression)`.
**Behavior:** groups an expression to control evaluation precedence. No collection semantics.
**Example:** `($a + $b) * $c` — group sum before multiplication.
**Composes with:** any expression.
**Edge cases:**
- Distinct from function-call brackets (`[]`); `MyFunc(a, b)` is not a call, it's `MyFunc` adjacent to a parenthesized expression — likely a parse error or unintended.
- Empty `()` has no defined meaning.

#### `[]` — array literal / index / call / filter
**Operands:** context-disambiguated by what's on the left.
- Bare `[1, 2, 3]` — array literal.
- `$arr[0]` — index into an array (integer key).
- `MyFunc[a, b]` — positional function call.
- `@e[condition]` — scope condition filter.
**Behavior:** "apply with these arguments/conditions" — the bracketed contents are interpreted per left context.
**Composes with:** any name, scope, or value supporting the relevant operation.
**Edge cases:**
- `["key"]` (string in array brackets) on an array is a type error — array indices must be integers.
- The left operand's *kind* (array vs. function vs. scope) determines interpretation; in a runtime-is-compile-time language, this happens at evaluation.
- Filter form (`[condition]`) inside scopes uses `?` directly: `[health ? ..50]`.

#### `{}` — map / named call
**Operands:** `{k: v, k: v}` or `MyFunc{k: v}` or `$map{key}`.
**Behavior:** map literal (key-value pairs), map access (key lookup), or named function call (named args as map).
**Example:** `$cfg = {speed: 5, jump: 8}`; `$cfg{"speed"}` = 5; `MyFunc{verbose: true}` — named call.
**Composes with:** `:` for key-value pairs inside.
**Edge cases:**
- Arrays are accessed only with `[]` and indexed only by integers. `$arr["key"]` is wrong (strings aren't valid array indices); `$arr{0}` is wrong (`$arr` is an array, use `[]`).
- Maps are accessed only with `{}` and keyed by *any value* — numbers, strings, vectors, namespaced ids, arrays, other maps. `$map{0}`, `$map{"name"}`, `$map{<5,0,3>}`, `$map{shadebreaker:phys:stone}` are all valid. `$map[0]` and `$map["key"]` are wrong (`$map` is a map, use `{}`).
- Map keys must be values with equality semantics (everything constructible in the language has these).
- Named-call inside body reads args as `arg{"name"}`; mismatched call form (passed `{}`, body indexes `arg[0]`) errors at access.

#### `<>` — vectors
**Operands:** `<v1, v2, v3>` with 2-4 components; each component is a number, a range, a prefixed value (`~5`, `^2`), or `_` (an absent axis).
**Behavior:** constructs a fixed-arity numeric vector. Components can be ranges to form a region (axis-aligned box). Shorthand `<..>` is equivalent to all components being unbounded ranges — `<.., .., ..>` for a 3-vector.
**Example:** `<5, 10, 3>` — point; `<-5..5, 0..10, -5..5>` — region; `<.., ..10, ..>` — any x/z, height ≤ 10; `<..>` — fully unconstrained vector (any value on every axis).
**Composes with:** `~`/`^`/`°` prefixes (per-component or whole-vector), `*` (scaling, rotation application), `+`/`-` (component-wise), `#` (magnitude — only on point vectors).
**Edge cases:**
- Components are numbers, ranges, prefixed values (`~`/`^`), or `_`. Nesting (vector-of-arrays) is undefined.
- **`_` component is an absent axis.** A `_` in a component slot means that axis is undefined — the operation skips it. In placement, `@s.position -> <_, 10, _>` writes only y; x and z aren't acted on. This is distinct from `<~, 10, ~>`: `~` is *relative-to-current* (a real value — current-plus-zero — that gets written back), while `_` is *absence* (no instruction for that axis). They coincide in effect for a static placement, but differ in meaning — `~` participates in the operation carrying the current value, `_` abstains from it.
- **`<~>` versus `<_>`.** `<~>` is the all-relative shorthand (`<~, ~, ~>`) — every axis relative-to-current, i.e. "no change" as a placement. `<_>` is the empty-vector equality witness (see `?` — the literal empty vector for `$x ? <_>` tests). Different glyphs, different jobs: `<~>` is a usable no-change vector, `<_>` is the null/empty-vector literal.
- Component count is fixed per vector; mixing 3-component and 4-component vectors in one operation is a type error.
- A vector's component *meaning* comes from the scope interpreting it, not from the vector itself. There's no distinct "time vector" or "position vector" type — `@t<...>` and `@s<...>` are the same vector kind, interpreted differently by their scopes. The component count (arity) is the vector's own property; what the components *mean* is the scope's.
- Bare `<>` regions are axis-aligned boxes. Non-AABB regions (spheres, cylinders, etc.) compose from vector arithmetic, range components, and the `°` rotation prefix, and fill the `<region>` slot like any region — see the Examples section.
- `<..>` shorthand: a single `..` inside the vector brackets means "this `..` applies to every component," yielding a fully-open vector of the appropriate arity. The arity is inferred from context (3 for spatial, 3 for time, 2 for 2D, etc.).
- Region operations (intersection, union, containment) on range vectors are *spatial set ops*, not interval arithmetic — dot/cross of range vectors is not defined.
- Vectors are constructed and operated on as wholes; there is no *direct named or indexed* component access (no `$vec.x`, no `$vec[0]`). Components are still reachable by iteration (`$c in $vec<...>`) and membership (`in $vec ? value`). To change one axis while preserving others, reconstruct with `~` (relative-to-current) on the kept axes: `@s.position = <~, 10, ~>` sets Y to 10 and keeps X and Z.

### Chains

#### `..` — range constructor
**Operands:** `low..high`, `..high`, or `low..`. Bounds may be omitted. Works over any orderable type — numbers, single-character strings, time-component values.
**Behavior:** constructs a range value (inclusive on both ends). Used as a vector component, as the right operand of `?` (membership), or in scope index slices (`@no(0..2)`).
**Example:** `2..5` numeric range; `..5` open below; `5..` open above; `!..5` above 5 (exclusive); `'a'..'z'` character range; `<2..5, .., ..12>` vector with range components forming a region.
**Composes with:** `!` (negates the range — `!2..5` = outside that range), `?` (membership test), `<>` (vector components), `()` (scope slices).
**Edge cases:**
- Always inclusive; for exclusive bounds, compose with `!`.
- `..` is a *constructor*, not a comparison operator — it builds a value. Comparison is via `?`.
- Works for any orderable type: numeric ranges on numbers, character ranges on single-character strings. Multi-character string bounds (`'foo'..'bar'`) aren't defined as a range and would likely be an error.
- Empty range (`5..2`, where low > high) is a valid empty range: nothing is in it, so any membership test against it finds no match. Not an error — it just matches nothing.
- Does not extend to substring positioning — string glob matching uses `**` inside the string itself, not `..`.

#### `->` — vector placement / "go to"
**Operands:** `mover -> destination`, where destination is a vector value. `mover` is a scope's vector-typed property (commonly `position`).
**Behavior:** sets the mover's vector-typed value directly to the destination. The semantic interpretation depends on the scope: spatial scopes interpret it as movement to a position; the time scope interprets it as setting the time.
**Example:**
- `@s.position -> <~>` — move self to its own current position (`<~>` = `<~, ~, ~>`, all components relative = no change)
- `@s.position -> <5, 10, 3>` — move self to absolute position (spatial)
- `@t -> <0, 0, 12>` — set time to day 0, minute 0, hour 12 (temporal)

**Bare-scope sugar.** `@type -> v` is shorthand for `@type.position -> v` — acting on a scope directly with `->` targets its position. `@s -> <5,10,3>` and `@s.position -> <5,10,3>` are the same. The same sugar applies to a scope *destination*: when the right side of `->` (or `~>`) is a scope, its `.position` is read, so `@s -> @np` means `@s.position -> @np.position` — teleport self to the nearest player. (The same sugar applies to `~>` on both sides.)

**Composes with:** any vector destination (with `~`/`^` prefixes for relative/local), and scopes whose state includes a vector (position, time, etc.).
**Edge cases:**
- Destination must be a vector; the scope determines what its components mean. Same vector value (e.g. `<5, 0, 12>`) means position with `@s` and a moment in time with `@t`.
- `->` places (instant, like assignment); `~>` is the normalized-step toward version.
- The `>` suffix marks this as a directed/targeted operation, distinct from plain `|` flow.

#### `~>` — normalized directed step
**Operands:** `mover ~> target * magnitude`, where target is a vector value and `* magnitude` is a *trailing modifier of the step itself*, not general multiplication. The step is a dedicated production: `mover ~> target` optionally followed by `* magnitude`, where the `* magnitude` suffix is parsed by the step and binds to the whole step — it does not multiply the target. So `@s ~> $enemy * 5` is "(step `@s` toward `$enemy`) at magnitude 5," never "step toward (`$enemy * 5`)."
**Behavior:** moves the mover toward target along the unit direction, scaled by magnitude. Normalization is implicit — the direction is always unit-length before scaling. Scope determines semantics: spatial scopes interpret as physical movement; time scope interprets as time advancement toward a target time at the rate.
**Example:**
- `@s ~> $enemy * 5` — self heads toward enemy at speed 5 (spatial)
- `@e[tag ? "swarm"] ~> @np * 3` — swarm entities head toward nearest player at speed 3 (spatial)
- `@t ~> <5, 0, 0> * 0.1` — time advances toward day 5 at rate 0.1 per tick (temporal)
**Composes with:** any vector target; the trailing `* magnitude` modifier (part of the step production, outside general `*` precedence).
**Edge cases:**
- The `* magnitude` suffix is part of the `~>` production, parsed outside the general operator-precedence table. This is why it reads as a step modifier rather than as scaling the target — the precedence table's level-4 `*` does not apply here.
- Without `* magnitude`, behavior is undefined — direction alone with no scale; default is interpreter choice.
- Target at same value as mover: direction is undefined (zero vector); interpreter must handle (no-op or error).
- `~>` is step-toward, not arrive-at — repeated application is needed to reach the target.
- The mover and target should share the same vector interpretation (both positions, or both times) — the scope on the left tells the runtime which.
- Like `->`, `@type ~> v` is sugar for `@type.position ~> v`.

#### `?>` — conditional if-then
**Operands:** `condition ?> ifValue`, optionally followed by `?? elseValue`.
**Behavior:** evaluates `condition`; if true, returns `ifValue`; if false and `??` is present, returns `elseValue`; if false and `??` is absent, returns null.
**Example:** `$damage = ($isDaytime ?> 10 ?? 20)` — 10 by day, 20 otherwise. `$msg = ($alert ?> "danger")` — "danger" if alert, null otherwise.
**Composes with:** `??` for the else branch; `()` for compound expressions on either side; any boolean-producing expression as the condition.
**Edge cases:**
- The else (`??`) is optional. Without it, false condition yields null — which then composes with `_value` fallback on the consuming side if you want a different missing-behavior.
- `?>` and the bare `?` (comparison) are distinct tokens; maximal munch grabs `?>` when followed by `>`.
- Values can be expressions; group with `()` when they contain operators that might lex ambiguously (`$x ?> ($a + $b) ?? ($c * 2)`).
- Short-circuit evaluation: only the taken branch is evaluated. Side effects in the untaken branch don't fire.

#### `??` — conditional else
**Operands:** `?? elseValue`, used as the second half of a `?>` ternary.
**Behavior:** provides the false-branch value for a `?>` conditional. Without a preceding `?>`, `??` has no meaning and is a parse error.
**Example:** `$x = ($cond ?> $a ?? $b)` — `$b` is taken if `$cond` is false.
**Composes with:** `?>` exclusively; only meaningful as the tail of a ternary.
**Edge cases:**
- A bare `??` (no `?>` preceding) is a parse error — it's not a standalone operator.
- Maximal munch handles `??` vs. `?` (`a ?? b` is else-branch; `a ? ?b` would be equality-against-not-b if that were a valid form).
- Nested conditionals use `()` grouping rather than special chaining rules. The forms `$n ? whatever ?> if` (then-only) and `$n ? whatever ?> if ?? else` (then-else) are the only shapes; for nesting, wrap the inner conditional as an expression with `()`. Example: `$n ? 5 ?> ($m ? ..10 ?> collect 'small') ?? collect 'five'`.

### Flow & comments

#### `|` — pipe (into command)
**Operands:** `value | command` or `command1 | command2`.
**Behavior:** sends the value (or previous command's result) into the next command as its primary input.
**Example:** `@e[health ? ..50] | damage 10` — find entities, pipe to damage.
**Composes with:** any command chain; `|>` for writing into variables instead of next command.
**Edge cases:**
- The "primary input" position in the receiving command is interpreter-defined (default arg slot, typically `arg[0]`).
- Piping a set scope into a single-target command implies iteration — the command runs once per element.
- Pipe direction is left-to-right; the chain is read in flow order.

#### `;` — command separator
**Operands:** between two commands on the same line.
**Behavior:** ends the current command and starts a new one. The next command runs after the previous completes; results are not piped.
**Example:** `heal @p 100; damage @e[tag ? "enemy"] 5` — heal players, then damage enemies. Two independent commands on one line.
**Composes with:** any commands; newline is the implicit separator otherwise.
**Edge cases:**
- Different from `|` — `;` runs sequentially, *discarding* the previous result. `|` pipes the result into the next command.
- Different from `&&` — `;` runs unconditionally. `&&` would run the next *only if* the previous succeeded (if you adopt that convention later).
- Trailing `;` is a no-op (empty command after); not an error.
- Inside string literals or function bodies, `;` is just a character / interior content.

#### `//` — line comment
**Operands:** everything from `//` to end of line.
**Behavior:** ignored by the parser. Documentation only.
**Example:** `// heal everyone` then `@p heal 100`.
**Composes with:** any line.
**Edge cases:**
- Line-end terminates; no block-comment form (`/* */` is not supported — design choice for a live command language like Cast).
- Inside a string literal, `//` is just characters, not a comment marker.

### Strings

#### `'...'` — string literal
**Operands:** characters between single quotes.
**Behavior:** a string value. Single quotes are the literal delimiter; double quotes are not — `"..."` is reserved for the host (shell, console, command-block input) and a double-quoted string would terminate the wrapping context.
**Example:** `'hello'`, `'hp: $health'` (interpolated), `'don''t'` (embedded quote via doubling), `'**boss**'` (glob pattern matching anything containing "boss").
**Composes with:** `$name` interpolation inside; `+` for concatenation; `-` for substring removal; `?`/`!?` for equality and glob matching; any string-accepting command.

**Operations on strings:**
- `'foo' + 'bar'` — concatenation, yields `'foobar'`
- `'hello world' - 'world'` — substring removal, yields `'hello '`. No-op if the substring isn't present.
- `$str ? 'exact'` — exact string equality (when right operand has no wildcards)
- `$str ? '**sub**'` — contains 'sub' (glob match)
- `$str ? 'pre**'` — starts with 'pre'
- `$str ? '**suf'` — ends with 'suf'
- `$str ? '**'` — matches any string
- `$str !? '...'` — negated form of any of the above
- Character ranges with `..`: `$char ? 'a'..'z'` — character is in the a–z range. Treats `..` over single-character strings the same way it treats numeric ranges.

**Edge cases:**
- To include a literal single quote, double it: `'don''t'` parses as `don't`.
- `$name` inside `'...'` interpolates the variable's value — same dereference as elsewhere.
- A `'...'` without matching close on the same line is a parse error (no multi-line string form currently).
- No backslash escapes — keeps the syntax small. If a special character is needed, it goes through a command or function.
- `**` inside a string is the *glob wildcard* — matches any sequence of characters (including empty). Distinct from `*` (multiplication / scaling), which never appears as a pattern character.
- To include a literal `**` in a string, that case currently isn't handled — pin a convention if it matters (probably tripling or escape sequence, but rare enough to defer).
- Rich string operations (position lookup, slicing, splitting, replacement) are *functions*, not operators — `index_of[$str, 'sub']`, `substring[$str, 0..4]`, `replace[$str, 'old', 'new']`. Operators handle the simple cases; functions handle the rest.

### Functions

#### `Name:: body ::` — function/macro definition
**Operands:** `Name` (identifier), `body` (sequence of commands/expressions).
**Behavior:** declares a callable. The target is established by the scope at the call site (`@s Func`, `@e Func`, etc.) and accessed via `@s` inside the body. Non-target operands supplied at the call site land in `arg` (positional, an array) and `param` (named, a map). Use `out $value` to exit the function with a return value; functions that don't hit `out` return null. (`out` exits the *immediately enclosing layer* — inside a loop, it exits the loop; at the top level of a function body, it exits the function. See *Iteration over collections* for loop semantics.)
**Example:**
```
// Simple — uses active scope's target via @s
Heal::
    $amount = arg[0] =& param{'amount'} ;
    @s.health = @s.health + $amount
::
@s Heal[10]                                 // positional arg
@np Heal{amount: 10}                        // named arg, different target
@e<region>[health ? ..50] Heal[50]          // set scope: heals each matched entity

// More operands, both call forms shown
ApplyDamage::
    $amount = arg[0] =& param{'amount'} ;
    $type = arg[1] =& param{'type'} ;
    @s.health = @s.health - $amount
    // (type would be used in a real implementation for resistance lookup)
::
@np ApplyDamage[10, 'fire']
@np ApplyDamage{type: 'fire', amount: 10}   // reordered via named form
```
Per-statement: `=&` binds early (level 3) to unify positional and named arg slots, then `=` snapshots the unified value into a local for the body to operate on. Named calls let the caller reorder args and pass complex values without the reader having to track which positional slot means what.
**Composes with:** `arg[n]` for positional access, `param{'k'}` for named access, `#arg` / `#param` for counts, `@s` for the active scope's primary subject. `=&` bidirectional alias unifies the two arg views for functions that accept both call forms.
**Edge cases:**
- `@s` is the function's *primary subject* — the thing it's "about" — established by the call site's scope (`@target Func args`). Args carry everything else.
- Args can hold any value type, including scopes used as *data* (a secondary entity to interact with, a region to query, a destination). For example, in a hypothetical `@s Damage[@np]`, self is the primary subject (the thing doing the damage) and `@np` is in arg position because it's referenced as data (the target), not operated on as the primary subject.
- A body that only reads `arg[n]` works on positional calls; a body that only reads `param{'k'}` works on named calls. To support both, alias them with `=&` at the top of the body.
- Mismatched call form without aliases (caller used `[]`, body reads `param{'name'}`) errors at the access site — the named view is empty.
- Definitions can be nested if the parser tracks `::` depth; otherwise nesting is disallowed.
- Return values are explicit via `out`: at the top level of a function body, `out $value` exits the function with that value. There's no implicit return — the body's last expression is not automatically returned.
- `out` exits the *immediately enclosing layer*. Inside a loop within a function, `out` exits the loop, not the function. To exit the function from inside a loop, the loop must complete (its result becomes available to the function) or be wrapped with another `out` at the function level.
- A function whose body ends without hitting `out` returns null.

#### `Name[a, b]` / `Name{k: v}` — function calls
**Operands:** function name, then `[positional]` or `{named}` non-target args. The target comes from the preceding scope.
**Behavior:** invokes the function with the preceding scope as the active target. `[args]` populates `arg` (positional, array); `{args}` populates `param` (named, map). Body chooses which to read; bodies that alias `arg[n] =& param{'name'}` accept either form.
**Example:** `@s Heal[100]`; `@np ApplyDamage{amount: 10, type: 'fire'}`.
**Composes with:** the body's access pattern via `arg` and/or `param`, and the calling scope.
**Edge cases:**
- Built-in commands and user functions use the same call syntax — no sigil distinction.
- A call is an expression; its return value can be assigned, piped, compared.
- Functions called without a scope prefix run in whatever active scope was inherited from the caller (commonly `@s` for the user typing the command).
- Calling with a form the body doesn't handle (function reads only `arg`, called with `{}`) errors at first access — the relevant view is empty.

---

## Commands & Standard Library

### Base commands

The language ships with a minimal command set. These are commands that *can't be derived from the language's operators* and that *every command-line user genuinely needs*.

**Command arguments use brackets.** A command takes its arguments in `[...]` (positional) or `{...}` (named), exactly like a function call — `tag['boss']`, `say[$value]`, `rng[1..10]`, `Heal[50]`, `Heal{amount: 50}`. There is no space-separated argument form; a bare command name with no brackets is a zero-argument call (`@s Kill`, `clear`, `casts`). Two commands have their own structured grammar instead of plain bracket-args, because their arguments aren't a simple value list: `cast` (the `[count] [over N] scope command` envelope) and `spawn` (the `<id>(selection)<region>[properties]` shape that reuses the scope chain). Everything else is plain bracket-args.

**Built-ins (language-owned, always available):**

- `log[$value]` — write to the language's developer console (a panel the language ships with, embedded in the host). For debugging and tracing. Always present, no host wiring needed.
- `rng` (with optional range arg) — random number. Bare `rng` returns a number in `0..1`; `rng[1..10]` returns a number in that range. The language owns its RNG.
- `def $name` — true if the name is currently bound (variable, function, or alias). Composes with `?>` for guarded reads (`def $maybe ?> $maybe ?? 0`).
- `clear` — clear the developer console output. Wipes the visible log buffer. Doesn't affect `@v`, saves, or any other state — only the console panel.
- `tag['name']` — add a tag to the active scope's target. `@s tag['boss']` adds `'boss'` to `@s.tags`. Filter via `@e[in tags ? 'boss']` or `@e['boss' ? in tags]` — both forms work since either side of `?` can hold the membership expression.
- `untag['name']` — remove a tag. `@s untag['boss']` removes it from `@s.tags`. No-op if not present.
- `cast [count] [over N] <scope-chain> command` — run a command in a scope, fired *whenever* that scope's state is reached. The namesake command of the language.

  **The core idea.** `cast` calls a command *now*, to run *whenever a certain scope-state is reached*, in that scope. The scope chain given *to* `cast` is an ordinary scope expression — the same grammar used everywhere — and it is *both the trigger and the context*: the command fires whenever that scope exists or its state holds, and runs in it. A cast given a scope is a *standing subscription*, not a one-shot.

  **Count is the level/edge switch.** Whether a standing cast fires continuously or once-per-entry is determined by the presence of a count:
  - **No count** — *level-triggered*: the command fires on anything fulfilling the scope, continuously, for as long as it keeps fulfilling. `cast @e<region> Hurt[10]` hurts each entity by 10 *every tick it remains in the region* — a damage field. `cast @e<region> Kill` kills anything in the region (it fulfills, then it's gone) — a death plane.
  - **Count N** — *edge-triggered*: the command fires N times *when a thing enters the scope*, then stops for that thing. `cast 1 @e<region> Hurt[10]` deals 10 once on crossing in — a tripwire. An entity that leaves and re-enters the scope triggers a fresh N firings: each entry is a new edge.

  Other scope-states read the same way: `cast @t<0,0,12> as @e[id ? @v:bell] Ring` (no count) fires whenever world-time reaches hour 12 — every day. `cast @t{'cooldown'} ? _ SpawnBoss` fires whenever the cooldown slot reads `_`. `cast @v:boss_dead Celebrate` fires when the condition becomes true.

  How often a cast fires depends entirely on how often its scope-state holds (level) or is entered (edge). There is no separate "scheduled" versus "immediate" form — one mechanism, firing whenever the state holds or is entered.

  **Fire-and-forget: a cast with no scope of its own.** Give `cast` no trigger-scope, and there's nothing to subscribe to — the command simply runs once, asynchronously, in the ambient scope, then is forgotten. The leading scope sets who it runs as; `cast` takes only the command:
  - `@s cast Heal[50]` — defer `Heal[50]` onto the async track, run once as `@s`, done. No recurrence, because there's no scope-state for the cast to watch.

  The presence or absence of the cast's own scope chain is the whole switch: given a scope, the cast subscribes to that scope-state and recurs; given none, it defers the command once in the ambient scope.

  **`as` redirects the running context.** By default the command runs *in the scope you cast to*. When you want to cast to one scope as the *trigger* but run the command *as a different scope*, add `as @scope`. You only need `as` when the running scope must differ from the trigger scope.
  - `cast @t<0,0,12> as @e[id ? @v:bell] Ring` — trigger is time reaching hour 12; the command runs as the bell entity. Without `as`, the command runs in the time scope (`cast @t<0,0,12> Ring` runs `Ring` as time itself), rarely what you want when the trigger is a time.
  - `cast @e<region> Kill` needs no `as` — the scope you cast to (each entity in the region) is exactly the scope you want to run as (the entity being killed).

  `at @scope` similarly sets position context. These are the same `as`/`at` defined above; `cast` reuses them, it doesn't define its own.

  **Repetition: `count` and `over N`.** Two optional modifiers, placed before the scope chain:
  - `count` (a bare number) — fire N times per entry (edge-triggered, as above). For a fire-and-forget cast (no trigger-scope), it simply fires N times: `@s cast 3 Pulse` runs Pulse 3 times as `@s`; with no `over`, all N run back-to-back on one tick.
  - `over N` — spread the count's firings (or a scope-only cast's iterations) evenly over the next N frames. `@s cast 3 over 45 Pulse` fires at frame 15, 30, and 45 (spacing N/count). `over N` needs something splittable — the firings of a `count`, or the iterations of a scope-only cast (`@s cast over 45 @e[expensive_filter]` splits that filter's work across 45 frames). With no count and no splittable loop there is nothing to spread.

  Putting `count`/`over N` before the command keeps the command and its args as the last thing parsed, so `@s cast 3 Heal[50]` is unambiguously "cast, 3 times, of `Heal[50]`" rather than arithmetic on the call. A bare number immediately after `cast` is always the count; this is unambiguous because of a language invariant: **a command never begins with a bare numeric literal** (commands are scope-led or identifier-led — a scope chain, a function call, or a built-in). Nothing that can follow `cast` as its command starts with a number, so a leading number is always the repeat count.

  **The command's shape never changes.** You write the command exactly as you would to run it now; `cast` and its modifiers go in front. `@e<region>[filter] Hurt[10]` hurts them now; `cast @t<0,0,12> as @e<region>[filter] Hurt[10]` hurts them at noon. Scheduling is a prefix, not a rewrite.

  **Lifecycle.** Active standing casts live on `@w.casts` — a world-state array. A standing cast (one given a trigger-scope) persists until removed. List active casts with `casts`; remove one with `uncast[id]`. Because `@w` is world state, active casts save and load with the world.

  **The session-local exclusion.** A cast is saved only if its *own* trigger and body contain no session-local (`$`). The check is on the tokens written directly in the cast — not down the call graph: `cast @v:cond Wither` is clean even though `Wither`'s body uses a function-local `$amount` internally, because that local is bound fresh per call, not captured from the session. The allowed ingredients of a savable cast are persistent state (`@v`, `@w`, `@t`), literals, namespaced ids, function/command calls (definitions cross the save boundary themselves), and the fire-time scope target (`@s`, args — resolved when the cast fires, not captured). A `$` written directly into the cast's trigger or body makes it session-only and excludes it from the save.

  This isn't an arbitrary restriction — a `$` directly in a standing cast's body is unsound even *within* a session: if the cast was registered inside a function, that call frame is gone by the time the world-state trigger fires, so the `$` re-resolves against the top level or comes up unbound. So `@v` is not merely the durable choice for a standing cast that reads a variable, it's the only sound one. To make a cast durable, reference `@v` instead of `$`.

  Fire-and-forget casts (no trigger-scope, or a bounded `count`) complete on their own and need no removal.

  **Errors in a fired cast** surface to the console at the point of execution, not where `cast` was written — the `cast` statement already completed; the command ran later.

- `casts` — return the list of active casts, one per line as an id paired with the cast command as a string. Queryable like any array (`casts[command ? '**Kill**']` to find kill-casts).
- `uncast[id]` — remove the active cast with the given id. The cast stops firing; entities already affected are unchanged.

**Save/load family (built-ins that require a host persistence binding):**

The language can serialize its own state — the persistent scopes (`@v` registry, `@w` world state, `@t` time and timer/counter slots) and user-defined functions — into a buffer. The host provides storage for the buffer; the language handles serialization round-tripping.

- `save 'name'` — serialize current state and hand the buffer to the host for storage under that name.
- `load 'name'` — retrieve the buffer for that name from the host and restore the state.
- `qsave` — `save` to a fixed quick-slot.
- `qload` — `load` from the fixed quick-slot.
- `saves` — return the list of available save names (an array of strings, queryable like any array).
- `unsave 'name'` — delete the saved state at that name.

These commands depend on the host providing a persistence binding (four functions: `Write`, `Read`, `List`, `Delete`). If the host doesn't wire persistence, save/load commands error visibly with a "no persistence provider" message. The persistent scopes (`@v`, `@w`, `@t`) and function definitions cross the save boundary; session-local `$` variables do not.

Active standing casts live on `@w.casts` and save with the world — *except* casts whose own trigger or body contains a session-local (`$`). Such a cast is session-only and is not written to the save. The exclusion is scoped to the cast's own tokens, not its call graph: `cast @v:cured Wither` saves even though `Wither` uses internal locals, because those are bound fresh per call. A durable curse references `@v`: `cast @e[in ancestors ? @v:cursed:bloodline] Wither` persists, while a `$cursed_one` version is dropped from the save.

This means the serializer can't dump `@w` wholesale — it must filter `@w.casts`, dropping entries whose own tokens reference `$`, so load never tries to re-resolve a dropped session-local. Because Cast fails visibly, `save` reports how many casts were excluded (`saved; 2 session-local casts not persisted`) rather than dropping them silently — a player should know a curse won't survive the reload, not discover it gone with no trace.

**File I/O family (built-ins that require a host directory binding):**

The host exposes one or more directories that Cast can access. Cast handles the actual file reading and writing internally; the host only declares *which directories* are reachable.

- `read['path']` — read a file as a string. Path is relative to a host-registered directory.
- `invoke['path']` — load a `.cast` file and execute its contents in the current session. Function definitions land in the function registry; `@v` writes persist; `$` writes leak into the importing session (which is correct, since `$` at top-level is session-local anyway). The `.cast` extension is implicit and can be omitted.
- `files['directory']` — list files in a directory (returns an array of names, queryable like any array). `files['scripts'][name ? '**.cast']` filters to only Cast files.
- `write['path', $contents]` — write a string to a file (in directories the host registered as writable).

These commands depend on the host registering at least one directory. Without a directory, file commands error visibly. The host's binding is simple — register a directory by name, mark it readonly or read-write. Cast handles I/O from there.

**Path semantics:**
- Paths are relative to the host-registered directory roots.
- Absolute paths and `..` traversal are disallowed — Cast can't reach outside the directories the host has explicitly exposed.
- Subdirectories within an exposed directory are accessible (`'creatures/goblin'`).

**`invoke` semantics:**
- The file runs as if its contents were typed at the current prompt, line by line.
- Function definitions from the file become callable in the current session.
- `@v` writes persist into the registry (since `@v` is persistent).
- `$` writes leak into the current session's top-level (session-local anyway).
- Naming collisions with existing functions error visibly — to override, the file must explicitly unbind the prior definition first.
- Circular invokes (file A invokes B which invokes A) are detected and refused.
- Re-invoking the same file re-runs it — no implicit guard against double-load. Files that want guard logic can check via `def`.

**Required-bindings (host must wire to a concrete implementation):**

- `say[$value]` — write to the host's player-facing channel (in-game chat, message log, dialog system, whatever the host treats as user-visible output). The host registers where `say` output goes. Falls back to `log` if the host doesn't register anything.
- `@scope msg['text']` — drop a spatial message at the active scope's position. Different from `say` (player-chat) and `log` (dev console): `msg` is *located in the world* — for debug markers, playtesting notes, and potential in-world communication systems (e.g. Souls-style player messages persisted across sessions in a multiplayer context). The host registers what happens with the message — render a floating label, persist it to a world-message store, send it to other players, whatever. Falls back to `log` with the location appended if the host doesn't register anything. Common forms:
  - `@s msg['hello']` — drop a message at self's position
  - `@np msg['enemy here']` — drop at nearest player
  - `@w<5,0,5> msg['spawn']` — drop at specific world coordinates
- `spawn <id>(selection)<region>[properties]` — create one or more entities of the given kind. The grammar reuses the scope chain shape: a namespaced id specifies the kind, an optional `(selection)` count, a vector or region for where, and a property bracket for initial state. The host implements entity creation; the language declares the command's grammar and dispatches.
  - `spawn shadebreaker:creature:wolf<5, 0, 5>` — one wolf at that position
  - `spawn shadebreaker:creature:wolf<5, 0, 5>[health: 100, name: 'Boss']` — with properties
  - `spawn shadebreaker:creature:wolf(5)<region>` — five wolves in the region
  - `spawn shadebreaker:creature:wolf(3..7)<region>[health: 100]` — 3 to 7 wolves in the region, each with health 100

Beyond these, all commands are host-defined. The base set is deliberately tiny because everything else either composes from existing operators or belongs to the host's domain.

### Standard library

A layer above the base command set: a vocabulary of *standard property names* and *standard functions* operating on them. The language declares these names; the host binds them to concrete runtime values; the standard library provides canonical operations.

**Standard property vocabulary.** The language declares these names as canonical, and hosts wire each to a concrete runtime value (via `TryGetProperty`/`TrySetProperty` on the relevant target adapter). If a host doesn't bind a property, reads error visibly (strict access) — which is correct: a puzzle game with no health concept shouldn't pretend to.

Common standard properties:
- `health` — current health value (number)
- `max_health` — maximum health
- `position` — vector position
- `rotation` — rotation vector
- `velocity` — movement vector
- `name` — display name (string)
- `id` — entity identifier
- `tags` — array of string tags attached to the entity

A host can bind a subset depending on what its game has. The exact list of declared standard properties evolves as the standard library grows; what matters is that the names are canonical so portable scripts can rely on them.

**Standard functions.** Operations on standard properties, expressible in Cast itself. The entity-acting functions (`Heal`, `Hurt`, `SetHealth`) ship *as Cast source* — a prelude loaded into the runtime at startup — so what executes is the reference implementation itself, and users can read exactly how a standard function is constructed (and write similar ones). They use only language primitives and the standard property names; nothing host-specific.

A host that binds the standard properties gets these functions automatically — they're not host code, they're language standard library. The host may *override* a standard function via the binding interface (e.g. to make `Heal` cap at `max_health` as a game policy), but the default reference implementation does the plain thing (`Heal` adds to `health`, no cap). A host that doesn't bind `health` will see `Heal` error at the property-access site, which is correct.

The reference implementations (these are the exact bodies loaded as the prelude):

```
Kill::
    @s.health = 0
::

Heal::
    $amount = arg[0] =& param{'amount'} ;
    @s.health = @s.health + $amount
::

Hurt::
    $amount = arg[0] =& param{'amount'} ;
    @s.health = @s.health - $amount
::

SetHealth::
    $value = arg[0] =& param{'value'} ;
    @s.health = $value
::
```

(`Kill` in the reference sets health to 0; a host that needs richer death semantics registers `Kill` as a command instead.)

```
Kill::
    @s.health = 0
::

Heal::
    $amount = arg[0] =& param{'amount'} ;
    @s.health = @s.health + $amount
::

Hurt::
    $amount = arg[0] =& param{'amount'} ;
    @s.health = @s.health - $amount
::

SetHealth::
    $value = arg[0] =& param{'value'} ;
    @s.health = $value
::
```

`@s` is always the function's primary subject — what it's "about" — established by the call site's scope prefix. Args carry everything else: amounts, values, destinations. Movement and teleportation use the language's placement operator `->` directly (e.g. `@s.position -> <0, 0, 0>` to move to origin, `@s -> @np` to teleport self to the nearest player — bare-scope sugar makes both sides target their `.position`) — no wrapping function needed. Example calls:

```
@s Kill                                 // self-explanatory
@np(0..2) Hurt[10]                      // hurt the three nearest players by 10
@e<region>[health ? ..50] Heal[50]      // heal low-health entities in region
@s.position -> <0, 0, 0>                // move self to origin (via -> directly)
@s -> @np                               // teleport self to nearest player (bare-scope sugar)
```

A host that binds the standard properties gets these functions automatically — they're not host code, they're language standard library. A host that doesn't bind `health` will see `Heal` error at the property-access site, which is correct.

**Portability via the standard library.** Scripts written against standard functions and properties run unchanged on any host that binds the relevant properties. `@s Heal[50]` does the same thing across every compliant host — the implementation adds 50 to `@s.health`, and the host's property bindings make `health` refer to the right runtime value.

**Standard math functions.** Pure numeric operations that ship in every Cast runtime as native intrinsics — they can't be written in Cast itself (no primitive computes a square root), so the runtime implements them directly. The names are canonical so scripts can rely on them across hosts. Called like any other function (e.g. `Sqrt[16]`, `Min[3, 7]`).

- `Floor[n]` — largest integer ≤ n
- `Ceil[n]` — smallest integer ≥ n
- `Round[n]` — nearest integer (half rounds to even, or up — host's choice)
- `Abs[n]` — absolute value
- `Min[a, b]` — smaller of two
- `Max[a, b]` — larger of two
- `Clamp[n, lo, hi]` — `n` constrained to `[lo, hi]`
- `Sqrt[n]` — square root
- `Pow[base, exp]` — `base` raised to `exp`
- `Sin[n]` — sine (radians)
- `Cos[n]` — cosine (radians)
- `Atan2[y, x]` — angle of the vector from origin to (x, y), in radians, range `(-π, π]`

These are reserved names; users cannot redefine them as functions. Like other standard library functions, hosts can override the implementations via the binding interface (e.g. for AOT-platform performance reasons), but the names and signatures are fixed.

**Overriding the standard library.** A host can override a standard function if it needs custom behavior (e.g. a damage-resistance system that wraps `Hurt`). The host's override takes precedence over the standard implementation; portable scripts may then behave differently on that host, which is the trade-off.
## Examples

Worked examples showing how Cast's primitives compose. These are illustrative, not new syntax — everything here is built from operators defined in Detailed Definitions.

### Derived shapes

Non-AABB volumes don't need new syntax — they emerge from composing the existing primitives: vector arithmetic, range components, rotation prefix `°`, and `*` for rotation application.

**Sphere shell** (surface only, fixed radius):
```
<5, 0, 5> + <10, 0, 0> * °<..>
```
Read as: center `<5, 0, 5>`, plus a length-10 vector under *any* rotation (`°<..>`). The set of all length-10 vectors radiating from the center = sphere shell of radius 10.

**Solid sphere** (filled ball):
```
<5, 0, 5> + <..10, 0, 0> * °<..>
```
The radius component is now a range (`..10`) — vectors of length 0 to 10, in any rotation. All points within radius 10 of the center.

**Cylinder around the y-axis** (radius 5, height 0-10):
```
<5, 0..10, 0> + <..5, 0, 0> * °<0, .., 0>
```
The y-component varies independently (0-10 vertical height); the radial offset is a length-5 vector rotated around y (`°<0, .., 0>` is "any rotation around the y-axis only").

**Cone, ring, hemisphere, torus** all follow from constraining the rotation range or radius range further. None require new syntax — they're descriptions of "what set of offset vectors qualifies," composed from `°`-range and `..`-range.

**Used as a region:**
```
@e<5, 0, 5> + <..10, 0, 0> * °<..>
```
Entities whose position is in the solid sphere. The sphere is the *region* — it goes in the `<region>` slot of the scope grammar, narrowing by position automatically. No manual `[pos ? ...]` membership test needed; that's what the region slot does.

Combine with conditions on the entities themselves:
```
@e<5, 0, 5> + <..10, 0, 0> * °<..> [health ? ..50]
```
Entities in the sphere, with low health. Region narrows by position; condition narrows by predicate.

This is the composition discipline paying off — shapes that would be special-cased constructs in other languages are *derivations* here, expressible by combining vectors, ranges, and rotation.
