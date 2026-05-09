using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Shouldly;

namespace Ananke.Orchestration.Tests;

public record CounterState
{
    public int Value { get; init; }
    public List<string> Trail { get; init; } = [];
}

[TestFixture]
public class WorkflowBuilderTests
{
    [Test]
    public void Build_WithNoJobs_Throws()
    {
        var workflow = new Workflow<CounterState>("empty");

        Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public void Build_WithUndefinedConnectionTarget_Throws()
    {
        #pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined connection target
                var workflow = new Workflow<CounterState>("bad-conn")
                    .Job("a", (s, _) => Task.FromResult(s))
                    .Then("a", "nonexistent");
        #pragma warning restore ANANKE001

                Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public void Build_WithUndefinedConnectionSource_Throws()
    {
        #pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined connection source
                var workflow = new Workflow<CounterState>("bad-source")
                    .Job("a", (s, _) => Task.FromResult(s))
                    .Then("nonexistent", "a")
                    .Then("a", Workflow.End);
        #pragma warning restore ANANKE001

                Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public void Build_WithDuplicateJobName_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            new Workflow<CounterState>("dup")
                .Job("a", (s, _) => Task.FromResult(s))
                .Job("a", (s, _) => Task.FromResult(s)));
    }

    [Test]
    public void Build_SingleJob_SetsEntryJob()
    {
        var definition = new Workflow<CounterState>("single")
            .Job("only", (s, _) => Task.FromResult(s))
            .Then("only", Workflow.End)
            .Build();

        definition.EntryJob.ShouldBe("only");
        definition.Jobs.Count.ShouldBe(1);
    }

    [Test]
    public void Build_MultipleJobs_FirstIsEntry()
    {
        var definition = new Workflow<CounterState>("multi")
            .Job("first", (s, _) => Task.FromResult(s))
            .Job("second", (s, _) => Task.FromResult(s))
            .Then("first", "second")
            .Then("second", Workflow.End)
            .Build();

        definition.EntryJob.ShouldBe("first");
    }

    [Test]
    public void Build_JobWithNoOutgoingConnection_Throws()
    {
        var workflow = new Workflow<CounterState>("no-out")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Then("a", "b");

        Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public void Build_ValidLinearWorkflow_Succeeds()
    {
        var definition = new Workflow<CounterState>("linear")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("c", (s, _) => Task.FromResult(s))
            .Then("a", "b")
            .Then("b", "c")
            .Then("c", Workflow.End)
            .Build();

        definition.Name.ShouldBe("linear");
        definition.Jobs.Count.ShouldBe(3);
        definition.Connections.Count.ShouldBe(3);
    }

    [Test]
    public void Chain_WiresJobsInSequence()
    {
        var definition = new Workflow<CounterState>("chained")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("c", (s, _) => Task.FromResult(s))
            .Chain("a", "b", "c", Workflow.End)
            .Build();

        definition.Connections.Count.ShouldBe(3);
    }

    [Test]
    public void Chain_WithSingleJob_Throws()
    {
        var workflow = new Workflow<CounterState>("single-chain")
            .Job("a", (s, _) => Task.FromResult(s));

        Should.Throw<ArgumentException>(() => workflow.Chain("a"));
    }
}
