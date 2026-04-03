using Ananke.Skills.OpenClaw;
using Shouldly;

namespace Ananke.Skills.Tests;

[TestFixture]
public class OpenClawCatalogTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ananke-skills-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SkillDescriptor AirbnbSkill => new()
    {
        Id = "stveenli/airbnb",
        Name = "airbnb-search",
        Description = "Search Airbnb listings with prices, ratings, and direct links. No API key required.",
        Tags = ["travel", "airbnb", "lodging", "search"],
        Homepage = "https://github.com/Olafs-World/airbnb-search",
        Install = SkillInstallMethod.Uvx,
        InstallPackage = "airbnb-search",
        ExtraCliArgs = "--json",
        Parameters =
        [
            new("query", "Location to search (e.g. 'Steamboat Springs, CO')", IsRequired: true, IsPositional: true),
            new("checkin", "Check-in date (YYYY-MM-DD)"),
            new("checkout", "Check-out date (YYYY-MM-DD)"),
            new("min-price", "Minimum price per night"),
            new("max-price", "Maximum price per night"),
            new("min-bedrooms", "Minimum number of bedrooms"),
            new("limit", "Max results to return (default: 20)")
        ]
    };

    private static SkillDescriptor WeatherSkill => new()
    {
        Id = "demo/weather",
        Name = "weather-cli",
        Description = "Get current weather for a location.",
        Tags = ["weather", "forecast"],
        Install = SkillInstallMethod.Uvx
    };

    // --- SyncAsync ---

    [Test]
    public async Task SyncAsync_CreatesEmptyCatalogFile()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.SyncAsync();

        File.Exists(Path.Combine(_tempDir, "catalog.json")).ShouldBeTrue();
    }

    [Test]
    public async Task SyncAsync_LoadsExistingCatalog()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill]);

        // Create a new catalog pointing at the same dir — should load persisted data
        var catalog2 = new OpenClawCatalog(_tempDir);
        await catalog2.SyncAsync();

        var results = await catalog2.SearchAsync("airbnb");
        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("airbnb-search");
    }

    // --- AddSkillsAsync ---

    [Test]
    public async Task AddSkillsAsync_PersistsSkills()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill, WeatherSkill]);

        var results = await catalog.SearchAsync("airbnb");
        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("stveenli/airbnb");
    }

    [Test]
    public async Task AddSkillsAsync_UpsertsDuplicates()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill]);
        await catalog.AddSkillsAsync([AirbnbSkill]); // upsert — same ID

        var results = await catalog.SearchAsync("airbnb search lodging travel");
        results.Count.ShouldBe(1);
    }

    [Test]
    public async Task AddSkillsAsync_UpsertsUpdatedDescriptor()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill]);

        var updated = AirbnbSkill with { Description = "Updated description for Airbnb search" };
        await catalog.AddSkillsAsync([updated]);

        var results = await catalog.SearchAsync("airbnb");
        results.Count.ShouldBe(1);
        results[0].Description.ShouldBe("Updated description for Airbnb search");
    }

    // --- SearchAsync ---

    [Test]
    public async Task SearchAsync_MatchesByName()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill, WeatherSkill]);

        var results = await catalog.SearchAsync("weather");
        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("weather-cli");
    }

    [Test]
    public async Task SearchAsync_MatchesByTags()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill, WeatherSkill]);

        var results = await catalog.SearchAsync("lodging");
        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("stveenli/airbnb");
    }

    [Test]
    public async Task SearchAsync_ReturnsEmptyForNoMatch()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill]);

        var results = await catalog.SearchAsync("quantum computing");
        results.ShouldBeEmpty();
    }

    [Test]
    public async Task SearchAsync_RespectsLimit()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill, WeatherSkill]);

        var results = await catalog.SearchAsync("search weather airbnb lodging forecast", limit: 1);
        results.Count.ShouldBe(1);
    }

    [Test]
    public async Task SearchAsync_TagFilterNarrowsResults()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        await catalog.AddSkillsAsync([AirbnbSkill, WeatherSkill]);

        var results = await catalog.SearchAsync("search", tags: ["travel"]);
        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("stveenli/airbnb");
    }

    // --- ResolveAsync ---

    [Test]
    public async Task ResolveAsync_ProducesToolDefinition()
    {
        var catalog = new OpenClawCatalog(_tempDir);

        var tool = await catalog.ResolveAsync(AirbnbSkill);

        tool.Name.ShouldBe("airbnb_search");
        tool.Description.ShouldBe(AirbnbSkill.Description);
        tool.Parameters.Count.ShouldBe(AirbnbSkill.Parameters.Count);
        tool.Requires.Count.ShouldBe(1);
        tool.Requires[0].Name.ShouldBe("uvx");
        tool.Tags.ShouldContain("travel");
    }

    [Test]
    public async Task ResolveAsync_SkillWithNoParams_HasQueryParameter()
    {
        var catalog = new OpenClawCatalog(_tempDir);

        var tool = await catalog.ResolveAsync(WeatherSkill);

        tool.Parameters.Count.ShouldBe(1);
        tool.Parameters[0].Name.ShouldBe("query");
    }

    [Test]
    public async Task ResolveAsync_NpxSkill_UsesNpxRunner()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        var npxSkill = new SkillDescriptor
        {
            Id = "demo/node-tool",
            Name = "node-tool",
            Description = "A node tool",
            Install = SkillInstallMethod.Npx
        };

        var tool = await catalog.ResolveAsync(npxSkill);

        tool.Requires[0].Name.ShouldBe("npx");
    }

    [Test]
    public void ResolveAsync_UnsupportedInstall_Throws()
    {
        var catalog = new OpenClawCatalog(_tempDir);
        var dockerSkill = new SkillDescriptor
        {
            Id = "demo/docker-tool",
            Name = "docker-tool",
            Description = "A docker tool",
            Install = SkillInstallMethod.Docker
        };

        Should.ThrowAsync<NotSupportedException>(() => catalog.ResolveAsync(dockerSkill));
    }

    // --- Scoring integration ---

    [Test]
    public async Task SearchAsync_WithScoreStore_EnrichesScores()
    {
        var scoreStore = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));
        await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Up);
        await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Up);

        var catalog = new OpenClawCatalog(_tempDir, scoreStore);
        await catalog.AddSkillsAsync([AirbnbSkill]);

        var results = await catalog.SearchAsync("airbnb");
        results[0].Score!.Net.ShouldBe(2);
    }

    [Test]
    public async Task SearchAsync_NegativeScore_StillReturnedForRanking()
    {
        // Negative scores rank skills last but never exclude them from results
        var scoreStore = new JsonFileScoreStore(Path.Combine(_tempDir, "scores.json"));
        await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Down);
        await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Down);

        var catalog = new OpenClawCatalog(_tempDir, scoreStore);
        await catalog.AddSkillsAsync([AirbnbSkill]);

        var results = await catalog.SearchAsync("airbnb");
        results.Count.ShouldBe(1);
        results[0].Score!.Net.ShouldBe(-2);
    }
}
