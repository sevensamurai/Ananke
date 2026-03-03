using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.StateMachine.Applications;

/// <summary>
/// Simple base class for hosted services that need continuous background execution.
/// Use this only when you have long-running async work (like subscriptions).
/// For startup-only services, implement IHostedService directly.
/// </summary>
public abstract class HostedServiceBase(ILogger? logger = null) : BackgroundService
{
    private readonly ILogger Log = logger ?? NullLogger.Instance;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.LogInformation("{Service} starting...", GetType().Name);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.LogInformation("{Service} stopping...", GetType().Name);
        await base.StopAsync(cancellationToken);
    }

    protected override abstract Task ExecuteAsync(CancellationToken stoppingToken);
}
