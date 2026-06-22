namespace Ananke.Design.Dsl;

/// <summary>
/// Discriminated union representing a single parsed line from the workflow DSL.
/// </summary>
internal abstract record ConnectionLine
{
    /// <summary><c>tool(name, ...)</c> — declares portable tool metadata in the DSL preamble.</summary>
    internal sealed record Tool(string Name, string Description, string[] Tags) : ConnectionLine;

    /// <summary><c>use(job, tool_a, tool_b, semantic: true)</c> — attaches declared tools to a job.</summary>
    internal sealed record Use(string JobName, string[] ToolNames, bool Semantic) : ConnectionLine;

    /// <summary><c>a -&gt; b</c></summary>
    internal sealed record Direct(string From, string To) : ConnectionLine;

    /// <summary><c>a -&gt; fork(b, c)</c> or <c>a -&gt; fork(b, c, mode: best-effort)</c></summary>
    internal sealed record Fork(string From, string[] Targets, string? Mode) : ConnectionLine;

    /// <summary><c>join(a, b) -&gt; c</c></summary>
    internal sealed record Join(string[] Sources, string Target) : ConnectionLine;

    /// <summary><c>a -&gt; router(b, c, End)</c></summary>
    internal sealed record Router(string From, string[] Options) : ConnectionLine;

    /// <summary>
    /// <c>a -&gt; loop(target, exit: x)</c> or <c>a -&gt; loop(target, exit: x, maxIterations: n)</c>
    /// — conditional back-edge to <paramref name="LoopTarget"/>, exiting to
    /// <paramref name="ExitTarget"/> once the bound condition is satisfied.
    /// </summary>
    internal sealed record Loop(string From, string LoopTarget, string ExitTarget, int? MaxIterations)
        : ConnectionLine;

    /// <summary><c>subflow(name)</c> — marks a job as a nested sub-workflow.</summary>
    internal sealed record SubFlow(string Name) : ConnectionLine;

    /// <summary><c>interrupt(name)</c> — pauses execution before the named job.</summary>
    internal sealed record Interrupt(string JobName) : ConnectionLine;

    /// <summary>
    /// <c>ask(name)</c> — marks a job as a free-text, input-collecting turn: pauses before
    /// the job (like <see cref="Interrupt"/>) plus a contract that resume injects the user's reply.
    /// </summary>
    internal sealed record Ask(string JobName) : ConnectionLine;
}
