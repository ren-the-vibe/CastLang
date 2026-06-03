#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cast.Lang;

public sealed class CastParseException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public CastParseException(string message, int line, int column)
        : base($"Parse error at {line}:{column}: {message}")
    {
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Recursive-descent parser with a precedence-climbing expression core. The
/// expression precedence table (spec Evaluation Model) is encoded as a ladder of
/// methods, loosest to tightest. The three carve-outs — range-complement `!`,
/// `~>` trailing magnitude, and the `cast [count] [over N]` prefix — are handled
/// by dedicated productions woven in at the right depth, exactly as the grammar
/// specifies.
/// </summary>
public sealed class CastParser
{
    private readonly List<CastToken> _toks;
    private int _i;

    public CastParser(IEnumerable<CastToken> tokens)
    {
        // Drop comments; keep newlines (they're statement separators).
        _toks = tokens.Where(t => t.Type != CastTokenType.Comment).ToList();
    }

    public CastParser(string source) : this(new CastLexer(source).Tokenize()) { }

    // ── token cursor ──────────────────────────────────────────────────────────

    private CastToken Cur => _toks[_i];
    private CastToken Peek(int n = 1) => _i + n < _toks.Count ? _toks[_i + n] : _toks[^1];
    private bool AtEnd => Cur.Type == CastTokenType.EndOfFile;
    private CastToken Advance() => _toks[_i++];
    private bool Check(CastTokenType t) => Cur.Type == t;

    private bool Match(params CastTokenType[] types)
    {
        foreach (var t in types)
            if (Check(t)) { Advance(); return true; }
        return false;
    }

    private CastToken Expect(CastTokenType t, string what)
    {
        if (Check(t)) return Advance();
        throw new CastParseException($"expected {what}, got {Cur.Type} '{Cur.Text}'", Cur.Line, Cur.Column);
    }

    private TNode At<TNode>(CastToken tok, TNode node) where TNode : Node =>
        node with { Line = tok.Line, Column = tok.Column };

    // Skip statement separators (newlines and ';').
    private void SkipSeparators()
    {
        while (Check(CastTokenType.Newline) || Check(CastTokenType.Semicolon)) Advance();
    }

    // ── program / statements ────────────────────────────────────────────────────

    public ProgramNode ParseProgram()
    {
        var stmts = new List<Node>();
        SkipSeparators();
        while (!AtEnd)
        {
            stmts.Add(ParseStatement());
            // a statement must be followed by a separator or EOF
            if (!AtEnd && !Check(CastTokenType.Newline) && !Check(CastTokenType.Semicolon)
                && !Check(CastTokenType.ColonColon) /* end of a function body */)
            {
                throw new CastParseException(
                    $"expected statement separator, got {Cur.Type} '{Cur.Text}'", Cur.Line, Cur.Column);
            }
            SkipSeparators();
        }
        return new ProgramNode(stmts);
    }

    private Node ParseStatement()
    {
        // Function definition:  Ident :: body ::
        if (Check(CastTokenType.Ident) && Peek().Type == CastTokenType.ColonColon)
            return ParseFunctionDef();

        // out [value]
        if (Check(CastTokenType.Out))
        {
            var t = Advance();
            Node? v = CanStartAction() ? ParseExpression() : null;
            return At(t, new OutNode(v));
        }

        // collect value
        if (Check(CastTokenType.Collect))
        {
            var t = Advance();
            var v = ParseExpression();
            return At(t, new CollectNode(v));
        }

        // Iteration:  $name in collection <bracket> body </bracket>
        if (Check(CastTokenType.Dollar) && Peek().Type == CastTokenType.Ident
            && Peek(2).Type == CastTokenType.In)
            return ParseIteration();

        // Membership as a bare statement/expression: in collection ? value
        if (Check(CastTokenType.In))
            return ParseExpression();

        // cast command
        if (Check(CastTokenType.Ident) && Cur.Text == "cast")
            return ParseCast();

        // spawn command (its own structured grammar)
        if (Check(CastTokenType.Ident) && Cur.Text == "spawn")
            return ParseSpawn();

        // scope-led command, or context-modified command, or bare action/expr
        return ParseCommandOrExpr();
    }

    private Node ParseIteration()
    {
        var dollar = Expect(CastTokenType.Dollar, "'$'");
        var name = Expect(CastTokenType.Ident, "iteration variable");
        Expect(CastTokenType.In, "'in'");
        var collection = ParseCollectionExpr();

        // body bracket matches the collection kind: [ ] { } < > ( )
        char open;
        CastTokenType closer;
        if (Check(CastTokenType.LBracket)) { open = '['; closer = CastTokenType.RBracket; }
        else if (Check(CastTokenType.LBrace)) { open = '{'; closer = CastTokenType.RBrace; }
        else if (Check(CastTokenType.Lt)) { open = '<'; closer = CastTokenType.Gt; }
        else if (Check(CastTokenType.LParen)) { open = '('; closer = CastTokenType.RParen; }
        else throw new CastParseException("expected iteration body bracket", Cur.Line, Cur.Column);
        Advance();

        var body = new List<Node>();
        SkipSeparators();
        while (!Check(closer) && !AtEnd)
        {
            body.Add(ParseStatement());
            SkipSeparators();
        }
        Expect(closer, "closing iteration body bracket");
        return At(dollar, new IterationNode(name.Text, collection, open, body));
    }

    private bool _suppressBodyBracket;

    // The collection in an iteration is an expression, but we must stop before the
    // body bracket (which belongs to the iteration, not the collection). We parse a
    // full additive/range expression with postfix *bracket* consumption suppressed,
    // so `1..5[ ... ]` keeps the `[` for the body and `$arr[0]` still works inside
    // sub-expressions via grouping. The suppression only affects the top level.
    private Node ParseCollectionExpr()
    {
        bool prev = _suppressBodyBracket;
        _suppressBodyBracket = true;
        try
        {
            // parse an additive expr (covers ranges via ParseRange), then an optional
            // range tail — all with top-level body brackets left alone.
            var node = ParseRange();
            return node;
        }
        finally { _suppressBodyBracket = prev; }
    }

    private FunctionDef ParseFunctionDef()
    {
        var nameTok = Expect(CastTokenType.Ident, "function name");
        Expect(CastTokenType.ColonColon, "'::'");
        var body = new List<Node>();
        SkipSeparators();
        while (!Check(CastTokenType.ColonColon) && !AtEnd)
        {
            body.Add(ParseStatement());
            SkipSeparators();
        }
        Expect(CastTokenType.ColonColon, "closing '::'");
        return At(nameTok, new FunctionDef(nameTok.Text, body));
    }

    // ── cast ────────────────────────────────────────────────────────────────────
    // cast [count] [over N] [trigger-scope] [as @s] [at @s] [action]

    private Node ParseCast()
    {
        var castTok = Advance(); // 'cast' (an Ident)

        Node? count = null, over = null;
        // count: a bare number immediately after cast (Finding 16 invariant)
        if (Check(CastTokenType.Number))
            count = ParseNumber();

        // over N
        if (Check(CastTokenType.Over))
        {
            Advance();
            over = ParseExpression(); // N (an additive expression in practice)
        }

        // trigger scope (optional)
        ScopeChain? trigger = null;
        if (Check(CastTokenType.ScopeSigil))
            trigger = ParseScopeChain();

        // context mods + action tail
        var mods = ParseContextMods();
        Node? action = null;
        if (CanStartAction())
            action = ParseAction();

        return At(castTok, new CastNode(count, over, trigger, mods, action));
    }

    // ── command / expression ───────────────────────────────────────────────────

    private Node ParseCommandOrExpr()
    {
        // Scope-led command:  @scope(...)... [as/at] action
        if (Check(CastTokenType.ScopeSigil))
        {
            int save = _i;
            var scope = ParseScopeChain();

            // A scope chain can carry value-postfixes ({key}, [i], .member) that
            // reach into its result — e.g. @v:poison{@s.id}. Apply them now.
            Node scopeExpr = ParsePostfixFrom(scope);

            // If the scope was extended by postfixes, it's an expression head, not a
            // bare command scope — continue as an expression (handles @v:poison{..} += ..).
            bool extended = !ReferenceEquals(scopeExpr, scope);

            var mods = extended ? new ContextMods(null, null) : ParseContextMods();

            if (!extended && CanStartAction())
            {
                var action = ParseAction();
                return At(_toks[save], new CommandNode(scope, mods, action));
            }
            if (!extended && !mods.IsEmpty)
                throw new CastParseException("expected an action after as/at", Cur.Line, Cur.Column);

            // bare scope chain (possibly postfixed) as an expression — may be the LHS
            // of an operator (e.g. `@s.health = ...`, `@v:poison{..} += ..`).
            return ParseBinaryContinuation(scopeExpr);
        }

        // Leading as/at (ambient scope, redirected context)
        if (Check(CastTokenType.As) || Check(CastTokenType.AtKw))
        {
            var mods = ParseContextMods();
            var action = ParseAction();
            return new CommandNode(null, mods, action) { Line = action.Line, Column = action.Column };
        }

        // Otherwise: a bare action or expression (binding, call, etc.)
        return ParseExpression();
    }

    private ContextMods ParseContextMods()
    {
        ScopeChain? asScope = null, atScope = null;
        if (Check(CastTokenType.As))
        {
            Advance();
            asScope = ParseScopeChain();
        }
        if (Check(CastTokenType.AtKw))
        {
            Advance();
            atScope = ParseScopeChain();
        }
        return new ContextMods(asScope, atScope);
    }

    private bool CanStartAction()
    {
        // An action is a built-in/call (Ident), or a scope-led expr starting with
        // a value head. We treat a following Ident, ScopeSigil, $, or a value head
        // as the start of an action. Separators / EOF / closing brackets do not.
        return Cur.Type switch
        {
            CastTokenType.Ident => true,
            CastTokenType.ScopeSigil => true,
            CastTokenType.Dollar => true,
            CastTokenType.Number => true,
            CastTokenType.String => true,
            CastTokenType.Hash => true,
            CastTokenType.Bang => true,
            CastTokenType.Tilde => true,
            CastTokenType.Caret => true,
            CastTokenType.Degree => true,
            CastTokenType.Minus => true,
            CastTokenType.Underscore => true,
            CastTokenType.LParen => true,
            CastTokenType.LBracket => true,
            CastTokenType.LBrace => true,
            CastTokenType.Lt => true,
            _ => false
        };
    }

    private Node ParseAction()
    {
        // 'cast' in action position is the cast command (e.g. `@s cast Heal[50]`).
        if (Check(CastTokenType.Ident) && Cur.Text == "cast")
            return ParseCast();
        // 'spawn' in action position keeps its structured grammar.
        if (Check(CastTokenType.Ident) && Cur.Text == "spawn")
            return ParseSpawn();
        // A call (Name[..]/Name{..}/bare Name) or a general expression
        // (property ops like @s.health = ...). We parse a full expression; a bare
        // Ident with [] / {} becomes a CallNode via postfix handling below.
        return ParseExpression();
    }

    // spawn <id>(selection)<region>[properties]
    //   id is a namespaced id; (selection) is a count expr; <region> is a vector;
    //   [properties] are initial named values. The grammar reuses the scope-chain
    //   shape but binds to entity creation rather than addressing.
    private Node ParseSpawn()
    {
        var spawnTok = Advance(); // 'spawn'
        // kind: a namespaced id (mod:type:name)
        var first = Expect(CastTokenType.Ident, "spawn kind id");
        var segs = new List<string> { first.Text };
        while (Check(CastTokenType.Colon) && Peek().Type == CastTokenType.Ident)
        {
            Advance(); // ':'
            segs.Add(Advance().Text);
        }

        Node? selection = null;
        if (Check(CastTokenType.LParen))
        {
            Advance();
            selection = ParseRangeOrExpr();   // a count or range
            Expect(CastTokenType.RParen, "')' closing spawn selection");
        }

        Node? where = null;
        if (Check(CastTokenType.Lt))
            where = ParseVector();
        else if (Check(CastTokenType.ScopeSigil))
            where = ParseScopeChain();        // spawn at a scope's position

        var props = new List<PairNode>();
        if (Check(CastTokenType.LBracket))
        {
            Advance();
            props = ParsePairs(CastTokenType.RBracket);
            Expect(CastTokenType.RBracket, "']' closing spawn properties");
        }

        return At(spawnTok, new SpawnNode(segs, selection, where, props));
    }

    // ── scope chains ────────────────────────────────────────────────────────────

    private ScopeChain ParseScopeChain()
    {
        var sigilTok = Expect(CastTokenType.ScopeSigil, "scope sigil");
        string sigil = sigilTok.Text;

        IReadOnlyList<RegistryKey>? vpath = null;
        IReadOnlyList<Node>? selection = null;
        VectorNode? region = null;
        Node? filter = null;

        // @v registry colon-path:  @v:score:player1
        if (sigil == "@v" && Check(CastTokenType.Colon))
        {
            var keys = new List<RegistryKey>();
            while (Check(CastTokenType.Colon))
            {
                Advance();
                var key = Expect(CastTokenType.Ident, "registry key segment");
                keys.Add(new RegistryKey(key.Text));
            }
            vpath = keys;
        }

        // (selection) — only meaningful for ordered scopes, but parse uniformly
        if (Check(CastTokenType.LParen))
            selection = ParseSelection();

        // <region>
        if (Check(CastTokenType.Lt))
            region = ParseVector();

        // [filter]
        if (Check(CastTokenType.LBracket))
        {
            Advance();
            filter = ParseExpression();
            Expect(CastTokenType.RBracket, "']' closing filter");
        }

        return At(sigilTok, new ScopeChain(sigil, selection, region, filter, vpath));
    }

    private IReadOnlyList<Node> ParseSelection()
    {
        Expect(CastTokenType.LParen, "'('");
        var items = new List<Node>();
        if (!Check(CastTokenType.RParen))
        {
            items.Add(ParseRangeOrExpr());
            while (Match(CastTokenType.Comma))
                items.Add(ParseRangeOrExpr());
        }
        Expect(CastTokenType.RParen, "')' closing selection");
        return items;
    }

    // A selection item is a range or a scalar.
    private Node ParseRangeOrExpr() => ParseExpression();

    // ── expressions: precedence ladder (loosest → tightest) ──────────────────────
    // L13 pipe, L12 binding, L11 placement, L10 conditional, L9 ||, L8 &&,
    // L7 comparison, L6 range, L5 additive, L4 multiplicative, L3 alias,
    // L2 unary, L1 postfix/primary.

    public Node ParseExpression() => ParsePipe();

    // Continue the ladder from an already-parsed L1 result (a scope chain used as an
    // expression head). Each level accepts an optional pre-parsed tighter operand.
    private Node ParseBinaryContinuation(Node seed) => ParsePipe(seed);

    // ── L13 pipe (left-assoc) ─────────────────────────────────────────────────
    private Node ParsePipe(Node? seed = null)
    {
        var left = ParseBinding(seed);
        while (Check(CastTokenType.Pipe))
        {
            var op = Advance();
            var right = ParseBinding();
            left = At(op, new PipeNode(left, right));
        }
        return left;
    }

    // ── L12 binding / directed write (right-assoc) ─────────────────────────────
    private Node ParseBinding(Node? seed = null)
    {
        var left = ParsePlacement(seed);
        if (Check(CastTokenType.Eq) || Check(CastTokenType.EqGt) || Check(CastTokenType.PipeGt))
        {
            var op = Advance();
            var right = ParseBinding(); // right-assoc
            return At(op, new BinaryNode(op.Text, left, right));
        }
        return left;
    }

    // ── L11 placement (non-assoc) ──────────────────────────────────────────────
    private Node ParsePlacement(Node? seed = null)
    {
        var left = ParseConditional(seed);
        if (Check(CastTokenType.Arrow))
        {
            var op = Advance();
            var target = ParseConditional();
            return At(op, new PlacementNode("->", left, target, null));
        }
        if (Check(CastTokenType.TildeArrow))
        {
            var op = Advance();
            // Carve-out B: the target is parsed *below* multiplicative (at alias/unary
            // level) so a trailing `* magnitude` is NOT absorbed into the target by
            // the level-4 multiplicative rule. The magnitude is then a full additive.
            var target = ParseAlias();
            Node? mag = null;
            if (Check(CastTokenType.Star))
            {
                Advance();
                mag = ParseAdditive();
            }
            return At(op, new PlacementNode("~>", left, target, mag));
        }
        return left;
    }

    // ── L10 conditional ?> ?? (right-assoc) ─────────────────────────────────────
    private Node ParseConditional(Node? seed = null)
    {
        var cond = ParseLogicalOr(seed);
        if (Check(CastTokenType.QuestionGt))
        {
            var op = Advance();
            var then = ParseConditionalBranch();
            Node? els = null;
            if (Check(CastTokenType.QuestionQuestion))
            {
                Advance();
                els = ParseConditionalBranch();
            }
            return At(op, new ConditionalNode(cond, then, els));
        }
        return cond;
    }

    // A conditional branch may be a statement-level keyword (collect/out) or a
    // nested expression. The spec's own examples use `?> collect 'small'`.
    private Node ParseConditionalBranch()
    {
        if (Check(CastTokenType.Collect))
        {
            var t = Advance();
            return At(t, new CollectNode(ParseExpression()));
        }
        if (Check(CastTokenType.Out))
        {
            var t = Advance();
            Node? v = CanStartAction() ? ParseExpression() : null;
            return At(t, new OutNode(v));
        }
        return ParseConditional();
    }

    // ── L9 || !| (left-assoc) ───────────────────────────────────────────────────
    private Node ParseLogicalOr(Node? seed = null)
    {
        var left = ParseLogicalAnd(seed);
        while (Check(CastTokenType.PipePipe) || Check(CastTokenType.BangPipe))
        {
            var op = Advance();
            var right = ParseLogicalAnd();
            left = At(op, new BinaryNode(op.Text, left, right));
        }
        return left;
    }

    // ── L8 && !& (left-assoc) ───────────────────────────────────────────────────
    private Node ParseLogicalAnd(Node? seed = null)
    {
        var left = ParseComparison(seed);
        while (Check(CastTokenType.AmpAmp) || Check(CastTokenType.BangAmp))
        {
            var op = Advance();
            var right = ParseComparison();
            left = At(op, new BinaryNode(op.Text, left, right));
        }
        return left;
    }

    // ── L7 comparison ? !? (non-assoc) ──────────────────────────────────────────
    private Node ParseComparison(Node? seed = null)
    {
        var left = ParseRange(seed);
        if (Check(CastTokenType.Question) || Check(CastTokenType.BangQuestion))
        {
            var op = Advance();
            var right = ParseRange();
            var node = At(op, new BinaryNode(op.Text, left, right));
            // non-assoc: a second comparison in a row is a parse error (Finding 12)
            if (Check(CastTokenType.Question) || Check(CastTokenType.BangQuestion))
                throw new CastParseException(
                    "comparison does not chain; parenthesize", Cur.Line, Cur.Column);
            return node;
        }
        return left;
    }

    // ── L6 range .. (non-assoc) + range-complement carve-out A ──────────────────
    private Node ParseRange(Node? seed = null)
    {
        // Range-complement: `!` only when a range actually follows (Finding 10).
        if (seed is null && Check(CastTokenType.Bang) && RangeFollowsAfterBang())
        {
            var bang = Advance();
            var inner = ParseRangeBody(null);
            if (inner is RangeNode rn)
                return At(bang, rn with { Complement = true });
            // shouldn't happen given the lookahead, but be safe
            return At(bang, new UnaryNode("!", inner));
        }
        return ParseRangeBody(seed);
    }

    private Node ParseRangeBody(Node? seed)
    {
        // forms: lo..hi | lo.. | ..hi | additive
        if (seed is null && Check(CastTokenType.DotDot))
        {
            var dd = Advance();
            // ..hi
            if (StartsAdditive())
            {
                var hi = ParseAdditive();
                return At(dd, new RangeNode(null, hi, false));
            }
            // bare `..` (only valid inside a vector body as <..>; here it's an open range)
            return At(dd, new RangeNode(null, null, false));
        }

        var lo = ParseAdditive(seed);
        if (Check(CastTokenType.DotDot))
        {
            var dd = Advance();
            if (StartsAdditive())
            {
                var hi = ParseAdditive();
                return At(dd, new RangeNode(lo, hi, false));
            }
            // lo..
            return At(dd, new RangeNode(lo, null, false));
        }
        return lo;
    }

    // ── L5 additive (left-assoc) ─────────────────────────────────────────────────
    private Node ParseAdditive(Node? seed = null)
    {
        var left = ParseMultiplicative(seed);
        while (Check(CastTokenType.Plus) || Check(CastTokenType.Minus))
        {
            var op = Advance();
            var right = ParseMultiplicative();
            left = At(op, new BinaryNode(op.Text, left, right));
        }
        return left;
    }

    // ── L4 multiplicative (left-assoc) ────────────────────────────────────────────
    private Node ParseMultiplicative(Node? seed = null)
    {
        var left = ParseAlias(seed);
        while (Check(CastTokenType.Star) || Check(CastTokenType.Slash) || Check(CastTokenType.Percent))
        {
            var op = Advance();
            var right = ParseAlias();
            left = At(op, new BinaryNode(op.Text, left, right));
        }
        return left;
    }

    // ── L3 alias =& (right-assoc) ──────────────────────────────────────────────────
    private Node ParseAlias(Node? seed = null)
    {
        var left = ParseUnary(seed);
        if (Check(CastTokenType.EqAmp))
        {
            var op = Advance();
            var right = ParseAlias(); // right-assoc
            return At(op, new BinaryNode("=&", left, right));
        }
        return left;
    }

    // ── L2 unary prefixes (right-assoc) ───────────────────────────────────────────
    private Node ParseUnary(Node? seed = null)
    {
        if (seed is not null) return ParsePostfixFrom(seed);

        if (Check(CastTokenType.Hash) || Check(CastTokenType.Bang) || Check(CastTokenType.Tilde)
            || Check(CastTokenType.Caret) || Check(CastTokenType.Degree) || Check(CastTokenType.Minus))
        {
            var op = Advance();
            var operand = ParseUnary();
            return At(op, new UnaryNode(op.Text, operand));
        }
        return ParsePostfix();
    }

    // ── L1 postfix: member / index / named-index / slice / ++ / -- ─────────────────
    private Node ParsePostfix()
    {
        var node = ParsePrimary();
        return ParsePostfixFrom(node);
    }

    private Node ParsePostfixFrom(Node node)
    {
        // During an iteration-collection parse, top-level '[' / '{' / '(' belong to
        // the body, not a postfix. The flag stays set for the whole collection
        // expression; nested parses that need real brackets (e.g. inside a grouping)
        // clear it locally when they recurse.
        bool suppress = _suppressBodyBracket;

        while (true)
        {
            if (Check(CastTokenType.Dot))
            {
                Advance();
                var member = Expect(CastTokenType.Ident, "member name");
                node = new MemberNode(node, member.Text) { Line = node.Line, Column = node.Column };
            }
            else if (Check(CastTokenType.LBracket) && !suppress)
            {
                Advance();
                var args = new List<Node>();
                if (!Check(CastTokenType.RBracket))
                {
                    args.Add(ParseExpression());
                    while (Match(CastTokenType.Comma)) args.Add(ParseExpression());
                }
                Expect(CastTokenType.RBracket, "']'");
                node = new IndexNode(node, args) { Line = node.Line, Column = node.Column };
            }
            else if (Check(CastTokenType.LBrace) && !suppress)
            {
                Advance();
                // {k: v, ...} named-index, OR {key} single-key access (param{'amount'},
                // @v:poison{@s.id}). Disambiguate: if the first entry has no ':' it's
                // a key-access (treated as an index with brace syntax).
                if (IsBareKeyBrace())
                {
                    var keys = new List<Node> { ParseExpression() };
                    while (Match(CastTokenType.Comma)) keys.Add(ParseExpression());
                    Expect(CastTokenType.RBrace, "'}'");
                    node = new IndexNode(node, keys) { Line = node.Line, Column = node.Column };
                }
                else
                {
                    var pairs = ParsePairs(CastTokenType.RBrace);
                    Expect(CastTokenType.RBrace, "'}'");
                    node = new NamedIndexNode(node, pairs) { Line = node.Line, Column = node.Column };
                }
            }
            else if (Check(CastTokenType.LParen) && !suppress)
            {
                Advance();
                var items = new List<Node>();
                if (!Check(CastTokenType.RParen))
                {
                    items.Add(ParseExpression());
                    while (Match(CastTokenType.Comma)) items.Add(ParseExpression());
                }
                Expect(CastTokenType.RParen, "')'");
                node = new SliceNode(node, items) { Line = node.Line, Column = node.Column };
            }
            else if (Check(CastTokenType.PlusPlus) || Check(CastTokenType.MinusMinus))
            {
                var op = Advance();
                node = new PostfixNode(op.Text, node) { Line = node.Line, Column = node.Column };
            }
            else if (IsCompoundAssign(Cur.Type))
            {
                // compound assignment as a postfix-ish binary (e.g. @t[0] += 5)
                var op = Advance();
                var rhs = ParseExpression();
                node = new BinaryNode(op.Text, node, rhs) { Line = node.Line, Column = node.Column };
            }
            else break;
        }
        return node;
    }

    private static bool IsCompoundAssign(CastTokenType t) =>
        t is CastTokenType.PlusEq or CastTokenType.MinusEq or CastTokenType.StarEq
          or CastTokenType.SlashEq or CastTokenType.PercentEq;

    // ── primaries ──────────────────────────────────────────────────────────────

    private Node ParsePrimary()
    {
        var tok = Cur;

        // Membership:  in collection ? value
        if (tok.Type == CastTokenType.In)
        {
            Advance();
            var coll = ParseRange();           // collection expression
            Expect(CastTokenType.Question, "'?' in membership test");
            var tested = ParseRange();
            return At(tok, new MembershipNode(coll, tested));
        }

        // Implicit loop variable `iter` (zero-based count) reads as an identifier.
        if (tok.Type == CastTokenType.Iter)
        {
            Advance();
            return At(tok, new IdentNode("iter"));
        }

        switch (tok.Type)
        {
            case CastTokenType.LParen:
                return ParseParenOrSequence();
            case CastTokenType.Lt:
                return ParseVector();
            case CastTokenType.LBracket:
                return ParseArray();
            case CastTokenType.LBrace:
                return ParseMap();
            case CastTokenType.Number:
                return ParseNumber();
            case CastTokenType.String:
                Advance();
                return At(tok, new StringNode(tok.CastValue));
            case CastTokenType.Dollar:
                Advance();
                var vname = Expect(CastTokenType.Ident, "variable name after '$'");
                return At(tok, new VarNode(vname.Text));
            case CastTokenType.Underscore:
                Advance();
                // _value (fallback, tight) vs bare _ (null). If a value head follows
                // with no separating token, it's a fallback construct.
                if (StartsValueHead())
                {
                    var val = ParseUnary();
                    return At(tok, new FallbackNode(val));
                }
                return At(tok, new NullNode());
            case CastTokenType.ScopeSigil:
                return ParseScopeChain();
            case CastTokenType.Ident:
                return ParseIdentOrCallOrNamespaced();
            default:
                throw new CastParseException($"unexpected token {tok.Type} '{tok.Text}'", tok.Line, tok.Column);
        }
    }

    private Node ParseNumber()
    {
        var tok = Expect(CastTokenType.Number, "number");
        double v = double.Parse(tok.Text, CultureInfo.InvariantCulture);
        return At(tok, new NumberNode(v, tok.Text));
    }

    private Node ParseIdentOrCallOrNamespaced()
    {
        var tok = Advance(); // Ident
        // namespaced id: mod:type:name  (Ident (: Ident)+)
        if (Check(CastTokenType.Colon) && Peek().Type == CastTokenType.Ident)
        {
            var segs = new List<string> { tok.Text };
            while (Check(CastTokenType.Colon) && Peek().Type == CastTokenType.Ident)
            {
                Advance(); // ':'
                segs.Add(Advance().Text);
            }
            return At(tok, new NamespacedIdNode(segs));
        }
        // call:  Name[..] / Name{..}
        if (Check(CastTokenType.LBracket))
        {
            Advance();
            var args = new List<Node>();
            if (!Check(CastTokenType.RBracket))
            {
                args.Add(ParseExpression());
                while (Match(CastTokenType.Comma)) args.Add(ParseExpression());
            }
            Expect(CastTokenType.RBracket, "']'");
            return At(tok, new CallNode(tok.Text, args, null));
        }
        if (Check(CastTokenType.LBrace))
        {
            Advance();
            if (IsBareKeyBrace())
            {
                // Name{key} — a named-key call/access with a single key value
                var keys = new List<Node> { ParseExpression() };
                while (Match(CastTokenType.Comma)) keys.Add(ParseExpression());
                Expect(CastTokenType.RBrace, "'}'");
                // represent as a call with positional key args (evaluator resolves)
                return At(tok, new CallNode(tok.Text, keys, null));
            }
            var pairs = ParsePairs(CastTokenType.RBrace);
            Expect(CastTokenType.RBrace, "'}'");
            return At(tok, new CallNode(tok.Text, null, pairs));
        }
        // bare identifier (a bare call / built-in / property name)
        return At(tok, new IdentNode(tok.Text));
    }

    private Node ParseParenOrSequence()
    {
        var open = Expect(CastTokenType.LParen, "'('");
        bool prevSup = _suppressBodyBracket; _suppressBodyBracket = false;
        try {
        if (Check(CastTokenType.RParen))
        {
            Advance();
            return At(open, new SequenceNode(Array.Empty<Node>()));
        }
        var first = ParseExpression();
        if (Check(CastTokenType.Comma))
        {
            var items = new List<Node> { first };
            while (Match(CastTokenType.Comma))
                items.Add(ParseExpression());
            Expect(CastTokenType.RParen, "')'");
            return At(open, new SequenceNode(items));
        }
        Expect(CastTokenType.RParen, "')'");
        return At(open, new GroupNode(first));
        } finally { _suppressBodyBracket = prevSup; }
    }

    private Node ParseArray()
    {
        var open = Expect(CastTokenType.LBracket, "'['");
        var elems = new List<Node>();
        if (!Check(CastTokenType.RBracket))
        {
            elems.Add(ParseExpression());
            while (Match(CastTokenType.Comma)) elems.Add(ParseExpression());
        }
        Expect(CastTokenType.RBracket, "']'");
        return At(open, new ArrayNode(elems));
    }

    private Node ParseMap()
    {
        var open = Expect(CastTokenType.LBrace, "'{'");
        var pairs = ParsePairs(CastTokenType.RBrace);
        Expect(CastTokenType.RBrace, "'}'");
        return At(open, new MapNode(pairs));
    }

    private List<PairNode> ParsePairs(CastTokenType closer)
    {
        var pairs = new List<PairNode>();
        if (Check(closer)) return pairs;
        do
        {
            var key = ParsePairKey();
            Expect(CastTokenType.Colon, "':' in pair");
            var val = ParseExpression();
            pairs.Add(new PairNode(key, val));
        } while (Match(CastTokenType.Comma, CastTokenType.Semicolon));
        return pairs;
    }

    private Node ParsePairKey()
    {
        // Ident / String / Expr (spec allows any value as a map key)
        if (Check(CastTokenType.Ident) && Peek().Type == CastTokenType.Colon)
        {
            var id = Advance();
            return new IdentNode(id.Text) { Line = id.Line, Column = id.Column };
        }
        if (Check(CastTokenType.String) && Peek().Type == CastTokenType.Colon)
        {
            var s = Advance();
            return new StringNode(s.CastValue) { Line = s.Line, Column = s.Column };
        }
        return ParseExpression();
    }

    // ── vectors ───────────────────────────────────────────────────────────────

    private VectorNode ParseVector()
    {
        var open = Expect(CastTokenType.Lt, "'<'");

        // Shorthand single-glyph bodies: <..>, <~>, <_>
        if (Check(CastTokenType.DotDot) && Peek().Type == CastTokenType.Gt)
        {
            Advance(); Advance();
            return At(open, new VectorNode(Array.Empty<Node>(), VectorShorthand.AllOpen));
        }
        if (Check(CastTokenType.Tilde) && Peek().Type == CastTokenType.Gt)
        {
            Advance(); Advance();
            return At(open, new VectorNode(Array.Empty<Node>(), VectorShorthand.AllRelative));
        }
        if (Check(CastTokenType.Underscore) && Peek().Type == CastTokenType.Gt)
        {
            Advance(); Advance();
            return At(open, new VectorNode(Array.Empty<Node>(), VectorShorthand.Empty));
        }

        var comps = new List<Node> { ParseComponent() };
        while (Match(CastTokenType.Comma))
            comps.Add(ParseComponent());
        Expect(CastTokenType.Gt, "'>' closing vector");
        return At(open, new VectorNode(comps, VectorShorthand.None));
    }

    private Node ParseComponent()
    {
        // Prefixed (~5, ^2, °30) tried before bare ~ (Finding 22)
        if (Check(CastTokenType.Tilde) || Check(CastTokenType.Caret) || Check(CastTokenType.Degree))
        {
            var pre = Advance();
            if (StartsAdditive())
            {
                var v = ParseAdditive();
                return At(pre, new PrefixedComponentNode(pre.Text, v));
            }
            // bare prefix component (e.g. bare ~ meaning relative-no-offset)
            return At(pre, new PrefixedComponentNode(pre.Text, null));
        }
        if (Check(CastTokenType.Underscore))
        {
            var u = Advance();
            return At(u, new NullNode());
        }
        // a range (axis region) or a plain additive value
        return ParseRange();
    }

    // ── small lookahead helpers ──────────────────────────────────────────────────

    // After consuming '{', decide whether this is a single-key access ({key}) or
    // a named-pairs map ({k: v}). Scan to the first top-level ':' before '}'; if
    // none, it's a bare-key access.
    private bool IsBareKeyBrace()
    {
        int depth = 0;
        for (int k = _i; k < _toks.Count; k++)
        {
            var t = _toks[k].Type;
            if (t is CastTokenType.LParen or CastTokenType.LBracket or CastTokenType.LBrace or CastTokenType.Lt) depth++;
            else if (t is CastTokenType.RParen or CastTokenType.RBracket or CastTokenType.Gt) { if (depth > 0) depth--; }
            else if (t == CastTokenType.RBrace)
            {
                if (depth == 0) return true;  // reached closing brace with no top-level ':'
                depth--;
            }
            else if (depth == 0 && t == CastTokenType.Colon) return false; // it's k:v pairs
            else if (t == CastTokenType.EndOfFile) return true;
        }
        return true;
    }

    private bool RangeFollowsAfterBang()
    {
        // After a leading '!', is there a range (a '..' before the expression ends)?
        // Cheap scan: from the token after '!', look for a DotDot before a closing
        // bracket / separator at the same nesting depth.
        int depth = 0;
        for (int k = _i + 1; k < _toks.Count; k++)
        {
            var t = _toks[k].Type;
            if (t is CastTokenType.LParen or CastTokenType.LBracket or CastTokenType.LBrace or CastTokenType.Lt) depth++;
            else if (t is CastTokenType.RParen or CastTokenType.RBracket or CastTokenType.RBrace or CastTokenType.Gt)
            {
                if (depth == 0) return false;
                depth--;
            }
            else if (depth == 0 && t == CastTokenType.DotDot) return true;
            else if (depth == 0 && (t is CastTokenType.Newline or CastTokenType.Semicolon
                     or CastTokenType.Comma or CastTokenType.EndOfFile
                     or CastTokenType.AmpAmp or CastTokenType.PipePipe
                     or CastTokenType.Question or CastTokenType.BangQuestion)) return false;
        }
        return false;
    }

    private bool StartsAdditive() => StartsValueHead();

    private bool StartsValueHead() => Cur.Type switch
    {
        CastTokenType.Number => true,
        CastTokenType.String => true,
        CastTokenType.Dollar => true,
        CastTokenType.Ident => true,
        CastTokenType.ScopeSigil => true,
        CastTokenType.Hash => true,
        CastTokenType.Bang => true,
        CastTokenType.Minus => true,
        CastTokenType.Tilde => true,
        CastTokenType.Caret => true,
        CastTokenType.Degree => true,
        CastTokenType.Underscore => true,
        CastTokenType.LParen => true,
        CastTokenType.LBracket => true,
        CastTokenType.LBrace => true,
        CastTokenType.Lt => true,
        _ => false
    };
}
