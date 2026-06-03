# Bloodline Arena — a Cast integration scenario

A flat world full of creatures that wander, collide, breed, and die — driven by
Cast rules over a host that owns the physics. It exercises most of the language at
once: scope iteration and filtering, `spawn` with inherited ancestry, standing
`cast` subscriptions, the `say` output channel, bare-scope teleport (`@s -> @v:...`),
and membership-filter cursing.

## The world

A box `40 x 40` with a ceiling at `20`. The four walls and the ceiling are **death
planes**; the floor is solid. Creatures random-walk around the floor plane.

- **Collision → push + birth.** When two creatures overlap, the host pushes them
  apart and Cast spawns a child between them. The child's `ancestors` array is its
  own fresh lineage id plus the union of both parents' ancestor sets.
- **Death planes.** A creature pushed past a wall or the ceiling is flagged
  out-of-bounds; the standing rule `@e[in tags ? 'oob'] Kill` removes it.
- **The mage** (`@`) is a persistent creature, immune to the death planes, that
  also wanders. If a creature pushes the mage out of bounds, the mage teleports to
  the center (`@s -> @v:spawn:center`) and **curses that creature's bloodline
  succession** — itself and all its descendants, going forward.

## How the curse models "the creature and its descendants, going forward"

Each creature carries a unique `lineage` id, and its `ancestors` array contains its
own id plus every forebear's id. Cursing creature *C* raises a **standing** cast:

```
cast @e[in ancestors ? <C_lineage>] Curse      // Curse:: @s tag['cursed'] ::
```

Because descendants inherit *C*'s id in their `ancestors`, the filter matches *C*'s
entire subtree — and because the cast is standing (fires every tick), descendants
born *after* the cursing get tagged too. Siblings and ancestors of *C* are untouched
(they don't carry *C*'s own id). The curse does nothing but tag `cursed`.

## Division of labour

The **host** owns physics: movement, collision detection, the push response, and
the geometry of the death planes (it knows when a creature — or the mage — has been
pushed out). **Cast** owns the consequences: who dies at the boundary, the mage's
teleport-and-curse reaction, and the curse's effect. Births are issued by the host
calling `spawn` (it knows which pair collided and where), with the inherited ancestry
passed as a property — so the spawn grammar is driven from the host while the rules
themselves stay in `arena.cast`.

## Files

- `arena.cast` — the Cast rules (`DeathPlanes`, `MageCursed`, `Curse`)
- `arena_host.cs` — the world, physics-free host (properties, scopes, spawner with
  ancestry inheritance, Kill command, say channel)
- `run_arena.cs` — the physics loop + simulation driver, headless report, ASCII render

## Running

```bash
# build (from Cast/)
csc=$(find /usr/lib/dotnet/sdk -name csc.dll | head -1)
ref=$(find /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref -type d -name net8.0 | head -1)
dotnet "$csc" -nologo -target:exe -out:build/arena.dll \
  $(for d in "$ref"/*.dll; do echo -r:$d; done) \
  src/Cast.Lang/*.cs scenario/arena_host.cs scenario/run_arena.cs
cp scenario/arena.cast build/arena.cast

dotnet build/arena.dll                 # defaults: 12 creatures, 80 ticks, seed 3
dotnet build/arena.dll 12 80 3 --render   # with the ASCII world view
dotnet build/arena.dll 20 120 7           # custom run
```

Args: `[N creatures] [ticks] [seed] [--render]`.

## Sample (seed 3)

Seed 3 curses an early bloodline (id 187): 46 creatures tagged over the run, 28
still alive and cursed at the end — the curse visibly spreading through the subtree
(`x` glyphs in the render) while unrelated lineages (`o`) stay clean. A late cursing
(e.g. seed 42, bloodline 439) tags fewer, since the subtree has little time to grow
— which is the mechanic behaving correctly, not a bug.
