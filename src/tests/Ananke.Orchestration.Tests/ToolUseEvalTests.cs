using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The floor: a workflow, two tools, a cheap model, and a typed answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>An eval rather than a unit test, and the distinction matters.</b> Everything else in this
/// suite runs against a scripted runner that does exactly what it is told, which is what makes the
/// suite fast and deterministic — and is also why it cannot see the failures that only a real model
/// produces: answering without calling the tool, calling the wrong one, or returning something that
/// does not fit the shape it was asked for. Those are the failures that matter at the bottom of the
/// stack, and nothing above it is worth trusting until they are ruled out.
/// </para>
/// <para>
/// <b>Every assertion here is about what the framework guarantees, not about what a model is like.</b>
/// The value in the answer must be the value a tool returned, so a model that guesses fails even
/// when it guesses correctly. The tool that should not have been called must not have been. And the
/// response must arrive as the declared type, because a schema that is not enforced is a comment.
/// </para>
/// <para>
/// <b>There is no persona.</b> The question, the tool descriptions and the response type are the
/// whole of what the model is given: if prose is needed on top of that, the floor is not solid, and
/// it is better to see that here than to discover it four tiers up.
/// </para>
/// <para>
/// Live and explicit — it spends model calls. Run with
/// <c>dotnet test --filter TestCategory=Live</c>, with <c>OPENAI_API_KEY</c> in the environment or
/// in the repo's <c>.env</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Calls a real model; needs OPENAI_API_KEY.")]
public class ToolUseEvalTests
{
    /// <summary>What the warehouse says, and the only place these numbers exist.</summary>
    /// <remarks>
    /// Deliberately not guessable. A model that answers from its own head gets a number that is not
    /// this one, so "did it use the tool" is decided by the answer rather than inferred from it.
    /// </remarks>
    private const int StockOnHand = 4173;
    private const int PriceInPence = 91_55;

    private sealed record Ask
    {
        public required string Question { get; init; }

        public Answer? Answer { get; init; }
    }

    /// <summary>The shape the model must reply in. A schema that is not enforced is a comment.</summary>
    private sealed record Answer
    {
        /// <summary>The stock-keeping unit the question was about.</summary>
        public required string Sku { get; init; }

        /// <summary>The number the tool returned — units in stock, or price in pence.</summary>
        public required int Value { get; init; }
    }

    [Test]
    public async Task AQuestionAboutStock_UsesTheStockTool_AndAnswersWithWhatItReturned()
    {
        var called = new List<string>();

        var result = await Run(called, "How many units of SKU-8842 are in stock?");

        called.ShouldBe(["stock_on_hand"]);

        var answer = result.Answer.ShouldNotBeNull("the model returned nothing that fit the schema");
        answer.Sku.ShouldBe("SKU-8842");
        answer.Value.ShouldBe(StockOnHand, "the answer must be the tool's number, not the model's");
    }

    [Test]
    public async Task AQuestionAboutPrice_UsesThePriceTool_AndNotTheOtherOne()
    {
        // The mirror of the first, so a run that passes by always reaching for the first tool
        // declared cannot pass both.
        var called = new List<string>();

        var result = await Run(called, "What is the price of SKU-8842, in pence?");

        called.ShouldBe(["price_in_pence"]);
        result.Answer.ShouldNotBeNull().Value.ShouldBe(PriceInPence);
    }

    [Test]
    public async Task AQuestionNeedingBoth_CallsBothBeforeAnswering()
    {
        // Two rounds of tool use rather than one, which is where a tool loop that only ever runs
        // once stops being able to answer anything real.
        var called = new List<string>();

        await Run(called, "For SKU-8842, report both how many are in stock and the price in pence.");

        called.ShouldContain("stock_on_hand");
        called.ShouldContain("price_in_pence");
    }

    // ── Fixtures ──

    /// <summary>
    /// One workflow, one agent job, two tools — the smallest thing that is still the real stack.
    /// </summary>
    /// <remarks>
    /// It goes through <c>Workflow</c> and <c>RunAsync</c> rather than calling the job directly,
    /// because the wiring between them is part of what is being checked: a job that works in
    /// isolation and not inside a workflow is not a working job.
    /// </remarks>
    private static async Task<Ask> Run(List<string> called, string question)
    {
        var tools = new ToolKit("warehouse")
            .AddTool(
                "stock_on_hand",
                "How many units of a SKU are in the warehouse right now.",
                tool => tool
                    .Param("sku", "The stock-keeping unit, e.g. SKU-1234.")
                    .OnExecute(args =>
                    {
                        called.Add("stock_on_hand");
                        return ToolResult.Ok($"{args.Get("sku")}: {StockOnHand} units on hand.");
                    }))
            .AddTool(
                "price_in_pence",
                "What one unit of a SKU costs, in pence.",
                tool => tool
                    .Param("sku", "The stock-keeping unit, e.g. SKU-1234.")
                    .OnExecute(args =>
                    {
                        called.Add("price_in_pence");
                        return ToolResult.Ok($"{args.Get("sku")}: {PriceInPence} pence per unit.");
                    }));

        var job = AgentJobFactory.Create<Ask, Answer>("ask", Model())
            .WithPrompt(state => state.Question)
            .WithTools(tools)
            .WithMaxToolRounds(4)
            .MapResult((state, answer) => state with { Answer = answer })
            .Build();

        var workflow = new Workflow<Ask>("warehouse-desk")
            .Job("ask", job)
            .Then("ask", Workflow.End);

        var run = await workflow.RunAsync(new Ask { Question = question }).ConfigureAwait(false);

        return run.State;
    }

    /// <summary>
    /// The cheapest current model, because the floor should not need an expensive one.
    /// </summary>
    /// <remarks>
    /// If these fail on the cheap model and pass on a stronger one, that is worth knowing and is not
    /// a reason to change the model: it says the guarantee depends on capability, which is the thing
    /// an eval exists to report. Override with <c>ANANKE_TEST_MODEL</c> to find out.
    /// </remarks>
    private static IAgentModel Model() => OpenAIChatAgentModel.Create(
        Keys.Require("OPENAI_API_KEY"),
        Environment.GetEnvironmentVariable("ANANKE_TEST_MODEL") ?? Models.OpenAI.Gpt56Luna);
}
