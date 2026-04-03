Excited to share something I've been building:

**Ananke** — a vendor-agnostic workflow orchestration framework for .NET that gives your AI agents and automated pipelines a production-grade backbone.

Whether you're building a simple streaming chat agent or a distributed, state-machine-coordinated multi-service pipeline, it brings typed state, distributed coordination, checkpointing, resilience, long-term memory, and first-class human-in-the-loop support.

A few things worth highlighting:
* **Graph-as-code** with a fluent, type-safe builder — conditional routing via lambdas, LLM-driven routing (`DecideWithAgent`), fork/join parallelism, nested sub-workflows, and human-in-the-loop interrupts with checkpoint/resume. The graph is validated at build time, not at runtime, which is a meaningful difference when you're shipping to production.
* **Design-time DSL + code binding** — define the workflow graph topology in a YAML file, then bind your C# jobs to each named node at runtime. Graph structure lives outside application code, so it can be reviewed, tuned, and redeployed via GitHub pipelines without recompiling. Every validated workflow also auto-exports as a Mermaid diagram.
* **Long-term memory that agents build themselves** — give an agent the `KnowledgeTools` toolkit and it can index documents and search them within the same conversation. A user says "index this PDF", the agent processes it, and it's immediately searchable — no admin panel, no separate batch job. The same pipeline (`DocumentProcessor`: extract → chunk → embed → store) works programmatically too, with pluggable PDF/Markdown extractors, a knowledge catalog with LLM-enriched metadata, and configurable time-decay reranking.
* **Smart model routing** — route each task to the right model at request time based on declared capabilities. Expensive reasoning model for complex decisions, a cheaper fast model for straightforward steps — all within the same workflow, with no changes to pipeline code.

[slides1-5]

But as Han Solo once said: "don't get cocky" — this is only an initial release, needs fine-tuning, and there's still plenty of room for improvement.
Give it a try and let me know if you find any problems.

And if you're working on agentic systems or AI infrastructure in .NET, I'd be happy to connect, discuss ideas, and learn from you.

GitHub / NuGet → https://github.com/sevensamurai/Ananke

Last but not least, most of the ideas came from watching and exploring the examples in *"AI Engineer Agentic Track: The Complete Agent & MCP Course"* by Ed Donner — I would totally recommend it.

#WorkflowOrchestration #LLMEngineering #DotNet #AgenticAI