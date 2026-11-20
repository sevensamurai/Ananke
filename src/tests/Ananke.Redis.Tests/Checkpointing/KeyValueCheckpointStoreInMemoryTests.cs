using Ananke.Abstractions.Distributed;
using Ananke.Orchestration.Checkpointing;
using Microsoft.Extensions.Time.Testing;

namespace Ananke.Redis.Tests.Checkpointing;

/// <summary>
/// The <see cref="KeyValueCheckpointStore"/> contract over <see cref="InMemoryDistributedLock"/>, with a clock of its
/// own.
/// </summary>
[TestFixture]
public sealed class KeyValueCheckpointStoreInMemoryTests : KeyValueCheckpointStoreContractTests
{
    private readonly FakeTimeProvider _adapterClock = new(DateTimeOffset.UtcNow);
    private InMemoryDistributedLock? _adapter;

    protected override IKeyValueDataAdapter Adapter => _adapter ??= new InMemoryDistributedLock(_adapterClock);

    [OneTimeTearDown]
    public async Task DisposeAsync()
    {
        if (_adapter is not null)
            await _adapter.DisposeAsync();
    }

    /// <summary>
    /// Moves the adapter's clock a century on: a key with an expiry is gone, one without is still there. Later
    /// writes take their expiry from the clock as it then is.
    /// </summary>
    protected override async Task<bool> HasExpiryAsync(string key)
    {
        if (!await Adapter.ExistsAsync(key))
            return false;
        _adapterClock.Advance(TimeSpan.FromDays(36_500));
        return !await Adapter.ExistsAsync(key);
    }
}
