#!/usr/bin/env bash
set -euo pipefail

if ! command -v dotnet &>/dev/null; then
    echo "Error: dotnet is required but not installed."
    echo "Install it from https://dot.net"
    exit 1
fi

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

curl -sSL "https://raw.githubusercontent.com/geofflamrock/create-sbx/main/create-sbx.cs" -o "$tmpdir/create-sbx.cs"
dotnet run "$tmpdir/create-sbx.cs"
