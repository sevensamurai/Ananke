# Ananke.Federation.Conformance.Tests

Contract test suite for `IFederationDeployer` and `IManagedAgentClient`.

## Purpose

Federation deployers and agent clients must honour a stable CRUD/lifecycle
contract regardless of which platform backs them. This project provides a
shared set of scenarios that every federation adapter must pass, so
regressions are caught in CI rather than production.

## Structure

| File | What it tests |
|------|---------------|
| `FederationDeployerConformanceTests.cs` | Deploy, teardown, mark-failed, validate, force-redeploy, tag propagation, timestamp invariants, cancellation |
| `ManagedAgentClientConformanceTests.cs` | Get, update, delete, list, null-for-unknown, post-delete absence, cancellation |
| `FakeConformanceFixtures.cs` | Reference `IFederationDeployer` + `IManagedAgentClient` sharing a single in-memory store — make the suite self-validating in CI |
| `FederationConformanceFactory.cs` | Shared test-data builders for `WorkflowManifest`, `ToolKit`, `DeployOptions` |

## Extending for a Provider

### Deployer

```csharp
[TestFixture]
public sealed class VertexAIDeployerConformanceTests : FederationDeployerConformanceTests
{
    protected override IFederationDeployer CreateDeployer() =>
        new VertexAIFederationDeployer(GetSandboxCredentials());
}
```

### Client

```csharp
[TestFixture]
public sealed class VertexAIAgentClientConformanceTests : ManagedAgentClientConformanceTests
{
    protected override IManagedAgentClient CreateClient() =>
        new VertexAIAgentClient(GetSandboxCredentials());

    protected override async Task<string> SeedDeploymentAsync(IManagedAgentClient client)
    {
        // deploy a real agent to the sandbox and return its ID
    }
}
```
