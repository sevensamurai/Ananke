# Ananke.Roles

`Ananke.Roles` provides role catalogs, manifest generation helpers, and studio-oriented wiring for persona-driven Ananke hosts.

## Included

- `AgentRole` and supporting review/escalation policies
- `AgentRoleCatalog` in-memory catalog implementation
- `RoleManifestFactory` for turning roles into `WorkflowManifest` instances
- `StudioRouter` and `StudioHostBuilder` for lightweight application wiring

## Slack binding helpers (`Ananke.Roles.Slack`)

The `Ananke.Roles.Slack` sub-namespace adds optional Slack-specific helpers that bridge
Slack channels and interactions to the role layer. These types take a direct dependency on
`Ananke.Platforms.Slack`; the rest of `Ananke.Roles` remains platform-agnostic.

> **Future note:** These helpers may be extracted into a dedicated `Ananke.Roles.Slack`
> integration package in a later release so that `Ananke.Roles` depends only on
> `Ananke.Platforms` abstractions.

### `SlackChannelMap`

Wraps `StudioOptions.ChannelRoleMap` with a typed lookup against `IAgentRoleCatalog`.

```csharp
var map = new SlackChannelMap(studioOptions, roleCatalog);

if (map.TryResolveRole(channelId, out var role))
    Console.WriteLine(role!.Name);   // e.g. "writer"

IReadOnlyList<string> knownChannels = map.MappedChannelIds;
```

Channels that map to a role name that does not exist in the catalog are silently excluded
from `MappedChannelIds` and return `false` from `TryResolveRole`.

### `RoleAwareMessageHandler`

An `IPlatformMessageHandler` that routes each incoming `PlatformMessage` to a workflow
named after the resolved role, falling back to a configured default when the channel is
not mapped.

```csharp
var handler = new RoleAwareMessageHandler(channelMap, studioRouter, defaultWorkflow: "default");
```

Override `OnWorkflowRoutedAsync` to plug in your own workflow execution logic.

### `SlackApprovalCallback`

Bridges Slack block-action interactions (Approve / Revise / Reject buttons rendered by
`SlackApprovalBlocks`) into `WorkReviewDecision` values for a `CallbackWorkReviewGate`.

```csharp
var gate = new CallbackWorkReviewGate(async (item, ct) =>
{
    // post SlackApprovalBlocks.Build(item) to Slack here …
    return await pendingDecision.Task;
});

var callback = new SlackApprovalCallback(async (decision, ct) =>
{
    pendingDecision.TrySetResult(decision);
    await Task.CompletedTask;
});

// In OnInteractionAsync:
await callback.HandleInteractionAsync(interaction, ct);
```

`HandleInteractionAsync` returns `true` when the action id matches one of the canonical
approval ids (`ananke_approve`, `ananke_revise`, `ananke_reject`) and `false` otherwise,
letting the caller forward unrecognised interactions elsewhere.
