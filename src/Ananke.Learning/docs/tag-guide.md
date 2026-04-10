# Tag Guide — Key Tags vs. Semantic Tags

`EmpiricalEntry` exposes two tag fields that serve different purposes.
Using the right field matters: key tags power exact-match filtering,
semantic tags power similarity scoring and learning.

## Quick rule

> If you'd filter by exact value → **key tag** (`Entry.Tags`).
> If it describes a property for similarity → **semantic tag** (`Description.SemanticTags`).

## `Entry.Tags` — Correlation keys

**Type:** `IReadOnlyList<string>`
**Query:** `RecallOptions.RequiredTags`, `BrowseOptions.RequiredTags` (AND filter)

Opaque identifiers for exact-match filtering. Think of these as foreign
keys into external systems. They answer: **"show me everything related
to this thing."**

- Never used for similarity scoring or learning
- High cardinality is fine — each value is filtered by exact match
- Convention: `prefix:value` (e.g., `release:v3.1.2`, `sku:ABC-123`)

```csharp
Tags = ["release:v3.1.2", "au-prod", "pr:42", "card:789"]
```

### When to use

| Scenario | Example tags |
|---|---|
| Correlate with a release / version | `release:v3.1.2`, `service:api-gateway` |
| Scope to an environment / region | `au-prod`, `nz-staging` |
| Link to an external record | `pr:42`, `card:789`, `sku:ABC-123` |
| Link to a device / asset | `device:sensor-42`, `firmware:v2.1` |

## `Description.SemanticTags` — Weighted descriptors

**Type:** `IReadOnlyDictionary<string, float>` (weights in [0.0, 1.0])
**Query:** `TagOverlapPredictionSource`, `TagImportanceTracker`, `OfflineLearner`

Carry domain meaning with relevance weights. Used by:
- `TagOverlapPredictionSource` for prediction-error reinforcement
- `OfflineLearner` for curiosity-driven discovery via tag overlap
- `TagImportanceTracker` for automatic weight learning

They answer: **"how similar is this entry to that one?"**

- Keep cardinality low — descriptors, not identifiers
- Weights express relevance: 1.0 = primary, 0.5 = secondary

```csharp
Description = new SemanticDescription
{
    Summary = "Timeout spike on api-gateway after connection pool refactor",
    SemanticTags = new Dictionary<string, float>
    {
        ["service:api-gateway"] = 1.0f,
        ["error:timeout"] = 1.0f,
        ["infra:redis"] = 0.9f,
        ["deploy.day:friday"] = 0.6f,
        ["cause:connection-pool-exhaust"] = 1.0f
    }
}
```

### When to use

| Scenario | Example semantic tags |
|---|---|
| Error classification | `error:timeout` 1.0, `error:oom` 0.8 |
| Service / component | `service:api-gateway` 1.0, `infra:redis` 0.9 |
| Category / domain | `category:electronics` 1.0, `defect:battery` 0.8 |
| Causal factors | `cause:connection-pool-exhaust` 1.0 |
| Contextual signals | `deploy.day:friday` 0.6, `metric:temperature` 1.0 |

## Why the split matters

Putting high-cardinality identifiers (release tags, SKUs, device IDs)
in `SemanticTags` dilutes similarity scoring — two entries about
completely different releases would appear dissimilar even if they
describe the same error pattern. Key tags keep identifiers out of the
similarity space while still enabling exact-match queries.

Conversely, putting descriptors in `Entry.Tags` makes them invisible
to `OfflineLearner` and `TagOverlapPredictionSource`, preventing the
framework from learning which properties matter.

## Domain examples

| Domain | Key tags (`Entry.Tags`) | Semantic tags (`SemanticTags`) |
|---|---|---|
| Backlog tool | `release:v3.1.2`, `pr:42`, `card:789` | `service:api-gw` 1.0, `error:timeout` 1.0 |
| Marketplace | `sku:ABC-123`, `order:98765` | `category:electronics` 1.0, `defect:battery` 0.8 |
| IoT fleet | `device:sensor-42`, `firmware:v2.1` | `metric:temperature` 1.0, `anomaly:drift` 0.9 |
