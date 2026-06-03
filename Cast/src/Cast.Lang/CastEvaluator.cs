#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cast.Lang;

// Control-flow signals used internally by the evaluator (out / collect).
internal sealed class OutSignal : Exception { public CastValue CastValue; public OutSignal(CastValue v) => CastValue = v; }

// The host-free core evaluator. It fully handles: expressions (arithmetic,
// comparison in all modes, logic, ranges, #magnitude, vectors as wholes,
// _ fallback), variable bindings (= / => / =&), the @v registry, @t timers,
// functions (definition/call/arg/param/out), iteration (in / collect / iter),
// conditionals, pipes, sequences/arrays/maps.
//
// Anything that requires the host (entity/world scopes @e @p @s @w @n @r, property
// get/set, world-acting commands, cast subscription firing) routes through
// NeedsHost(), which throws until a host is wired in a later layer.
public sealed class CastEvaluator
{
    public CastRegistry V { get; } = new();
    public CastTimers T { get; } = new();
    public CastRuntime Casts { get; } = new();
    private CastFrame _frame = new();
    private readonly Dictionary<string, FunctionValue> _functions = new();

    // Optional host. When null, host-dependent features throw via NeedsHost.
    private readonly IHost? _host;

    // The active acting target (@s) — a stack so `as @scope` can rebind per-target
    // during iteration and restore afterward.
    private readonly Stack<CastTarget> _self = new();
    private CastTarget? CurrentSelf => _self.Count > 0 ? _self.Peek() : _host?.AmbientSelf;

    public CastEvaluator(IHost? host = null)
    {
        _host = host;
        LoadPrelude();
    }

    // The standard-library entity-acting functions are defined in Cast itself (spec
    // §Standard library). They're loaded once at construction so any script can call
    // them. They read @s (the call-site subject) and use the standard property names
    // the host binds. A host that doesn't bind `health` sees these error at the
    // property-access site, which is correct.
    private const string Prelude = @"
Heal:: $amount = arg[0] =& param{'amount'} ; @s.health = @s.health + $amount ::
Hurt:: $amount = arg[0] =& param{'amount'} ; @s.health = @s.health - $amount ::
SetHealth:: $value = arg[0] =& param{'value'} ; @s.health = $value ::
";

    private void LoadPrelude()
    {
        // Parse and register the prelude function definitions only (no execution).
        var prog = new CastParser(Prelude).ParseProgram();
        foreach (var s in prog.Statements)
            if (s is FunctionDef fd) _functions[fd.Name] = new FunctionValue(fd);
    }

    // loop context for collect/iter
    private sealed class LoopCtx { public List<CastValue> Collected = new(); public double Iter; }
    private readonly Stack<LoopCtx> _loops = new();

    // ── public entry points ───────────────────────────────────────────────────

    public CastValue EvalProgram(ProgramNode p)
    {
        CastValue last = CastValue.Null;
        foreach (var s in p.Statements) last = ExecStatement(s);
        return last;
    }

    public CastValue Run(string source) => EvalProgram(new CastParser(source).ParseProgram());

    // ── statements ──────────────────────────────────────────────────────────────

    public CastValue ExecStatement(Node n)
    {
        switch (n)
        {
            case FunctionDef fd:
                _functions[fd.Name] = new FunctionValue(fd);
                return CastValue.Null;
            case OutNode o:
                throw new OutSignal(o.CastValue is null ? CastValue.Null : Eval(o.CastValue));
            case CollectNode c:
                {
                    var v = Eval(c.CastValue);
                    if (_loops.Count > 0) _loops.Peek().Collected.Add(v);
                    return v;
                }
            case IterationNode it:
                return EvalIteration(it);
            case CommandNode cmd:
                return EvalCommand(cmd);
            case CastNode cn:
                return EvalCast(cn);
            case SpawnNode sp:
                return EvalSpawn(sp);
            default:
                // statement position: a bare function name invokes (zero args)
                return EvalAction(n);
        }
    }

    // ── expressions ──────────────────────────────────────────────────────────────

    public CastValue Eval(Node n)
    {
        switch (n)
        {
            case InjectedValueNode iv: return iv.Value;
            case NumberNode num: return new NumberValue(num.CastValue);
            case StringNode s: return new StringValue(s.CastValue);
            case NullNode: return CastValue.Null;
            case IdentNode id: return EvalIdent(id.Name);
            case VarNode v: return ReadVar(v.Name);
            case NamespacedIdNode ns:
            {
                // Resolve through the host if it has an id resolver; otherwise the id
                // is itself the value (a literal handle, as for filter keys).
                if (_host?.IdResolver is { } r && r.TryResolve(ns.Segments, out var resolved))
                    return resolved;
                return new NamespacedIdValue(ns.Segments);
            }
            case FallbackNode: return CastValue.Null; // bare fallback with no failing op = null
            case GroupNode g: return Eval(g.Inner);
            case SequenceNode seq: return new SequenceValue(seq.Items.Select(Eval).ToList());
            case ArrayNode arr: return new ArrayValue(arr.Elements.Select(Eval).ToList());
            case MapNode map: return EvalMap(map);
            case VectorNode vec: return EvalVector(vec);
            case RangeNode r: return EvalRange(r);
            case UnaryNode u: return EvalUnary(u);
            case PostfixNode pf: return EvalPostfix(pf);
            case BinaryNode b: return EvalBinary(b);
            case ConditionalNode c: return EvalConditional(c);
            case PipeNode pn: return EvalPipe(pn);
            case MembershipNode m: return EvalMembership(m);
            case CallNode call: return EvalCall(call);
            case MemberNode mem: return EvalMember(mem);
            case IndexNode ix: return EvalIndex(ix);
            case NamedIndexNode ni:
            {
                // value{k: v} — only meaningful when the target is a function value
                // (a named call on a function held in a variable/expression). The
                // common Name{k:v} form is parsed directly as a CallNode.
                var tv = Eval(ni.CastTarget);
                if (tv is FunctionValue fv)
                {
                    var named = new List<PairNode>(ni.Pairs);
                    return EvalCall(new CallNode(fv.Def.Name, null, named));
                }
                return NeedsHost("named index on a non-function value");
            }
            case PlacementNode pl: return EvalPlacement(pl);
            case ScopeChain sc: return EvalScopeChain(sc);
            case CastNode cn: return EvalCast(cn);
            case SpawnNode sp: return EvalSpawn(sp);
            case FunctionDef fd: _functions[fd.Name] = new FunctionValue(fd); return CastValue.Null;
            default: throw new CastRuntimeException($"cannot evaluate node {n.GetType().Name}");
        }
    }

    // ── identifiers / variables ─────────────────────────────────────────────────

    private CastValue EvalIdent(string name)
    {
        // implicit loop var
        if (name == "iter" && _loops.Count > 0) return new NumberValue(_loops.Peek().Iter);
        if (name == "collected" && _loops.Count > 0)
            return new ArrayValue(_loops.Peek().Collected.ToList());
        // a known function referenced by name
        if (_functions.TryGetValue(name, out var fn)) return fn;
        // language-owned cast lifecycle built-in (no-arg form)
        if (name == "casts")
            return new ArrayValue(Casts.Active.Select(c => (CastValue)new NumberValue(c.Id)).ToList());
        // no-arg language built-ins reachable bare
        if (name == "rng") return new NumberValue(_rng.NextDouble());
        if (name is "clear" or "log" or "qsave" or "qload" or "saves")
            return DispatchHostCommand(new CallNode(name, null, null));
        // a bare identifier that names a host command is a no-arg command call
        if (_host is not null && _host.CommandHandlers.Any(h => h.Handles(name)))
            return DispatchHostCommand(new CallNode(name, null, null));
        // a bare property name resolves against the candidate under filter evaluation
        // (two-stage rule), else against the active @s.
        if (_host is not null)
        {
            var ctx = _testTarget.Count > 0 ? _testTarget.Peek() : CurrentSelf;
            if (ctx is { } target && _host.Properties.TryGet(target, name, out var v))
                return v;
        }
        return NeedsHost($"identifier '{name}'");
    }

    private CastValue ReadVar(string name)
    {
        var cell = _frame.Find(name);
        if (cell is null) throw new CastRuntimeException($"${name} is unbound");
        return cell.Read();
    }

    // ── binding operators (= => =& and compound) ─────────────────────────────────

    private CastValue EvalBinary(BinaryNode b)
    {
        switch (b.Op)
        {
            case "=": return EvalAssign(b.Left, Eval(b.Right));
            case "=>": return EvalLazyBind(b.Left, b.Right);
            case "=&": return EvalAlias(b.Left, b.Right);
            case "+=": case "-=": case "*=": case "/=": case "%=":
                return EvalCompound(b);
            case "|>": return EvalDirectedWrite(b.Left, b.Right);
        }

        // comparison / logic / arithmetic / range membership
        return b.Op switch
        {
            "+" or "-" or "*" or "/" or "%" => Arith(b.Op, Eval(b.Left), Eval(b.Right)),
            "?" => Compare(Eval(b.Left), b.Right),
            "!?" => new NumberValue(Compare(Eval(b.Left), b.Right).IsTruthy ? 0 : 1),
            "&&" => Bool(Eval(b.Left).IsTruthy && Eval(b.Right).IsTruthy),
            "||" => Bool(Eval(b.Left).IsTruthy || Eval(b.Right).IsTruthy),
            "!&" => Bool(!(Eval(b.Left).IsTruthy && Eval(b.Right).IsTruthy)),
            "!|" => Bool(!(Eval(b.Left).IsTruthy || Eval(b.Right).IsTruthy)),
            _ => throw new CastRuntimeException($"unknown operator {b.Op}")
        };
    }

    private CastValue EvalAssign(Node target, CastValue value)
    {
        switch (target)
        {
            case VarNode v:
                _frame.Declare(v.Name).Write(value);
                return value;
            case ScopeChain sc when sc.Sigil == "@v":
                V.Write(VKey(sc), value);
                return value;
            case IndexNode { CastTarget: ScopeChain { Sigil: "@t" }, Args: var args }:
                T.Set(Eval(args[0]), AsNum(value));
                return value;
            case MemberNode mem:
                {
                    // Fast path: bare @s.prop write straight to CurrentSelf.
                    if (mem.CastTarget is ScopeChain { Sigil: "@s", Selection: null, Region: null, Filter: null }
                        && _host is not null && CurrentSelf is { } self)
                    {
                        if (_host.Properties.TrySet(self, mem.Member, value)) return value;
                        throw new CastRuntimeException($"property '{mem.Member}' not settable");
                    }
                    var t = Eval(mem.CastTarget);
                    if (TryAsSingleTarget(t, out var tgt) && _host is not null)
                    {
                        if (_host.Properties.TrySet(tgt, mem.Member, value)) return value;
                        throw new CastRuntimeException($"property '{mem.Member}' not settable");
                    }
                    return NeedsHost("assignment to host property");
                }
            case NamedIndexNode:
            case IndexNode:
                return NeedsHost("assignment to host property");
            default:
                throw new CastRuntimeException("invalid assignment target");
        }
    }

    private CastValue EvalCompound(BinaryNode b)
    {
        double delta = AsNum(Eval(b.Right));
        string op = b.Op[..1];
        // @t slot compound
        if (b.Left is IndexNode { CastTarget: ScopeChain { Sigil: "@t" }, Args: var targs })
        {
            var idx = Eval(targs[0]);
            if (op == "+") { T.Add(idx, delta); return T.Read(idx); }
            if (op == "-") { T.Add(idx, -delta); return T.Read(idx); }
            // * / % on a timer: read, apply, set (honors crossing rule via Set)
            var cur = T.Read(idx);
            double r = ApplyArith(op, AsNum(cur), delta);
            T.Set(idx, r); return T.Read(idx);
        }
        // $var compound
        if (b.Left is VarNode v)
        {
            var cell = _frame.Find(v.Name) ?? _frame.Declare(v.Name);
            double r = ApplyArith(op, AsNum(cell.Read()), delta);
            cell.Write(new NumberValue(r));
            return cell.Read();
        }
        // @v slot compound
        if (b.Left is ScopeChain { Sigil: "@v" } sc)
        {
            string key = VKey(sc);
            double r = ApplyArith(op, AsNum(V.ReadOr(key, new NumberValue(0))), delta);
            V.Write(key, new NumberValue(r));
            return new NumberValue(r);
        }
        return NeedsHost("compound assignment to host property");
    }

    // `value |> $var`: the source-first form of `=`. Writes the flowing value into
    // the variable AND returns it, so it keeps flowing down a `|` chain (passthrough).
    private CastValue EvalDirectedWrite(Node sourceNode, Node destNode)
    {
        var value = Eval(sourceNode);
        if (destNode is VarNode v) { _frame.Declare(v.Name).Write(value); return value; }
        if (destNode is ScopeChain { Sigil: "@v" } sc) { V.Write(VKey(sc), value); return value; }
        throw new CastRuntimeException("|> requires a writable variable on the right");
    }

    private CastValue EvalLazyBind(Node target, Node expr)
    {
        if (target is VarNode v)
        {
            var cell = _frame.Declare(v.Name);
            cell.Eager = null;
            cell.Lazy = () => Eval(expr);
            return cell.Read();
        }
        throw new CastRuntimeException("=> requires a variable target");
    }

    private CastValue EvalAlias(Node left, Node right)
    {
        // Variable-to-variable alias: fuse cells so both names share storage.
        if (left is VarNode lv && right is VarNode rv)
        {
            var rc = _frame.Find(rv.Name) ?? _frame.Declare(rv.Name);
            _frame.BindCell(lv.Name, rc);
            return rc.Read();
        }

        // Dual-call idiom: arg[i] =& param{'key'} (in either order) unifies the
        // positional and named views of one argument. The caller supplies exactly
        // one of them; the alias resolves to whichever is present. This is how a
        // function body accepts both Heal[50] and Heal{amount: 50} uniformly.
        if (TryArgParamSlot(left, out var lVal, out var lOk) &&
            TryArgParamSlot(right, out var rVal, out var rOk))
        {
            if (lOk) return lVal;
            if (rOk) return rVal;
            throw new CastRuntimeException("dual-call argument supplied neither positionally nor by name");
        }

        throw new CastRuntimeException("=& requires storage locations on both sides");
    }

    // Resolve an arg[i] or param{'key'} access without throwing on absence: returns
    // ok=false if the slot wasn't supplied by the caller.
    private bool TryArgParamSlot(Node node, out CastValue value, out bool ok)
    {
        value = CastValue.Null; ok = false;
        // arg[i]  (parsed as CallNode "arg" with a single positional index)
        if (node is CallNode { Name: "arg" } ac)
        {
            if (ReadVarOrNull("arg") is ArrayValue arr)
            {
                if (ac.PositionalArgs is { Count: 1 } aargs)
                {
                    int i = (int)AsNum(Eval(aargs[0]));
                    if (i >= 0 && i < arr.Items.Count) { value = arr.Items[i]; ok = true; }
                }
            }
            return true;
        }
        // param{'key'}  (parsed as CallNode "param" with a single positional key)
        if (node is CallNode { Name: "param" } pc)
        {
            if (ReadVarOrNull("param") is MapValue map)
            {
                CastValue? key = null;
                if (pc.PositionalArgs is { Count: 1 }) key = EvalKey(pc.PositionalArgs[0]);
                else if (pc.NamedArgs is { Count: 1 }) key = EvalKey(pc.NamedArgs[0].Key);
                if (key is not null && map.Entries.Any(e => ValueEquality.Equal(e.Key, key)))
                { value = map.Entries.First(e => ValueEquality.Equal(e.Key, key)).Value; ok = true; }
            }
            return true;
        }
        return false;
    }

    // ── arithmetic ────────────────────────────────────────────────────────────────

    private static double ApplyArith(string op, double a, double b) => op switch
    {
        "+" => a + b, "-" => a - b, "*" => a * b,
        "/" => a / b, "%" => a % b,
        _ => throw new CastRuntimeException($"bad arith {op}")
    };

    private CastValue Arith(string op, CastValue a, CastValue b)
    {
        // vectors operate as wholes
        if (a is VectorValue va && b is VectorValue vb)
        {
            if (va.Arity != vb.Arity) throw new CastRuntimeException("vector arity mismatch");
            var comps = va.Components.Zip(vb.Components, (x, y) => ApplyArith(op, x, y)).ToList();
            return new VectorValue(comps);
        }
        if (a is VectorValue v1 && b is NumberValue s1) // vector * scalar etc.
            return new VectorValue(v1.Components.Select(x => ApplyArith(op, x, s1.N)).ToList());
        if (a is NumberValue s2 && b is VectorValue v2)
            return new VectorValue(v2.Components.Select(x => ApplyArith(op, s2.N, x)).ToList());
        // string concatenation with +
        if (op == "+" && (a is StringValue || b is StringValue))
            return new StringValue(Stringify(a) + Stringify(b));
        return new NumberValue(ApplyArith(op, AsNum(a), AsNum(b)));
    }

    // ── comparison (the ? modes) ──────────────────────────────────────────────────
    // equality | numeric-range membership | char range | glob | region membership |
    // type check (empty bracket) | empty-value equality | structural equality.
    private CastValue Compare(CastValue left, Node rightNode)
    {
        // Type-witness: right is an empty bracket literal → "is left this kind?"
        if (rightNode is ArrayNode { Elements.Count: 0 }) return Bool(left is ArrayValue);
        if (rightNode is MapNode { Pairs.Count: 0 }) return Bool(left is MapValue);
        if (rightNode is SequenceNode { Items.Count: 0 }) return Bool(left is SequenceValue);
        if (rightNode is VectorNode { Shorthand: VectorShorthand.None, Components.Count: 0 })
            return Bool(left is VectorValue);

        var right = Eval(rightNode);

        // range membership
        if (right is RangeValue rng)
        {
            if (left is NumberValue ln) return Bool(rng.Contains(ln.N));
            // char range: single-char strings
            if (left is StringValue ls && ls.S.Length == 1) return Bool(rng.Contains(ls.S[0]));
            return Bool(false);
        }

        // glob string match: right is a string containing '**'
        if (right is StringValue rs && rs.S.Contains("**") && left is StringValue lstr)
            return Bool(GlobMatch(lstr.S, rs.S));

        // region membership: right is a vector with range components, left a vector
        if (right is VectorValue rv && left is VectorValue lv && HasRangeRegion(rightNode))
            return Bool(RegionContains(rv, lv)); // (rv carries plain numbers; see note)

        // structural / scalar equality
        return Bool(ValueEquality.Equal(left, right));
    }

    // Region membership needs the unevaluated vector to see range components.
    private bool HasRangeRegion(Node rightNode) =>
        rightNode is VectorNode vn && vn.Components.Any(c => c is RangeNode);

    private bool RegionContains(VectorValue region, VectorValue point)
    {
        // NOTE: in the host-free core, a region built purely from ranges is compared
        // component-wise; this path is exercised once vectors carry range components.
        // Placeholder: equal-arity required.
        return region.Arity == point.Arity;
    }

    private static bool GlobMatch(string s, string pattern)
    {
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                     .Replace("\\*\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(s, rx);
    }

    // ── unary / postfix ────────────────────────────────────────────────────────────

    private CastValue EvalUnary(UnaryNode u)
    {
        if (u.Op == "#") return Magnitude(Eval(u.Operand));
        if (u.Op == "!") return Bool(!Eval(u.Operand).IsTruthy);
        if (u.Op == "-") return new NumberValue(-AsNum(Eval(u.Operand)));
        // ~ ^ ° as standalone unary prefixes only meaningful in vector context/host
        return NeedsHost($"unary '{u.Op}' outside vector/host context");
    }

    private CastValue Magnitude(CastValue v) => v switch
    {
        NumberValue n => new NumberValue(Math.Abs(n.N)),
        StringValue s => new NumberValue(s.S.Length),
        ArrayValue a => new NumberValue(a.Items.Count),
        SequenceValue sq => new NumberValue(sq.Items.Count),
        MapValue m => new NumberValue(m.Entries.Count),
        VectorValue vec => new NumberValue(Math.Sqrt(vec.Components.Sum(c => c * c))),
        _ => throw new CastRuntimeException($"# undefined for {v.GetType().Name}")
    };

    private CastValue EvalPostfix(PostfixNode pf)
    {
        // ++ / -- on a $var or @t/@v slot
        double delta = pf.Op == "++" ? 1 : -1;
        if (pf.Operand is VarNode v)
        {
            var cell = _frame.Find(v.Name) ?? _frame.Declare(v.Name);
            double old = AsNum(cell.Read());
            cell.Write(new NumberValue(old + delta));
            return new NumberValue(old); // postfix returns prior value
        }
        if (pf.Operand is IndexNode { CastTarget: ScopeChain { Sigil: "@t" }, Args: var a })
        {
            var idx = Eval(a[0]); var old = T.Read(idx);
            T.Add(idx, delta); return old;
        }
        if (pf.Operand is ScopeChain { Sigil: "@v" } sc)
        {
            string key = VKey(sc); double old = AsNum(V.ReadOr(key, new NumberValue(0)));
            V.Write(key, new NumberValue(old + delta)); return new NumberValue(old);
        }
        return NeedsHost("postfix on host property");
    }

    // ── ranges / vectors / maps ──────────────────────────────────────────────────

    private CastValue EvalRange(RangeNode r)
    {
        double? lo = r.Low is null ? null : AsNum(Eval(r.Low));
        double? hi = r.High is null ? null : AsNum(Eval(r.High));
        return new RangeValue(lo, hi, r.Complement);
    }

    private CastValue EvalVector(VectorNode vec)
    {
        switch (vec.Shorthand)
        {
            case VectorShorthand.Empty: return new VectorValue(Array.Empty<double>());
            case VectorShorthand.AllOpen:
            case VectorShorthand.AllRelative:
                // these are placement/region shorthands; need host position context
                return NeedsHost("vector shorthand <..> / <~> needs position context");
        }
        // plain numeric vector; prefixed/relative/null components need host context
        var comps = new List<double>();
        foreach (var c in vec.Components)
        {
            if (c is NumberNode nn) comps.Add(nn.CastValue);
            else if (c is UnaryNode { Op: "-" } un && un.Operand is NumberNode n2) comps.Add(-n2.CastValue);
            else if (c is PrefixedComponentNode || c is NullNode || c is RangeNode)
                return NeedsHost("relative/null/range vector component needs context");
            else comps.Add(AsNum(Eval(c)));
        }
        return new VectorValue(comps);
    }

    private CastValue EvalMap(MapNode map)
    {
        var d = new ValueKeyDictionary();
        foreach (var p in map.Pairs)
            d[EvalKey(p.Key)] = Eval(p.Value);
        return new MapValue(d);
    }

    // A map/named-arg key written as a bare identifier is a literal key name (a
    // symbol), not a variable reference. Strings and other expressions evaluate
    // normally.
    private CastValue EvalKey(Node key) => key switch
    {
        IdentNode id => new StringValue(id.Name),
        _ => Eval(key)
    };

    // ── conditional / membership ─────────────────────────────────────────────────

    private CastValue EvalConditional(ConditionalNode c)
    {
        if (Eval(c.Condition).IsTruthy) return EvalAction(c.Then);
        if (c.Else is not null) return EvalAction(c.Else);
        return CastValue.Null;
    }

    private CastValue EvalMembership(MembershipNode m)
    {
        var coll = Eval(m.Collection);
        var item = Eval(m.Tested);
        return coll switch
        {
            ArrayValue a => Bool(a.Items.Any(x => ValueEquality.Equal(x, item))),
            SequenceValue s => Bool(s.Items.Any(x => ValueEquality.Equal(x, item))),
            MapValue mp => Bool(mp.Entries.Keys.Any(k => ValueEquality.Equal(k, item))),
            VectorValue v => Bool(item is NumberValue nv && v.Components.Contains(nv.N)),
            _ => Bool(false)
        };
    }

    // ── iteration ────────────────────────────────────────────────────────────────

    private CastValue EvalIteration(IterationNode it)
    {
        var coll = Eval(it.Collection);
        IEnumerable<CastValue> items = coll switch
        {
            ArrayValue a => a.Items,
            SequenceValue s => s.Items,
            MapValue m => m.Entries.Values,
            RangeValue r => RangeItems(r),
            VectorValue v => v.Components.Select(c => (CastValue)new NumberValue(c)),
            _ => throw new CastRuntimeException("cannot iterate this value")
        };

        var ctx = new LoopCtx();
        _loops.Push(ctx);
        try
        {
            double i = 0;
            foreach (var item in items)
            {
                ctx.Iter = i++;
                _frame.Declare(it.VarName).Write(item);
                foreach (var stmt in it.Body)
                {
                    try { ExecStatement(stmt); }
                    catch (OutSignal sig) { return sig.CastValue; }
                }
            }
            return new ArrayValue(ctx.Collected.ToList());
        }
        finally { _loops.Pop(); }
    }

    private static IEnumerable<CastValue> RangeItems(RangeValue r)
    {
        if (r.Low is null || r.High is null) throw new CastRuntimeException("cannot iterate an open range");
        for (double x = r.Low.Value; x <= r.High.Value; x++) yield return new NumberValue(x);
    }

    // ── functions ────────────────────────────────────────────────────────────────

    private CastValue EvalCall(CallNode call)
    {
        // 'arg' and 'param' are the implicit argument bindings inside a function
        // body, not callable functions. arg[i] indexes the positional array;
        // param{k} / param['k'] looks up the named map.
        if (call.Name == "arg")
        {
            var arr = ReadVarOrNull("arg") as ArrayValue
                      ?? throw new CastRuntimeException("'arg' is only valid in a function body");
            if (call.PositionalArgs is { Count: 1 })
            {
                int i = (int)AsNum(Eval(call.PositionalArgs[0]));
                if (i < 0 || i >= arr.Items.Count) throw new CastRuntimeException("arg index out of range");
                return arr.Items[i];
            }
            return arr; // bare 'arg' -> whole positional array
        }
        if (call.Name == "param")
        {
            var map = ReadVarOrNull("param") as MapValue
                      ?? throw new CastRuntimeException("'param' is only valid in a function body");
            // param{k} arrives as NamedArgs (k:v) OR as PositionalArgs (a single key)
            CastValue? key = null;
            if (call.PositionalArgs is { Count: 1 }) key = EvalKey(call.PositionalArgs[0]);
            else if (call.NamedArgs is { Count: 1 }) key = EvalKey(call.NamedArgs[0].Key);
            if (key is null) return map;
            var hit = map.Entries.FirstOrDefault(e => ValueEquality.Equal(e.Key, key));
            if (map.Entries.Any(e => ValueEquality.Equal(e.Key, key))) return hit.Value;
            throw new CastRuntimeException("param key not present");
        }

        if (!_functions.TryGetValue(call.Name, out var fn))
            return DispatchHostCommand(call);

        var callee = new CastFrame(_frame);
        bool hasPos = call.PositionalArgs is { Count: > 0 };
        bool hasNamed = call.NamedArgs is { Count: > 0 };
        // Bind arg[]/param{}. For the common zero-arg call we reuse shared empty
        // singletons instead of allocating a fresh ArrayValue + MapValue + dictionary
        // every call. arg/param are always present in the frame so `arg`/`param` in
        // the body resolve correctly (to empty) rather than reading a stale outer one.
        CastValue argVal, paramVal;
        if (hasPos)
            argVal = new ArrayValue(call.PositionalArgs!.Select(Eval).ToList());
        else
            argVal = EmptyArrayValue;
        if (hasNamed)
        {
            var named = new ValueKeyDictionary();
            foreach (var p in call.NamedArgs!) named[EvalKey(p.Key)] = Eval(p.Value);
            paramVal = new MapValue(named);
        }
        else paramVal = EmptyMapValue;
        callee.Declare("arg").Write(argVal);
        callee.Declare("param").Write(paramVal);

        var prev = _frame; _frame = callee;
        try
        {
            CastValue result = CastValue.Null;
            foreach (var stmt in fn.Def.Body)
            {
                try { result = ExecStatement(stmt); }
                catch (OutSignal sig) { return sig.CastValue; }
            }
            return result;
        }
        finally { _frame = prev; }
    }

    private static readonly ArrayValue EmptyArrayValue = new(new List<CastValue>());
    private static readonly MapValue EmptyMapValue = new(new ValueKeyDictionary());
    private static readonly CastValue[] EmptyArgList = Array.Empty<CastValue>();

    private CastValue? ReadVarOrNull(string name)
    {
        var cell = _frame.Find(name);
        return cell?.Read();
    }

    // Dispatch a non-function call to: language built-ins, intrinsic math, then host.
    private CastValue DispatchHostCommand(CallNode call)
    {
        // Intrinsic standard-library math first — this is the hot path (Clamp/Min/etc.
        // called in tight per-entity loops), so it shouldn't pay for the uncast/casts
        // string comparisons below.
        if (Intrinsics.TryGetValue(call.Name, out var intrinsic))
        {
            var pa = call.PositionalArgs;
            if (pa is null || pa.Count == 0) return intrinsic(EmptyArgList);
            var a = new CastValue[pa.Count];
            for (int i = 0; i < pa.Count; i++) a[i] = Eval(pa[i]);
            return intrinsic(a);
        }

        // Language-owned cast lifecycle built-ins.
        if (call.Name == "uncast")
        {
            var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
            if (a.Count == 1 && a[0] is NumberValue idn) return Bool(Casts.Uncast((int)idn.N));
            throw new CastRuntimeException("uncast expects a cast id");
        }
        if (call.Name == "casts")
            return new ArrayValue(Casts.Active.Select(c => (CastValue)new NumberValue(c.Id)).ToList());

        // Language built-ins.
        switch (call.Name)
        {
            case "rng":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 0) return new NumberValue(_rng.NextDouble());
                if (a.Count == 1 && a[0] is RangeValue { Low: { } lo, High: { } hi })
                    return new NumberValue(lo + _rng.NextDouble() * (hi - lo));
                throw new CastRuntimeException("rng takes no args or a range");
            }
            case "def":
            {
                // def $name -> is the name bound? (variable/function/alias)
                if (call.PositionalArgs is { Count: 1 } pa)
                {
                    if (pa[0] is VarNode vn) return Bool(_frame.Find(vn.Name) is not null);
                    if (pa[0] is IdentNode idn) return Bool(_functions.ContainsKey(idn.Name));
                }
                return Bool(false);
            }
            case "clear":
                Console.Clear();
                return CastValue.Null;
            case "tag":
            case "untag":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                return TagOp(call.Name, a);
            }
            case "save": case "qsave":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                string name = call.Name == "qsave" ? "__quick__"
                    : (a.Count == 1 && a[0] is StringValue sv ? sv.S
                       : throw new CastRuntimeException("save expects a name"));
                return DoSave(name);
            }
            case "load": case "qload":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                string name = call.Name == "qload" ? "__quick__"
                    : (a.Count == 1 && a[0] is StringValue sv ? sv.S
                       : throw new CastRuntimeException("load expects a name"));
                return DoLoad(name);
            }
            case "saves":
            {
                if (_host?.Persistence is null) return NeedsHost("no persistence provider");
                return new ArrayValue(_host.Persistence.List().Select(s => (CastValue)new StringValue(s)).ToList());
            }
            case "unsave":
            {
                if (_host?.Persistence is null) return NeedsHost("no persistence provider");
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 1 && a[0] is StringValue sv) { _host.Persistence.Delete(sv.S); return CastValue.Null; }
                throw new CastRuntimeException("unsave expects a name");
            }
            case "log":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                Console.WriteLine(string.Join(" ", a.Select(Stringify)));
                return CastValue.Null;
            }
            case "say":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                string text = string.Join(" ", a.Select(Stringify));
                if (_host?.Output is { } o) o.Say(text);
                else Console.WriteLine(text);            // falls back to log
                return CastValue.Null;
            }
            case "msg":
            {
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                string text = string.Join(" ", a.Select(Stringify));
                // msg is located at the active scope's position
                VectorValue? pos = null;
                if (_host is not null && CurrentSelf is { } self
                    && _host.Properties.TryGet(self, "position", out var pv) && pv is VectorValue vv)
                    pos = vv;
                if (_host?.Output is { } o) o.Msg(text, pos);
                else Console.WriteLine(pos is null ? text : $"{text} @ {pos}");  // falls back to log + location
                return CastValue.Null;
            }
            case "read":
            {
                if (_host?.Directories is null) return NeedsHost("no directory provider");
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 1 && a[0] is StringValue p && _host.Directories.TryRead(p.S, out var contents))
                    return new StringValue(contents);
                throw new CastRuntimeException("read: file not found or path not in a registered directory");
            }
            case "write":
            {
                if (_host?.Directories is null) return NeedsHost("no directory provider");
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 2 && a[0] is StringValue p)
                {
                    if (_host.Directories.TryWrite(p.S, Stringify(a[1]))) return CastValue.Null;
                    throw new CastRuntimeException("write: path not writable or not in a registered directory");
                }
                throw new CastRuntimeException("write expects a path and contents");
            }
            case "files":
            {
                if (_host?.Directories is null) return NeedsHost("no directory provider");
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 1 && a[0] is StringValue d)
                    return new ArrayValue(_host.Directories.List(d.S).Select(s => (CastValue)new StringValue(s)).ToList());
                throw new CastRuntimeException("files expects a directory name");
            }
            case "invoke":
            {
                if (_host?.Directories is null) return NeedsHost("no directory provider");
                var a = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
                if (a.Count == 1 && a[0] is StringValue p)
                {
                    string path = p.S.EndsWith(".cast") ? p.S : p.S + ".cast";  // .cast implicit
                    if (_host.Directories.TryRead(path, out var src))
                    {
                        // execute the file's contents in the current session
                        foreach (var stmt in new CastParser(src).ParseProgram().Statements)
                            ExecStatement(stmt);
                        return CastValue.Null;
                    }
                    throw new CastRuntimeException($"invoke: '{path}' not found");
                }
                throw new CastRuntimeException("invoke expects a path");
            }
        }

        if (_host is null) return NeedsHost($"call to '{call.Name}' (built-in or host command)");
        var handler = _host.CommandHandlers.FirstOrDefault(h => h.Handles(call.Name));
        if (handler is null) return NeedsHost($"unregistered command '{call.Name}'");

        var pargs = (call.PositionalArgs ?? new List<Node>()).Select(Eval).ToList();
        var named = new Dictionary<string, CastValue>();
        foreach (var p in call.NamedArgs ?? new List<PairNode>())
            named[KeyName(p.Key)] = Eval(p.Value);

        var targets = CurrentSelf is { } s ? new[] { s } : Array.Empty<CastTarget>();
        return handler.Invoke(call.Name, targets, pargs, named);
    }

    private readonly Random _rng = new();

    private static readonly HashSet<string> PreludeNames = new() { "Heal", "Hurt", "SetHealth" };

    private CastValue DoSave(string name)
    {
        if (_host?.Persistence is null) return NeedsHost("no persistence provider");
        // exclude prelude functions (re-loaded fresh) from the user's save
        var userFns = _functions.Where(kv => !PreludeNames.Contains(kv.Key))
                                .ToDictionary(kv => kv.Key, kv => kv.Value);
        var result = StateSerializer.Save(V, userFns, Casts.Active);
        _host.Persistence.Write(name, result.Buffer);
        // fail-visibly: report dropped session-local casts
        if (result.DroppedCasts > 0)
            Console.WriteLine($"saved; {result.DroppedCasts} session-local cast(s) not persisted");
        return new NumberValue(result.DroppedCasts);
    }

    private CastValue DoLoad(string name)
    {
        if (_host?.Persistence is null) return NeedsHost("no persistence provider");
        var buffer = _host.Persistence.Read(name);
        var ls = StateSerializer.Load(buffer);
        // restore @v
        foreach (var (key, val) in ls.CastRegistry) V.Write(key, val);
        // re-register functions
        foreach (var src in ls.Functions)
            foreach (var s in ((ProgramNode)new CastParser(src).ParseProgram()).Statements)
                if (s is FunctionDef fd) _functions[fd.Name] = new FunctionValue(fd);
        // re-register casts
        foreach (var src in ls.Casts)
            foreach (var s in ((ProgramNode)new CastParser(src).ParseProgram()).Statements)
                if (s is CastNode cn) EvalCast(cn);
        return CastValue.Null;
    }

    // tag/untag operate on the active @s via the host property adapter's "tags".
    private CastValue TagOp(string op, IReadOnlyList<CastValue> args)
    {
        if (_host is null || CurrentSelf is not { } target) return NeedsHost("tag/untag");
        if (args.Count != 1 || args[0] is not StringValue tag)
            throw new CastRuntimeException($"{op} expects a tag string");
        // read current tags, modify, write back (host exposes 'tags' as an array)
        if (!_host.Properties.TryGet(target, "tags", out var cur) || cur is not ArrayValue arr)
            return NeedsHost("host does not expose 'tags'");
        var set = arr.Items.OfType<StringValue>().Select(s => s.S).ToHashSet();
        if (op == "tag") set.Add(tag.S); else set.Remove(tag.S);
        _host.Properties.TrySet(target, "tags",
            new ArrayValue(set.Select(s => (CastValue)new StringValue(s)).ToList()));
        return CastValue.Null;
    }

    // Intrinsic math functions: pure numeric, implemented natively (the spec's
    // standard math library — Sqrt etc. can't be written in Cast).
    private static readonly Dictionary<string, Func<IReadOnlyList<CastValue>, CastValue>> Intrinsics = new()
    {
        ["Floor"] = a => new NumberValue(Math.Floor(N(a, 0))),
        ["Ceil"]  = a => new NumberValue(Math.Ceiling(N(a, 0))),
        ["Round"] = a => new NumberValue(Math.Round(N(a, 0), MidpointRounding.ToEven)),
        ["Abs"]   = a => new NumberValue(Math.Abs(N(a, 0))),
        ["Min"]   = a => new NumberValue(Math.Min(N(a, 0), N(a, 1))),
        ["Max"]   = a => new NumberValue(Math.Max(N(a, 0), N(a, 1))),
        ["Clamp"] = a => new NumberValue(Math.Clamp(N(a, 0), N(a, 1), N(a, 2))),
        ["Sqrt"]  = a => new NumberValue(Math.Sqrt(N(a, 0))),
        ["Pow"]   = a => new NumberValue(Math.Pow(N(a, 0), N(a, 1))),
        ["Sin"]   = a => new NumberValue(Math.Sin(N(a, 0))),
        ["Cos"]   = a => new NumberValue(Math.Cos(N(a, 0))),
        ["Atan2"] = a => new NumberValue(Math.Atan2(N(a, 0), N(a, 1))),
    };

    private static double N(IReadOnlyList<CastValue> a, int i) =>
        i < a.Count && a[i] is NumberValue n ? n.N
        : throw new CastRuntimeException("math function: numeric argument expected");

    private string KeyName(Node key) => key switch
    {
        IdentNode id => id.Name,
        StringNode s => s.CastValue,
        _ => Stringify(Eval(key))
    };

    // `value | command`: send the value into the command as its primary input
    // (the default arg slot, arg[0]). If the value is a set scope (an array of
    // targets), the command runs once per element.
    private CastValue EvalPipe(PipeNode pn)
    {
        var input = Eval(pn.Left);

        if (input is ArrayValue { Items: var items } && items.Count > 0 && items[0] is TargetValue)
        {
            // Piping a set scope iterates the command per element, with each element
            // as the acting subject (@s) — NOT injected as an argument. The command
            // keeps its own args (e.g. @e | Heal[5] runs Heal[5] once per entity).
            CastValue last = CastValue.Null;
            foreach (var it in items)
            {
                if (it is TargetValue tv)
                {
                    _self.Push(tv.CastTarget);
                    try { last = Eval(pn.Right); }
                    finally { _self.Pop(); }
                }
            }
            return last;
        }

        return PipeInto(pn.Right, input);
    }

    private CastValue PipeInto(Node receiver, CastValue input)
    {
        switch (receiver)
        {
            case CallNode call:
            {
                var prepended = new List<Node> { new InjectedValueNode(input) };
                if (call.PositionalArgs is { } pa) prepended.AddRange(pa);
                return EvalCall(call with { PositionalArgs = prepended });
            }
            case IdentNode id:
                return EvalCall(new CallNode(id.Name, new List<Node> { new InjectedValueNode(input) }, null));
            case PipeNode chained:
                var mid = PipeInto(chained.Left, input);
                return PipeInto(chained.Right, mid);
            case BinaryNode { Op: "|>" } dw:
            {
                // `... | (cmd |> $v) | ...`: run the left side as a pipe stage on the
                // input, write the result into $v, and pass the result through.
                var result = PipeInto(dw.Left, input);
                EvalDirectedWrite(new InjectedValueNode(result), dw.Right);
                return result;
            }
            default:
                return Eval(receiver);
        }
    }

    // ── member / index ────────────────────────────────────────────────────────────

    private CastValue EvalMember(MemberNode mem)
    {
        // Fast path: @scope.prop (the overwhelmingly common case, e.g. @s.health).
        // Resolve the scope to its target(s) directly and read the property, skipping
        // the ArrayValue/TargetValue wrapping that EvalScopeChain would allocate only
        // to be unwrapped here. For a single-target scope this is one property call
        // with zero intermediate allocation.
        if (mem.CastTarget is ScopeChain sc && _host is not null
            && sc.Sigil is not ("@v" or "@t"))
        {
            // bare @s with no narrowing: read directly off CurrentSelf, no list at all
            if (sc.Sigil == "@s" && sc.Selection is null && sc.Region is null && sc.Filter is null)
            {
                if (CurrentSelf is { } self)
                {
                    if (_host.Properties.TryGet(self, mem.Member, out var sv)) return sv;
                    throw new CastRuntimeException($"property '{mem.Member}' not exposed");
                }
                return NeedsHost($"member access .{mem.Member} on host value");
            }
            var targets = ResolveScope(sc);
            if (targets.Count == 1)
            {
                if (_host.Properties.TryGet(targets[0], mem.Member, out var pv)) return pv;
                throw new CastRuntimeException($"property '{mem.Member}' not exposed");
            }
            // multi-target scope: map the property across the set
            var outv = new List<CastValue>(targets.Count);
            foreach (var tg in targets)
                outv.Add(_host.Properties.TryGet(tg, mem.Member, out var v) ? v : CastValue.Null);
            return new ArrayValue(outv);
        }

        var t = Eval(mem.CastTarget);
        if (t is MapValue m)
        {
            var key = new StringValue(mem.Member);
            if (m.Entries.Any(e => ValueEquality.Equal(e.Key, key)))
                return m.Entries.First(e => ValueEquality.Equal(e.Key, key)).Value;
            throw new CastRuntimeException($".{mem.Member} not present");
        }
        // host property on a target (e.g. @s.health). The target expr may resolve to
        // a single TargetValue, or an array of one (scope chains return arrays).
        if (TryAsSingleTarget(t, out var target) && _host is not null)
        {
            if (_host.Properties.TryGet(target, mem.Member, out var v)) return v;
            throw new CastRuntimeException($"property '{mem.Member}' not exposed");
        }
        return NeedsHost($"member access .{mem.Member} on host value");
    }

    // A scope chain resolves to an ArrayValue of targets; for single-target property
    // access we accept either a lone TargetValue or a one-element array of them.
    private static bool TryAsSingleTarget(CastValue v, out CastTarget target)
    {
        switch (v)
        {
            case TargetValue tv: target = tv.CastTarget; return true;
            case ArrayValue { Items: [TargetValue only] }: target = only.CastTarget; return true;
            default: target = default; return false;
        }
    }

    private CastValue EvalIndex(IndexNode ix)
    {
        // @v:slot indexing handled via scope chain; @t handled in assign/compound.
        if (ix.CastTarget is ScopeChain { Sigil: "@t" } && ix.Args.Count == 1)
            return T.Read(Eval(ix.Args[0]));

        var t = Eval(ix.CastTarget);
        if (t is ArrayValue a && ix.Args.Count == 1 && Eval(ix.Args[0]) is NumberValue n)
        {
            int i = (int)n.N;
            if (i < 0 || i >= a.Items.Count) throw new CastRuntimeException("array index out of range");
            return a.Items[i];
        }
        if (t is MapValue m && ix.Args.Count == 1)
        {
            var key = Eval(ix.Args[0]);
            var hit = m.Entries.FirstOrDefault(e => ValueEquality.Equal(e.Key, key));
            if (m.Entries.Any(e => ValueEquality.Equal(e.Key, key))) return hit.Value;
            throw new CastRuntimeException("map key not present");
        }
        return NeedsHost("index on host value");
    }

    // ── scope chains (host-free subset: @v, @t) ──────────────────────────────────

    private CastValue EvalScopeChain(ScopeChain sc)
    {
        if (sc.Sigil == "@v")
        {
            if (sc.VPath is not null) return V.Read(VKey(sc));
            // bare @v (e.g. #@v) -> represent as a map snapshot
            var d = new ValueKeyDictionary();
            foreach (var kv in V.Snapshot()) d[new StringValue(kv.Key)] = kv.Value;
            return new MapValue(d);
        }
        if (sc.Sigil == "@t")
        {
            // bare @t with no index: not a value by itself in host-free core
            return NeedsHost("bare @t value");
        }
        // Host-backed entity/world scopes: resolve to the set of targets, returned
        // as an ArrayValue of TargetValue so downstream commands can act on them.
        var targets = ResolveScope(sc);
        return new ArrayValue(targets.Select(t => (CastValue)new TargetValue(t)).ToList());
    }

    // Resolve a scope chain to its targets via the host. Implements the two-stage
    // active-scope rule: the [filter] is evaluated with the CALLER's @s still active
    // (bare names in the filter test the candidate; @s in the filter = caller). Only
    // after the filter selects does @s rebind per-match for the following command.
    private IReadOnlyList<CastTarget> ResolveScope(ScopeChain sc)
    {
        if (_host is null) { NeedsHost($"scope {sc.Sigil}"); return Array.Empty<CastTarget>(); }

        // Fast path: bare @s (no selection/region/filter) is just the current self.
        // This is by far the hottest scope in real scripts (@s.health, @s.x, ...), so
        // we return it directly instead of going through the handler + LINQ + query
        // allocation. CurrentSelf already encodes the @s / AmbientSelf rule.
        if (sc.Sigil == "@s" && sc.Selection is null && sc.Region is null && sc.Filter is null)
            return CurrentSelf is { } self ? new[] { self } : Array.Empty<CastTarget>();

        string letters = sc.Sigil.TrimStart('@');

        var handler = _host.ScopeHandlers.FirstOrDefault(h => h.Handles(letters));
        if (handler is null) { NeedsHost($"unregistered scope {sc.Sigil}"); return Array.Empty<CastTarget>(); }

        var selection = sc.Selection?.Select(Eval).ToList();
        CastValue? region = sc.Region is null ? null : Eval(sc.Region);

        // Build the per-candidate filter closure. During filter evaluation the active
        // @s remains the caller's; a bare property name in the filter resolves against
        // the candidate being tested (we push the candidate as a "test target").
        Func<CastTarget, bool>? filter = null;
        if (sc.Filter is not null)
        {
            filter = candidate =>
            {
                _testTarget.Push(candidate);
                try { return Eval(sc.Filter!).IsTruthy; }
                catch (CastRuntimeException) { return false; } // a failing filter excludes
                finally { _testTarget.Pop(); }
            };
        }

        var query = new ScopeQuery
        {
            Letters = letters,
            Selection = selection,
            Region = region,
            Self = CurrentSelf,
            Filter = filter
        };
        return handler.Resolve(query);
    }

    // The candidate target currently under filter evaluation (bare names resolve
    // against it). Distinct from @s (the acting target).
    private readonly Stack<CastTarget> _testTarget = new();

    private static string VKey(ScopeChain sc) =>
        sc.VKeyCache ??= string.Join(":", (sc.VPath ?? new List<RegistryKey>()).Select(k => k.Text));

    // ── commands (host-free subset) ───────────────────────────────────────────────

    private CastValue EvalCommand(CommandNode cmd)
    {
        // Pure @v / @t property ops with no host scope are evaluable directly.
        if (cmd.Scope is { } vt && (vt.Sigil == "@v" || vt.Sigil == "@t")
            && cmd.Mods.IsEmpty)
        {
            return Eval(cmd.Action);
        }
        // A command with no leading scope (or a language built-in) is evaluated
        // directly — DispatchHostCommand handles log/say/clear/rng/etc.
        if (cmd.Scope is null) return EvalAction(cmd.Action);

        if (_host is null) return NeedsHost("scoped command");

        // Resolve the acting set. `as` redirects WHO acts; the leading scope is the
        // trigger/context. Without `as`, the command runs as the leading scope.
        IReadOnlyList<CastTarget> actors;
        if (cmd.Mods.As is { } asScope)
            actors = ResolveScope(asScope);
        else if (cmd.Scope is { } sc && sc.Sigil is not ("@v" or "@t"))
            actors = ResolveScope(sc);
        else
            actors = CurrentSelf is { } s ? new[] { s } : Array.Empty<CastTarget>();

        // Run the action once per actor, rebinding @s each time (scope auto-iteration).
        CastValue last = CastValue.Null;
        foreach (var actor in actors)
        {
            _self.Push(actor);
            try { last = EvalAction(cmd.Action); }
            finally { _self.Pop(); }
        }
        return last;
    }

    // Evaluate a node in ACTION/statement position. Differences from value position:
    // a bare identifier naming a user function INVOKES it (zero args) rather than
    // returning the function value; and statement-level nodes (collect/out/iteration/
    // command/cast/spawn) route through ExecStatement.
    private CastValue EvalAction(Node action)
    {
        if (action is IdentNode { Name: var nm } && _functions.ContainsKey(nm))
            return EvalCall(new CallNode(nm, null, null));
        if (action is OutNode or CollectNode or IterationNode or CommandNode or CastNode or SpawnNode or FunctionDef)
            return ExecStatement(action);
        return Eval(action);
    }

    // ── spawn ───────────────────────────────────────────────────────────────────────

    private CastValue EvalSpawn(SpawnNode sp)
    {
        if (_host?.Spawner is null) return NeedsHost("no spawner (spawn unimplemented by host)");

        // count: a number, or a range -> pick a value in it
        int count = 1;
        if (sp.Selection is not null)
        {
            var sel = Eval(sp.Selection);
            count = sel switch
            {
                NumberValue n => (int)n.N,
                RangeValue { Low: { } lo, High: { } hi } => (int)(lo + _rng.NextDouble() * (hi - lo + 1)),
                _ => 1
            };
        }

        CastValue? where = sp.Where is null ? null : Eval(sp.Where);

        var props = new Dictionary<string, CastValue>();
        foreach (var p in sp.Properties)
            props[KeyName(p.Key)] = Eval(p.Value);

        var created = _host.Spawner.Spawn(sp.Kind, count, where, props);
        return new ArrayValue(created.Select(t => (CastValue)new TargetValue(t)).ToList());
    }

    // ── cast ───────────────────────────────────────────────────────────────────────

    private CastValue EvalCast(CastNode cn)
    {
        if (cn.Trigger is null)
        {
            int times = cn.Count is null ? 1 : (int)((NumberNode)cn.Count).CastValue;
            if (cn.Over is not null && cn.Count is not null)
            {
                var ac = new ActiveCast { Id = -1, Node = cn };
                int frames = (int)((NumberNode)cn.Over).CastValue;
                for (int i = 1; i <= times; i++)
                {
                    long due = Casts.CastFrame + (long)Math.Round((double)frames * i / times);
                    ac.Scheduled.Enqueue((due, CurrentSelf));
                }
                _transient.Add(ac);
                return CastValue.Null;
            }
            CastValue last = CastValue.Null;
            for (int i = 0; i < times; i++) last = FireCastAction(cn, CurrentSelf);
            return last;
        }

        bool refsLocal = ReferencesSessionLocal(cn);
        int id = Casts.Register(cn, refsLocal);
        return new NumberValue(id);
    }

    private CastValue FireCastAction(CastNode cn, CastTarget? actor)
    {
        if (cn.Mods.As is { } asScope)
        {
            var redirected = ResolveScope(asScope);
            CastValue last = CastValue.Null;
            foreach (var a in redirected)
            {
                _self.Push(a);
                try { last = cn.Action is null ? CastValue.Null : EvalAction(cn.Action); }
                finally { _self.Pop(); }
            }
            return last;
        }
        if (actor is { } s)
        {
            _self.Push(s);
            try { return cn.Action is null ? CastValue.Null : EvalAction(cn.Action); }
            finally { _self.Pop(); }
        }
        return cn.Action is null ? CastValue.Null : EvalAction(cn.Action);
    }

    private readonly List<ActiveCast> _transient = new();

    /// <summary>Advance one frame: drive standing casts and transient schedules.</summary>
    public void Tick()
    {
        foreach (var ac in _transient.ToList())
        {
            while (ac.Scheduled.Count > 0 && ac.Scheduled.Peek().frame <= Casts.CastFrame + 1)
            {
                var (_, tgt) = ac.Scheduled.Dequeue();
                FireCastAction(ac.Node, tgt);
            }
            if (ac.Scheduled.Count == 0) _transient.Remove(ac);
        }

        Casts.Tick(
            resolveTargets: cast =>
            {
                if (cast.Node.Trigger is { } trig)
                {
                    if (trig.Sigil is "@v")
                        return V.ReadOr(VKey(trig), CastValue.Null).IsTruthy
                            ? new[] { CurrentSelf ?? default } : Array.Empty<CastTarget>();
                    return ResolveScope(trig);
                }
                return Array.Empty<CastTarget>();
            },
            fire: (cast, target) => FireCastAction(cast.Node, target));
    }

    private bool ReferencesSessionLocal(CastNode cn)
    {
        bool found = false;
        void Walk(Node? n)
        {
            if (n is null || found) return;
            switch (n)
            {
                case VarNode: found = true; return;
                case CallNode call:
                    foreach (var a in call.PositionalArgs ?? Enumerable.Empty<Node>()) Walk(a);
                    foreach (var p in call.NamedArgs ?? Enumerable.Empty<PairNode>()) { Walk(p.Key); Walk(p.Value); }
                    return;
            }
            foreach (var child in Children(n)) Walk(child);
        }
        Walk(cn.Trigger);
        Walk(cn.Action);
        Walk(cn.Mods.As); Walk(cn.Mods.At);
        return found;
    }

    private static IEnumerable<Node> Children(Node n) => n switch
    {
        ScopeChain s => new Node?[] { s.Filter, s.Region }.Concat(s.Selection ?? Enumerable.Empty<Node>())
                         .Where(x => x is not null).Cast<Node>(),
        BinaryNode b => new[] { b.Left, b.Right },
        UnaryNode u => new[] { u.Operand },
        PostfixNode p => new[] { p.Operand },
        ConditionalNode c => new Node?[] { c.Condition, c.Then, c.Else }.Where(x => x is not null).Cast<Node>(),
        RangeNode r => new Node?[] { r.Low, r.High }.Where(x => x is not null).Cast<Node>(),
        PlacementNode pl => new Node?[] { pl.Mover, pl.CastTarget, pl.Magnitude }.Where(x => x is not null).Cast<Node>(),
        PipeNode pp => new[] { pp.Left, pp.Right },
        MemberNode m => new[] { m.CastTarget },
        IndexNode ix => new[] { ix.CastTarget }.Concat(ix.Args),
        NamedIndexNode ni => new[] { ni.CastTarget }.Concat(ni.Pairs.SelectMany(p => new[] { p.Key, p.Value })),
        SliceNode sl => new[] { sl.CastTarget }.Concat(sl.Items),
        MembershipNode mm => new[] { mm.Collection, mm.Tested },
        ArrayNode a => a.Elements,
        MapNode mp => mp.Pairs.SelectMany(p => new[] { p.Key, p.Value }),
        SequenceNode sq => sq.Items,
        GroupNode g => new[] { g.Inner },
        VectorNode v => v.Components,
        SpawnNode sp => (sp.Selection is null ? Enumerable.Empty<Node>() : new[] { sp.Selection })
                        .Concat(sp.Where is null ? Enumerable.Empty<Node>() : new[] { sp.Where })
                        .Concat(sp.Properties.SelectMany(p => new[] { p.Key, p.Value })),
        PrefixedComponentNode pc => pc.CastValue is null ? Enumerable.Empty<Node>() : new[] { pc.CastValue },
        FallbackNode f => new[] { f.CastValue },
        _ => Enumerable.Empty<Node>()
    };

    // ── placement / step ──────────────────────────────────────────────────────────

    private CastValue EvalPlacement(PlacementNode pl)
    {
        if (_host is null) return NeedsHost("placement (needs host position)");

        // If the mover is a scope with a registered vector interpreter (non-spatial
        // semantics, e.g. @t <day,minute,hour>), route the vector through it. Such a
        // scope (like @t, the world clock) isn't an entity scope, so we don't resolve
        // it to host targets — we apply against the ambient/world target.
        if (MoverScopeLetters(pl.Mover) is { } letters)
        {
            var interp = _host.VectorInterpreters.FirstOrDefault(i => i.Handles(letters));
            if (interp is not null)
            {
                var vec = EvalVectorWith(pl.CastTarget, new VectorValue(new double[] { 0, 0, 0 }));
                var who = CurrentSelf ?? new CastTarget(letters);  // synthetic handle if no ambient
                interp.Apply(letters, who, vec);
                return vec;
            }
        }

        if (!ResolveMover(pl.Mover, out var target, out var prop))
            return NeedsHost("placement mover must be a scope position");

        VectorValue current = _host.Properties.TryGet(target, prop, out var cv) && cv is VectorValue vv
            ? vv : new VectorValue(new double[] { 0, 0, 0 });

        VectorValue dest = EvalVectorWith(pl.CastTarget, current);

        if (pl.Op == "->")
        {
            _host.Properties.TrySet(target, prop, dest);
            return dest;
        }
        double mag = pl.Magnitude is null ? 1 : AsNum(Eval(pl.Magnitude));
        var stepped = StepToward(current, dest, mag);
        _host.Properties.TrySet(target, prop, stepped);
        return stepped;
    }

    // The scope letters of a placement mover, if it's a scope (bare or .position).
    private static string? MoverScopeLetters(Node mover) => mover switch
    {
        ScopeChain sc => sc.Sigil.TrimStart('@'),
        MemberNode { CastTarget: ScopeChain sc } => sc.Sigil.TrimStart('@'),
        _ => null
    };

    private bool ResolveMover(Node mover, out CastTarget target, out string prop)
    {
        target = default; prop = "position";
        if (mover is MemberNode { CastTarget: var tnode, Member: var m })
        {
            var t = Eval(tnode);
            if (TryAsSingleTarget(t, out target)) { prop = m; return true; }
            return false;
        }
        if (mover is ScopeChain)
        {
            var t = Eval(mover);
            if (TryAsSingleTarget(t, out target)) { prop = "position"; return true; }
        }
        return false;
    }

    private VectorValue EvalVectorWith(Node node, VectorValue current)
    {
        if (node is not VectorNode vn)
        {
            var val = Eval(node);
            if (val is VectorValue v) return v;
            // Bare-scope sugar on the destination: `@s -> @np` means
            // `@s.position -> @np.position`. A scope resolves to a set of targets;
            // read the (single) target's position. Also accept an explicit
            // `@np.position` member access, which already yields the vector above.
            if (TryAsSingleTarget(val, out var t) && _host is not null
                && _host.Properties.TryGet(t, "position", out var pv) && pv is VectorValue tv)
                return tv;
            throw new CastRuntimeException("placement target must be a vector or a scope position");
        }
        switch (vn.Shorthand)
        {
            case VectorShorthand.AllRelative: return current;
            case VectorShorthand.AllOpen:     return current;
            case VectorShorthand.Empty:       return new VectorValue(Array.Empty<double>());
        }
        var comps = new List<double>();
        for (int i = 0; i < vn.Components.Count; i++)
        {
            double cur = i < current.Arity ? current.Components[i] : 0;
            comps.Add(ResolveComponent(vn.Components[i], cur));
        }
        return new VectorValue(comps);
    }

    private double ResolveComponent(Node c, double current)
    {
        switch (c)
        {
            case NullNode: return current;
            case PrefixedComponentNode { Prefix: "~", CastValue: null }: return current;
            case PrefixedComponentNode { Prefix: "~", CastValue: { } v }: return current + AsNum(Eval(v));
            case PrefixedComponentNode { Prefix: "^", CastValue: { } v }: return AsNum(Eval(v));
            case PrefixedComponentNode { Prefix: "°", CastValue: { } v }: return AsNum(Eval(v));
            case NumberNode n: return n.CastValue;
            case UnaryNode { Op: "-", Operand: NumberNode n2 }: return -n2.CastValue;
            default: return AsNum(Eval(c));
        }
    }

    private static VectorValue StepToward(VectorValue from, VectorValue to, double mag)
    {
        int n = Math.Min(from.Arity, to.Arity);
        var dir = new double[n];
        double len = 0;
        for (int i = 0; i < n; i++) { dir[i] = to.Components[i] - from.Components[i]; len += dir[i] * dir[i]; }
        len = Math.Sqrt(len);
        var result = new double[n];
        if (len == 0) { for (int i = 0; i < n; i++) result[i] = from.Components[i]; return new VectorValue(result); }
        for (int i = 0; i < n; i++) result[i] = from.Components[i] + dir[i] / len * mag;
        return new VectorValue(result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static NumberValue Bool(bool b) => new(b ? 1 : 0);

    private static double AsNum(CastValue v) => v switch
    {
        NumberValue n => n.N,
        NullValue => 0,
        _ => throw new CastRuntimeException($"expected number, got {v.GetType().Name}")
    };

    private static string Stringify(CastValue v) => v switch
    {
        StringValue s => s.S,
        NumberValue n => n.ToString(),
        _ => v.ToString() ?? ""
    };

    private CastValue NeedsHost(string what) =>
        throw new CastRuntimeException($"needs host: {what}");
}
