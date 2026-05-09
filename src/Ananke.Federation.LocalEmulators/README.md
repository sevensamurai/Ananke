# Ananke.Federation.LocalEmulators

Local emulators for Ananke Federation platform-native capabilities.

Enables the **local design loop**: author and test
workflows that declare `ToolExecutionMode.PlatformNative` capabilities on your
developer machine or in CI — without credentials and without deploying to a
managed agent platform (Claude, Foundry, Gemini Enterprise).

## Quick start

```csharp
using Ananke.Federation.Execution;
using Ananke.Federation.LocalEmulators;

var registry = new PlatformNativeExecutorRegistry();
DefaultPlatformNativeExecutors.Register(registry);

// Patch a ToolKit so platform-native tools run locally
registry.ApplyTo(toolKit, platform: "azure-ai");
```

## Capability coverage

| Capability | Kind | Notes |
|---|---|---|
| `web_search` | Real | DuckDuckGo Lite — no API key required |
| `web_fetch` | Real | `HttpClient` GET |
| `bash` | Real | OS shell in a temp sandbox directory |
| `text_editor` | Real | File I/O scoped to the bash sandbox |
| `code_execution` / `code_interpreter` / `vertex_extension:code_interpreter` | Real | Subprocess via bash; Python / Node / C# (`dotnet-script`) |
| `file_search` | Real | Keyword search over a configurable root directory |
| `memory` / `memory_bank` / `memory_profiles` / `memory_search` | Real | In-process concurrent dictionary store |
| `bing_search` / `bing_grounding` / `bing_custom_search` | Stub | Deterministic fixture — for test use |
| `azure_ai_search` | Stub | In-memory fixture |
| `sharepoint` / `sharepoint_grounding` / `microsoft_fabric` | Stub | Fixture document set |
| `google_search` / `google_search_retrieval` / `url_context` | Stub | Fixture results |
| `computer_use` / `browser_automation` | Stub | Records action history; no real browser |
| `image_generation` | Stub | Returns fixture placeholder URL |
| `deep_research` | Stub | Composes `web_search` + `web_fetch` in N steps |
| `bigquery` / `spanner` / `bigtable` / `pubsub` / `maps` / `artifact_service` | Stub | Fixture data per service |
| `capture_structured_outputs` | Stub | Passthrough |
