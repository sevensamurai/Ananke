using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Patterns;

namespace Ananke.Orchestration;

/// <summary>
/// Entry point for named agentic design patterns. Each method returns a fluent
/// builder that produces a <see cref="Workflow{TState}"/> pre-wired for a
/// recognized pattern (review-critique, iterative refinement, etc.).
/// <para>
/// The returned <see cref="Workflow{TState}"/> can be further customized with
/// checkpointing, tracing, metadata, and additional jobs — or embedded as a
/// <see cref="Workflow{TState}.SubFlow{TChild}(string, Workflow{TChild}, System.Func{TState, TChild}, System.Func{TState, TChild, TState}, int)"/> inside a larger workflow.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Pattern catalog:</b></para>
/// <list type="table">
///   <listheader>
///     <term>Method</term>
///     <description>Agentic pattern</description>
///   </listheader>
///   <item>
///     <term><see cref="ReviewCritique{TState}"/></term>
///     <description>
///     Review and Critique (Generator-Critic) — a generator agent produces output,
///     a critic agent evaluates it, and the loop repeats until the critic approves
///     or the iteration cap is reached.
///     </description>
///   </item>
///   <item>
///     <term><see cref="IterativeRefinement{TState}"/></term>
///     <description>
///     Iterative Refinement — a single agent refines its output over multiple
///     cycles until a quality threshold is met or the iteration cap is reached.
///     </description>
///   </item>
/// </list>
/// <para>
/// See also: <see cref="Agents.StreamingChatWorkflow"/> (streaming agent chat pattern)
/// and <see cref="Handoff"/> (agent-to-agent delegation pattern).
/// </para>
/// <para>
/// Each builder composes the same low-level primitives available on
/// <see cref="Workflow{TState}"/> (<c>Job</c>, <c>Then</c>, <c>Decide</c>).
/// Use the builders for guided, validated pattern construction. Drop to the
/// primitive layer when you need a custom pattern variant.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Review and Critique — generator + critic loop
/// var workflow = AgenticPattern.ReviewCritique&lt;ArticleState&gt;("draft-review")
///     .WithGenerator(generatorAgent)
///     .WithCritic(criticAgent)
///     .Until(s =&gt; s.ApprovalScore &gt;= 0.9)
///     .MaxIterations(5)
///     .Build();
///
/// // Iterative Refinement — single agent refine loop
/// var workflow = AgenticPattern.IterativeRefinement&lt;DraftState&gt;("polish")
///     .WithAgent(refinementAgent)
///     .Until(s =&gt; s.QualityScore &gt;= 0.95)
///     .MaxIterations(8)
///     .Build();
///
/// // Embed a pattern as a sub-workflow
/// var pipeline = new Workflow&lt;PipelineState&gt;("content-pipeline")
///     .Job("gather", gatherJob)
///     .SubFlow("review",
///         AgenticPattern.ReviewCritique&lt;ArticleState&gt;("review")
///             .WithGenerator(generator)
///             .WithCritic(critic)
///             .Until(s =&gt; s.Score &gt;= 0.9)
///             .Build(),
///         mapIn: s =&gt; s.Article,
///         mapOut: (s, a) =&gt; s with { Article = a })
///     .Job("publish", publishJob)
///     .Chain("gather", "review", "publish", Workflow.End);
/// </code>
/// </example>
public static class AgenticPattern
{
    /// <summary>
    /// Creates a builder for the <b>Review and Critique</b> pattern (also known as
    /// Generator-Critic). A generator agent produces output, a critic agent evaluates
    /// it against quality criteria, and the loop repeats until the critic approves or
    /// the iteration cap is reached.
    /// </summary>
    /// <typeparam name="TState">
    /// The workflow state type. Must carry enough information for the generator to
    /// produce output, the critic to evaluate it, and the <c>Until</c> predicate to
    /// determine convergence.
    /// </typeparam>
    /// <param name="name">
    /// Workflow name used in traces, logs, and diagram export. Choose something
    /// descriptive (e.g. <c>"draft-review"</c>, <c>"code-review"</c>).
    /// </param>
    /// <returns>A fluent builder. Call <see cref="ReviewCritiqueBuilder{TState}.Build"/>
    /// to produce the <see cref="Workflow{TState}"/>.</returns>
    /// <example>
    /// <code>
    /// var workflow = AgenticPattern.ReviewCritique&lt;ArticleState&gt;("draft-review")
    ///     .WithGenerator(generatorAgent)
    ///     .WithCritic(criticAgent)
    ///     .Until(s =&gt; s.ApprovalScore &gt;= 0.9)
    ///     .MaxIterations(5)
    ///     .Build();
    ///
    /// var result = await workflow.RunAsync(new ArticleState { Topic = "AI agents" });
    /// </code>
    /// </example>
    public static ReviewCritiqueBuilder<TState> ReviewCritique<TState>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ReviewCritiqueBuilder<TState>(name);
    }

    /// <summary>
    /// Creates a builder for the <b>Iterative Refinement</b> pattern. A single
    /// agent refines its output over multiple cycles until a quality threshold is
    /// met or the iteration cap is reached. Simpler than Review-Critique: one
    /// agent plays both the generator and evaluator roles.
    /// </summary>
    /// <typeparam name="TState">
    /// The workflow state type. Must carry both the current output and enough
    /// information for the <c>Until</c> predicate to determine convergence.
    /// </typeparam>
    /// <param name="name">
    /// Workflow name used in traces, logs, and diagram export.
    /// </param>
    /// <returns>A fluent builder. Call <see cref="IterativeRefinementBuilder{TState}.Build"/>
    /// to produce the <see cref="Workflow{TState}"/>.</returns>
    /// <example>
    /// <code>
    /// var workflow = AgenticPattern.IterativeRefinement&lt;DraftState&gt;("polish")
    ///     .WithAgent(refinementAgent)
    ///     .Until(s =&gt; s.QualityScore &gt;= 0.95)
    ///     .MaxIterations(8)
    ///     .Build();
    ///
    /// var result = await workflow.RunAsync(new DraftState { Draft = initialDraft });
    /// </code>
    /// </example>
    public static IterativeRefinementBuilder<TState> IterativeRefinement<TState>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new IterativeRefinementBuilder<TState>(name);
    }
}
