Hello there,

Excited to share a new release of **Ananke**!

The theme: what if your agents could learn by themselves?

Two main new features ship in this release. **Learning and memory** — agents now have a living record of what they've figured out: patterns, skills, and heuristics that get reinforced when they work, weakened when they don't, and autonomously re-evaluated by a background process (the *dreamer*) even when no conversation is happening. **Fluid communication** — agent messages are now multimodal (text, audio, images), workflows expose a typed event stream instead of callbacks, and a new `Ananke.AspNetCore` package makes wiring all of that to SSE for browser clients trivial.

At this point, Ananke is a .NET framework for building agents that learn — not just orchestrate. It lays the foundations for real intelligence: memory that evolves with experience, reasoning that runs in the background, and behaviour that genuinely improves over time, at any scale from a simple chatbot to a complex distributed system.

Two new demos ship with this release: **Pet Adoption Store** (multimodal, multi-provider, interrupt cycles, Qdrant knowledge retrieval, vanilla JS frontend) and **Connect 4** (two agents play, a third learns from every game via the dreamer loop).

Ananke stays .NET-native, deliberately. If you ever need to bridge to Python or another ecosystem, the recommended path is **A2A** (Agent-to-Agent protocol) — let each language do what it does best.

Give it a try and let me know what you think!

Github repo here - https://github.com/sevensamurai/Ananke
