#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Cast.Lang;

// ── tests ──────────────────────────────────────────────────────────────────

int failures = 0;
void Check(string label, bool cond)
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")} | {label}");
    if (!cond) failures++;
}

World NewWorld()
{
    var w = new World();
    var hero = new Entity { Id = "hero", Health = 50, MaxHealth = 100, Pos = new double[]{0,0,0} };
    hero.Tags.Add("player");
    var goblin = new Entity { Id = "goblin", Health = 30, MaxHealth = 30, Pos = new double[]{5,0,0} };
    goblin.Tags.Add("enemy");
    var orc = new Entity { Id = "orc", Health = 80, MaxHealth = 80, Pos = new double[]{10,0,0} };
    orc.Tags.Add("enemy");
    w.Entities.Add(hero); w.Entities.Add(goblin); w.Entities.Add(orc);
    w.Active = hero;
    return w;
}

Console.WriteLine("=== property read ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    Check("@s.health = 50", ((NumberValue)ev.Run("@s.health")).N == 50);
    Check("@s.id = 'hero'", ((StringValue)ev.Run("@s.id")).S == "hero");
}

Console.WriteLine();
Console.WriteLine("=== property write ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s.health = 75");
    Check("@s.health set to 75", w.Entities[0].Health == 75);
    ev.Run("@s.health = @s.health + 10");
    Check("@s.health += via expr -> 85", w.Entities[0].Health == 85);
}

Console.WriteLine();
Console.WriteLine("=== command over scope ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@e Heal[10]");
    Check("@e Heal[10] heals all living (hero 50->60)", w.Entities[0].Health == 60);
    Check("goblin 30->40 (stdlib Heal adds, no cap)", w.Entities[1].Health == 40);
    Check("orc 80->90 (stdlib Heal adds, no cap)", w.Entities[2].Health == 90);
}

Console.WriteLine();
Console.WriteLine("=== filtered command ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // hurt only low-health entities (health < 60): hero(50), goblin(30)
    ev.Run("@e[health ? ..59] Hurt[5]");
    Check("hero hurt (50->45)", w.Entities[0].Health == 45);
    Check("goblin hurt (30->25)", w.Entities[1].Health == 25);
    Check("orc untouched (80)", w.Entities[2].Health == 80);
}

Console.WriteLine();
Console.WriteLine("=== tag filter ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@e[in tags ? 'enemy'] Kill");
    Check("hero alive (player)", w.Entities[0].Alive);
    Check("goblin killed (enemy)", !w.Entities[1].Alive);
    Check("orc killed (enemy)", !w.Entities[2].Alive);
}

Console.WriteLine();
Console.WriteLine("=== placement ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s.position -> <10, 20, 30>");
    Check("placed at <10,20,30>", w.Entities[0].Pos.SequenceEqual(new double[]{10,20,30}));
    // keep X/Z, set Y to 5 using relative components
    ev.Run("@s.position -> <~, 5, ~>");
    Check("set Y keep X/Z (10,5,30)", w.Entities[0].Pos.SequenceEqual(new double[]{10,5,30}));
    // relative offset on X
    ev.Run("@s.position -> <~3, ~, ~>");
    Check("relative +3 on X (13,5,30)", w.Entities[0].Pos.SequenceEqual(new double[]{13,5,30}));
    // bare-scope sugar both sides: @s -> @n(0) is @s.position -> @n(0).position.
    // @n(0) is the single nearest entity (here the hero itself = self), so this is
    // a self-teleport; confirms the destination-scope-position path runs.
    ev.Run("@s.position -> <0, 0, 0>");
    ev.Run("@s -> @n(0)");
    Check("@s -> @n(0) runs (bare-scope sugar both sides)", w.Entities[0].Pos.SequenceEqual(new double[]{0,0,0}));
}

Console.WriteLine();
Console.WriteLine("=== bare-scope teleport across entities ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // place the goblin somewhere known, then teleport the hero to it via bare scopes.
    // @e[id ? 'goblin'] resolves to a single target; @s -> (that) reads its position.
    ev.Run("@e[id ? 'goblin'] @s.position -> <42, 0, 7>");  // (acts as goblin: set its pos)
    // now hero teleports to goblin's position with the terse form
    ev.Run("@s -> @e[id ? 'goblin']");
    Check("hero teleported to goblin via @s -> @e[...] (42,0,7)",
        w.Entities[0].Pos.SequenceEqual(new double[]{42,0,7}));
    // bare-scope teleport to a @v slot holding a vector
    ev.Run("@v:spawn:default = <3, 4, 5>");
    ev.Run("@s -> @v:spawn:default");
    Check("hero teleported to @v vector slot (3,4,5)",
        w.Entities[0].Pos.SequenceEqual(new double[]{3,4,5}));
}

Console.WriteLine();
Console.WriteLine("=== step toward (~>) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // hero at origin, step 5 toward <10,0,0> -> lands at <5,0,0>
    ev.Run("@s.position -> <0, 0, 0>");
    ev.Run("@s ~> <10, 0, 0> * 5");
    Check("step 5 toward <10,0,0> -> <5,0,0>", w.Entities[0].Pos.SequenceEqual(new double[]{5,0,0}));
}

Console.WriteLine();
Console.WriteLine("=== as redirect ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // act as enemies: heal them. hero stays.
    ev.Run("@w as @e[in tags ? 'enemy'] Heal[5]");
    Check("goblin healed via as (30->35)", w.Entities[1].Health == 35);
    Check("orc healed via as (80->85)", w.Entities[2].Health == 85);
    Check("hero not healed (50)", w.Entities[0].Health == 50);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
