<!-- topic: uv-setup, tags: python, uv, uvx, setup, tools, dotnet -->
# uv & uvx Setup for .NET Developers

A quick-start guide for C#/.NET developers who need to run Python-based CLI tools
(like OpenClaw skills) without becoming Python developers.

**You do not need to learn Python.** You just need one tool installed: **uv**.

---

## What is uv?

**uv** is to Python what `dotnet` is to .NET — a single binary that manages
runtimes, packages, and tool execution. Think of it as:

| .NET concept | Python / uv equivalent |
|---|---|
| `dotnet` CLI | `uv` |
| `dotnet tool install` | `uv tool install` |
| `dotnet tool run` / `npx` | `uvx` (runs tools without installing) |
| NuGet | PyPI |
| `.csproj` / `global.json` | `pyproject.toml` |
| .NET SDK | Python interpreter (uv manages this for you) |

The key command is **`uvx`** — it downloads a Python tool into an isolated
environment, runs it, and cleans up. No global installs, no virtual environments,
no `pip install` steps.

```
uvx some-tool "arguments here"
```

That's it. If the tool isn't cached locally, `uvx` fetches it automatically.

---

## Installation

### Windows (recommended: winget)

```powershell
winget install astral-sh.uv
```

### Windows (PowerShell one-liner)

```powershell
irm https://astral.sh/uv/install.ps1 | iex
```

### macOS / Linux

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

### Verify

```powershell
uv --version
```

You should see something like `uv 0.7.x`. That's all you need — uv handles
Python itself. You do **not** need to install Python separately.

---

## Using uvx (the only command you need)

### Run a tool without installing it

```powershell
uvx airbnb-search "Denver, CO" --checkin 2025-08-01 --checkout 2025-08-03
```

First run downloads the tool and a Python interpreter into uv's cache.
Subsequent runs are instant.

### Get JSON output (for programmatic use)

Most OpenClaw-style tools support a `--output json` flag:

```powershell
uvx airbnb-search "Aspen, CO" --checkin 2025-02-01 --checkout 2025-02-03 --output json
```

This is what the Ananke skill catalog bridge uses — it captures stdout as
`ToolResult.Ok(json)`.

### Install a tool globally (optional)

If you use a tool frequently:

```powershell
uv tool install airbnb-search
airbnb-search "Seattle, WA" --checkin 2025-09-01 --checkout 2025-09-03
```

This puts the command on your PATH permanently.

---

## How it works under the hood

```
uvx airbnb-search "Denver, CO" --output json
     │
     ├─ 1. Checks local cache for 'airbnb-search' package
     ├─ 2. If missing: downloads from PyPI into isolated venv
     ├─ 3. Ensures a compatible Python interpreter exists (downloads if needed)
     ├─ 4. Runs the tool's entry point with your arguments
     └─ 5. Streams stdout/stderr back to your terminal
```

- **No global pollution** — each tool gets its own isolated environment
- **No version conflicts** — tools can't interfere with each other
- **Auto-cleanup** — uv manages the cache; `uv cache clean` frees space

---

## Calling from C# (Process.Start)

For the Ananke skill catalog, this is how a `uvx` tool gets called from .NET:

```csharp
using System.Diagnostics;

var psi = new ProcessStartInfo
{
    FileName = "uvx",
    Arguments = "airbnb-search \"Denver, CO\" --checkin 2025-08-01 --checkout 2025-08-03 --output json",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};

using var process = Process.Start(psi)!;
var stdout = await process.StandardOutput.ReadToEndAsync();
var stderr = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();

if (process.ExitCode == 0)
    Console.WriteLine(stdout);  // JSON output
else
    Console.Error.WriteLine($"Failed (exit {process.ExitCode}): {stderr}");
```

In the Ananke framework, this pattern is wrapped behind `ToolDefinition.Execute`
— the agent never knows it's calling a Python tool.

---

## Common issues

### "uvx is not recognized"

The installer adds uv to your PATH, but your current terminal session may not
see it yet. Fix:

```powershell
# Refresh PATH in current PowerShell session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

# Or just open a new terminal
```

### First run is slow

Normal. uv is downloading the tool package and (possibly) a Python interpreter.
Subsequent runs use the cache and are fast.

### "No Python interpreters found"

uv usually manages Python automatically. If it doesn't:

```powershell
uv python install
```

This downloads a standalone Python build — it does **not** modify any system
Python installation.

### Proxy / corporate firewall

uv respects standard environment variables:

```powershell
$env:HTTPS_PROXY = "http://proxy.corp.example:8080"
uvx airbnb-search "Denver, CO" --output json
```

---

## Cache management

```powershell
# See where uv stores things
uv cache dir

# See cache size
uv cache clean --dry-run

# Clear everything (next uvx call will re-download)
uv cache clean
```

Default cache locations:
- **Windows:** `%LOCALAPPDATA%\uv\cache`
- **macOS:** `~/Library/Caches/uv`
- **Linux:** `~/.cache/uv`

---

## Quick reference

| Task | Command |
|---|---|
| Install uv | `winget install astral-sh.uv` |
| Run a tool (no install) | `uvx <tool> <args>` |
| Install a tool globally | `uv tool install <tool>` |
| List installed tools | `uv tool list` |
| Uninstall a tool | `uv tool uninstall <tool>` |
| Install Python (if needed) | `uv python install` |
| Clear cache | `uv cache clean` |
| Check version | `uv --version` |

---

## Further reading

- [uv documentation](https://docs.astral.sh/uv/)
- [uvx tool runner](https://docs.astral.sh/uv/concepts/tools/)
- [OpenClaw skills registry](https://github.com/openclaw/skills)
- [ADR-001: Skill Catalog](../adr/001-skill-catalog.md) — how Ananke integrates these tools
