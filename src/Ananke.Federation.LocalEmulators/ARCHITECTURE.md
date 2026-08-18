# Ananke.Federation.LocalEmulators — Architecture

> Local emulators and deterministic stubs for `IPlatformNativeExecutor` capabilities.

## Role

Lets a manifest declaring `PlatformNative` tools (e.g. `bing_grounding`,
`code_interpreter`, `web_search`) run locally without cloud credentials — for
`nnke-platform up --emulate <platform>` (planned — ADR CLI-7), CI, and the local design
loop. Every capability listed
in `platform-capabilities.json` is covered, either by a real emulator (HTTP client,
local process, or in-memory store) or by a documented stub returning deterministic
fixture data.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `DefaultPlatformNativeExecutors` — the only public entry point — `Register(registry)`
   pre-registers every built-in emulator/stub into a `PlatformNativeExecutorRegistry` — `src/Ananke.Federation.LocalEmulators/DefaultPlatformNativeExecutors.cs`

---

## Dependencies

- `Ananke.Federation` (project) — `IPlatformNativeExecutor`, `PlatformNativeExecutorRegistry`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `DefaultPlatformNativeExecutors` | Static class | The only public entry point — `Register(registry)` pre-registers every built-in emulator/stub into a `PlatformNativeExecutorRegistry` | `src/Ananke.Federation.LocalEmulators/DefaultPlatformNativeExecutors.cs` |

All 18 individual executors (`BashExecutor`, `WebSearchExecutor`, `WebFetchExecutor`,
`CodeExecutionExecutor`, `TextEditorExecutor`, `FileSearchExecutor`, `MemoryExecutor`,
`DeepResearchExecutor`, `GoogleDataServiceExecutor`, the Azure stubs in
`src/Ananke.Federation.LocalEmulators/AzureStubExecutors.cs`, and the UI/search stubs in
`src/Ananke.Federation.LocalEmulators/UiStubExecutors.cs`) are `internal`
implementation details reached only through `DefaultPlatformNativeExecutors.Register`.

## Real emulators vs. stubs

| Category | Capabilities | Behaviour |
|---|---|---|
| **Real emulators** (local tooling / network) | `web_search`, `web_fetch`, `bash`, `text_editor`, `code_execution`, `code_interpreter`, `vertex_extension:code_interpreter`, `file_search`, `memory`, `memory_bank`, `memory_profiles`, `memory_search` | Backed by an HTTP client, local process, or in-memory store — real behaviour, no cloud account needed |
| **Stubs** (deterministic, no network/credentials needed) | `bing_search`, `bing_grounding`, `bing_custom_search`, `azure_ai_search`, `sharepoint`, `sharepoint_grounding`, `microsoft_fabric`, `google_search`, `google_search_retrieval`, `url_context`, `computer_use`, `browser_automation`, `image_generation`, `deep_research`, `bigquery`, `spanner`, `bigtable`, `pubsub`, `maps`, `artifact_service`, `capture_structured_outputs` | Returns fixed fixture data — exercises the tool-calling path without requiring the real platform |
