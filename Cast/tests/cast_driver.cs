#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Cast.Lang;

int failures = 0;
void Check(string label, bool cond)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")} | {label}");
    if (!cond) failures++;
}

World NewWorld()
{
    var w = new World();
    var hero = new Entity { Id = "hero", Health = 100, MaxHealth = 100, Pos = new double[]{0,0,0} };
    hero.Tags.Add("player");
    w.Entities.Add(hero);
    w.Active = hero;
    return w;
}

Console.WriteLine("=== fire-and-forget (no trigger) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s cast Hurt[10]");      // runs once immediately
    Check("fire-and-forget runs once (100->90)", w.Entities[0].Health == 90);
}

Console.WriteLine();
Console.WriteLine("=== fire-and-forget with count ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s cast 3 Hurt[10]");    // runs 3x back-to-back
    Check("count 3 fires 3x (100->70)", w.Entities[0].Health == 70);
}

Console.WriteLine();
Console.WriteLine("=== level-triggered standing cast (no count) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // damage field: every tick the hero fulfills @e, hurt by 5
    ev.Run("cast @e Hurt[5]");
    Check("registered (no immediate fire, still 100)", w.Entities[0].Health == 100);
    ev.Tick();
    Check("tick 1 hurts (95)", w.Entities[0].Health == 95);
    ev.Tick();
    Check("tick 2 hurts again (90) — level fires every tick", w.Entities[0].Health == 90);
    ev.Tick();
    Check("tick 3 (85)", w.Entities[0].Health == 85);
}

Console.WriteLine();
Console.WriteLine("=== edge-triggered standing cast (count 1) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // tripwire: 1 hit on entry, then nothing while still inside
    ev.Run("cast 1 @e Hurt[5]");
    ev.Tick();
    Check("edge fires once on entry (95)", w.Entities[0].Health == 95);
    ev.Tick();
    Check("still inside -> no refire (95)", w.Entities[0].Health == 95);
    ev.Tick();
    Check("still inside -> still 95", w.Entities[0].Health == 95);
}

Console.WriteLine();
Console.WriteLine("=== edge re-entry is a fresh edge ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // filter by a tag the host can toggle; entity leaves the scope when tag removed
    w.Entities[0].Tags.Add("marked");
    ev.Run("cast 1 @e[in tags ? 'marked'] Hurt[5]");
    ev.Tick();
    Check("entry fires (95)", w.Entities[0].Health == 95);
    ev.Tick();
    Check("still inside, no refire (95)", w.Entities[0].Health == 95);
    w.Entities[0].Tags.Remove("marked");   // leaves the scope
    ev.Tick();
    Check("left scope, no fire (95)", w.Entities[0].Health == 95);
    w.Entities[0].Tags.Add("marked");       // re-enters
    ev.Tick();
    Check("re-entry is fresh edge, fires (90)", w.Entities[0].Health == 90);
}

Console.WriteLine();
Console.WriteLine("=== condition trigger (@v) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("cast 1 @v:boss_dead Hurt[5]");   // edge: fires once when condition becomes true
    ev.Tick();
    Check("condition false -> no fire (100)", w.Entities[0].Health == 100);
    ev.Run("@v:boss_dead = 1");
    ev.Tick();
    Check("condition true -> edge fires (95)", w.Entities[0].Health == 95);
    ev.Tick();
    Check("condition still true -> no refire (95)", w.Entities[0].Health == 95);
}

Console.WriteLine();
Console.WriteLine("=== over N frame-spread (fire-and-forget) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s cast 3 over 6 Hurt[10]");   // 3 firings spread over 6 frames -> frames 2,4,6
    Check("nothing yet (100)", w.Entities[0].Health == 100);
    ev.Tick(); // frame 1
    ev.Tick(); // frame 2 -> first
    Check("frame 2 first fire (90)", w.Entities[0].Health == 90);
    ev.Tick(); // 3
    ev.Tick(); // 4 -> second
    Check("frame 4 second fire (80)", w.Entities[0].Health == 80);
    ev.Tick(); // 5
    ev.Tick(); // 6 -> third
    Check("frame 6 third fire (70)", w.Entities[0].Health == 70);
}

Console.WriteLine();
Console.WriteLine("=== lifecycle: casts / uncast ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    var id = ev.Run("cast @e Hurt[5]");
    Check("cast returns an id", id is NumberValue);
    var list = ev.Run("casts");
    Check("casts lists 1 active", list is ArrayValue { Items.Count: 1 });
    ev.Tick();
    Check("active cast fires (95)", w.Entities[0].Health == 95);
    ev.Run($"uncast[{((NumberValue)id).N}]");
    var list2 = ev.Run("casts");
    Check("uncast removes it (0 active)", list2 is ArrayValue { Items.Count: 0 });
    ev.Tick();
    Check("removed cast no longer fires (95)", w.Entities[0].Health == 95);
}

Console.WriteLine();
Console.WriteLine("=== save exclusion: session-local in cast ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("$dmg = 5");
    ev.Run("cast @e Hurt[$dmg]");          // references $ -> excluded from save
    ev.Run("cast @e[in tags ? @v:mark] Hurt[5]"); // references @v -> saveable
    Check("2 active casts", ev.Casts.Active.Count == 2);
    Check("1 dropped on save (the $ one)", ev.Casts.DroppedOnSave == 1);
    Check("1 saveable (the @v one)", ev.Casts.Saveable.Count() == 1);
}

Console.WriteLine();
Console.WriteLine();
Console.WriteLine("=== standing cast firing a FUNCTION binds @s per match ===");
{
    // Regression: a cast whose action is a bare function name must invoke the
    // function with @s bound to each matched entity, so @s inside the function
    // body refers to the match (not lost across the call frame).
    var w = new World();
    var a = new Entity { Id = "a", Health = 100, MaxHealth = 100 }; a.Tags.Add("enemy");
    var b = new Entity { Id = "b", Health = 100, MaxHealth = 100 }; b.Tags.Add("enemy");
    var c = new Entity { Id = "c", Health = 100, MaxHealth = 100 }; // not an enemy
    w.Entities.Add(a); w.Entities.Add(b); w.Entities.Add(c);
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("Sting:: @s Hurt[5] ::");                       // function acts on @s
    ev.Run("cast @e[in tags ? 'enemy'] Sting");            // standing cast over enemies
    ev.Tick();                                             // fire once
    Check("cast-fired fn hurt enemy a (100->95)", a.Health == 95);
    Check("cast-fired fn hurt enemy b (100->95)", b.Health == 95);
    Check("cast-fired fn left non-enemy c (100)", c.Health == 100);
    ev.Tick();                                             // fires again (standing)
    Check("standing cast re-fires (a 95->90)", a.Health == 90);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
