# nnke-platform-anthropic — Architecture

> Standalone adapter plugin that teaches `nnke-platform` how to deploy to Claude
> Managed Agents.

## Role

A small, independently-installed executable — not referenced as a library. Installing
it (`AdapterInstaller`) copies itself and its dependencies into `nnke-platform`'s
adapter probe directory and writes a `claude.adapter.json` `AdapterManifest`. From then
on, `nnke-platform` loads this assembly at startup; a `[ModuleInitializer]` self-registers
a `"claude"` deployer factory, enabling `nnke-platform deploy --platform claude` without
`nnke-platform` itself ever referencing `Ananke.Federation.Anthropic`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `AdapterInstaller` — copies the build output into `AnankePaths.AdaptersDirectory` and writes `claude.adapter.json`; `--uninstall` removes both — `src/nnke-platform-anthropic/AdapterInstaller.cs`
2. `ModuleInit` — `[ModuleInitializer]` that runs when `nnke-platform` loads this DLL; registers the `"claude"` factory into `FederationDeployerRegistry` — `src/nnke-platform-anthropic/ModuleInit.cs`
3. `Program` — entry point; calls `AdapterInstaller.Run(args)` — `src/nnke-platform-anthropic/Program.cs`

---

## Dependencies

- `Ananke.Federation.Anthropic` (project) — `ClaudeCredentialProvider`, `ClaudeDeployer`
- `Ananke.Federation` (project) — `FederationDeployerRegistry`, `AdapterManifest`, `AnankePaths`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `Program` | Entry point | Calls `AdapterInstaller.Run(args)` — the entire executable's job is install/uninstall | `src/nnke-platform-anthropic/Program.cs` |
| `AdapterInstaller` | Internal static class | Copies the build output into `AnankePaths.AdaptersDirectory` and writes `claude.adapter.json`; `--uninstall` removes both | `src/nnke-platform-anthropic/AdapterInstaller.cs` |
| `ModuleInit` | Internal static class | `[ModuleInitializer]` — runs when `nnke-platform` loads this DLL; registers a `"claude"` factory into `FederationDeployerRegistry` that builds a `ClaudeCredentialProvider` (from `ANTHROPIC_API_KEY`) and a `ClaudeDeployer` | `src/nnke-platform-anthropic/ModuleInit.cs` |

## Notes

- Targets the Anthropic Beta managed-agents API (`agents-2025-05-14`) — see
  `src/Ananke.Federation.Anthropic/README.md` for the Beta dependency notice.
- `ANTHROPIC_API_KEY` may be unset at module-init time; `ClaudeCredentialProvider`
  re-reads the environment variable at credential-resolution time.
- Structurally identical to `nnke-platform-azure` and `nnke-platform-google` — only the
  registered platform key, credential provider, and deployer type differ.
