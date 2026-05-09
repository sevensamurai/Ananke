using Ananke.Federation.Deployment;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform status [--deployment-id &lt;id&gt;]</c> —
/// queries the deployment registry for active deployments.
/// </summary>
internal static class StatusCommand
{
    public static Command Create()
    {
        var deploymentIdOption = new Option<string?>("--deployment-id")
        {
            Description = "Specific deployment ID to query. Omit to list all deployments."
        };

        var workflowOption = new Option<string?>("--workflow")
        {
            Description = "Filter results by workflow name."
        };

        var command = new Command("status", "Show deployment status.")
        {
            deploymentIdOption,
            workflowOption
        };

        command.SetAction(async parseResult =>
        {
            var deploymentId = parseResult.GetValue(deploymentIdOption);
            var workflow = parseResult.GetValue(workflowOption);
            var json = parseResult.GetValue<bool>("--json");
            var inMemory = parseResult.GetValue<bool>("--in-memory");

            using var host = new PlatformHost(inMemory);
            await ExecuteAsync(host, deploymentId, workflow, json);
        });

        return command;
    }

    private static async Task ExecuteAsync(PlatformHost host, string? deploymentId, string? workflow, bool json)
    {
        if (deploymentId is not null)
        {
            var record = await host.Registry.GetAsync(deploymentId);
            if (record is null)
            {
                if (json)
                    JsonOutput.Write(new { status = "not-found", deploymentId });
                else
                {
                    Console.WriteLine();
                    Console.WriteLine($"  No deployment found with ID '{deploymentId}'.");
                    Console.WriteLine();
                }
                return;
            }

            if (json)
                JsonOutput.Write(new { status = "ok", deployment = ToDto(record) });
            else
                PrintRecord(record);
            return;
        }

        var records = await host.Registry.ListAsync(workflow);

        if (json)
        {
            JsonOutput.Write(new { status = "ok", count = records.Count, deployments = records.Select(ToDto) });
        }
        else
        {
            Console.WriteLine();
            if (records.Count == 0)
            {
                Console.WriteLine(workflow is not null
                    ? $"  No deployments found for workflow '{workflow}'."
                    : "  No deployments found.");
            }
            else
            {
                Console.WriteLine($"  {"ID",-38} {"Workflow",-24} {"Platform",-22} {"Status",-12} {"Updated"}");
                Console.WriteLine($"  {new string('─', 38)} {new string('─', 24)} {new string('─', 22)} {new string('─', 12)} {new string('─', 24)}");
                foreach (var r in records)
                    Console.WriteLine($"  {r.DeploymentId,-38} {r.WorkflowName,-24} {r.Platform,-22} {r.Status,-12} {r.UpdatedAt:yyyy-MM-dd HH:mm:ss}z");
            }
            Console.WriteLine();
        }
    }

    private static void PrintRecord(DeploymentRecord r)
    {
        Console.WriteLine();
        Console.WriteLine($"  Deployment ID : {r.DeploymentId}");
        Console.WriteLine($"  Workflow      : {r.WorkflowName}");
        Console.WriteLine($"  Platform      : {r.Platform}");
        Console.WriteLine($"  Version       : {r.Version}");
        Console.WriteLine($"  Status        : {r.Status}");
        Console.WriteLine($"  Created       : {r.CreatedAt:yyyy-MM-dd HH:mm:ss}z");
        Console.WriteLine($"  Updated       : {r.UpdatedAt:yyyy-MM-dd HH:mm:ss}z");
        if (r.PlatformResourceId is not null)
            Console.WriteLine($"  Resource ID   : {r.PlatformResourceId}");
        Console.WriteLine();
    }

    private static object ToDto(DeploymentRecord r) => new
    {
        deploymentId = r.DeploymentId,
        workflowName = r.WorkflowName,
        platform = r.Platform,
        version = r.Version,
        status = r.Status.ToString().ToLowerInvariant(),
        platformResourceId = r.PlatformResourceId,
        createdAt = r.CreatedAt,
        updatedAt = r.UpdatedAt
    };
}
