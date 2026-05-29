using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ananke.Abstractions.Agents;

namespace MiniAgencyDemo;

internal sealed class MiniAgencyBudgetMetrics : IDisposable
{
    private readonly Meter? _meter;
    private readonly Counter<long>? _tokensIn;
    private readonly Counter<long>? _tokensOut;
    private readonly Counter<double>? _usd;

    public MiniAgencyBudgetMetrics(bool enabled)
    {
        if (!enabled)
            return;

        _meter = new Meter("Ananke.Federation", "1.0.0");
        _tokensIn = _meter.CreateCounter<long>("ananke.federation.tokens.in");
        _tokensOut = _meter.CreateCounter<long>("ananke.federation.tokens.out");
        _usd = _meter.CreateCounter<double>("ananke.federation.usd");
    }

    public void Record(string workflowName, TokenUsage usage, decimal estimatedUsd = 0m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(usage);

        if (_meter is null)
            return;

        var tags = new TagList
        {
            { "workflow", workflowName },
            { "role", workflowName }
        };

        _tokensIn!.Add(usage.InputTokens, tags);
        _tokensOut!.Add(usage.OutputTokens, tags);
        _usd!.Add(decimal.ToDouble(estimatedUsd), tags);
    }

    public void Dispose() => _meter?.Dispose();
}
