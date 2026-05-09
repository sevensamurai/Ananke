using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class CheckpointTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _store = new InMemoryCheckpointStore();
    }

    [Test]
    public async Task RunAsync_WithCheckpointing_SavesAfterEachJob()
    {
        var execution = await new Workflow<CounterState>("checkpointed")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Job("c", (s, _) => Task.FromResult(s with { Value = 3 }))
            .Chain("a", "b", "c", Workflow.End)
            .UseCheckpointing(_store)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(3);

        // Checkpoint is cleaned up on successful completion
        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task RunAsync_WhenJobFails_CheckpointPersists()
    {
        var callCount = 0;

        var execution = await new Workflow<CounterState>("fail-checkpoint")
            .Job("ok", (s, _) => Task.FromResult(s with { Value = 1, Trail = [.. s.Trail, "ok"] }))
            .Job("boom", (s, _) =>
            {
                callCount++;
                throw new InvalidOperationException("fail");
#pragma warning disable CS0162 // Unreachable code detected
                return Task.FromResult(s);
#pragma warning restore CS0162
            })
            .Chain("ok", "boom", Workflow.End)
            .UseCheckpointing(_store)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);

        // Checkpoint persists after the successful "ok" job
        // (the "boom" job failed, so its checkpoint wasn't written)
        _store.Count.ShouldBe(1);

        var checkpoint = await _store.LoadAsync<CounterState>(execution.Id);
        checkpoint.ShouldNotBeNull();
        checkpoint.CurrentJob.ShouldBe("ok");
        checkpoint.State.Value.ShouldBe(1);
        checkpoint.State.Trail.ShouldBe(new[] { "ok" });
    }

    [Test]
    public async Task ResumeAsync_ContinuesFromCheckpoint()
    {
        var callCount = 0;

        var workflow = new Workflow<CounterState>("resumable")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1, Trail = [.. s.Trail, "a"] }))
            .Job("b", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient failure");
                return Task.FromResult(s with { Value = 2, Trail = [.. s.Trail, "b"] });
            })
            .Job("c", (s, _) => Task.FromResult(s with { Value = 3, Trail = [.. s.Trail, "c"] }))
            .Chain("a", "b", "c", Workflow.End)
            .UseCheckpointing(_store);

        // First run — fails at "b"
        var firstRun = await workflow.RunAsync(new CounterState());
        firstRun.Status.ShouldBe(ExecutionStatus.Faulted);
        _store.Count.ShouldBe(1);

        // Resume — "b" succeeds this time, then "c" runs
        var resumed = await workflow.ResumeAsync(firstRun.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Id.ShouldBe(firstRun.Id);
        resumed.Result!.FinalState.Value.ShouldBe(3);
        resumed.Result.FinalState.Trail.ShouldBe(new[] { "a", "b", "c" });

        // Checkpoint cleaned up on success
        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task ResumeAsync_PreservesHistory()
    {
        var callCount = 0;

        var workflow = new Workflow<CounterState>("history-resume")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("fail");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("a", "b", Workflow.End)
            .UseCheckpointing(_store);

        var firstRun = await workflow.RunAsync(new CounterState());
        firstRun.History.Count.ShouldBe(2); // "a" succeeded, "b" failed

        var resumed = await workflow.ResumeAsync(firstRun.Id);
        // History from checkpoint ("a" succeeded) + new runs ("b" succeeded)
        resumed.History.Count.ShouldBe(2);
        resumed.History[0].JobName.ShouldBe("a");
        resumed.History[0].Success.ShouldBeTrue();
        resumed.History[1].JobName.ShouldBe("b");
        resumed.History[1].Success.ShouldBeTrue();
    }

    [Test]
    public async Task ResumeAsync_WithoutCheckpointing_Throws()
    {
        var workflow = new Workflow<CounterState>("no-checkpoints")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End);

        await Should.ThrowAsync<InvalidOperationException>(
            () => workflow.ResumeAsync("nonexistent"));
    }

    [Test]
    public async Task ResumeAsync_WithMissingCheckpoint_Throws()
    {
        var workflow = new Workflow<CounterState>("missing")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End)
            .UseCheckpointing(_store);

        await Should.ThrowAsync<InvalidOperationException>(
            () => workflow.ResumeAsync("nonexistent-id"));
    }

    [Test]
    public async Task Checkpoint_SerializesFullStateAsJson()
    {
        var execution = await new Workflow<CounterState>("json-state")
            .Job("enrich", (s, _) => Task.FromResult(s with
            {
                Value = 42,
                Trail = ["step-a", "step-b"]
            }))
            .Job("boom", (_, _) => throw new InvalidOperationException("stop"))
            .Chain("enrich", "boom", Workflow.End)
            .UseCheckpointing(_store)
            .RunAsync(new CounterState());

        var checkpoint = await _store.LoadAsync<CounterState>(execution.Id);
        checkpoint.ShouldNotBeNull();
        checkpoint.State.Value.ShouldBe(42);
        checkpoint.State.Trail.ShouldBe(new[] { "step-a", "step-b" });
        checkpoint.WorkflowName.ShouldBe("json-state");
        checkpoint.ExecutionId.ShouldBe(execution.Id);
    }

    [Test]
    public async Task InMemoryCheckpointStore_ExistsAndDelete()
    {
        var execution = await new Workflow<CounterState>("store-ops")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("boom", (_, _) => throw new InvalidOperationException("stop"))
            .Chain("a", "boom", Workflow.End)
            .UseCheckpointing(_store)
            .RunAsync(new CounterState());

        (await _store.ExistsAsync(execution.Id)).ShouldBeTrue();

        await _store.DeleteAsync(execution.Id);

        (await _store.ExistsAsync(execution.Id)).ShouldBeFalse();
        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task Checkpoint_HasFutureExpiryByDefault()
    {
        var execution = await new Workflow<CounterState>("ttl-test")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("boom", (_, _) => throw new InvalidOperationException("stop"))
            .Chain("a", "boom", Workflow.End)
            .UseCheckpointing(_store)
            .RunAsync(new CounterState());

        var checkpoint = await _store.LoadAsync<CounterState>(execution.Id);
        checkpoint.ShouldNotBeNull();
        checkpoint.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task CleanupExpiredAsync_RemovesExpiredCheckpoints()
    {
        // Manually save a checkpoint that has already expired
        var checkpoint = new Checkpointing.Checkpoint<CounterState>
        {
            ExecutionId = "expired-id",
            WorkflowName = "test",
            CurrentJob = "a",
            State = new CounterState(),
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        await _store.SaveAsync(checkpoint);
        _store.Count.ShouldBe(1);

        await _store.CleanupExpiredAsync();

        _store.Count.ShouldBe(0);
    }

    [Test]
    public async Task LoadAsync_ExpiredCheckpoint_ReturnsNull()
    {
        var checkpoint = new Checkpointing.Checkpoint<CounterState>
        {
            ExecutionId = "soon-expired",
            WorkflowName = "test",
            CurrentJob = "a",
            State = new CounterState(),
            Status = ExecutionStatus.Running,
            History = [],
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        await _store.SaveAsync(checkpoint);

        var loaded = await _store.LoadAsync<CounterState>("soon-expired");

        loaded.ShouldBeNull();
        _store.Count.ShouldBe(0);
    }
}
