using Ananke.Orchestration;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;
using AgenticDesignPatternsDemo;

// -------------------------------------------------------------------
//  Ananke — Agentic Design Patterns Demo
//
//  Each section demonstrates a recognized agentic pattern using
//  simulated models. No API keys required.
// -------------------------------------------------------------------

Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Ananke Agentic Design Patterns Demo");
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine();

await Demo01_SingleAgent();
await Demo02_SequentialChain();
await Demo03_ParallelForkJoin();
await Demo04_RouterCoordinator();
await Demo05_LoopPrimitive();
await Demo06_ReviewCritiquePattern();
await Demo07_IterativeRefinementPattern();
await Demo08_HumanInTheLoop();
await Demo09_SubFlowComposition();
await Demo10_AgentMiddleware();
await Demo11_ContextStrategy();
await Demo12_BudgetTracking();
await Demo13_StreamingChat();
await Demo14_WorkflowStreaming();

Console.WriteLine();
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  All demos complete!");
Console.WriteLine("-----------------------------------------------------------");


// -----------------------------------------------------------------
//  1. Single Agent — tool-calling ReAct loop
// -----------------------------------------------------------------

async Task Demo01_SingleAgent()
{
    PrintHeader("1. Single Agent (ReAct tool-calling loop)");

    var model = SimulatedModel.Fixed("""{"Answer":"The weather in Seattle is sunny and 22°C — great for a walk!"}""");

    var tools = new ToolKit("weather")
        .AddTool("get_weather", "Gets current weather for a city",
            (string city) => ToolResult.Ok($"Sunny, 22°C in {city}"),
            "city", "City name");

    var agent = AgentJobFactory.Create<AgentState, AgentReply>("weather-agent", model)
        .WithSystemPrompt("You are a helpful weather assistant.")
        .WithPrompt(s => s.UserInput)
        .WithTools(tools)
        .MapResult((s, r) => s with { Output = r.Answer ?? "" })
        .Build();

    var workflow = new Workflow<AgentState>("single-agent")
        .Job("agent", agent)
        .Then("agent", Workflow.End);

    var result = await workflow.RunAsync(new AgentState { UserInput = "What's the weather in Seattle?" });
    Console.WriteLine($"  Output: {result.State.Output}");
    Console.WriteLine($"  Status: {result.Status}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  2. Sequential Chain — linear pipeline of jobs
// -----------------------------------------------------------------

async Task Demo02_SequentialChain()
{
    PrintHeader("2. Sequential Chain");

    var workflow = new Workflow<PipelineState>("content-pipeline")
        .Job("research", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Research = "AI agents are software that act autonomously." };
        })
        .Job("draft", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Draft = $"Article based on: {state.Research}" };
        })
        .Job("edit", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { FinalOutput = $"[Polished] {state.Draft}" };
        })
        .Chain("research", "draft", "edit", Workflow.End);

    var result = await workflow.RunAsync(new PipelineState());
    Console.WriteLine($"  Research: {result.State.Research}");
    Console.WriteLine($"  Draft:    {result.State.Draft}");
    Console.WriteLine($"  Final:    {result.State.FinalOutput}");
    Console.WriteLine($"  Jobs run: {result.History.Count}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  3. Parallel Fork/Join — concurrent branches
// -----------------------------------------------------------------

async Task Demo03_ParallelForkJoin()
{
    PrintHeader("3. Parallel Fork/Join");

    var workflow = new Workflow<ParallelState>("parallel-research")
        .Job("split", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Topic = "AI Safety" };
        })
        .Job("research-papers", async (state, ct) =>
        {
            await Task.Delay(50, ct); // Simulates slower research
            return state with { Papers = "Found 3 papers on AI alignment." };
        })
        .Job("research-news", async (state, ct) =>
        {
            await Task.Delay(30, ct);
            return state with { News = "EU AI Act passed; OpenAI announces safety board." };
        })
        .Job("synthesize", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Summary = $"Papers: {state.Papers} | News: {state.News}" };
        })
        .Then("split", Workflow.Fork("research-papers", "research-news"))
        .Join(["research-papers", "research-news"], "synthesize",
            states =>
            {
                var papers = states.FirstOrDefault(s => s.Papers is not null)?.Papers ?? "";
                var news = states.FirstOrDefault(s => s.News is not null)?.News ?? "";
                return states[0] with { Papers = papers, News = news };
            })
        .Then("synthesize", Workflow.End);

    var result = await workflow.RunAsync(new ParallelState());
    Console.WriteLine($"  Papers: {result.State.Papers}");
    Console.WriteLine($"  News:   {result.State.News}");
    Console.WriteLine($"  Summary: {result.State.Summary}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  4. Router / Coordinator — LLM-driven dispatch
// -----------------------------------------------------------------

async Task Demo04_RouterCoordinator()
{
    PrintHeader("4. Router / Coordinator (dynamic dispatch)");

    var workflow = new Workflow<RouterState>("smart-router")
        .Job("classify", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            var category = state.Input.Contains("code", StringComparison.OrdinalIgnoreCase)
                ? "technical" : "general";
            return state with { Category = category };
        })
        .Job("technical-agent", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Response = $"[Technical] Here's a code solution for: {state.Input}" };
        })
        .Job("general-agent", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Response = $"[General] Here's information about: {state.Input}" };
        })
        .Then("classify", Workflow.Decide<RouterState>(state =>
            state.Category == "technical" ? "technical-agent" : "general-agent"))
        .Then("technical-agent", Workflow.End)
        .Then("general-agent", Workflow.End);

    var result = await workflow.RunAsync(new RouterState { Input = "Write code for sorting" });
    Console.WriteLine($"  Input:    \"{result.State.Input}\"");
    Console.WriteLine($"  Category: {result.State.Category}");
    Console.WriteLine($"  Response: {result.State.Response}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  5. Loop Primitive — manual loop with termination
// -----------------------------------------------------------------

async Task Demo05_LoopPrimitive()
{
    PrintHeader("5. Loop Primitive (workflow-level cycle)");

    var workflow = new Workflow<LoopState>("retry-loop")
        .Job("attempt", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            var newAttempt = state.Attempt + 1;
            var quality = 0.3 * newAttempt;
            Console.WriteLine($"    Attempt {newAttempt}: quality = {quality:F1}");
            return state with { Attempt = newAttempt, Quality = quality };
        })
        .Loop("attempt",
            loopTarget: "attempt",
            exitTarget: Workflow.End,
            until: s => s.Quality >= 0.9,
            maxIterations: 5);

    var result = await workflow.RunAsync(new LoopState());
    Console.WriteLine($"  Final quality: {result.State.Quality:F1} after {result.State.Attempt} attempts");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  6. Review & Critique — AgenticPattern builder
// -----------------------------------------------------------------

async Task Demo06_ReviewCritiquePattern()
{
    PrintHeader("6. Review & Critique (generator-critic loop)");

    var iteration = 0;

    var generator = AgentJobFactory.Create<ArticleState, ArticleGenResponse>("generator",
            SimulatedModel.Json(new ArticleGenResponse { Draft = "AI agents can autonomously perform tasks." }))
        .WithPrompt(s => $"Write an article about: {s.Topic}. Current draft: {s.Draft}")
        .MapResult((s, r) =>
        {
            iteration++;
            return s with { Draft = $"[v{iteration}] {r.Draft}" };
        })
        .Build();

    var critic = AgentJobFactory.Create<ArticleState, ArticleCritiqueResponse>("critic",
            SimulatedModel.Json(new ArticleCritiqueResponse { Score = 0.0, Feedback = "Needs more depth." }))
        .WithPrompt(s => $"Critique this draft (0-1 score): {s.Draft}")
        .MapResult((s, r) =>
        {
            var score = Math.Min(1.0, 0.3 * iteration);
            Console.WriteLine($"    Critic score: {score:F1} — {r.Feedback}");
            return s with { Score = score, Feedback = r.Feedback ?? "" };
        })
        .Build();

    var workflow = AgenticPattern.ReviewCritique<ArticleState>("article-review")
        .WithGenerator(generator)
        .WithCritic(critic)
        .Until(s => s.Score >= 0.9)
        .MaxIterations(5)
        .Build();

    var result = await workflow.RunAsync(new ArticleState { Topic = "AI Agents" });
    Console.WriteLine($"  Final draft: {result.State.Draft}");
    Console.WriteLine($"  Final score: {result.State.Score:F1}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  7. Iterative Refinement — single agent self-loop
// -----------------------------------------------------------------

async Task Demo07_IterativeRefinementPattern()
{
    PrintHeader("7. Iterative Refinement (self-improvement loop)");

    var round = 0;
    var refineAgent = AgentJobFactory.Create<RefinementState, RefinementResponse>("refine",
            SimulatedModel.Json(new RefinementResponse { Output = "Refined output." }))
        .WithPrompt(s => $"Improve this output: {s.Output}")
        .MapResult((s, r) =>
        {
            round++;
            var quality = Math.Min(1.0, 0.25 * round);
            Console.WriteLine($"    Round {round}: quality = {quality:F2}");
            return s with { Output = $"[r{round}] {r.Output}", Quality = quality };
        })
        .Build();

    var workflow = AgenticPattern.IterativeRefinement<RefinementState>("polish")
        .WithAgent(refineAgent)
        .Until(s => s.Quality >= 0.95)
        .MaxIterations(8)
        .Build();

    var result = await workflow.RunAsync(new RefinementState { Output = "Initial rough draft." });
    Console.WriteLine($"  Final output:  {result.State.Output}");
    Console.WriteLine($"  Final quality: {result.State.Quality:F2}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  8. Human-in-the-Loop — interrupt + resume
// -----------------------------------------------------------------

async Task Demo08_HumanInTheLoop()
{
    PrintHeader("8. Human-in-the-Loop (interrupt + resume)");

    var checkpointStore = new InMemoryCheckpointStore();

    var workflow = new Workflow<ApprovalState>("approval-flow")
        .Job("analyze", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Analysis = $"Trade analysis for: {state.Request}" };
        })
        .Job("execute", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with
            {
                Result = state.Approved
                    ? $"Executed: {state.Analysis}"
                    : $"Rejected: {state.Analysis}"
            };
        })
        .Then("analyze", "execute")
        .Then("execute", Workflow.End)
        .InterruptAfter("analyze")
        .UseCheckpointing(checkpointStore);

    var execution = await workflow.RunAsync(new ApprovalState { Request = "Buy 100 AAPL" });
    Console.WriteLine($"  Status:   {execution.Status}");
    Console.WriteLine($"  Analysis: {execution.State.Analysis}");

    var resumed = await workflow.ResumeAsync(
        execution.Id,
        state => state with { Approved = true });

    Console.WriteLine($"  Resumed:  {resumed.Status}");
    Console.WriteLine($"  Result:   {resumed.State.Result}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  9. SubFlow Composition — nested workflows
// -----------------------------------------------------------------

async Task Demo09_SubFlowComposition()
{
    PrintHeader("9. SubFlow Composition (nested workflows)");

    var innerIteration = 0;
    var innerWorkflow = new Workflow<InnerState>("inner-review")
        .Job("review", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            innerIteration++;
            return state with
            {
                Output = $"[reviewed-v{innerIteration}] {state.Input}",
                Score = Math.Min(1.0, 0.5 * innerIteration)
            };
        })
        .Loop("review", loopTarget: "review", exitTarget: Workflow.End,
            until: s => s.Score >= 0.9, maxIterations: 3);

    var outerWorkflow = new Workflow<OuterState>("outer-pipeline")
        .Job("prepare", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Draft = "Raw content about AI patterns." };
        })
        .SubFlow("review-subflow", innerWorkflow,
            mapIn: outer => new InnerState { Input = outer.Draft },
            mapOut: (outer, inner) => outer with { FinalOutput = inner.Output })
        .Job("publish", async (state, ct) =>
        {
            await Task.Delay(10, ct);
            return state with { Published = true };
        })
        .Chain("prepare", "review-subflow", "publish", Workflow.End);

    var result = await outerWorkflow.RunAsync(new OuterState());
    Console.WriteLine($"  Draft:     {result.State.Draft}");
    Console.WriteLine($"  Reviewed:  {result.State.FinalOutput}");
    Console.WriteLine($"  Published: {result.State.Published}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  10. Agent-Level Middleware — pre/post LLM call hooks
// -----------------------------------------------------------------

async Task Demo10_AgentMiddleware()
{
    PrintHeader("10. Agent-Level Middleware (guardrails + logging)");

    var innerModel = SimulatedModel.Fixed("""{"Summary":"The data shows a 15% increase in Q3."}""");

    var guardrail = new GuardrailAgentModelMiddleware.Builder()
        .DenyPattern("pii-ssn", @"\b\d{3}-\d{2}-\d{4}\b")
        .DenyWhen("empty-response", (resp, _) => string.IsNullOrWhiteSpace(resp.Text))
        .Build();

    var safeModel = MiddlewareAgentModel.Wrap(innerModel, guardrail);

    var agent = AgentJobFactory.Create<MiddlewareState, SummaryResponse>("summarize", safeModel)
        .WithSystemPrompt("Summarize the provided data. Never include personal information.")
        .WithPrompt(s => s.Data)
        .MapResult((s, r) => s with { Summary = r.Summary ?? "" })
        .Build();

    var workflow = new Workflow<MiddlewareState>("guarded-workflow")
        .Job("summarize", agent)
        .Then("summarize", Workflow.End);

    var result = await workflow.RunAsync(new MiddlewareState { Data = "Q3 revenue: $1.2M, up 15%." });
    Console.WriteLine($"  Summary: {result.State.Summary}");
    Console.WriteLine($"  Status:  {result.Status} (guardrail passed)");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  11. Context Strategy — sliding window + summarizing
// -----------------------------------------------------------------

async Task Demo11_ContextStrategy()
{
    PrintHeader("11. Context Strategy (sliding window)");

    var messages = new List<AgentMessage>();
    for (var i = 1; i <= 20; i++)
        messages.Add(AgentMessage.User($"Message {i}: " + new string('x', 100)));

    var strategy = new SlidingWindowContextStrategy(maxTokens: 500);
    var compacted = await strategy.ApplyAsync(messages, "You are a helpful assistant.");

    Console.WriteLine($"  Original messages: {messages.Count}");
    Console.WriteLine($"  After compaction:  {compacted.Count}");
    Console.WriteLine($"  Strategy: SlidingWindowContextStrategy(maxTokens: 500)");

    var model = SimulatedModel.Fixed("""{"Reply":"I remember the recent context."}""");
    var agent = AgentJobFactory.Create<ContextState, ContextResponse>("chat", model)
        .WithSystemPrompt("You are a helpful assistant.")
        .WithPrompt(s => s.UserMessage)
        .WithContextStrategy(strategy)
        .MapResult((s, r) => s with { Reply = r.Reply ?? "" })
        .Build();

    var workflow = new Workflow<ContextState>("context-demo")
        .Job("chat", agent)
        .Then("chat", Workflow.End);

    var result = await workflow.RunAsync(new ContextState { UserMessage = "What did we discuss?" });
    Console.WriteLine($"  Reply:   {result.State.Reply}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  12. Budget / Cost Tracking — per-workflow cost caps
// -----------------------------------------------------------------

async Task Demo12_BudgetTracking()
{
    PrintHeader("12. Budget / Cost Tracking");

    var model = SimulatedModel.Json(
        new { Result = "analysis complete" },
        inputTokens: 500,
        outputTokens: 200);

    var agentA = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-a", model)
        .WithPrompt(s => "Analyze data set A")
        .MapResult((s, r) => s with { StepA = r.Result ?? "" })
        .Build();

    var agentB = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-b", model)
        .WithPrompt(s => "Analyze data set B")
        .MapResult((s, r) => s with { StepB = r.Result ?? "" })
        .Build();

    var agentC = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-c", model)
        .WithPrompt(s => "Final synthesis")
        .MapResult((s, r) => s with { StepC = r.Result ?? "" })
        .Build();

    var workflow = new Workflow<BudgetState>("budget-demo")
        .Job("agent-a", agentA)
        .Job("agent-b", agentB)
        .Job("agent-c", agentC)
        .Chain("agent-a", "agent-b", "agent-c", Workflow.End)
        .WithBudget(
            maxCost: 0.01m,
            costPer1KInputTokens: 0.003m,
            costPer1KOutputTokens: 0.006m);

    var result = await workflow.RunAsync(new BudgetState());
    Console.WriteLine($"  Status:         {result.Status}");
    Console.WriteLine($"  Estimated cost: ${result.EstimatedCost:F6}");
    Console.WriteLine($"  Total tokens:   {result.CumulativeUsage.TotalTokens}");
    Console.WriteLine($"    Input:        {result.CumulativeUsage.InputTokens}");
    Console.WriteLine($"    Output:       {result.CumulativeUsage.OutputTokens}");
    Console.WriteLine($"  Jobs completed: {result.History.Count}");

    Console.WriteLine();
    Console.WriteLine("  [Budget exceeded scenario — tight budget]");
    var tightWorkflow = new Workflow<BudgetState>("budget-tight")
        .Job("agent-a", agentA)
        .Job("agent-b", agentB)
        .Job("agent-c", agentC)
        .Chain("agent-a", "agent-b", "agent-c", Workflow.End)
        .WithBudget(
            maxCost: 0.003m,
            costPer1KInputTokens: 0.003m,
            costPer1KOutputTokens: 0.006m);

    var tightResult = await tightWorkflow.RunAsync(new BudgetState());
    Console.WriteLine($"  Status:         {tightResult.Status}");
    Console.WriteLine($"  Estimated cost: ${tightResult.EstimatedCost:F6}");
    Console.WriteLine($"  Error:          {tightResult.Result?.Error}");
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  13. Streaming Chat — StreamingChatWorkflow with tool calling
// -----------------------------------------------------------------

async Task Demo13_StreamingChat()
{
    PrintHeader("13. Streaming Chat (StreamingChatWorkflow)");

    var model = SimulatedModel.Fixed("The capital of France is Paris. It's known for the Eiffel Tower.");

    var tools = new ToolKit("geography")
        .AddTool("get_capital", "Gets the capital of a country",
            (string country) => ToolResult.Ok($"The capital of {country} is Paris."),
            "country", "Country name");

    Console.Write("  Streaming: ");
    await foreach (var evt in StreamingChatWorkflow.Create("chat", model)
        .WithSystemPrompt("You are a geography expert.")
        .WithTools(tools)
        .OnTextDelta(async delta => { Console.Write(delta); await Task.CompletedTask; })
        .BuildStream([AgentMessage.User("What is the capital of France?")]))
    {
        switch (evt)
        {
            case CompletedEvent completed:
                Console.WriteLine();
                Console.WriteLine($"  Completed: {completed.FullText?.Length ?? 0} chars");
                break;
            case ErrorEvent error:
                Console.WriteLine($"  Error: {error.Message}");
                break;
        }
    }
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  14. Workflow Streaming — real-time orchestration events
// -----------------------------------------------------------------

async Task Demo14_WorkflowStreaming()
{
    PrintHeader("14. Workflow Streaming (orchestration events)");

    var workflow = new Workflow<StreamState>("event-demo")
        .Job("step-1", async (state, ct) =>
        {
            await Task.Delay(50, ct);
            return state with { Progress = "Step 1 done" };
        })
        .Job("step-2", async (state, ct) =>
        {
            await Task.Delay(50, ct);
            return state with { Progress = "Step 2 done" };
        })
        .Chain("step-1", "step-2", Workflow.End);

    await foreach (var evt in workflow.StreamAsync(new StreamState()))
    {
        switch (evt)
        {
            case JobStarted<StreamState> js:
                Console.WriteLine($"  ? Job started:   {js.JobName}");
                break;
            case JobCompleted<StreamState> jc:
                Console.WriteLine($"  ? Job completed: {jc.JobName} ({jc.Duration.TotalMilliseconds:F0}ms)");
                break;
            case WorkflowCompleted<StreamState> wc:
                Console.WriteLine($"  ? Workflow completed! Success: {wc.Result.Success}");
                break;
        }
    }
    Console.WriteLine();
}

// -----------------------------------------------------------------
//  Helper
// -----------------------------------------------------------------

static void PrintHeader(string title)
{
    Console.WriteLine($"--- {title} ---");
}

// -------------------------------------------------------------------
//  State Records
// -------------------------------------------------------------------

record AgentState
{
    public string UserInput { get; init; } = "";
    public string Output { get; init; } = "";
}

record AgentReply
{
    public string? Answer { get; init; }
}

record PipelineState
{
    public string Research { get; init; } = "";
    public string Draft { get; init; } = "";
    public string FinalOutput { get; init; } = "";
}

record ParallelState
{
    public string Topic { get; init; } = "";
    public string? Papers { get; init; }
    public string? News { get; init; }
    public string Summary { get; init; } = "";
}

record RouterState
{
    public string Input { get; init; } = "";
    public string Category { get; init; } = "";
    public string Response { get; init; } = "";
}

record LoopState
{
    public int Attempt { get; init; }
    public double Quality { get; init; }
}

record ArticleState
{
    public string Topic { get; init; } = "";
    public string Draft { get; init; } = "";
    public double Score { get; init; }
    public string Feedback { get; init; } = "";
}

record ArticleGenResponse
{
    public string? Draft { get; init; }
}

record ArticleCritiqueResponse
{
    public double Score { get; init; }
    public string? Feedback { get; init; }
}

record RefinementState
{
    public string Output { get; init; } = "";
    public double Quality { get; init; }
}

record RefinementResponse
{
    public string? Output { get; init; }
}

record ApprovalState
{
    public string Request { get; init; } = "";
    public string Analysis { get; init; } = "";
    public bool Approved { get; init; }
    public string Result { get; init; } = "";
}

record InnerState
{
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
    public double Score { get; init; }
}

record OuterState
{
    public string Draft { get; init; } = "";
    public string FinalOutput { get; init; } = "";
    public bool Published { get; init; }
}

record MiddlewareState
{
    public string Data { get; init; } = "";
    public string Summary { get; init; } = "";
}

record SummaryResponse
{
    public string? Summary { get; init; }
}

record ContextState
{
    public string UserMessage { get; init; } = "";
    public string Reply { get; init; } = "";
}

record ContextResponse
{
    public string? Reply { get; init; }
}

record BudgetState
{
    public string StepA { get; init; } = "";
    public string StepB { get; init; } = "";
    public string StepC { get; init; } = "";
}

record BudgetResponse
{
    public string? Result { get; init; }
}

record StreamState
{
    public string Progress { get; init; } = "";
}
