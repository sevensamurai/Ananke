# Ananke.Roles — Architecture

> Declarative agent role/persona scaffolding, with Slack channel-to-role routing.

## Role

Lets a host application declare a set of named agent roles (persona, review behaviour,
escalation policy) and route incoming requests — from Slack or any platform — to the
workflow for the resolved role, instead of hand-wiring routing per channel/team.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IAgentRoleCatalog` / `AgentRoleCatalog` — lookup and registration of named roles; the default dictionary-backed in-memory implementation — `src/Ananke.Roles/Roles/IAgentRoleCatalog.cs` / `src/Ananke.Roles/Roles/AgentRoleCatalog.cs`
2. `AgentRole` — describes a reusable role/persona for a studio workflow: name, system prompt, model alias, `ReviewPolicy`, `EscalationPolicy` — `src/Ananke.Roles/Roles/AgentRole.cs`
3. `StudioHostBuilder` — fluent builder that wires studio roles, workflows, and supporting services into an `IServiceCollection` — `src/Ananke.Roles/Studio/StudioHostBuilder.cs`
4. `RoleAwareMessageHandler` — `IPlatformMessageHandler` that resolves the role via `SlackChannelMap`, routes via `StudioRouter`, and runs the resolved role's workflow — `src/Ananke.Roles/Slack/RoleAwareMessageHandler.cs`

---

## Dependencies

- `Ananke.Design` (project) — `WorkflowManifest`, manifest-driven workflow construction
- `Ananke.Organics` (project) — `IRequestRouter`, work-review gates
- `Ananke.Platforms` (project) — `IPlatformMessageHandler`, `PlatformInteractionEvent`
- `Ananke.Platforms.Slack` (project) — Slack-specific routing helpers
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Roles` (`Roles/`) | `AgentRole`, `IAgentRoleCatalog`, `AgentRoleCatalog`, `ReviewPolicy`, `EscalationPolicy`, `RoleManifestFactory` |
| `Ananke.Roles` (`Slack/`) | `SlackChannelMap`, `RoleAwareMessageHandler`, `SlackApprovalCallback` |
| `Ananke.Roles` (`Studio/`) | `StudioOptions`, `StudioHostBuilder`, `StudioRouter` |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `AgentRole` | Sealed record | Describes a reusable role/persona for a studio workflow — name, system prompt, model alias, `ReviewPolicy`, `EscalationPolicy` | `src/Ananke.Roles/Roles/AgentRole.cs` |
| `IAgentRoleCatalog` / `AgentRoleCatalog` | Interface / class | Lookup and registration of named roles. `AgentRoleCatalog` is the default dictionary-backed in-memory implementation | `src/Ananke.Roles/Roles/IAgentRoleCatalog.cs` / `src/Ananke.Roles/Roles/AgentRoleCatalog.cs` |
| `ReviewPolicy` | Sealed record | Review requirements attached to a role (which `IWorkReviewGate` applies, quorum settings) | `src/Ananke.Roles/Roles/ReviewPolicy.cs` |
| `EscalationPolicy` | Sealed record | Thresholds that trigger escalation to a secondary model or reviewer lane | `src/Ananke.Roles/Roles/EscalationPolicy.cs` |
| `RoleManifestFactory` | Sealed class | Projects an `AgentRole` definition into a `WorkflowManifest` for the design-tooling layer | `src/Ananke.Roles/Roles/RoleManifestFactory.cs` |
| `StudioOptions` | Sealed record | Configuration for a studio host — includes `ChannelRoleMap` (channel id/name → role name) | `src/Ananke.Roles/Studio/StudioOptions.cs` |
| `StudioHostBuilder` | Sealed class | Fluent builder that wires studio roles, workflows, and supporting services into an `IServiceCollection` | `src/Ananke.Roles/Studio/StudioHostBuilder.cs` |
| `StudioRouter` | Sealed class | `IRequestRouter` decorator — checks keyword overrides before delegating to an inner router | `src/Ananke.Roles/Studio/StudioRouter.cs` |
| `SlackChannelMap` | Sealed class | Strongly-typed wrapper over `StudioOptions.ChannelRoleMap` — resolves a Slack channel id/name to an `AgentRole` | `src/Ananke.Roles/Slack/SlackChannelMap.cs` |
| `RoleAwareMessageHandler` | Sealed class | `IPlatformMessageHandler` — resolves the role via `SlackChannelMap`, routes via `StudioRouter`, runs the resolved role's workflow | `src/Ananke.Roles/Slack/RoleAwareMessageHandler.cs` |
| `SlackApprovalCallback` | Sealed class | Bridges Slack block-action / view-submission interaction events into `WorkReviewDecision` values for a `CallbackWorkReviewGate` | `src/Ananke.Roles/Slack/SlackApprovalCallback.cs` |

## Request Flow

```
Inbound Slack message
  → RoleAwareMessageHandler.HandleAsync
      → SlackChannelMap.Resolve(channelId)        → AgentRole
      → StudioRouter.RouteAsync(message)           → workflow name
      → run the resolved role's Workflow<TState>

Reviewer clicks Approve / Revise / Reject in Slack
  → SlackApprovalCallback.HandleInteractionAsync(PlatformInteractionEvent)
      → WorkReviewDecision
      → CallbackWorkReviewGate resumes the parked review
```

See [Guide 07 — Human-in-the-Loop](../../docs/guides/07-human-in-the-loop.md) for the
review-gate side of this flow, and the
[MiniAgencyDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/MiniAgencyDemo)
for a complete worked example.
