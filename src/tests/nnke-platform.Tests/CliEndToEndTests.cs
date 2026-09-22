using System.Diagnostics;
using System.Text.Json;
using Ananke.Federation.Paths;
using Shouldly;

namespace Ananke.Tool.Platform.Tests;

/// <summary>
/// Launches the built <c>nnke-platform</c> binary as a process and asserts on what it does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>nnke-platform deploy</c> never resolved a probed adapter, on any
/// platform, from the day the probe shipped — <c>Assembly.LoadFrom</c> does not fire module
/// initializers. Three tests touched that area and each missed by construction:
/// two called <c>ModuleInit.Initialize()</c> or registered a stub <i>directly</i>, and the third ran
/// the real probe and then <b>deliberately asserted nothing</b> about the result, on the reasonable-
/// sounding grounds that it "depends on what is installed on the machine".
/// </para>
/// <para>
/// So the rule this fixture exists to keep: <b>install a fixture adapter into a directory the test
/// controls, and assert on that.</b> Never skip because the machine's state is unknown — make the
/// machine's state part of the test.
/// </para>
/// <para>
/// It runs inside the ordinary <c>dotnet test</c> invocation, so CI picks it up with no second
/// toolchain — unlike <c>src/nnke/test-cli.ps1</c>, which is invoked as Windows PowerShell, is
/// absent from <c>pipeline.yml</c>, and last reported 22 failures.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CliEndToEndTests
{
    private string _home = null!;
    private string _manifest = null!;

    [SetUp]
    public void SetUp()
    {
        // Every invocation in this fixture gets its own config root, so the probe directory and the
        // deployment registry are the test's and not the developer's.
        _home = Directory.CreateTempSubdirectory("nnke-e2e-").FullName;

        _manifest = Path.Combine(_home, "fixture.ananke.yml");
        File.WriteAllText(_manifest, string.Join('\n',
        [
            "name: e2e-fixture",
            "models:",
            "  main:",
            "    provider: openai",
            "    model: gpt-5.6-terra",
            "jobs:",
            "  work:",
            "    type: agent",
            "    model: main",
            "connections:",
            "  - work -> End",
            ""
        ]));
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_home, recursive: true); }
        catch (IOException) { /* a temp directory that outlives the run is not a test failure */ }
    }

    // ── the lifecycle D2(c) promises: no credentials, no cloud ────────────────

    [Test]
    public void Deploy_status_teardown_runs_against_the_local_substrate()
    {
        var deploy = Run("deploy", _manifest, "--platform", "local", "--json");
        deploy.ExitCode.ShouldBe(0, deploy.Diagnostic);

        var deploymentId = Json(deploy.Stdout).GetProperty("deploymentId").GetString();
        deploymentId.ShouldNotBeNullOrWhiteSpace();

        var status = Run("status", "--json");
        status.ExitCode.ShouldBe(0, status.Diagnostic);
        var listed = Json(status.Stdout).GetProperty("deployments").EnumerateArray().Single();
        listed.GetProperty("platform").GetString().ShouldBe("local");
        listed.GetProperty("status").GetString().ShouldBe("active");

        var teardown = Run("teardown", "--deployment-id", deploymentId!, "--json");
        teardown.ExitCode.ShouldBe(0, teardown.Diagnostic);

        var after = Run("status", "--json");
        Json(after.Stdout).GetProperty("deployments").EnumerateArray().Single()
            .GetProperty("status").GetString().ShouldBe("stopped");
    }

    /// <summary>
    /// <c>local</c> was documented <i>Stable</i> while every mechanism that decides what a platform
    /// is rejected it with <c>FED023</c>.
    /// </summary>
    [Test]
    public void Validate_accepts_local_as_a_platform()
    {
        var result = Run("validate", _manifest, "--platform", "local");

        result.ExitCode.ShouldBe(0, result.Diagnostic);
        result.Stdout.ShouldNotContain("FED023");
    }

    // ── check / whoami / login (L6, L7) ───────────────────────────────────────

    /// <summary>
    /// <c>check</c> reaches <c>IFederationDeployer.ValidateAsync</c>, which had no caller at all:
    /// <c>validate</c> is offline and <c>deploy --dry-run</c> short-circuits before it, so the only
    /// route to a live credential check was a real deploy.
    /// </summary>
    [Test]
    public void Check_runs_the_live_validation_and_creates_nothing()
    {
        var check = Run("check", _manifest, "--platform", "local", "--json");
        check.ExitCode.ShouldBe(0, check.Diagnostic);

        var payload = Json(check.Stdout);
        payload.GetProperty("status").GetString().ShouldBe("ok");

        // The local substrate uses no credentials, so a clean result must not read as
        // "my credentials work".
        payload.GetProperty("contactedPlatform").GetBoolean().ShouldBeFalse();

        // "Creates nothing" is the claim that matters — a preflight that deploys is not a preflight.
        var status = Run("status", "--json");
        Json(status.Stdout).GetProperty("deployments").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public void Check_on_an_unresolvable_platform_explains_rather_than_crashing()
    {
        var result = Run("check", _manifest, "--platform", "azure", "--json");

        result.ExitCode.ShouldBe(1, result.Diagnostic);
        result.Stderr.ShouldNotContain("Unhandled exception", customMessage: result.Diagnostic);
    }

    /// <summary>
    /// <c>whoami</c> reported a credentials file nothing read. It now reports what <c>deploy</c>
    /// would use, and an unavailable platform carries the adapter's own reason.
    /// </summary>
    [Test]
    public void Whoami_reports_what_deploy_would_use()
    {
        InstallAdapterFixture("nnke-platform-google", "vertex-ai", "vertex-ai.adapter.json");

        var result = Run("whoami", "--json");
        result.ExitCode.ShouldBe(0, result.Diagnostic);

        var platforms = Json(result.Stdout).GetProperty("platforms").EnumerateArray().ToList();

        var local = platforms.Single(p => p.GetProperty("platform").GetString() == "local");
        local.GetProperty("available").GetBoolean().ShouldBeTrue();

        var google = platforms.Single(p => p.GetProperty("platform").GetString() == "vertex-ai");
        google.GetProperty("available").GetBoolean().ShouldBeFalse();
        google.GetProperty("detail").GetString()!.ShouldContain("GOOGLE_CLOUD_PROJECT");
    }

    /// <summary>
    /// Both of these are invocations the published guide tells users to run, and both exited 1
    /// because <c>login</c> was the only verb that did not resolve platform aliases.
    /// </summary>
    [TestCase("vertex-ai")]
    [TestCase("claude")]
    [TestCase("foundry")]
    public void Login_accepts_every_platform_identifier_the_other_verbs_do(string platform)
    {
        var result = Run("login", "--platform", platform, "--json");

        result.ExitCode.ShouldBe(0, result.Diagnostic);
    }

    /// <summary>
    /// It must stay scriptable — the old implementation read secrets with <c>Console.ReadKey</c>,
    /// which throws whenever stdin is redirected, so it could not run in CI on any platform.
    /// </summary>
    [Test]
    public void Login_stores_nothing_and_needs_no_terminal()
    {
        var result = Run("login", "--platform", "claude", "--json");

        result.ExitCode.ShouldBe(0, result.Diagnostic);
        Json(result.Stdout).GetProperty("variables").EnumerateArray()
            .Select(v => v.GetProperty("name").GetString())
            .ShouldContain("ANTHROPIC_API_KEY");

        File.Exists(Path.Combine(_home, ".ananke", "credentials.json")).ShouldBeFalse();
    }

    // ── the 1.0 surface (L8) ──────────────────────────────────────────────────

    /// <summary>
    /// Pins the shipped command surface. **Removing a verb after 1.0 is breaking; adding one is
    /// not** — so this list is a commitment, and it should not grow by accident.
    /// </summary>
    /// <remarks>
    /// If this fails because a verb was added, that is the conversation the test exists to force:
    /// decide deliberately, then update the list.
    /// </remarks>
    [Test]
    public void The_command_surface_is_the_one_1_0_commits_to()
    {
        string[] expected =
        [
            "validate", "check", "capabilities", "eval", "profiles",
            "deploy", "status", "teardown",
            "analyze", "lineage", "mesh",
            "login", "whoami", "adapters"
        ];

        var help = Run("--help");
        help.ExitCode.ShouldBe(0, help.Diagnostic);

        var listed = help.Stdout
            .Split('\n')
            .SkipWhile(l => !l.StartsWith("Commands:", StringComparison.Ordinal))
            .Skip(1)
            .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        listed.ShouldBe(expected, ignoreOrder: true, customMessage: help.Diagnostic);
    }

    /// <summary>
    /// Class 4 — <c>RemoteMetricsTracker</c> is per-process and every command built a fresh one, so
    /// these could only ever report "no data" and exit 0, which is indistinguishable from a healthy
    /// nothing-to-report. <c>apoptosis</c> needs a substrate population nothing yet produces.
    /// </summary>
    [TestCase("trends")]
    [TestCase("compare")]
    [TestCase("events")]
    [TestCase("apoptosis")]
    public void Verbs_that_cannot_return_a_true_answer_are_not_registered(string verb)
    {
        var result = Run(verb);

        result.ExitCode.ShouldNotBe(0, result.Diagnostic);
    }

    /// <summary>
    /// Preview has to be visible to mean anything: a caveat that lives only in the documentation is
    /// one the person running the command never sees.
    /// </summary>
    [Test]
    public void A_vendor_platform_announces_itself_as_preview()
    {
        InstallAzureAdapterFixture();

        var result = Run(
            ["check", _manifest, "--platform", "azure", "--json"],
            ("AZURE_AI_ENDPOINT", "https://example.services.ai.azure.com/api/projects/p"));

        result.Stderr.ShouldContain("preview", customMessage: result.Diagnostic);
    }

    /// <summary>
    /// ...and the built-in substrate is supported, not preview, so it must stay silent — otherwise
    /// the notice becomes noise and stops being read.
    /// </summary>
    [Test]
    public void The_local_substrate_is_not_announced_as_preview()
    {
        var result = Run("check", _manifest, "--platform", "local", "--json");

        result.Stderr.ShouldNotContain("preview", customMessage: result.Diagnostic);
    }

    // ── the P1 regression guard ───────────────────────────────────────────────

    /// <summary>
    /// The assertion whose absence let the blocker ship: install an adapter the way a user does,
    /// then require that the CLI can actually resolve its platform.
    /// </summary>
    [Test]
    public void An_installed_adapter_resolves_for_deploy()
    {
        InstallAzureAdapterFixture();

        // --dry-run so nothing is created; the claim under test is resolution, not deployment.
        var result = Run(
            ["deploy", _manifest, "--platform", "azure", "--dry-run", "--json"],
            ("AZURE_AI_ENDPOINT", "https://example.services.ai.azure.com/api/projects/p"));

        result.Stdout.ShouldNotContain("No adapter registered", customMessage: result.Diagnostic);
    }

    /// <summary>
    /// <c>doctor</c> reported "All adapters healthy" while <c>deploy</c> could resolve none of them
    ///, because it asserted that a file loaded rather than that a platform
    /// resolved. An empty probe directory must therefore not read as healthy-with-adapters.
    /// </summary>
    [Test]
    public void Adapters_list_reports_the_probe_directory_it_actually_used()
    {
        var result = Run("adapters", "list", "--json");

        result.ExitCode.ShouldBe(0, result.Diagnostic);
        var reported = Json(result.Stdout).GetProperty("adaptersDirectory").GetString();

        // Pins the documented config root against the real one — the divergence behind
        // the guide used to send a Linux user to ~/.ananke/adapters while the code
        // resolves ~/.local/share/.ananke/adapters.
        reported.ShouldBe(ExpectedAdaptersDirectory());
        Json(result.Stdout).GetProperty("adapters").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// An adapter whose factory throws must not take the tool with it.
    /// </summary>
    /// <remarks>
    /// Adapter factories run third-party code and read configuration — the Google adapter throws
    /// outright when <c>GOOGLE_CLOUD_PROJECT</c> is unset. While module initializers never fired,
    /// no factory ran and this could not bite. The moment they did, one unconfigured adapter
    /// crashed <i>every</i> command with an unhandled exception, including read-only ones and
    /// including commands targeting a different platform.
    /// </remarks>
    [Test]
    public void An_adapter_whose_factory_throws_does_not_crash_the_cli()
    {
        InstallAdapterFixture("nnke-platform-google", "vertex-ai", "vertex-ai.adapter.json");

        // GOOGLE_CLOUD_PROJECT deliberately unset: this is the failure under test.
        var doctor = Run("adapters", "doctor", "--json");

        doctor.Stderr.ShouldNotContain("Unhandled exception", customMessage: doctor.Diagnostic);
        doctor.ExitCode.ShouldBe(2, doctor.Diagnostic);

        var issue = Json(doctor.Stdout).GetProperty("issues").EnumerateArray().Single();
        issue.GetProperty("id").GetString().ShouldBe("vertex-ai");
        issue.GetProperty("error").GetString()!.ShouldContain("GOOGLE_CLOUD_PROJECT");
    }

    /// <summary>
    /// And it must not block a command aimed at a different, working platform.
    /// </summary>
    [Test]
    public void A_broken_adapter_does_not_block_another_platform()
    {
        InstallAdapterFixture("nnke-platform-google", "vertex-ai", "vertex-ai.adapter.json");

        var result = Run("deploy", _manifest, "--platform", "local", "--json");

        result.ExitCode.ShouldBe(0, result.Diagnostic);
    }

    [Test]
    public void An_unknown_platform_fails_rather_than_succeeding_quietly()
    {
        var result = Run("deploy", _manifest, "--platform", "no-such-platform", "--json");

        result.ExitCode.ShouldNotBe(0, result.Diagnostic);
    }

    // ── harness ───────────────────────────────────────────────────────────────

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr)
    {
        /// <summary>Everything a failed assertion needs to be actionable without a re-run.</summary>
        public string Diagnostic => $"exit={ExitCode}\nstdout:\n{Stdout}\nstderr:\n{Stderr}";
    }

    private CliResult Run(params string[] args) => Run(args, []);

    private CliResult Run(string[] args, params (string Key, string Value)[] environment)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _home
        };
        start.ArgumentList.Add(CliDll());
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        // Redirect the config root so the probe directory and the deployment registry are this
        // test's, not the developer's. POSIX only; on Windows AnankePaths reads UserProfile.
        start.Environment["XDG_DATA_HOME"] = _home;
        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 60_000).ShouldBeTrue("the CLI did not exit within 60s");

        return new CliResult(process.ExitCode, stdout, stderr);
    }

    private static JsonElement Json(string stdout) => JsonDocument.Parse(stdout).RootElement;

    private string ExpectedAdaptersDirectory() =>
        Path.Combine(_home, ".ananke", "adapters");

    /// <summary>
    /// Reproduces what <c>nnke-platform-azure</c>'s installer does — copy its assemblies into the
    /// probe directory and write the manifest sidecar — so the test exercises the real install
    /// shape rather than a simulation of it.
    /// </summary>
    private void InstallAzureAdapterFixture() =>
        InstallAdapterFixture("nnke-platform-azure", "azure", "azure-ai.adapter.json");

    private void InstallAdapterFixture(string project, string adapterId, string manifestName)
    {
        var source = BuildOutput(project);
        var target = ExpectedAdaptersDirectory();
        Directory.CreateDirectory(target);

        foreach (var dll in Directory.EnumerateFiles(source, "*.dll"))
            File.Copy(dll, Path.Combine(target, Path.GetFileName(dll)), overwrite: true);

        File.WriteAllText(Path.Combine(target, manifestName), $$"""
            {
              "id": "{{adapterId}}",
              "displayName": "{{adapterId}} fixture",
              "version": "0.8.9",
              "minCliVersion": "0.8",
              "maxCliVersionExclusive": "1.0",
              "entryAssembly": "{{project}}.dll"
            }
            """);
    }

    private static string CliDll() => Path.Combine(BuildOutput("nnke-platform"), "nnke-platform.dll");

    /// <summary>
    /// Locates a project's build output for the configuration this test was built in.
    /// </summary>
    /// <remarks>
    /// Fails loudly rather than skipping when the output is missing. A harness that quietly does
    /// nothing when it cannot find its subject is the exact failure this fixture exists to prevent —
    /// and CI builds the whole solution before testing, so absence means something is wrong.
    /// </remarks>
    private static string BuildOutput(string project)
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar)))!;

        var path = Path.Combine(RepositoryRoot(), "src", project, "bin", configuration, "net10.0");
        Directory.Exists(path).ShouldBeTrue(
            $"Build output for '{project}' not found at '{path}'. Build the solution first: "
            + "dotnet build src/Ananke.slnx");

        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Ananke.slnx")))
            directory = directory.Parent;

        directory.ShouldNotBeNull("Could not locate the repository root from the test output directory.");
        return directory!.FullName;
    }
}
