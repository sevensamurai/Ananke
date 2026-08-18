using Ananke.Tool.Commands;
using Ananke.Tool.Shared;
using Shouldly;

namespace Ananke.Tool.Tests;

/// <summary>
/// Pins Q8 (B1-2): <c>nnke new quickstart</c> must report a clean diagnostic for an invalid
/// project name, not let a raw .NET exception (and its stack trace) reach the user, and must
/// not write outside the requested directory. Originally fixed 2026-08-03 by validating
/// <c>name</c> against <see cref="Path.GetInvalidFileNameChars"/>, verified only by running
/// the built binary against the repro (<c>nnke new quickstart 'bad&lt;&gt;|name'</c>) — which
/// was a no-op on Linux (that API returns 2 characters there, 41 on Windows) and let
/// <c>".."</c> through on every platform, writing into the parent directory. Re-fixed
/// 2026-08-10 with <see cref="ProjectNameValidator"/>, an explicit allowlist that behaves the
/// same on every platform.
/// </summary>
[TestFixture]
public class ScaffoldCommandTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp() => _tempDir = Directory.CreateTempSubdirectory("nnke-scaffold-tests-").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task NewQuickstart_InvalidName_ExitsOne_ReportsDiagnostic_NoException()
    {
        // Exact B1-2 repro. If Program.cs's top-level try/catch is the only thing standing
        // between this and a raw stack trace, InvokeAsync itself would surface that as a
        // non-throwing exit code (System.CommandLine's default exception handler), not an
        // exception out of this call — asserting no throw plus the diagnostic covers both.
        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            NewQuickstartCommand.Create(), "quickstart", "bad<>|name", "--output", _tempDir);

        exitCode.ShouldBe(1);
        stdErr.ShouldContain("ANANKE_IO_002");
        stdErr.ShouldContain("Invalid project name");
    }

    [Test]
    public async Task NewQuickstart_InvalidName_DoesNotCreateOutputDirectory()
    {
        var target = Path.Combine(_tempDir, "should-not-exist");

        await CliTestHost.RunAsync(
            NewQuickstartCommand.Create(), "quickstart", "bad<>|name", "--output", target);

        Directory.Exists(target).ShouldBeFalse();
    }

    [Test]
    public async Task NewQuickstart_ValidName_ExitsZero_CreatesProjectFiles()
    {
        var target = Path.Combine(_tempDir, "my-project");

        var (exitCode, stdOut, _) = await CliTestHost.RunAsync(
            NewQuickstartCommand.Create(), "quickstart", "my-project", "--output", target);

        exitCode.ShouldBe(0);
        stdOut.ShouldContain("Created quickstart project");
        File.Exists(Path.Combine(target, "my-project.csproj")).ShouldBeTrue();
        File.Exists(Path.Combine(target, "Program.cs")).ShouldBeTrue();
    }

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("has/slash")]
    public async Task NewQuickstart_TraversalOrSeparatorName_ExitsOne_DoesNotCreateOutputDirectory(string name)
    {
        var target = Path.Combine(_tempDir, "should-not-exist");

        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            NewQuickstartCommand.Create(), "quickstart", name, "--output", target);

        exitCode.ShouldBe(1);
        stdErr.ShouldContain("ANANKE_IO_002");
        Directory.Exists(target).ShouldBeFalse();
    }

    [Test]
    public async Task NewQuickstart_ParentTraversalName_NoOutputOption_WritesNothingToParentDirectory()
    {
        // The actual R1(3) repro: with no --output, projectDir is
        // Path.Combine(CurrentDirectory, name). The pre-2026-08-10 guard let ".." through on
        // every platform (Path.GetInvalidFileNameChars() never rejects '.'), so the scaffolder
        // wrote its files into the parent of the current directory and reported success.
        var innerDir = Directory.CreateDirectory(Path.Combine(_tempDir, "inner")).FullName;
        var originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(innerDir);
        try
        {
            var before = Directory.GetFileSystemEntries(_tempDir).OrderBy(e => e).ToArray();

            var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
                NewQuickstartCommand.Create(), "quickstart", "..");

            exitCode.ShouldBe(1);
            stdErr.ShouldContain("ANANKE_IO_002");
            Directory.GetFileSystemEntries(_tempDir).OrderBy(e => e).ShouldBe(before);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    [Test]
    public async Task NewChatbox_InvalidName_ExitsOne_DoesNotCreateOutputDirectory()
    {
        var target = Path.Combine(_tempDir, "should-not-exist");

        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            NewChatboxCommand.Create(), "chatbox", "bad<>|name", "--output", target);

        exitCode.ShouldBe(1);
        stdErr.ShouldContain("ANANKE_IO_002");
        Directory.Exists(target).ShouldBeFalse();
    }

    [Test]
    public async Task NewWorkflow_InvalidName_ExitsOne_DoesNotCreateOutputDirectory()
    {
        var target = Path.Combine(_tempDir, "should-not-exist");

        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            NewWorkflowCommand.Create(), "workflow", "bad<>|name", "--output", target);

        exitCode.ShouldBe(1);
        stdErr.ShouldContain("ANANKE_IO_002");
        Directory.Exists(target).ShouldBeFalse();
    }
}
