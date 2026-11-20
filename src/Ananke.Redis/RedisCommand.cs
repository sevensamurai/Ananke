namespace Ananke.Redis;

/// <summary>
/// Waiting on StackExchange.Redis commands, which take no <see cref="CancellationToken"/>.
/// </summary>
internal static class RedisCommand
{
    /// <summary>
    /// Waits for <paramref name="command"/> until <paramref name="ct"/> is cancelled. A command no longer waited on
    /// keeps running; its failure, if any, is observed so it is not raised as an unobserved task exception.
    /// </summary>
    public static async Task<T> WaitForAsync<T>(this Task<T> command, CancellationToken ct)
    {
        try
        {
            return await command.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested && !command.IsCompleted)
        {
            Observe(command);
            throw;
        }
    }

    /// <summary>Observes a failure of <paramref name="command"/> that nothing else awaits.</summary>
    public static void Observe(Task command) =>
        command.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
