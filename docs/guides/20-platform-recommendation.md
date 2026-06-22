<!-- topic: platform-recommendation, tags: nnke-platform, eval, recommender, platform-fit, hybrid-router, telemetry, governance, federation -->
# 20 — Platform Recommendation

Before deploying a workflow to a cloud platform, use `nnke-platform eval` to
score every candidate platform against your manifest and get a ranked
recommendation with per-platform explanations.

**Demo:** [LocalPlatformLoopDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/LocalPlatformLoopDemo) — includes a `nnke-platform eval` step with no API keys required

---

## Why a Recommender?

`nnke-platform validate` answers "can I deploy this manifest to platform X?"
(yes / no + error list).

`nnke-platform eval` answers "which platform fits this manifest *best*, and
why?" It scores all candidates and returns a ranked list with reasons — before
you provision anything.

The two commands are complementary:

| Command | Question | Network required |
|---|---|---|
| `validate` | Will this deploy on X? | No (structural check) |
| `eval` | Which platform fits best? | No (offline) |
| `eval --live` | Same, with live structural verification | No¹ |
| `compare` | How do deployed cells compare at runtime? | Yes |

¹ `--live` exercises `DeployabilityValidator` + emulator registry per candidate
— same offline checks as `validate`, one per platform.

---

## Scoring Model

Each platform receives a score in `[0, 1]` from four independent axes:

| Axis | Default weight | What it measures |
|---|---|---|
| **Capability coverage** | 1.0 | Fraction of `PlatformNative` tools the platform supports natively |
| **Strength alignment** | 1.0 | How well the manifest's `intents:` tags match the platform's declared strengths/weaknesses |
| **Cost & latency fit** | 0.5 | Whether the platform's cost and latency bands meet `budget:` / `slo:` constraints |
| **Governance fit** | 1.5 | Whether RBAC, private networking, content safety, and region requirements are satisfied |

A **Block** reason (from a missing mandatory capability, failed governance requirement,
or a live-validation error) zeroes the total score for that platform regardless
of other axes.

The final score is the weighted average of the four axes.

---

## Quick Start

```bash
# Score all platforms against the nearest .ananke.yml
nnke-platform eval

# Restrict to specific candidates
nnke-platform eval my-workflow.ananke.yml --candidates azure-ai vertex-ai

# Machine-readable output
nnke-platform eval --format json

# Markdown table (e.g. for a PR comment)
nnke-platform eval --format markdown

# Run a live structural validation pass per candidate
nnke-platform eval --live

# Export the recommendation as HybridRouter routing rules
nnke-platform eval --emit-rules routing-rules.json
```

---

## Manifest Extensions

The recommender reads four optional top-level sections in your manifest.
**Existing manifests work unchanged** — these sections are all optional and
the recommender falls back to capability-only scoring when they are absent.

```yaml
# my-workflow.ananke.yml (excerpt)
name: enterprise-search

tools:
  sharepoint_search:
    description: Search SharePoint
    binding:
      kind: platform
      capability: sharepoint_grounding

intents: [enterprise_data, governance, agentic_loop]

governance:
  rbac: true
  privateNetworking: true
  region: eastus
  contentSafety: true

budget:
  maxCostPerRunUsd: 0.50

slo:
  latencyP50Ms: 2000
```

### `intents`

A list of string tags that describe what the workflow does.
They are matched against each platform's `strengths` and `weaknesses` in
`platform-profiles.json`.  Any tag that is a strength adds to the score;
a weakness subtracts.  Unknown tags are neutral.

Common tags: `enterprise_data`, `governance`, `agentic_loop`, `reasoning`,
`code_execution`, `web_research`, `multimodal`, `bash`, `creative_writing`.

### `governance`

| Field | Type | Meaning |
|---|---|---|
| `rbac` | bool | Role-based access control is required |
| `privateNetworking` | bool | VNet / VPC-SC private networking is required |
| `contentSafety` | bool | Built-in content moderation is required |
| `region` | string | Specific Azure / GCP region required (e.g. `eastus`, `us-central1`) |

A required governance feature that the platform does not support becomes a
**Block** — the platform is removed from the ranking regardless of other scores.

### `budget`

| Field | Type | Meaning |
|---|---|---|
| `maxCostPerRunUsd` | decimal | Maximum acceptable cost per workflow execution in USD |

### `slo`

| Field | Type | Meaning |
|---|---|---|
| `latencyP50Ms` | int | Maximum acceptable p50 latency in milliseconds |

---

## Axis Weights

Override the default axis weights to tune the recommender for your priorities:

```bash
# Governance-first (default: 1.5 × governance already)
nnke-platform eval --weight-governance 3.0 --weight-cost-latency 0.0

# Cost-first
nnke-platform eval --weight-cost-latency 2.0 --weight-governance 1.0
```

---

## Live Validation Pass (`--live`)

`--live` overlays a structural validation result per candidate on top of the
offline score.  It uses `LocalPlatformValidator` with the emulator registry —
the same checks as `nnke-platform validate`, no credentials needed.

- `Error` diagnostics from validation → **Block** reason (zeroes the platform)
- `Warning` diagnostics → **Minus** reason (reduces the score)

Cloud-adapter live passes (contacting Azure AI, Vertex AI, Anthropic APIs) are
available when the corresponding adapter tool (`nnke-platform-azure`, etc.) is
installed.  Install an adapter and re-run `eval --live` to get credential and
quota checks in addition to structural ones.

---

## Emitting Routing Rules (`--emit-rules`)

Once you have a recommendation, let `nnke-platform` write it directly into a
routing-rules JSON file for `HybridRouter`:

```bash
nnke-platform eval --emit-rules routing-rules.json
```

The output is a JSON array of `RoutingRule` objects.  Pass the file to
`HybridRouter` at startup to make the host automatically route cells to the
recommended platform:

```csharp
var rulesJson = File.ReadAllText("routing-rules.json");
var rules = JsonSerializer.Deserialize<List<RoutingRule>>(rulesJson)!;
var router = new HybridRouter(registry, rules);
```

---

## Telemetry Calibration

When `RemoteMetricsTracker` has accumulated enough samples for a deployment on
a given platform, `PlatformRecommender` automatically overrides the qualitative
cost and latency bands from `platform-profiles.json` with data-driven estimates:

- Rising `TokensPerExecutionSlope` (> 10% per interval) → bumps cost band up
- Falling slope (< −10%) → bumps cost band down
- Stable → adds a "calibrated from telemetry (N samples, stable)" Plus reason

This is automatic when you pass a tracker to the recommender:

```csharp
var recommender = new PlatformRecommender(metricsTracker);
var report = recommender.Evaluate(manifest, toolKit);
```

---

## Programmatic API

```csharp
using Ananke.Federation.Recommendation;

var recommender = new PlatformRecommender();

var report = recommender.Evaluate(manifest, toolKit);

Console.WriteLine($"Recommended: {report.Recommended}");
foreach (var score in report.Scores)
{
    Console.WriteLine($"  {score.Platform}: {score.Total:P0}");
    foreach (var reason in score.Reasons)
        Console.WriteLine($"    [{reason.Kind}] {reason.Message}");
}

// With live validation
var validators = new IPlatformValidator[]
{
    new LocalPlatformValidator(emulatedPlatform: "azure-ai"),
    new LocalPlatformValidator(emulatedPlatform: "vertex-ai"),
};

var liveReport = await recommender.EvaluateWithLiveValidationAsync(
    manifest, toolKit, validators);
```

### Custom weights

```csharp
var weights = new RecommendationWeights
{
    CapabilityWeight  = 1.0,
    StrengthWeight    = 0.5,
    CostLatencyWeight = 0.5,
    GovernanceWeight  = 2.0   // governance matters most
};

var report = recommender.Evaluate(manifest, toolKit, weights: weights);
```

---

## Platform Profiles

Platform qualitative data is stored in
`src/Ananke.Federation/Recommendation/platform-profiles.json` (embedded resource).
Each entry has:

| Field | Meaning |
|---|---|
| `displayName` | Human-readable name |
| `strengths` | Intent tags the platform excels at |
| `weaknesses` | Intent tags the platform struggles with |
| `governance` | Governance capability flags |
| `costBand` | Qualitative cost: `low` / `medium` / `high` |
| `latencyBand` | Qualitative latency: `low` / `medium` / `high` |
| `regions` | Supported region prefixes or `["global"]` |

Use `nnke-platform profiles` to inspect the loaded profiles.

---

## See Also

- [`Ananke.Federation` README](https://github.com/sevensamurai/Ananke/tree/main/src/Ananke.Federation/README.md) — deployment, validation, and the local design loop
- [`nnke-platform` tool reference](../cli/nnke-platform-tool.md)
- [`HybridRouter` source](https://github.com/sevensamurai/Ananke/tree/main/src/Ananke.Federation/Hosting/HybridRouter.cs) — distribute cells across platforms
