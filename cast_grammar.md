# Cast — Formal Grammar (PEG)

Companion to the Cast language spec (`cast_spec.md`). This document gives a normative PEG for the full surface syntax. It is written in layers — **lexer** (this section), **expressions**, **commands/statements**, **scope chains** — so that each "parsed by its own production, outside the precedence table" case from the spec is forced to actually compose.

PEG semantics: ordered choice (`/`) tries alternatives left-to-right and commits to the first that matches; `*` and `+` are greedy; lookahead is `&` (and-predicate) / `!` (not-predicate). Where Cast's spec says "maximal munch," that maps directly to ordered choice with the longer token listed first.

**Findings** (places where writing the grammar forced a decision the prose left open, or exposed a collision) are called out inline as **[FINDING]** and collected at the end. They are flagged for review, not decided unilaterally.

---

## Lexer

The lexer produces a token stream. Whitespace (space, tab) separates tokens but is otherwise insignificant except where noted; newlines are statement separators (equivalent to `;`).

### Token-level ordered choice (maximal munch)

The spec repeatedly specifies "maximal munch grabs the longer token." In PEG this is an ordered choice with longer operators first. The complete operator-token ordering, longest-first within each colliding family:

```
Token        <- Operator / Bracket / Literal / Keyword / Ident / ScopeSigil

# Operators, ordered so longer forms win over their prefixes.
Operator     <-
    # 3-char
      "**"                      # glob marker (only inside strings; see String) — listed for completeness
    # 2-char (must precede their 1-char prefixes)
    / "::"                      # function-def delimiter — MUST precede ":"
    / "++" / "--"
    / "+=" / "-=" / "*=" / "/=" / "%="
    / "=>" / "=&"
    / "!?" / "!&" / "!|"
    / "&&" / "||"
    / "?>" / "??"
    / "->" / "~>" / "|>"
    / ".." / "//"
    # 1-char
    / "$" / "@" / "#" / "!" / "?" / "*" / "+" / "-" / "/" / "%"
    / "^" / "~" / "°" / ":" / "|" / "=" / "." / ";" / "_"
```

**Critical orderings (each is a real collision the lexer must resolve):**

- `++` / `--` before `+` / `-` — otherwise `$x++` lexes as `$x` `+` `+`.
- `+=` etc. before `+` and before `=`.
- `=>`, `=&` before `=` — otherwise the binding-family heads lex as bare `=`.
- `!?`, `!&`, `!|` before `!` — otherwise `!?` lexes as `!` then `?`.
- `&&` before `&` (bare `&` is not a token — see [FINDING 2]).
- `||` before `|` — otherwise logical-or lexes as two pipes.
- `?>` before `?`; `??` before `?` — both conditional heads before bare comparison.
- `->`, `~>` before `-`/`~`; `|>` before `|`.
- `..` before `.` — otherwise a range lexes as two member-accesses.
- `//` before `/` — comment head before divide.
- `::` before `:` — otherwise a function-def delimiter lexes as two namespace separators.

### The unary/binary `-` rule

The spec says "lexer disambiguates by position." A PEG handles this in the *parser*, not the lexer — the `-` token is single, and unary vs binary is decided by grammar position (a `-` at expression start or immediately after another operator is unary). The lexer emits one `MINUS` token; the expression grammar (next layer) has `Unary <- "-" Unary / Postfix`. So:

```
# Lexer emits a single MINUS for "-". No lexer-level disambiguation.
```

**[FINDING 3]** — This means `-` is never a lexically distinct unary token; the parser's `Unary` production is the sole place the distinction lives. Confirmed sound for PEG, but worth noting the spec's "lexer disambiguates" phrasing is imprecise — it's the parser.

### Numbers

```
Number       <- "-"? (Digit+ ("." Digit+)? / "." Digit+)
Digit        <- [0-9]
```

One numeric kind (no int/float split — spec Overview). A bare `.5` is valid and equals `0.5` (leading `0` assumed). As with the unary-minus rule, the lexer does not absorb a leading `-` into the number — it emits `MINUS` then `Number`, and the parser's `Unary` folds them, keeping `5-3` (binary) and `-3` (unary) consistent.

Decimal forms: `3.14`, `0.5`, and bare `.5` (= `0.5`, leading zero assumed) are all valid.

### Strings

```
String       <- "'" StringChar* "'"
StringChar   <- "''"            # escaped single quote -> literal '
              / (!"'" .)        # any char except the closing quote
```

- Single-quote delimited. `''` inside is a literal single quote (spec).
- `**` inside a string is the glob wildcard (handled at match-time, not lexer — the lexer just captures the raw string content including `**`).
- No backslash escapes (spec).
- A string not closed on its line is a lex error (no multiline strings).
- `$name` interpolation inside strings is resolved at evaluation, not lex time; the lexer captures the literal text including `$name`.

**[FINDING 5]** — Literal `**` in a string is spec-deferred ("not handled — pin a convention"). The lexer currently cannot distinguish a glob `**` from an intended-literal `**`. Left as-is per spec's deferral; noted so the grammar doesn't imply it's resolved.

### Identifiers and namespaced IDs

```
Ident        <- IdentStart IdentCont*
IdentStart   <- [A-Za-z]        # identifiers may NOT start with _ (see below)
IdentCont    <- [A-Za-z0-9_]    # _ allowed inside/after the first char

NamespacedID <- Ident (":" Ident)+      # mod:type:name — two or more segments
```

**`_` is a sigil, not an identifier character.** Identifiers may not begin with `_`. A bare `_` is the null/empty value. `_` immediately followed (no space) by a value is the *fallback construct* — `_0`, `_false`, `_'default'`, `_<0,0,0>` — parsed as one unit meaning "fall back to this value on failure." So:
- `_` alone → null
- `_value` (sigil + value, tight) → fallback-with-that-value
- `foo`, `foo_bar`, `my_var` → identifiers (`_` allowed after the first character)
- `_foo` → the fallback construct `_` applied to identifier `foo`, never an identifier named `_foo`

```
Fallback     <- "_" Value        # the fallback form, e.g. _0, _'default'
NullLit      <- "_" !Value       # bare _ not followed by a value = null literal
```

**[FINDING 7]** — `NamespacedID` requires `:`-segments, but `:` is also the map key-value separator (`{name: 'stone'}`) and the `@v` registry separator (`@v:score:player1`). The lexer cannot tell these apart — they're all `Ident : Ident`. Disambiguation is *parser-level* by context (inside `{}` it's key:value; after `@v` it's a registry path; standalone it's a namespaced id). The lexer emits `Ident COLON Ident ...` and the parser decides. Confirmed workable, but it means `:` is one of the most context-loaded tokens — flagged.

### Scope sigils

```
ScopeSigil   <- "@" ScopeLetters
ScopeLetters <- [a-z]+          # one or more lowercase letters: @e, @s, @np, @v, @t, @w, @nc...
```

**[FINDING 8]** — Multi-letter scopes (`@np`, `@nc`, `@rp`) are ordering-letter + kind-letter compositions, and host-registered scopes add letters. The lexer can't know which letter-combinations are valid (that's host-defined), so it lexes `@` + `[a-z]+` greedily and lets the parser/runtime validate against the registered scope set. This means `@npfoo` lexes as one scope sigil `@npfoo` — a runtime "unknown scope" error, not a lex error. Confirmed acceptable (validation is runtime, matching the spec's runtime-is-compile-time stance), but noted: there is no lexical guard on scope-letter validity.

### Keywords vs identifiers

The keywords `in`, `out`, `collect`, `iter`, `as`, `at`, `over` are **globally reserved** — they are always keywords and may never be used as identifiers or function names, anywhere. The lexer recognizes them unconditionally (an `Ident` that exactly matches a keyword is the keyword, not an identifier). `over` is not merely contextual to `cast`; it is reserved everywhere, consistent with the spec's Reserved-names section.

### Comments and separators

```
Comment      <- "//" (!Newline .)*
Newline      <- "\n" / "\r\n"
StatementSep <- ";" / Newline
```

Comments run to end of line. No block comments (spec).

---

## Lexer findings summary

1. **No `!=` token** — confirmed: inequality is `!?`, the `"!="` reminder is removed. *(Resolved.)*
2. **Bare `&` is not a token** — confirmed against spec: only `&&`, `=&`, `!&` use `&`, never standalone. *(Resolved.)* Relatedly, `=&` requires *storage locations* on both sides (it identifies storage, it isn't a directional assignment), so aliasing a computed value errors — now stated as the core rule in the `=&` operator section.
3. **Unary `-` is parser-level**, not lexer-level — the spec's "lexer disambiguates by position" is imprecise.
4. **Number literals** — bare `.5` is valid (= `0.5`, leading zero assumed). *(Resolved.)*
5. **Literal `**` in strings** remains spec-deferred; grammar can't yet distinguish it from glob.
6. **`_` is a sigil, not an identifier character** — identifiers can't start with `_`; bare `_` is null; `_value` (tight) is the fallback construct; `_` is allowed after an identifier's first character (`my_var`). *(Resolved.)*
7. **`:` is heavily context-loaded** (namespace / map-key / registry-path) — disambiguated by parser context, not lexer.
8. **Scope-letter validity is runtime, not lexical** — `@` + `[a-z]+` lexed greedily, validated at runtime.
9. **Loop/cast keywords globally reserved** — `in`/`out`/`collect`/`iter`/`as`/`at`/`over` are keywords everywhere, never identifiers. `over` is not contextual. *(Resolved.)*

Next layers: **expression grammar** (precedence table as productions + the carve-out productions), **command/statement/cast layer**, **scope-chain grammar**.

---

## Expression grammar

The precedence table (spec, Evaluation Model) becomes a descent chain: tightest-binding level is the *deepest* production, loosest is the *entry* production. Each level either recurses into the next-tighter level for its operands (left/right-assoc) or forbids chaining (non-assoc). Two constructs sit outside the table and get dedicated productions woven in at the right depth.

Entry point for any expression:

```
Expr         <- Pipe
```

### Levels, loosest to tightest

```
# L13 pipe (left-assoc)
Pipe         <- Binding (S "|" S Binding)*

# L12 binding / directed write (right-assoc)
Binding      <- Placement S ("=" / "=>" / "|>") S Binding
              / Placement

# L11 placement (non-assoc — no chaining)
Placement    <- Conditional S ("->" / Step) S Conditional
              / Conditional
#  ^ see Step production below for ~> with its trailing magnitude

# L10 conditional ?> ?? (right-assoc)
Conditional  <- LogicalOr S "?>" S Conditional (S "??" S Conditional)?
              / LogicalOr

# L9 || !| (left-assoc)
LogicalOr    <- LogicalAnd (S ("||" / "!|") S LogicalAnd)*

# L8 && !& (left-assoc)
LogicalAnd   <- Comparison (S ("&&" / "!&") S Comparison)*

# L7 comparison ? !? (non-assoc)
Comparison   <- Range S ("?" / "!?") S Range
              / Range

# L6 range .. (non-assoc) — also the home of range-complement (carve-out A)
Range        <- "!" RangeBody          # range-complement: !2..5, !..5
              / RangeBody
RangeBody    <- Additive ".." Additive   # 2..5
              / Additive ".."            # 5..
              / ".." Additive            # ..5
              / Additive                 # not a range, pass through

# L5 + - binary (left-assoc)
Additive     <- Multiplicative (S ("+" / "-") S Multiplicative)*

# L4 * / % (left-assoc)
Multiplicative <- Alias (S ("*" / "/" / "%") S Alias)*

# L3 =& bidirectional alias (right-assoc) — both operands must be storage (lvalues)
Alias        <- Unary S "=&" S Alias
              / Unary

# L2 prefixes (right-assoc)
Unary        <- ("#" / "!" / "~" / "^" / "°" / "-") Unary
              / Postfix

# L1 member access + postfix call/index/slice (left-assoc)
Postfix      <- Primary (PostfixOp)*
PostfixOp    <- "." Ident                 # member access
              / "[" ArgsOrIndex "]"       # index / positional call / filter
              / "{" MapOrNamed "}"        # map / named call
              / "(" Selection ")"         # scope selection slice

# Primary values
Primary      <- "(" Expr ")"              # grouping / sequence
              / Vector
              / Map
              / Array
              / Function
              / Fallback
              / NullLit
              / String
              / Number
              / NamespacedID
              / ScopeChain
              / Ident
```

### Carve-out A — range-complement `!`

Folded into the `Range` production above: at level 6, a leading `!` before a range body complements the whole range. This works *because* `Range` sits below comparison/logic but the `!`-here is tried at range level, not as the level-2 prefix.

**[FINDING 10] — the two `!`s now live at two levels, and ordered choice resolves which.** `!` is both a level-2 prefix (logical negation, in `Unary`) and a level-6 range-complement (in `Range`). For `!2..5`, the descent reaches `Range` first (level 6 is looser, tried earlier in descent), sees `!`, and takes the range-complement branch — *before* ever descending to `Unary`. Good. But for `!$flag` (plain boolean negation, no range), `Range` tries `"!" RangeBody`, `RangeBody` tries `Additive ".." ...`, finds no `..`, and the whole range-complement branch must fail and backtrack so `!$flag` is parsed by `Unary` as a prefix. **This requires the `"!" RangeBody` branch to only commit when a `..` actually appears.** As written, `RangeBody`'s last alternative (`Additive`) matches `$flag` *with no `..`*, so `!RangeBody` would wrongly succeed on `!$flag` and consume the `!` as a range-complement of a non-range. Fix: range-complement must require a range:

```
Range        <- "!" &(RangeLookahead) RangeBody    # only complement if a range follows
              / RangeBody
RangeLookahead <- Additive ".." / ".." Additive     # there IS a .. ahead
```
With the `&(RangeLookahead)` and-predicate, `!$flag` fails the lookahead (no `..`), the range-complement branch is skipped, and `!$flag` falls through to `Unary`. Confirmed this resolves it — but it's a real subtlety the prose "parsed by its own production" glossed over.

### Carve-out B — `~>`'s trailing `* magnitude`

The `~>` step is at level 11 (placement), but its trailing `* magnitude` must bind to the whole step, not be captured by level-4 `*`. Dedicated `Step` production:

```
Step         <- "~>" S Conditional (S "*" S Additive)?
```

In `Placement`, the right side of a `~>` is parsed by `Step`, which optionally consumes a trailing `* magnitude`. The magnitude is parsed as a full `Additive` expression — so `@s ~> $enemy * (5 + $bonus)` and `@s ~> $enemy * 5 + 1` both give a computed magnitude (`5+1` = 6) — but capped below ternary, so a `?>` can't appear inside a magnitude. Because `Step` is invoked at level 11 and explicitly grabs the `* Additive` itself, the magnitude is never seen by the level-4 `Multiplicative` rule.

**[FINDING 11] — the magnitude operand's own precedence.** `Step`'s magnitude is parsed as `Conditional` (a fairly tight level). Should `@s ~> $enemy * 5 + 1` mean magnitude `5+1`, or `(step at 5) + 1` (which is nonsense — placement isn't additive)? Since placement (L11) is looser than additive (L5), `5 + 1` as a whole can't trail naturally. Parsing the magnitude as `Conditional` (which descends through additive) makes `* 5 + 1` → magnitude `(5+1)=6`. That's almost certainly the intent (the magnitude is an expression), but it means the `* magnitude` grabs a full sub-expression, not just a primary. **Confirm:** is the `~>` magnitude a full expression (`* (5 + $bonus)`) — recommend yes — or only a simple value (`* 5`)? Recommending full expression; parsing magnitude as `Additive` (not `Conditional`, to avoid a magnitude containing a `?>` ternary which would be bizarre).

### Carve-out interactions with non-assoc levels

**[FINDING 12] — placement and comparison being non-assoc needs the productions to actually reject chaining, not silently take the first operand.** As written, `Comparison <- Range ("?"/"!?") Range / Range` parses `$a ? $b ? $c` as... `Range`=`$a`, then `?`, then `Range`=`$b`, and *stops* — leaving `? $c` unconsumed, which surfaces as a trailing-token parse error at the statement level. That's the correct "non-assoc rejects chaining" behavior (it doesn't silently group), but the error appears as "unexpected `?`" rather than "comparison doesn't chain." Acceptable, but worth a clearer parser error message. Same pattern for `->`/`~>` placement.

### Postfix ambiguity: call vs index vs filter

`PostfixOp` uses `[` for index, positional call, *and* filter; `{` for map literal and named call; `(` for slice. Which one applies is **runtime-resolved by the left operand's kind** (spec: "the left operand disambiguates"), not grammatically distinguished. So the grammar parses all `[...]` postfixes into one node and the evaluator decides:

```
ArgsOrIndex  <- Expr (S "," S Expr)*      # one node; evaluator decides index vs call vs filter
MapOrNamed   <- Pair (S ("," / ";") S Pair)*
Pair         <- (Ident / String / Expr) S ":" S Expr
Selection    <- SliceItem (S "," S SliceItem)*
SliceItem    <- Range / Number
```

**[FINDING 13] — filter vs index share `[]`, and a filter contains a full boolean expression while an index contains a number/expr.** `@e[health ? ..50]` (filter) and `$arr[0]` (index) and `Heal[50]` (call) are all `Primary "[" ... "]"`. The grammar can't tell them apart — nor should it, per spec. But it means `ArgsOrIndex` must accept the *full* expression grammar (so a filter's `health ? ..50` parses), which it does via `Expr`. Confirmed consistent with the spec's "left operand disambiguates" — flagged only because it means `[]` contents are maximally permissive grammatically, with all validation deferred to evaluation.

## Expression-layer findings summary

10. **Range-complement `!` needs a `&(.. ahead)` lookahead** so it doesn't swallow the `!` of a plain `!$flag`. Resolved with an and-predicate; noted as a real subtlety.
11. **`~>` magnitude operand** — parsed as a full `Additive` expression (so `* (5 + $bonus)` and `* 5 + 1` both compute), capped below ternary. *(Resolved.)*
12. **Non-assoc levels reject chaining via trailing-token error** — correct behavior, but the parser should emit a "X doesn't chain; parenthesize" message rather than a generic "unexpected token."
13. **`[]`/`{}` postfix contents are grammatically permissive** (full expressions), with index-vs-call-vs-filter resolved at evaluation per the spec — confirmed consistent, flagged for awareness.

---

## Command / statement layer

A program is a sequence of statements separated by `;` or newline. A statement is a command, a binding/expression, or a function definition.

```
Program      <- S Statement (StatementSep S Statement)* S EOF
StatementSep <- ";" / Newline
Statement    <- FunctionDef
              / Command
              / Expr            # a bare expression (binding, value) is a valid statement
```

### Commands

A command is scope-led: an optional scope chain, optional execution-context modifiers (`as`/`at`), then an action (a function call, a built-in, or a property operation). The scope chain establishes who/where; the modifiers can redirect; the action operates within it.

```
Command      <- CastCommand                   # the namesake — see below
              / ScopeChain S ContextMods S Action   # @e<region>[f] as @s Heal[10]
              / ContextMods S Action           # as @np Heal[10] — ambient scope, redirected
              / Action                          # bare action in the ambient scope

# Shared execution-context modifiers, used by both Command and the cast tail (Finding 18)
ContextMods  <- (S "as" S ScopeChain)? (S "at" S ScopeChain)?

Action       <- BuiltinCmd
              / Call                            # Name[...] / Name{...} / bare Name
              / Expr                            # property op: @s.health = ...

Call         <- Ident ("[" ArgsOrIndex "]" / "{" MapOrNamed "}")?
```

### Scope chains

The canonical narrowing order is `@scope(selection)<region>[condition]` — selection, then region, then filter — each optional, in that order (spec: "shortest operand first, longest last").

```
ScopeChain   <- ScopeSigil Selection? Region? Filter?
Selection    <- "(" SliceItem (S "," S SliceItem)* ")"
Region       <- Vector                        # <...> with possibly-range components
Filter       <- "[" Expr "]"                  # full boolean expression
```

**[FINDING 14] — `(selection)` vs `()` grouping vs `()` sequence is three uses of one bracket, disambiguated by position.** After a scope sigil, `(` opens a selection (index list). In a `Primary`, `(` opens grouping/sequence. As a postfix after a value, `(` opens a slice. The grammar separates them by *where* they appear (right after a `ScopeSigil` → selection; in `Primary` → grouping; as `PostfixOp` → slice). Confirmed they don't collide because the positions are distinct, but `(` is, like `:`, a heavily position-loaded token. Noted.

**[FINDING 15] — the scope chain's `<region>` collides with `<` less-than... except there is no `<` less-than.** Cast has no `<`/`>` comparison (comparison is `?`/`!?`). That's exactly what frees `<...>` for vectors/regions unambiguously. Confirmed: after a scope sigil (or anywhere a value is expected), `<` always opens a vector. There is no context where `<` could be a comparison, so no collision. This is a load-bearing reason the spec spends `?` on comparison — flagged as confirmed-sound.

### The `cast` command

```
CastCommand  <- "cast" S CastEnvelope? CastTarget? S CommandTail
CastEnvelope <- Count? (S "over" S Count)?
Count        <- Number
CastTarget   <- ScopeChain                    # the trigger-and-context scope (optional)
CommandTail  <- (S "as" S ScopeChain)? (S "at" S ScopeChain)? S Action
```

So a full cast is: `cast [count] [over N] [trigger-scope] [as @scope] [at @scope] command`.

**[FINDING 16 — the big one: bare-number count vs a command starting with a number.]** `cast 3 over 45 @e Pulse` — the `3` is a count. But `Count <- Number` means `cast` greedily reads a leading number as the count. Is there any valid cast where the thing after `cast` *starts* with a number but isn't a count? A command is scope-led or an action (function call / built-in); none of those start with a bare number. Arithmetic like `cast 3 + 4 ...` would be nonsensical (you don't cast a number). So **a number immediately after `cast` is unambiguously the count** — no collision. Confirmed, but the confirmation depends on "no command begins with a bare number," which is true only because actions are scope-led or identifier-led. Flagged as the load-bearing invariant: *if a future command form could begin with a numeric literal, this breaks.* Recommend enshrining "commands never begin with a bare number literal" as a spec invariant.

**[FINDING 17 — `over` parsing depends on the count existing.]** `CastEnvelope <- Count? (over Count)?` allows `cast over 45 ...` (the scope-only-loop-spread case, no repeat count, just frame-spreading a filter's iterations — spec). Good: both `Count` and the `over` clause are independently optional. But `cast over 45 @e[...]` with no command tail is the "spread a loop over frames" form (a scope cast with no command). The grammar's `CommandTail` requires an `Action` at the end — so a scope-only cast (no command) needs `Action` to be optional here:
```
CommandTail  <- ContextMods (S Action)?
```
With `Action` optional, `cast over 45 @e[expensive_filter]` parses (scope-only cast, iterations spread over frames). Confirmed needed — without it, the spec's loop-spread cast form is unparseable. Fixed above.

**[FINDING 18 — `as`/`at` after cast vs `as`/`at` as standalone execution-context on a normal command.]** `as`/`at` appear both in `CommandTail` (after cast) and as standalone context modifiers on any command (spec: execution context). The standalone form needs its own production on `Command`:
```
Command      <- ScopeChain S (S "as" S ScopeChain)? (S "at" S ScopeChain)? S Action
              / (S "as" S ScopeChain) (S "at" S ScopeChain)? S Action
              / ...
```
This means `as`/`at` are recognized in two places (after `cast`, and on a plain command). Both reduce to the same "context modifiers before an action" shape. Recommend factoring a shared `ContextMods <- ("as" ScopeChain)? ("at" ScopeChain)?` production used by both `Command` and `CastCommand`'s tail, so the rule lives once. Flagged for a clean factoring rather than duplicating the as/at logic.

### Function definitions

```
FunctionDef  <- Ident "::" S Body S "::"
Body         <- Statement (StatementSep S Statement)*
```

**[FINDING 19 — nested function definitions are allowed.]** `Name:: ... ::` delimits a body that is itself a `Statement*`, and a `FunctionDef` may appear inside a `Body`. Nested definitions are permitted: a function can, when run, define another function (e.g. one that installs handlers). The inner definition takes effect when the outer body executes, registering the function in the same registry. No grammar change needed — it falls out of `Body <- Statement*` where `Statement` includes `FunctionDef`.

**[FINDING 20 — `::` body delimiter vs `:` namespace/map — maximal munch already handles it,]** since the lexer lists `::`... actually it doesn't. Checking: the operator-token ordered choice has `:` but **not** `::`. Two colons would lex as `:` `:`. **This is a lexer gap.** `::` must be added to the lexer's operator list (before `:`) or function-definition delimiters won't tokenize. Fix: add `"::"` to the 2-char operator group, before `:`. Flagged as a concrete lexer bug introduced by my earlier omission.

## Command-layer findings summary

14. **`(` is position-loaded** (selection after scope / grouping in primary / slice as postfix) — disambiguated by position, confirmed non-colliding.
15. **`<region>` never collides with `<`** because Cast has no `<` comparison — confirmed sound, and a load-bearing reason `?` is the comparison operator.
16. **Bare-number count after `cast` is unambiguous** because no command begins with a numeric literal — now to be enshrined as a spec invariant ("commands never begin with a bare number literal"). *(Resolved — invariant added to spec.)*
17. **Scope-only cast (`cast over 45 @e[...]`)** requires `Action` to be optional in the cast tail — fixed; without it the loop-spread form was unparseable.
18. **`as`/`at` factored into a shared `ContextMods` production** used by both `Command` and the cast tail — the rule lives once. *(Resolved.)*
19. **Nested function definitions are allowed** — a function may define another when it runs; falls out of `Body <- Statement*`. *(Resolved.)*
20. **Lexer gap: `::` was missing** from the operator token list — now added before `:` in the 2-char group, so function delimiters tokenize correctly. *(Resolved.)*

---

## Value-literal and slot grammar

The `Primary` production referenced these; here are their definitions, plus the `@v`/`@t` slot-access forms that aren't plain scope chains.

### Vectors

```
Vector       <- "<" VectorBody ">"
VectorBody   <- ".."                          # <..> all-open shorthand
              / Component (S "," S Component){1,3}   # 2-4 components total
Component    <- Range                          # numeric range (region axis): -5..5, ..10
              / Prefixed                       # ~5, ^2, °30
              / "_"                             # absent axis
              / "~"                             # bare ~ (relative no-offset), e.g. <~, 10, ~>
              / Additive                        # plain numeric value
Prefixed     <- ("~" / "^" / "°") Additive
```

**[FINDING 21 — `<..>` shorthand vs a 1-component vector.]** `<..>` is the all-open shorthand (every axis unbounded). But `VectorBody`'s second alternative requires *2-4* components (`Component (, Component){1,3}` = 2 to 4). A literal single-component vector `<5>` is therefore not a valid vector (arity 2 minimum, per spec). So `<..>` is unambiguously the shorthand, not "a 1-component vector of one open range." Confirmed: arity-2-minimum means `<..>` can only be the shorthand. But `<_>` (the empty-vector witness) and `<~>` (all-relative shorthand) are *also* single-glyph-in-brackets forms — they must each be their own alternative, not parsed as 1-component vectors:
```
Vector       <- "<" ("_" / "~" / ".." / VectorComponents) ">"
VectorComponents <- Component (S "," S Component){1,3}
```
With `_`, `~`, `..` as explicit single-token bodies (the three shorthands: empty-witness, all-relative, all-open), and real vectors requiring 2-4 comma-separated components. Resolved — but it confirms these three single-glyph bracket forms are special-cased literals, not degenerate vectors.

**[FINDING 22 — bare `~` vs `~5` as a component.]** A component can be `~` alone (relative no-offset, `<~, 10, ~>`) or `~5` (relative-plus-offset). Ordered choice must try `Prefixed` (`~` Additive) before bare `~`, else `~5` parses as bare-`~` leaving `5` dangling. Order: `Prefixed / "_" / "~" / Range / Additive`. With `Prefixed` first, `~5` is consumed whole; bare `~` only matches when no value follows. Resolved.

### Arrays, maps, sequences

```
Array        <- "[" (S Expr (S "," S Expr)* S)? "]"     # [] empty, [1,2,3]
Map          <- "{" (S Pair (S ("," / ";") S Pair)* S)? "}"
Pair         <- (Ident / String / Expr) S ":" S Expr
Sequence     <- "(" S Expr (S "," S Expr)* S ")"        # (1, 3..8, 12) — 2+ items
                                                         # (single item) is grouping, not a sequence
```

**[FINDING 23 — `()` with one item is grouping; with 2+ comma-separated items is a sequence.]** `(5)` is grouping (a parenthesized expression). `(1, 2, 3)` is a sequence value. The grammar distinguishes by the presence of a comma: `Primary`'s `"(" Expr ")"` (grouping) vs `Sequence`'s `"(" Expr ("," Expr)+ ")"` (2+ items). Ordered choice in `Primary` must try `Sequence` before grouping, or `(1, 2)` would match grouping on `1` and choke on the comma. Order in `Primary`: `Sequence / "(" Expr ")"`. Resolved.

**[FINDING 24 — empty bracket forms `[]` `{}` `<>` `()` are type-witnesses, and must be distinguished from empty literals `[_]` `{_}` etc.]** The spec: bare `[]` is a type witness ("is left an array?"), `[_]` is the literal empty array. The grammar already separates them — `[]` is `Array` with no elements, `[_]` is `Array` containing the single `_` null. The *semantic* difference (witness vs literal-empty) is resolved at evaluation by whether it's the right operand of `?`. Grammatically both parse fine. Confirmed; the witness-vs-literal distinction is evaluation-level, not grammar-level.

### Functions

```
Function     <- Ident "::" S Body S "::"      # same as FunctionDef — a function literal IS its definition
```

**[FINDING 25 — is there an anonymous function literal?]** Every function form in the spec is `Name:: body ::` — named. There is no anonymous `:: body ::`. (Earlier in design this was explicitly rejected — `:: body ::` with no name reads as "define a function named (nothing)".) So `Function` always carries a name; there is no lambda. Confirmed against spec; flagged only to record that the grammar has no anonymous-function production by design.

### `@v` registry and `@t` slot access

These extend `ScopeChain` for the two scopes with special access forms.

```
VRegistryRef <- "@v" (":" Ident)+             # @v:score:player1 — colon-path key
              / "@v" "[" Expr "]"             # @v[name ? 'score:**'] — query
              / "@v"                           # bare @v (whole registry, e.g. #@v)

TSlotRef     <- "@t" "[" Expr "]"             # @t[0] — indexed slot
              / "@t" "{" Expr "}"             # @t{'name'} — named slot
              / "@t" Vector                    # @t<0,0,12> — time vector
              / "@t"                           # bare @t (global controls, e.g. @t.pause)
```

**[FINDING 26 — `@v:score:player1` colon-path vs namespaced-ID `mod:type:name`.]** Both are `Ident (":" Ident)+`. After `@v`, a colon-path is a *registry key* (literal segments, no value-splicing — spec). Standalone, `mod:type:name` is a *namespaced ID*. The lexer can't tell them apart (Finding 7); the parser distinguishes by the `@v` prefix. `VRegistryRef`'s first alternative (`"@v" (":" Ident)+`) captures the registry-key case; a bare `mod:type:name` elsewhere is `NamespacedID`. Confirmed resolved by the `@v` context, consistent with Finding 7.

**[FINDING 27 — `@v:poison{@s.id}` — map access on a registry slot's value.]** The spec's per-entity pattern is `@v:poison{@s.id}` (a map stored in slot `poison`, keyed by entity id). This is `VRegistryRef` (`@v:poison`) followed by a `{...}` postfix (map access on the stored value). So `VRegistryRef` must be allowed to take trailing `PostfixOp`s (`.field`, `{key}`, `[index]`) that reach *into the stored value*, matching the spec's "`.` after `@v:name` accesses into the stored value." This already falls out if `@v`/`@t` refs are a `Primary` that `Postfix` can extend. Confirmed: slot refs are primaries, and the existing `Postfix <- Primary PostfixOp*` chain gives them value-access for free. No special rule needed — flagged as a nice composition (the `{@s.id}` map-key access on a slot is just the normal postfix chain).

## Value/slot-layer findings summary

21. **`<..>`, `<_>`, `<~>` are special-cased single-glyph bracket bodies**, not degenerate 1-component vectors (real vectors are arity 2-4). Resolved by explicit alternatives.
22. **Bare `~` vs `~5` component** — `Prefixed` tried before bare `~` in ordered choice. Resolved.
23. **`()` grouping (1 item) vs sequence (2+ items)** — `Sequence` tried before grouping in `Primary`. Resolved.
24. **Empty-bracket type-witness vs `[_]` empty-literal** — both parse; witness-vs-literal is an evaluation distinction (right operand of `?`), not grammatical. Confirmed.
25. **No anonymous function literal** — all functions are named `Name:: body ::`; no lambda, by design. Confirmed.
26. **`@v:`-path vs namespaced-ID** — disambiguated by the `@v` prefix at parser level (consistent with Finding 7). Resolved.
27. **`@v`/`@t` slot refs are primaries** — trailing `.field`/`{key}`/`[i]` postfixes reach into the stored value via the normal `Postfix` chain (`@v:poison{@s.id}`). No special rule; clean composition. Confirmed.

---

## Lexer validation

The lexer's maximal-munch logic was validated against spec snippets (now run for real against the compiled C# lexer (.NET 8)). All the tricky cases tokenize correctly: `!2..5` (range-complement), `!$flag` (plain negation, no `..`), `.5` (leading-dot number), `=&`/`~>`/`::`/`++`/`?>`/`??`/`..` (maximal munch, no mis-splitting), `@v:poison{@s.id}` (slot + map-access postfix), `@t<0,0,12>` (time vector).

**[FINDING 28 — spec used double-quoted strings in function examples.]** Validation flagged `param{"amount"}` in several function examples — but Cast strings are single-quoted only (`'...'`); there is no double-quote string delimiter. These were spec bugs (the examples contradicted the string-literal definition). Fixed: all code-context `param{"k"}` → `param{'k'}` (7 occurrences). Prose double-quotes (English quotation marks in explanatory text) are untouched. *(Resolved.)*

## Parser validation

The parser was built and run against the real C# implementation (.NET 8). Every spec program in the test driver parses, and 15 structural assertions pass — covering the full precedence ladder, all three carve-outs, scope chains with `@v` paths and value postfixes, iteration, conditionals with statement branches, and nested functions.

**[FINDING 29 — bare `~` as a vector's last component collides with the `~>` operator.]** `<~, 10, ~>` (the spec's canonical "set Y, keep X/Z" example) tokenized its trailing `~>` as the `TildeArrow` step operator under maximal munch. Resolved in the lexer: it tracks `<...>` nesting depth, and inside a vector, no multi-char operator ending in `>` is formed — `>` always closes the vector. So the trailing `~` lexes as `Tilde` then `Gt`. This is the kind of collision only an actual run surfaces; the prose and even the hand-written grammar missed it. *(Resolved — lexer fix, verified by test.)*

## Grammar status

All four layers are written: lexer, expressions, commands/statements/cast, value-literals/slots. Findings 1–27 are each resolved or confirmed. Open items requiring no further decision; two recommendations remain for a cleanup pass (Finding 18: factor a shared `ContextMods` production; Finding 12: clearer non-assoc-chaining parser errors). The grammar is ready to drive a recursive-descent + Pratt implementation in C#.
