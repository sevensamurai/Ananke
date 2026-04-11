# ADR-ARCH-007 — Agent Serialization Format Negotiation (TOON / JSON / YAML)

| Field          | Value                                                                                          |
|----------------|------------------------------------------------------------------------------------------------|
| **Status**     | Deferred                                                                                       |
| **Date**       | 2025-07-31                                                                                     |
| **Authors**    | —                                                                                              |
| **Deciders**   | Ananke maintainers                                                                             |
| **Tags**       | architecture, serialization, agents, llm, token-efficiency, toon, json, format-negotiation     |
| **Relates to** | Ananke.Abstractions, Ananke.Orchestration                                                      |

---

## Context

Ananke agents communicate structured data (episode logs, skill metadata, workflow
state) over an internal message bus. Today there is no formal contract for the
serialization format — messages are assumed to be JSON.

Different LLM backends have vastly different context-window sizes and
token-generation reliability:

| Backend class            | Context  | Format reliability          |
|--------------------------|----------|-----------------------------|
| Cloud models (Claude, GPT-4o) | 128 K+   | High — any format           |
| Mid-range local (Llama 3 8B, Mistral 7B) | 8–32 K  | Good for JSON, poor for novel formats |
| Small local (Phi-3-mini, Gemma 2B)       | 4–8 K   | JSON only; indentation-sensitive formats hallucinate |

**TOON** ([toonformat.dev](https://toonformat.dev/)) is a line-oriented,
indentation-based notation (spec v3.0, 2025-11-24) that maps to the JSON data
model but eliminates braces, mandatory key quoting, and commas. Its tabular mode
(`items[3]{name,price}:`) is particularly compact for repeated-schema payloads
like episode batches. A Core Profile (§19) exists for minimal implementations.

The idea: let agents **declare format capabilities** and have the orchestration
layer **negotiate** the wire format per conversation, similar to HTTP
`Accept` / `Content-Type` semantics:

```
preferred_format:  json | toon | yaml
accepted_formats:  [json, toon, yaml]
token_budget:      4096
```

The orchestrator would act as a **format bridge**, transcoding between agents
when their preferences differ.

## Evaluation

### Potential benefits

- **Token savings for constrained models.** TOON's tabular mode could compress
  repeated-schema payloads (episodes, skills) by 30–50 % vs pretty-printed JSON.
  Even compact JSON still carries `"key":` quoting overhead that TOON avoids.
- **Human readability.** TOON and YAML are more readable than compact JSON for
  logs, config, and debugging.
- **Future-proofing.** An `ISerializationFormat` abstraction decouples agents
  from any single format, allowing new formats (or compression) to be added
  without breaking existing agents.

### Risks and concerns

- **Tower of Babel.** Allowing every agent to pick its own format introduces
  combinatorial complexity in the orchestrator's bridging logic. With *N* formats
  there are *N²* potential translation paths. Testing, debugging, and reasoning
  about message flows become harder. A system where every participant speaks a
  different dialect is worse than one where everyone speaks the same language
  imperfectly.
- **No C# TOON implementation.** The reference implementation is TypeScript-only.
  Ananke would own the first .NET encoder/decoder — a maintenance burden for a
  format that may not gain traction.
- **Zero LLM training data for TOON.** Local models have never seen TOON. They
  require few-shot examples in the system prompt to produce it, partially
  negating the token savings.
- **Indentation hallucinations.** Both TOON and YAML share sensitivity to
  whitespace — the same class of errors that small models already struggle with.
  JSON's explicit delimiters are more robust to sloppy generation.
- **Overhead.** Format negotiation, transcoding, and capability advertisement add
  latency and complexity to every message exchange. For a library that values
  simplicity, this may not be justified until there is measured evidence of
  context-window pressure in production.

## Decision

**Deferred.** The current approach (compact JSON everywhere) is adequate. The
risks — particularly the Tower of Babel complexity and the absence of a C# TOON
implementation — outweigh the benefits today.

Revisit when:

1. **Measured need.** Profiling shows that token consumption in agent↔agent
   messages is a bottleneck for real workloads on constrained local models.
2. **TOON ecosystem matures.** A C# implementation exists (community or
   first-party) and local models gain TOON in their training data.
3. **Format count stays small.** If negotiation is reconsidered, cap supported
   formats at 2–3 (e.g., compact JSON + TOON) and require all agents to accept
   JSON as the universal fallback. This prevents the Babel scenario.

## If / when revisited — recommended layering

```
Ananke.Abstractions   →  ISerializationFormat { string MediaType; T Decode<T>(...); string Encode<T>(...); }
Ananke.Orchestration  →  Format negotiation / bridging logic
Ananke.Formats.Json   →  Built-in default (System.Text.Json)
Ananke.Formats.Toon   →  Optional package (Core Profile only)
```

Rules to enforce:

- JSON is always the **mandatory fallback** — every agent MUST accept it.
- Negotiation is **per-conversation**, not per-message (avoids mid-stream format
  switches).
- The orchestrator never exposes format details to workflow authors — it is an
  infrastructure concern.

## Consequences

- **Now:** No code changes. Agents continue to use compact JSON via
  `System.Text.Json`.
- **Future:** This ADR serves as the design record if format negotiation is
  implemented. The layering and Babel-prevention rules above should be followed.
