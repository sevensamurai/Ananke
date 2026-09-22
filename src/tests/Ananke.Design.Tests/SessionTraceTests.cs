using Ananke.Abstractions.Tracing;
using Ananke.Design;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// One account of a session, joined from the two places it is reported from.
/// </summary>
/// <remarks>
/// <para>
/// The plan says what it decided, through events; the work below says what it did, through spans.
/// Neither alone answers *"what is going on"* — and the join is where the mistakes live, so the
/// tests are about attribution, silence, and what a fault is turned into.
/// </para>
/// </remarks>
[TestFixture]
public class SessionTraceTests
{
    [Test]
    public async Task Trace_AToolCall_IsAttributedToThePlanNodeThatMadeIt()
    {
        // Attribution comes from the span's own name — "plan-node:<id>/tool-loop" — and not from the
        // event stream, which is read by a consumer that can be a step behind the work.
        var trace = new SessionTrace();

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        await using (var call = job.StartSpan("plan-node:write-index/tool-loop", SpanKind.LlmCall))
        await using (var tool = call.StartSpan("tool:save", SpanKind.ToolCall))
        {
            tool.SetAttribute("output_length", "42");
        }

        trace.Lines.ShouldContain(l => l.Contains("write-index: called save") && l.Contains("42 chars"));
    }

    [Test]
    public async Task Trace_AModelCallThatDidNothingUnusual_SaysNothing()
    {
        // The step line already reports what the node came back with. A bare "asked the model"
        // beside it is the shape of trace that gets skimmed and then ignored.
        var trace = new SessionTrace();

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        await using (var call = job.StartSpan("plan-node:survey/answer", SpanKind.LlmCall))
        {
            call.SetAttribute("response_length", "400");
        }

        trace.Lines.ShouldNotContain(l => l.Contains("asked the model"));
    }

    [Test]
    public async Task Trace_AModelCallThatUsedTools_SaysSo()
    {
        var trace = new SessionTrace();

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        await using (var call = job.StartSpan("plan-node:survey/tool-loop", SpanKind.LlmCall))
        {
            call.SetAttribute("tool_rounds", "2");
        }

        trace.Lines.ShouldContain(l => l.Contains("survey: asked the model") && l.Contains("2 tool round"));
    }

    [Test]
    public async Task Trace_AHallucinatedTool_IsExplainedRatherThanFlagged()
    {
        // The whole difference between a trace and a dump: `tool.hallucination=true` is an attribute,
        // and what a reader needs is that the model was told and can correct itself.
        var trace = new SessionTrace();

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        await using (var call = job.StartSpan("plan-node:survey/tool-loop", SpanKind.LlmCall))
        await using (var tool = call.StartSpan("tool:delete_everything", SpanKind.ToolCall))
        {
            tool.SetAttribute("tool.hallucination", "true");
            tool.SetAttribute("tool.hallucination.requested_name", "delete_everything");
        }

        var line = trace.Lines.Single(l => l.Contains("delete_everything"));

        line.ShouldContain("no such tool");
        line.ShouldContain("can correct itself");
        line.ShouldNotContain("tool.hallucination");
    }

    [Test]
    public async Task Trace_ATruncatedToolOutput_SaysWhatWasDoneAboutIt()
    {
        var trace = new SessionTrace();

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        await using (var call = job.StartSpan("plan-node:survey/tool-loop", SpanKind.LlmCall))
        await using (var tool = call.StartSpan("tool:read_changelog", SpanKind.ToolCall))
        {
            tool.SetAttribute("output_length", "8000");
            tool.SetAttribute("tool.output_capped", "true");
            tool.SetAttribute("tool.output_chars_after", "4000");
        }

        trace.Lines.ShouldContain(l => l.Contains("truncated to 4000 chars to fit the window"));
    }

    [Test]
    public void Note_APlanEvent_IsNarratedIntoTheSameAccount()
    {
        var trace = new SessionTrace();

        trace.Note(new PlanNodeDisputed
        {
            WorkflowName = "w",
            ExecutionId = "e",
            PlanId = "p",
            PlanVersion = 1,
            NodeId = "write-index",
            Criterion = "index.md fits in 5 lines",
            Reason = "the title and footer already take two"
        });

        trace.Lines.ShouldContain(l => l.Contains("disputes its contract"));
    }

    [Test]
    public void ToTranscript_IsEverythingInTheOrderItHappened()
    {
        var trace = new SessionTrace();

        trace.Note("first");
        trace.Note("second");

        trace.ToTranscript().ShouldBe($"first{Environment.NewLine}second{Environment.NewLine}");
    }

    [Test]
    public async Task Trace_WithNowhereToWrite_StillKeepsTheAccount()
    {
        // A caller that only wants the file — or a test — should not have to invent a writer, and
        // the transcript is the copy that exists whether or not anything was watching.
        var written = new List<string>();
        var trace = new SessionTrace(written.Add);

        await using (var scope = trace.StartTrace("delivery", "e1"))
        await using (var job = scope.StartSpan("plan"))
        {
            job.SetAttribute("workflow", "delivery");
        }

        written.ShouldNotBeEmpty();
        written.Count.ShouldBe(trace.Lines.Count);
    }
}
