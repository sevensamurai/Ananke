using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using ItineraryDemo.Model;

namespace ItineraryDemo.Shared;

/// <summary>
/// The two models this demo runs on, when the keys are there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two roles, deliberately different sizes.</b> A step searches and reports what it found, which
/// is cheap, repetitive and gated by a check that will catch a wrong report. Deciding what a step's
/// options mean for the plan, and rewriting it when nothing fits, is the expensive judgement — so it
/// gets the stronger model. Each role's env var names the seat it sets.
/// </para>
/// <para>
/// <b>The work is never scripted.</b> A step's report and the Planner's rewrite both happen in a
/// model's head, and a recording of that decision is not the decision. No key means nothing to run.
/// </para>
/// </remarks>
internal sealed record TripModels(
    IAgentModel Executor, IAgentModel Supervisor, RoleModel ExecutorRole, RoleModel SupervisorRole)
{
    public string ExecutorName => ExecutorRole.Id;

    public string SupervisorName => SupervisorRole.Id;

    /// <summary>What the provider argument accepts.</summary>
    /// <remarks>
    /// Naming the provider is worth an argument because the two are not interchangeable in the way
    /// that matters: they rate-limit differently, they price differently, and a run that quietly
    /// picked the other one is a run whose findings are about the wrong account.
    /// </remarks>
    public static readonly string[] Providers = ["auto", "openai", "google"];

    /// <summary>
    /// The models for <paramref name="provider"/>, or <see langword="null"/> when it has no key.
    /// </summary>
    /// <remarks>
    /// Under <c>auto</c>, OpenAI first when both are set. Add a provider by adding a name here and
    /// two model ids — nothing else in the demo knows which one it got.
    /// </remarks>
    public static TripModels? For(string provider)
    {
        var auto = Is(provider, "auto");

        if ((auto || Is(provider, "openai")) && EnvKeys.Get("OPENAI_API_KEY") is { } openAiKey)
        {
            return Pair(
                (id, key) => OpenAIChatAgentModel.Create(key, id),
                openAiKey,
                fallback: EnvKeys.Get("OPENAI_MODEL"),
                sharedName: "OPENAI_MODEL",
                cheap: Models.OpenAI.Gpt56Luna,
                standard: Models.OpenAI.Gpt56Sol);
        }

        if ((auto || Is(provider, "google")) && EnvKeys.Get("GOOGLE_API_KEY") is { } googleKey)
        {
            return Pair(
                (id, key) => GeminiAgentModel.Create(key, id),
                googleKey,
                fallback: EnvKeys.Get("GEMINI_MODEL"),
                sharedName: "GEMINI_MODEL",
                cheap: Models.Google.Gemini35FlashLite,
                standard: Models.Google.Gemini31Pro);
        }

        return null;
    }

    /// <summary>What to tell somebody who has no key configured.</summary>
    /// <remarks>
    /// A demo that fails with "object reference not set" has told a reader nothing. This one names
    /// the variables, where they go, and what the demo would have done with them.
    /// </remarks>
    public static string Setup(string provider) =>
        $"""
        This demo needs a model. Nothing here is scripted: each step searches the service and
        reports the stays it found, and what the plan becomes when a stay will not fit is decided by
        a supervisor and a Planner. Without a key there is nothing to run.

        No key was found for '{provider}'. Set one in a .env at the repository root:

            OPENAI_API_KEY=sk-...        (or)        GOOGLE_API_KEY=...

        `.env.example` at the root lists every variable and is gitignored once copied. Optionally
        pin the ids with OPENAI_MODEL / GEMINI_MODEL, or per role with ANANKE_DEMO_EXECUTOR_MODEL
        and ANANKE_DEMO_SUPERVISOR_MODEL.

        Then: dotnet run --project src/demos/02-workflow-patterns/ItineraryDemo -- [{string.Join(" | ", Providers)}] [--hitl] [--plan <file>] [--verify]
        """;

    /// <remarks>
    /// The ids are overridable and fall back to the provider's own variable before the catalogue's
    /// current id — a model id is the thing most likely to age, and the id already in someone's
    /// <c>.env</c> is the one their account is known to serve.
    /// </remarks>
    private static TripModels Pair(
        Func<string, string, IAgentModel> create,
        string key,
        string? fallback,
        string sharedName,
        string cheap,
        string standard)
    {
        // Resolved by the framework rather than here, because "which model did this role get, and
        // did anybody decide that" is the question a dozen runs got wrong — and it was answered in
        // two demos, identically, and tested in neither.
        var executor = RoleModels.Resolve(
            "executor", EnvKeys.Get("ANANKE_DEMO_EXECUTOR_MODEL"), "ANANKE_DEMO_EXECUTOR_MODEL",
            fallback, sharedName, cheap);

        var supervisor = RoleModels.Resolve(
            "supervisor", EnvKeys.Get("ANANKE_DEMO_SUPERVISOR_MODEL"), "ANANKE_DEMO_SUPERVISOR_MODEL",
            fallback, sharedName, standard);

        return new TripModels(
            Bind(create(executor.Id, key), executor.Id),
            Bind(create(supervisor.Id, key), supervisor.Id),
            executor,
            supervisor);
    }

    /// <summary>Tells the model which model it is, when the catalogue knows the id.</summary>
    /// <remarks>
    /// An adapter cannot answer this for itself — the same OpenAI adapter serves Ollama, vLLM and
    /// Azure deployments — so an id the catalogue does not know is left unbound rather than guessed
    /// at.
    /// </remarks>
    private static IAgentModel Bind(IAgentModel model, string id) =>
        ModelCatalog.TryGet(id) is { } known ? model.WithProfile(known) : model;

    /// <summary>Whether the two roles actually got different models.</summary>
    public bool Escalated => RoleModels.Escalated(ExecutorRole, SupervisorRole);

    /// <summary>
    /// Why this pair is not the escalation it is wired as, or <see langword="null"/> when it is.
    /// </summary>
    /// <remarks>
    /// <b>Acted on rather than printed.</b> This was a parenthesis on a line of output for a dozen
    /// runs, every one of them reporting a stronger supervisor it never had. Saying it louder was
    /// not the fix; refusing to start was.
    /// </remarks>
    public string? Collapsed => RoleModels.Collapsed(ExecutorRole, SupervisorRole);

    public string Describe =>
        $"steps done by {ExecutorName}, planning and changes of plan by {SupervisorName}"
        + (Escalated ? "" : " (the same model, named for each role deliberately)");

    private static bool Is(string provider, string name) =>
        string.Equals(provider, name, StringComparison.OrdinalIgnoreCase);
}
