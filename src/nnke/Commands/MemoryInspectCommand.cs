using Ananke.Learning.EmpiricalMemory;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke colony memory inspect [--cell &lt;name&gt;] &lt;file&gt;</c> — browses
/// entries from an exported empirical memory JSON file. Supports filtering by
/// cell (entity) and <c>--json</c> for agent pipelines.
/// </summary>
/// <remarks>
/// The memory file is produced by serialising an <see cref="InMemoryEmpiricalMemory"/>
/// instance using <see cref="System.Text.Json.JsonSerializer"/>. At runtime you can
/// export it with: <c>File.WriteAllText("memory.json", JsonSerializer.Serialize(memory.Export()));</c>
/// </remarks>
internal static class MemoryInspectCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to an exported empirical memory JSON file."
        };

        var cellOption = new Option<string?>("--cell")
        {
            Description = "Filter entries by cell/entity name."
        };

        var kindOption = new Option<string?>("--kind")
        {
            Description = "Filter by knowledge kind: pattern, skill, or heuristic."
        };

        var topOption = new Option<int>("--top")
        {
            Description = "Maximum number of entries to display (default: 20).",
            DefaultValueFactory = _ => 20
        };

        var command = new Command("inspect",
            "Browse entries from an exported empirical memory file.")
        {
            fileArg,
            cellOption,
            kindOption,
            topOption
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var cell = parseResult.GetValue(cellOption);
            var kind = parseResult.GetValue(kindOption);
            var top = parseResult.GetValue(topOption);
            var json = parseResult.GetValue<bool>("--json");
            return Execute(file, cell, kind, top, json);
        });

        return command;
    }

    private static int Execute(FileInfo file, string? cell, string? kind, int top, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return 1;
        }

        IReadOnlyList<EmpiricalEntry> entries;
        try
        {
            var text = File.ReadAllText(file.FullName);
            entries = JsonSerializer.Deserialize<List<EmpiricalEntry>>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse memory file: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse memory file: {ex.Message}");
            return 1;
        }

        // Apply filters
        var filtered = entries.AsEnumerable();

        if (cell is not null)
            filtered = filtered.Where(e => e.EntityId?.Equals(cell, StringComparison.OrdinalIgnoreCase) == true);

        if (kind is not null)
        {
            if (!Enum.TryParse<EmpiricalKind>(kind, ignoreCase: true, out var parsedKind))
            {
                if (json)
                    JsonOutput.Write(new { status = "error", message = $"Unknown kind '{kind}'. Valid: pattern, skill, heuristic." });
                else
                    Console.Error.WriteLine($"  Unknown kind '{kind}'. Valid: pattern, skill, heuristic.");
                return 1;
            }
            filtered = filtered.Where(e => e.Kind == parsedKind);
        }

        var page = filtered
            .OrderByDescending(e => e.Confidence)
            .Take(top)
            .ToList();

        var totalCount = filtered.Count();

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                total = totalCount,
                showing = page.Count,
                entries = page.Select(e => new
                {
                    id = e.Id,
                    kind = e.Kind.ToString().ToLowerInvariant(),
                    entityId = e.EntityId,
                    tags = e.Tags,
                    confidence = e.Confidence,
                    observations = e.ObservationCount,
                    summary = e.Description.Summary,
                    firstObserved = e.FirstObserved,
                    lastObserved = e.LastObserved,
                    source = e.Source
                }).ToList()
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Memory: {totalCount} entries  (showing top {page.Count})");
        if (cell is not null) Console.WriteLine($"  Filter: cell={cell}");
        if (kind is not null) Console.WriteLine($"  Filter: kind={kind}");
        Console.WriteLine();

        foreach (var e in page)
        {
            var tags = e.Tags.Count > 0 ? $"  [{string.Join(", ", e.Tags.Take(4))}]" : "";
            var entity = e.EntityId is not null ? $"  entity:{e.EntityId}" : "";
            Console.WriteLine($"  ▸ [{e.Kind.ToString().ToLowerInvariant()}] {e.Description.Summary ?? e.Id}");
            Console.WriteLine($"    confidence={e.Confidence:F2}  observations={e.ObservationCount}{tags}{entity}");
        }
        Console.WriteLine();

        return 0;
    }
}
