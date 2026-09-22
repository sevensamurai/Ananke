<!-- topic: local-models, tags: local, ollama, vllm, llamacpp, lmstudio, self-hosted, open-weight, small-models, privacy, routing, licence -->
# 17 — Local & self-hosted models

Run a model on your own hardware, and understand where it behaves differently from a hosted one —
context windows, routing economics, quantization, structured output, licences.

Running a model on your own hardware — Ollama, `llama-server`, vLLM, LM Studio — needs no adapter and
no extra package. Point the OpenAI adapter at a base URL and it works.

**What this guide is actually for is the part that follows.** The reach is easy and documented in
half a dozen places; what is not documented anywhere else is how a small, self-hosted model behaves
differently from a hosted frontier one, and which of those differences Ananke can see. Every trap
below is real, most are silent, and one of them will re-route your traffic without saying anything.

---

## 1. Connecting

```csharp
using Ananke.Orchestration.OpenAI;

var local = OpenAIChatAgentModel.Create(
    apiKey: "no-key-required",          // most servers ignore it; the SDK requires something
    model: "llama3.2:1b",
    endpoint: new Uri("http://localhost:11434/v1"));
```

| Runtime | Default endpoint | Auth | Notes |
|---|---|---|---|
| **Ollama** | `http://localhost:11434/v1` | none | Easiest start. `ollama pull <model>` first |
| **llama.cpp** (`llama-server`) | `http://localhost:8080/v1` | none | Closest control over context and quantization |
| **vLLM** | `http://localhost:8000/v1` | optional bearer | Throughput-oriented; GPU-first |
| **LM Studio** | `http://localhost:1234/v1` | none | GUI-driven |

Include the `/v1` suffix: these servers expose their OpenAI-compatible routes underneath it.

**"Local" does not have to mean *this machine*.** Inference is memory-hungry and the box running your
application is often not the box with the RAM. Every example here takes a URL, so point it at
whichever host actually serves the model.

### From a manifest

```yaml
models:
  local:
    provider: openai
    model: llama3.2:1b
    endpoint: http://model-host:11434/v1
```

**No API key is required when an endpoint is set.** `ModelResolver` supplies a readable placeholder
rather than failing, because a self-hosted server has no key to give. A configured
`{section}:ApiKey` still wins, which is what hosted OpenAI-compatible providers — Groq, Together,
DeepSeek, Microsoft Foundry — need.

---

## 2. The context window is not the number in the catalogue

**This is the trap most likely to produce a wrong answer rather than an error.**

A model's context window is a property of its weights: `llama-3.2-1b` supports 128 K. The window that
binds at runtime is whatever the *server* was launched with, and the defaults are far smaller —
Ollama's `num_ctx` defaults to a few thousand tokens, and `llama-server` and vLLM have their own
flags. Exceed it and the runtime **silently truncates the prompt**. The model then answers
confidently about a conversation it never fully saw.

Tell the router the real number:

```csharp
var profile = ModelProfile.ForTier(
    "llama3.2:1b",
    local,
    ModelTier.TextBase,
    new LocalDeployment(
        Runtime: "ollama",
        Quantization: "Q4_K_M",
        EffectiveContextTokens: 4096,       // what the server was actually started with
        ServedBy: new Uri("http://model-host:11434/v1")),
    maxContextTokens: 128_000);             // what the weights support
```

`ModelProfile.ContextTokens` then reports `4096`, and routing filters on it. **If you write a custom
scoring function, read `ContextTokens`, not `MaxContextTokens`** — the latter describes the weights
and will happily rank a model on a window its server does not have.

Ananke cannot discover this number for you. No OpenAI-compatible endpoint reports it, and probing for
it would make startup depend on a server being up.

---

## 3. Zero cost wins every routing decision

A local model costs nothing per token. `RoutingStrategy.CheapestFit` ranks candidates by price, so a
zero-cost profile **sorts ahead of every paid model it qualifies against** — and because the
tie-break prefers speed, the *smallest* free model wins among several.

Add one local model to an existing router for privacy or for development, and it quietly takes every
request whose inferred requirements it nominally satisfies.

Ananke logs a one-time warning when a free, tier-1 model displaces a paid one. Two ways to fix it:

```csharp
// Say what the task actually needs …
var request = new AgentRequest { Messages = messages }.WithMinIntelligenceTier(3);

// … or rate the model honestly, so it stops qualifying for work it cannot do.
var profile = ModelProfile.ForTier("llama3.2:1b", local, ModelTier.TextBase, deployment)
    with { IntelligenceTier = 1 };
```

`TaskRequirements.MinIntelligenceTier` defaults to `1`, which is why the default behaviour is so
permissive.

---

## 4. Quantization changes behaviour without changing the name

`Q4_K_M` and `bf16` of the same weights are the same model by name and measurably different in
instruction-following and tool-call validity. Nothing in the OpenAI-compatible API reports which one
is loaded.

Record it on `LocalDeployment.Quantization`. Ananke does not act on the value — deriving a capability
downgrade from a quantization string would be a guess, and the whole design here is that Ananke never
guesses what a model can do. It is there so a human can see it, and so a bug report can say which
artefact was running.

---

## 5. Structured output and tool calling are where small models fail first

Ananke sends one structured-output shape: a JSON schema with a strictness flag. Local runtimes vary
in what they do with it — `llama-server` and vLLM implement real constrained decoding, some
compatibility layers accept only `{"type":"json_object"}`, and **some accept the field and ignore
it entirely.**

There is no detection and no negotiation. A request that asks for schema-strict JSON and receives
prose **succeeds** — the call returns, the schema is ignored, and nothing tells the caller.

The same holds for tool calling: whether a given deployment emits well-formed tool-call JSON is a
property of the weights and the quantization, not of the adapter.

**So test your deployment rather than assuming.** The repository ships a fixture that does exactly
this and reports what your server actually does:

```bash
LOCAL_MODEL_ENDPOINT=http://model-host:11434/v1 LOCAL_MODEL_ID=llama3.2:1b \
  dotnet test src/Ananke.slnx --filter TestCategory=LocalModel
```

It costs nothing and needs no account. If a schema request comes back as prose, the run says so.

**A practical shape that works:** use the small model for the narrow, high-volume hops —
classification, routing, extraction — and a larger one where a malformed tool call would break the
workflow. Each `AgentJob` takes its own model, so this needs no special support.

---

## 6. Local embeddings

The privacy argument is often *stronger* here than for chat: RAG is where your private documents are.
`OpenAIEmbeddingModel` takes the same `endpoint` parameter, so this works today.

Two things bite immediately:

- **Dimension mismatch.** Local embedders commonly emit 768 or 1024 dimensions against OpenAI's 1536.
  A Qdrant collection is created with a fixed vector size, so it must be sized to the model you are
  actually using.
- **Changing embedder invalidates the store.** Vectors from different models are not comparable, and
  nothing re-embeds silently on your behalf. Switching means re-ingesting.

See [06 — Memory](06-memory.md) for the pipeline itself.

---

## 7. What Ananke can and cannot tell you about where a call went

**It cannot.** Nothing in the framework records which endpoint served a given request — not a span
attribute, not a middleware hook. `LocalDeployment.ServedBy` records what you *configured*, which is
not the same claim and must not be presented as one.

This matters because it is usually the whole reason for running locally. If you need to
*demonstrate* that prompts never left a boundary — to an auditor, or under a data-residency
obligation — that evidence has to come from your network and infrastructure controls today, not from
Ananke. Saying so plainly is better than implying an assurance the framework does not provide.

---

## 8. Choosing a model

The catalogue can be queried by the things that decide this:

```csharp
// Small enough to serve on ordinary hardware
var small = ModelCatalog.SmallModels;

// Permissively licensed
var apache = ModelCatalog.LicensedUnder("apache-2.0");

// Self-hostable at all
var openWeights = ModelCatalog.All
    .Where(t => t.Classification.Weights == ModelWeights.OpenWeights);
```

**"Open weights" is not "open source".** Two of the most widely deployed open-weight families are
*not* OSI-approved: Llama carries acceptable-use terms and a monthly-active-user threshold, and Gemma
carries use restrictions. Both are recorded with `LicenseIsOsiApproved = false` and no SPDX
identifier, because none exists for either.

If that distinction is a policy for you, enforce it rather than documenting it:

```csharp
router.WithPolicy(p => p.Classification.Weights == ModelWeights.OpenWeights
                    && p.Classification.LicenseIsOsiApproved);
```

A policy filters candidates before any strategy ranks them, so no amount of price advantage can trade
it away.

Two caveats on the licence data, both deliberate. It describes **the weights as published** — running
an open-weight model through a hosted provider is additionally governed by that provider's terms,
which a catalogue cannot know. And it is a **filtering aid, not legal advice**: vendors do change
licences at a release, so a hard compliance boundary means confirming the licence for the artefact
you deploy.

---

## 9. What is not supported

**In-process inference.** ONNX Runtime GenAI, LLamaSharp and the Windows AI APIs load a model into
your process, so there is no endpoint to point a base URL at. That is a transport gap, not a
size one — a 70 B model in-process is equally unreachable, and a 1 B model behind `llama-server` is
equally reachable.

**Ananke does not manage weights.** No downloading, no serving, no model lifecycle. `ollama pull` is
not our command to own.

---

## See also

- [03 — Agents](03-agents.md) — the model interface these all implement
- [11 — Advanced agents](11-advanced-agents.md) — middleware, and model routing in general
- [06 — Memory](06-memory.md) — the knowledge pipeline the embedding notes apply to
- [21 — Enterprise clouds](21-enterprise-clouds.md) — Bedrock and Foundry, which reach open-weight models over the same OpenAI-shaped path
