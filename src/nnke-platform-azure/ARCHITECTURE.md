# nnke-platform-azure — Architecture

> Standalone adapter plugin that teaches `nnke-platform` how to deploy to Azure AI
> Agent Service.

## Role

A small, independently-installed executable — not referenced as a library. Installing
it (`AdapterInstaller`) copies itself and its dependencies into `nnke-platform`'s
adapter probe directory and writes an `azure-ai.adapter.json` `AdapterManifest`. From
then on, `nnke-platform` loads this assembly at startup; a `[ModuleInitializer]`
self-registers an `"azure-ai"` deployer factory, enabling
`nnke-platform deploy --platform azure-ai` without `nnke-platform` itself ever
referencing `Ananke.Federation.Azure`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `AdapterInstaller` — copies the build output into `AnankePaths.AdaptersDirectory` and writes `azure-ai.adapter.json`; `--uninstall` removes both — `src/nnke-platform-azure/AdapterInstaller.cs`
2. `ModuleInit` — `[ModuleInitializer]` that runs when `nnke-platform` loads this DLL; registers the `"azure-ai"` factory into `FederationDeployerRegistry` — `src/nnke-platform-azure/ModuleInit.cs`
3. `Program` — entry point; calls `AdapterInstaller.Run(args)` — `src/nnke-platform-azure/Program.cs`

---

## Dependencies

- `Ananke.Federation.Azure` (project) — `AzureAgentCredentialProvider`, `AzureAgentDeployer`
- `Ananke.Federation` (project) — `FederationDeployerRegistry`, `AdapterManifest`, `AnankePaths`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `Program` | Entry point | Calls `AdapterInstaller.Run(args)` — the entire executable's job is install/uninstall | `src/nnke-platform-azure/Program.cs` |
| `AdapterInstaller` | Internal static class | Copies the build output into `AnankePaths.AdaptersDirectory` and writes `azure-ai.adapter.json`; `--uninstall` removes both | `src/nnke-platform-azure/AdapterInstaller.cs` |
| `ModuleInit` | Internal static class | `[ModuleInitializer]` — runs when `nnke-platform` loads this DLL; registers an `"azure-ai"` factory into `FederationDeployerRegistry` that builds an `AzureAgentCredentialProvider` (from `AZURE_AI_ENDPOINT`) and an `AzureAgentDeployer` | `src/nnke-platform-azure/ModuleInit.cs` |

## Notes

- Requires `AZURE_AI_ENDPOINT` (your Azure AI Foundry project endpoint) to be set
  before `nnke-platform deploy` runs — `ModuleInit` throws `InvalidOperationException`
  immediately if it is missing, rather than failing later at deploy time.
- Structurally identical to `nnke-platform-anthropic` and `nnke-platform-google` — only
  the registered platform key, credential provider, and deployer type differ.
