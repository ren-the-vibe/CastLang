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

// Evaluate a single-expression program and return the resulting CastValue.
CastValue E(string src)
{
    var ev = new CastEvaluator();
    return ev.Run(src);
}

double Num(CastValue v) => ((NumberValue)v).N;

Console.WriteLine("=== arithmetic & precedence ===");
Check("2 + 3 * 4 = 14", Num(E("2 + 3 * 4")) == 14);
Check("(2 + 3) * 4 = 20", Num(E("(2 + 3) * 4")) == 20);
Check("10 - 3 - 2 = 5 (left assoc)", Num(E("10 - 3 - 2")) == 5);
Check("#-7 = 7", Num(E("#-7")) == 7);
Check("#'hello' = 5", Num(E("#'hello'")) == 5);
Check("17 % 5 = 2", Num(E("17 % 5")) == 2);

Console.WriteLine();
Console.WriteLine("=== truthiness ===");
Check("0 is falsey", !E("0").IsTruthy);
Check("'' is falsey", !E("''").IsTruthy);
Check("[] is falsey", !E("[]").IsTruthy);
Check("_ is falsey", !E("_").IsTruthy);
Check("5 is truthy", E("5").IsTruthy);
Check("'x' is truthy", E("'x'").IsTruthy);
Check("<0,0,0> is falsey", !E("<0, 0, 0>").IsTruthy);
Check("<0,1,0> is truthy", E("<0, 1, 0>").IsTruthy);

Console.WriteLine();
Console.WriteLine("=== comparison modes ===");
Check("5 ? 5 equality true", E("5 ? 5").IsTruthy);
Check("5 ? 6 equality false", !E("5 ? 6").IsTruthy);
Check("3 ? 1..10 range true", E("3 ? 1..10").IsTruthy);
Check("15 ? 1..10 range false", !E("15 ? 1..10").IsTruthy);
Check("3 ? !1..10 complement false", !E("3 ? !1..10").IsTruthy);
Check("15 ? !1..10 complement true", E("15 ? !1..10").IsTruthy);
Check("[] type-witness on array", E("[1,2] ? []").IsTruthy);
Check("[] type-witness on number false", !E("5 ? []").IsTruthy);
Check("'abc' ? 'a**' glob true", E("'abc' ? 'a**'").IsTruthy);
Check("'abc' ? 'z**' glob false", !E("'abc' ? 'z**'").IsTruthy);
Check("5 !? 6 inequality true", E("5 !? 6").IsTruthy);
Check("5 !? 5 inequality false", !E("5 !? 5").IsTruthy);

Console.WriteLine();
Console.WriteLine("=== logic ===");
Check("1 && 1", E("1 && 1").IsTruthy);
Check("1 && 0 false", !E("1 && 0").IsTruthy);
Check("0 || 1", E("0 || 1").IsTruthy);
Check("1 !& 1 (nand) false", !E("1 !& 1").IsTruthy);
Check("0 !| 0 (nor) true", E("0 !| 0").IsTruthy);

Console.WriteLine();
Console.WriteLine("=== bindings ===");
{
    var ev = new CastEvaluator();
    ev.Run("$x = 5");
    Check("$x = 5 then read", Num(ev.Run("$x")) == 5);
    ev.Run("$x = $x + 3");
    Check("$x = $x + 3 -> 8", Num(ev.Run("$x")) == 8);
    ev.Run("$x += 2");
    Check("$x += 2 -> 10", Num(ev.Run("$x")) == 10);
    ev.Run("$x ++");
    Check("$x++ -> 11", Num(ev.Run("$x")) == 11);
}

Console.WriteLine();
Console.WriteLine("=== =& alias ===");
{
    var ev = new CastEvaluator();
    ev.Run("$a = 1");
    ev.Run("$b =& $a");      // fuse b onto a
    ev.Run("$a = 99");
    Check("alias: $b reflects $a", Num(ev.Run("$b")) == 99);
    ev.Run("$b = 7");
    Check("alias: write $b updates $a", Num(ev.Run("$a")) == 7);
}

Console.WriteLine();
Console.WriteLine("=== => lazy bind ===");
{
    var ev = new CastEvaluator();
    ev.Run("$base = 10");
    ev.Run("$live => $base + 1");   // lazy: re-reads $base each time
    Check("lazy initial = 11", Num(ev.Run("$live")) == 11);
    ev.Run("$base = 100");
    Check("lazy after change = 101", Num(ev.Run("$live")) == 101);
}

Console.WriteLine();
Console.WriteLine("=== @v registry ===");
{
    var ev = new CastEvaluator();
    ev.Run("@v:score:p1 = 50");
    Check("@v read", Num(ev.Run("@v:score:p1")) == 50);
    ev.Run("@v:score:p1 += 25");
    Check("@v compound", Num(ev.Run("@v:score:p1")) == 75);
    Check("registry has slot", ev.V.Has("score:p1"));
    ev.Run("@v:score:p1 = _");   // writing null clears
    Check("write null clears slot", !ev.V.Has("score:p1"));
}

Console.WriteLine();
Console.WriteLine("=== @t timers ===");
{
    var ev = new CastEvaluator();
    ev.Run("@t{'cooldown'} = -10");   // a timer (negative)
    Check("timer set negative", Num(ev.Run("@t{'cooldown'}")) == -10);
    ev.T.Tick(3);
    Check("timer ticks toward 0 (-7)", Num(ev.Run("@t{'cooldown'}")) == -7);
    ev.T.Tick(7);                     // crosses zero -> nulls
    Check("timer nulls on crossing", ev.Run("@t{'cooldown'}") is NullValue);

    ev.Run("@t{'kills'} = 0");        // a counter (zero-or-positive)
    ev.T.Tick(5);
    Check("counter counts up (5)", Num(ev.Run("@t{'kills'}")) == 5);

    // pause / speed
    ev.Run("@t{'slow'} = -100");
    ev.T.SetSpeed(new StringValue("slow"), 0.5);
    ev.T.Tick(10);                    // 10 * 0.5 = 5
    Check("per-slot speed (-95)", Num(ev.Run("@t{'slow'}")) == -95);
    ev.T.Pause(new StringValue("slow"));
    ev.T.Tick(10);
    Check("paused slot doesn't tick", Num(ev.Run("@t{'slow'}")) == -95);

    // null-on-crossing via arithmetic
    ev.Run("@t{'t2'} = -5");
    ev.Run("@t{'t2'} += 5");          // -5 -> 0 crosses -> null
    Check("arithmetic crossing nulls", ev.Run("@t{'t2'}") is NullValue);
    // abs of a timer
    ev.Run("@t{'t3'} = -8");
    Check("#@t{'t3'} = 8", Num(ev.Run("#@t{'t3'}")) == 8);
}

Console.WriteLine();
Console.WriteLine("=== functions ===");
{
    var ev = new CastEvaluator();
    ev.Run("Double:: out arg[0] * 2 ::");
    Check("positional arg fn", Num(ev.Run("Double[21]")) == 42);

    ev.Run("Add:: out arg[0] + arg[1] ::");
    Check("two positional args", Num(ev.Run("Add[3, 4]")) == 7);

    ev.Run("Named:: out param{'amount'} * 10 ::");
    Check("named param fn", Num(ev.Run("Named{amount: 5}")) == 50);

    // dual call form: a function that reads its arg via either positional or named.
    ev.Run("Flex:: $x = arg[0]; out $x * 2 ::");
    Check("Flex[21] = 42", Num(ev.Run("Flex[21]")) == 42);
}

Console.WriteLine();
Console.WriteLine("=== dual-call shape parses & runs positionally ===");
{
    var ev = new CastEvaluator();
    ev.Run("Greet:: out arg[0] ::");
    Check("Greet['hi'] returns 'hi'", ((StringValue)ev.Run("Greet['hi']")).S == "hi");
}

Console.WriteLine();
Console.WriteLine("=== iteration ===");
{
    var ev = new CastEvaluator();
    // sum 1..5 via collect + manual fold
    var r = ev.Run("$n in 1..5[ collect $n * $n ]");
    Check("collect squares -> array of 5", r is ArrayValue { Items.Count: 5 });
    if (r is ArrayValue av)
        Check("squares correct (1,4,9,16,25)",
            av.Items.Select(x => ((NumberValue)x).N).SequenceEqual(new double[]{1,4,9,16,25}));

    // iter index
    var r2 = ev.Run("$x in [10, 20, 30][ collect iter ]");
    if (r2 is ArrayValue av2)
        Check("iter is 0-based (0,1,2)",
            av2.Items.Select(x => ((NumberValue)x).N).SequenceEqual(new double[]{0,1,2}));

    // out exits early with a value
    var r3 = ev.Run("$x in [1,2,3,4][ $x ? 3 ?> out $x ]");
    Check("out returns first match (3)", r3 is NumberValue { N: 3 });
}

Console.WriteLine();
Console.WriteLine("=== membership ===");
Check("in [1,2,3] ? 2 true", E("in [1, 2, 3] ? 2").IsTruthy);
Check("in [1,2,3] ? 9 false", !E("in [1, 2, 3] ? 9").IsTruthy);

Console.WriteLine();
Console.WriteLine("=== vectors as wholes ===");
{
    Check("vector add <1,2,3>+<10,20,30>",
        E("<1, 2, 3> + <10, 20, 30>") is VectorValue v
        && v.Components.SequenceEqual(new double[]{11,22,33}));
    Check("#<3,4,0> = 5 (magnitude)", Num(E("#<3, 4, 0>")) == 5);
    Check("vector * scalar <1,2,3>*2",
        E("<1, 2, 3> * 2") is VectorValue v2
        && v2.Components.SequenceEqual(new double[]{2,4,6}));
}

Console.WriteLine();
Console.WriteLine("=== fallback ===");
{
    var ev = new CastEvaluator();
    // _ fallback in arithmetic-free context: a null coalesces? In Cast _value is the
    // fallback form; here we test that reading an unset @v with a fallback works.
    Check("@v unset read errors without fallback", ThrowsRuntime(() => ev.Run("@v:missing:key")));
}

bool ThrowsRuntime(Action a)
{
    try { a(); return false; } catch (CastRuntimeException) { return true; }
}

Console.WriteLine();
Console.WriteLine();
Console.WriteLine("=== bare function name as action invokes it ===");
{
    var ev = new CastEvaluator();
    // A bare function name in statement/action position must INVOKE the function
    // (zero args), not return the function value. (Regression: previously a no-op.)
    // Use @v for cross-call state, since function-locals don't leak to the caller.
    ev.Run("@v:counter = 0");
    ev.Run("Bump:: @v:counter = @v:counter + 1 ::");
    ev.Run("Bump");           // bare call as a statement
    ev.Run("Bump");
    Check("bare 'Bump' invoked twice -> counter 2", Num(ev.Run("@v:counter")) == 2);
    // as a conditional branch
    ev.Run("1 ? 1 ?> Bump");
    Check("bare fn in ?> branch invokes -> counter 3", Num(ev.Run("@v:counter")) == 3);
    ev.Run("1 ? 9 ?> Bump ?? Bump");  // else-branch (cond false -> else runs)
    Check("bare fn in ?? branch invokes -> counter 4", Num(ev.Run("@v:counter")) == 4);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL GREEN" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
