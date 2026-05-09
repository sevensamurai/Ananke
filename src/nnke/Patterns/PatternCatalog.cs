using Ananke.Orchestration.Workflows;

namespace Ananke.Tool.Patterns;

/// <summary>
/// Registry of all recognized workflow and agentic patterns in Ananke.
/// This is the single source of truth for <c>nnke patterns</c>, <c>nnke inspect</c>
/// pattern detection, and <c>nnke new workflow --pattern</c> validation.
/// </summary>
/// <remarks>
/// Patterns are divided into two styles:
/// <list type="bullet">
///   <item><b>Manifest-driven</b> — expressed in <c>.ananke.yml</c> DSL, bound via <c>WorkflowScaffold</c></item>
///   <item><b>Code-driven</b> — built via <c>AgenticPattern</c> fluent builders or <c>Workflow&lt;T&gt;</c> primitives</item>
/// </list>
/// </remarks>
internal static class PatternCatalog
{
    private static readonly Dictionary<string, PatternEntry> Registry = BuildRegistry();

    /// <summary>Looks up a pattern by key (case-insensitive).</summary>
    public static PatternEntry? Find(string key) =>
        Registry.GetValueOrDefault(key.ToLowerInvariant());

    /// <summary>Returns all registered patterns, ordered by style then key.</summary>
    public static IReadOnlyList<PatternEntry> All() =>
        Registry.Values
            .OrderBy(p => p.Style == "manifest" ? 0 : 1)
            .ThenBy(p => p.Key)
            .ToList();

    /// <summary>Returns all manifest-driven patterns.</summary>
    public static IReadOnlyList<PatternEntry> ManifestPatterns() =>
        Registry.Values.Where(p => p.Style == "manifest").OrderBy(p => p.Key).ToList();

    /// <summary>Returns all code-driven (agentic) patterns.</summary>
    public static IReadOnlyList<PatternEntry> AgenticPatterns() =>
        Registry.Values.Where(p => p.Style == "code").OrderBy(p => p.Key).ToList();

    private static Dictionary<string, PatternEntry> BuildRegistry()
    {
        var entries = new PatternEntry[]
        {
            // ── Manifest-driven patterns ─────────────────────────────

            new()
            {
                Key = "sequential",
                Title = "Sequential Chain",
                Style = "manifest",
                Topology = "a → b → c → End",
                DslEquivalent = "a -> b\nb -> c\nc -> End",
                ApiEntryPoint = "WorkflowScaffold.Parse<TState>(name, dsl)",
                UseCases = ["Simple ETL pipelines", "Linear processing chains", "Data validation flows"],
                ScaffoldCommand = "nnke new workflow <name> --pattern sequential",
                DocsRef = "nnke docs workflow-dsl",
                Description = """
                    The simplest topology: jobs execute in strict order, each passing
                    state to the next. No parallelism, no branching. Ideal for pipelines
                    where each step depends on the previous step's output.
                    """,
                ApiExample = """
                    var scaffold = WorkflowScaffold.Parse<MyState>("pipeline", \"\"\"
                        extract -> transform
                        transform -> load
                        load -> End
                        \"\"\");
                    """,
            },
            new()
            {
                Key = "etl",
                Title = "Extract-Transform-Load (ETL)",
                Style = "manifest",
                Topology = "extract → fork(transform_a, transform_b) → join → combine → End",
                DslEquivalent = "extract -> fork(transform_a, transform_b)\njoin(transform_a, transform_b) -> combine\ncombine -> End",
                ApiEntryPoint = "WorkflowScaffold.Parse<TState>(name, dsl)",
                UseCases = ["Data ingestion with parallel transforms", "Multi-source aggregation", "Document processing pipelines"],
                ScaffoldCommand = "nnke new workflow <name> --pattern etl",
                DocsRef = "nnke docs workflows",
                Description = """
                    A classic data pipeline: extract data, fork into parallel
                    transformation branches, join the results with a merge function,
                    and combine into a final output. The fork/join pattern provides
                    parallelism while the merge function ensures deterministic aggregation.
                    """,
                ApiExample = """
                    var scaffold = WorkflowScaffold.Parse<MyState>("etl", \"\"\"
                        extract -> fork(transform_a, transform_b)
                        join(transform_a, transform_b) -> combine
                        combine -> End
                        \"\"\");
                    scaffold.BindMerge("combine", branches => Merge(branches));
                    """,
            },
            new()
            {
                Key = "fan-out",
                Title = "Fan-Out / Fan-In",
                Style = "manifest",
                Topology = "dispatch → fork(worker_1, worker_2, ...) → join → aggregate → End",
                DslEquivalent = "dispatch -> fork(w1, w2, w3)\njoin(w1, w2, w3) -> aggregate\naggregate -> End",
                ApiEntryPoint = "WorkflowScaffold.Parse<TState>(name, dsl)",
                UseCases = ["Parallel API calls", "Distributed search", "Multi-agent consensus"],
                ScaffoldCommand = "nnke new workflow <name> --pattern fan-out",
                DocsRef = "nnke docs workflows",
                Description = """
                    Dispatch work to N parallel workers and collect results. Similar to
                    ETL but emphasizes the fan-out degree (many branches). Supports
                    fail-fast mode (abort on first failure) and best-effort mode
                    (continue despite failures).
                    """,
                ApiExample = """
                    var scaffold = WorkflowScaffold.Parse<MyState>("search", \"\"\"
                        dispatch -> fork(best-effort, search_a, search_b, search_c)
                        join(search_a, search_b, search_c) -> aggregate
                        aggregate -> End
                        \"\"\");
                    """,
            },
            new()
            {
                Key = "human-in-the-loop",
                Title = "Human-in-the-Loop",
                Style = "manifest",
                Topology = "process → [interrupt] → review → publish → End",
                DslEquivalent = "process -> review\nreview -> publish\npublish -> End\ninterrupt(review)",
                ApiEntryPoint = "Workflow<TState>.InterruptBefore(jobName)",
                UseCases = ["Approval workflows", "Content moderation", "Human oversight of AI decisions"],
                ScaffoldCommand = "nnke new workflow <name> --pattern human-in-the-loop",
                DocsRef = "nnke docs human-in-the-loop",
                Description = """
                    Pauses execution before a designated job so a human can review,
                    modify state, and approve continuation. Uses the interrupt directive
                    in DSL or InterruptBefore() in code. Requires checkpoint storage
                    to persist state across the pause.
                    """,
                ApiExample = """
                    workflow
                        .Job("analyze", analyzeJob)
                        .Job("review", reviewJob)
                        .Job("publish", publishJob)
                        .Chain("analyze", "review", "publish", Workflow.End)
                        .InterruptBefore("review");
                    """,
            },
            new()
            {
                Key = "sub-workflow",
                Title = "Sub-Workflow (Nested Composition)",
                Style = "manifest",
                Topology = "prepare → [sub-workflow] → finalize → End",
                DslEquivalent = "prepare -> inner\ninner -> finalize\nfinalize -> End\nsubflow(inner)",
                ApiEntryPoint = "Workflow<TState>.SubFlow<TChild>(name, inner, mapIn, mapOut)",
                UseCases = ["Reusable processing modules", "Nested orchestration", "Domain isolation"],
                ScaffoldCommand = "nnke new workflow <name> --pattern sequential",
                DocsRef = "nnke docs workflows",
                Description = """
                    Embeds a complete inner workflow as a single job within a parent
                    workflow. The mapIn function converts parent state to child state;
                    mapOut merges the child result back. Supports nesting up to
                    a configurable depth limit (default 5).
                    """,
                ApiExample = """
                    workflow.SubFlow("enrich",
                        innerWorkflow,
                        mapIn: s => s.Document,
                        mapOut: (s, doc) => s with { Document = doc },
                        maxDepth: 3);
                    """,
            },

            // ── Code-driven (agentic) patterns ───────────────────────

            new()
            {
                Key = "review-critique",
                Title = "Review and Critique (Generator-Critic)",
                Style = "code",
                Topology = "generator → critic → [approved?] → End  (loop if not approved)",
                DslEquivalent = null,
                ApiEntryPoint = "AgenticPattern.ReviewCritique<TState>(name)",
                UseCases = ["Content generation with quality review", "Code generation with automated testing", "Document drafting with editorial feedback"],
                ScaffoldCommand = "nnke new workflow <name> --pattern review-critique",
                DocsRef = "nnke docs agentic-patterns",
                Description = """
                    A generator agent produces output, a critic agent evaluates it
                    against quality criteria, and the loop repeats until the critic
                    approves or the iteration cap is reached. The two-agent design
                    prevents self-confirmation bias — the critic is a separate model
                    call with its own system prompt.
                    """,
                ApiExample = """
                    var workflow = AgenticPattern.ReviewCritique<ArticleState>("draft-review")
                        .WithGenerator(generatorAgent)
                        .WithCritic(criticAgent)
                        .Until(s => s.ApprovalScore >= 0.9)
                        .MaxIterations(5)
                        .Build();
                    """,
            },
            new()
            {
                Key = "iterative-refinement",
                Title = "Iterative Refinement",
                Style = "code",
                Topology = "refine → [quality met?] → End  (loop if not met)",
                DslEquivalent = null,
                ApiEntryPoint = "AgenticPattern.IterativeRefinement<TState>(name)",
                UseCases = ["Self-improving text generation", "Progressive summarization", "Incremental data cleaning"],
                ScaffoldCommand = "nnke new workflow <name> --pattern iterative-refinement",
                DocsRef = "nnke docs agentic-patterns",
                Description = """
                    A single agent refines its output over multiple cycles until a
                    quality threshold is met or the iteration cap is reached. Simpler
                    than review-critique: one agent plays both generator and evaluator.
                    Best when the quality metric is objective (e.g. length, coverage,
                    score).
                    """,
                ApiExample = """
                    var workflow = AgenticPattern.IterativeRefinement<DraftState>("polish")
                        .WithAgent(refinementAgent)
                        .Until(s => s.QualityScore >= 0.95)
                        .MaxIterations(8)
                        .Build();
                    """,
            },
            new()
            {
                Key = "router",
                Title = "LLM-Driven Router",
                Style = "code",
                Topology = "classify → [LLM picks branch] → branch_a | branch_b | ... → End",
                DslEquivalent = "classify -> router(branch_a, branch_b, branch_c)",
                ApiEntryPoint = "Workflow.DecideWithAgent<TState>(model)",
                UseCases = ["Intent classification", "Dynamic task routing", "Multi-path processing based on content"],
                ScaffoldCommand = "nnke new workflow <name> --pattern router",
                DocsRef = "nnke docs workflows",
                Description = """
                    An LLM reads the current state and selects which branch to execute
                    next. The router receives a prompt derived from the state and returns
                    one of the declared option names. Combines deterministic graph structure
                    with LLM-driven control flow.
                    """,
                ApiExample = """
                    workflow
                        .Job("classify", classifyJob)
                        .Job("support", supportJob)
                        .Job("sales", salesJob)
                        .Then("classify", Workflow.DecideWithAgent<MyState>(model)
                            .WithPrompt(s => $"Customer said: {s.Input}")
                            .WithOptions("support", "sales", Workflow.End)
                            .Build());
                    """,
            },
            new()
            {
                Key = "handoff",
                Title = "Agent-to-Agent Handoff",
                Style = "code",
                Topology = "triage → [handoff to specialist] → await response → resolve → End",
                DslEquivalent = null,
                ApiEntryPoint = "Handoff.To<TState, TMessage, TResponse>(...)",
                UseCases = ["Specialist delegation", "Cross-system orchestration", "Escalation workflows"],
                ScaffoldCommand = "nnke new workflow <name> --pattern handoff",
                DocsRef = "nnke docs distributed",
                Description = """
                    Delegates a task to an external agent (or another workflow) via a
                    handoff channel and awaits a response. The channel abstraction
                    supports in-memory (testing), MQTT (production), and custom
                    transports. The workflow pauses until the external agent responds
                    or the timeout expires.
                    """,
                ApiExample = """
                    workflow.Job("escalate", Handoff.To<TriageState, Payload, Reply>(
                        "specialist-queue",
                        channel,
                        state => new Payload { Summary = state.Summary },
                        (state, reply) => state with { Resolution = reply.Text }));
                    """,
            },
        };

        return entries.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
    }
}
