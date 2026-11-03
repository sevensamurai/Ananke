<!-- topic: enterprise-clouds, tags: bedrock, aws, azure, foundry, entra, a2a, agentcore, strands, byo-model-id -->
# 21 — Enterprise clouds (Bedrock, Microsoft Foundry)

Run Ananke against models served through AWS and Azure, where the boundary your organisation cares
about is the cloud account — not the model vendor.

The short version, and the thing worth internalising before any of the recipes below:

> **Every enterprise surface has converged on an OpenAI-shaped or Anthropic-shaped HTTP endpoint.
> What stays vendor-specific is authentication.** Ananke does not add a third wire format for Bedrock
> or Foundry. You point the existing adapters at the cloud's URL, and the authentication is yours.

## Calling a model, not deploying an agent

This page is about **calling a model**. Ananke does not deploy agents to a cloud's managed agent
platform; a workflow runs in your process and calls the model over HTTP. The credential it needs is
one with **model inference** rights:

| | Call a model |
|---|---|
| Microsoft Foundry | `AZURE_OPENAI_ENDPOINT` → `https://<res>.openai.azure.com/openai/v1/` |
| Google | `GOOGLE_API_KEY` (AI Studio) or ADC for the enterprise surface |
| Anthropic | `ANTHROPIC_API_KEY` |
| Amazon Bedrock | `AWS_BEARER_TOKEN_BEDROCK` or the IAM chain |

**A Foundry model key does not open a Foundry project endpoint**, and an AI Studio key does not
reach Agent Runtime. Those are platform-administration credentials, and a model call does not use
them. Getting this wrong produces authorization errors that read like connectivity errors.

If you already run agents on a managed platform such as Bedrock AgentCore, the interop path below
needs no cloud credentials in your process at all.

---

## Amazon Bedrock

**No Ananke package.** Bedrock serves an **OpenAI-compatible Chat Completions API** and an
**Anthropic-native Messages API**, and Ananke's OpenAI and Anthropic adapters both take an SDK client
you build. Authentication is whatever you put in that client's transport: a Bedrock API key, SigV4
signing from the AWS SDK, or your organisation's own broker. Ananke does not sign requests for any
cloud, and does not test that a given cloud's authentication works with a given adapter.

The base URLs, for a region such as `us-west-2`:

| Route | Base URL |
|---|---|
| Chat Completions | `https://bedrock-runtime.us-west-2.amazonaws.com/openai/v1/` |
| Messages (Claude) | `https://bedrock-runtime.us-west-2.amazonaws.com/anthropic/v1/` |

`bedrock-mantle.{region}.api.aws` serves the same two paths. A VPC interface endpoint uses its own
hostname; SigV4 still needs the region for the credential scope.

### Chat Completions

Put your authentication in a `DelegatingHandler` and give the OpenAI client a transport built on it:

```csharp
using System.ClientModel;
using System.ClientModel.Primitives;
using Ananke.Orchestration.OpenAI;
using OpenAI;
using OpenAI.Chat;

var http = new HttpClient(new MyBedrockAuthHandler { InnerHandler = new HttpClientHandler() });

var model = new OpenAIChatAgentModel(new ChatClient(
    "openai.gpt-oss-20b-1:0",
    new ApiKeyCredential("unused"), // the handler authenticates; the client still requires a value
    new OpenAIClientOptions
    {
        Endpoint = new Uri("https://bedrock-runtime.us-west-2.amazonaws.com/openai/v1/"),
        Transport = new HttpClientPipelineTransport(http)
    }));
```

`MyBedrockAuthHandler` is yours: it sets `Authorization: Bearer <key>` for a Bedrock API key, or signs
the request with SigV4 for IAM.

### Claude models on Bedrock

Claude does **not** serve Chat Completions on Bedrock. Use the Messages API, with the same handler:

```csharp
using Ananke.Orchestration.Anthropic;
using Anthropic;
using Anthropic.Core;

var claude = new AnthropicAgentModel(
    new AnthropicClient(new ClientOptions
    {
        BaseUrl = "https://bedrock-runtime.us-west-2.amazonaws.com/anthropic/v1/",
        ApiKey = "unused", // the handler authenticates
        HttpClient = http
    }),
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

Check `aws bedrock list-foundation-models --region <region>` for what your account can reach.

> ⚠️ **Amazon's own Nova and Titan families are not reachable**, nor are older Claude models. They
> speak only Bedrock's native Converse and Invoke APIs, which Ananke does not implement.

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

This overload is `[Experimental]` as `ANNKEEXP002`, because the OpenAI SDK constructor under it is
experimental too; suppress the id to use it — see [Experimental APIs](../reference/experimental-apis.md).
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

This path needs **no AWS credentials in your process.**

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
- [Model deprecations](../reference/model-deprecations.md) — the analyzer rules and the pragma
