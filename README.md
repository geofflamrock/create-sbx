# create-sbx

An interactive CLI for creating [Docker Sandboxes](https://docs.docker.com/ai/sandboxes). There is some support in `sbx` for creating sandboxes through the TUI but it doesn't yet support more advanced features like kits and templates. It can be hard to remember the syntax for these for the cli which is where this tool comes in.

## Features

Supports the following options when creating a sandbox:
- **Sandbox name**: Defaults to the current directory name.
- **Agent**: Select from the standard list of agents available in `sbx` or enter a custom agent identifier (from a kit).
- **Workspace directory**: The local directory to mount, defaults to `.`.
- **Workspace mode**: `Direct` (mount the host directory) or `Clone` (clone the repository into the sandbox)
- **Template** (optional): Select a Docker image from a registry, or build a Dockerfile from a Git repository/local path.
- **Kits** (optional): Select kits to add from a Git repository.
- **Additional workspace directories** (optional): Extra directories to mount, each as read/write or read-only.
- **Customizing disk sizes (optional)**: Set the environment variables which control disk size for the root, Docker and Clone disks.

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
