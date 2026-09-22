using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Google;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Reaches the real Gemini Developer API. <b>Opt-in only</b> — see the remarks.
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
/// The Developer API rather than Gemini Enterprise Agent Platform: a key from
/// <c>aistudio.google.com</c> needs no billing account, no API enablement and no service account, so
/// this stays runnable by any contributor. The enterprise path shares this adapter and differs only
/// in credentials — Application Default Credentials instead of a key — so what it leaves unproven is
/// ADC and the regional endpoint, not the translation layer.
/// </para>
/// <para>
/// <b>This is the first end-to-end verification <c>Ananke.Orchestration.Google</c> has ever had.</b>
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Live tier — reaches Gemini Developer API. Opt in with: "
    + "dotnet test src/Ananke.slnx --filter TestCategory=Live")]
public sealed class GeminiLiveTests
{
    private const string Key = "GOOGLE_API_KEY";
    private const string Model = "GEMINI_MODEL";

    [Test]
    public async Task Gemini_answersAsync()
    {
        RequireOrIgnore(Key, Model);

        var model = GeminiAgentModel.Create(EnvFile.Get(Key)!, EnvFile.Get(Model)!);

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
    /// Pins that reasoning tokens are counted. A thinking model bills them as output and reports
    /// them separately from the visible answer, so a mapping that reads only
    /// <c>CandidatesTokenCount</c> under-reports the call — and <c>TokenUsage</c> feeds budget
    /// accounting, where an under-count is a budget that fails to stop a run.
    /// </summary>
    [Test]
    public async Task Usage_counts_reasoning_tokens_not_just_the_visible_answerAsync()
    {
        RequireOrIgnore(Key, Model);

        var model = GeminiAgentModel.Create(EnvFile.Get(Key)!, EnvFile.Get(Model)!);

        var response = await model.GenerateAsync(
            new AgentRequest { Messages = [AgentMessage.User("Say OK.")] },
            TestContext.CurrentContext.CancellationToken);

        response.Usage.ShouldNotBeNull();
        response.Usage!.InputTokens.ShouldBeGreaterThan(0);
        response.Usage.OutputTokens.ShouldBeGreaterThan(0);

        // A trivial reply is a handful of visible tokens; a thinking model spends many more getting
        // there. Asserting a floor above the visible answer is what catches a regression to
        // candidates-only, without pinning a count that Google is free to change.
        response.Usage.TotalTokens.ShouldBeGreaterThan(
            response.Usage.InputTokens + 5,
            "reasoning tokens are billed as output and must be counted — see OutputTokensOf");
    }

    private static void RequireOrIgnore(params string[] keys)
    {
        var missing = EnvFile.MissingKeys(keys);
        if (missing.Length > 0)
        {
            Assert.Ignore(
                $"Not set in .env or the environment: {string.Join(", ", missing)} — " +
                "skipping the live Gemini test. See .env.example.");
        }
    }
}
