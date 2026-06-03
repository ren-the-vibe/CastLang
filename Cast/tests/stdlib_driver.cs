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
double Num(CastValue v) => ((NumberValue)v).N;

World NewWorld()
{
    var w = new World();
    var hero = new Entity { Id = "hero", Health = 50, MaxHealth = 100, Pos = new double[]{0,0,0} };
    w.Entities.Add(hero); w.Active = hero;
    return w;
}

Console.WriteLine("=== intrinsic math ===");
{
    var ev = new CastEvaluator();
    Check("Floor[3.7] = 3", Num(ev.Run("Floor[3.7]")) == 3);
    Check("Ceil[3.2] = 4", Num(ev.Run("Ceil[3.2]")) == 4);
    Check("Round[2.5] = 2 (to-even)", Num(ev.Run("Round[2.5]")) == 2);
    Check("Round[3.5] = 4 (to-even)", Num(ev.Run("Round[3.5]")) == 4);
    Check("Abs[-9] = 9", Num(ev.Run("Abs[-9]")) == 9);
    Check("Min[3, 7] = 3", Num(ev.Run("Min[3, 7]")) == 3);
    Check("Max[3, 7] = 7", Num(ev.Run("Max[3, 7]")) == 7);
    Check("Clamp[15, 0, 10] = 10", Num(ev.Run("Clamp[15, 0, 10]")) == 10);
    Check("Clamp[-5, 0, 10] = 0", Num(ev.Run("Clamp[-5, 0, 10]")) == 0);
    Check("Sqrt[16] = 4", Num(ev.Run("Sqrt[16]")) == 4);
    Check("Pow[2, 10] = 1024", Num(ev.Run("Pow[2, 10]")) == 1024);
    Check("Cos[0] = 1", Num(ev.Run("Cos[0]")) == 1);
}

Console.WriteLine();
Console.WriteLine("=== math composes with expressions ===");
{
    var ev = new CastEvaluator();
    Check("Min[Floor[3.9], 5] = 3", Num(ev.Run("Min[Floor[3.9], 5]")) == 3);
    Check("Clamp[Sqrt[100], 0, 8] = 8", Num(ev.Run("Clamp[Sqrt[100], 0, 8]")) == 8);
    Check("Max[1, 2] + Min[3, 4] = 5", Num(ev.Run("Max[1, 2] + Min[3, 4]")) == 5);
}

Console.WriteLine();
Console.WriteLine("=== prelude: Heal/Hurt/SetHealth (real Cast functions) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s Heal[20]");              // 50 + 20 = 70 (stdlib Heal: no cap)
    Check("Heal[20]: 50->70", w.Entities[0].Health == 70);
    ev.Run("@s Hurt[30]");              // 70 - 30 = 40
    Check("Hurt[30]: 70->40", w.Entities[0].Health == 40);
    ev.Run("@s SetHealth[100]");
    Check("SetHealth[100]: ->100", w.Entities[0].Health == 100);
}

Console.WriteLine();
Console.WriteLine("=== prelude: named-arg (dual call form) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // The =& dual-call form: Heal accepts arg[0] OR param{'amount'}
    ev.Run("@s Heal{amount: 25}");      // 50 + 25 = 75 via named arg
    Check("Heal{amount: 25}: 50->75", w.Entities[0].Health == 75);
}

Console.WriteLine();
Console.WriteLine("=== prelude over a scope (auto-iteration + per-@s) ===");
{
    var w = new World();
    var a = new Entity { Id="a", Health=10, MaxHealth=100 };
    var b = new Entity { Id="b", Health=20, MaxHealth=100 };
    w.Entities.Add(a); w.Entities.Add(b); w.Active = a;
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@e Heal[5]");               // each entity +5, @s rebinds per entity
    Check("entity a 10->15", a.Health == 15);
    Check("entity b 20->25", b.Health == 25);
}

Console.WriteLine();
Console.WriteLine("=== rng ===");
{
    var ev = new CastEvaluator();
    var r = ev.Run("rng");
    Check("bare rng in 0..1", r is NumberValue { N: >= 0 and <= 1 });
    var r2 = ev.Run("rng[1..10]");
    Check("rng[1..10] in range", r2 is NumberValue { N: >= 1 and <= 10 });
}

Console.WriteLine();
Console.WriteLine("=== def ===");
{
    var ev = new CastEvaluator();
    ev.Run("$known = 5");
    Check("def $known true", ev.Run("def[$known]").IsTruthy);
    Check("def $unknown false", !ev.Run("def[$missing]").IsTruthy);
}

Console.WriteLine();
Console.WriteLine("=== tag / untag ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    ev.Run("@s tag['boss']");
    Check("tag adds 'boss'", w.Entities[0].Tags.Contains("boss"));
    ev.Run("@s untag['boss']");
    Check("untag removes 'boss'", !w.Entities[0].Tags.Contains("boss"));
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
