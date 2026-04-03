using System.Threading.Channels;

namespace LogEventsDemo;

/// <summary>
/// Generates synthetic structured log streams from all simulated services.
/// Produces a mix of normal operations, stochastic transient errors, and
/// scripted failure cascades. Output is written to a <see cref="Channel{T}"/>.
/// </summary>
internal sealed class LogSimulator
{
    private readonly Channel<LogEvent> _channel;
    private readonly Random _rng = new();
    private readonly List<LogEvent> _history = [];
    private readonly Lock _historyLock = new();

    /// <summary>Simulated clock — advances faster than real time.</summary>
    private DateTimeOffset _clock;

    /// <summary>All log events generated so far, in chronological order.</summary>
    internal IReadOnlyList<LogEvent> History
    {
        get
        {
            lock (_historyLock) return _history.ToList();
        }
    }

    /// <summary>Current simulated time.</summary>
    internal DateTimeOffset CurrentTime => _clock;

    internal ChannelReader<LogEvent> Reader => _channel.Reader;

    internal LogSimulator(DateTimeOffset? startTime = null)
    {
        _channel = Channel.CreateUnbounded<LogEvent>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
        _clock = startTime ?? new DateTimeOffset(2025, 7, 14, 8, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Runs a simulation producing <paramref name="ticks"/> log ticks.
    /// Each tick advances the simulated clock by 1–5 seconds and produces
    /// log events from all services. Failure scenarios fire stochastically.
    /// </summary>
    internal async Task RunAsync(int ticks = 200, CancellationToken ct = default)
    {
        for (var t = 0; t < ticks && !ct.IsCancellationRequested; t++)
        {
            // Advance clock by 1–5 simulated seconds per tick
            _clock = _clock.AddSeconds(_rng.Next(1, 6));

            // Normal operations from each service
            foreach (var svc in SystemTopology.Services)
            {
                await EmitNormalLogAsync(svc);

                // Stochastic transient errors
                if (_rng.NextSingle() < svc.BaseErrorRate)
                    await EmitTransientErrorAsync(svc);
            }

            // Failure scenario triggers
            foreach (var scenario in FailureScenarios.All)
            {
                if (_rng.NextSingle() < scenario.TriggerProbability)
                    await EmitCascadeAsync(scenario);
            }
        }

        _channel.Writer.Complete();
    }

    private async Task EmitNormalLogAsync(ServiceDefinition svc)
    {
        var msg = svc.NormalMessages[_rng.Next(svc.NormalMessages.Count)];
        var evt = new LogEvent
        {
            Timestamp = _clock,
            Service = svc.Name,
            Level = LogLevel.Info,
            Message = msg,
            CorrelationId = Guid.NewGuid().ToString("N")[..12]
        };
        await WriteEventAsync(evt);
    }

    private async Task EmitTransientErrorAsync(ServiceDefinition svc)
    {
        var msg = svc.TransientErrorMessages[_rng.Next(svc.TransientErrorMessages.Count)];
        var fields = new Dictionary<string, string> { ["transient"] = "true" };

        // Pull infra dependency into fields if present
        if (svc.InfraDependencies.Count > 0)
            fields["infra"] = svc.InfraDependencies[_rng.Next(svc.InfraDependencies.Count)];

        var evt = new LogEvent
        {
            Timestamp = _clock,
            Service = svc.Name,
            Level = LogLevel.Error,
            Message = msg,
            Fields = fields,
            CorrelationId = Guid.NewGuid().ToString("N")[..12]
        };
        await WriteEventAsync(evt);
    }

    private async Task EmitCascadeAsync(FailureScenario scenario)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var baseTime = _clock;

        foreach (var stage in scenario.Stages)
        {
            var stageTime = baseTime + stage.Delay;
            var fields = new Dictionary<string, string>(stage.Fields)
            {
                ["scenario"] = scenario.Name,
                ["cause"] = scenario.CauseTag
            };
            if (scenario.InfraTag is not null)
                fields["infra"] = scenario.InfraTag;

            for (var i = 0; i < stage.EventCount; i++)
            {
                var msg = stage.Messages[i % stage.Messages.Count];
                var evt = new LogEvent
                {
                    Timestamp = stageTime.AddMilliseconds(i * 50),
                    Service = stage.Service,
                    Level = stage.Level,
                    Message = msg,
                    Fields = fields,
                    CorrelationId = correlationId,
                    SpanId = Guid.NewGuid().ToString("N")[..8]
                };
                await WriteEventAsync(evt);
            }

            // Advance the global clock past the cascade stage
            if (stageTime > _clock)
                _clock = stageTime;
        }
    }

    private async Task WriteEventAsync(LogEvent evt)
    {
        lock (_historyLock) _history.Add(evt);
        await _channel.Writer.WriteAsync(evt);
    }
}
