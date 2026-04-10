# Ananke.Design — Architecture

> Design-time tooling — YAML DSL import for workflow topology
> and Mermaid diagram export.

## Role

Enables declarative workflow definitions via YAML manifests.
Parse a YAML file into a `WorkflowManifest`, scaffold it into a
`Workflow<TState>`, and export any workflow as a Mermaid diagram
for documentation.

## Dependencies

- `Ananke.Orchestration` (project)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `WorkflowManifest` | Record | Parsed YAML manifest — jobs, connections, model aliases, system prompts |
| `WorkflowDslParser` | Class | Parses YAML text into `WorkflowManifest` |
| `WorkflowScaffold` | Class | Converts a `WorkflowManifest` into a `Workflow<TState>` instance |
| `ModelResolver` | Class | Resolves model alias strings (from YAML) to `IAgentModel` instances |
| `WorkflowDiagramExtensions` | Static class | `workflow.ToMermaid()` — generates Mermaid flowchart from workflow topology |
| `AgentTextResponse` | Record | Wrapper for text responses in design-time scaffolding |
| `ConnectionLine` | Record | Parsed DSL connection line (source → target with optional condition) |
