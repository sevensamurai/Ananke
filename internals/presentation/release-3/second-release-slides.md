# Ananke — Second Release Slides

---

## Slide 0: The Signal Behind Learning

**Every learning system faces one hard question: what is worth remembering, and how strongly?**

Frequency is a poor answer.
A pattern observed fifty times during normal operations tells you less than one observed three times during a crisis.
*Happened often* and *mattered enormously* are not the same thing — and conflating them produces a system that learns the routine and forgets the remarkable.

Computational neuroscience converged on a different decomposition.
Rather than a single reinforcement count, it separates the learning event into three independent signals:

| Signal | Question it answers | Role in learning |
|---|---|---|
| **Valence** | Was the outcome good or bad? | Shapes what surfaces in future recall — *priority*, not truth |
| **Intensity** | How much did it matter? | Amplifies or suppresses the priority signal |
| **Surprise** | How unexpected was it? | Determines how much the belief actually changes — the learning rate |

These are orthogonal. Each does a distinct job.
Together they let a system distinguish importance from repetition, and update beliefs in proportion to what it *didn't already know*.

The formal grounding is the **Rescorla-Wagner rule** (1972): learning rate should be proportional to prediction error.
If an outcome matched the expectation, there is little new information — the update is small.
If it didn't, the belief needs revision — and the magnitude of the revision scales with the gap.
This is also the mechanism underlying temporal-difference learning in modern reinforcement learning.

The other half comes from **Damasio's somatic marker hypothesis** (1994): the valence trace of past outcomes shapes future decisions faster than deliberate reasoning. Not by computing — by leaving a mark that surfaces automatically during recall.

In biological systems, these signals are implemented as affect.
In an engineered system, they are scalars — but they do the same structural job.

> *Aether borrows the architecture, not the phenomenology.*

---

## Slide 1: Aether — Empirical Memory & Learning

**Your agents now learn from experience.**

| | |
|---|---|
| 🧠 **Empirical Memory** | A third memory tier — alongside semantic and episodic — that persists patterns, skills, and heuristics discovered during real interactions. Backed by Qdrant or in-memory. |
| ⚡ **Surprise-based Reinforcement** | Belief strength updates proportionally to how unexpected an outcome was. Surprising confirmations reinforce harder; expected failures barely register. |
| 🎯 **Outcome Signals** | Every belief carries an outcome and an intensity score — so the learner prioritises what actually matters, not just what happened most often. |
| 🌙 **Offline Learner** | A background cognition process that runs independently: decays stale beliefs, explores low-confidence entries with curiosity walks, and consolidates mature knowledge into the semantic store. Analogous to sleep consolidation. |
| 📡 **Background Insights Push** | A background agent discovers something useful and delivers it to the active conversation. |

> *Pattern → Skill → Heuristic. Knowledge compounds.*

---

### 📖 Background — Why signals, and why do they resemble feelings?

In biological cognition, **emotions are not decoration — they are a learning infrastructure**.
They solve a specific problem: how do you decide what is worth remembering, and how strongly?

Aether borrows the answer from computational neuroscience, not to simulate feelings,
but because the engineering problem is the same.

Three signals, three jobs:

| Signal | Neuroscience parallel | What it does in Aether |
|---|---|---|
| **Valence** `[-1, +1]` | Positive/negative affect | Tags whether an outcome was good or bad — shapes *priority*, not truth |
| **Excitement** `[0, 1]` | Arousal / intensity | Tags how much the outcome matters — amplifies recall ranking for high-stakes discoveries |
| **Surprise** `[0, 1]` | Prediction error (dopamine signal) | Drives *reinforcement strength* — surprising confirmations update beliefs harder than expected ones |

**The key design constraint** — and the part that prevents pathology:

> Valence and excitement influence **what surfaces**. Surprise influences **what persists**.

This separation is deliberate. Without it, high-valence entries get recalled more, confirmed more, and grow stronger in a loop — the same mechanism behind human confirmation bias. By keeping priority signals separate from truth signals, the system stays epistemically honest.

The theoretical grounding is the **Rescorla-Wagner learning rule**: learning rate should be proportional to prediction error.
If an outcome matches the prediction, there is little new information — the update is small.
If the outcome is surprising, the entry needs significant revision.
This is also the basis of temporal-difference learning in reinforcement learning.

The **somatic marker hypothesis** (Damasio) adds the other half: past outcomes leave an affective trace that fast-tracks future decisions — not by reasoning, but by feeling. Valence and excitement are Aether's version of that trace: a signal that biases what gets surfaced without corrupting what is believed to be true.

---

## Slide 2:

**The interaction surface is now considerably more expressive.**

| | |
|---|---|
| 🖼️ **Rich Multimodal Messages** | Agent messages natively carry text, audio (with transcript and duration), and images (bytes or URI). Model routing detects provider capabilities at request time and routes accordingly — no changes to pipeline code. |
| 🌊 **Streaming `IAsyncEnumerable` API** | A typed async stream of `ChatSessionEvent` — text deltas, audio chunks, tool calls, interruptions, completions — replaces callback wiring. Maps directly to Server-Sent Events via `Ananke.AspNetCore`. |
| 🔀 **Interrupt Stack** | Push and pop temporary contexts onto the state machine (e.g. a payment flow mid-conversation). On pop, the stack automatically repairs conversation history to prevent orphaned tool calls. |

> *Express any modality. Stream any event. Interrupt anything — cleanly.*

---

## Slide 3: Real-World Scenarios

**Two new demos that put all of it together.**

### 🐾 Pet Adoption Store
An end-to-end integration demo. A state machine drives sessions through `Searching → Paperwork → Payment → Done`. Real-time streaming over SSE. Mid-generation interrupts. Human-in-the-loop payment pause. Voice and photo input. Vector-indexed knowledge base with Qdrant. Runs on OpenAI or Google Gemini — swap with one config line.

> *Every second-release feature in a single, runnable scenario.*

### ♟️ Connect 4 — Agent Learns While Playing
The agent starts knowing only the rules. No LLM. No API keys. No Docker. After each game the offline learner runs, decay sweeps stale beliefs, and new patterns are committed. By game 7 the agent has moved from random play to center control and offensive pressure — all from accumulated empirical memory.

> *Watch confidence scores grow. Inspect memory live. Press `m`.*
