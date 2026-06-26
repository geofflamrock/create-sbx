#!/usr/bin/env bash
set -euo pipefail

if ! command -v dotnet &>/dev/null; then
    echo "Error: dotnet is required but not installed."
    echo "Install it from https://dot.net"
    exit 1
fi

INSTALL_DIR="$HOME/.local/share/create-sbx"
BIN_DIR="$HOME/.local/bin"

mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"

curl -sSL "https://raw.githubusercontent.com/geofflamrock/create-sbx/main/create-sbx.cs" -o "$INSTALL_DIR/create-sbx"
chmod +x "$INSTALL_DIR/create-sbx"

ln -sf "$INSTALL_DIR/create-sbx" "$BIN_DIR/create-sbx"

if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
    echo ""
    echo "Warning: $BIN_DIR is not in your PATH."
    echo "Add the following to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
    echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi

echo ""
echo "create-sbx installed successfully. Run 'create-sbx' to get started."
