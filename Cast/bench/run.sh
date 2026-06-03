#!/usr/bin/env bash
# ── Cast speed-reference harness ──────────────────────────────────────────────
# Runs the identical poison-tick workload in all five implementations, verifies
# every implementation produced the SAME checksum (proving identical work), and
# prints a comparison table. Cast runs on its tree-walking C# interpreter, so the
# Cast row measures interpreter overhead vs native/interpreted execution — not a
# language-design comparison. See bench/README.md.
set -e
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
N="${1:-2000}"
T="${2:-200}"

REFDIR=$(find /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref -type d -name net8.0 | head -1)
CSC=$(find /usr/lib/dotnet/sdk -name csc.dll | head -1)
REFS=""; for d in "$REFDIR"/*.dll; do REFS="$REFS -r:$d"; done
RC='{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" } } }'

mkdir -p build
echo ">> building Cast interpreter benchmark..."
dotnet "$CSC" -nologo -optimize+ -target:exe -out:build/cast_bench.dll $REFS src/Cast.Lang/*.cs bench/cast_bench.cs 2>/dev/null
echo "$RC" > build/cast_bench.runtimeconfig.json
cp bench/workload.cast build/workload.cast
echo ">> building native C# baseline..."
dotnet "$CSC" -nologo -optimize+ -target:exe -out:build/cs_native.dll $REFS bench/cs_native.cs 2>/dev/null
echo "$RC" > build/cs_native.runtimeconfig.json

echo ">> running ($N entities x $T ticks = $((N*T)) entity-ticks each)..."
echo ""

# collect raw results: lang  N  T  ms  Mops/s  checksum  total_dmg
RESULTS=$(mktemp)
dotnet build/cast_bench.dll "$N" "$T"  >> "$RESULTS"
dotnet build/cs_native.dll "$N" "$T"   >> "$RESULTS"
python3 bench/py_bench.py "$N" "$T"    >> "$RESULTS"
node    bench/js_bench.js "$N" "$T"    >> "$RESULTS"
lua5.4  bench/lua_bench.lua "$N" "$T"  >> "$RESULTS"

# verify all checksums identical
CHECKSUMS=$(awk -F'\t' '{print $6}' "$RESULTS" | sort -u)
NCK=$(echo "$CHECKSUMS" | wc -l)
if [ "$NCK" -ne 1 ]; then
    echo "!! CHECKSUM MISMATCH — implementations are not doing identical work:"
    awk -F'\t' '{printf "   %-12s checksum=%s\n", $1, $6}' "$RESULTS"
    rm -f "$RESULTS"; exit 1
fi
echo ">> checksum verified identical across all five: $CHECKSUMS"
echo ""

# pretty table, sorted fastest-first, with relative-to-cast column
CAST_MS=$(awk -F'\t' '$1=="cast"{print $4}' "$RESULTS")
printf "%-12s %10s %12s %14s\n" "language" "ms" "Mops/s" "vs Cast"
printf "%-12s %10s %12s %14s\n" "--------" "--" "------" "-------"
sort -t$'\t' -k4 -n "$RESULTS" | while IFS=$'\t' read -r lang n t ms mops ck td; do
    rel=$(awk -v c="$CAST_MS" -v m="$ms" 'BEGIN{printf (m>0)? "%.0fx" : "-", c/m}')
    printf "%-12s %10s %12s %14s\n" "$lang" "$ms" "$mops" "$rel"
done
echo ""
echo "checksum=$CHECKSUMS  total_damage=$(awk -F'\t' 'NR==1{print $7}' "$RESULTS")"
rm -f "$RESULTS"
