using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Which model each role runs on, and whether the escalation a wiring expresses is real.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the answer was computed, printed, and acted on by nothing.</b> Two demos
/// each resolved a pair of role models from settings, each worked out whether the pair had collapsed
/// onto one model, and each said so in a line of console output. A dozen live runs went by with the
/// stronger role filled by the cheap model, and every finding from them was about one model wearing
/// two hats.
/// </para>
/// <para>
/// <b>Neither of those demos had a test project, and that is not incidental — it is the reason.</b>
/// The resolution lives here so a collapse fails a test rather than a season of runs.
/// </para>
/// </remarks>
[TestFixture]
public class RoleModelsTests
{
    private const string ForRole = "ANANKE_DEMO_SUPERVISOR_MODEL";
    private const string Shared = "OPENAI_MODEL";

    // ── Resolution ──

    [Test]
    public void Resolve_WhatWasSetForTheRole_WinsAndIsMarkedChosen()
    {
        var model = RoleModels.Resolve("supervisor", "gpt-strong", ForRole, "gpt-shared", Shared, "gpt-default");

        model.Id.ShouldBe("gpt-strong");
        model.From.ShouldBe(ForRole);
        model.Chosen.ShouldBeTrue();
    }

    [Test]
    public void Resolve_WithNothingForTheRole_TakesTheSharedSettingAndSaysSo()
    {
        // The precedence that makes the collapse possible: an id already in someone's configuration
        // is the one their account is known to serve, so it beats the catalogue. It is not a defect
        // — but it is why the source has to travel with the answer.
        var model = RoleModels.Resolve("supervisor", null, ForRole, "gpt-shared", Shared, "gpt-default");

        model.Id.ShouldBe("gpt-shared");
        model.From.ShouldBe(Shared);
        model.Chosen.ShouldBeFalse();
    }

    [Test]
    public void Resolve_WithNothingSetAtAll_TakesTheCatalogueDefault()
    {
        var model = RoleModels.Resolve("supervisor", null, ForRole, null, Shared, "gpt-default");

        model.Id.ShouldBe("gpt-default");
        model.From.ShouldBe(RoleModels.Catalogue);
        model.Chosen.ShouldBeFalse();

        // Carried even though nothing set it, so a message can say what to set.
        model.Setting.ShouldBe(ForRole);
    }

    [Test]
    public void Resolve_WithBlankSettings_TreatsThemAsUnset()
    {
        // An exported-but-empty variable is how this fails in a shell, and "" is not a model id.
        RoleModels.Resolve("supervisor", "  ", ForRole, "", Shared, "gpt-default").Id
            .ShouldBe("gpt-default");
    }

    // ── The collapse this was built to catch ──

    [Test]
    public void Collapsed_WhenBothRolesFellBackToOneSharedSetting_NamesTheSettingResponsible()
    {
        // The defect exactly: OPENAI_MODEL set, neither role named, both roles on one model, and a
        // run that reports an escalation it never had.
        var executor = RoleModels.Resolve("executor", null, "ANANKE_DEMO_EXECUTOR_MODEL", "gpt-mini", Shared, "gpt-cheap");
        var supervisor = RoleModels.Resolve("supervisor", null, ForRole, "gpt-mini", Shared, "gpt-strong");

        var why = RoleModels.Collapsed(executor, supervisor);

        why.ShouldNotBeNull();

        // Actionable, not merely correct: it names the value, the setting that supplied it, and the
        // setting to change. A message that said only "these are the same" leaves the reader to
        // work out which of three places did it.
        why.ShouldContain(Shared);
        why.ShouldContain("gpt-mini");
        why.ShouldContain("ANANKE_DEMO_EXECUTOR_MODEL");
        RoleModels.Escalated(executor, supervisor).ShouldBeFalse();
    }

    [Test]
    public void Collapsed_WhenOnlyOneRoleInheritedTheSharedSetting_IsStillACollapse()
    {
        // Half-configured is not half-safe. One role named, the other inheriting a value that
        // happens to match, is the same run measuring one model twice.
        var executor = RoleModels.Resolve("executor", "gpt-mini", "ANANKE_DEMO_EXECUTOR_MODEL", null, Shared, "gpt-cheap");
        var supervisor = RoleModels.Resolve("supervisor", null, ForRole, "gpt-mini", Shared, "gpt-strong");

        RoleModels.Collapsed(executor, supervisor).ShouldNotBeNull();
    }

    [Test]
    public void Collapsed_WhenEachRoleWasNamedExplicitly_AllowsThePair()
    {
        // Comparing the tier against itself is a legitimate experiment, and somebody typing the same
        // id twice has said so. Refusing here would block the one case where equal ids are a
        // decision rather than an accident.
        var executor = RoleModels.Resolve("executor", "gpt-mini", "ANANKE_DEMO_EXECUTOR_MODEL", null, Shared, "gpt-cheap");
        var supervisor = RoleModels.Resolve("supervisor", "gpt-mini", ForRole, null, Shared, "gpt-strong");

        RoleModels.Collapsed(executor, supervisor).ShouldBeNull();

        // Still not an escalation, and the two questions stay separate: one asks whether anybody
        // decided this, the other asks what is actually true of the run.
        RoleModels.Escalated(executor, supervisor).ShouldBeFalse();
    }

    [Test]
    public void Collapsed_WhenTheRolesGotDifferentModels_IsNothing()
    {
        var executor = RoleModels.Resolve("executor", null, "ANANKE_DEMO_EXECUTOR_MODEL", null, Shared, "gpt-cheap");
        var supervisor = RoleModels.Resolve("supervisor", null, ForRole, null, Shared, "gpt-strong");

        RoleModels.Collapsed(executor, supervisor).ShouldBeNull();
        RoleModels.Escalated(executor, supervisor).ShouldBeTrue();
    }

    [Test]
    public void Collapsed_ComparesIdsWithoutCaring_AboutCase()
    {
        // A provider that answers to either spelling would otherwise slip a collapse past this.
        var executor = RoleModels.Resolve("executor", null, "ANANKE_DEMO_EXECUTOR_MODEL", "GPT-Mini", Shared, "gpt-cheap");
        var supervisor = RoleModels.Resolve("supervisor", null, ForRole, "gpt-mini", Shared, "gpt-strong");

        RoleModels.Collapsed(executor, supervisor).ShouldNotBeNull();
    }
}
