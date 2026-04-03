using Ananke.Orchestration.Tools;
using Ananke.Skills.OpenClaw;
using Shouldly;

namespace Ananke.Skills.Tests;

[TestFixture]
public class ToolKitSkillExtensionsTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ananke-ext-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task AddFromCatalogAsync_AddsMatchingSkills()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([
            new SkillDescriptor
            {
                Id = "test/tool-a",
                Name = "tool-a",
                Description = "A test tool for searching things",
                Tags = ["search"]
            }
        ]);

        var toolkit = await new ToolKit("test")
            .AddFromCatalogAsync(catalog, "search");

        toolkit.Tools.Count.ShouldBe(1);
        toolkit.Tools.ShouldContainKey("tool_a");
    }

    [Test]
    public async Task AddFromCatalogAsync_NegativeScoresStillResolved_RankedLast()
    {
        var scoreStore = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));
        for (var i = 0; i < 6; i++)
            await scoreStore.RecordVoteAsync("test/bad-tool", VoteDirection.Down);

        var catalog = new OpenClawCatalog(_tempDir, scoreStore);
        await catalog.AddSkillsAsync([
            new SkillDescriptor
            {
                Id = "test/bad-tool",
                Name = "bad-tool",
                Description = "A tool that always fails at searching",
                Tags = ["search"]
            },
            new SkillDescriptor
            {
                Id = "test/good-tool",
                Name = "good-tool",
                Description = "A reliable search tool",
                Tags = ["search"]
            }
        ]);

        var toolkit = await new ToolKit("test")
            .AddFromCatalogAsync(catalog, "search");

        // Both tools are resolved — negative scores rank last but don't gate
        toolkit.Tools.Count.ShouldBe(2);
        toolkit.Tools.ShouldContainKey("good_tool");
        toolkit.Tools.ShouldContainKey("bad_tool");
    }

    [Test]
    public async Task AddFromCatalogAsync_RespectsLimit()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([
            new SkillDescriptor { Id = "t/a", Name = "tool-a", Description = "Search A", Tags = ["search"] },
            new SkillDescriptor { Id = "t/b", Name = "tool-b", Description = "Search B", Tags = ["search"] },
            new SkillDescriptor { Id = "t/c", Name = "tool-c", Description = "Search C", Tags = ["search"] }
        ]);

        var toolkit = await new ToolKit("test")
            .AddFromCatalogAsync(catalog, "search", limit: 2);

        toolkit.Tools.Count.ShouldBe(2);
    }

    [Test]
    public async Task AddFromCatalogAsync_NoMatches_ToolKitRemainsEmpty()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([
            new SkillDescriptor { Id = "t/a", Name = "tool-a", Description = "A tool", Tags = ["cooking"] }
        ]);

        var toolkit = await new ToolKit("test")
            .AddFromCatalogAsync(catalog, "quantum physics");

        toolkit.Tools.ShouldBeEmpty();
    }
}
