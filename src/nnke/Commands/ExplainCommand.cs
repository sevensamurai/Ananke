using Ananke.Tool.Diagnostics;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke explain &lt;code&gt;</c> — shows a detailed explanation of a
/// diagnostic error code with examples and fix guidance.
/// Supports <c>--json</c> for agent consumption.
/// </summary>
internal static class ExplainCommand
{
    public static Command Create()
    {
        var codeArg = new Argument<string?>("code")
        {
            Description = "Diagnostic code to explain (e.g. ANANKE_TOPO_003), or omit to list all codes.",
            DefaultValueFactory = _ => null,
        };

        var command = new Command("explain", "Show a detailed explanation of a diagnostic error code.")
        {
            codeArg
        };

        command.SetAction(parseResult =>
        {
            var code = parseResult.GetValue(codeArg);
            var json = parseResult.GetValue<bool>("--json");

            return code is null ? ExecuteList(json) : ExecuteExplain(code, json);
        });

        return command;
    }

    private static int ExecuteList(bool json)
    {
        var all = DiagnosticExplanations.All();

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                count = all.Count,
                codes = all.Select(e => new
                {
                    code = e.Code,
                    title = e.Title,
                    docsRef = e.DocsRef,
                }).ToList()
            });
            return 0;
        }

        Console.WriteLine("  Diagnostic Codes");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();

        foreach (var entry in all)
        {
            Console.WriteLine($"    {entry.Code,-25} {entry.Title}");
        }

        Console.WriteLine();
        Console.WriteLine("  Usage: nnke explain <code>");

        return 0;
    }

    private static int ExecuteExplain(string code, bool json)
    {
        var explanation = DiagnosticExplanations.Find(code);

        if (explanation is null)
        {
            if (json)
            {
                JsonOutput.Write(new
                {
                    status = "not_found",
                    code,
                    hint = "Run 'nnke explain' with no arguments to list all known codes."
                });
            }
            else
            {
                Console.Error.WriteLine($"  Unknown diagnostic code: {code}");
                Console.Error.WriteLine("  Run 'nnke explain' to list all known codes.");
            }
            return 1;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                code = explanation.Code,
                title = explanation.Title,
                description = explanation.Description.Trim(),
                badExample = explanation.BadExample.Trim(),
                fixExample = explanation.FixExample.Trim(),
                docsRef = explanation.DocsRef,
            });
            return 0;
        }

        Console.WriteLine($"  {explanation.Code} — {explanation.Title}");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();
        WriteIndented(explanation.Description);
        Console.WriteLine();
        Console.WriteLine("  Example of the problem:");
        Console.WriteLine();
        WriteIndented(explanation.BadExample);
        Console.WriteLine();
        Console.WriteLine("  Fix:");
        Console.WriteLine();
        WriteIndented(explanation.FixExample);
        Console.WriteLine();
        Console.WriteLine($"  Reference: {explanation.DocsRef}");

        return 0;
    }

    private static void WriteIndented(string text)
    {
        foreach (var line in text.Trim().Split('\n'))
            Console.WriteLine($"    {line.TrimEnd()}");
    }
}
