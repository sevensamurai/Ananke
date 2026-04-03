Hello there,

Excited to share a new release of Ananke!

The theme of this iteration explored the idea of what if your agents could learn by themselves?

In Greek mythology, Ananke fixed the laws of the cosmos, but she wasn’t alone: Aether, born of that ordered cosmos, was the luminous medium through which patterns across the heavens became visible.
That’s the mental model for this release: Ananke holds the foundation; Aether becomes the layer where patterns crystallize — the part of the system that observes, adapts, and turns experience into capability.

In that context, the two main new features introduced in this release are:

* Learning and memory — let your agentic setup learn from experience, remember what works, and forget what doesn’t.
* Fluid communication — make the back-and-forth between agents and the outside world more natural and responsive.



On the learning side, a few concrete things land in this release:

Slide 1: Aether — Empirical Memory & Learning

* Empirical Memory: A new third memory tier; committing skills, patterns, and heuristics to a living record based on usage.
* Surprise-based Reinforcement: Belief strength updates based on how surprising an outcome is, instead of flat confidence bumps.
* Outcome Signals: Beliefs carry outcome and intensity to prioritize important discoveries.
* Offline Learner: Independent background cognition process that decays stale beliefs, runs cyclic evaluations, and pushes insights directly or queues them for delivery contextually.
* Background Insights Push: Directly forward-typed insights from agents running out-of-band without forcing state transitions.



On the fluid communication side, the interaction surface became considerably more expressive.

Slide 2: Fluid Operations & Interoperability

* Rich Multimodal Content: Agent messages natively carry text, audio (with optional transcript/duration), and images (bytes/URI). Model routing detects capabilities automatically.
* Streaming IAsyncEnumerable API: Typed async stream of events for text, audio, tool calls, and interruptions instead of wiring callbacks. Maps cleanly to Server-Sent Events via Ananke.AspNetCore.
* State Machine Interrupt Stack: Push/pop mechanisms for temporary contexts (e.g., payment flows). Automatically repairs conversation history to avoid orphaned tool calls.



This release also introduces 2 new demos:

Slide 3: Real-World Scenarios

* Pet Adoption Store: End-to-end integration demo showcasing multi-phase state machines, multimodal input, payment interrupt cycles, and Qdrant memory.
* Connect 4: Agent learning Connect 4 strategies while playing (experimental).



A central consideration this iteration: Should Ananke limit its design to stay compatible with Python’s constraints (so it can be easily imported from Python), or should it fully embrace modern .NET?

Technically, it could be done. But doing so purely out of fear of missing adoption would be the wrong decision.
Python and C# are very different beasts. Trying to serve both from the same codebase means compromising both.
“Fear is the path to the dark side. Fear leads to anger; anger leads to hate; hate leads to suffering.” — Master Yoda.


Ananke stays .NET-native deliberately.

Ananke now supports A2A (Agent-to-Agent protocol), so it can be used as a backend for Python or any other language.


So, what is this and where is it going?
Ananke is a .NET framework for foundational intelligence where reasoning and self-improvement evolve.


The next Iteration will be about cleaning internals, removing inconsistencies, improving reliability, and simplifying core primitives.

Give it a try and let me know what you think!

Github repo here - https://github.com/sevensamurai/Ananke