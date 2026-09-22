# Ananke.Orchestration.Bedrock

Amazon Bedrock access for Ananke — **authentication and endpoint construction, and nothing else.**

Bedrock serves an OpenAI-compatible Chat Completions API and an Anthropic-native Messages API. Ananke
already implements both wire formats, so this package does not add a third. What it adds is the half
that cannot be delegated: SigV4 signing, Bedrock API-key auth, and building the right base URL.

```csharp
// IAM / SigV4 — uses the standard AWS credential chain.
var model = BedrockAgentModel.CreateChatCompletions(
    BedrockEndpoint.Runtime("us-west-2"),
    "openai.gpt-oss-20b-1:0");

// Bedrock API key. Note the `us.` inference-profile prefix — see "Claude needs more than a
// model id" below.
var claude = BedrockAgentModel.CreateMessages(
    BedrockEndpoint.Runtime("us-west-2"),
    "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    Environment.GetEnvironmentVariable("AWS_BEARER_TOKEN_BEDROCK")!);

// Private networking — a PrivateLink interface endpoint, region given explicitly for signing.
var vpc = BedrockAgentModel.CreateChatCompletions(
    BedrockEndpoint.Custom(new Uri("https://vpce-….vpce.amazonaws.com"), "us-east-1"),
    "qwen.qwen3-32b-v1:0");
```

Both factories return the ordinary `OpenAIChatAgentModel` / `AnthropicAgentModel` — there is no
Bedrock-specific model type to learn.

## Choosing an endpoint

Not interchangeable, so the choice is explicit. AWS's own guidance is to use both and pick per use
case.

| | `BedrockEndpoint.Runtime` | `BedrockEndpoint.Mantle` |
|---|---|---|
| Guardrails, intelligent prompt routing | ✅ | ❌ |
| Cross-Region inference | ✅ | ❌ |
| Usage attribution | IAM principal, request tags, inference profiles | Projects / Workspaces |
| Server-side tool use, pre-configured tools | ❌ | ✅ |
| Asynchronous / long-running inference | ❌ | ✅ |

Both accept **either** SigV4 or a Bedrock API key.

## You supply the model id

There is **no Bedrock model catalogue here, by decision.** Bedrock ids carry region prefixes (`us.`,
`eu.`, `global.`) and version suffixes that churn; a catalogue would be a maintenance tax that buys
nothing, and cross-Region inference profiles make the prefix the caller's choice anyway.

Two consequences to know about:

- Bedrock ids sit **outside `ModelCatalog.Validate`** and **outside the `ANNKE001/2/3` analyzers**.
  Nothing will tell you a model was deprecated — check AWS's own model lifecycle pages.
- If a model id you pass happens to string-match a known model in `model-lifecycle.json`, the
  analyzer *will* flag the literal. Wrap it in an annotated `#pragma warning disable ANNKE00x` with a
  one-line reason, as `docs/reference/model-deprecations.md` describes.

## Claude needs more than a model id

Three things bite in order, and each error message points somewhere other than the cause:

1. **A one-time use-case submission per AWS account** (or at the organisation's management account)
   before Anthropic models can be invoked at all; the details are shared with Anthropic. Until it is
   done, calls fail with not-found or access-denied naming the model — which reads like a typo.
2. **The id is not the Anthropic API's id.** Bedrock's newest entries carry no version suffix
   (`anthropic.claude-sonnet-5`); dated ones do (`anthropic.claude-haiku-4-5-20251001-v1:0`).
3. **Dated ids generally need a cross-Region inference profile prefix** such as `us.` for on-demand
   throughput. Without it Bedrock answers *"Invocation of model ID … with on-demand throughput isn't
   supported"*.

`aws bedrock list-foundation-models --region <region>` is the source of truth for your account.

This path is **verified offline only** — the repository's live tests exercise non-Anthropic models,
because step 1 is not something a test suite can perform.

## Not everything on Bedrock is reachable

Reachable through the two compatible APIs: DeepSeek, Qwen, Mistral, GLM, Nemotron, MiniMax, Kimi,
Gemma, gpt-oss, Grok, OpenAI GPT-5.6, Writer Palmyra Vision — and current Claude models via Messages.

**Not reachable:** **Amazon's own Nova and Titan families**, and older Claude models. Those speak
only Bedrock's native Converse and Invoke APIs, which this package deliberately does not implement.
If you need Nova, this package is not enough and a real Converse adapter is a separate decision.

## Non-goals

Written down because a "thin layer" that grows a normalization table within two releases is exactly
what this package was scoped to avoid. **Promoting any of these is a separate decision with its own
ADR — never a maintenance change.**

- ❌ Converse / InvokeModel wire formats, and therefore Nova and Titan
- ❌ The OpenAI Responses API
- ❌ Model-id normalization, region-prefix handling, id families
- ❌ Cross-region inference profile management, capacity catalogues
- ❌ Bedrock Guardrails configuration — use it via the runtime endpoint, configured in AWS
- ❌ A model catalogue, `Models.Bedrock` constants, or a `Families` entry
- ❌ AgentCore deployment (that is the federation axis, and it is not built)

## Design

The short version: every enterprise surface has converged on an
OpenAI- or Anthropic-shaped HTTP endpoint, and what stays vendor-specific is authentication — so the
enterprise adapter problem is a credential problem wearing a wire-format costume.
