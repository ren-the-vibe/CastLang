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
    var hero = new Entity { Id = "hero", Health = 100, MaxHealth = 100 };
    hero.Tags.Add("marked");
    w.Entities.Add(hero); w.Active = hero;
    return w;
}

Console.WriteLine("=== @v round-trip ===");
{
    var store = new MockPersistence();
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w, store));
    ev.Run("@v:score:p1 = 250");
    ev.Run("@v:spawn:default = <0, 10, 0>");
    ev.Run("save['game1']");

    // fresh evaluator, same store -> load
    var ev2 = new CastEvaluator(new MockHost(NewWorld(), store));
    ev2.Run("load['game1']");
    Check("@v:score:p1 restored (250)", Num(ev2.Run("@v:score:p1")) == 250);
    var spawn = ev2.Run("@v:spawn:default");
    Check("@v vector restored <0,10,0>",
        spawn is VectorValue v && v.Components.SequenceEqual(new double[]{0,10,0}));
}

Console.WriteLine();
Console.WriteLine("=== function round-trip ===");
{
    var store = new MockPersistence();
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w, store));
    ev.Run("Triple:: out arg[0] * 3 ::");
    ev.Run("save['g']");

    var ev2 = new CastEvaluator(new MockHost(NewWorld(), store));
    ev2.Run("load['g']");
    Check("user function restored (Triple[7]=21)", Num(ev2.Run("Triple[7]")) == 21);
}

Console.WriteLine();
Console.WriteLine("=== cast round-trip + session-local exclusion ===");
{
    var store = new MockPersistence();
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w, store));
    // a durable cast referencing @v survives; a $-referencing one is dropped
    ev.Run("@v:dmg = 5");
    ev.Run("cast 1 @e[in tags ? 'marked'] Hurt[3]");   // durable (no $)
    ev.Run("$x = 9");
    ev.Run("cast @e Hurt[$x]");                          // session-local -> dropped
    var dropped = ev.Run("save['g']");
    Check("save reports 1 dropped cast", Num(dropped) == 1);

    var w2 = NewWorld();
    var ev2 = new CastEvaluator(new MockHost(w2, store));
    ev2.Run("load['g']");
    Check("1 cast restored (the durable one)", ev2.Casts.Active.Count == 1);
    // the restored edge cast fires once on entry
    ev2.Tick();
    Check("restored cast fires (100->97)", w2.Entities[0].Health == 97);
}

Console.WriteLine();
Console.WriteLine("=== saves / unsave ===");
{
    var store = new MockPersistence();
    var ev = new CastEvaluator(new MockHost(NewWorld(), store));
    ev.Run("@v:a = 1");
    ev.Run("save['slot1']");
    ev.Run("save['slot2']");
    var list = ev.Run("saves");
    Check("saves lists 2", list is ArrayValue { Items.Count: 2 });
    ev.Run("unsave['slot1']");
    var list2 = ev.Run("saves");
    Check("unsave removes one (1 left)", list2 is ArrayValue { Items.Count: 1 });
}

Console.WriteLine();
Console.WriteLine("=== qsave / qload ===");
{
    var store = new MockPersistence();
    var ev = new CastEvaluator(new MockHost(NewWorld(), store));
    ev.Run("@v:quick = 42");
    ev.Run("qsave");
    var ev2 = new CastEvaluator(new MockHost(NewWorld(), store));
    ev2.Run("qload");
    Check("qsave/qload round-trips @v (42)", Num(ev2.Run("@v:quick")) == 42);
}

Console.WriteLine();
Console.WriteLine("=== no persistence provider errors visibly ===");
{
    var ev = new CastEvaluator(new MockHost(NewWorld(), null));
    bool threw = false;
    try { ev.Run("save['x']"); } catch (CastRuntimeException e) { threw = e.Message.Contains("persistence"); }
    Check("save without provider errors visibly", threw);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
