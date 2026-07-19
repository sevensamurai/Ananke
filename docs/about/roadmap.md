<!-- topic: roadmap, tags: roadmap, versioning, releases, roadmap, open source, Apache-2.0, license, free, commercial -->
## Roadmap

### Where We Are — v0.8.6

**0.8.6 is the current release.**

The core framework is feature-complete. Workflows, agents, state machines, empirical
memory, distributed infrastructure, MCP/A2A interop, observability, design tooling,
Slack integration, work-review gates, async review parking, role scaffolding, and the
federation layer are all in place and covered by tests.

No new functionality is planned before 1.0. The remaining work is:

- **Bug fixes** — anything discovered during wider adoption
- **Minor improvements** — ergonomics, error messages, edge-case handling
- **Stability** — ensuring public APIs are clean and consistent before they are locked

If you are building on Ananke today, 0.8.x is production-ready for non-critical workloads.
The surface area that matters — `IStreamingAgentModel`, `IJob<T>`, `Workflow<T>`,
`AbstractStateMachine`, `IEmpiricalMemory` — will not change between 0.8.0 and 1.0.

---

### The Path to 1.0

| Milestone | Summary |
|---|---|
| **0.1.0** | Initial release of Ananke as a vendor-agnostic workflow orchestration framework for .NET. |
| **0.2.0** | Tool metadata and a reorganized documentation set improved tool-calling accuracy and discoverability. |
| **0.3.0** | Learning and memory capabilities landed alongside more fluid communication patterns. |
| **0.4.0** | Dedicated learning and skills packages formalized portable memory, exploration, and community skill resolution. |
| **0.5.0** | Developer experience and API usability improved with type-safe workflow graphs, analyzers, and multi-model cost tracking. |
| **0.6.0** | The public docs site, messaging platform adapters, and external knowledge ingestion became first-class parts of the stack. |
| **0.7.0** | `nnke` expanded into an agent-friendly CLI and MCP companion for design-time inspection, docs, and patterns. |
| **0.8.0** | Release candidate: pluggable organics, federation, `nnke-platform` CLI and the Smart Tool Router completed the feature surface for 1.0. |
| **0.8.4** | Platform review loops and studio scaffolding: richer Slack integrations (slash commands, interactivity, assistant pane, modals, approval blocks), work-review and budget gates in Organics, async review parking, `Ananke.Roles` package, OTel budget meter, `WorkItemReviewNotifier`, and `MiniAgencyDemo`. |
| **0.8.5** | Agent trajectory observability and resilience (`TrajectorySnapshot`, `IAdaptiveHarnessPolicy`, `IHallucinationObserver`), Organics division governance (`IDivisionApprovalGate` with concrete gates, `IDivisionOutcomeTracker`), and Federation platform-fit scoring (`PlatformRecommender`). |
| **0.8.6** | Declarative loops and conversational interview workflows (`loop()`/`ask()` DSL, `AgenticPattern.Interview`), multi-label knowledge graph nodes (`GraphNode.EffectiveLabels`), and a documentation drift guard (`scripts/check-docs.ps1`, `MAP.md`) that found and fixed real stale-doc drift across the repo. |
| **0.8.x** | Bug fixes and minor improvements only. |
| **1.0.0** | API lock — semantic versioning honoured from this point forward. |

After 1.0, breaking changes to established interfaces will require a major version bump
and a formal migration guide.

---

### License & Commercial Use

Ananke is and will remain **free and open source under the [Apache 2.0 License](https://opensource.org/licenses/Apache-2.0)**.

That means:

- **Use it commercially.** Build products, charge customers, deploy to production — the
  license places no restrictions on how you use the framework.
- **No usage fees, no tiers, no open-core.** The full framework — every package — is
  free of charge. There is no paid version with more features.
- **Fork it, extend it, redistribute it.** The Apache 2.0 license is intentionally permissive.
  You are not required to open-source your own application.

**What may be offered separately** — at some future point, consulting, custom integration
work, or tailored extensions *on top of* the framework could be offered as a paid
professional service. That would never affect the framework itself. The core remains Apache 2.0,
maintained in the open, free forever.

If you want the framework to stay healthy: contribute, report issues, and share what you
build with it. The goal is systems that don't just run — but learn from what they do and
grow into the complexity they accumulate. Building that in the open, with people who care
about the same problems, is the point.
