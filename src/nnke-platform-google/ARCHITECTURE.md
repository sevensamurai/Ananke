# nnke-platform-google — Architecture

> Standalone adapter plugin that teaches `nnke-platform` how to deploy to Google's
> Gemini Enterprise Agent Platform (formerly Vertex AI).

## Role

A small, independently-installed executable — not referenced as a library. Installing
it (`AdapterInstaller`) copies itself and its dependencies into `nnke-platform`'s
adapter probe directory and writes a `vertex-ai.adapter.json` `AdapterManifest`. From
then on, `nnke-platform` loads this assembly at startup; a `[ModuleInitializer]`
self-registers a `"vertex-ai"` deployer factory, enabling
`nnke-platform deploy --platform vertex-ai` without `nnke-platform` itself ever
referencing `Ananke.Federation.Google`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `AdapterInstaller` — copies the build output into `AnankePaths.AdaptersDirectory` and writes `vertex-ai.adapter.json`; `--uninstall` removes both — `src/nnke-platform-google/AdapterInstaller.cs`
2. `ModuleInit` — `[ModuleInitializer]` that runs when `nnke-platform` loads this DLL; registers the `"vertex-ai"` factory into `FederationDeployerRegistry` — `src/nnke-platform-google/ModuleInit.cs`
3. `Program` — entry point; calls `AdapterInstaller.Run(args)` — `src/nnke-platform-google/Program.cs`

---

## Dependencies

- `Ananke.Federation.Google` (project) — `VertexAICredentialProvider`, `VertexAIDeployer`
- `Ananke.Federation` (project) — `FederationDeployerRegistry`, `AdapterManifest`, `AnankePaths`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `Program` | Entry point | Calls `AdapterInstaller.Run(args)` — the entire executable's job is install/uninstall | `src/nnke-platform-google/Program.cs` |
| `AdapterInstaller` | Internal static class | Copies the build output into `AnankePaths.AdaptersDirectory` and writes `vertex-ai.adapter.json`; `--uninstall` removes both | `src/nnke-platform-google/AdapterInstaller.cs` |
| `ModuleInit` | Internal static class | `[ModuleInitializer]` — runs when `nnke-platform` loads this DLL; registers a `"vertex-ai"` factory into `FederationDeployerRegistry` that builds a `VertexAICredentialProvider` (from `GOOGLE_CLOUD_PROJECT` / `GOOGLE_CLOUD_LOCATION`) and a `VertexAIDeployer` | `src/nnke-platform-google/ModuleInit.cs` |

## Notes

- Requires `GOOGLE_CLOUD_PROJECT` to be set before `nnke-platform deploy` runs —
  `ModuleInit` throws `InvalidOperationException` immediately if it is missing.
  `GOOGLE_CLOUD_LOCATION` defaults to `us-central1` if unset.
- The `VertexAI*` class names (`VertexAICredentialProvider`, `VertexAIDeployer`) are
  preserved for backwards compatibility after Google's rebrand to Gemini Enterprise
  Agent Platform — see `src/Ananke.Federation.Google/README.md`.
- Structurally identical to `nnke-platform-anthropic` and `nnke-platform-azure` — only
  the registered platform key, credential provider, and deployer type differ.
