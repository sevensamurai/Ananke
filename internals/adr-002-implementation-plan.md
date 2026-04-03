# ADR-002 Implementation Plan

Phased delivery plan for multimodal conversations with state machine interrupts.
Each phase is independently shippable and testable.

---

## Phase 1 — Multimodal Content Model

**Goal:** Extend `AgentMessage`, `AgentStreamChunk`, `AgentResponse` with
multimodal content parts. Zero breaking changes — existing text-only code works
unchanged.

**Branch:** `feature/multimodal-content-model`

### 1.1 New Types

| File (new) | Contents |
|---|---|
| `Ananke.Orchestration/Agents/ContentPart.cs` | `abstract record ContentPart` with subtypes: `TextPart { Text }`, `AudioPart { Data (byte[]), MimeType, Duration?, Transcript? }`, `ImagePart { Data (byte[])?, Uri?, MimeType, AltText? }` |

### 1.2 Extend Existing Types

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/AgentRequest.cs` | `AgentMessage`: add `IReadOnlyList<ContentPart>? Parts { get; init; }`. Add computed `Content` getter that joins `TextPart.Text` from `Parts`. Keep `Content` settable for backward compat. Add factory methods: `AgentMessage.User(IReadOnlyList<ContentPart> parts)`, `AgentMessage.UserAudio(byte[] data, string mimeType)`. |
| `Ananke.Orchestration/Agents/AgentStreamChunk.cs` | Add `byte[]? AudioDelta`, `string? AudioMimeType`, `string? TranscriptDelta`. |
| `Ananke.Orchestration/Agents/AgentResponse.cs` | Add `IReadOnlyList<ContentPart>? Parts`. `Text` becomes computed from `Parts` when `Parts` is set, else uses backing field. |
| `Ananke.Orchestration/Agents/ModelCapability.cs` | Add flags: `AudioInput = 1 << 7`, `ImageGeneration = 1 << 8`, `AudioOutput = 1 << 9`, `RealtimeStreaming = 1 << 10`, `VideoInput = 1 << 11`. |
| `Ananke.Orchestration/Agents/TaskRequirements.cs` | In `InferFrom`: detect `AudioPart` → require `AudioInput`; detect `ImagePart` → require `Vision`. |

### 1.3 Tests

| File (new) | Coverage |
|---|---|
| `tests/Ananke.Orchestration.Tests/ContentPartTests.cs` | Construct `AgentMessage` with `Parts`; verify `Content` computed property. Construct with `Content` only; verify backward compat. Round-trip `AudioPart`, `ImagePart`. |
| `tests/Ananke.Orchestration.Tests/TaskRequirementsMultimodalTests.cs` | `InferFrom` detects audio/image parts and sets correct capability flags. |

### 1.4 Acceptance Criteria

- [x] `AgentMessage.User("hello")` still works identically
- [x] `AgentMessage.User([new TextPart("hello"), new AudioPart(bytes, "audio/wav")])` creates a multimodal message
- [x] `agentMessage.Content` returns concatenated text from `Parts`
- [x] `TaskRequirements.InferFrom` requires `AudioInput` when `AudioPart` present
- [x] All existing tests pass without modification

---

## Phase 2 — Provider Multimodal Wiring

**Goal:** Map `ContentPart` to native SDK types in each provider. Start with
Gemini (primary target for audio demo).

**Branch:** `feature/provider-multimodal`  
**Depends on:** Phase 1

### 2.1 Gemini Provider

| File | Change |
|---|---|
| `Ananke.Orchestration.Google/GeminiAgentModel.cs` | **`MapContents`**: When `msg.Parts` is non-null, iterate parts: `TextPart` → `Part { Text }`, `AudioPart` → `Part { InlineData = new Blob { MimeType, Data } }`, `ImagePart` → same with image mime. When `msg.Parts` is null, fall back to existing `Part { Text = msg.Content }`. **`BuildConfig`**: When `AgentRequest.Metadata` contains `"response_modalities" = "AUDIO,TEXT"`, set `config.ResponseModalities` and `config.SpeechConfig`. **`MapResponse` / stream**: When response `Part` has `InlineData` with audio mime, emit `AgentStreamChunk { AudioDelta, AudioMimeType }` and map to `AudioPart` in `AgentResponse.Parts`. |

### 2.2 OpenAI Provider

| File | Change |
|---|---|
| `Ananke.Orchestration.OpenAI/OpenAIChatAgentModel.cs` | **`MapMessages`**: When `msg.Parts` is non-null, build `List<ChatMessageContentPart>` with `CreateTextPart()`, `CreateImagePart()`, `CreateInputAudioPart()`. Fall back to text-only when `Parts` is null. |

### 2.3 Anthropic Provider

| File | Change |
|---|---|
| `Ananke.Orchestration.Anthropic/AnthropicAgentModel.cs` | **`MapMessages`**: When `msg.Parts` is non-null, build content blocks with text, image (base64), audio blocks. Fall back to text-only when `Parts` is null. |

### 2.4 Tests

| File (new) | Coverage |
|---|---|
| `tests/Ananke.Orchestration.Tests/GeminiMultimodalTests.cs` | Verify `MapContents` produces `InlineData` for audio/image parts. Verify `BuildConfig` sets `ResponseModalities` from metadata. |

### 2.5 Acceptance Criteria

- [x] `GeminiAgentModel` sends audio bytes as `InlineData` to the Gemini API
- [x] `GeminiAgentModel` streams `AudioDelta` chunks back when model generates audio
- [x] Text-only requests work identically to before (no regression)
- [x] OpenAI and Anthropic providers map image/audio parts to native types

---

## Phase 3 — State Machine Interrupt Stack

**Goal:** Extend `AbstractStateMachine` with `.ToInterrupt(S)` / `.ToResume()`
transitions that manage a state stack, enabling formal conversation protocols.

**Branch:** `feature/statemachine-interrupts`  
**Depends on:** Nothing (parallel with Phases 1–2)

### 3.1 Builder Extensions

| File | Change |
|---|---|
| `Ananke.StateMachine/Builder/ITransitionBuilder.cs` | **`IToStateBuilder<S,T>`**: Add `ITransitionConfigBuilder<S, T> ToInterrupt(S interruptState)`. **New interface**: `IResumeBuilder<S, T>` with `ITransitionConfigBuilder<S, T> ToResume()`. **`IToStateBuilder<S,T>`**: extend to also implement `IResumeBuilder`. **`TransitionConfig<S, T>`**: Add `bool IsInterrupt`, `bool IsResume`. |
| `Ananke.StateMachine/Builder/TransitionBuilder.cs` | Implement `ToInterrupt(S)`: sets `_currentTargetState = interruptState`, marks config `IsInterrupt = true`. Implement `ToResume()`: sets `_currentTargetState = default` (placeholder — resolved at runtime from stack), marks config `IsResume = true`. In `FinalizeCurrentTransition`: store `IsInterrupt`/`IsResume` on `TransitionConfig`. |

### 3.2 Core Engine

| File | Change |
|---|---|
| `Ananke.StateMachine/AbstractStateMachine.cs` | **`PersistedContext<S>`**: Add `List<S> InterruptStack { get; set; } = []`. **`TryExecuteTransitionAsync`**: After resolving `TransitionConfig` — if `config.IsInterrupt`: push `CurrentState` onto `InterruptStack`, transition to `config.FinalState`. If `config.IsResume`: pop from `InterruptStack`, set `FinalState` to popped value. If `InterruptStack` has items and transition is not valid from interrupt state, reject. **Properties**: Add `bool IsInterrupted => InterruptStack.Count > 0` (from persisted context). |
| `Ananke.StateMachine/IStateMachine.cs` | Add `bool IsInterrupted { get; }` to `IActionStateMachine`. |
| `Ananke.StateMachine/TransitionResult.cs` | Add `bool WasInterrupt`, `bool WasResume`, `S? ResumedFromState`. |
| `Ananke.StateMachine/StateMachineOptions.cs` | Add `int MaxInterruptDepth { get; set; } = 5`. |

### 3.3 Tests

| File (new) | Coverage |
|---|---|
| `tests/Ananke.StateMachine.Tests/InterruptTransitionTests.cs` | Basic interrupt: `A → interrupt → B`, verify stack has `A`, current is `B`. Resume: pop back to `A`. Nested interrupt: `A → interrupt → B → interrupt → C → resume → B → resume → A`. Guard on interrupt transition. Max depth exceeded → rejected. `IsInterrupted` property correct at each stage. Distributed persistence: interrupt stack survives round-trip through `PersistedContext`. |

### 3.4 Acceptance Criteria

- [x] `.ToInterrupt(S)` pushes current state and transitions to interrupt state
- [x] `.ToResume()` pops stack and returns to previous state
- [x] Nested interrupts work up to `MaxInterruptDepth`
- [x] `IsInterrupted` is `true` while interrupt stack is non-empty
- [x] All existing state machine tests pass without modification

---

## Phase 4 — Interrupt-Aware Streaming Chat

**Goal:** Enable external interruption of an in-progress `StreamingChatWorkflow`
generation, capturing partial output and re-entering the agent loop.

**Branch:** `feature/streaming-interrupts`  
**Depends on:** Phase 1 (for `StreamingChatState` changes)

### 4.1 State Extension

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` | **`StreamingChatState`**: Add `bool WasInterrupted`, `string? PartialText`, `AgentMessage? InterruptMessage`. |

### 4.2 Session Handle

| File (new) | Contents |
|---|---|
| `Ananke.Orchestration/Agents/ChatSessionHandle.cs` | Public class wrapping `Channel<AgentMessage>` (interrupt signal), `CancellationTokenSource` (generation cancel), and `IAsyncEnumerable<ChatSessionEvent>` (output). Methods: `InterruptAsync(AgentMessage)` — writes to channel + cancels CTS. `IAsyncEnumerable<ChatSessionEvent> Events` — relays text deltas, tool events, interrupt acks, done. |
| `Ananke.Orchestration/Agents/ChatSessionEvent.cs` | `abstract record ChatSessionEvent` with subtypes: `TextDeltaEvent { Text }`, `AudioDeltaEvent { Data, MimeType }`, `ToolCallEvent { Name, Args }`, `ToolResultEvent { Name, Result }`, `InterruptedEvent { PartialText }`, `ResumedEvent`, `CompletedEvent { FullText }`, `ErrorEvent { Message }`. |

### 4.3 Workflow Integration

| File | Change |
|---|---|
| `Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` | **Builder**: Add `BuildInterruptible() → ChatSessionHandle`. Internally: create `Channel<AgentMessage>` + linked CTS. **"agent" job body**: Wrap `await foreach` with concurrent read on interrupt channel. On interrupt: break from loop, save `fullText.ToString()` as `PartialText`, add `AgentMessage.Assistant(partialText)` + interrupt message to history, set `WasInterrupted = true`. **Router**: Extend decide: if `WasInterrupted` → route back to `"agent"` (clear `WasInterrupted`). |

### 4.4 Tests

| File (new) | Coverage |
|---|---|
| `tests/Ananke.Orchestration.Tests/StreamingInterruptTests.cs` | Interrupt mid-stream: mock model streams 10 chunks, interrupt after chunk 5, verify partial text captured. Interrupt message injected into history. Agent re-invoked with full context. No interrupt: workflow completes normally (regression). |

### 4.5 Acceptance Criteria

- [x] `ChatSessionHandle.InterruptAsync(msg)` stops current generation
- [x] Partial text is captured and injected into conversation history
- [x] Agent is re-invoked with: history + partial answer + interrupt message
- [x] Non-interrupted workflows work identically (backward compatible)

---

## Phase 5 — Pet Adoption Demo

**Goal:** A working web demo where a user converses with an AI pet adoption
assistant (text or voice) backed by a knowledge base. Demonstrates Layers 1–4
end-to-end in a non-technical domain everyone can follow.

**Project:** `demos/PetAdoptionDemo`  
**Depends on:** Phases 1, 2, 4

### 5.0 Design Principles

#### Latency model (for demo purposes)

The demo adds **artificial delays inside tool execution** so observers can clearly
see the agent working and have time to interrupt. The SSE connection and first
event are instant — delays are only in the observable processing:

```
0ms     User sends message
~50ms   SSE: event:tool    → { name: "search_knowledge", status: "calling" }
        ┌─ UI: 🔍 Searching knowledge base...
        │  (tool body: await Task.Delay(2500ms) + real search)
~2600ms SSE: event:tool    → { name: "search_knowledge", result: "..." }
        └─ UI: ✅ Found 3 results
~2700ms SSE: event:delta   → "Based on "
~2900ms SSE: event:delta   → "our available "    ← user can interrupt
~3100ms SSE: event:delta   → "pets, I found "
...     (Gemini streams at natural pace; optional relay throttle if too fast)
```

**Two interrupt windows:** during tool execution wait, and during text streaming.

A persistent banner in the UI states: *"⏳ Tool calls are intentionally slowed
down for demo purposes to illustrate real-time interrupts."*

#### Audio mode

- **Input:** `MediaRecorder` → wav → base64 → `AudioPart` → Gemini native audio
- **Output:** Text only (Gemini text response streamed via SSE)
- **Record:** Click-to-start / click-to-stop (no VAD — deferred to future phase)
- **Interrupt:** Dedicated button or typing in the text box while agent is responding

### 5.1 Knowledge Content

Markdown files under `demos/PetAdoptionDemo/data/`, loaded into
`InMemoryKnowledgeStore` + `InMemoryKnowledgeCatalog` at startup.

| File (new) | Contents |
|---|---|
| `data/adoption-process.md` | Eligibility, application steps, home visit, fees ($50–$150), timeline (1–2 weeks), what to bring on adoption day, post-adoption support. |
| `data/pet-care-basics.md` | First 48 hours at home, feeding schedules, vet checkup, vaccinations, socialization, crate/litter training, common behavioral issues. |
| `data/available-pets.md` | ~10–12 pets in deliberately **unstructured** narrative prose (not tables). The model must parse/categorize on its own. Mix of dogs, cats, a rabbit, a parrot. Each entry: name, species, breed, age, personality, special needs, adoption fee. |

### 5.2 Project Setup

| File | Contents |
|---|---|
| `PetAdoptionDemo.csproj` | ASP.NET Core Web. References: `Ananke.Orchestration`, `Ananke.Orchestration.Google`, `Ananke.Documents`. Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`. |
| `Program.cs` | Minimal API: secrets.json, static files, OpenAPI, knowledge bootstrap, agent config, endpoint registration. |

### 5.3 Backend

| File (new) | Contents |
|---|---|
| `AgentConfig.cs` | Reads `Google:ApiKey` and `Google:Model` (default `gemini-2.0-flash`). Creates `GeminiAgentModel`. System prompt defines the pet adoption assistant persona and available tools. |
| `KnowledgeBootstrap.cs` | On startup: reads `data/*.md`, extracts via `MarkdownExtractor`, chunks via `SlidingWindowChunker`, upserts into `InMemoryKnowledgeStore`. Indexes each file as a `CatalogEntry` in `InMemoryKnowledgeCatalog`. |
| `AdoptionTools.cs` | Three tools in a `ToolKit`:<br>**`search_knowledge`** — Searches the knowledge base. Wraps real search with `Task.Delay(2500)` before returning results.<br>**`browse_pets`** — Browses the catalog filtered by category (dog/cat/other). `Task.Delay(2000)` before results.<br>**`start_adoption`** — Simulated: validates pet name exists, returns confirmation with next-steps text. `Task.Delay(2000)` before result. |
| `ChatEndpoint.cs` | `POST /api/chat` — Accepts `{ message?, audioBase64?, audioMimeType?, sessionId, history[] }`. Builds `AgentMessage` with `AudioPart` or text. Creates or reuses `ChatSessionHandle` via `SessionStore`. Relays `ChatSessionEvent` stream as SSE. |
| `InterruptEndpoint.cs` | `POST /api/interrupt` — Accepts `{ sessionId, message }`. Looks up handle from `SessionStore`, calls `InterruptAsync`. Returns 200. |
| `SessionStore.cs` | `ConcurrentDictionary<string, ChatSessionHandle>` — keyed by client-generated session ID. Handles clean up after completion. |
| `ConversationModels.cs` | `ChatRequest`, `InterruptRequest`, `HistoryMessage` records. |

### 5.4 Frontend

| File (new) | Contents |
|---|---|
| `wwwroot/index.html` | Single-page app. Top banner (demo latency note). Conversation transcript. Text input + send. Record button (🎤). Interrupt button (⏹). |
| `wwwroot/app.js` | **Audio:** `getUserMedia` → `MediaRecorder` (wav). **Chat:** POST to `/api/chat`, read SSE stream, render deltas + tool status badges. **Interrupt:** POST to `/api/interrupt` (button or text submit while agent is streaming). **History:** Maintained in JS array, sent with each request. **Session:** Random UUID generated on page load. |
| `wwwroot/styles.css` | Conversation bubbles (user right, agent left). Tool-call badges with spinner → checkmark. Record button pulse animation. Demo banner styling. |

### 5.5 Conversation Flow

```
User: "What dogs do you have?"
 → Agent calls search_knowledge("available dogs")
 → UI: 🔍 Searching knowledge base... (2.5s visible wait)
 → UI: ✅ Found 4 results
 → Agent streams: "We have several wonderful dogs looking for homes!
   **Buddy** is a 1-year-old golden retriever, super energetic..."
 → (user reads streaming text, can interrupt any time)

User: "Tell me more about Buddy, and what's the adoption process?"
 → Agent calls search_knowledge("Buddy golden retriever") (2.5s)
 → Agent calls search_knowledge("adoption process steps") (2.5s)
 → Agent streams combined answer about Buddy + process

User: [clicks 🎤, says "I'd like to adopt Buddy"]
 → Audio sent as AudioPart → Gemini processes voice natively
 → Agent calls start_adoption("Buddy") (2s)
 → Agent streams: "Great choice! I've started Buddy's adoption
   application. Here are your next steps..."

User: [interrupts mid-stream] "Wait, actually what about Luna?"
 → Partial text captured
 → Agent re-invoked: sees partial answer + "Wait, what about Luna?"
 → Agent streams new response about Luna
```

### 5.6 Ananke Features Demonstrated

| Feature | How |
|---|---|
| `StreamingChatWorkflow` | Core agent loop |
| `ChatSessionHandle` / `BuildInterruptible` | Mid-generation interrupts |
| `ChatSessionEvent` stream | SSE event types map 1:1 |
| `ToolKit` | search, browse, adopt tools |
| `InMemoryKnowledgeStore` | Vector search (RAG) |
| `InMemoryKnowledgeCatalog` | Catalog browse/filter |
| `SlidingWindowChunker` | Markdown-aware chunking at startup |
| `MarkdownExtractor` | Document extraction from `.md` |
| `ContentPart` / `AudioPart` | Multimodal voice input |
| `GeminiAgentModel` | Audio-capable LLM provider |

### 5.7 Acceptance Criteria

- [ ] Knowledge base loads from `data/*.md` on startup (no external dependencies)
- [ ] Agent can search knowledge and answer questions about pets, process, and care
- [ ] Tool calls show visible progress in UI with intentional delay
- [ ] User can interrupt agent mid-response (text or voice)
- [ ] User can send voice input via record button (Gemini processes natively)
- [ ] Text input works as primary interaction mode
- [ ] Conversation history maintained across turns
- [ ] Demo runs with `dotnet run` + browser (only needs `Google:ApiKey`)

---

## Sequencing & Parallelism

```
Week 1─2          Week 3─4          Week 5─6          Week 7
────────          ────────          ────────          ──────
Phase 1           Phase 2           Phase 4           Phase 5
(Content Model)──►(Providers)──────►(Stream Interrupt)►(Demo)
                                         ▲
Phase 3                                  │
(SM Interrupts)──────────────────────────┘
  (parallel)
```

- **Phases 1 & 3** can proceed in parallel (no dependencies between them)
- **Phase 2** depends on Phase 1 (needs `ContentPart` types)
- **Phase 4** depends on Phase 1 (needs `StreamingChatState` extensions)
- **Phase 5** depends on Phases 1 + 2 + 4

---

## Files Summary

### New Files

| File | Phase |
|---|---|
| `Ananke.Orchestration/Agents/ContentPart.cs` | 1 |
| `Ananke.Orchestration/Agents/ChatSessionHandle.cs` | 4 |
| `Ananke.Orchestration/Agents/ChatSessionEvent.cs` | 4 |
| `tests/Ananke.Orchestration.Tests/ContentPartTests.cs` | 1 |
| `tests/Ananke.Orchestration.Tests/TaskRequirementsMultimodalTests.cs` | 1 |
| `tests/Ananke.Orchestration.Tests/GeminiMultimodalTests.cs` | 2 |
| `tests/Ananke.StateMachine.Tests/InterruptTransitionTests.cs` | 3 |
| `tests/Ananke.Orchestration.Tests/StreamingInterruptTests.cs` | 4 |
| `demos/PetAdoptionDemo/data/adoption-process.md` | 5 |
| `demos/PetAdoptionDemo/data/pet-care-basics.md` | 5 |
| `demos/PetAdoptionDemo/data/available-pets.md` | 5 |
| `demos/PetAdoptionDemo/AgentConfig.cs` | 5 |
| `demos/PetAdoptionDemo/KnowledgeBootstrap.cs` | 5 |
| `demos/PetAdoptionDemo/AdoptionTools.cs` | 5 |
| `demos/PetAdoptionDemo/ChatEndpoint.cs` | 5 |
| `demos/PetAdoptionDemo/InterruptEndpoint.cs` | 5 |
| `demos/PetAdoptionDemo/SessionStore.cs` | 5 |
| `demos/PetAdoptionDemo/ConversationModels.cs` | 5 |
| `demos/PetAdoptionDemo/wwwroot/index.html` | 5 |
| `demos/PetAdoptionDemo/wwwroot/app.js` | 5 |
| `demos/PetAdoptionDemo/wwwroot/styles.css` | 5 |

### Modified Files

| File | Phase | Nature of Change |
|---|---|---|
| `Ananke.Orchestration/Agents/AgentRequest.cs` | 1 | Add `Parts` to `AgentMessage`, new factories |
| `Ananke.Orchestration/Agents/AgentStreamChunk.cs` | 1 | Add `AudioDelta`, `AudioMimeType`, `TranscriptDelta` |
| `Ananke.Orchestration/Agents/AgentResponse.cs` | 1 | Add `Parts` |
| `Ananke.Orchestration/Agents/ModelCapability.cs` | 1 | Add audio/video capability flags |
| `Ananke.Orchestration/Agents/TaskRequirements.cs` | 1 | Detect multimodal parts |
| `Ananke.Orchestration.Google/GeminiAgentModel.cs` | 2 | Map `ContentPart` ↔ `Part.InlineData` |
| `Ananke.Orchestration.OpenAI/OpenAIChatAgentModel.cs` | 2 | Map `ContentPart` ↔ native parts |
| `Ananke.Orchestration.Anthropic/AnthropicAgentModel.cs` | 2 | Map `ContentPart` ↔ content blocks |
| `Ananke.StateMachine/Builder/ITransitionBuilder.cs` | 3 | Add `ToInterrupt`, `ToResume`, config flags |
| `Ananke.StateMachine/Builder/TransitionBuilder.cs` | 3 | Implement interrupt/resume builder methods |
| `Ananke.StateMachine/AbstractStateMachine.cs` | 3 | Interrupt stack management in transition logic |
| `Ananke.StateMachine/IStateMachine.cs` | 3 | Add `IsInterrupted` property |
| `Ananke.StateMachine/TransitionResult.cs` | 3 | Add interrupt/resume metadata |
| `Ananke.StateMachine/StateMachineOptions.cs` | 3 | Add `MaxInterruptDepth` |
| `Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` | 4 | Interrupt-aware agent loop, `BuildInterruptible()` |
| `demos/PetAdoptionDemo/PetAdoptionDemo.csproj` | 5 | Add project references and packages |
| `demos/PetAdoptionDemo/Program.cs` | 5 | Replace template with demo app |

---

## Open Questions

1. **Audio memory persistence:** Should `IConversationMemory` store audio bytes
   inline or externally (blob store + URI reference)? Inline is simpler for the
   demo; URI reference is more practical for production.

2. **Voice Activity Detection (VAD):** Should the demo implement client-side VAD
   (browser detects user speaking → auto-interrupt) or use a manual interrupt
   button only? Manual button is simpler for Phase 5; VAD can be added later.

3. **Audio output from Gemini:** Gemini 2.0 Flash supports audio output via
   `ResponseModalities`. Should the demo stream audio back or text-only? Text-only
   is simpler and works with all Gemini models; audio output adds richness but
   limits model choice.

4. **State machine ↔ workflow bridge:** Phase 3 (SM interrupts) and Phase 4
   (streaming interrupts) are currently independent. Should we build a formal
   bridge where the state machine *drives* the workflow lifecycle? Deferred to a
   follow-up ADR after validating both independently.

---

## Test Coverage Notes

### Pre-Phase 5 coverage pass (added alongside Phases 1–4)

New test files for previously untested core areas:

| Test File | Covers | Tests |
|---|---|---|
| `SlidingWindowChunkerTests.cs` | Heading/paragraph splitting, overlap, metadata propagation, defaults | 14 |
| `InMemoryKnowledgeStoreTests.cs` | Upsert, search, ranking, TopK, score threshold, filter, delete, metadata | 11 |
| `ResilientAgentModelTests.cs` | Retry on 429, exhaustion, non-retryable pass-through, streaming first-chunk retry, `IsRateLimitException` detection, custom predicate | 11 |
| `CachingAgentModelTests.cs` | Cache miss→hit, streaming cache, tool-call skip, key isolation, validation | 9 |

### Deferred test areas

The following require integration-level test infrastructure (HTTP servers, ASP.NET
test host, or external service mocks) and are deferred to a dedicated test hardening pass:

| Project | Reason |
|---|---|
| **Ananke.A2A** | HTTP client/server A2A protocol — needs `WebApplicationFactory` or `TestServer` |
| **Ananke.MCP** | MCP server adapter — needs ASP.NET test host for endpoint routing |
| **Ananke** (Bridge) | SM↔Workflow glue — needs both subsystems wired together in integration |
| **Ananke.Orchestration — AgentRouter** | LLM-driven routing — needs controllable fake agent model with tool support |
| **Ananke.Orchestration — DocumentSummarizer** | Thin wrapper over agent model — low standalone value |
| **Ananke.Orchestration — Handoff** | Cross-workflow channel — needs multi-workflow integration setup |
