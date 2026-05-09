using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Shouldly;

namespace Ananke.Integration.Tests;

/// <summary>
/// Integration tests verifying that <see cref="InMemoryEmpiricalMemory"/> learns
/// meaningful patterns from Connect4-like game data. Simulates the commit/reinforce
/// cycle that the Connect4Demo's <c>GameAnalyzer</c> performs, using raw structural
/// semantic tags (no game-specific strategy encoded).
/// </summary>
[TestFixture]
public class Connect4LearningTests
{
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        // Disable reinforcement cooldown so back-to-back reinforcements
        // in tests take full effect (cooldown is tested separately).
        _memory = new InMemoryEmpiricalMemory(
            new InMemoryEmbedder(),
            dedupThreshold: 0.9f,
            affectOptions: new AffectOptions { ReinforcementCooldownHours = 0.0001f });
    }

    // ── Dedup across games ───────────────────────────────────────

    [Test]
    public async Task Same_board_position_across_games_merges_via_dedup()
    {
        // Game 1: opening position, agent plays center
        var game1 = MakeObservation("obs_g1_m0", gameNumber: 1, col: 3,
            summary: "opening c3h0 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:opening"] = 0.4f,
                ["action:col_3"] = 0.9f
            });

        var first = await _memory.CommitAsync(game1);
        first.ObservationCount.ShouldBe(1);

        // Game 2: same opening position, same move
        var game2 = MakeObservation("obs_g2_m0", gameNumber: 2, col: 3,
            summary: "opening c3h0 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:opening"] = 0.4f,
                ["action:col_3"] = 0.9f
            });

        var merged = await _memory.CommitAsync(game2);

        // Should merge into the same entry — identical embedding text
        merged.Id.ShouldBe("obs_g1_m0");
        merged.ObservationCount.ShouldBe(2);
        _memory.Count.ShouldBe(1);
    }

    // ── Reinforcement direction ──────────────────────────────────

    [Test]
    public async Task Positive_reward_increases_confidence_across_games()
    {
        var entry = MakeObservation("mid_center", gameNumber: 1, col: 3,
            summary: "midgame c3h3 c0h2 c6h1 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.5f,
                ["line:a2"] = 0.4f
            });

        await _memory.CommitAsync(entry);
        var before = (await _memory.GetAsync("mid_center"))!;
        before.Confidence.ShouldBe(0.3f);

        // Agent won — reinforce with positive reward
        await _memory.ReinforceAsync("mid_center", new Reinforcement
        {
            NewEvidence = ["game 2: similar position, agent won"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        var after = (await _memory.GetAsync("mid_center"))!;
        after.Confidence.ShouldBeGreaterThan(before.Confidence);
        after.Valence.ShouldBeGreaterThan(0f, "Positive reward should produce positive valence");
        after.ObservationCount.ShouldBe(2);
    }

    [Test]
    public async Task Negative_reward_produces_negative_valence()
    {
        var entry = MakeObservation("edge_play", gameNumber: 1, col: 0,
            summary: "midgame c0h2 c3h1 played_c0",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_0"] = 0.9f,
                ["line:e2"] = 0.4f
            });

        await _memory.CommitAsync(entry);

        // Agent lost — reinforce with negative reward
        await _memory.ReinforceAsync("edge_play", new Reinforcement
        {
            NewEvidence = ["game 2: similar position, agent lost"],
            Source = "game-analysis",
            Reward = -1.0f
        });

        var updated = (await _memory.GetAsync("edge_play"))!;
        updated.Valence.ShouldBeLessThan(0f, "Negative reward should produce negative valence");
        updated.Intensity.ShouldBeGreaterThan(0f, "Large prediction error should produce high intensity");
    }

    // ── Tag overlap distinguishes positions ───────────────────────

    [Test]
    public async Task Tag_overlap_distinguishes_positions_with_similar_text()
    {
        // Two midgame positions with similar text but different structural tags
        var centerStrong = MakeObservation("pos_center", gameNumber: 1, col: 3,
            summary: "midgame c3h3 c2h2 c4h2 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.5f,
                ["line:a2"] = 0.6f
            });

        var edgeFocused = MakeObservation("pos_edge", gameNumber: 2, col: 0,
            summary: "midgame c0h3 c1h2 c6h2 played_c0",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_0"] = 0.9f,
                ["line:e2"] = 0.6f
            });

        await _memory.CommitAsync(centerStrong);
        await _memory.CommitAsync(edgeFocused);

        // Build a query description that structurally matches the center position
        var queryDescription = new SemanticDescription
        {
            Summary = "midgame c3h4 c2h3 played_c3",
            SemanticTags = new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.67f,
                ["line:a2"] = 0.4f
            }
        };

        // Tag overlap with center position should be much higher
        var centerOverlap = queryDescription.TagOverlap(centerStrong.Description);
        var edgeOverlap = queryDescription.TagOverlap(edgeFocused.Description);

        centerOverlap.ShouldBeGreaterThan(edgeOverlap,
            "Query with center:agent and line:a2 should overlap more with the center-focused position");
    }

    // ── Recall improves after reinforcement ──────────────────────

    [Test]
    public async Task Recall_score_improves_after_positive_reinforcement()
    {
        var entry = MakeObservation("learnable", gameNumber: 1, col: 3,
            summary: "midgame c3h2 c2h1 h_a2f2 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["line:a2"] = 0.4f
            });

        await _memory.CommitAsync(entry);

        // First recall — low confidence
        var firstRecall = await _memory.RecallAsync("midgame c3h2 h_a2f2");
        firstRecall.ShouldNotBeEmpty();
        var firstScore = firstRecall[0].Score;

        // Reinforce positively (agent won from this position)
        await _memory.ReinforceAsync("learnable", new Reinforcement
        {
            NewEvidence = ["game 2: agent won from similar position"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        // Second recall — same query, higher score
        var secondRecall = await _memory.RecallAsync("midgame c3h2 h_a2f2");
        secondRecall.ShouldNotBeEmpty();
        secondRecall[0].Score.ShouldBeGreaterThan(firstScore,
            "Recall score should increase after positive reinforcement");
    }

    // ── Cross-game learning lifecycle ────────────────────────────

    [Test]
    public async Task Three_game_lifecycle_shows_progressive_learning()
    {
        // === Game 1: Agent plays center, wins ===
        var g1Move = MakeObservation("g1_m4", gameNumber: 1, col: 3,
            summary: "midgame c3h2 c1h1 h_a1f3 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.33f,
                ["line:a1"] = 0.15f
            });
        await _memory.CommitAsync(g1Move);

        var g1Outcome = MakeOutcome("outcome_g1", gameNumber: 1, result: "win",
            summary: "endgame c3h4 c1h3 c5h2 h_a3f1 v_a2f2",
            tags: new Dictionary<string, float>
            {
                ["phase:endgame"] = 0.4f,
                ["outcome:win"] = 1.0f,
                ["center:agent"] = 0.67f,
                ["line:a3"] = 0.4f
            });
        await _memory.CommitAsync(g1Outcome);

        // Reinforce the move with the win reward
        await _memory.ReinforceAsync("g1_m4", new Reinforcement
        {
            NewEvidence = ["game 1: agent won, center was strong"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        var afterGame1 = (await _memory.GetAsync("g1_m4"))!;

        // === Game 2: Similar position, agent plays center again, wins again ===
        // This dedup-merges with g1_m4
        var g2Move = MakeObservation("g2_m4", gameNumber: 2, col: 3,
            summary: "midgame c3h2 c1h1 h_a1f3 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.33f,
                ["line:a1"] = 0.15f
            });
        var g2Merged = await _memory.CommitAsync(g2Move);
        g2Merged.Id.ShouldBe("g1_m4", "Should merge with game 1's identical position");
        g2Merged.ObservationCount.ShouldBe(3); // 1 original + 1 from reinforce + 1 from merge

        // Reinforce again with another win
        await _memory.ReinforceAsync("g1_m4", new Reinforcement
        {
            NewEvidence = ["game 2: agent won again from same position"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        // === Game 3: Different position (edge play), agent loses ===
        var g3Move = MakeObservation("g3_m4", gameNumber: 3, col: 0,
            summary: "midgame c0h3 c6h2 h_e2f2 played_c0",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_0"] = 0.9f,
                ["line:e2"] = 0.4f
            });
        await _memory.CommitAsync(g3Move);

        await _memory.ReinforceAsync("g3_m4", new Reinforcement
        {
            NewEvidence = ["game 3: agent lost, edge play failed"],
            Source = "game-analysis",
            Reward = -1.0f
        });

        // === Verify progressive learning ===
        var winningEntry = (await _memory.GetAsync("g1_m4"))!;
        var losingEntry = (await _memory.GetAsync("g3_m4"))!;

        // Confidence converges toward reward: positive reinforcement raises it,
        // negative reinforcement pushes it toward 0. After two positive
        // reinforcements with reward=1.0, confidence should be above initial 0.3.
        winningEntry.Confidence.ShouldBeGreaterThan(0.3f,
            "Confidence should increase after positive reinforcement");

        // Winning position should have higher confidence than losing one
        winningEntry.Confidence.ShouldBeGreaterThan(losingEntry.Confidence,
            "Repeatedly winning position should have higher confidence than losing one");

        // Variance should decrease with consistent reinforcement direction
        winningEntry.Variance.ShouldBeLessThan(1.0f,
            "Variance should decrease from initial 1.0 after consistent positive outcomes");

        // Winning position has positive valence, losing has negative
        winningEntry.Valence.ShouldBeGreaterThan(0f);
        losingEntry.Valence.ShouldBeLessThan(0f);

        // Both should have been observed multiple times
        winningEntry.ObservationCount.ShouldBeGreaterThan(2);
        losingEntry.ObservationCount.ShouldBe(2);
    }

    // ── Heuristic synthesis and recall ────────────────────────────

    [Test]
    public async Task Heuristic_entries_are_recalled_alongside_patterns()
    {
        // Commit a pattern observation (mid-game position)
        var pattern = MakeObservation("pattern_1", gameNumber: 1, col: 3,
            summary: "midgame c3h3 c2h2 line_a2f2 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.5f,
                ["line:a2"] = 0.4f
            });
        await _memory.CommitAsync(pattern);

        // Commit a heuristic derived from the game outcome
        var heuristic = MakeHeuristic("heuristic_g1",
            summary: "when agent had multiple pieces in center column: play center column when available",
            tags: new Dictionary<string, float>
            {
                ["phase:endgame"] = 0.4f,
                ["outcome:win"] = 1.0f,
                ["center:agent"] = 0.67f,
                ["line:a3"] = 0.4f
            },
            situation: "Agent had multiple pieces in center column",
            preferred: "Play center column when available");
        await _memory.CommitAsync(heuristic);

        // Recall for a midgame center-play query
        var results = await _memory.RecallAsync(
            "midgame c3h3 center play strategy",
            new RecallOptions { TopK = 5 });

        results.Count.ShouldBeGreaterThanOrEqualTo(2);

        var kinds = results.Select(r => r.Entry.Kind).Distinct().ToList();
        kinds.ShouldContain(EmpiricalKind.Pattern);
        kinds.ShouldContain(EmpiricalKind.Heuristic);
    }

    // ── Decay removes weak entries ───────────────────────────────

    [Test]
    public async Task Repeatedly_contradicted_position_loses_strength()
    {
        var entry = MakeObservation("weak_pos", gameNumber: 1, col: 6,
            summary: "opening c6h0 played_c6",
            tags: new Dictionary<string, float>
            {
                ["phase:opening"] = 0.4f,
                ["action:col_6"] = 0.9f
            });

        await _memory.CommitAsync(entry);

        // Three negative reinforcements — strength should erode
        for (var i = 0; i < 3; i++)
        {
            await _memory.ReinforceAsync("weak_pos", new Reinforcement
            {
                NewEvidence = [$"game {i + 2}: agent lost from this position"],
                Source = "game-analysis",
                Reward = -1.0f
            });
        }

        var weakened = (await _memory.GetAsync("weak_pos"))!;
        weakened.Valence.ShouldBeLessThan(0f);
        weakened.Variance.ShouldBeGreaterThan(0f,
            "Repeated large prediction errors should increase variance");
    }

    // ── Prediction source with tag overlap ──────────────────────

    [Test]
    public async Task TagOverlapPredictionSource_forms_prediction_from_neighbors()
    {
        var predictionSource = new TagOverlapPredictionSource(neighborCount: 5);
        var memory = new InMemoryEmpiricalMemory(
            new InMemoryEmbedder(),
            dedupThreshold: 0.9f,
            affectOptions: new AffectOptions { ReinforcementCooldownHours = 0.0001f },
            predictionSource: predictionSource);

        // Commit and reinforce a "center play wins" pattern
        var centerWin = MakeObservation("center_win", gameNumber: 1, col: 3,
            summary: "opening c3h0 first move center column",
            tags: new Dictionary<string, float>
            {
                ["phase:opening"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.5f
            });
        await memory.CommitAsync(centerWin);

        await memory.ReinforceAsync("center_win", new Reinforcement
        {
            NewEvidence = ["game 1: agent won"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        // Commit a different position that shares tags but has very different text
        var centerNew = MakeObservation("center_new", gameNumber: 2, col: 3,
            summary: "aggressive endgame multiple threats diagonal line",
            tags: new Dictionary<string, float>
            {
                ["phase:endgame"] = 0.4f,
                ["action:col_3"] = 0.9f,
                ["center:agent"] = 0.67f,
                ["line:a3"] = 0.4f
            });
        await memory.CommitAsync(centerNew);

        // Both entries should exist separately (different enough text to avoid dedup)
        memory.Count.ShouldBe(2, "Entries should not have been dedup-merged");

        // Reinforce the new entry — the prediction source should use the
        // reinforced center_win neighbor's experience to form a prediction
        await memory.ReinforceAsync("center_new", new Reinforcement
        {
            NewEvidence = ["game 2: agent won"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        var updated = await memory.GetAsync("center_new");
        updated.ShouldNotBeNull();

        // Prediction should have been set (not null — prediction source was active)
        updated.Prediction.ShouldNotBeNull(
            "TagOverlapPredictionSource should form a prediction from the reinforced neighbor");
    }

    [Test]
    public async Task TagOverlapPredictionSource_no_neighbors_falls_back_to_confidence()
    {
        var predictionSource = new TagOverlapPredictionSource(neighborCount: 5);
        var memory = new InMemoryEmpiricalMemory(
            new InMemoryEmbedder(),
            dedupThreshold: 0.9f,
            affectOptions: new AffectOptions { ReinforcementCooldownHours = 0.0001f },
            predictionSource: predictionSource);

        // Commit a single entry with no neighbors
        var lone = MakeObservation("lone_pos", gameNumber: 1, col: 3,
            summary: "midgame c3h2 played_c3",
            tags: new Dictionary<string, float>
            {
                ["phase:midgame"] = 0.4f,
                ["action:col_3"] = 0.9f
            });
        await memory.CommitAsync(lone);

        // Reinforce — no reinforced neighbors exist, so prediction source returns null
        // Falls back to Confidence (0.3)
        await memory.ReinforceAsync("lone_pos", new Reinforcement
        {
            NewEvidence = ["game 1: agent won"],
            Source = "game-analysis",
            Reward = 1.0f
        });

        var updated = await memory.GetAsync("lone_pos");
        updated.ShouldNotBeNull();
        // Prediction fell back to initial confidence (0.3)
        updated.Prediction.ShouldBe(0.3f);
        // Valence = actual - predicted = 1.0 - 0.3 = 0.7
        updated.Valence.ShouldBe(0.7f, 0.001f);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static EmpiricalEntry MakeObservation(
        string id, int gameNumber, int col,
        string summary,
        Dictionary<string, float> tags) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Pattern,
        Tags = [$"game_{gameNumber}", $"col_{col}", $"move_4"],
        Source = "game_observation",
        Description = new SemanticDescription { Summary = summary, SemanticTags = tags },
        Confidence = 0.3f,
        ObservationCount = 1,
        Evidence = [$"game {gameNumber}: agent played col {col + 1}"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };

    private static EmpiricalEntry MakeOutcome(
        string id, int gameNumber, string result,
        string summary,
        Dictionary<string, float> tags) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Pattern,
        Tags = [$"game_{gameNumber}", "outcome", result],
        Source = "game_outcome",
        Description = new SemanticDescription { Summary = summary, SemanticTags = tags },
        Confidence = result is "win" or "loss" ? 0.5f : 0.2f,
        ObservationCount = 1,
        Evidence = [$"game {gameNumber}: {result}"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };

    private static EmpiricalEntry MakeHeuristic(
        string id,
        string summary,
        Dictionary<string, float> tags,
        string situation,
        string preferred) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Heuristic,
        Tags = ["win"],
        Source = "game_analysis",
        Description = new SemanticDescription { Summary = summary, SemanticTags = tags },
        Situation = situation,
        PreferredApproach = preferred,
        Confidence = 0.3f,
        ObservationCount = 1,
        Evidence = ["game 1: agent won"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };
}
