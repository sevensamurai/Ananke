using Ananke.Organics.Healing;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;

namespace Ananke.Organics.Division;

/// <summary>
/// Classifies the origin of a workflow execution failure (or underperformance)
/// by inspecting the execution status, job history, error messages, and
/// optionally the agent's response content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three failure lanes:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Upstream</b> — detected from exception types and HTTP status codes
///         in job error messages.</item>
///   <item><b>Workflow</b> — detected from unhandled exceptions in code jobs,
///         state mapping errors, or missing tools.</item>
///   <item><b>Capability mismatch</b> — detected from the structured
///         <see cref="JobOutcome.Deflected"/> signal set by the agent job
///         when it determines it cannot serve the request.</item>
/// </list>
/// <para>
/// Upstream detection is heuristic-based (pattern matching on error messages).
/// Extend by registering custom patterns via <see cref="AddUpstreamPattern"/>.
/// Capability mismatch detection is deterministic via <see cref="JobOutcome.Deflected"/>.
/// </para>
/// </remarks>
public sealed class FailureClassifier
{
    private readonly List<string> _upstreamPatterns;

    /// <summary>
    /// Creates a <see cref="FailureClassifier"/> with the default OpenAI
    /// upstream error patterns (see <see cref="FailureClassifierProfiles.OpenAI"/>).
    /// </summary>
    public FailureClassifier()
        : this(patterns: null) { }

    /// <summary>
    /// Creates a <see cref="FailureClassifier"/> from an explicit list of
    /// <see cref="FailurePattern"/> records, as produced by
    /// <see cref="FailureClassifierBuilder.Build"/>.
    /// </summary>
    /// <param name="patterns">
    /// Patterns to seed. Pass <see langword="null"/> to use the OpenAI default
    /// profile. Only <see cref="FailureOrigin.Upstream"/> patterns are currently
    /// consumed; other lanes are reserved for future use.
    /// </param>
    public FailureClassifier(IReadOnlyList<FailurePattern>? patterns)
    {
        if (patterns is null)
        {
            // Default: OpenAI profile — keeps backwards compatibility.
            _upstreamPatterns =
            [
                "429", "502", "503", "504",
                "Too Many Requests", "Service Unavailable", "Bad Gateway", "Gateway Timeout",
                "HttpRequestException", "TaskCanceledException", "TimeoutException",
                "SocketException", "IOException",
                "timed out", "connection refused", "network error",
                "rate limit", "Rate limit", "quota exceeded", "overloaded",
                "model_not_available", "server_error",
                "InternalServerError", "ServiceUnavailable"
            ];
        }
        else
        {
            _upstreamPatterns = patterns
                .Where(p => p.Lane == FailureOrigin.Upstream)
                .Select(p => p.Pattern)
                .ToList();
        }
    }

    /// <summary>
    /// Register an additional substring pattern that indicates an upstream error.
    /// When any job error message contains this pattern (case-insensitive),
    /// the failure is classified as <see cref="FailureOrigin.Upstream"/>.
    /// </summary>
    public void AddUpstreamPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        _upstreamPatterns.Add(pattern);
    }

    /// <summary>
    /// Classify the failure origin of a workflow execution.
    /// Returns <see cref="FailureOrigin.None"/> for successful executions.
    /// </summary>
    /// <remarks>
    /// Capability mismatch is detected exclusively from the structured
    /// <see cref="JobOutcome.Deflected"/> signal set by the agent job.
    /// No heuristic text matching is used.
    /// </remarks>
    public FailureOrigin Classify<TState>(WorkflowExecution<TState> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        // Structured signal: if any job reported Deflected, the cell
        // can't serve this request — deterministic, no heuristics.
        if (execution.History.Any(j => j.Outcome == JobOutcome.Deflected))
            return FailureOrigin.CapabilityMismatch;

        if (execution.IsSuccess)
            return FailureOrigin.None;

        // Infrastructure: cancellation and budget exceeded
        if (execution.Status is ExecutionStatus.Cancelled or ExecutionStatus.BudgetExceeded)
            return FailureOrigin.Infrastructure;

        // Inspect job error messages for upstream patterns
        var failedJobs = execution.History
            .Where(j => !j.Success && j.Error is not null)
            .ToList();

        if (failedJobs.Count == 0)
        {
            // Faulted but no job-level errors — likely a runner/framework issue
            return FailureOrigin.Unknown;
        }

        var hasUpstream = false;
        var hasWorkflow = false;

        foreach (var job in failedJobs)
        {
            if (IsUpstreamError(job.Error!))
                hasUpstream = true;
            else
                hasWorkflow = true;
        }

        // If ALL failed jobs are upstream → upstream
        // If ANY failed job is workflow → workflow (the workflow itself is broken,
        //   even if some failures happen to be upstream too)
        if (hasWorkflow)
            return FailureOrigin.Workflow;

        if (hasUpstream)
            return FailureOrigin.Upstream;

        return FailureOrigin.Unknown;
    }

    private bool IsUpstreamError(string errorMessage) =>
        _upstreamPatterns.Any(p =>
            errorMessage.Contains(p, StringComparison.OrdinalIgnoreCase));
}
