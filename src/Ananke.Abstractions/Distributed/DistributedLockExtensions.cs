namespace Ananke.Abstractions.Distributed;

public static class DistributedLockExtensions
{
    /// <summary>
    /// Executes an action, retrying while the lock is held elsewhere. Only taking the lock is retried:
    /// the action runs at most once, and its exception is returned once in
    /// <see cref="CoordinatedActionResult{R}.Failed"/>. When the lock is still held after
    /// <paramref name="maxRetries"/> retries, returns the last <see cref="CoordinatedActionResult{R}.LockFailed"/>.
    /// </summary>
    /// <param name="locker">the lock each attempt goes through, with <see cref="IDistributedLock.RunCoordinatedActionAsync{R}"/></param>
    /// <param name="resourceId">unique resource id</param>
    /// <param name="action">operation to execute</param>
    /// <param name="maxRetries">attempts to take the lock after the first one</param>
    /// <param name="retryDelayMs">
    /// delay before the first retry in milliseconds; it doubles before each further retry, up to 1024 times this value
    /// </param>
    /// <param name="ct">
    /// Cancels taking the lock or the delay between attempts: throws <see cref="OperationCanceledException"/>.
    /// </param>
    /// <param name="clock">Time source for the delays. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <returns>Result indicating if lock was acquired and action succeeded</returns>
    public static async Task<CoordinatedActionResult<R>> RunCoordinatedActionWithRetryAsync<R>(
        this IDistributedLock locker,
        string resourceId,
        Func<Task<R>> action,
        int maxRetries = 3,
        int retryDelayMs = 100,
        CancellationToken ct = default,
        TimeProvider? clock = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await locker.RunCoordinatedActionAsync(resourceId, action, ct).ConfigureAwait(false);
            if (result.LockAcquired || attempt >= maxRetries)
                return result;
            var delayMs = Math.Min((long)Math.Max(retryDelayMs, 0) << Math.Min(attempt, 10), int.MaxValue);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), clock ?? TimeProvider.System, ct).ConfigureAwait(false);
        }
    }
}
