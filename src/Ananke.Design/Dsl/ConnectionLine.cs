namespace Ananke.Design.Dsl;

/// <summary>
/// Discriminated union representing a single parsed line from the workflow DSL.
/// </summary>
internal abstract record ConnectionLine
{
    /// <summary><c>a -&gt; b</c></summary>
    internal sealed record Direct(string From, string To) : ConnectionLine;

    /// <summary><c>a -&gt; fork(b, c)</c> or <c>a -&gt; fork(b, c, mode: best-effort)</c></summary>
    internal sealed record Fork(string From, string[] Targets, string? Mode) : ConnectionLine;

    /// <summary><c>join(a, b) -&gt; c</c></summary>
    internal sealed record Join(string[] Sources, string Target) : ConnectionLine;

    /// <summary><c>a -&gt; router(b, c, End)</c></summary>
    internal sealed record Router(string From, string[] Options) : ConnectionLine;

    /// <summary><c>subflow(name)</c> — marks a job as a nested sub-workflow.</summary>
    internal sealed record SubFlow(string Name) : ConnectionLine;

    /// <summary><c>interrupt(name)</c> — pauses execution before the named job.</summary>
    internal sealed record Interrupt(string JobName) : ConnectionLine;
}
