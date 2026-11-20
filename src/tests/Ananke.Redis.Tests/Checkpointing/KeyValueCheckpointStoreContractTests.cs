using Ananke.Abstractions.Distributed;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Redis.Tests.Checkpointing;

/// <summary>
/// The behaviour of <see cref="KeyValueCheckpointStore"/> over an <see cref="IKeyValueDataAdapter"/>. Each adapter
/// runs these tests through a fixture that derives from this class. The store's clock is a
/// <see cref="FakeTimeProvider"/> starting at the current time, apart from the adapter's own clock, so moving it
/// expires a checkpoint for the store while the adapter still holds it.
/// </summary>
public abstract class KeyValueCheckpointStoreContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string Prefix = "test:checkpoint";

    /// <summary>The adapter under test; one instance serves a whole fixture.</summary>
    protected abstract IKeyValueDataAdapter Adapter { get; }

    /// <summary>Whether the adapter holds <paramref name="key"/> with an expiry.</summary>
    protected abstract Task<bool> HasExpiryAsync(string key);

    private FakeTimeProvider _clock = null!;
    private KeyValueCheckpointStore _store = null!;

    [SetUp]
    public void NewStore()
    {
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _store = new KeyValueCheckpointStore(Adapter, Prefix, clock: _clock);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string KeyOf(string executionId) => $"{Prefix}:{executionId}";

    private Checkpoint<string> NewCheckpoint(string executionId, DateTimeOffset expiresAt) => new()
    {
        ExecutionId = executionId,
        WorkflowName = "workflow",
        CurrentJob = "job",
        State = "state",
        Status = ExecutionStatus.Running,
        History = [],
        CreatedAt = _clock.GetUtcNow(),
        ExpiresAt = expiresAt,
    };

    [Test]
    public async Task SaveAsync_ThenLoadAndExists_ReturnTheCheckpointWithItsExpiry()
    {
        var id = NewId();
        var expiresAt = _clock.GetUtcNow().AddMinutes(5);

        await _store.SaveAsync(NewCheckpoint(id, expiresAt)).WaitAsync(Timeout);

        (await _store.ExistsAsync(id).WaitAsync(Timeout)).ShouldBeTrue();
        var loaded = await _store.LoadAsync<string>(id).WaitAsync(Timeout);
        loaded.ShouldNotBeNull();
        loaded.ExecutionId.ShouldBe(id);
        loaded.State.ShouldBe("state");
        loaded.CurrentJob.ShouldBe("job");
        loaded.ExpiresAt.ShouldBe(expiresAt);
        (await HasExpiryAsync(KeyOf(id))).ShouldBeTrue();
    }

    [Test]
    public async Task DeleteAsync_SavedCheckpoint_LoadsAsNullAndDoesNotExist()
    {
        var id = NewId();
        await _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddMinutes(5))).WaitAsync(Timeout);

        await _store.DeleteAsync(id).WaitAsync(Timeout);

        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldBeNull();
        (await _store.ExistsAsync(id).WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Test]
    public async Task LoadAndExists_NoCheckpoint_ReturnNullAndFalse()
    {
        var id = NewId();

        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldBeNull();
        (await _store.ExistsAsync(id).WaitAsync(Timeout)).ShouldBeFalse();
    }

    [TestCase(-1)]
    [TestCase(0)]
    public async Task SaveAsync_AlreadyExpired_RemovesTheCheckpointRatherThanWritingIt(int secondsFromNow)
    {
        var id = NewId();
        await _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddMinutes(5))).WaitAsync(Timeout);

        await _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddSeconds(secondsFromNow))).WaitAsync(Timeout);

        (await Adapter.ExistsAsync(KeyOf(id)).WaitAsync(Timeout)).ShouldBeFalse();
        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldBeNull();
    }

    [Test]
    public async Task SaveAsync_ExpiresAtMaxValue_SavesWithNoExpiry()
    {
        var id = NewId();

        await _store.SaveAsync(NewCheckpoint(id, DateTimeOffset.MaxValue)).WaitAsync(Timeout);

        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldNotBeNull();
        (await HasExpiryAsync(KeyOf(id))).ShouldBeFalse();
    }

    [Test]
    public async Task LoadAndExists_ExpiredByTheStoresClock_ReturnNullAndFalse()
    {
        var id = NewId();
        await _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddMinutes(5))).WaitAsync(Timeout);

        _clock.Advance(TimeSpan.FromMinutes(5));

        (await Adapter.ExistsAsync(KeyOf(id)).WaitAsync(Timeout)).ShouldBeTrue();
        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldBeNull();
        (await _store.ExistsAsync(id).WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Test]
    public async Task LoadAndExists_ValueIsNotACheckpoint_ReturnNullAndFalse()
    {
        var id = NewId();
        await Adapter.SetValueAsync(KeyOf(id), "not json").WaitAsync(Timeout);

        (await _store.LoadAsync<string>(id).WaitAsync(Timeout)).ShouldBeNull();
        (await _store.ExistsAsync(id).WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Test]
    public async Task Operations_TokenAlreadyCancelled_ThrowOperationCanceledAndChangeNothing()
    {
        var id = NewId();
        await _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddMinutes(5))).WaitAsync(Timeout);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            _store.SaveAsync(NewCheckpoint(id, DateTimeOffset.MaxValue), cts.Token).WaitAsync(Timeout));
        await Should.ThrowAsync<OperationCanceledException>(
            _store.SaveAsync(NewCheckpoint(id, _clock.GetUtcNow().AddMinutes(-5)), cts.Token).WaitAsync(Timeout));
        await Should.ThrowAsync<OperationCanceledException>(
            _store.LoadAsync<string>(id, cts.Token).WaitAsync(Timeout));
        await Should.ThrowAsync<OperationCanceledException>(
            _store.DeleteAsync(id, cts.Token).WaitAsync(Timeout));
        await Should.ThrowAsync<OperationCanceledException>(
            _store.ExistsAsync(id, cts.Token).WaitAsync(Timeout));
        await Should.ThrowAsync<OperationCanceledException>(
            _store.CleanupExpiredAsync(cts.Token).WaitAsync(Timeout));

        (await HasExpiryAsync(KeyOf(id))).ShouldBeTrue();
    }
}
