using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Learning.Episodes;
using Ananke.Learning.Features;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Skills;

/// <summary>
/// JSON streaming implementation of <see cref="ISkillPackageFormat"/>.
/// Write path uses <see cref="Utf8JsonWriter"/> for true entry-by-entry streaming.
/// Read path uses <see cref="JsonDocument"/> with lazy <see cref="IAsyncEnumerable{T}"/>
/// iteration over entries and episodes.
/// </summary>
public sealed class JsonSkillPackageFormat : ISkillPackageFormat
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public ISkillPackageWriter CreateWriter(Stream output) =>
        new JsonSkillPackageWriter(output);

    /// <inheritdoc />
    public async Task<ISkillPackageReader> CreateReaderAsync(
        Stream input, CancellationToken ct = default)
    {
        var doc = await JsonDocument.ParseAsync(input, cancellationToken: ct);
        return new JsonSkillPackageReader(doc);
    }
}

internal sealed class JsonSkillPackageWriter(Stream output) : ISkillPackageWriter
{
    private readonly Utf8JsonWriter _writer = new(output, new JsonWriterOptions { Indented = true });
    private bool _entriesOpen;
    private bool _episodesOpen;
    private bool _hasEntries;
    private bool _hasEpisodes;

    public async Task WriteHeaderAsync(SkillPackageHeader header, CancellationToken ct = default)
    {
        _writer.WriteStartObject();
        _writer.WritePropertyName("header");
        JsonSerializer.Serialize(_writer, header, JsonSkillPackageFormat.SerializerOptions);
        await _writer.FlushAsync(ct);
    }

    public async Task WriteEntryAsync(EmpiricalEntry entry, CancellationToken ct = default)
    {
        if (!_entriesOpen)
        {
            _writer.WritePropertyName("entries");
            _writer.WriteStartArray();
            _entriesOpen = true;
        }

        _hasEntries = true;
        JsonSerializer.Serialize(_writer, entry, JsonSkillPackageFormat.SerializerOptions);
        await _writer.FlushAsync(ct);
    }

    public async Task WriteEpisodeAsync(Episode episode, CancellationToken ct = default)
    {
        EnsureEntriesClosed();

        if (!_episodesOpen)
        {
            _writer.WritePropertyName("episodes");
            _writer.WriteStartArray();
            _episodesOpen = true;
        }

        _hasEpisodes = true;
        JsonSerializer.Serialize(_writer, episode, JsonSkillPackageFormat.SerializerOptions);
        await _writer.FlushAsync(ct);
    }

    public async Task CompleteAsync(TrainingManifest manifest, CancellationToken ct = default)
    {
        EnsureEntriesClosed();
        EnsureEpisodesClosed();

        _writer.WritePropertyName("manifest");
        JsonSerializer.Serialize(_writer, manifest, JsonSkillPackageFormat.SerializerOptions);
        _writer.WriteEndObject();
        await _writer.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _writer.DisposeAsync();

    private void EnsureEntriesClosed()
    {
        if (_entriesOpen)
        {
            _writer.WriteEndArray();
            _entriesOpen = false;
        }
        else if (!_hasEntries)
        {
            _writer.WritePropertyName("entries");
            _writer.WriteStartArray();
            _writer.WriteEndArray();
            _hasEntries = true;
        }
    }

    private void EnsureEpisodesClosed()
    {
        if (_episodesOpen)
        {
            _writer.WriteEndArray();
            _episodesOpen = false;
        }
        else if (!_hasEpisodes)
        {
            _writer.WritePropertyName("episodes");
            _writer.WriteStartArray();
            _writer.WriteEndArray();
            _hasEpisodes = true;
        }
    }
}

internal sealed class JsonSkillPackageReader(JsonDocument document) : ISkillPackageReader
{
    public Task<SkillPackageHeader> ReadHeaderAsync(CancellationToken ct = default)
    {
        var header = document.RootElement.GetProperty("header")
            .Deserialize<SkillPackageHeader>(JsonSkillPackageFormat.SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize package header.");
        return Task.FromResult(header);
    }

    public async IAsyncEnumerable<EmpiricalEntry> ReadEntriesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!document.RootElement.TryGetProperty("entries", out var entries))
            yield break;

        foreach (var element in entries.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var entry = element.Deserialize<EmpiricalEntry>(JsonSkillPackageFormat.SerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize entry.");
            yield return entry;
        }
    }

    public async IAsyncEnumerable<Episode> ReadEpisodesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!document.RootElement.TryGetProperty("episodes", out var episodes))
            yield break;

        foreach (var element in episodes.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var episode = element.Deserialize<Episode>(JsonSkillPackageFormat.SerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize episode.");
            yield return episode;
        }
    }

    public Task<TrainingManifest> ReadManifestAsync(CancellationToken ct = default)
    {
        var manifest = document.RootElement.GetProperty("manifest")
            .Deserialize<TrainingManifest>(JsonSkillPackageFormat.SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize manifest.");
        return Task.FromResult(manifest);
    }

    public ValueTask DisposeAsync()
    {
        document.Dispose();
        return ValueTask.CompletedTask;
    }
}
