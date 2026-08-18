using Ananke.Tool.Commands;
using Shouldly;

namespace Ananke.Tool.Tests;

/// <summary>
/// Pins Q1 (B1-1): <c>nnke validate</c> must exit non-zero on a handled failure, not just
/// report the failure and return 0. Fixed 2026-08-03 by converting <c>Execute</c> to return
/// <c>int</c> via a <c>Func&lt;ParseResult, int&gt;</c> <c>SetAction</c>; verified only by
/// running the built binary at the time, with no automated regression test until now.
/// </summary>
[TestFixture]
public class ValidateCommandTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp() => _tempDir = Directory.CreateTempSubdirectory("nnke-validate-tests-").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task Validate_NonexistentFile_ExitsOne()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.ananke.yml");

        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            ValidateCommand.Create(), "validate", missing);

        exitCode.ShouldBe(1);
        stdErr.ShouldContain("File not found");
    }

    [Test]
    public async Task Validate_UndefinedModelAlias_ExitsTwo()
    {
        var file = WriteManifest("""
            name: bad-workflow
            jobs:
              step:
                type: agent
                model: undefined-alias
            connections:
              - step -> End
            """);

        var (exitCode, _, stdErr) = await CliTestHost.RunAsync(
            ValidateCommand.Create(), "validate", file);

        exitCode.ShouldBe(2);
        stdErr.ShouldContain("undefined-alias");
    }

    [Test]
    public async Task Validate_UndefinedModelAlias_JsonMode_ExitsTwo_ReportsErrorStatus()
    {
        var file = WriteManifest("""
            name: bad-workflow
            jobs:
              step:
                type: agent
                model: undefined-alias
            connections:
              - step -> End
            """);

        var (exitCode, stdOut, _) = await CliTestHost.RunAsync(
            ValidateCommand.Create(), "validate", file, "--json");

        exitCode.ShouldBe(2);
        stdOut.ShouldContain("\"status\": \"error\"");
    }

    [Test]
    public async Task Validate_ValidManifest_ExitsZero()
    {
        var file = WriteManifest("""
            name: good-workflow
            jobs:
              step:
                type: code
            connections:
              - step -> End
            """);

        var (exitCode, stdOut, _) = await CliTestHost.RunAsync(
            ValidateCommand.Create(), "validate", file);

        exitCode.ShouldBe(0);
        stdOut.ShouldContain("Manifest is valid");
    }

    private string WriteManifest(string yaml)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.ananke.yml");
        File.WriteAllText(path, yaml);
        return path;
    }
}
