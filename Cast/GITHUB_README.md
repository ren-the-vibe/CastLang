# Cast

A portable command language for game runtimes. Commands act over scopes: `@e[health ? ..50] Heal[50]` heals every entity under half health; `cast @e<region> Kill` registers a standing rule that kills anything entering a region. The language is engine-agnostic — you bridge it to your runtime through a host interface.

```
@e[in tags ? 'undead'] Hurt[10]              // hurt every undead
cast @t<0, 0, 12> as @e[id ? @v:bell] Ring   // ring the bell at noon, every day
@s.position -> <~, 10, ~>                     // set self's Y to 10, keep X and Z
@np ~> @s * 5                                 // nearest player steps toward self at speed 5
cast @e<region> Kill                          // anything entering the region dies
```

## Requirements

.NET 8. The implementation has no external dependencies, so the `Cast.Lang` sources drop into any .NET project (including Unity) as-is.

## Building

```bash
cd Cast
dotnet build src/Cast.Lang/Cast.Lang.csproj
```

Without NuGet access, `build.sh` compiles against the SDK's bundled reference assemblies and runs a test driver:

```bash
./build.sh tests/host_driver.cs    # build + run the end-to-end host tests
```

## Usage

Host-free (arithmetic, `@v` registry, `@t` timers, functions, iteration):

```csharp
using Cast.Lang;

var cast = new CastEvaluator();
cast.Run("$x = 2 + 3 * 4");          // 14
cast.Run("@v:score:p1 = 100");
cast.Run("Double:: out arg[0] * 2 ::");
var n = cast.Run("Double[21]");      // 42
```

With a host, to act on a world:

```csharp
var cast = new CastEvaluator(myHost);
cast.Run("@e[health ? ..50] Heal[50]");   // dispatches to your entities
cast.Run("cast @e<region> Kill");          // registers a standing cast
void Update() => cast.Tick();              // once per frame: drives standing casts and timers
```

## Integrating with your engine

Implement `IHost`. A `CastTarget` is an opaque handle — your own entity reference. Cast never inspects it; it only passes it back to your property adapter and command handlers.

```csharp
public interface IHost
{
    IReadOnlyList<IScopeHandler>      ScopeHandlers      { get; } // @e, @s, @n, @w, ...
    IPropertyAdapter                  Properties         { get; } // @s.health get/set
    IReadOnlyList<ICommandHandler>    CommandHandlers    { get; } // your verbs
    IIdResolver?                      IdResolver         { get; } // mod:type:name -> value
    IReadOnlyList<IVectorInterpreter> VectorInterpreters { get; } // @t <day,minute,hour>
    IPersistenceProvider?             Persistence        { get; } // save/load backend
    IOutputChannels?                  Output             { get; } // say / msg routing
    IDirectoryProvider?               Directories        { get; } // read/write/files/invoke
    ISpawner?                         Spawner            { get; } // spawn
    CastTarget? AmbientSelf        => null; // default @s (e.g. player)
}
```

`IdResolver`, `VectorInterpreters`, `Persistence`, `Output`, `Directories`, and `Spawner` are optional — leave them null and the features that need them (`save`/`load`, namespaced-id resolution, non-spatial vectors, `say`/`msg`, file I/O, `spawn`) error visibly or fall back to the log. The three you usually implement:

**Scope handlers** map a scope letter to its targets. The query carries narrowing args and a `Filter` predicate you call per candidate, so `[filter]` works without you knowing Cast's evaluation rules:

```csharp
class MyScopes : IScopeHandler
{
    readonly World _w;
    public MyScopes(World w) => _w = w;
    public bool Handles(string letters) => letters is "e" or "s" or "n";

    public IReadOnlyList<CastTarget> Resolve(ScopeQuery q)
    {
        if (q.Letters == "s")
            return q.Self is { } self ? new[] { self } : Array.Empty<CastTarget>();

        var targets = _w.Entities.Where(e => e.Alive).Select(e => new CastTarget(e));
        if (q.Filter is { } f) targets = targets.Where(t => f(t));   // apply [filter]
        return targets.ToList();
    }
}
```

**A property adapter** reads/writes dotted properties. Bind the standard names `health` and `position` and you get `Heal`, `Hurt`, `SetHealth`, and `->` placement for free — they're standard-library functions written in Cast against those names:

```csharp
class MyProps : IPropertyAdapter
{
    public bool TryGet(CastTarget t, string prop, out CastValue value)
    {
        value = CastValue.Null;
        if (t.Handle is not Entity e) return false;
        switch (prop)
        {
            case "health":   value = new NumberValue(e.Health); return true;
            case "position": value = new VectorValue(e.Pos);    return true;
            case "id":       value = new StringValue(e.Id);     return true;
            case "tags":     value = new ArrayValue(e.Tags.Select(s => (Value)new StringValue(s)).ToList()); return true;
            default: return false;   // unknown property -> Cast errors visibly
        }
    }
    public bool TrySet(CastTarget t, string prop, CastValue value)
    {
        if (t.Handle is not Entity e) return false;
        if (prop == "health")   { e.Health = ((NumberValue)value).N; return true; }
        if (prop == "position") { e.Pos = ((VectorValue)value).Components.ToArray(); return true; }
        return false;
    }
}
```

**Command handlers** register your verbs, dispatched by name over the active targets:

```csharp
class MyCommands : ICommandHandler
{
    public bool Handles(string name) => name is "Kill" or "Spawn";
    public CastValue Invoke(string name, IReadOnlyList<CastTarget> targets,
                        IReadOnlyList<CastValue> args, IReadOnlyDictionary<string, CastValue> named)
    {
        foreach (var t in targets)
            if (t.Handle is Entity e && name == "Kill") e.Alive = false;
        return CastValue.Null;
    }
}
```

Compose them into a host and construct the evaluator:

```csharp
class MyHost : IHost
{
    public MyHost(World w)
    {
        ScopeHandlers   = new[] { (IScopeHandler)new MyScopes(w) };
        Properties      = new MyProps();
        CommandHandlers = new[] { (ICommandHandler)new MyCommands() };
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

var cast = new CastEvaluator(new MyHost(world));
cast.Run("@e[in tags ? 'enemy'] Hurt[25]");
```

## Standing casts

A `cast` with a trigger scope is a standing subscription. Call `Tick()` once per frame; Cast polls each cast's scope-state and fires per the level/edge rules:

```csharp
cast.Run("cast @e<region> Hurt[5]");    // level: hurts each entity every tick inside
cast.Run("cast 1 @e<region> Hurt[20]"); // edge: one hit on entry; re-entry re-arms
```

## Unity

`Cast.Lang` is plain .NET 8 with no dependencies — drop the sources into a project. Your `CastTarget` handles wrap `GameObject`/`Component`/ECS-entity references; your property adapter reads `transform.position`, a health component, etc.; your scope handlers query the scene or your entity registry. `Tick()` goes in `Update` or a fixed step.

## Status

The language and its reference host interface are complete and tested: lexer, parser, evaluator (expressions, bindings, `@v`, `@t`, functions, iteration), host binding (scopes including ordered `@n`/`@r` and kind-letter composition, properties, commands, placement, the id resolver, vector interpreters), the cast subscription mechanism, the standard library, persistence, pipes, `spawn`, file I/O (`read`/`invoke`/`files`/`write`), and output routing (`say`/`msg`). Nine test suites, all green. The included mock host (`tests/mock_host.cs`) wires every interface piece and doubles as a worked integration reference.

## Documentation

- `cast_spec.md` — the complete language specification
- `cast_guide.md` — a script-writer's guide
- `cast_grammar.md` — the formal PEG grammar
- `Cast/README.md` — implementation status and internals

## License

TBD.
