#!/usr/bin/env bash
# Build + run Cast's lexer test without NuGet (the sandbox blocks api.nuget.org).
# Compiles directly with the Roslyn compiler (csc) against the .NET 8 reference
# assemblies bundled with the SDK. When you have normal NuGet access, you can use
# the .csproj files with `dotnet build` instead.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

ROOT="$(cd "$(dirname "$0")" && pwd)"
REFDIR=$(find /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref -type d -name net8.0 | head -1)
CSC=$(find /usr/lib/dotnet/sdk -name csc.dll | head -1)

REFS=""
for dll in "$REFDIR"/*.dll; do REFS="$REFS -r:$dll"; done

mkdir -p "$ROOT/build"

# All library sources + the chosen driver (+ any mock_*.cs companion helpers).
LIB_SRCS=$(find "$ROOT/src/Cast.Lang" -name '*.cs' | tr '\n' ' ')
DRIVER="${1:-$ROOT/tests/lex_driver.cs}"
COMPANIONS=$(find "$ROOT/tests" -name 'mock_*.cs' | tr '\n' ' ')
OUT="$ROOT/build/test.dll"

dotnet "$CSC" -nologo -target:exe -out:"$OUT" $REFS $LIB_SRCS "$DRIVER" $COMPANIONS

cat > "$ROOT/build/test.runtimeconfig.json" << 'JSON'
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" }
  }
}
JSON

dotnet "$OUT"
