# Ananke.Analyzers — Architecture

> This repository's own Roslyn rule: `ConfigureAwait(false)` in library code. Never packed.

## Role

A .NET Standard 2.0 Roslyn analyzer assembly, wired into every project in this repository by
`src/Directory.Build.props` and packed into no NuGet package. The analyzers a product gets are in
`Ananke.Orchestration.Analyzers`.

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `ConfigureAwaitAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE_ASYNC_001` (warning) when an internal/private async method awaits without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt — see [`Ananke.Orchestration` ARCHITECTURE § ConfigureAwait Convention](../Ananke.Orchestration/ARCHITECTURE.md#configureawait-convention). | `src/Ananke.Analyzers/ConfigureAwaitAnalyzer.cs` |

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (Roslyn APIs)
- `Microsoft.CodeAnalysis.Analyzers`

## Target

- .NET Standard 2.0 (required for Roslyn analyzers)
