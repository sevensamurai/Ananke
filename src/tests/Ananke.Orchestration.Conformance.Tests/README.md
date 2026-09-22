# Ananke.Orchestration.Conformance.Tests

**Self-validation for the conformance contract.** The scenarios live in
[`Ananke.Orchestration.Conformance`](../../Ananke.Orchestration.Conformance/README.md) and the NUnit
fixtures over them in
[`Ananke.Orchestration.Conformance.NUnit`](../../Ananke.Orchestration.Conformance.NUnit/README.md);
this project is one consumer of both.

| File | What it proves |
|------|----------------|
| `SelfValidationTests.cs` | The reference subjects — `FakeConformanceModel` and the two pass-through translators — satisfy all 28 scenarios, so a failure elsewhere points at the adapter under test rather than at the fixture |
| `ConformanceCoreTests.cs` | The two outcomes the reference subjects never produce. A model reporting no usage, parts or tool calls must `Skip` all four conditional scenarios; a contract-violating one must `Fail` carrying the original assertion |
| `ConformanceOutcomeAssertTests.cs` | The NUnit shim's mapping of all three outcomes |

Adapter conformance subclasses live in each adapter's own test project, not here.
