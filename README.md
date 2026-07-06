# create-sbx

An interactive CLI for creating [Docker Sandboxes](https://docs.docker.com/ai/sandboxes).

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

## Features

`create-sbx` walks you through an interactive prompt to configure a sandbox:

- **Sandbox name** - defaults to the current directory name
- **Agent** - `claude`, `codex`, `copilot`, `cursor`, `droid`, `gemini`, `kiro`, `opencode`, `docker-agent`, `shell` (agent-less), or a custom agent identifier
- **Workspace directory** - the local directory to mount, defaults to `.`
- **Workspace mode** - `Direct` (mount the host directory) or `Clone` (clone the repository into the sandbox)
- **Template** (optional) - a Docker image from a registry, or a Dockerfile from a Git repository or local path, built before the sandbox is created
- **Kits** (optional) - one or more kits selected from a Git repository, added to the sandbox
- **Additional workspace directories** (optional) - extra directories to mount, each as read/write or read-only

## Requirements

- [sbx](https://docs.docker.com/ai/sandboxes)
