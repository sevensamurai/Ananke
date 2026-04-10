# Ananke — Architecture (Meta-Package)

> Convenience meta-package that bundles `Ananke.Orchestration`,
> `Ananke.StateMachine`, and the Bridge integration layer.

## Role

Single NuGet install to get the workflow engine, state machine, and the
bridge layer that allows a `Workflow` completion to trigger a state machine
transition (and vice versa).

## Dependencies

- `Ananke.Orchestration` (project)
- `Ananke.StateMachine` (project)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `BridgeExtensions` | Static class | Extension methods to wire workflow → state machine trigger patterns |
| `StateMachineTriggerJob` | Class | `IJob` that fires a state machine transition when executed in a workflow |
| `WorkflowCompletionTrigger` | Class | Listens for workflow completion and triggers a state machine action |

## When to Use

Install `Ananke` (this package) when you need **both** the workflow engine
and the state machine in the same application and want them to coordinate.
If you only need workflows, install `Ananke.Orchestration` directly.
