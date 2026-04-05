using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class JobRefTests
{
    [Test]
    public void JobRef_ImplicitConversion_ReturnsName()
    {
        new Workflow<CounterState>("conv")
            .Job("test", (s, _) => Task.FromResult(s), out var jobRef);

        string name = jobRef;
        name.ShouldBe("test");
    }

    [Test]
    public void JobRef_ToString_ReturnsName()
    {
        new Workflow<CounterState>("str")
            .Job("test", (s, _) => Task.FromResult(s), out var jobRef);

        jobRef.ToString().ShouldBe("test");
    }

    [Test]
    public void EndRef_MatchesEnd()
    {
        string endRef = Workflow.EndRef;
        endRef.ShouldBe(Workflow.End);
    }

    [Test]
    public void Job_Delegate_OutRef_ReturnsSameName()
    {
        var workflow = new Workflow<CounterState>("ref-test")
            .Job("alpha", (s, _) => Task.FromResult(s), out var alpha);

        alpha.Name.ShouldBe("alpha");
    }

    [Test]
    public void Job_IJob_OutRef_ReturnsSameName()
    {
        var job = new StubJob("beta");
        var workflow = new Workflow<CounterState>("ref-test")
            .Job("beta", job, out var beta);

        beta.Name.ShouldBe("beta");
    }

    [Test]
    public void Then_WithJobRefs_BuildsValidWorkflow()
    {
        var definition = new Workflow<CounterState>("ref-then")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .Job("b", (s, _) => Task.FromResult(s), out var b)
            .Then(a, b)
            .Then(b, Workflow.EndRef)
            .Build();

        definition.EntryJob.ShouldBe("a");
        definition.Jobs.Count.ShouldBe(2);
        definition.Connections.Count.ShouldBe(2);
    }

    [Test]
    public async Task Then_WithJobRefs_ExecutesCorrectly()
    {
        var exec = await new Workflow<CounterState>("ref-exec")
            .Job("inc", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }), out var inc)
            .Job("double", (s, _) => Task.FromResult(s with { Value = s.Value * 2 }), out var dbl)
            .Then(inc, dbl)
            .Then(dbl, Workflow.EndRef)
            .RunAsync(new CounterState { Value = 3 });

        exec.State.Value.ShouldBe(8); // (3 + 1) * 2
    }

    [Test]
    public void Chain_WithJobRefs_BuildsValidWorkflow()
    {
        var definition = new Workflow<CounterState>("ref-chain")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .Job("b", (s, _) => Task.FromResult(s), out var b)
            .Job("c", (s, _) => Task.FromResult(s), out var c)
            .Chain(a, b, c, Workflow.EndRef)
            .Build();

        definition.Connections.Count.ShouldBe(3);
    }

    [Test]
    public async Task Chain_WithJobRefs_ExecutesInOrder()
    {
        var exec = await new Workflow<CounterState>("ref-chain-exec")
            .Job("a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"] }), out var a)
            .Job("b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }), out var b)
            .Job("c", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "c"] }), out var c)
            .Chain(a, b, c, Workflow.EndRef)
            .RunAsync(new CounterState());

        exec.State.Trail.ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void Loop_WithJobRefs_BuildsValidWorkflow()
    {
        var definition = new Workflow<CounterState>("ref-loop")
            .Job("inc", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }), out var inc)
            .Loop(inc, inc, Workflow.EndRef, s => s.Value >= 3, maxIterations: 5)
            .Build();

        definition.EntryJob.ShouldBe("inc");
    }

    [Test]
    public async Task Loop_WithJobRefs_ExecutesCorrectly()
    {
        var exec = await new Workflow<CounterState>("ref-loop-exec")
            .Job("inc", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }), out var inc)
            .Loop(inc, inc, Workflow.EndRef, s => s.Value >= 3, maxIterations: 10)
            .RunAsync(new CounterState { Value = 0 });

        exec.State.Value.ShouldBe(3);
    }

    [Test]
    public void Then_Router_WithJobRef_BuildsValidWorkflow()
    {
        var definition = new Workflow<CounterState>("ref-router")
            .Job("start", (s, _) => Task.FromResult(s), out var start)
            .Job("pathA", (s, _) => Task.FromResult(s), out var pathA)
            .Job("pathB", (s, _) => Task.FromResult(s), out var pathB)
            .Then(start, Workflow.Decide<CounterState>(s => s.Value > 0 ? "pathA" : "pathB"))
            .Then(pathA, Workflow.EndRef)
            .Then(pathB, Workflow.EndRef)
            .Build();

        definition.Connections.Count.ShouldBe(3);
    }

    [Test]
    public void Fork_WithJobRefs_BuildsValidWorkflow()
    {
        var definition = new Workflow<CounterState>("ref-fork")
            .Job("start", (s, _) => Task.FromResult(s), out var start)
            .Job("branchA", (s, _) => Task.FromResult(s), out var branchA)
            .Job("branchB", (s, _) => Task.FromResult(s), out var branchB)
            .Job("merge", (s, _) => Task.FromResult(s), out var merge)
            .Then(start, Workflow.Fork(branchA, branchB))
            .Join([branchA, branchB], merge, states => states[0])
            .Then(merge, Workflow.EndRef)
            .Build();

        definition.Joins.Count.ShouldBe(1);
    }

    [Test]
    public void OnEnter_WithJobRef_RegistersAction()
    {
        var definition = new Workflow<CounterState>("ref-enter")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .OnEnter(a, _ => Task.CompletedTask)
            .Then(a, Workflow.EndRef)
            .Build();

        definition.Jobs["a"].OnEnter.ShouldNotBeNull();
    }

    [Test]
    public void OnExit_WithJobRef_RegistersAction()
    {
        var definition = new Workflow<CounterState>("ref-exit")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .OnExit(a, _ => Task.CompletedTask)
            .Then(a, Workflow.EndRef)
            .Build();

        definition.Jobs["a"].OnExit.ShouldNotBeNull();
    }

    [Test]
    public void Timeout_WithJobRef_RegistersTimeout()
    {
        var definition = new Workflow<CounterState>("ref-timeout")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .Timeout(a, TimeSpan.FromSeconds(30))
            .Then(a, Workflow.EndRef)
            .Build();

        definition.Jobs["a"].Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void InterruptBefore_WithJobRef_RegistersInterrupt()
    {
        var definition = new Workflow<CounterState>("ref-interrupt-before")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .Job("b", (s, _) => Task.FromResult(s), out var b)
            .InterruptBefore(b)
            .Then(a, b)
            .Then(b, Workflow.EndRef)
            .Build();

        definition.Jobs["b"].Interrupt.ShouldBe(Jobs.InterruptMode.Before);
    }

    [Test]
    public void InterruptAfter_WithJobRef_RegistersInterrupt()
    {
        var definition = new Workflow<CounterState>("ref-interrupt-after")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .InterruptAfter(a)
            .Then(a, Workflow.EndRef)
            .Build();

        definition.Jobs["a"].Interrupt.ShouldBe(Jobs.InterruptMode.After);
    }

    [Test]
    public void SubFlow_OutRef_ReturnsJobRef()
    {
        var inner = new Workflow<string>("inner")
            .Job("child", (s, _) => Task.FromResult(s + "!"))
            .Then("child", Workflow.End);

        var definition = new Workflow<CounterState>("ref-subflow")
            .Job("before", (s, _) => Task.FromResult(s), out var before)
            .SubFlow("nested", inner, s => s.Value.ToString(), (s, r) => s, out var nested)
            .Then(before, nested)
            .Then(nested, Workflow.EndRef)
            .Build();

        nested.Name.ShouldBe("nested");
        definition.Jobs.ContainsKey("nested").ShouldBeTrue();
    }

    [Test]
    public void MixedStringAndJobRef_WorksTogether()
    {
        // Demonstrates that string-based and JobRef-based APIs interop
        var definition = new Workflow<CounterState>("mixed")
            .Job("a", (s, _) => Task.FromResult(s), out var a)
            .Job("b", (s, _) => Task.FromResult(s))
            .Then(a, "b")  // JobRef → string implicit conversion
            .Then("b", Workflow.End)
            .Build();

        definition.Connections.Count.ShouldBe(2);
    }

    private sealed class StubJob(string name) : Jobs.IJob<CounterState>
    {
        public string Name => name;
        public Task<CounterState> ExecuteAsync(CounterState state, CancellationToken ct = default) =>
            Task.FromResult(state);
    }
}
