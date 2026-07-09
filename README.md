# create-sbx

An interactive TUI for creating [Docker Sandboxes](https://docs.docker.com/ai/sandboxes).

## Features

Supports the following options when creating a sandbox:
- **Sandbox name**: Defaults to the current directory name.
- **Agent**: Select from the standard list of agents available in `sbx` or enter a custom agent identifier (from a kit).
- **Workspace directory**: The local directory to mount, defaults to `.`.
- **Workspace mode**: `Direct` (mount the host directory) or `Clone` (clone the repository into the sandbox)
- **Template** (optional): Select a Docker image from a registry, or build a Dockerfile from a Git repository/local path.
- **Kits** (optional): Select kits to add from a Git repository.
- **Additional workspace directories** (optional): Extra directories to mount, each as read/write or read-only.

## Install

Install `create-sbx` as a persistent command available on your PATH.

**bash / zsh:**
```bash
curl -sSL https://raw.githubusercontent.com/geofflamrock/create-sbx/main/install.sh | bash
```

**PowerShell:**
```powershell
irm https://raw.githubusercontent.com/geofflamrock/create-sbx/main/install.ps1 | iex
```

Once installed, run `create-sbx` from anywhere.

## Requirements

- [sbx](https://docs.docker.com/ai/sandboxes)
