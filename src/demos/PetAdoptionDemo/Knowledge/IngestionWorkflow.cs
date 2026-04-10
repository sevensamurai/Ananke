using System.Text;
using System.Text.Json;
using Ananke.AspNetCore.Configuration;
using Ananke.Design;
using Ananke.Documents;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Qdrant;
using Qdrant.Client;

/// <summary>
/// Workflow state that flows through the ingestion pipeline.
/// Immutable record — each job returns a new instance with updated fields.
/// Stores are reference types shared across fork branches.
/// </summary>
internal sealed record IngestionState
{
    public required string DataPath { get; init; }
    public required ProviderProfile Settings { get; init; }
    public required IEmbeddingModel Embedder { get; init; }
    public required IKnowledgeStore KnowledgeStore { get; init; }
    public required IKnowledgeStore PetStore { get; init; }
    public required IKnowledgeCatalog Catalog { get; init; }
    public string[] AllFiles { get; init; } = [];
    public string? PetFile { get; init; }
    public string[] KnowledgeFiles { get; init; } = [];
    public int PetChunksIndexed { get; init; }
    public int KnowledgeChunksIndexed { get; init; }
}

/// <summary>
/// Models the knowledge ingestion pipeline as an <c>Ananke.Design</c> workflow
/// loaded from <c>ingestion.ananke.yml</c>.
/// Each job is a pure function over <see cref="IngestionState"/>.
/// </summary>
internal static class IngestionWorkflow
{
    /// <summary>
    /// Builds and runs the ingestion workflow, returning the final stores and embedder.
    /// </summary>
    internal static async Task<(IKnowledgeStore KnowledgeStore, IKnowledgeStore PetStore, IKnowledgeCatalog Catalog, IEmbeddingModel Embedder)> RunAsync(
        string dataPath, ProviderProfile settings, string qdrantHost = "localhost", int qdrantPort = 6334)
    {
        // ── Create Qdrant-backed stores ──────────────────────────────────
        var embedder = CreateEmbedder(settings);
        var qdrantClient = new QdrantClient(qdrantHost, qdrantPort);

        var knowledgeStore = new QdrantKnowledgeStore(qdrantClient, embedder, collectionName: "knowledge");
        var petStore = new QdrantKnowledgeStore(qdrantClient, embedder, collectionName: "pets");
        var catalog = new QdrantKnowledgeCatalog(qdrantClient, embedder);

        // ── Skip ingestion if Qdrant already has data ────────────────────
        var existing = await catalog.BrowseAsync();
        if (existing.Count > 0)
        {
            Console.WriteLine($"[Knowledge] ✅ Found {existing.Count} catalog entries in Qdrant — skipping ingestion");
            return (knowledgeStore, petStore, catalog, embedder);
        }

        // ── Load manifest ────────────────────────────────────────────────
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "ingestion.ananke.yml");
        var manifest = WorkflowManifest.Load(manifestPath);

        var classificationPrompt = manifest.Jobs["ingest_pets"].SystemPrompt
            ?? throw new InvalidOperationException("ingest_pets job is missing system_prompt in the manifest.");

        var (formatName, formatSchema) = ReadClassificationConfig(File.ReadAllLines(manifestPath));
        var classificationFormat = new AgentResponseFormat(formatName, formatSchema);

        // ── Build workflow from YAML topology ─────────────────────────────
        var initialState = new IngestionState
        {
            DataPath = dataPath,
            Settings = settings,
            Embedder = embedder,
            KnowledgeStore = knowledgeStore,
            PetStore = petStore,
            Catalog = catalog
        };

        var workflow = WorkflowScaffold.Parse<IngestionState>(manifest.Name, manifest.Connections)
            .Bind("scan_files", ScanFilesAsync)
            .Bind("ingest_pets", (state, ct) => IngestPetsAsync(state, classificationPrompt, classificationFormat, ct))
            .Bind("ingest_knowledge", IngestKnowledgeAsync)
            .Bind("summarize", SummarizeAsync)
            .BindMerge("summarize", MergeResults)
            .Build();

        var execution = await workflow.RunAsync(initialState);
        var final = execution.State;
        return (final.KnowledgeStore, final.PetStore, final.Catalog, final.Embedder);
    }

    // ─── Workflow jobs ───────────────────────────────────────────────

    /// <summary>Discovers markdown files and partitions them into pet vs knowledge paths.</summary>
    private static Task<IngestionState> ScanFilesAsync(IngestionState state, CancellationToken ct)
    {
        var allFiles = Directory.GetFiles(state.DataPath, "*.md");
        Console.WriteLine($"[Knowledge] Loading {allFiles.Length} file(s) from {state.DataPath}");

        string? petFile = null;
        var knowledgeFiles = new List<string>();

        foreach (var file in allFiles)
        {
            if (Path.GetFileName(file).Equals("available-pets.md", StringComparison.OrdinalIgnoreCase))
                petFile = file;
            else
                knowledgeFiles.Add(file);
        }

        return Task.FromResult(state with
        {
            AllFiles = allFiles,
            PetFile = petFile,
            KnowledgeFiles = [.. knowledgeFiles]
        });
    }

    /// <summary>Reads available-pets.md, classifies each paragraph with the LLM, and indexes into the pet store + catalog.</summary>
    private static async Task<IngestionState> IngestPetsAsync(
        IngestionState state, string classificationPrompt, AgentResponseFormat classificationFormat, CancellationToken ct)
    {
        if (state.PetFile is null) return state;

        var classifier = state.Settings.CreateAgentModel();
        var petDocs = await ClassifyPetsAsync(state.PetFile, classifier, classificationPrompt, classificationFormat);
        await state.PetStore.UpsertAsync(petDocs, ct);

        foreach (var doc in petDocs)
        {
            var name = doc.Metadata["pet_name"];
            var category = doc.Metadata["pet_category"];
            await state.Catalog.IndexAsync(new CatalogEntry
            {
                Source = name,
                Summary = doc.Text.Length > 120 ? doc.Text[..120] + "…" : doc.Text,
                Keywords = [name, category],
                Category = category,
                IndexedAt = DateTimeOffset.UtcNow,
                ChunkCount = 1
            });
            Console.WriteLine($"[Catalog]     • {name} ({category})");
        }

        var fileName = Path.GetFileName(state.PetFile);
        Console.WriteLine($"[Knowledge]   {fileName}: {petDocs.Count} pet entries indexed");
        return state with { PetChunksIndexed = petDocs.Count };
    }

    /// <summary>Extracts, chunks, and indexes general knowledge files into the knowledge store + catalog.</summary>
    private static async Task<IngestionState> IngestKnowledgeAsync(IngestionState state, CancellationToken ct)
    {
        var extractor = new MarkdownExtractor();
        var chunker = new SlidingWindowChunker();
        var totalChunks = 0;

        foreach (var file in state.KnowledgeFiles)
        {
            var fileName = Path.GetFileName(file);

            await using var stream = File.OpenRead(file);
            var extracted = await extractor.ExtractAsync(stream);
            var chunks = chunker.Chunk(extracted, new ChunkingOptions(MaxTokens: 256, OverlapTokens: 32));

            var knowledgeDocs = chunks.Select((c, i) => new KnowledgeDocument
            {
                Id = $"{fileName}:{i}",
                Text = c.Text,
                Metadata = new Dictionary<string, string>(c.Metadata)
                {
                    ["source"] = fileName
                }
            }).ToList();

            await state.KnowledgeStore.UpsertAsync(knowledgeDocs, ct);
            totalChunks += knowledgeDocs.Count;

            var firstChunkPreview = knowledgeDocs.Count > 0
                ? knowledgeDocs[0].Text.ReplaceLineEndings(" ")
                : fileName;
            if (firstChunkPreview.Length > 120) firstChunkPreview = firstChunkPreview[..120] + "…";

            await state.Catalog.IndexAsync(new CatalogEntry
            {
                Source = fileName,
                Summary = firstChunkPreview,
                Keywords = [Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ')],
                Category = "knowledge",
                IndexedAt = DateTimeOffset.UtcNow,
                ChunkCount = knowledgeDocs.Count
            });

            Console.WriteLine($"[Knowledge]   {fileName}: {extracted.Sections.Count} section(s) → {chunks.Count} chunks");
            Console.WriteLine($"[Catalog]     • {fileName} (knowledge)");
        }

        return state with { KnowledgeChunksIndexed = totalChunks };
    }

    /// <summary>Logs the final ingestion summary.</summary>
    private static Task<IngestionState> SummarizeAsync(IngestionState state, CancellationToken ct)
    {
        Console.WriteLine(
            $"[Knowledge] ✅ {state.KnowledgeChunksIndexed} knowledge chunks + " +
            $"{state.PetChunksIndexed} pet chunks from {state.AllFiles.Length} files");
        return Task.FromResult(state);
    }

    /// <summary>Merges the fork branches — combines chunk counts from both paths.</summary>
    private static IngestionState MergeResults(IngestionState[] branches)
    {
        var petBranch = branches.FirstOrDefault(b => b.PetChunksIndexed > 0) ?? branches[0];
        var knowledgeBranch = branches.FirstOrDefault(b => b.KnowledgeChunksIndexed > 0) ?? branches[0];

        return petBranch with
        {
            KnowledgeChunksIndexed = knowledgeBranch.KnowledgeChunksIndexed,
            KnowledgeFiles = knowledgeBranch.KnowledgeFiles
        };
    }

    // ─── Pet classification helpers ──────────────────────────────────

    /// <summary>
    /// Reads a pets markdown file, splits into paragraphs, and classifies each
    /// with the LLM using structured output.
    /// </summary>
    private static async Task<List<KnowledgeDocument>> ClassifyPetsAsync(
        string filePath, IAgentModel classifier, string classificationPrompt, AgentResponseFormat classificationFormat)
    {
        var docs = new List<KnowledgeDocument>();
        var petIndex = 0;
        var paragraph = new StringBuilder();
        var paragraphs = new List<string>();

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph, paragraphs);
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }

        FlushParagraph(paragraph, paragraphs);

        foreach (var text in paragraphs)
        {
            var (name, category) = await ClassifyWithLlmAsync(text, classifier, classificationPrompt, classificationFormat);
            if (name is null || category is null)
                continue;

            docs.Add(new KnowledgeDocument
            {
                Id = $"pet:{petIndex++}",
                Text = text,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "available-pets.md",
                    ["pet_name"] = name,
                    ["pet_category"] = category
                }
            });
        }

        return docs;
    }

    private static void FlushParagraph(StringBuilder paragraph, List<string> paragraphs)
    {
        if (paragraph.Length == 0) return;

        var text = paragraph.ToString();
        paragraph.Clear();

        if (text.StartsWith('#') || text.StartsWith("---"))
            return;

        paragraphs.Add(text);
    }

    private static async Task<(string? Name, string? Category)> ClassifyWithLlmAsync(
        string text, IAgentModel classifier, string classificationPrompt, AgentResponseFormat classificationFormat)
    {
        var request = new AgentRequest
        {
            SystemPrompt = classificationPrompt,
            Messages = [AgentMessage.User(text)],
            ResponseFormat = classificationFormat,
            StoreCompletions = false
        };

        var response = await classifier.GenerateAsync(request);
        if (response.Text is null) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(response.Text);
            var root = doc.RootElement;
            var name = root.TryGetProperty("pet_name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() : null;
            var category = root.TryGetProperty("pet_category", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() : null;
            return (name, category);
        }
        catch (JsonException)
        {
            Console.WriteLine($"[Knowledge] ⚠ Failed to parse classification for: {text[..Math.Min(60, text.Length)]}…");
            return (null, null);
        }
    }

    // ─── Manifest helpers ─────────────────────────────────────────────

    /// <summary>
    /// Reads the custom <c>classification:</c> section from the manifest lines.
    /// This section is not part of the standard <see cref="WorkflowManifest"/> schema.
    /// </summary>
    private static (string Name, string Schema) ReadClassificationConfig(string[] lines)
    {
        string? name = null;
        string? schema = null;
        var inSection = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            if (!char.IsWhiteSpace(line[0]))
            {
                if (line.StartsWith("classification:"))
                {
                    inSection = true;
                    continue;
                }
                if (inSection) break;
                continue;
            }

            if (!inSection) continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("format_name:"))
                name = trimmed["format_name:".Length..].Trim();
            else if (trimmed.StartsWith("format_schema:"))
                schema = trimmed["format_schema:".Length..].Trim();
        }

        return (
            name ?? throw new InvalidOperationException("classification.format_name missing from manifest."),
            schema ?? throw new InvalidOperationException("classification.format_schema missing from manifest.")
        );
    }

    // ─── Shared utilities ────────────────────────────────────────────

    private static IEmbeddingModel CreateEmbedder(ProviderProfile settings)
    {
        var embedder = settings.CreateEmbeddingModel();
        if (embedder is not null)
        {
            Console.WriteLine($"[Knowledge] Using {settings.Provider} embedding model: {settings.EmbeddingModel}");
            return embedder;
        }

        Console.WriteLine($"[Knowledge] Using InMemoryEmbedder (set {settings.Provider}:EmbeddingModel to use provider embeddings)");
        return new InMemoryEmbedder();
    }
}
