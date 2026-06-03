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
    w.Entities.Add(new Entity { Id="a", Health=10, MaxHealth=100 });
    w.Entities.Add(new Entity { Id="b", Health=20, MaxHealth=100 });
    w.Active = w.Entities[0];
    return w;
}

Console.WriteLine("=== |> directed write (source-first =) ===");
{
    var ev = new CastEvaluator();
    ev.Run("5 |> $x");
    Check("5 |> $x binds x=5", Num(ev.Run("$x")) == 5);
    // equivalent to $x = 5
    ev.Run("$y = 5");
    Check("|> matches = result", Num(ev.Run("$x")) == Num(ev.Run("$y")));
    // passthrough: the value is returned
    Check("|> returns the value", Num(ev.Run("7 |> $z")) == 7);
}

Console.WriteLine();
Console.WriteLine("=== |> into @v ===");
{
    var ev = new CastEvaluator();
    ev.Run("100 |> @v:score:p1");
    Check("|> writes @v slot", Num(ev.Run("@v:score:p1")) == 100);
}

Console.WriteLine();
Console.WriteLine("=== | pipe a value into a function (arg[0]) ===");
{
    var ev = new CastEvaluator();
    ev.Run("Double:: out arg[0] * 2 ::");
    Check("21 | Double = 42", Num(ev.Run("21 | Double")) == 42);
    // chain: 5 | Double | Double = 20
    ev.Run("Inc:: out arg[0] + 1 ::");
    Check("5 | Double | Inc = 11", Num(ev.Run("5 | Double | Inc")) == 11);
}

Console.WriteLine();
Console.WriteLine("=== | with explicit extra args (piped value is primary/arg[0]) ===");
{
    var ev = new CastEvaluator();
    ev.Run("Sub:: out arg[0] - arg[1] ::");
    // 10 | Sub[3]  ->  Sub[10, 3] = 7  (piped value is arg[0])
    Check("10 | Sub[3] = 7", Num(ev.Run("10 | Sub[3]")) == 7);
}

Console.WriteLine();
Console.WriteLine("=== | a set scope iterates the command ===");
{
    var w = NewWorld();
    var ev = new CastEvaluator(new MockHost(w));
    // @e is a set; pipe into Heal[5] -> runs per entity
    ev.Run("@e | Heal[5]");
    Check("entity a 10->15 (pipe iterates)", w.Entities[0].Health == 15);
    Check("entity b 20->25 (pipe iterates)", w.Entities[1].Health == 25);
}

Console.WriteLine();
Console.WriteLine("=== | with mid-chain |> capture ===");
{
    var ev = new CastEvaluator();
    ev.Run("Double:: out arg[0] * 2 ::");
    // 5 | Double |> $captured : double 5 to 10, capture into $captured
    ev.Run("5 | Double |> $captured");
    Check("mid-chain capture $captured = 10", Num(ev.Run("$captured")) == 10);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
