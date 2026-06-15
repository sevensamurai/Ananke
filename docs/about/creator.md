<!-- topic: about-creator, tags: about, creator, biography, background -->
# The Creator

I'm Patricio. I design systems the way an artist lets form emerge — guided by simplicity, precision, and the pursuit of functional beauty.

I've been building software professionally for over two decades — defense platforms, 3D simulation engines, enterprise integrations, distributed services, cloud migrations. Each of those domains taught me something different about what it means to design for the long run: simplicity that survives contact with reality, foundations that hold when requirements grow, and abstractions that earn their keep.

For the past few years I've been deep in the AI and LLM space — exploring what it means to build *with* models rather than just *on* them. The ecosystem moves fast, which is exciting, but it also means the underlying infrastructure tends to lag behind. I wanted a stable, flexible foundation for building the next generation of systems — ones that use agentic AI not as a feature bolted on top, but as a first-class part of how they work and grow. That idea is what became Ananke — a free, open-source .NET framework, built in the open and kept that way.

---

## Why I Built This

Almost everything in the AI agent space is Python-first. That's fine — Python has a huge ML ecosystem, and it made sense early on. The frameworks that emerged moved fast, which is how you explore a new space. But as teams started taking agents into production, a familiar set of questions kept surfacing: untyped state, provider abstractions that leaked, no clear answer on how to structure a multi-step workflow, no obvious path when requirements grew beyond the happy path.

I wanted to try a different starting point — not to iterate on what was already there, but to ask what the foundation would look like if you started with production-system thinking. Fix the contracts first. Make the vendor layer genuinely pluggable from the beginning. Build primitives that compose cleanly, so the framework stays out of your way as requirements grow. If the foundation holds, everything else follows.

That became a personal challenge — and the deeper I went, the clearer it became that the foundation wasn't just missing: it was the prerequisite for everything I actually wanted to build. Systems that don't just run, but learn from what they do and grow into the complexity they accumulate. That's where everything in Ananke gravitates toward.

---

## The Crucible: Engineering as a Learning Lab

The design patterns in Ananke weren't conceived in an academic vacuum. I look at both extremes of the tech landscape not as deterrents, but as a rich lab to stress-test the framework against real conditions.

**The Enterprise Legacy Lab — testing resilience.** Maintaining complex, aging corporate infrastructure is the ultimate crucible for software. These environments are full of flaky network boundaries, silent failures, and unmonitored states. Ananke's reliance on OpenTelemetry, distributed state checkpoints, and active circuit breaking exists because production is messy and software must survive it. If a framework can't hold up here, it isn't a framework — it's a demo.

**The High-Speed Startup Lab — testing ergonomics.** Bleeding-edge teams move at breakneck speed, rewriting prompt chains hourly and shifting goals daily. This forces an obsession with developer ergonomics. If an orchestration framework takes days to reconfigure, it fails. Ananke's fluent API and lightweight CLI tools mean you can pivot an entire logic topology in minutes without breaking underlying state contracts.

Both extremes push the design in different directions — and both directions matter. Resilience without ergonomics produces software nobody ships. Ergonomics without resilience produces software that fails in production. The goal is both, without compromise.

---

## The Longer Arc — Systems That Learn From Their Own Behavior

Vendor-agnostic infrastructure was the immediate problem. It wasn't the only reason to build this.

The frameworks that already exist — Microsoft Agent Framework, Semantic Kernel, the Python-first ecosystem — solve a different problem. They're good on-ramps to a hosted runtime. If you're shipping into Azure Foundry next quarter, MAF is the right tool to use now.

Ananke starts from a different set of priorities:

- **Vendor-agnostic by design.** The LLM provider, the vector store, the message bus, the deployment target — all behind interfaces, all swappable without touching your workflow code. The architecture stays yours as requirements evolve.
- **Idiomatic C# first.** Typed generic state (`Workflow<TState>`), DI-first construction, compile-time topology validation via Roslyn analyzers — designed for C# from the ground up, not shaped by the constraints of cross-language parity. The language works with you, not around you.

Beyond that, there's a longer-arc reason that I find genuinely exciting. The next generation of useful applications won't just *call* models. They'll accumulate experience and get better at their specific job over time. Most current frameworks treat the agent as a static artifact: deploy it, run it, redeploy a new version. Ananke treats it as a system with a lifecycle.

Concretely, that means three things I believe are worth addressing:

- **Episodic memory of what the system has actually done** — not just chat history, but trajectories with outcomes attached.
- **Promotion of recurring patterns into reusable skills** — observed, scored, and packaged so they can be shared across deployments.
- **Confidence that decays with contradiction and grows with reinforcement** — so the system's beliefs about its own behavior stay calibrated.

That is the empirical memory layer (`IEmpiricalMemory`, `IOfflineLearner`, `IConsolidationSummarizer`). Alongside it, `Ananke.Organics` treats *the system getting larger over time* as a normal operating mode: workflows can spawn sub-workflows, and can divide when complexity crosses a threshold so a single overloaded workflow becomes two specialised peers.

Resilience and growth aren't features you remember to enable. They're properties of the composition model. That's the part I'm most excited to keep building and what drives all design decisions for this.

---

## What Works Today vs. What's Ahead

Ananke is a release candidate. The foundation is settled; the next chapter is actively being written. Here's where things stand — specifically, because I'd rather you know what you're getting into than discover it later.

**Working and demonstrable today:**

- Vendor-agnostic core: OpenAI, Anthropic, Google Gemini, and any OpenAI-compatible endpoint behind one interface. Swap providers without touching workflow code.
- In-memory implementation for every infrastructure contract — full unit-test coverage of federated, learning, MCP-exposed workflows with zero containers.
- Empirical memory primitives — `commit`, `recall`, `reinforce`, `contradict`, offline learning sweeps with decay, curiosity exploration, and consolidation.
- Cell division as a working demo. The [OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo) runs end-to-end, no API keys: a generalist bookstore workflow accumulates tools, structural tension is detected, a division is proposed and approved, two specialised peers are spawned, the parent is killed, and the outcome is recorded into empirical memory.
- Cross-cloud federation with human approval gates — deploy the same manifest to Azure, Google, or Anthropic.

**Honest work-in-progress:**

- *Multi-generation lineage* — the division demo divides once. A specialist becoming overloaded and dividing again is the next step.
- *Closed-loop learning* — division outcomes are recorded into memory; making the next policy decision actually consume that memory to choose differently is wired structurally but not yet exercised end-to-end.
- *Real-load complexity signals* — current complexity monitors look at tool count and routing entropy. Driving division from production behaviour under real LLM load is ahead, not behind.

The framework is the substrate. The practical demonstration of it solving meaningful problems at scale is the next chapter — and building that in the open, with people who care about the same problems, is exactly the point.

---

## A Bit More Context

If you're considering Ananke for something serious — production workloads, team adoption, a long-term bet — it's fair to want to know a little about who built it. The design decisions here didn't come only from reading other frameworks and picking favourites. They came from two decades of building systems where untyped state, leaky abstractions, and non-composable primitives have real costs.

The table below is just for reference.

Try out the framework for yourself and let me know what you think — the code is the real answer.


| Area | Years | Highlights |
|---|---|---|
| C# / .NET Ecosystem | 20+ | from .NET Framework to .NET 10 |
| System Integration / APIs | 15+ | gRPC, GraphQL, REST, SignalR |
| Cloud (Azure / AWS) | 10+ | Monolith migration, serverless (Lambda / Functions), K8s |
| Tech Lead, Design / Architecture | 10+ | Multiple roles |
| C / C++ | 10+ | Defense systems, 3D/computer graphics/simulation |
| Databases (SQL & NoSQL) | 10+ | SQL Server, PostgreSQL, Redis, MongoDB, QDrant & Tamino XML DBs |
| DevOps & CI/CD | 10+ | GitHub Actions, TeamCity, Octopus Deploy |
| Frontend (React / Angular) | 8+ | Multiple roles |
| Java / Python | 7+ | Enterprise ORM, command systems |
| AI / Machine Learning | 3+ | Stanford ML, several AI/ML/LLM projects, Ananke framework |

---

 [LinkedIn](https://www.linkedin.com/in/patricio-nz/) ·  [GitHub](https://github.com/sevensamurai)
