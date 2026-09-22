using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Simulation;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a job calls the schema it asks for, which a provider validates before it answers anything.
/// </summary>
/// <remarks>
/// A generic answer type's runtime name carries its arity — <c>Draft`1</c> — and a backtick is outside
/// what a provider accepts, so a job over a generic answer was refused with HTTP 400 before the model
/// read a word of the prompt.
/// </remarks>
[TestFixture]
public class AgentJobSchemaNameTests
{
    /// <summary>What providers accept as the name of a response schema.</summary>
    private const string Accepted = "^[a-zA-Z0-9_-]+$";

    private sealed record Draft<TStep>
    {
        public IReadOnlyList<TStep> Steps { get; init; } = [];
    }

    private sealed record Leg
    {
        public string Place { get; init; } = string.Empty;
    }

    [Test]
    public async Task Build_AGenericAnswer_NamesTheSchemaSoAProviderAcceptsIt()
    {
        var sent = await SchemaNameAsync<Draft<Leg>>("""{"steps":[{"place":"kyoto"}]}""");

        sent.ShouldMatch(Accepted);
    }

    [Test]
    public async Task Build_AGenericAnswer_KeepsTheTypesOwnNameInTheSchemasName()
    {
        var sent = await SchemaNameAsync<Draft<Leg>>("""{"steps":[{"place":"kyoto"}]}""");

        sent.ShouldContain("Draft");
    }

    [Test]
    public async Task Build_APlainAnswer_IsNamedForItsTypeAsBefore()
    {
        var sent = await SchemaNameAsync<Leg>("""{"place":"kyoto"}""");

        sent.ShouldBe("Leg");
    }

    /// <summary>The schema name a job of this answer type asks a model for.</summary>
    private static async Task<string> SchemaNameAsync<TResponse>(string answer)
        where TResponse : class
    {
        AgentRequest? sent = null;

        var model = new SimulatedAgentModel(request =>
        {
            sent = request;
            return answer;
        });

        var job = AgentJobFactory.Create<string, TResponse>("naming", model)
            .WithPrompt(state => state)
            .MapResult((state, _) => state)
            .Build();

        await job.ExecuteAsync("go", TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

        return sent.ShouldNotBeNull().ResponseFormat.ShouldNotBeNull().SchemaName;
    }
}
