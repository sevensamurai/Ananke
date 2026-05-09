using Ananke.Design;
using Ananke.Federation.Recommendation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class PlatformRecommenderTests
{
    private readonly PlatformRecommender _recommender = new();

    // ── helpers ───────────────────────────────────────────────────────

    private static WorkflowManifest MakeManifest(
        string name = "test",
        IReadOnlyList<string>? intents = null,
        ManifestGovernance? governance = null,
        ManifestBudget? budget = null,
        ManifestSlo? slo = null) => new()
    {
        Name = name,
        Models = new() { ["default"] = new() { Provider = "openai", Model = "gpt-4.1-mini" } },
        Jobs = new() { ["agent1"] = new() { Type = "agent", ModelAlias = "default" } },
        Connections = ["agent1"],
        Intents = intents ?? [],
        Governance = governance,
        Budget = budget,
        Slo = slo
    };

    private static ToolKit MakeKit(params (string Name, string Capability)[] nativeTools)
    {
        var kit = new ToolKit("test");
        foreach (var (name, cap) in nativeTools)
        {
            kit.AddTool(new ToolDefinition
            {
                Name             = name,
                Description      = name,
                Parameters       = [],
                ExecutionMode    = ToolExecutionMode.PlatformNative,
                PlatformCapability = cap,
                Execute          = (_, _) => Task.FromResult(ToolResult.Ok("stub"))
            });
        }
        return kit;
    }

    // ── P1: platform-profiles.json loads ─────────────────────────────

    [Test]
    public void PlatformProfiles_loads_all_three_platforms()
    {
        PlatformProfiles.KnownPlatforms.Count.ShouldBeGreaterThanOrEqualTo(3);
        PlatformProfiles.Get("azure-ai").ShouldNotBeNull();
        PlatformProfiles.Get("vertex-ai").ShouldNotBeNull();
        PlatformProfiles.Get("claude").ShouldNotBeNull();
    }

    [Test]
    public void PlatformProfiles_alias_foundry_resolves()
    {
        var foundry = PlatformProfiles.Get("foundry");
        foundry.ShouldNotBeNull();
        foundry!.DisplayName.ShouldContain("Foundry");
    }

    [Test]
    public void PlatformProfiles_has_strengths_and_weaknesses()
    {
        var azureAi = PlatformProfiles.Get("azure-ai")!;
        azureAi.Strengths.ShouldContain("enterprise_data");
        azureAi.Weaknesses.ShouldContain("bash");

        var claude = PlatformProfiles.Get("claude")!;
        claude.Strengths.ShouldContain("bash");
        claude.Weaknesses.ShouldContain("enterprise_data");
    }

    // ── P2: capability coverage ───────────────────────────────────────

    [Test]
    public void Empty_toolkit_gives_full_capability_coverage_on_all_platforms()
    {
        var report = _recommender.Evaluate(MakeManifest(), new ToolKit("empty"));

        foreach (var s in report.Scores)
            s.CapabilityCoverage.ShouldBe(1.0);
    }

    [Test]
    public void Azure_ai_covers_sharepoint_grounding()
    {
        var kit = MakeKit(("search", "sharepoint_grounding"));
        var report = _recommender.Evaluate(MakeManifest(), kit, ["azure-ai", "claude"]);

        var azure = report.Scores.First(s => s.Platform == "azure-ai");
        var claude = report.Scores.First(s => s.Platform == "claude");

        azure.CapabilityCoverage.ShouldBe(1.0);
        claude.CapabilityCoverage.ShouldBeLessThan(1.0);
    }

    [Test]
    public void Claude_covers_bash_but_not_sharepoint_grounding()
    {
        var kit = MakeKit(
            ("shell", "bash"),
            ("docs", "sharepoint_grounding"));

        var report = _recommender.Evaluate(MakeManifest(), kit, ["claude"]);

        var claude = report.Scores.Single();
        claude.CapabilityCoverage.ShouldBe(0.5); // 1 of 2 covered
    }

    [Test]
    public void Recommended_platform_is_the_best_fitting_one()
    {
        // Manifest with enterprise data tools — Foundry (azure-ai) should win
        var kit = MakeKit(
            ("sp", "sharepoint_grounding"),
            ("bing", "bing_grounding"),
            ("mem", "memory_search"));

        var report = _recommender.Evaluate(
            MakeManifest(intents: ["enterprise_data", "governance"]),
            kit);

        report.Recommended.ShouldBe("azure-ai");
    }

    [Test]
    public void Report_scores_are_sorted_descending()
    {
        var report = _recommender.Evaluate(MakeManifest(), new ToolKit("empty"));

        var totals = report.Scores.Select(s => s.Total).ToList();
        totals.ShouldBeInOrder(SortDirection.Descending);
    }

    // ── P3: strength alignment ────────────────────────────────────────

    [Test]
    public void Strength_alignment_is_neutral_when_no_intents()
    {
        var report = _recommender.Evaluate(MakeManifest(intents: []), new ToolKit("empty"));

        foreach (var s in report.Scores)
            s.StrengthAlignment.ShouldBe(0.5);
    }

    [Test]
    public void Claude_scores_high_strength_for_bash_intent()
    {
        var report = _recommender.Evaluate(
            MakeManifest(intents: ["bash", "code_agentic_loop"]),
            new ToolKit("empty"),
            ["claude"]);

        var claude = report.Scores.Single();
        claude.StrengthAlignment.ShouldBeGreaterThan(0.5);
    }

    [Test]
    public void Azure_ai_scores_low_strength_for_bash_intent()
    {
        var report = _recommender.Evaluate(
            MakeManifest(intents: ["bash"]),
            new ToolKit("empty"),
            ["azure-ai"]);

        var azure = report.Scores.Single();
        azure.StrengthAlignment.ShouldBeLessThan(0.5);
    }

    // ── P3: governance fit ────────────────────────────────────────────

    [Test]
    public void No_governance_requirements_gives_full_governance_score()
    {
        var report = _recommender.Evaluate(MakeManifest(governance: null), new ToolKit("empty"));

        foreach (var s in report.Scores)
            s.GovernanceFit.ShouldBe(1.0);
    }

    [Test]
    public void Private_networking_blocks_claude()
    {
        var gov = new ManifestGovernance { PrivateNetworking = true };
        var report = _recommender.Evaluate(
            MakeManifest(governance: gov),
            new ToolKit("empty"),
            ["azure-ai", "claude"]);

        var azure = report.Scores.First(s => s.Platform == "azure-ai");
        var claude = report.Scores.First(s => s.Platform == "claude");

        azure.GovernanceFit.ShouldBe(1.0);
        azure.Total.ShouldBeGreaterThan(0);

        claude.Total.ShouldBe(0.0); // blocked
        claude.Reasons.ShouldContain(r => r.Kind == FitReasonKind.Block);
    }

    [Test]
    public void All_platforms_blocked_gives_null_recommended()
    {
        // Require a region that no platform has
        var gov = new ManifestGovernance { PrivateNetworking = true, Rbac = true };
        var report = _recommender.Evaluate(
            MakeManifest(governance: gov),
            new ToolKit("empty"),
            ["claude"]); // claude has neither privateNetworking nor rbac

        report.Recommended.ShouldBeNull();
    }

    // ── P3: cost / latency fit ────────────────────────────────────────

    [Test]
    public void Tight_budget_penalises_medium_cost_platforms()
    {
        var budget = new ManifestBudget { MaxCostPerRunUsd = 0.05 };
        var report = _recommender.Evaluate(
            MakeManifest(budget: budget),
            new ToolKit("empty"),
            ["azure-ai", "vertex-ai"]); // azure = medium, vertex = low

        var azure  = report.Scores.First(s => s.Platform == "azure-ai");
        var vertex = report.Scores.First(s => s.Platform == "vertex-ai");

        azure.CostLatencyFit.ShouldBeLessThan(vertex.CostLatencyFit);
    }

    [Test]
    public void Alias_foundry_resolves_and_is_scored()
    {
        var report = _recommender.Evaluate(MakeManifest(), new ToolKit("empty"), ["foundry"]);

        report.Scores.ShouldContain(s => s.Platform == "azure-ai");
    }
}
