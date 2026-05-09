using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform apoptosis [--auto] &lt;file&gt;</c> — reads prune
/// verdicts from the idle/aged healing policies and prints (or executes) teardown.
/// Dry-run by default; <c>--auto</c> would call <c>IFederationDeployer.TeardownAsync</c>
/// once live deployer wiring is available (v0.9).
/// </summary>
internal static class ApoptosisCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file."
        };

        var autoOption = new Option<bool>("--auto")
        {
            Description = "Execute teardown for prunable cells. Without this flag the command is a dry-run."
        };

        var idleMinutesOption = new Option<int>("--idle-minutes")
        {
            Description = "Minutes since snapshot before a cell is considered idle (default: 15).",
            DefaultValueFactory = _ => 15
        };

        var maxAgeDaysOption = new Option<int>("--max-age-days")
        {
            Description = "Maximum cell age in days; cells without birth metadata are ignored (default: 7).",
            DefaultValueFactory = _ => 7
        };

        var command = new Command("apoptosis",
            "Identify idle or aged cells eligible for teardown. Dry-run unless --auto is passed.")
        {
            fileArg,
            autoOption,
            idleMinutesOption,
            maxAgeDaysOption
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var auto = parseResult.GetValue(autoOption);
            var idleMinutes = parseResult.GetValue(idleMinutesOption);
            var maxAgeDays = parseResult.GetValue(maxAgeDaysOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(file, auto, idleMinutes, maxAgeDays, json);
        });

        return command;
    }

    private static void Execute(FileInfo file, bool auto, int idleMinutes, int maxAgeDays, bool json)
    {
        if (!file.Exists)
        {
            if (json) JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else Console.Error.WriteLine($"  File not found: {file.FullName}");
            return;
        }

        HostSnapshot snapshot;
        try { snapshot = HostSnapshotExporter.FromYaml(File.ReadAllText(file.FullName)); }
        catch (Exception ex)
        {
            if (json) JsonOutput.Write(new { status = "error", message = ex.Message });
            else Console.Error.WriteLine($"  Failed to parse snapshot: {ex.Message}");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var idleThreshold = TimeSpan.FromMinutes(idleMinutes);
        var maxAge = TimeSpan.FromDays(maxAgeDays);

        var candidates = snapshot.Cells
            .Select(cell =>
            {
                // Idle: treat time since snapshot as lower-bound idle time
                var timeSinceSnapshot = now - snapshot.TakenAt;
                var idleReason = timeSinceSnapshot >= idleThreshold
                    ? $"idle ≥ {idleMinutes} min since snapshot"
                    : null;

                // Aged: cells split from an ancestor may have a calculable minimum age
                // from the earliest division record. Without live lineage we use division history.
                var firstDiv = snapshot.DivisionHistory
                    .Where(d => d.Children.Contains(cell.Name, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(d => d.OccurredAt)
                    .FirstOrDefault();

                var bornAt = firstDiv?.OccurredAt ?? snapshot.TakenAt;
                var age = now - bornAt;
                var agedReason = age >= maxAge
                    ? $"age {age.TotalDays:F1}d ≥ max {maxAgeDays}d"
                    : null;

                var reason = idleReason ?? agedReason;
                return (cell, reason);
            })
            .Where(x => x.reason is not null)
            .ToList();

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                dryRun = !auto,
                idleThresholdMinutes = idleMinutes,
                maxAgeDays,
                candidateCount = candidates.Count,
                candidates = candidates.Select(x => new
                {
                    name = x.cell.Name,
                    domain = x.cell.Domain,
                    reason = x.reason
                }),
                note = auto
                    ? "TeardownAsync requires a live IFederationDeployer (available in v0.9)."
                    : "Pass --auto to execute teardown (requires v0.9 deployer wiring)."
            });
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  Apoptosis scan  [{(auto ? "AUTO" : "DRY-RUN")}]");
        Console.WriteLine($"  Idle threshold: {idleMinutes} min  |  Max age: {maxAgeDays} days");
        Console.WriteLine();

        if (candidates.Count == 0)
        {
            Console.WriteLine("  ✓ No cells eligible for apoptosis.");
        }
        else
        {
            Console.WriteLine($"  {candidates.Count} cell(s) eligible for teardown:");
            foreach (var (cell, reason) in candidates)
                Console.WriteLine($"    ● {cell.Name}  [{cell.Domain}]  — {reason}");

            Console.WriteLine();
            if (auto)
                Console.WriteLine("  ⚠  --auto passed but live IFederationDeployer not wired in v0.8.0 (available in v0.9).");
            else
                Console.WriteLine("  Pass --auto to execute teardown (requires v0.9 deployer wiring).");
        }
        Console.WriteLine();
    }
}
