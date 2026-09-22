# Ananke.Orchestration.Conformance

The provider **conformance contract** for `IStreamingAgentModel`, `IToolSchemaTranslator` and
`IJsonSchemaTranslator` — a shared set of scenarios every adapter must satisfy.

## Why this is a library, not a test project

Adapter drift is invisible until a user hits it at runtime, so the scenarios have to be runnable
against the *real* adapters. As a test project they could not be: a test project is not
referenceable as a contract, so the fixtures' only subclasses were fakes and the suite asserted that
a fake behaved the way the fake declared. As a library, every adapter's test
project can reference it.

## Structure

The contract is **framework-neutral**: a scenario is a named delegate over the
subject under test, and every assertion is Shouldly, which throws its own exception type and needs no
runner. This assembly references no test framework and nothing but `Ananke.Abstractions`.

| File | What it holds |
|------|---------------|
| `ConformanceScenario.cs` | One named rule, as a delegate. `RunAsync` turns any assertion into an outcome instead of letting it escape, so a runner gets one uniform result |
| `ConformanceOutcome.cs` | `Passed` / `Skipped(reason)` / `Failed(reason, exception)` |
| `StreamingAgentModelConformance.cs` | 16 scenarios — text, tools, structured output, streaming, multimodal, token usage, content-part shape, system-prompt + JSON schema fusion |
| `ToolSchemaTranslatorConformance.cs` | 6 scenarios — basic translation, idempotency, local-tool rejection |
| `JsonSchemaTranslatorConformance.cs` | 6 scenarios — basic translation, idempotency, type preservation |
| `FakeConformanceModel.cs` | Reference `IStreamingAgentModel` — deterministic, in-process, no credentials |
| `PassThroughToolSchemaTranslator.cs` / `PassThroughJsonSchemaTranslator.cs` | Reference translators |

The reference subjects are what make the suite **self-validating**: `Ananke.Orchestration.Conformance.Tests`
runs all three through every scenario, so a failure in an adapter's run points at the adapter rather
than at the fixture.

### Why `Skipped` is a returned value, not a thrown one

Several rules are conditional — *"**when** usage is reported, input and output must be positive"* —
and a provider that legitimately does not report usage has nothing to prove. The NUnit fixtures this
grew from expressed that as a control-flow throw only one runner understands. Returning the
distinction is what lets the contract be consumed from any runner.

**Report a skip as your runner's skip, not as a pass.** A subject that skips every conditional
scenario would otherwise read as fully green — which is precisely the failure this package exists to
prevent one level up. The NUnit fixtures map `Skipped` to `Assert.Ignore` for that reason.

## Extending for a provider

On **NUnit**, inherit the fixtures from `Ananke.Orchestration.Conformance.NUnit` and you are done:

```csharp
[TestFixture]
public sealed class OpenAIConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() =>
        new OpenAIChatAgentModel(StubbedClient(), "gpt-4o-mini");
}
```

On **any other runner**, drive the scenario list yourself — the glue is about ten lines:

```csharp
foreach (var scenario in StreamingAgentModelConformance.Scenarios)
{
    var outcome = await scenario.RunAsync(CreateModel(), ct);
    switch (outcome.Status)
    {
        case ConformanceStatus.Skipped: /* your runner's skip, with outcome.Reason */ break;
        case ConformanceStatus.Failed:  throw outcome.Exception ?? new Exception(outcome.Reason);
    }
}
```

Wire the adapter to a **stub transport** rather than a live endpoint. Every SDK exposes a seam, and
**each one is a different mechanism with a different name** — two of these three are not the obvious
candidate, and each was established by a failing test rather than read from documentation:

| SDK | Seam |
|---|---|
| OpenAI (`System.ClientModel`) | `OpenAIClientOptions.Transport` |
| Anthropic | `ClientOptions.HttpClient` — **not** `Handlers`, which rejects a pre-attached handler |
| Google | `ClientOptions.HttpClientFactory` — **not** `HttpOptions`, which carries no transport |

**This package ships no stub transports.** A stub encodes one vendor's wire format, which changes on
that vendor's schedule and belongs with whoever maintains the adapter — not in a contract that has to
stay stable. Working examples for all three live in the Ananke repository under
`src/tests/Ananke.TestHelpers/ProviderStubs/`.

## What ships

Everything needed to run the contract and to tell a broken fixture from a broken adapter:

- **The scenarios**, as data — enumerate `…Conformance.Scenarios` on any runner.
- **The reference subjects** — `FakeConformanceModel`, `PassThroughToolSchemaTranslator`,
  `PassThroughJsonSchemaTranslator`. Run yours *and* one of these; if both fail, suspect the fixture,
  and if only yours fails, suspect the adapter. Without them that distinction is unavailable to
  anyone outside this repository.
- **`Ananke.Orchestration.Conformance.NUnit`**, separately, for inherit-and-done on NUnit.

## Versioning

The package version tracks Ananke's.

- **A minor version may add scenarios.** Adding one can break a conforming implementation's build or
  run — that is the point of a conformance suite, and the reason it is worth pinning a version and
  upgrading deliberately rather than floating.
- **A major version may change anything**, including removing or reshaping scenarios.
- **A patch version never changes the scenario set** — only fixes to a scenario that was testing the
  wrong thing.

> ⚠ **Pre-1.0: the contract underneath is not frozen.** `Ananke.Abstractions` is still moving toward
> its 1.0 freeze, and while it moves this suite moves with it — a `0.x` minor may change scenarios
> for reasons that have nothing to do with adding coverage. Ananke 1.0 is when the promise above
> becomes worth relying on.

## What conformance does and does not prove

> Conformance proves an adapter is **self-consistent with the contract**. It does not prove the
> adapter is **right about the service**.

Both halves matter. On 2026-08-20 an offline test asserted `user-agent` was inside SigV4's
`SignedHeaders` and passed while the adapter signed a value AWS never received; and a 6x token
under-count could not have surfaced from a stub, because the fixture author would have written the
same wrong number the code already believed. Offline conformance is necessary and cheap; it is not
sufficient.
