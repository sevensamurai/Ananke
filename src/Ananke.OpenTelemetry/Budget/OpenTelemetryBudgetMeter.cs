using System.Diagnostics.Metrics;
using Ananke.Abstractions.Budget;

namespace Ananke.OpenTelemetry.Budget;

/// <summary>
/// <see cref="IBudgetMeter"/> implementation that listens to federation budget counters
/// and aggregates them with a rolling time window.
/// </summary>
public sealed class OpenTelemetryBudgetMeter : IBudgetMeter, IDisposable
{
    private static readonly HashSet<string> SupportedInstrumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ananke.federation.tokens.in",
        "ananke.federation.tokens.out",
        "ananke.federation.usd"
    };

    private static readonly string[] SupportedRoleTagNames =
    [
        "role",
        "role_name",
        "workflow",
        "workflow_name"
    ];

    private readonly BudgetMeterOptions _options;
    private readonly InMemoryBudgetMeter _inner;
    private readonly MeterListener _listener = new();

    /// <summary>
    /// Creates a budget meter backed by OpenTelemetry counters.
    /// </summary>
    public OpenTelemetryBudgetMeter(
        BudgetMeterOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = options ?? new BudgetMeterOptions();
        _inner = new InMemoryBudgetMeter(_options.TimeWindow, clock);

        _listener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == Sources.Federation &&
                SupportedInstrumentNames.Contains(instrument.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<byte>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<short>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<float>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<decimal>(OnMeasurementRecorded);
        _listener.Start();
    }

    /// <summary>
    /// Gets the configured cap for the supplied workflow or role key.
    /// </summary>
    public long GetConfiguredCap(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (_options.PerRoleCaps.TryGetValue(role, out var cap))
            return cap;

        return _options.DefaultTokenCap;
    }

    /// <summary>
    /// Determines whether the current total token usage meets or exceeds the configured cap.
    /// </summary>
    public bool IsOverCap(string role)
    {
        var cap = GetConfiguredCap(role);
        return cap > 0 && _inner.IsOverCap(role, cap);
    }

    /// <inheritdoc />
    public BudgetSpend GetCurrentSpend(string role) => _inner.GetCurrentSpend(role);

    /// <inheritdoc />
    public bool IsOverCap(string role, long cap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cap);

        return cap == 0 ? IsOverCap(role) : _inner.IsOverCap(role, cap);
    }

    private void OnMeasurementRecorded<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        if (!TryGetRole(tags, out var role))
            return;

        switch (instrument.Name)
        {
            case "ananke.federation.tokens.in":
                Record(role, tokensIn: ToLong(measurement), tokensOut: 0, estimatedUsd: 0m);
                break;
            case "ananke.federation.tokens.out":
                Record(role, tokensIn: 0, tokensOut: ToLong(measurement), estimatedUsd: 0m);
                break;
            case "ananke.federation.usd":
                Record(role, tokensIn: 0, tokensOut: 0, estimatedUsd: ToDecimal(measurement));
                break;
        }
    }

    private void Record(string role, long tokensIn, long tokensOut, decimal estimatedUsd)
    {
        if (tokensIn < 0 || tokensOut < 0 || estimatedUsd < 0)
            return;

        _inner.Record(role, tokensIn, tokensOut, estimatedUsd);
    }

    private static bool TryGetRole(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        out string role)
    {
        foreach (var tagName in SupportedRoleTagNames)
        {
            for (var i = 0; i < tags.Length; i++)
            {
                var entry = tags[i];
                if (!string.Equals(entry.Key, tagName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = entry.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    role = candidate;
                    return true;
                }
            }
        }

        role = string.Empty;
        return false;
    }

    private static long ToLong<T>(T measurement)
        where T : struct
    {
        return measurement switch
        {
            byte value => value,
            short value => value,
            int value => value,
            long value => value,
            float value => checked((long)value),
            double value => checked((long)value),
            decimal value => checked((long)value),
            _ => throw new InvalidOperationException($"Unsupported measurement type '{typeof(T)}'.")
        };
    }

    private static decimal ToDecimal<T>(T measurement)
        where T : struct
    {
        return measurement switch
        {
            byte value => value,
            short value => value,
            int value => value,
            long value => value,
            float value => (decimal)value,
            double value => (decimal)value,
            decimal value => value,
            _ => throw new InvalidOperationException($"Unsupported measurement type '{typeof(T)}'.")
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener.Dispose();
    }
}
