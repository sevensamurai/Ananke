using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Episodes;
using Ananke.Learning.Features;
using Ananke.Learning.Skills;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class SkillPackagingTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryEmpiricalMemory _memory = null!;
    private InMemoryEpisodeStore _episodeStore = null!;
    private SkillPackager _packager = null!;
    private JsonSkillPackageFormat _format = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
        _episodeStore = new InMemoryEpisodeStore();
        _packager = new SkillPackager();
        _format = new JsonSkillPackageFormat();
    }

    private async Task<EmpiricalEntry> CommitEntryAsync(
        string id,
        float strength = 0.5f,
        float confidence = 0.5f,
        int observationCount = 5,
        string? consolidatedInto = null,
        string? episodeId = null,
        int? stepIndex = null,
        IReadOnlyList<string>? tags = null,
        string? descriptionText = null)
    {
        return await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = tags ?? ["test-tag"],
            Source = "test",
            Description = SemanticDescription.FromText(descriptionText ?? $"entry {id} {Guid.NewGuid():N}"),
            Confidence = confidence,
            ObservationCount = observationCount,
            Evidence = [$"evidence-{id}"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Strength = strength,
            ConsolidatedInto = consolidatedInto,
            EpisodeId = episodeId,
            StepIndex = stepIndex
        });
    }

    private SkillExportOptions DefaultExportOptions(
        float minStrength = 0.3f,
        float minConfidence = 0.2f,
        int minObservations = 2) => new()
    {
        Name = "test-skill",
        Domain = "test",
        Version = "1.0.0",
        MinStrength = minStrength,
        MinConfidence = minConfidence,
        MinObservations = minObservations
    };

    private async Task<(SkillExportResult Export, MemoryStream Stream)> ExportToStreamAsync(
        SkillExportOptions? options = null,
        TagImportanceMap? tagImportances = null)
    {
        options ??= DefaultExportOptions();
        var stream = new MemoryStream();
        SkillExportResult result;
        await using (var writer = _format.CreateWriter(stream))
        {
            result = await _packager.ExportAsync(
                options, _memory, writer, _episodeStore, tagImportances);
        }

        stream.Position = 0;
        return (result, stream);
    }

    // ── Export: quality gates ─────────────────────────────────────

    [Test]
    public async Task ExportFiltersByStrength()
    {
        await CommitEntryAsync("strong", strength: 0.8f);
        await CommitEntryAsync("weak", strength: 0.1f);

        var (result, stream) = await ExportToStreamAsync(
            DefaultExportOptions(minStrength: 0.5f));

        result.EntriesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);

        entries.Count.ShouldBe(1);
        entries[0].Id.ShouldBe("strong");
    }

    [Test]
    public async Task ExportFiltersByConfidence()
    {
        await CommitEntryAsync("confident", confidence: 0.8f);
        await CommitEntryAsync("uncertain", confidence: 0.05f);

        var (result, stream) = await ExportToStreamAsync(
            DefaultExportOptions(minConfidence: 0.5f));

        result.EntriesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);

        entries.Count.ShouldBe(1);
        entries[0].Id.ShouldBe("confident");
    }

    [Test]
    public async Task ExportFiltersByObservations()
    {
        await CommitEntryAsync("frequent", observationCount: 10);
        await CommitEntryAsync("rare", observationCount: 1);

        var (result, stream) = await ExportToStreamAsync(
            DefaultExportOptions(minObservations: 5));

        result.EntriesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);

        entries.Count.ShouldBe(1);
        entries[0].Id.ShouldBe("frequent");
    }

    [Test]
    public async Task ExportOmitsConsolidatedEntries()
    {
        await CommitEntryAsync("active");
        await CommitEntryAsync("consolidated", consolidatedInto: "doc-1");

        var (result, stream) = await ExportToStreamAsync();

        result.EntriesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);

        entries.Count.ShouldBe(1);
        entries[0].Id.ShouldBe("active");
    }

    [Test]
    public async Task ExportIncludesLinkedEpisodes()
    {
        await CommitEntryAsync("e1", episodeId: "ep-1", stepIndex: 0);
        await CommitEntryAsync("e2");

        await _episodeStore.CommitAsync(new Episode
        {
            Id = "ep-1",
            Steps = [new EpisodeStep { StepIndex = 0, EntryId = "e1" }],
            TerminalReward = 1.0f,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        });

        var (result, stream) = await ExportToStreamAsync();

        result.EpisodesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var episodes = new List<Episode>();
        await foreach (var ep in reader.ReadEpisodesAsync())
            episodes.Add(ep);

        episodes.Count.ShouldBe(1);
        episodes[0].Id.ShouldBe("ep-1");
    }

    [Test]
    public async Task EmptyMemoryExportsEmptyPackage()
    {
        var (result, stream) = await ExportToStreamAsync();

        result.EntriesExported.ShouldBe(0);
        result.EpisodesExported.ShouldBe(0);

        await using var reader = await _format.CreateReaderAsync(stream);
        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);
        entries.Count.ShouldBe(0);

        var manifest = await reader.ReadManifestAsync();
        manifest.TotalEntries.ShouldBe(0);
        manifest.TotalEpisodes.ShouldBe(0);
    }

    // ── Import: options ──────────────────────────────────────────

    [Test]
    public async Task ImportAppliesStrengthScale()
    {
        await CommitEntryAsync("e1", strength: 0.8f);
        var (_, stream) = await ExportToStreamAsync();

        var freshMemory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
        await using var reader = await _format.CreateReaderAsync(stream);
        var result = await _packager.ImportAsync(reader, freshMemory,
            options: new SkillImportOptions { StrengthScale = 0.5f });

        result.EntriesAdded.ShouldBe(1);
        var imported = await freshMemory.GetAsync("e1");
        imported.ShouldNotBeNull();
        imported.Strength.ShouldBe(0.4f, tolerance: 0.001f);
    }

    [Test]
    public async Task ImportAppliesIdPrefix()
    {
        await CommitEntryAsync("e1");
        var (_, stream) = await ExportToStreamAsync();

        var freshMemory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
        await using var reader = await _format.CreateReaderAsync(stream);
        var result = await _packager.ImportAsync(reader, freshMemory,
            options: new SkillImportOptions { IdPrefix = "imp_" });

        result.EntriesAdded.ShouldBe(1);
        var imported = await freshMemory.GetAsync("imp_e1");
        imported.ShouldNotBeNull();
    }

    [Test]
    public async Task ImportAddOnlySkipsDuplicates()
    {
        await CommitEntryAsync("e1");
        var (_, stream) = await ExportToStreamAsync();

        // Target memory already has entry with same ID
        var freshMemory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
        await freshMemory.CommitAsync(new EmpiricalEntry
        {
            Id = "e1",
            Kind = EmpiricalKind.Pattern,
            Tags = ["existing"],
            Source = "existing",
            Description = SemanticDescription.FromText($"existing entry {Guid.NewGuid():N}"),
            Confidence = 0.9f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        await using var reader = await _format.CreateReaderAsync(stream);
        var result = await _packager.ImportAsync(reader, freshMemory,
            options: new SkillImportOptions { Mode = SkillImportMode.AddOnly });

        result.EntriesSkipped.ShouldBe(1);
        result.EntriesAdded.ShouldBe(0);
    }

    [Test]
    public async Task ImportMergeDedups()
    {
        // Source: entry with fixed description (no Guid) so embeddings match
        var sharedText = "shared description for merge testing";
        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = "src-1",
            Kind = EmpiricalKind.Pattern,
            Tags = ["test"],
            Source = "test",
            Description = SemanticDescription.FromText(sharedText),
            Confidence = 0.5f,
            ObservationCount = 5,
            Evidence = ["ev-src"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Strength = 0.8f
        });

        var (_, stream) = await ExportToStreamAsync();

        // Target has entry with same description text -> same embedding -> dedup
        var targetMemory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 0.9f);
        await targetMemory.CommitAsync(new EmpiricalEntry
        {
            Id = "existing-1",
            Kind = EmpiricalKind.Pattern,
            Tags = ["test"],
            Source = "existing",
            Description = SemanticDescription.FromText(sharedText),
            Confidence = 0.5f,
            ObservationCount = 3,
            Evidence = ["ev-existing"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Strength = 0.5f
        });

        await using var reader = await _format.CreateReaderAsync(stream);
        var result = await _packager.ImportAsync(reader, targetMemory);

        result.EntriesMerged.ShouldBe(1);
        result.EntriesAdded.ShouldBe(0);
    }

    // ── JSON round-trip ──────────────────────────────────────────

    [Test]
    public async Task JsonRoundTrip()
    {
        await CommitEntryAsync("e1", episodeId: "ep-1", stepIndex: 0, strength: 0.7f);
        await CommitEntryAsync("e2", episodeId: "ep-1", stepIndex: 1, strength: 0.6f);

        await _episodeStore.CommitAsync(new Episode
        {
            Id = "ep-1",
            Steps =
            [
                new EpisodeStep { StepIndex = 0, EntryId = "e1" },
                new EpisodeStep { StepIndex = 1, EntryId = "e2" }
            ],
            TerminalReward = 1.0f,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow
        });

        var (exportResult, stream) = await ExportToStreamAsync();

        exportResult.EntriesExported.ShouldBe(2);
        exportResult.EpisodesExported.ShouldBe(1);

        await using var reader = await _format.CreateReaderAsync(stream);
        var header = await reader.ReadHeaderAsync();
        header.Name.ShouldBe("test-skill");
        header.Domain.ShouldBe("test");

        var entries = new List<EmpiricalEntry>();
        await foreach (var e in reader.ReadEntriesAsync())
            entries.Add(e);
        entries.Count.ShouldBe(2);

        var episodes = new List<Episode>();
        await foreach (var ep in reader.ReadEpisodesAsync())
            episodes.Add(ep);
        episodes.Count.ShouldBe(1);

        var manifest = await reader.ReadManifestAsync();
        manifest.TotalEntries.ShouldBe(2);
        manifest.TotalEpisodes.ShouldBe(1);
    }

    [Test]
    public async Task JsonRoundTripWithNulls()
    {
        await CommitEntryAsync("e1");
        var (exportResult, stream) = await ExportToStreamAsync();

        exportResult.EntriesExported.ShouldBe(1);
        exportResult.EpisodesExported.ShouldBe(0);

        await using var reader = await _format.CreateReaderAsync(stream);

        var header = await reader.ReadHeaderAsync();
        header.TagImportances.ShouldBeNull();
        header.Description.ShouldBeNull();

        var episodes = new List<Episode>();
        await foreach (var ep in reader.ReadEpisodesAsync())
            episodes.Add(ep);
        episodes.Count.ShouldBe(0);
    }

    [Test]
    public async Task ImportIntoFreshMemoryWorks()
    {
        await CommitEntryAsync("e1", strength: 0.7f);
        await CommitEntryAsync("e2", strength: 0.6f);
        await CommitEntryAsync("e3", strength: 0.8f);

        var (_, stream) = await ExportToStreamAsync();

        var freshMemory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
        await using var reader = await _format.CreateReaderAsync(stream);
        var result = await _packager.ImportAsync(reader, freshMemory);

        result.EntriesAdded.ShouldBe(3);
        result.EntriesMerged.ShouldBe(0);
        result.EntriesSkipped.ShouldBe(0);

        (await freshMemory.GetAsync("e1")).ShouldNotBeNull();
        (await freshMemory.GetAsync("e2")).ShouldNotBeNull();
        (await freshMemory.GetAsync("e3")).ShouldNotBeNull();
    }
}
