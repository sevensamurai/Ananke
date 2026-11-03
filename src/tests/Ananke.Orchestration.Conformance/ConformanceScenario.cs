namespace Ananke.Orchestration.Conformance;

/// <summary>
/// One named rule of the provider contract, as a delegate over the subject under test.
/// </summary>
/// <typeparam name="TSubject">
/// The contract being proved — <c>IStreamingAgentModel</c>, <c>IToolSchemaTranslator</c> or
/// <c>IJsonSchemaTranslator</c>.
/// </typeparam>
/// <remarks>
/// Scenarios are stateless and are enumerated without a subject, so a runner can list the contract
/// before it has anything to run it against. Assertions inside a scenario are Shouldly, which throws
/// its own exception type and needs no test framework.
/// </remarks>
public sealed class ConformanceScenario<TSubject>
{
    private readonly Func<TSubject, CancellationToken, Task<ConformanceOutcome>> _run;

    /// <summary>Creates an asynchronous scenario.</summary>
    /// <param name="name">Stable identifier — runners use it to name the test case.</param>
    /// <param name="run">The scenario body.</param>
    public ConformanceScenario(string name, Func<TSubject, CancellationToken, Task<ConformanceOutcome>> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        Name = name;
        _run = run;
    }

    /// <summary>Creates a synchronous scenario.</summary>
    /// <param name="name">Stable identifier — runners use it to name the test case.</param>
    /// <param name="run">The scenario body.</param>
    public ConformanceScenario(string name, Func<TSubject, ConformanceOutcome> run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);
        Name = name;
        _run = (subject, _) => Task.FromResult(run(subject));
    }

    /// <summary>The scenario's stable name.</summary>
    public string Name { get; }

    /// <summary>
    /// Runs the scenario against <paramref name="subject"/>, converting any assertion or unexpected
    /// exception into a <see cref="ConformanceStatus.Failed"/> outcome rather than letting it
    /// escape — a runner needs one uniform result for all three statuses.
    /// </summary>
    /// <param name="subject">The implementation under test.</param>
    /// <param name="cancellationToken">Cancels the scenario.</param>
    public async Task<ConformanceOutcome> RunAsync(TSubject subject, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _run(subject, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ConformanceOutcome.Fail(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
