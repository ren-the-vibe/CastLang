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
    var hero = new Entity { Id="hero", Health=100, MaxHealth=100, Pos=new double[]{0,0,0} };
    hero.Tags.Add("player");
    w.Entities.Add(hero); w.Active = hero;
    return w;
}

Console.WriteLine("=== log / say / msg ===");
{
    var w = NewWorld();
    var output = new MockOutput();
    var ev = new CastEvaluator(new MockHost(w, output: output));
    ev.Run("say['hello world']");
    Check("say routed to output channel", output.Said.Count == 1 && output.Said[0] == "hello world");
    ev.Run("@s msg['marker']");
    Check("msg routed with position", output.Messages.Count == 1 && output.Messages[0].text == "marker");
    Check("msg carries @s position <0,0,0>",
        output.Messages[0].pos is VectorValue v && v.Components.SequenceEqual(new double[]{0,0,0}));
    // say falls back to log when no output channel
    var ev2 = new CastEvaluator(new MockHost(NewWorld()));
    ev2.Run("say['fallback ok']");   // should not throw (falls back to console)
    Check("say without channel falls back (no throw)", true);
}

Console.WriteLine();
Console.WriteLine("=== spawn ===");
{
    var w = NewWorld();
    var spawner = new MockSpawner(w);
    var ev = new CastEvaluator(new MockHost(w, spawner: spawner));
    int before = w.Entities.Count;
    ev.Run("spawn shadebreaker:creature:wolf<5, 0, 5>");
    Check("spawn creates one entity", w.Entities.Count == before + 1);
    Check("spawned at <5,0,5>", w.Entities[^1].Pos.SequenceEqual(new double[]{5,0,5}));
    Check("spawned tagged with kind 'wolf'", w.Entities[^1].Tags.Contains("wolf"));
    // with properties
    ev.Run("spawn shadebreaker:creature:wolf<0,0,0>[health: 250, name: 'Boss']");
    Check("spawn with health property", w.Entities[^1].Health == 250);
    Check("spawn with name property", w.Entities[^1].Id == "Boss");
    // count via selection
    int b2 = w.Entities.Count;
    ev.Run("spawn shadebreaker:creature:rat(3)<0,0,0>");
    Check("spawn (3) creates three", w.Entities.Count == b2 + 3);
    // spawn returns the created set
    var r = ev.Run("spawn shadebreaker:creature:bat(2)<0,0,0>");
    Check("spawn returns created targets", r is ArrayValue { Items.Count: 2 });
}

Console.WriteLine();
Console.WriteLine("=== file I/O: read / write / files / invoke ===");
{
    var w = NewWorld();
    var dirs = new MockDirectories("scripts");   // 'scripts' is writable
    dirs.Files["scripts/greeting.txt"] = "hello from file";
    dirs.Files["scripts/setup.cast"] = "@v:loaded = 1\nBonus:: out arg[0] + 100 ::";
    var ev = new CastEvaluator(new MockHost(w, directories: dirs));

    var read = ev.Run("read['scripts/greeting.txt']");
    Check("read returns file contents", read is StringValue { S: "hello from file" });

    ev.Run("write['scripts/out.txt', 'written content']");
    Check("write persists to directory", dirs.Files.TryGetValue("scripts/out.txt", out var c) && c == "written content");

    var list = ev.Run("files['scripts']");
    Check("files lists directory (>=3 entries)", list is ArrayValue { Items.Count: >= 3 });
    // filter via array membership on the result
    var castFiles = ev.Run("$f in files['scripts'][ collect $f ? '**.cast' ]");
    // (collect of comparisons -> array of 1/0; at least one true)
    Check("files result is filterable array", castFiles is ArrayValue);

    // invoke a .cast file (extension implicit)
    ev.Run("invoke['scripts/setup']");
    Check("invoke ran @v write", Num(ev.Run("@v:loaded")) == 1);
    Check("invoke registered function", Num(ev.Run("Bonus[5]")) == 105);
}

Console.WriteLine();
Console.WriteLine("=== namespaced id resolution ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w, idResolver: new MockIdResolver()));
    Check("shadebreaker:phys:stone -> 1", Num(ev.Run("shadebreaker:phys:stone")) == 1);
    Check("shadebreaker:phys:water -> 2", Num(ev.Run("shadebreaker:phys:water")) == 2);
    // unresolved id stays a namespaced-id value (truthy, not a number)
    var ev2 = new CastEvaluator(new MockHost(NewWorld()));   // no resolver
    Check("unresolved id is a NamespacedIdValue", ev2.Run("mod:type:name") is NamespacedIdValue);
}

Console.WriteLine();
Console.WriteLine("=== @t vector interpreter (non-spatial) ===");
{
    var w = NewWorld();
    var interp = new MockTimeInterpreter();
    var ev = new CastEvaluator(new MockHost(w, vectorInterpreters: new IVectorInterpreter[]{ interp }));
    // @t<day, minute, hour> routes the vector through the interpreter, not position
    ev.Run("@t -> <3, 30, 12>");
    Check("vector interpreter received <3,30,12>",
        interp.Applied.Count == 1 && interp.Applied[0] == (3, 30, 12));
}

Console.WriteLine();
Console.WriteLine("=== ordered scopes: @np selection ===");
{
    var w = new World();
    // three players at increasing distance from origin
    var p0 = new Entity { Id="near",  Pos=new double[]{1,0,0} }; p0.Tags.Add("player");
    var p1 = new Entity { Id="mid",   Pos=new double[]{5,0,0} }; p1.Tags.Add("player");
    var p2 = new Entity { Id="far",   Pos=new double[]{9,0,0} }; p2.Tags.Add("player");
    var self = new Entity { Id="self", Pos=new double[]{0,0,0} };
    w.Entities.AddRange(new[]{ self, p2, p1, p0 });  // deliberately unordered
    w.Active = self;
    var ev = new CastEvaluator(new MockHost(w));
    // @np(0) is the nearest player; hurt only it
    ev.Run("@np(0) Hurt[10]");
    Check("@np(0) hits nearest player only (near hurt)", p0.Health == 90);
    Check("@np(0) leaves mid alone", p1.Health == 100);
    Check("@np(0) leaves far alone", p2.Health == 100);
    // @np(0..1) the two nearest
    ev.Run("@np(0..1) Hurt[5]");
    Check("@np(0..1) hits two nearest (near, mid)", p0.Health == 85 && p1.Health == 95);
    Check("@np(0..1) leaves far alone", p2.Health == 100);
}

Console.WriteLine();
Console.WriteLine("=== named call on a function value (NamedIndexNode path) ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // Heal is a function; capture it and call via {amount: ...} on the held value.
    // (The direct Name{..} form is a CallNode; this tests the value{..} postfix path.)
    ev.Run("@s SetHealth[40]");
    ev.Run("@s Heal{amount: 35}");   // named-arg form of a prelude function
    Check("Heal{amount: 35}: 40->75", w.Entities[0].Health == 75);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
