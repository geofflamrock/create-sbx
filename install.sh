#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="$HOME/.local/share/create-sbx"
BIN_DIR="$HOME/.local/bin"

OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

case "$OS" in
  linux)
    case "$ARCH" in
      x86_64)  ARTIFACT="create-sbx-linux-amd64" ;;
      aarch64) ARTIFACT="create-sbx-linux-arm64" ;;
      *) echo "Error: Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  darwin)
    case "$ARCH" in
      x86_64) ARTIFACT="create-sbx-macos-amd64" ;;
      arm64)  ARTIFACT="create-sbx-macos-arm64" ;;
      *) echo "Error: Unsupported architecture: $ARCH"; exit 1 ;;
    esac
    ;;
  *)
    echo "Error: Unsupported OS: $OS"
    exit 1
    ;;
esac

mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"

curl -sSL "https://github.com/geofflamrock/create-sbx/releases/latest/download/$ARTIFACT" -o "$INSTALL_DIR/create-sbx"
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
