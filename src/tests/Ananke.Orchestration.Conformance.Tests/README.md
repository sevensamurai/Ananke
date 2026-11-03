# Ananke.Orchestration.Conformance.Tests

**Self-validation for the conformance suite.** The scenarios and the NUnit fixtures over them live
in [`Ananke.Orchestration.Conformance`](../Ananke.Orchestration.Conformance/README.md); this project
runs them against the reference subjects and against the operation seam's reference.

| File | What it proves |
|------|----------------|
| `SelfValidationTests.cs` | The reference subjects — `FakeConformanceModel` and the two pass-through translators — satisfy all 28 scenarios, so a failure elsewhere points at the adapter under test rather than at the fixture |
| `ConformanceCoreTests.cs` | The two outcomes the reference subjects never produce. A model reporting no usage, parts or tool calls must `Skip` all four conditional scenarios; a contract-violating one must `Fail` carrying the original assertion |
| `ConformanceOutcomeAssertTests.cs` | The NUnit shim's mapping of all three outcomes |

Adapter conformance subclasses live in each adapter's own test project, not here.
