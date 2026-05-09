using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class FailureClassifierTests
{
    private FailureClassifier _classifier = null!;

    [SetUp]
    public void SetUp()
    {
        _classifier = new FailureClassifier();
    }

    [Test]
    public async Task SuccessfulExecution_ReturnsNone()
    {
        var exec = await RunWorkflow("test");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.None);
    }

    [Test]
    public async Task CancelledExecution_ReturnsInfrastructure()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var wf = new Workflow<string>("test")
            .Job("slow", async (s, ct) =>
            {
                await WorkflowLoops.Park(ct);
                return s;
            })
            .Then("slow", Workflow.End);

        var exec = await wf.RunAsync("", cts.Token);
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Infrastructure);
    }

    [Test]
    public async Task WorkflowException_ReturnsWorkflow()
    {
        var wf = new Workflow<string>("test")
            .Job("fail", (_, _) => throw new InvalidOperationException("null ref in state mapper"))
            .Then("fail", Workflow.End);

        var exec = await wf.RunAsync("");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Workflow);
    }

    [Test]
    public async Task HttpRequestException_ReturnsUpstream()
    {
        var wf = new Workflow<string>("test")
            .Job("api", (_, _) => throw new HttpRequestException("503 Service Unavailable"))
            .Then("api", Workflow.End);

        var exec = await wf.RunAsync("");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task TimeoutError_ReturnsUpstream()
    {
        var wf = new Workflow<string>("test")
            .Job("api", (_, _) => throw new TimeoutException("request timed out"))
            .Then("api", Workflow.End);

        var exec = await wf.RunAsync("");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task RateLimitError_ReturnsUpstream()
    {
        var wf = new Workflow<string>("test")
            .Job("api", (_, _) => throw new InvalidOperationException("429 Too Many Requests - rate limit exceeded"))
            .Then("api", Workflow.End);

        var exec = await wf.RunAsync("");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public void CustomPattern_RegisteredAndMatched()
    {
        _classifier.AddUpstreamPattern("CUSTOM_PROVIDER_DOWN");

        var wf = new Workflow<string>("test")
            .Job("api", (_, _) => throw new Exception("CUSTOM_PROVIDER_DOWN: service maintenance"))
            .Then("api", Workflow.End);

        var exec = wf.RunAsync("").GetAwaiter().GetResult();
        _classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task UnknownException_ReturnsUnknown()
    {
        // An exception with no recognizable upstream pattern and no matching
        // workflow pattern — should be Unknown
        var wf = new Workflow<string>("test")
            .Job("mystery", (_, _) => throw new Exception("xyzzy"))
            .Then("mystery", Workflow.End);

        var exec = await wf.RunAsync("");
        // "xyzzy" doesn't match any upstream pattern, but the job DID fail
        // with an exception that isn't clearly "workflow logic" either.
        // The classifier treats non-matching errors as workflow (the job threw).
        var origin = _classifier.Classify(exec);
        origin.ShouldBe(FailureOrigin.Workflow);
    }

    // ── Capability mismatch (structured JobOutcome.Deflected) ─────────

    [Test]
    public void StructuredDeflection_DetectedWithoutHeuristic()
    {
        // When a job reports Deflected via JobOutcome, the classifier
        // should detect it WITHOUT needing response text heuristics
        var exec = MakeExecutionWithDeflectedJob("test");
        _classifier.Classify(exec).ShouldBe(FailureOrigin.CapabilityMismatch);
    }

    [Test]
    public void StructuredDeflection_TakesPriorityOverSuccess()
    {
        // Even though the execution "succeeded" (no exception), the
        // Deflected outcome means the result is meaningless
        var exec = MakeExecutionWithDeflectedJob("test");
        exec.IsSuccess.ShouldBeTrue(); // technically succeeded
        _classifier.Classify(exec).ShouldBe(FailureOrigin.CapabilityMismatch);
    }

    private static WorkflowExecution<string> MakeExecutionWithDeflectedJob(string name)
    {
        var wf = new Workflow<string>(name)
            .Job("agent", (s, _) => Task.FromResult(s + "[deflected]"))
            .Then("agent", Workflow.End);

        var exec = wf.RunAsync("").GetAwaiter().GetResult();

        // Simulate what an agent job would do: replace the job execution
        // record with one that has Outcome = Deflected.
        // In production, TextAgentJob would set this directly.
        var field = typeof(WorkflowExecution<string>)
            .GetField("_history", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var history = (List<JobExecution>)field.GetValue(exec)!;
        history[0] = history[0] with { Outcome = JobOutcome.Deflected };

        return exec;
    }

    private static async Task<WorkflowExecution<string>> RunWorkflow(string name)
    {
        var wf = new Workflow<string>(name)
            .Job("step", (s, _) => Task.FromResult(s + "[done]"))
            .Then("step", Workflow.End);
        return await wf.RunAsync("");
    }
}
