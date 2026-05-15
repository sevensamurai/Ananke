<!-- topic: demos-multi-agent, tags: demo, multi-agent, orchestration, router, worker, reviewer, agentic-patterns, review-critique, advanced-agents -->
# Demo — Multi-Agent

Build a router → worker → reviewer pipeline using Ananke's workflow primitives and the `AgenticPattern` library.

This walkthrough validates two things concretely: that provider-swapping works without touching the workflow shape, and that recognized agentic patterns (`ReviewCritique`) produce the same typed topology as hand-wiring the equivalent graph.

→ **Further reading:** [11 — Advanced Agents](../guides/11-advanced-agents.md) · [16 — Agentic Patterns](../guides/16-agentic-patterns.md)

---

## The Pattern

This example wires three roles:

```
User request
     │
     ▼
 [Router]  ── classifies intent ──►  [Worker A]  ──┐
                                  └►  [Worker B]  ──┤
                                                     ▼
                                               [Reviewer]
                                                     │
                                    approved? ──► [Done]
                                    rejected? ──► [Worker] (retry)
```

1. **Router** — classifies the input and routes to the appropriate specialist worker.
2. **Worker** — executes the task (e.g., write code, draft text, run a calculation).
3. **Reviewer** — critiques the output and either approves or sends it back.

---

## State

```csharp
record AgentPipelineState
{
    public string   Request  { get; init; } = "";
    public string   Intent   { get; init; } = "";   // set by Router
    public string   Draft    { get; init; } = "";   // set by Worker
    public string   Review   { get; init; } = "";   // set by Reviewer
    public bool     Approved { get; init; } = false;
}
```

---

## Router Job

```csharp
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;

var routerJob = AgentJob.Create<AgentPipelineState>("router", routerModel)
    .WithSystemPrompt("Classify the user's request as 'code' or 'text'. Reply with one word only.")
    .WithUserPrompt(s => s.Request)
    .MapResponse((s, reply) => s with { Intent = reply.Trim().ToLower() });
```

---

## Worker Jobs

```csharp
var codeWorker = AgentJob.Create<AgentPipelineState>("code_worker", workerModel)
    .WithSystemPrompt("You are an expert C# developer. Implement the requested feature.")
    .WithUserPrompt(s => s.Request)
    .MapResponse((s, reply) => s with { Draft = reply });

var textWorker = AgentJob.Create<AgentPipelineState>("text_worker", workerModel)
    .WithSystemPrompt("You are a professional technical writer. Write clear, concise content.")
    .WithUserPrompt(s => s.Request)
    .MapResponse((s, reply) => s with { Draft = reply });
```

---

## Reviewer Job

```csharp
var reviewerJob = AgentJob.Create<AgentPipelineState>("reviewer", reviewerModel)
    .WithSystemPrompt(
        "Review the draft. Reply with 'APPROVED' if it is good, or 'REJECTED: <reason>' if not.")
    .WithUserPrompt(s => $"Request: {s.Request}\n\nDraft:\n{s.Draft}")
    .MapResponse((s, reply) => s with
    {
        Review   = reply,
        Approved = reply.StartsWith("APPROVED", StringComparison.OrdinalIgnoreCase)
    });
```

---

## Assemble the Workflow

```csharp
var workflow = new Workflow<AgentPipelineState>("multi-agent-pipeline")
    .Job("router",      routerJob)
    .Job("code_worker", codeWorker)
    .Job("text_worker", textWorker)
    .Job("reviewer",    reviewerJob)

    // Router decides which worker handles this request
    .Then("router", Workflow.Decide<AgentPipelineState>(s =>
        s.Intent == "code" ? "code_worker" : "text_worker"))

    // Both workers hand off to the reviewer
    .Then("code_worker", "reviewer")
    .Then("text_worker", "reviewer")

    // Reviewer either approves (done) or sends back for a re-draft
    .Then("reviewer", Workflow.Decide<AgentPipelineState>(s =>
        s.Approved ? Workflow.End : (s.Intent == "code" ? "code_worker" : "text_worker")));

var result = await workflow.RunAsync(new AgentPipelineState
{
    Request = "Write a C# method that calculates Fibonacci numbers iteratively."
});

Console.WriteLine(result.State.Draft);
```

---

## Shorter: Using `AgenticPattern`

For a pure review loop without the router, use the built-in pattern builder:

```csharp
using Ananke.Orchestration;

var workflow = AgenticPattern.ReviewCritique<DraftState>("code-review")
    .WithGenerator(codeWorker)
    .WithCritic(reviewerJob)
    .Until(s => s.Approved)
    .MaxIterations(4)
    .Build();
```

`AgenticPattern` validates the topology at `Build()` and names the graph semantically. It's equivalent to the manual wiring above but discoverable via IntelliSense.

→ [All agentic patterns](../guides/16-agentic-patterns.md)
