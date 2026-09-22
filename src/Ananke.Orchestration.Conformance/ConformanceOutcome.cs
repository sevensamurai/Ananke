namespace Ananke.Orchestration.Conformance;

/// <summary>How a <see cref="ConformanceScenario{TSubject}"/> ended.</summary>
public enum ConformanceStatus
{
    /// <summary>The subject satisfied the scenario.</summary>
    Passed,

    /// <summary>
    /// The scenario does not apply to this subject — see <see cref="ConformanceOutcome.Reason"/>.
    /// Not a failure: several parts of the contract are conditional ("<i>when</i> usage is
    /// reported…"), and a provider that legitimately does not do the thing has nothing to prove.
    /// </summary>
    Skipped,

    /// <summary>The subject violated the scenario.</summary>
    Failed
}

/// <summary>
/// The result of running one <see cref="ConformanceScenario{TSubject}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because <c>Skipped</c> has no framework-neutral expression. In the NUnit
/// fixtures this suite grew from, "this scenario does not apply to this adapter" was written as
/// <c>Assert.Pass(reason)</c> — a thrown control-flow signal that only one runner understands.
/// Returning the distinction instead is what lets the contract be consumed from any runner
///
/// </para>
/// </remarks>
public sealed record ConformanceOutcome
{
    private ConformanceOutcome(ConformanceStatus status, string? reason, Exception? exception)
    {
        Status = status;
        Reason = reason;
        Exception = exception;
    }

    /// <summary>How the scenario ended.</summary>
    public ConformanceStatus Status { get; }

    /// <summary>Why it was skipped or how it failed. <see langword="null"/> when it passed.</summary>
    public string? Reason { get; }

    /// <summary>
    /// The assertion or unexpected exception behind a <see cref="ConformanceStatus.Failed"/>
    /// outcome, so a runner can rethrow it with its original stack rather than a summary of it.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>The subject satisfied the scenario.</summary>
    public static ConformanceOutcome Passed { get; } = new(ConformanceStatus.Passed, null, null);

    /// <summary>The scenario does not apply to this subject.</summary>
    /// <param name="reason">Why it does not apply — surfaced in the runner's output.</param>
    public static ConformanceOutcome Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ConformanceOutcome(ConformanceStatus.Skipped, reason, null);
    }

    /// <summary>The subject violated the scenario.</summary>
    /// <param name="reason">What the subject did wrong.</param>
    /// <param name="exception">The assertion that detected it, when there was one.</param>
    public static ConformanceOutcome Fail(string reason, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ConformanceOutcome(ConformanceStatus.Failed, reason, exception);
    }

    /// <inheritdoc />
    public override string ToString() =>
        Reason is null ? Status.ToString() : $"{Status}: {Reason}";
}
