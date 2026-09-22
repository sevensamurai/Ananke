using Amazon.Runtime;
using Ananke.Abstractions.Agents;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Bedrock.Tests;

/// <summary>
/// The only tests here that reach AWS. Skipped unless a <c>.env</c> supplies credentials, so the
/// default build stays offline and free.
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
/// <b>Kept deliberately small.</b> Everything that can be asserted offline is asserted in
/// <see cref="BedrockStubTransportTests"/>, against the same adapters. The only thing these buy is
/// the half a stub cannot fake: that AWS itself accepts what we send. For SigV4 that is the whole
/// ballgame — a signature is either accepted or produces a 403 with no useful diagnostic, and no
/// amount of local verification distinguishes the two.
/// </para>
/// <para>
/// Adding more live tests would mostly re-buy the same evidence at the cost of a slower, flakier,
/// billed suite. One per authentication mode is the intended ceiling.
/// </para>
/// <para>
/// <b>Only non-Anthropic models are exercised here, deliberately.</b> Invoking a Claude model on
/// Bedrock requires a one-time use-case submission per AWS account (or at the organisation's
/// management account) before any request succeeds, and the details go to Anthropic. That is an
/// account-provisioning step no test can perform, so gating this suite on it would mean a live tier
/// that most contributors cannot run. The Anthropic route is documented on
/// <see cref="BedrockEndpoint.AnthropicMessagesBaseUrl"/> and covered offline in
/// <see cref="BedrockStubTransportTests"/>; what is not covered is an end-to-end call.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Live tier — reaches Amazon Bedrock. Opt in with: "
    + "dotnet test src/Ananke.slnx --filter TestCategory=Live")]
public sealed class BedrockLiveTests
{
    private const string Region = "BEDROCK_REGION";
    private const string ApiKey = "AWS_BEARER_TOKEN_BEDROCK";
    private const string ChatModelId = "BEDROCK_CHAT_MODEL_ID";

    private static void RequireOrIgnore(params string[] keys)
    {
        var missing = EnvFile.MissingKeys(keys);
        if (missing.Length > 0)
        {
            Assert.Ignore(
                $"Not set in .env or the environment: {string.Join(", ", missing)} — " +
                "skipping the live Bedrock test. See .env.example.");
        }
    }

    private static AWSCredentials Credentials()
    {
        var accessKey = EnvFile.Get("AWS_ACCESS_KEY_ID")!;
        var secretKey = EnvFile.Get("AWS_SECRET_ACCESS_KEY")!;
        var sessionToken = EnvFile.Get("AWS_SESSION_TOKEN");

        return sessionToken is null
            ? new BasicAWSCredentials(accessKey, secretKey)
            : new SessionAWSCredentials(accessKey, secretKey, sessionToken);
    }

    private static AgentRequest SayOk() => new()
    {
        SystemPrompt = "Reply with exactly: OK",
        Messages = [AgentMessage.User("Say OK.")]
    };

    [Test]
    public async Task Sigv4_is_accepted_by_bedrockAsync()
    {
        // The test that matters most in this file. Signing is verified offline against a golden
        // vector, but only AWS can say whether the signature is actually valid for a real request.
        RequireOrIgnore("AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", Region, ChatModelId);

        // Passed explicitly rather than left to the AWS chain: the chain reads process environment
        // variables, and would not see a key that lives only in `.env`.
        var model = BedrockAgentModel.CreateChatCompletions(
            BedrockEndpoint.Runtime(EnvFile.Get(Region)!),
            EnvFile.Get(ChatModelId)!,
            Credentials());

        var response = await model.GenerateAsync(SayOk(), TestContext.CurrentContext.CancellationToken);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Api_key_is_accepted_by_bedrockAsync()
    {
        RequireOrIgnore(ApiKey, Region, ChatModelId);

        var model = BedrockAgentModel.CreateChatCompletions(
            BedrockEndpoint.Runtime(EnvFile.Get(Region)!),
            EnvFile.Get(ChatModelId)!,
            EnvFile.Get(ApiKey)!);

        var response = await model.GenerateAsync(SayOk(), TestContext.CurrentContext.CancellationToken);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }
}
