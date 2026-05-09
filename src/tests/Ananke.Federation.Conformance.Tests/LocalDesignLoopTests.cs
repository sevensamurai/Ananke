using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Execution;
using Ananke.Federation.Hosting;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// Phase B conformance tests for the local design loop:
/// <see cref="LocalFederationDeployer"/>, <see cref="LocalPlatformValidator"/>,
/// <see cref="PlatformNativeExecutorRegistry"/>, and <see cref="HybridRouter"/>
/// <c>local-emulated:&lt;platform&gt;</c> routing tier.
/// </summary>
[TestFixture]
public class LocalDesignLoopTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static WorkflowManifest Manifest(string name = "local-wf") =>
        FederationConformanceFactory.MakeManifest(name);

    private static ToolKit LocalKit() =>
        FederationConformanceFactory.MakeToolKit("local-kit");

    private static ToolKit PlatformNativeKit(string capability = "web_search")
    {
        var kit = new ToolKit("native-kit");
        kit.AddTool("search", "Web search tool", b => b.PlatformNative(capability));
        return kit;
    }

    // ── LocalFederationDeployer ───────────────────────────────────────

    [Test]
    public void LocalFederationDeployer_Platform_IsLocal()
    {
        var deployer = new LocalFederationDeployer(new InMemoryDeploymentRegistry());
        deployer.Platform.ShouldBe("local");
    }

    [Test]
    public async Task LocalFederationDeployer_DeployAsync_RegistersActiveRecord()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = new LocalFederationDeployer(registry);
        var options = new DeployOptions { Platform = "local" };

        var record = await deployer.DeployAsync(Manifest(), LocalKit(), options);

        record.ShouldNotBeNull();
        record.Platform.ShouldBe("local");
        record.Status.ShouldBe(DeploymentStatus.Active);
        record.WorkflowName.ShouldBe("local-wf");

        var stored = await registry.GetAsync(record.DeploymentId);
        stored.ShouldNotBeNull();
        stored!.Status.ShouldBe(DeploymentStatus.Active);
    }

    [Test]
    public async Task LocalFederationDeployer_TeardownAsync_SetsStatusStopped()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = new LocalFederationDeployer(registry);

        var record = await deployer.DeployAsync(Manifest(), LocalKit(), new DeployOptions { Platform = "local" });
        await deployer.TeardownAsync(record.DeploymentId);

        var stored = await registry.GetAsync(record.DeploymentId);
        stored!.Status.ShouldBe(DeploymentStatus.Stopped);
    }

    [Test]
    public async Task LocalFederationDeployer_MarkFailedAsync_SetsStatusFailed()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = new LocalFederationDeployer(registry);

        var record = await deployer.DeployAsync(Manifest(), LocalKit(), new DeployOptions { Platform = "local" });
        await deployer.MarkFailedAsync(record.DeploymentId);

        var stored = await registry.GetAsync(record.DeploymentId);
        stored!.Status.ShouldBe(DeploymentStatus.Failed);
    }

    [Test]
    public async Task LocalFederationDeployer_ValidateAsync_EmptyKit_ReturnsDeployable()
    {
        var deployer = new LocalFederationDeployer(new InMemoryDeploymentRegistry());
        var report = await deployer.ValidateAsync(Manifest(), LocalKit());
        report.IsDeployable.ShouldBeTrue();
    }

    // ── LocalPlatformValidator ────────────────────────────────────────

    [Test]
    public void LocalPlatformValidator_Platform_WhenNoEmulatedPlatform_IsLocal()
    {
        var validator = new LocalPlatformValidator();
        validator.Platform.ShouldBe("local");
    }

    [Test]
    public void LocalPlatformValidator_Platform_WithEmulatedPlatform_HasPrefix()
    {
        var validator = new LocalPlatformValidator(emulatedPlatform: "azure-ai");
        validator.Platform.ShouldBe("local-emulated:azure-ai");
    }

    [Test]
    public async Task LocalPlatformValidator_FED061_WhenNoExecutorRegistered()
    {
        var registry = new PlatformNativeExecutorRegistry(); // empty
        var validator = new LocalPlatformValidator(registry);

        var report = await validator.ValidateAsync(Manifest(), PlatformNativeKit("web_search"));

        report.Errors.ShouldContain(d => d.Code == "FED061");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task LocalPlatformValidator_FED062_WhenStubExecutorRegistered()
    {
        var registry = new PlatformNativeExecutorRegistry();
        registry.Register(new StubExecutor("web_search"));
        var validator = new LocalPlatformValidator(registry);

        var report = await validator.ValidateAsync(Manifest(), PlatformNativeKit("web_search"));

        report.Warnings.ShouldContain(d => d.Code == "FED062");
        report.IsDeployable.ShouldBeTrue("Stub executors are warnings, not errors.");
    }

    [Test]
    public async Task LocalPlatformValidator_NoIssues_WhenRealExecutorRegistered()
    {
        var registry = new PlatformNativeExecutorRegistry();
        registry.Register(new RealExecutor("web_search"));
        var validator = new LocalPlatformValidator(registry);

        var report = await validator.ValidateAsync(Manifest(), PlatformNativeKit("web_search"));

        report.Errors.ShouldBeEmpty();
        report.Warnings.ShouldBeEmpty();
        report.IsDeployable.ShouldBeTrue();
    }

    // ── PlatformNativeExecutorRegistry ────────────────────────────────

    [Test]
    public void PlatformNativeExecutorRegistry_TryResolve_ReturnsNull_WhenNotRegistered()
    {
        var registry = new PlatformNativeExecutorRegistry();
        registry.TryResolve("web_search").ShouldBeNull();
    }

    [Test]
    public void PlatformNativeExecutorRegistry_TryResolve_ReturnsExecutor_WhenRegistered()
    {
        var registry = new PlatformNativeExecutorRegistry();
        registry.Register(new RealExecutor("code_execution"));

        registry.TryResolve("code_execution").ShouldNotBeNull();
    }

    [Test]
    public void PlatformNativeExecutorRegistry_PlatformScopedExecutor_TakesPriority()
    {
        var registry = new PlatformNativeExecutorRegistry();
        var generic = new RealExecutor("bash");
        var scoped = new RealExecutor("bash");
        registry.Register(generic);
        registry.RegisterForPlatform("azure-ai", scoped);

        registry.TryResolve("bash", "azure-ai").ShouldBeSameAs(scoped);
        registry.TryResolve("bash").ShouldBeSameAs(generic);
    }

    [Test]
    public void PlatformNativeExecutorRegistry_Register_Throws_WhenDuplicate()
    {
        var registry = new PlatformNativeExecutorRegistry();
        registry.Register(new RealExecutor("web_search"));

        Should.Throw<ArgumentException>(() => registry.Register(new RealExecutor("web_search")));
    }

    [Test]
    public void PlatformNativeExecutorRegistry_ApplyTo_PatchesPlatformNativeTools()
    {
        var registry = new PlatformNativeExecutorRegistry();
        var executor = new RealExecutor("web_search");
        registry.Register(executor);

        var kit = PlatformNativeKit("web_search");
        var patched = registry.ApplyTo(kit);

        patched.ShouldBe(1);
    }

    [Test]
    public void PlatformNativeExecutorRegistry_ApplyTo_IgnoresNonPlatformNativeTools()
    {
        var registry = new PlatformNativeExecutorRegistry();
        var kit = LocalKit();
        kit.AddTool("ping", "Pings", () => ToolResult.Ok("pong"));

        var patched = registry.ApplyTo(kit);

        patched.ShouldBe(0);
    }

    // ── HybridRouter local-emulated tier ─────────────────────────────

    [Test]
    public void HybridRouter_IsLocalEmulated_True_ForLocalEmulatedPrefix()
    {
        HybridRouter.IsLocalEmulated("local-emulated:azure-ai", out var platform).ShouldBeTrue();
        platform.ShouldBe("azure-ai");
    }

    [Test]
    public void HybridRouter_IsLocalEmulated_False_ForPlainPlatform()
    {
        HybridRouter.IsLocalEmulated("azure-ai", out var platform).ShouldBeFalse();
        platform.ShouldBeNull();
    }

    [Test]
    public void HybridRouter_IsLocalEmulated_False_ForNull()
    {
        HybridRouter.IsLocalEmulated(null, out var platform).ShouldBeFalse();
        platform.ShouldBeNull();
    }

    [Test]
    public void RoutingRule_EmulateAll_ProducesCorrectTargetPlatform()
    {
        var rule = RoutingRule.EmulateAll("vertex-ai");
        rule.TargetPlatform.ShouldBe("local-emulated:vertex-ai");
        rule.ExactName.ShouldBeNull();
        rule.Prefix.ShouldBeNull();
        rule.Suffix.ShouldBeNull();
    }

    [Test]
    public void RoutingRule_EmulateCell_MatchesOnlyNamedCell()
    {
        var rule = RoutingRule.EmulateCell("search-agent", "claude");
        rule.TargetPlatform.ShouldBe("local-emulated:claude");
        rule.ExactName.ShouldBe("search-agent");
        rule.Matches("search-agent").ShouldBeTrue();
        rule.Matches("other-agent").ShouldBeFalse();
    }

    [Test]
    public async Task HybridRouter_ResolveAsync_ReturnsLocalEmulatedPlatform_WhenRuleMatches()
    {
        var registry = new InMemoryDeploymentRegistry();
        var rules = new[] { RoutingRule.EmulateAll("azure-ai") };
        var router = new HybridRouter(registry, rules);

        var platform = await router.ResolveAsync("any-cell");

        platform.ShouldBe("local-emulated:azure-ai");
    }

    // ── Test doubles ──────────────────────────────────────────────────

    private sealed class StubExecutor(string capability) : IPlatformNativeExecutor
    {
        public string Capability => capability;
        public bool IsStub => true;

        public Task<ToolResult> ExecuteAsync(
            IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Ok("stub-result"));
    }

    private sealed class RealExecutor(string capability) : IPlatformNativeExecutor
    {
        public string Capability => capability;
        public bool IsStub => false;

        public Task<ToolResult> ExecuteAsync(
            IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Ok("real-result"));
    }
}
