using Ananke.Abstractions.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Reaches a real Microsoft Foundry deployment. Skipped unless a <c>.env</c> supplies the endpoint,
/// deployment name and key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two gates, and they do different jobs.</b> <c>[Explicit]</c> keeps this out of an ordinary
/// <c>dotnet test</c> run — the live tier costs money, depends on a service being up, and is not
/// something a contributor should trip over by having credentials configured. The
/// <c>RequireOrIgnore</c> check below then skips with a readable reason if someone opts in without
/// the keys. A category alone would do neither: it makes a test <i>selectable</i>, not <i>opt-in</i>.
/// </para>
/// <para>
/// Run them with <c>dotnet test src/Ananke.slnx --filter TestCategory=Live</c>.
/// </para>
/// <para>
/// <b>There is no Foundry adapter, and that is the claim under test.</b> Foundry's
/// <c>/openai/v1/</c> route is OpenAI-shaped and implicitly versioned, so
/// <see cref="OpenAIChatAgentModel"/> reaches it with nothing but a base-URL override. If that ever
/// stops being true, this test is where it surfaces — and the answer would be a new package rather
/// than a patch.
/// </para>
/// <para>
/// <b>Entra ID is deliberately not covered here</b> (postponed, 2026-08-20). Doing so would pull
/// <c>Azure.Identity</c> into the tree, which nothing in this repository references today — E3's
/// credential seam takes an <c>AuthenticationPolicy</c> precisely so that no vendor identity package
/// is required. That seam is covered offline by <c>OpenAICredentialSeamTests</c>; what stays
/// unproven is a real token issuer end to end.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Live tier — reaches Microsoft Foundry. Opt in with: "
    + "dotnet test src/Ananke.slnx --filter TestCategory=Live")]
public sealed class FoundryLiveTests
{
    private const string Endpoint = "AZURE_OPENAI_ENDPOINT";
    private const string Deployment = "AZURE_OPENAI_DEPLOYMENT";
    private const string Key = "AZURE_INFERENCE_CREDENTIAL";

    [Test]
    public async Task Foundry_answers_through_the_plain_openai_adapterAsync()
    {
        RequireOrIgnore(Endpoint, Deployment, Key);

        var model = OpenAIChatAgentModel.Create(
            apiKey: EnvFile.Get(Key)!,
            model: EnvFile.Get(Deployment)!,
            endpoint: BaseUrl());

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
    /// A deployment name is an alias that may front any model, so the id echoed back is the
    /// deployment's, not the underlying model's. Worth pinning: it is the reason Ananke ships no
    /// Foundry catalogue and why <c>ANNKE002</c> can fire on a deployment name that happens to match
    /// a retired model id.
    /// </summary>
    [Test]
    public async Task Usage_is_reported_so_budget_accounting_worksAsync()
    {
        RequireOrIgnore(Endpoint, Deployment, Key);

        var model = OpenAIChatAgentModel.Create(
            apiKey: EnvFile.Get(Key)!,
            model: EnvFile.Get(Deployment)!,
            endpoint: BaseUrl());

        var response = await model.GenerateAsync(
            new AgentRequest { Messages = [AgentMessage.User("Say OK.")] },
            TestContext.CurrentContext.CancellationToken);

        response.Usage.ShouldNotBeNull();
        response.Usage!.InputTokens.ShouldBeGreaterThan(0);
        response.Usage.OutputTokens.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Accepts the endpoint in either form the portal shows it, with or without a trailing slash.
    /// Getting this wrong silently truncates the last path segment when the SDK composes the route.
    /// </summary>
    private static Uri BaseUrl()
    {
        var configured = EnvFile.Get(Endpoint)!;
        return new Uri(configured.EndsWith('/') ? configured : configured + "/");
    }

    private static void RequireOrIgnore(params string[] keys)
    {
        var missing = EnvFile.MissingKeys(keys);
        if (missing.Length > 0)
        {
            Assert.Ignore(
                $"Not set in .env or the environment: {string.Join(", ", missing)} — " +
                "skipping the live Foundry test. See .env.example.");
        }
    }
}
