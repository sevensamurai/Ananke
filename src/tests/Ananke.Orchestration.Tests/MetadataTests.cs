using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class MetadataTests
{
    [Test]
    public async Task WithMetadata_FlowsIntoExecution()
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenant"] = "acme",
            ["correlation_id"] = "abc-123"
        };

        var execution = await new Workflow<CounterState>("with-meta")
            .Job("work", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Then("work", Workflow.End)
            .WithMetadata(metadata)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Metadata.ShouldContainKeyAndValue("tenant", "acme");
        execution.Metadata.ShouldContainKeyAndValue("correlation_id", "abc-123");
    }

    [Test]
    public async Task WithMetadata_FlowsIntoResult()
    {
        var metadata = new Dictionary<string, string> { ["env"] = "test" };

        var execution = await new Workflow<CounterState>("meta-result")
            .Job("noop", (s, _) => Task.FromResult(s))
            .Then("noop", Workflow.End)
            .WithMetadata(metadata)
            .RunAsync(new CounterState());

        execution.Metadata["env"].ShouldBe("test");
    }

    [Test]
    public async Task WithoutMetadata_MetadataIsEmpty()
    {
        var execution = await new Workflow<CounterState>("no-meta")
            .Job("noop", (s, _) => Task.FromResult(s))
            .Then("noop", Workflow.End)
            .RunAsync(new CounterState());

        execution.Metadata.ShouldBeEmpty();
    }

    [Test]
    public async Task Build_MetadataFlowsIntoDefinition()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var definition = new Workflow<CounterState>("def-meta")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End)
            .WithMetadata(metadata)
            .Build();

        definition.Metadata.ShouldContainKeyAndValue("key", "value");
    }
}
