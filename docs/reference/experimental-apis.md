<!-- topic: experimental-apis, tags: experimental, diagnostics, ANNKEEXP001, plan tier, suppress -->
# Experimental APIs

Some of Ananke's public surface is marked `[Experimental]`: it ships, it is tested, and it may still
change in any release — including a patch. Using it reports a diagnostic, an **error by default**, so
the compiler tells you before a release note does. Suppress the diagnostic to use the API.

Each experimental surface has its own id, so suppressing one does not suppress another.

| Id | Covers | Since |
|---|---|---|
| `ANNKEEXP001` | The plan tier | 0.9.1 |
| `ANNKEEXP002` | `OpenAIChatAgentModel.Create` with a credential | 0.9.1 |
| `ANNKEEXP003` | Fields of an endpoint's own: `OpenAIChatAgentModel.WithRequestFields`, `AgentRequest.RequestFields` | not yet released |
| `ANNKEEXP004` | The document knowledge base's additions: so far `OnnxEmbeddingModel`, `OnnxEmbeddingOptions`, `OnnxPooling`, `PostgresKnowledgeStore`, `PostgresKnowledgeCatalog`, `PostgresKnowledgeOptions`, `SpendingLimit`, `DocumentHeaderOptions`, `DocumentHeaders`, `DocumentProcessor.Headers`, `DocumentProcessor.Records`, `DatedRecordExtractor<TRecord>`, `DatedRecordReader<TRecord>`, `DocumentLinks`, `DocumentProcessor.LinkPatterns`, `DocumentProcessor.DropSections`, `DocumentProcessor.CleanText`, `IListableKnowledgeStore`, `DatedRecordTools`, `SearchResultLimits` | not yet released |

## ANNKEEXP001 — the plan tier

**What it covers:**

- every public type in `Ananke.Orchestration.Planning`;
- the entry points outside that namespace: `Workflow<TState>.Supervise` (all overloads),
  `AgenticPattern.SupervisedPlan` and `SupervisedPlanBuilder<TState>`;
- in `Ananke.Design`: `PlanManifest`, `PlanNodeManifest`, `PlanRevisionManifest`,
  `PlanManifestExporter`, `PlanReportExporter`, `PlanNarrator`, `PlanNarratorLines`,
  `PlanNarrationExtensions`, `PlanRunEnding` and `PlanEndingWords`;
- in `Ananke.Organics`: `RememberedPlanTerms`.

A member of a marked type is covered through its type.

**Why.** The plan tier is where Ananke's design work is still happening. Its rulings have moved
release to release, and its vocabulary and defaults are not settled. The marking says so in the
compiler rather than only in release notes.

**What is not covered.** `SessionTrace` and `JsonLinesEventSink` in `Ananke.Design` work with any
workflow; they recognise plan events but using them reports nothing.

## ANNKEEXP002 — OpenAI credential overloads

**What it covers:** `OpenAIChatAgentModel.Create(AuthenticationPolicy, …)` and
`OpenAIChatAgentModel.Create(AuthenticationTokenProvider, …)` — the overloads that take a credential
instead of an API key, such as Microsoft Entra ID in front of Foundry.

**Why.** Both build the OpenAI SDK's `ChatClient` through constructors the SDK itself marks
experimental (`OPENAI001`). When the SDK changes them, these overloads change with it.

## ANNKEEXP003 — fields of an endpoint's own

**What it covers:** fields beside the ones the adapter writes: an OpenAI-compatible endpoint's own
parameters, such as OpenRouter's `provider`, `session_id` and `reasoning`.

- `OpenAIChatAgentModel.WithRequestFields` adds them to every request a model makes.
- `AgentRequest.RequestFields` carries them on one request. The OpenAI adapter writes them after its
  model's, so a field both name is the request's. The Gemini, Anthropic and A2A adapters have no way
  to send them, and refuse a request that carries any (`AgentRequest.ThrowIfRequestFields`).
- `AgentJob<TState, TResponse>.Builder.WithRequestFields` sets them on every request a job makes.

`CachingAgentModel` keys on a request's own fields, since they change what a call answers.

**Why.** The fields are written through the SDK's `JsonPatch`, which `System.ClientModel` marks
experimental (`SCME0001`). When the SDK changes it, this method changes with it.

## Suppressing a diagnostic

For a whole project, add the id to `NoWarn`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);ANNKEEXP001</NoWarn>
</PropertyGroup>
```

For a few lines, use a pragma:

```csharp
#pragma warning disable ANNKEEXP001 // the plan tier is experimental
var workflow = new Workflow<TripState>("trip")
    .Supervise("plan", BuildPlan, supervision, (state, result) => state with { Result = result });
#pragma warning restore ANNKEEXP001
```

Suppressing is a statement that you accept the surface may change. Read the release notes'
*Upgrading* section before taking a new version.

## ANNKEEXP004 — the document knowledge base's additions

**What it covers:** in `Ananke.Embeddings.Onnx`, `OnnxEmbeddingModel`, `OnnxEmbeddingOptions` and `OnnxPooling`; in
`Ananke.Postgres`, `PostgresKnowledgeStore`, `PostgresKnowledgeCatalog` and `PostgresKnowledgeOptions`; in
`Ananke.Orchestration`, `SpendingLimit`, `SpendingLimitedAgentModel`, `SpendingLimitExceededException` and
`WithSpendingLimit`; in `Ananke.Orchestration.Knowledge`, `DocumentHeaderOptions`, `DocumentHeaders`,
`DocumentProcessor.Headers`, `IDocumentRecords`, `DocumentProcessor.Records`, `DocumentLinks`, `DocumentProcessor.LinkPatterns`,
`DocumentProcessor.DropSections`, `DocumentProcessor.CleanText`, `IListableKnowledgeStore` and `SearchResultLimits`; in
`Ananke.Orchestration`,
`DatedRecordExtractor<TRecord>`, `DatedRecordOptions<TRecord>`, `RecordExtractionException`,
`DatedRecordReader<TRecord>`, `DatedRecord<TRecord>` and `DatedRecordTools`. The
knowledge base's other additions take the same id as they ship.

**Why.** They are new, and they are being shaped by the first product that uses them. Their options and defaults may
change before 1.0.

**What is not covered.** `EmbeddingModelIdentity` and `IEmbeddingModel.Identity`, which are settled,
`CatalogEntry.EffectiveAt`, `CatalogEntry.Replaces`, `MarkdownExtractor`'s front matter, and the search
modes of `InMemoryKnowledgeStore`, which are existing behaviour.
