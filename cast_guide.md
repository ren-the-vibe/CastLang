# Cast — User Guide

This is a script writer's guide to Cast. For complete operator definitions, edge cases, and host integration details, see the language specification.

Cast is a command language for game runtimes. You type commands at a live console; each command *casts* an action over some target. Files use the `.cast` extension.

---

## 1. The console

Cast ships with a developer console — a panel embedded in whatever host runs it. You type commands, they execute, output appears.

```
log 'hello world'
```

Run it. The string appears in the console output. `log` is your debugging companion throughout — anything you want to inspect, send it to `log`.

To clear the console: `clear`.

---

## 2. Selecting things

Most Cast commands act on something. The thing is identified by a *scope* — a letter or two prefixed with `@`.

```
@s                  // yourself / the active target
@e                  // all entities
@p                  // all players
@np                 // nearest player
@n                  // nearest entity
@r                  // a random entity
@rp                 // a random player
@w                  // the world
@t                  // time
@v                  // the persistent variables registry
```

Scopes narrow with brackets and parens:

```
@np(0..2)                       // the three nearest players
@e<5, 0, 5>                     // entities at position (5, 0, 5)
@e<-5..5, 0..10, -5..5>         // entities in a region (vector with range components)
@e[health ? ..50]               // entities with health ≤ 50
@e[in tags ? 'boss']            // entities tagged 'boss'
@e<region>[health ? ..50]       // both: entities in region AND below 50 health
```

The chain order is always `@scope(selection)<region>[condition]`. Shortest goes first, longest last.

`@s` is special — it's "self," meaning whatever target the current command line is acting on. When you write `@np Heal`, inside `Heal`'s definition `@s` refers to that nearest player.

---

## 3. Doing things

Once you've selected something, attach a command. Commands either come from the standard library, from the host, or from functions you define yourself.

```
@s Kill                         // standard library: kill self
@np Heal[50]                    // heal nearest player by 50
@s.position -> <0, 0, 0>        // move to origin (direct, no function needed)
@e<region> Hurt[10]             // damage all entities in region by 10
@s -> @np                       // teleport self to nearest player (bare-scope sugar)
```

When a scope returns multiple things (like `@e[...]`), the command runs once per target. `@e<region> Hurt[10]` hurts each entity individually.

You can also issue built-in commands directly:

```
@s tag 'wounded'                // add a string tag to self
@s untag 'wounded'              // remove it
@e[in tags ? 'boss'] Hurt[100]  // hit all tagged-'boss' entities
say 'hello'                     // broadcast to players
@s msg 'I was here'             // drop a message at self's position
```

---

## 4. Values

Cast has a fixed set of value kinds, each with its own bracket or sigil:

**Numbers**: `5`, `3.14`, `-2`. Arithmetic works as expected: `+`, `-`, `*`, `/`, `%`.

**Strings**: single quotes. `'hello'`, `'a value: $health'` (variables interpolate inside strings). Embed a literal quote by doubling: `'don''t'`. Glob wildcard inside strings: `**`. `'Boss_**'` matches anything starting with `Boss_`.

**Vectors**: angle brackets. `<5, 0, 5>` is a 3-vector. Components can be ranges to make a region: `<-5..5, 0..10, -5..5>`. Component prefixes:
- `~5` — relative to current position
- `^5` — relative to facing direction
- `°90` — rotation (degrees around an axis)

So `<~5, ~0, ~0>` is "5 units east of where I am," and `°<0, 90, 0>` is "rotated 90 degrees on Y."

**Arrays**: square brackets. `[1, 2, 3]`. Index with `[N]`: `$arr[0]` is the first element.

**Maps**: curly braces. `{name: 'Bob', health: 100}`. Access by key: `$map{'name'}`. Map keys can be any value, not just strings.

**Ranges**: `..`. `2..5` is "two to five." `..50` is "up to 50." `5..` is "5 and up." Used for filtering, vector regions, scope slicing, character ranges in strings.

**Namespaced IDs**: `mod:type:name`. `shadebreaker:phys:stone` is a structured identifier for *kinds of things*. Used wherever you'd say "what kind is this?" — materials, item types, creature kinds.

---

## 5. Variables

`$name` is a variable.

```
$health = 100                   // bind
$health                         // read
$x = @s.health                  // bind to a value from a scope property
@s.health = $health + 10        // use it
```

Variables that look unbound error visibly. To make a read tolerant of missing values, use the `_` fallback marker:

```
$health _0                      // 0 if $health isn't bound
@s.armor _0                     // 0 if entity has no armor property
```

Three lifetimes:
- **Function-local** — `$x` inside a function body. Lives in the call frame, cleaned up on return.
- **Session-local** — `$x` at the prompt. Lives until the session ends.
- **Persistent** — `@v:name`. Lives across sessions in the persistent registry.

The persistent registry `@v` is a flat dictionary with namespace-style structured keys:

```
@v:score:player1 = 100
@v:score:player2 = 80
@v:boss:phase = 2
@v:spawn:default = <0, 10, 0>
```

`@v:score:player1` and `@v:score:player2` are independent slots — no hierarchy, no inheritance. The `:` separators are part of the key name. Query the registry like any scope:

```
@v                              // the whole registry
@v[name ? 'score:**']           // entries whose key starts with "score:"
#@v                             // count of slots
```

---

## 6. Conditionals

`?>` is "if-then," `??` is "else." Together they form a ternary:

```
@s.health ? ..50 ?> Heal[50] ?? log 'fine'
```

Reads: if self's health is in range `..50` (≤ 50), heal 50; otherwise log 'fine.'

The `?` operator is the comparator. It does several jobs depending on its right operand:

```
$x ? 5                          // equality
$x ? 2..5                       // numeric range membership
$name ? 'Boss_**'               // glob match
$pos ? <-5..5, 0..10, -5..5>    // region membership (component-wise)
$x ? []                         // type check: is $x an array?
$x ? {}                         // type check: is $x a map?
$x ? <>                         // type check: is $x a vector?
$x ? [_]                        // equality with the empty array specifically
```

For *containment* (is value X inside collection C), use `in`:

```
in $array ? 2                   // is 2 in $array?
in $map ? 'name'                // does $map have key 'name'?
in [1, 2, 3] ? $x               // is $x one of these values?
```

Logical composition: `&&`, `||`, `!`, `!&` (nand), `!|` (nor). All short-circuit. Inequality is `!?`.

Falsey values: `_` (null), `0`, empty string, empty array, empty map, all-zero vector. Everything else is truthy.

---

## 7. Loops

The `in` keyword also iterates. The bracket type tells the iteration what kind of collection it's walking.

```
$x in $array[ log $x ]                  // array
$v in $map{ log $v }                    // map values
$c in $position< log $c >               // vector components
$x in (1, 3..8, 12)[ log $x ]           // inline sequence
```

Inside the body, three names are available:
- `iter` — the current iteration count, zero-based
- `key` — the current entry's key (map iteration only)
- `collected` — the array of values you've appended via `collect`

Filtering happens via `?>` inside the body:

```
$x in $array[ $x ? 3.. ?> log $x ]              // log only elements ≥ 3
```

Loops are *expressions* — they produce a value. Either `out $value` to exit with one specific value, or `collect $value` repeatedly to build up a collection:

```
$first_match = $x in $array[ $x ? $target ?> out $x ]
// $first_match is the first element matching $target, or null

$scores = $x in $array[ collect $x.score ]
// $scores is an array of all scores
```

`out` exits the immediately enclosing layer. Inside a loop, it exits the loop. Inside a function (not within any loop), it exits the function.

---

## 8. Functions

Define a function with `Name:: body ::`.

```
HealAll::
    @e[health ? ..@s.max_health] Heal[50]
::
```

Call it the same way you'd call any standard function:

```
@s HealAll                      // run HealAll with @s as the subject
```

`@s` inside the body refers to whatever was on the left of the call.

Functions can take arguments two ways — positional or named:

```
@s Heal[50]                     // positional: 50 lands in arg[0]
@s Heal{amount: 50}             // named: 50 lands in param{"amount"}
```

Inside the body, `arg` is an array of positional args, `param` is a map of named args. You can support both call forms by aliasing:

```
Heal::
    $amount = arg[0] =& param{"amount"} ;
    @s.health = @s.health + $amount
::
```

The `=&` is a bidirectional alias — the two slots become one storage, so whichever the caller used, `$amount` reads correctly.

Return values are explicit via `out`:

```
Distance::
    $other = arg[0] =& param{"to"} ;
    out #(@s.position - $other.position)
::

$d = @s Distance[@np]
```

Functions without an `out` return null.

---

## 9. Execution context — `as` and `at`

Sometimes you need to act *as* one entity but *positioned at* another. `as` and `at` keywords let you split those contexts:

- `as @scope` — change *who* is acting (`@s` rebinds to that scope's target)
- `at @scope` — change *where* commands are positioned (relative coords resolve against that scope's position)

```
@e[in tags ? 'enemy'] at @np :: @s.position ~> @np.position * 0.1 ::
// each enemy, positioned-aware of nearest player, takes a step toward them
```

Canonical order is `as` before `at` — who first, then where.

`as` also doubles as a value-shape coercion when followed by a bracket form rather than a scope:

```
$map as []                              // view the map as an array
$array as {first: [0], second: [1]}     // view the array as a map with named indices
```

---

## 10. Time and scheduling — the `cast` command

`cast` is the language's namesake command — schedule a function to run with a temporal envelope:

```
@s cast HealRoutine                      // fire-and-forget (next tick, no wait)
@s cast @t<0,0,12> HealRoutine           // at hour 12
@s cast @t<~0,~5,~0> HealRoutine         // 5 minutes from now (relative time)
@s cast $boss_dead HealRoutine           // when condition becomes true
@s cast * 3 HealRoutine                  // repeat 3 times in sequence
@s cast / 5 SlowSpell                    // distribute one execution over 5 frames
@s cast * 3 / 5 Heal                     // 3 repeats × 5 frames each (15 total)
```

The envelope (`@t<...>`, `$condition`, `* N`, `/ N`) goes *before* the function name, so it modifies the cast rather than the function's args. If the function takes its own args, they follow the function name:

```
@s cast * 3 Heal[50]                     // repeat Heal[50] three times
```

---

## 11. Persistence — `save` and `load`

`save 'name'` serializes the language state (the `@v` registry plus your defined functions) to a host-managed storage slot. `load 'name'` restores it.

```
save 'before_boss'              // bookmark current state
load 'before_boss'              // restore it
qsave                           // save to the quick-slot
qload                           // load from the quick-slot
saves                           // list all save names (returns an array)
unsave 'before_boss'            // delete a save
```

Session-local `$` variables don't save. Only `@v` and function definitions cross the save boundary. If you want a value to persist, put it in `@v`.

---

## 12. Files and scripts

Cast can read and write files in directories the host has exposed.

```
read 'config.txt'                       // read a file as a string
invoke 'mylib'                          // load mylib.cast — its definitions become available
files 'scripts'                         // list filenames in a directory
files 'scripts'[name ? '**.cast']       // filtered to Cast files
write 'log.txt' 'session complete'      // write a string to a file
```

`invoke` runs a `.cast` file as if you'd typed its contents at the prompt — function definitions become callable, `@v` writes persist, `$` writes leak into the importing session. The `.cast` extension is implicit and can be omitted.

---

## 13. Global timers and counters — `@t[N]` and `@t{'name'}`

`@t` doubles as a timer/counter registry. Any index or name is a slot that automatically ticks up once per second.

The user writes the starting value directly; the sign determines the behavior:

```
@t[0] = -10                                 // timer: ticks -10 → ... → 0, then nulls
@t{'boss_cooldown'} = -30                   // 30-second timer
@t{'kills'} = 0                             // counter: ticks 0, 1, 2, ... forever
@t[12] = 500                                // counter already partway in — ticks from 500 up
```

Negative values count down toward 0 and null themselves when the runtime ticks them across 0. Zero or positive values count up indefinitely (until you manually clear them with `= _`).

**Reading:** `@t[0]` returns the current value (negative for timers, positive for counters, `_` if unset/expired). `#@t[0]` gives the absolute value — remaining time on a timer, elapsed time on a counter.

```
@t[0] ? _                                   // true if expired/unset
@t[0] ? ..0                                 // true if it's a timer (negative)
@t[0] ? 0..                                 // true if it's a counter
```

**Arithmetic:** `+=` advances (timer closer to expiry, counter further along); `-=` reverses. You can also write `= -10` to explicitly turn any slot back into a timer, or `= 50` to make it a counter at 50.

```
@t[0] += 5                                  // timer: 5 sec closer; counter: 5 sec further along
@t[0] -= 5                                  // timer: 5 more sec of life; counter: rewind 5
```

Arithmetic that crosses 0 from below nulls the slot too — `@t[0] += 100` on a timer at `-5` results in `_`, not `95`. The rule is "value transitioned from negative to 0-or-above," regardless of cause. This makes timers reliable: action-based decrement (subtract on every hit) still triggers expiry even if it overshoots 0.

**Control:** `.pause`, `.resume`, and `.speed` work both per-slot and on `@t` itself globally:

```
@t[0].pause                                 // halt one slot
@t[0].resume                                // restart that slot
@t[0].speed = 2                             // double-speed for that slot
@t[0] = _                                   // stop and clear

@t.pause                                    // pause everything (menus, cutscenes)
@t.speed = 0.5                              // slow-mo for all timers
@t.resume                                   // back to normal
```

A scheduled cast waiting on a timer (`cast SpawnBoss @t{'cooldown'} ? _`) keeps its schedule when the timer is paused — the condition just doesn't become true until the timer resumes and expires normally. Casts effectively pause with their condition timers.

All timer/counter state saves and loads with the rest of language state.

```
// Combo system: counter that resets if a window timer expires
@t{'combo'} = 0                             // counter for combo count
@t{'combo_window'} = -3                     // 3-second window
@t{'combo_window'} ? _ ?> @t{'combo'} = 0   // when window expires, reset combo

// Schedule something to fire when a timer expires
cast SpawnBoss @t{'boss_cooldown'} ? _
```

---

## 14. Spawning entities

The `spawn` command creates entities. Its grammar reuses the scope chain shape:

```
spawn shadebreaker:creature:wolf<5, 0, 5>                      // one wolf there
spawn shadebreaker:creature:wolf<5, 0, 5>[health: 100]         // with properties
spawn shadebreaker:creature:wolf(5)<region>                    // five in the region
spawn shadebreaker:creature:wolf(3..7)<region>[health: 100]    // 3-7 wolves with set health
```

A namespaced id says what kind, a vector (or region) says where, the bracket sets initial properties, the `(selection)` says how many.

---

## 15. The standard library

Cast ships canonical names for common operations. Hosts implement them, but the names are the same everywhere — scripts using only standard library work portably across hosts.

**Standard properties** (on entities, when the host exposes them):
- `health`, `max_health`
- `position`, `rotation`, `velocity`
- `name`, `id`, `tags`

**Standard functions on entities**: `Heal`, `Hurt`, `SetHealth`, `Kill`. Movement uses the `->` placement operator directly (`@s.position -> destination`).

**Standard math functions**: `Floor`, `Ceil`, `Round`, `Abs`, `Min`, `Max`, `Clamp`, `Sqrt`, `Pow`, `Sin`, `Cos`, `Atan2`. Called like any other function: `Sqrt[16]`, `Min[3, 7]`, `Clamp[$x, 0, 100]`.

For a complete reference of operators, edge cases, error semantics, and host integration, see the Cast language specification.
