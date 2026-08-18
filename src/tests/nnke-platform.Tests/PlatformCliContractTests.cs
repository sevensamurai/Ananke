using Ananke.Federation.Adapters;
using Ananke.Tool.Platform;
using Ananke.Tool.Platform.Commands;
using Ananke.Tool.Shared;
using Shouldly;
using System.CommandLine;

namespace Ananke.Tool.Platform.Tests;

/// <summary>
/// Exercises <c>nnke-platform</c> commands through the real <see cref="RootCommand"/> pipeline
/// and asserts the process exit code each one yields.
/// </summary>
/// <remarks>
/// <para>
/// The rest of this suite drives <c>PlatformHost</c> and the deployment registry directly, so
/// nothing covered the command actions themselves. Three defects lived in that gap:
/// every action returned <see langword="void"/> (so a BLOCKED manifest still exited 0),
/// <c>adapters</c> never probed the adapters directory, and <c>--across a,b</c> was treated
/// as one platform named <c>"a,b"</c>. These tests are the guard for all three.
/// </para>
/// <para>
/// Exit-code contract, matching <c>nnke</c>: <c>0</c> success, <c>1</c> usage or I/O error
/// (missing file, unknown platform/profile), <c>2</c> the command ran but the answer is
/// negative (manifest not deployable, adapter unhealthy).
/// </para>
/// </remarks>
[TestFixture]
public class PlatformCliContractTests
{
    private string _dir = null!;
    private string _validManifest = null!;
    private string _blockedManifest = null!;
    private string _missingFile = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nnke-platform-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        _validManifest = Path.Combine(_dir, "valid.ananke.yml");
        File.WriteAllText(_validManifest,
            """
            name: cli-contract-workflow
            models:
              primary:
                provider: openai
                model: gpt-4.1-mini
            jobs:
              extract:
                type: agent
                model: primary
                prompt: "Extract: {{input}}"
              load:
                type: agent
                model: primary
                prompt: "Load: {{input}}"
            connections:
              - extract -> load
              - load -> End
            """);

        // References a model alias that is not declared — FED011, an error-severity
        // diagnostic, so the deployability report comes back BLOCKED.
        _blockedManifest = Path.Combine(_dir, "blocked.ananke.yml");
        File.WriteAllText(_blockedManifest,
            """
            name: blocked-workflow
            models: {}
            jobs:
              orphan:
                type: agent
                model: undefined-alias
                prompt: "orphan"
            connections:
              - orphan -> End
            """);

        _missingFile = Path.Combine(_dir, "does-not-exist.yml");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Builds a root command carrying the recursive <c>--json</c> and <c>--in-memory</c>
    /// options the actions read, mirroring <c>nnke-platform</c>'s <c>Program.cs</c>.
    /// </summary>
    private static int Invoke(Command command, params string[] args)
    {
        var root = new RootCommand("test-root")
        {
            CliOptions.CreateJsonOption(),
            new Option<bool>("--in-memory") { Recursive = true },
            command
        };

        return root.Parse(args).Invoke();
    }

    // ── validate ──────────────────────────────────────────────────────────────

    [Test]
    public void Validate_deployable_manifest_exits_zero()
    {
        Invoke(ValidateCommand.Create(), "validate", _validManifest, "--platform", "azure-ai")
            .ShouldBe(0);
    }

    [Test]
    public void Validate_blocked_manifest_exits_two()
    {
        Invoke(ValidateCommand.Create(), "validate", _blockedManifest, "--platform", "azure-ai")
            .ShouldBe(2);
    }

    [Test]
    public void Validate_missing_file_exits_one()
    {
        Invoke(ValidateCommand.Create(), "validate", _missingFile, "--platform", "azure-ai")
            .ShouldBe(1);
    }

    [Test]
    public void Validate_unknown_profile_exits_one()
    {
        Invoke(ValidateCommand.Create(),
            "validate", _validManifest, "--platform", "azure-ai", "--profile", "no-such-profile")
            .ShouldBe(1);
    }

    // ── other manifest-reading commands ───────────────────────────────────────

    [Test]
    public void Profiles_missing_file_exits_one() =>
        Invoke(ProfilesCommand.Create(), "profiles", _missingFile).ShouldBe(1);

    [Test]
    public void Profiles_valid_manifest_exits_zero() =>
        Invoke(ProfilesCommand.Create(), "profiles", _validManifest).ShouldBe(0);

    [Test]
    public void Analyze_missing_file_exits_one() =>
        Invoke(AnalyzeCommand.Create(), "analyze", _missingFile).ShouldBe(1);

    [Test]
    public void Analyze_valid_manifest_exits_zero() =>
        Invoke(AnalyzeCommand.Create(), "analyze", _validManifest).ShouldBe(0);

    // ── snapshot-reading commands ─────────────────────────────────────────────
    // A workflow manifest is not a host snapshot, so these all fail to parse.

    [Test]
    public void Mesh_unparseable_snapshot_exits_one() =>
        Invoke(MeshStatusCommand.Create(), "mesh", _validManifest).ShouldBe(1);

    [Test]
    public void Lineage_unparseable_snapshot_exits_one() =>
        Invoke(LineageCommand.Create(), "lineage", "some-cell", _validManifest).ShouldBe(1);

    [Test]
    public void Apoptosis_unparseable_snapshot_exits_one() =>
        Invoke(ApoptosisCommand.Create(), "apoptosis", _validManifest).ShouldBe(1);

    [Test]
    public void Apoptosis_missing_file_exits_one() =>
        Invoke(ApoptosisCommand.Create(), "apoptosis", _missingFile).ShouldBe(1);

    // ── capabilities / login ──────────────────────────────────────────────────

    [Test]
    public void Capabilities_known_platform_exits_zero() =>
        Invoke(CapabilitiesCommand.Create(), "capabilities", "--platform", "azure-ai").ShouldBe(0);

    [Test]
    public void Capabilities_unknown_platform_exits_one() =>
        Invoke(CapabilitiesCommand.Create(), "capabilities", "--platform", "not-a-platform").ShouldBe(1);

    [Test]
    public void Capabilities_list_all_exits_zero() =>
        Invoke(CapabilitiesCommand.Create(), "capabilities").ShouldBe(0);

    [Test]
    public void Login_unknown_platform_exits_one() =>
        Invoke(LoginCommand.Create(), "login", "--platform", "not-a-platform").ShouldBe(1);

    // ── status ────────────────────────────────────────────────────────────────

    [Test]
    public void Status_unknown_deployment_id_exits_one() =>
        Invoke(StatusCommand.Create(), "status", "--deployment-id", "no-such-id", "--in-memory")
            .ShouldBe(1);

    [Test]
    public void Status_empty_registry_exits_zero() =>
        Invoke(StatusCommand.Create(), "status", "--in-memory").ShouldBe(0);

    // ── compare: --across splitting ───────────────────────────────────────────

    [Test]
    public void Compare_comma_separated_across_is_split_into_platforms()
    {
        var output = CaptureStdout(() =>
            Invoke(CompareCommand.Create(), "compare", "cell-a", "--across", "azure-ai,claude", "--json")
                .ShouldBe(0));

        // Each platform must appear as its own row — never a single "azure-ai,claude" entry.
        output.ShouldContain("\"platform\": \"azure-ai\"");
        output.ShouldContain("\"platform\": \"claude\"");
        output.ShouldNotContain("azure-ai,claude");
    }

    [Test]
    public void Compare_space_separated_across_is_still_supported()
    {
        var output = CaptureStdout(() =>
            Invoke(CompareCommand.Create(), "compare", "cell-a", "--across", "azure-ai", "claude", "--json")
                .ShouldBe(0));

        output.ShouldContain("\"platform\": \"azure-ai\"");
        output.ShouldContain("\"platform\": \"claude\"");
    }

    [Test]
    public void Compare_across_trims_whitespace_and_deduplicates()
    {
        var output = CaptureStdout(() =>
            Invoke(CompareCommand.Create(), "compare", "cell-a", "--across", "azure-ai, claude,azure-ai", "--json")
                .ShouldBe(0));

        // "azure-ai" was named twice; it must be compared once.
        CountOccurrences(output, "\"platform\": \"azure-ai\"").ShouldBe(1);
        output.ShouldContain("\"platform\": \"claude\"");
    }

    // ── adapters: the probe must actually run ─────────────────────────────────

    [Test]
    public void Adapters_list_reports_the_probe_directory_and_exits_zero()
    {
        // The probe result depends on what is installed on the machine, so this asserts the
        // contract that holds either way: the command runs, names the directory it probed,
        // and exits 0 (list) — never throwing because diagnostics were never populated.
        var output = CaptureStdout(() =>
            Invoke(AdaptersCommand.Create(), "adapters", "list").ShouldBe(0));

        output.ShouldContain("adapters");
    }

    [Test]
    public void Adapters_list_json_emits_an_adapters_array()
    {
        var output = CaptureStdout(() =>
            Invoke(AdaptersCommand.Create(), "adapters", "list", "--json").ShouldBe(0));

        output.ShouldContain("\"adaptersDirectory\"");
        output.ShouldContain("\"adapters\"");
    }

    [Test]
    public void Adapters_doctor_exits_zero_or_two_and_never_throws()
    {
        var exit = Invoke(AdaptersCommand.Create(), "adapters", "doctor");
        exit.ShouldBeOneOf(0, 2);
    }

    /// <summary>
    /// Regression guard for the adapters probe. <c>AdapterDiagnostics</c> is only populated by
    /// the <c>PlatformHost</c> constructor; when <c>adapters</c> skipped that step it reported
    /// "No adapters installed" no matter what was on disk. Constructing a host must make the
    /// probe observable.
    /// </summary>
    [Test]
    public void Adapters_probe_runs_when_a_platform_host_is_constructed()
    {
        var probeDirectory = PlatformHost.AdaptersDirectory;
        probeDirectory.ShouldNotBeNullOrWhiteSpace();

        using var host = new PlatformHost(inMemory: true);

        // Every manifest on disk must be accounted for in diagnostics — the count may be zero
        // on a clean machine, but it can never be fewer than the manifests present.
        var manifestCount = Directory.Exists(probeDirectory)
            ? Directory.GetFiles(probeDirectory, "*.adapter.json").Length
            : 0;

        AdapterDiagnostics.Results.Count.ShouldBeGreaterThanOrEqualTo(manifestCount);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
