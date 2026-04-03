# ADR-002: Multimodal Conversations with State Machine Interrupts

| Field         | Value                                              |
|---------------|----------------------------------------------------|
| **Status**    | Proposed                                           |
| **Date**      | 2026-03-07                                         |
| **Authors**   | —                                                  |
| **Deciders**  | —                                                  |
| **Tags**      | state-machine, multimodal, audio, streaming, interrupts |

---

## Context

Ananke provides two complementary engines for building AI agent workflows:

1. **`Ananke.StateMachine`** — A distributed finite state machine with typed
   transitions, guard conditions, middleware, lifecycle hooks (`OnEnter`/`OnExit`),
   and an `OperationalStatus` circuit breaker (`Faulted`/`Operative`).

2. **`Ananke.Orchestration`** — A workflow engine with jobs, routing, fork/join,
   checkpointing, `InterruptMode` (Before/After), `ResumeAsync`, streaming
   (`IAsyncEnumerable<WorkflowEvent<T>>`), and a pre-built `StreamingChatWorkflow`
   for LLM conversations.

The current architecture is **text-only** at the abstraction layer. While provider
SDKs (Google GenAI, OpenAI, Anthropic) support multimodal inputs/outputs, Ananke's
`AgentMessage`, `AgentStreamChunk`, `AgentResponse`, and `AgentRequest` only carry
`string? Content` / `string? TextDelta`.

Additionally, neither engine supports **mid-stream interruption** — the ability for
a user (or another agent) to interrupt an LLM that is actively generating a response.
The state machine has no interrupt/resume stack, and the orchestration layer's
interrupt mechanism pauses execution *between* jobs, not *during* a streaming
generation.

These gaps block a key scenario: **natural conversations** (interview, customer
support, multi-agent debate) where participants can interrupt each other, and where
the conversation may include audio, images, or mixed modalities — especially with
models like Gemini 2.0 that handle audio natively.

---

## Decision

We will extend Ananke in five incremental layers, each independently shippable:

### Layer 1 — Multimodal Content Model (Foundation)

Extend the core message types to carry multimodal content parts alongside text.

**Changes:**

| Type | Change |
|---|---|
| New: `ContentPart` | Abstract base with `TextPart`, `AudioPart`, `ImagePart` subtypes |
| `AgentMessage` | Add `IReadOnlyList<ContentPart>? Parts`; `Content` becomes a computed shortcut over text parts for backward compatibility |
| `AgentStreamChunk` | Add `byte[]? AudioDelta`, `string? AudioMimeType`, `string? TranscriptDelta` |
| `AgentResponse` | Add `IReadOnlyList<ContentPart>? Parts`; `Text` remains as a shortcut |
| `ModelCapability` | Add `AudioInput` (1 << 7), `AudioOutput` (1 << 9), `RealtimeStreaming` (1 << 10) |
| `TaskRequirements.InferFrom` | Detect `AudioPart`/`ImagePart` in messages → require corresponding capability |

**Backward compatibility:** All existing code that reads `AgentMessage.Content` or
`AgentResponse.Text` continues to work unchanged. The `Parts` property is additive.

### Layer 2 — Provider Multimodal Wiring

Update each provider to map `ContentPart` to native SDK multimodal types.

**Changes per provider:**

| Provider | Mapping |
|---|---|
| `GeminiAgentModel` | `AudioPart` → `Part { InlineData = new Blob { MimeType, Data } }`; `ImagePart` → same |
| `OpenAIChatAgentModel` | `AudioPart` → `ChatMessageContentPart.CreateInputAudioPart()`; `ImagePart` → `CreateImagePart()` |
| `AnthropicAgentModel` | `AudioPart` → audio `ContentBlockParam`; `ImagePart` → image `ContentBlockParam` |

**Output wiring (Gemini audio generation):**
- `GeminiAgentModel.BuildConfig` → set `ResponseModalities = ["AUDIO", "TEXT"]` and
  `SpeechConfig` when the request contains audio output hints (via metadata).
- Map `Part.InlineData` in response/stream to `AgentStreamChunk.AudioDelta`.

### Layer 3 — State Machine Interrupt Stack

Extend `AbstractStateMachine` with interrupt/resume semantics so the conversation
protocol (who speaks, who can interrupt) is formally modeled.

**Changes:**

| Component | Change |
|---|---|
| `TransitionConfig<S, T>` | Add `bool IsInterrupt`, `bool IsResume` |
| `ITransitionBuilder<S, T>` | Add `ITransitionConfigBuilder<S, T> ToInterrupt(S interruptState)` |
| `ITransitionBuilder<S, T>` | Add `ITransitionConfigBuilder<S, T> ToResume()` |
| `PersistedContext<S>` | Add `List<S> InterruptStack` |
| `AbstractStateMachine.TryExecuteTransitionAsync` | On `IsInterrupt`: push current state, transition to interrupt state. On `IsResume`: pop stack, restore state. While interrupted: only allow transitions valid from the interrupt state. |
| `TransitionResult<S>` | Add `bool WasInterrupt`, `bool WasResume`, `S? ResumedFromState` |
| `StateMachineOptions` | Add `int MaxInterruptDepth = 5` |
| `IActionStateMachine` | Add `bool IsInterrupted { get; }`, `S? InterruptedState { get; }` |
| `OperationalStatus` | Consider adding `Interrupted` alongside `Operative`/`Faulted` |

**Builder usage:**

```csharp
protected override Action<ITransitionBuilder<S, T>> Transitions => b => b
    .From(ConvState.Answering)
        .On(ConvTransition.Interrupt).ToInterrupt(ConvState.Clarifying)
    .From(ConvState.Clarifying)
        .On(ConvTransition.Resume).ToResume()
    .From(ConvState.Clarifying)
        .On(ConvTransition.Clarify).To(ConvState.Clarifying);
```

### Layer 4 — Interrupt-Aware Streaming Chat

Extend `StreamingChatWorkflow` so external signals can interrupt a streaming
generation mid-token, capture partial output, and re-enter the agent loop.

**Changes:**

| Component | Change |
|---|---|
| `StreamingChatState` | Add `bool WasInterrupted`, `string? PartialText`, `AgentMessage? InterruptMessage` |
| `StreamingChatWorkflow.Builder` | Add `BuildInterruptible()` returning a `ChatSessionHandle` |
| New: `ChatSessionHandle` | `InterruptAsync(AgentMessage)`, `IAsyncEnumerable<SessionEvent> StreamAsync()` |
| "agent" job body | Monitor an interrupt `Channel<AgentMessage>` concurrently with `GenerateStreamAsync`; on signal: break, capture partial, inject into history |
| Router after "agent" | If `WasInterrupted` → route back to "agent" (not "tools" or end) |

### Layer 5 — Audio Conversation Demo

A minimal ASP.NET Core web application demonstrating:
- Browser captures audio via MediaRecorder API
- Audio sent to server (WebSocket or chunked POST)
- Server sends audio to Gemini 2.0 via `GeminiAgentModel` with `AudioPart`
- Agent response streamed back (text + optional audio) via SSE
- Browser plays audio response and/or renders text
- Interrupt button or voice-activity detection stops current generation

---

## Architecture

### Layer Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 5: AudioConversationDemo                                 │
│  ASP.NET Core + JS (MediaRecorder, SSE, WebSocket)              │
├─────────────────────────────────────────────────────────────────┤
│  Layer 4: ChatSessionHandle (interrupt-aware streaming)         │
│  Channel<AgentMessage> ↔ StreamingChatWorkflow                  │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3: StateMachine Interrupt Stack                          │
│  .ToInterrupt(S) / .ToResume() / PersistedContext.InterruptStack│
├─────────────────────────────────────────────────────────────────┤
│  Layer 2: Provider Multimodal Wiring                            │
│  GeminiAgentModel ↔ Part.InlineData, SpeechConfig              │
├─────────────────────────────────────────────────────────────────┤
│  Layer 1: Multimodal Content Model                              │
│  ContentPart, AudioPart, ImagePart, AgentMessage.Parts          │
├─────────────────────────────────────────────────────────────────┤
│  Layer 0: Existing infrastructure (unchanged)                   │
│  IStreamingAgentModel, Workflow, AbstractStateMachine,          │
│  IConversationMemory, WorkflowRunner, OperationalStatus         │
└─────────────────────────────────────────────────────────────────┘
```

### Conversation Flow (Audio Interview with Interrupt)

```
Browser                    Server                       Gemini 2.0
───────                    ──────                       ──────────
 [Record audio] ─────────► POST /api/audio
                           AudioPart { wav, bytes }
                           ──────────────────────────► GenerateStreamAsync
                                                        (InlineData audio)
                           ◄─────────────────────────  stream text deltas
 ◄── SSE: delta ──────────
 ◄── SSE: delta ──────────

 [User clicks interrupt
  or VAD detects speech]
 ── POST /api/interrupt ──►
                           ChatSessionHandle
                             .InterruptAsync(msg)
                           ─ cancel generation ──────►  (stream cancelled)
                           capture partialText
                           inject interrupt message
                           re-enter "agent" job
                           ──────────────────────────► GenerateStreamAsync
                                                        (history + partial + clarification)
                           ◄─────────────────────────  stream new response
 ◄── SSE: delta ──────────
 ◄── SSE: done ───────────
```

---

## Alternatives Considered

### A. Middleware-Only Interrupts (No State Machine Changes)

Use the existing `ITransitionMiddleware` to gate transitions and queue deferred
work when interrupted.

**Rejected because:** The interrupt protocol is implicit (hidden in middleware
logic), not declarative. Multi-party conversations and nested interrupts become
hard to reason about. Distributed persistence of a deferred queue adds complexity
without formal state management.

### B. Parallel Orthogonal Regions

Run a separate state machine instance for the interrupt flow while the primary is
suspended.

**Rejected because:** Coordination between two independent machines is heavyweight.
Shared context (partial output, conversation history) requires external glue.
Overkill for the conversation interrupt scenario.

### C. Bidirectional Real-Time Session (Gemini Live API / OpenAI Realtime API)

Introduce `IRealtimeAgentSession` with persistent WebSocket-based bidirectional
audio streaming, where interrupt is handled natively by the provider.

**Deferred (not rejected):** This is the eventual goal for true real-time voice
conversations. However, it requires a fundamentally different abstraction
(persistent session vs request/response) and provider-specific WebSocket protocols.
We will build toward this in a future ADR after Layers 1–5 are validated.
Layers 1–4 provide the foundation: multimodal content model, interrupt semantics,
and partial-output capture are all prerequisites for a real-time session layer.

### D. Text-Only Interrupts First (Skip Multimodal)

Implement interrupt/resume on the state machine and `StreamingChatWorkflow`
without any multimodal changes.

**Partially accepted:** Layers 3–4 can be implemented independently of Layers 1–2.
However, the audio demo (Layer 5) requires Layers 1–2, and the combined delivery
is more compelling. The implementation plan sequences them for parallel work.

---

## Consequences

### Positive

- **Natural conversations:** Agents and users can interrupt each other mid-stream,
  enabling interview, debate, and customer support scenarios.
- **Multimodal foundation:** `ContentPart` model unlocks audio, image, and video
  inputs/outputs across all providers without breaking existing text-only code.
- **Formal protocol:** State machine interrupts make conversation turn-taking
  visible, testable, and auditable via OpenTelemetry tracing.
- **Incremental delivery:** Each layer is independently shippable and testable.
  Layer 1 alone improves the framework significantly.
- **Provider readiness:** Google GenAI SDK (1.3.0), OpenAI SDK (2.9.1), and
  Anthropic SDK (12.8.0) already support multimodal — only the Ananke abstraction
  layer needs to catch up.

### Negative / Risks

- **Breaking change surface:** While `AgentMessage.Content` stays as a computed
  property, any code that constructs `AgentMessage` with `Content = ...` continues
  to work — but new code should prefer `Parts`. Documentation and migration notes
  needed.
- **Audio payload size:** Audio `byte[]` in `AgentMessage.Parts` can be large.
  `IConversationMemory` implementations may need size limits or external blob
  storage references. The `ImagePart`/`AudioPart` should support both inline data
  and URI references.
- **Provider asymmetry:** Not all providers support all modalities equally.
  Gemini 2.0 has strong native audio; OpenAI has Realtime API; Anthropic has
  limited audio. The capability router handles this, but consumers need to
  understand provider limitations.
- **Interrupt complexity:** Stack-based interrupts in a distributed state machine
  add persistence complexity. The interrupt stack must be serializable and
  coordinated via the distributed lock.

---

## Implementation Plan

See [implementation-plan.md](./001-implementation-plan.md) for the detailed
phased plan with file-level changes and the audio demo specification.
