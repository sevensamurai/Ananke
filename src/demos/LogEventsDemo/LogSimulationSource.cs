using Ananke.Learning;
using Ananke.Learning.Offline;

namespace LogEventsDemo;

/// <summary>
/// <see cref="ISimulationSource"/> for the log events domain. Replays log
/// time windows through the simulator and evaluates whether a Pattern entry's
/// condition→effect pair manifests. Returns a reward signal for intrinsic
/// reinforcement by the offline learner.
/// </summary>
/// <remarks>
/// This is the second <see cref="ISimulationSource"/> implementation in the
/// Ananke demos (after Connect4Demo's <c>Connect4SimulationSource</c>),
/// validating the interface against a non-game operational domain.
/// </remarks>
internal sealed class LogSimulationSource : ISimulationSource
{
    private readonly Random _rng = new();

    /// <inheritdoc />
    public Task<SimulationOutcome> SimulateAsync(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge,
        int maxEpisodes,
        CancellationToken ct = default)
    {
        // Each "episode" is a simulated log window.
        // We check if the hypothesis condition→effect pair appears.
        var supported = 0;

        for (var ep = 0; ep < maxEpisodes && !ct.IsCancellationRequested; ep++)
        {
            if (SimulateOneWindow(hypothesis, relatedKnowledge))
                supported++;
        }

        var supportRate = (float)supported / maxEpisodes;

        // Reward: map support rate to [-1, 1]
        // If a pattern appears in >60% of windows, it's strongly supported
        // If <30%, it's weakly supported or contradicted
        var reward = Math.Clamp((supportRate - 0.4f) * 2.5f, -1f, 1f);

        var summary = $"{supported}/{maxEpisodes} windows supported hypothesis: "
            + Truncate(hypothesis.Description.ToString(), 60);

        return Task.FromResult(new SimulationOutcome
        {
            Reward = reward,
            Summary = summary,
            EpisodesRun = maxEpisodes,
            EpisodesSupported = supported
        });
    }

    /// <summary>
    /// Simulates a single log window and checks if the hypothesis manifests.
    /// Uses the hypothesis's semantic tags to determine what to look for.
    /// </summary>
    private bool SimulateOneWindow(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge)
    {
        var tags = hypothesis.Description.SemanticTags;

        // Determine what scenario this hypothesis relates to
        var scenarioName = tags.Keys
            .Where(k => k.StartsWith("scenario:"))
            .Select(k => k["scenario:".Length..])
            .FirstOrDefault();

        var causeTag = tags.Keys
            .Where(k => k.StartsWith("cause:"))
            .Select(k => k["cause:".Length..])
            .FirstOrDefault();

        var infraTag = tags.Keys
            .Where(k => k.StartsWith("infra:"))
            .Select(k => k["infra:".Length..])
            .FirstOrDefault();

        // Find matching failure scenario
        var matchingScenario = FailureScenarios.All.FirstOrDefault(s =>
            (causeTag is not null && s.CauseTag.Equals(causeTag, StringComparison.OrdinalIgnoreCase))
            || (infraTag is not null && s.InfraTag?.Equals(infraTag, StringComparison.OrdinalIgnoreCase) == true));

        if (matchingScenario is null)
        {
            // No known scenario maps to this hypothesis — low support
            // Still give it a small random chance to model uncertainty
            return _rng.NextSingle() < 0.15f;
        }

        // The scenario exists — simulate whether it triggers
        // Use the scenario's trigger probability boosted by related knowledge
        var triggerProb = matchingScenario.TriggerProbability;

        // Boost probability if related knowledge corroborates
        var corroboration = relatedKnowledge
            .Where(m => m.Entry.Id != hypothesis.Id && m.Entry.Confidence > 0.5f)
            .Sum(m => m.Score * 0.1f);

        triggerProb = Math.Clamp(triggerProb + corroboration, 0f, 0.9f);

        if (_rng.NextSingle() >= triggerProb)
            return false; // Scenario didn't trigger in this window

        // Scenario triggered — check if the full cascade manifests
        // (i.e., all stages fire, confirming the condition→effect)
        if (hypothesis.Kind == EmpiricalKind.Pattern)
        {
            // For cascades: check that at least 2 stages would fire
            var stagesFired = matchingScenario.Stages.Count(s =>
                _rng.NextSingle() < 0.8f); // 80% per-stage success

            return stagesFired >= Math.Min(2, matchingScenario.Stages.Count);
        }

        // For non-pattern entries (heuristics, skills), just check trigger
        return true;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : string.Concat(text.AsSpan(0, maxLen - 3), "...");
}
