#!/usr/bin/env bash
# ── Arena cross-language benchmark ────────────────────────────────────────────
# Same deterministic arena (portable LCG, identical physics) in all five. Only the
# rule portion differs: inline in C#/Python/JS/Lua, interpreted in Cast. Gates on a
# shared invariant (births/deaths/cursings/live/checksum) before reporting — a
# realistic mixed host+script workload, unlike bench/ which is pure compute.
set -e
cd "$(dirname "$0")/.."
N="${1:-30}"; T="${2:-300}"; SEED="${3:-12345}"

REFDIR=$(find /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref -type d -name net8.0 | head -1)
CSC=$(find /usr/lib/dotnet/sdk -name csc.dll | head -1)
REFS=""; for d in "$REFDIR"/*.dll; do REFS="$REFS -r:$d"; done
RC='{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" } } }'
mkdir -p build

echo ">> building..."
dotnet "$CSC" -nologo -optimize+ -target:exe -out:build/cs_arena.dll $REFS bench_arena/cs_arena.cs 2>/dev/null
echo "$RC" > build/cs_arena.runtimeconfig.json
dotnet "$CSC" -nologo -optimize+ -target:exe -out:build/cast_arena.dll $REFS src/Cast.Lang/*.cs bench_arena/cast_arena.cs 2>/dev/null
echo "$RC" > build/cast_arena.runtimeconfig.json
cp bench_arena/arena_rules.cast build/arena_rules.cast

echo ">> running ($N creatures, $T ticks, seed $SEED)..."
echo ""
R=$(mktemp)
dotnet build/cast_arena.dll "$N" "$T" "$SEED" >> "$R"
dotnet build/cs_arena.dll   "$N" "$T" "$SEED" >> "$R"
python3 bench_arena/py_arena.py "$N" "$T" "$SEED" >> "$R"
node    bench_arena/js_arena.js "$N" "$T" "$SEED" >> "$R"
lua5.4  bench_arena/lua_arena.lua "$N" "$T" "$SEED" >> "$R"

# invariant gate: columns 5-9 (births deaths cursings live checksum) must match
INV=$(awk -F'\t' '{print $5"/"$6"/"$7"/"$8"/"$9}' "$R" | sort -u)
if [ "$(echo "$INV" | wc -l)" -ne 1 ]; then
    echo "!! INVARIANT MISMATCH — implementations diverge:"
    awk -F'\t' '{printf "   %-12s %s/%s/%s/%s/%s\n",$1,$5,$6,$7,$8,$9}' "$R"
    rm -f "$R"; exit 1
fi
echo ">> invariant verified (births/deaths/cursings/live/checksum): $INV"
echo ""

CAST_MS=$(awk -F'\t' '$1=="cast"{print $4}' "$R")
printf "%-12s %10s %14s\n" "language" "ms" "vs Cast"
printf "%-12s %10s %14s\n" "--------" "--" "-------"
sort -t$'\t' -k4 -n "$R" | while IFS=$'\t' read -r lang n t ms b d c l chk; do
    rel=$(awk -v c="$CAST_MS" -v m="$ms" 'BEGIN{printf (m>0)?"%.1fx":"-", c/m}')
    printf "%-12s %10s %14s\n" "$lang" "$ms" "$rel"
done
rm -f "$R"
