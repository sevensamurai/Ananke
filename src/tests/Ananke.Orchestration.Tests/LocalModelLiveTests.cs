using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Orchestration.OpenAI;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Reaches a real self-hosted, OpenAI-compatible model server — Ollama, <c>llama-server</c>, vLLM or
/// LM Studio. Skipped unless one is reachable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture exists.</b> Every other live tier here reaches a *cloud* host. Nothing in this
/// repository had ever run against a locally served model, while the documentation advertises the
/// reach on six pages — so every claim about local behaviour rested on the OpenAI-shaped path being
/// identical, and nothing checked it. This is the seam nobody crossed.
/// </para>
/// <para>
/// <b>"Local" means self-hosted, not <i>on this machine</i>.</b> The endpoint is configuration:
/// <c>LOCAL_MODEL_ENDPOINT</c> points wherever the server actually runs, and
/// <c>http://localhost:11434/v1</c> is only the fallback. Inference is memory-hungry and the machine
/// running the tests is often not the machine with the RAM; a fixture that hardcoded <c>localhost</c>
/// could not be run by the people who most need it.
/// </para>
/// <para>
/// <b>Two gates, doing different jobs.</b> <c>[Explicit]</c> keeps this out of an ordinary
/// <c>dotnet test</c> run, and the reachability probe then skips with a readable reason if someone
/// opts in without a server. It has its own category rather than joining <c>Live</c> deliberately:
/// this is the one live tier that costs nothing and needs no account, and opting into the free tier
/// must not opt you into the billed ones.
/// </para>
/// <para>
/// Run it with <c>dotnet test src/Ananke.slnx --filter TestCategory=LocalModel</c>.
/// </para>
/// <para>
/// <b>What these tests deliberately do not do is fail when a small model behaves like a small
/// model.</b> A 1B model asked for schema-strict JSON may well return prose. That is a fact about the
/// deployment, not a defect in the adapter, and the honest response is to record it — so the
/// capability probes below report through <c>Assert.Warn</c> rather than red. Making the runtime
/// *refuse* what it cannot do is a different piece of work with its own prerequisites, and it is not
/// in scope here.
/// </para>
/// <para>
/// <b>One thing this fixture cannot prove.</b> The catalogue's small-model templates declare a
/// 128 K context window, which describes the weights; what truncates a prompt is the serving
/// runtime's own setting, whose default is commonly 4 K. Nothing over this API reports that number,
/// so the gap is closed by letting a profile carry the effective value, not by a test here.
/// </para>
/// </remarks>
[TestFixture]
[Category("LocalModel")]
[Explicit("Local-model tier — needs a self-hosted OpenAI-compatible server. Opt in with: "
    + "dotnet test src/Ananke.slnx --filter TestCategory=LocalModel")]
public sealed class LocalModelLiveTests
{
    private const string EndpointKey = "LOCAL_MODEL_ENDPOINT";
    private const string ModelKey = "LOCAL_MODEL_ID";

    private const string DefaultEndpoint = "http://localhost:11434/v1";
    private const string DefaultModel = "llama3.2:1b";

    [Test]
    public async Task Local_server_answers_through_the_plain_openai_adapterAsync()
    {
        await RequireReachableAsync();

        var model = OpenAIChatAgentModel.Create(apiKey: ApiKey(), model: ModelId(), endpoint: BaseUrl());

        var response = await model.GenerateAsync(
            new AgentRequest
            {
                SystemPrompt = "Reply with exactly: OK",
                Messages = [AgentMessage.User("Say OK.")]
            },
            TestContext.CurrentContext.CancellationToken);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The manifest path, end to end, with no API key configured anywhere. This is the flow the
    /// documentation describes for a local-first user, and until the resolver stopped demanding a
    /// key it failed before reaching the server at all — on a secret the user had to invent.
    /// </summary>
    [Test]
    public async Task Manifest_with_an_endpoint_and_no_api_key_resolves_and_runsAsync()
    {
        await RequireReachableAsync();

        var manifest = WorkflowManifest.Parse([
            "name: local",
            "models:",
            "  local:",
            "    provider: openai",
            $"    model: {ModelId()}",
            $"    endpoint: {BaseUrl()}",
            "jobs:",
            "connections:",
        ]);

        var models = new ModelResolver()
            .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)
            .Resolve(manifest, _ => null);

        var response = await models["local"].GenerateAsync(
            new AgentRequest { Messages = [AgentMessage.User("Say OK.")] },
            TestContext.CurrentContext.CancellationToken);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Records whether this deployment honours a schema-strict structured-output request. A small
    /// model commonly returns prose instead, and the adapter has no way to detect or refuse that —
    /// there is one structured-output shape and no negotiation.
    /// </summary>
    [Test]
    public async Task Structured_output_reports_what_this_deployment_actually_returnsAsync()
    {
        await RequireReachableAsync();

        var model = OpenAIChatAgentModel.Create(apiKey: ApiKey(), model: ModelId(), endpoint: BaseUrl());

        var response = await model.GenerateAsync(
            new AgentRequest
            {
                Messages = [AgentMessage.User("Give me a result object whose result field says OK.")],
                ResponseFormat = new AgentResponseFormat(
                    "result",
                    JsonSchema: """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""")
            },
            TestContext.CurrentContext.CancellationToken);

        response.Text.ShouldNotBeNullOrWhiteSpace("A structured-output request must still return something");

        if (!IsJsonObject(response.Text!))
        {
            Assert.Warn(
                $"{ModelId()} answered a schema-strict request with non-JSON text. This is the "
                + "documented hazard, not a test failure: the request succeeded, the schema was "
                + $"ignored, and nothing told the caller. Returned: {Excerpt(response.Text!)}");
        }
    }

    /// <summary>
    /// Records whether this deployment emits a well-formed tool call. Tool-call JSON validity is one
    /// of the things small models are measurably worse at, and it degrades further under
    /// quantization without the model's name changing.
    /// </summary>
    [Test]
    public async Task Tool_calling_reports_what_this_deployment_actually_doesAsync()
    {
        await RequireReachableAsync();

        var model = OpenAIChatAgentModel.Create(apiKey: ApiKey(), model: ModelId(), endpoint: BaseUrl());

        var response = await model.GenerateAsync(
            new AgentRequest
            {
                Messages = [AgentMessage.User("What is the weather in Lisbon? Use the tool.")],
                Tools =
                [
                    new AgentTool(
                        "get_weather",
                        "Returns the current weather for a city",
                        """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")
                ]
            },
            TestContext.CurrentContext.CancellationToken);

        if (response.ToolCalls is not { Count: > 0 })
        {
            Assert.Warn(
                $"{ModelId()} returned text rather than a tool call when a tool was offered. "
                + "Recorded, not failed: whether a given deployment can drive tools is a property of "
                + $"the weights and quantization, not of the adapter. Returned: {Excerpt(response.Text)}");
            return;
        }

        foreach (var call in response.ToolCalls)
        {
            call.Id.ShouldNotBeNullOrEmpty("A tool call must carry an id the result can be correlated to");
            call.FunctionName.ShouldBe("get_weather");
            IsJsonObject(call.Arguments).ShouldBeTrue(
                $"Tool-call arguments must be a JSON object; got: {Excerpt(call.Arguments)}");
        }
    }

    private static string ModelId() => EnvFile.Get(ModelKey) ?? DefaultModel;

    /// <summary>
    /// A self-hosted server generally ignores the credential, but the client SDK still requires a
    /// non-empty one. <c>LOCAL_MODEL_API_KEY</c> covers the case where a gateway sits in front.
    /// </summary>
    private static string ApiKey() => EnvFile.Get("LOCAL_MODEL_API_KEY") ?? ModelResolver.PlaceholderApiKey;

    private static Uri BaseUrl()
    {
        var configured = EnvFile.Get(EndpointKey) ?? DefaultEndpoint;
        return new Uri(configured.EndsWith('/') ? configured : configured + "/");
    }

    /// <summary>
    /// Skips rather than fails when no server answers. The probe is deliberately short: the common
    /// case is nothing listening, which refuses immediately, and a developer who has not started a
    /// server should not wait to be told so.
    /// </summary>
    private static async Task RequireReachableAsync()
    {
        var baseUrl = BaseUrl();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "models"));
            using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, TestContext.CurrentContext.CancellationToken)
                .ConfigureAwait(false);

            // Any answer proves something is listening and speaking HTTP. Whether it likes this
            // particular route is the individual test's problem, not the probe's.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Ignore(
                $"No OpenAI-compatible server answered at {baseUrl} — skipping the local-model tests. "
                + $"Set {EndpointKey} (and optionally {ModelKey}, default '{DefaultModel}') in .env or "
                + "the environment, or start one locally. See .env.example.");
        }
    }

    private static bool IsJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var parsed = JsonDocument.Parse(text);
            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Excerpt(string? text) =>
        text is null ? "<null>" : text.Length <= 200 ? text : text[..200] + "…";
}
