<!-- topic: enterprise-clouds, tags: bedrock, aws, azure, foundry, entra, sigv4, a2a, agentcore, strands, byo-model-id -->
# 21 — Enterprise clouds (Bedrock, Microsoft Foundry)

Run Ananke against models served through AWS and Azure, where the boundary your organisation cares
about is the cloud account — not the model vendor.

The short version, and the thing worth internalising before any of the recipes below:

> **Every enterprise surface has converged on an OpenAI-shaped or Anthropic-shaped HTTP endpoint.
> What stays vendor-specific is authentication.** Ananke does not add a third wire format for Bedrock
> or Foundry; it adds the credential plumbing and points the existing adapters at the right URL.

## Two kinds of credential — read this before provisioning anything

This page is about **calling a model**. Deploying and operating an agent on a cloud's managed
platform is a different job with different credentials, and it belongs to
[`nnke-platform`](../cli/nnke-platform-tool.md).

| | Call a model *(this page)* | Deploy an agent *(`nnke-platform`)* |
|---|---|---|
| Rights needed | Model inference | Platform **administration** |
| Microsoft Foundry | `AZURE_OPENAI_ENDPOINT` → `https://<res>.openai.azure.com/openai/v1/` | `AZURE_AI_ENDPOINT` → `https://<res>.services.ai.azure.com/api/projects/<project>` |
| Google | `GOOGLE_API_KEY` (AI Studio) or ADC for the enterprise surface | `GOOGLE_CLOUD_PROJECT` + ADC |
| Anthropic | `ANTHROPIC_API_KEY` | `ANTHROPIC_API_KEY` with the agents beta entitlement |
| Amazon Bedrock | `AWS_BEARER_TOKEN_BEDROCK` or the IAM chain | *(no federation adapter — see below)* |

**A Foundry model key does not open a Foundry project endpoint**, and an AI Studio key does not
reach Agent Runtime. Getting this wrong produces authorization errors that read like connectivity
errors, so it is worth checking which column you are in before spending anything.

**Ananke has no AWS federation adapter**, on purpose: Bedrock AgentCore deploys a container image
rather than a declarative agent definition, which is a build-and-publish pipeline rather than an
adapter. If you already run agents there, the interop path below needs no Bedrock package at all.

---

## Amazon Bedrock

```bash
dotnet add package Ananke.Orchestration.Bedrock
```

Bedrock serves an **OpenAI-compatible Chat Completions API** and an **Anthropic-native Messages
API**. Both accept either AWS SigV4 or a Bedrock API key.

### With IAM (SigV4)

The usual choice in an organisation that forbids long-lived keys. Credentials come from the standard
AWS chain — environment, profile, SSO, container, instance metadata — so existing AWS configuration
applies with no extra wiring.

```csharp
using Ananke.Orchestration.Bedrock;

var model = BedrockAgentModel.CreateChatCompletions(
    BedrockEndpoint.Runtime("us-west-2"),
    "openai.gpt-oss-20b-1:0");
```

### With a Bedrock API key

```csharp
var model = BedrockAgentModel.CreateChatCompletions(
    BedrockEndpoint.Runtime("us-west-2"),
    "qwen.qwen3-32b-v1:0",
    Environment.GetEnvironmentVariable("AWS_BEARER_TOKEN_BEDROCK")!);
```

### Claude models on Bedrock

Claude does **not** serve Chat Completions on Bedrock. Use the Messages API instead — same auth
choices, same endpoint object:

```csharp
var claude = BedrockAgentModel.CreateMessages(
    BedrockEndpoint.Runtime("us-west-2"),
    "us.anthropic.claude-haiku-4-5-20251001-v1:0");
```

Three things bite before that call succeeds, and each error points somewhere other than the cause:

1. **A one-time use-case submission per AWS account** — or at the organisation's management account
   — before any Anthropic model can be invoked; the details are shared with Anthropic. Until it is
   done you get not-found or access-denied naming the model, which reads like a typo.
2. **Bedrock's Claude ids are not the Anthropic API's ids.** The newest carry no version suffix
   (`anthropic.claude-sonnet-5`); dated ones do (`anthropic.claude-haiku-4-5-20251001-v1:0`).
3. **Dated ids generally need an inference-profile prefix** such as `us.` for on-demand throughput,
   or Bedrock refuses with *"Invocation of model ID … with on-demand throughput isn't supported"*.

Check `aws bedrock list-foundation-models --region <region>` for what your account can reach. Ananke
verifies this path offline only; its live tests use non-Anthropic models, since step 1 cannot be
automated.

> ⚠️ **A documentation discrepancy worth knowing about.** AWS's own OpenAI-SDK guidance says the
> Chat Completions route authenticates *with a Bedrock API key only*. Ananke's live test signs the
> same route with **SigV4** and it passed against the real service. The empirical result is the one
> to trust — but expect the docs to disagree, and re-check if that test ever starts failing.

### Private networking

A VPC interface endpoint's hostname carries no region, but SigV4 still needs one for the credential
scope — so `Custom` takes both:

```csharp
var endpoint = BedrockEndpoint.Custom(
    new Uri("https://vpce-0abc-1def.bedrock-runtime.us-east-1.vpce.amazonaws.com"),
    region: "us-east-1");
```

### Choosing an endpoint

`Runtime` and `Mantle` are **not** interchangeable. AWS's own guidance is to use both and pick per
use case.

| | `BedrockEndpoint.Runtime` | `BedrockEndpoint.Mantle` |
|---|---|---|
| Guardrails, intelligent prompt routing | ✅ | ❌ |
| Cross-Region inference | ✅ | ❌ |
| Usage attribution | IAM principal, request tags, inference profiles | Projects / Workspaces |
| Server-side tool use, pre-configured tools | ❌ | ✅ |
| Asynchronous / long-running inference | ❌ | ✅ |

### What is reachable — and what is not

| Reachable | Via |
|---|---|
| DeepSeek, Qwen 3, Mistral, GLM, Nemotron, MiniMax, Kimi, Gemma 3, gpt-oss, Grok, OpenAI GPT-5.6, Writer Palmyra Vision | Chat Completions |
| Current Claude models — Sonnet 5, Opus 4.7/4.8, Haiku 4.5, Fable 5, Mythos 5 | Messages |

> ⚠️ **Amazon's own Nova and Titan families are not reachable**, nor are older Claude models. They
> speak only Bedrock's native Converse and Invoke APIs, which `Ananke.Orchestration.Bedrock`
> deliberately does not implement. **If you need Nova, this package is not enough** — say so, because
> a Converse adapter is a separate decision rather than a missing feature.

---

## Microsoft Foundry (formerly Azure AI Foundry / Azure OpenAI)

**No extra package.** Foundry's `/openai/v1/` route is OpenAI-shaped and uses implicit versioning —
there is no `api-version` to manage — so the OpenAI adapter reaches it directly.

### With an API key

```csharp
var model = OpenAIChatAgentModel.Create(
    apiKey: Environment.GetEnvironmentVariable("AZURE_INFERENCE_CREDENTIAL")!,
    model: "my-deployment-name",
    endpoint: new Uri("https://<resource>.openai.azure.com/openai/v1/"));
```

### With Microsoft Entra ID (keyless)

Preferred in most tenants, and the case a plain base-URL override cannot express. Ananke takes an
*authentication policy*, not any vendor's credential type — so nothing in
`Ananke.Orchestration.OpenAI` references the **Azure.Identity** package:

```csharp
using System.ClientModel.Primitives;
using Azure.Identity;

var model = OpenAIChatAgentModel.Create(
    new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"),
    model: "my-deployment-name",
    endpoint: new Uri("https://<resource>.openai.azure.com/openai/v1/"));
```

The same overload accepts **any** `AuthenticationPolicy`, so corporate SSO, a token broker, or an
identity provider Ananke has never heard of all plug in the same way. There is also a convenience
overload taking an `AuthenticationTokenProvider` and a scope.

---

## You supply the model id

Neither surface ships a model catalogue in Ananke, and that is a decision rather than an omission.
Bedrock ids carry region prefixes (`us.`, `eu.`, `global.`) and version suffixes that churn; Foundry
addresses models by **deployment name**, which is yours to choose. A catalogue would be a maintenance
tax that buys nothing.

Two consequences to plan around:

- **Enterprise model ids sit outside `ModelCatalog.Validate` and outside the `ANNKE001/2/3`
  analyzers.** Nothing will warn you that a model was deprecated — track the cloud's own lifecycle
  pages.
- **A deployment name can collide with a known model id.** If you name a Foundry deployment `gpt-4o`
  or `gpt-4.1`, the literal `"gpt-4o"` in your code matches a *deprecated* entry in
  `model-lifecycle.json` and `ANNKE002` will flag it — advising a replacement that is wrong, because
  a deployment name is an alias that may front any model at all.

  The fix is the sanctioned one from
  [model deprecations](../reference/model-deprecations.md) — an annotated pragma at the site:

  ```csharp
  #pragma warning disable ANNKE002 // Foundry deployment name, not the OpenAI model id
  const string Deployment = "gpt-4o";
  #pragma warning restore ANNKE002
  ```

  The analyzer is deliberately **not** narrowed to avoid this: its exact-match rule over every string
  literal is what makes lifecycle enforcement work at all, and trading it away to silence a
  documented, pragma-able warning would be a bad bargain.

---

## Orchestrating agents you did not build

If a team already runs agents on **Bedrock AgentCore** — Strands, LangGraph, OpenAI Agents SDK — you
do not need any of the above to work with them. AgentCore speaks **A2A** and **MCP**, and so does
Ananke:

```csharp
using Ananke.A2A.Client;

// A remote A2A agent presented as an ordinary IStreamingAgentModel.
var remote = new A2AAgentModel(new A2AAgentModelOptions
{
    AgentUrl = new Uri("https://my-agent.example/")
});
```

That agent then drops into an `AgentJob` like any other model. The division of labour is the point:
**the remote agent owns its inner reasoning loop; Ananke owns the graph** — fork/join, checkpoints,
human-in-the-loop approval, budget accounting, and memory that persists across runs.

This path needs **no Bedrock package and no AWS credentials in your process.**

### What not to rebuild

For a team already inside one of these clouds, several things Ananke could offer already exist there:
Bedrock **Guardrails**, **CloudTrail** audit, **AgentCore Memory**, Foundry's **content safety** and
RBAC. Use them. Ananke's claim is *cross-provider* — one graph, one budget model, one memory, working
when half the workload is on Bedrock and half is not.

---

## See also

- [03 — Agents](03-agents.md) — the provider-agnostic model interface these all implement
- [11 — Advanced agent features](11-advanced-agents.md) — routing, caching, retry
- [12 — MCP and interop](12-mcp-and-interop.md) — the A2A and MCP surfaces in full
- [20 — Platform recommendation](20-platform-recommendation.md) — choosing a deployment target
- [Model deprecations](../reference/model-deprecations.md) — the analyzer rules and the pragma
