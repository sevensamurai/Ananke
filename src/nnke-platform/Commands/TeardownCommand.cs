using Ananke.Federation.Deployment;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform teardown --deployment-id &lt;id&gt;</c> —
/// tears down a previously deployed workflow, releasing platform resources.
/// The deployer is resolved from the deployment record's <c>Platform</c> field.
/// </summary>
internal static class TeardownCommand
{
    public static Command Create()
    {
        var deploymentIdOption = new Option<string>("--deployment-id")
        {
            Description = "Deployment ID to tear down.",
            Required = true
        };

        var command = new Command("teardown", "Tear down a deployed workflow and release platform resources.")
        {
            deploymentIdOption
        };

        command.SetAction(async parseResult =>
        {
            var deploymentId = parseResult.GetValue(deploymentIdOption)!;
            var json = parseResult.GetValue<bool>("--json");
            var inMemory = parseResult.GetValue<bool>("--in-memory");

            using var host = new PlatformHost(inMemory);
            await ExecuteAsync(host, deploymentId, json);
        });

        return command;
    }

    private static async Task ExecuteAsync(PlatformHost host, string deploymentId, bool json)
    {
        // ── 1. Look up record ────────────────────────────────────────────────
        var record = await host.Registry.GetAsync(deploymentId);
        if (record is null)
        {
            if (json)
                JsonOutput.Write(new { status = "not-found", deploymentId });
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  ✗ No deployment found with ID '{deploymentId}'.");
                Console.Error.WriteLine();
            }
            return;
        }

        if (record.Status == DeploymentStatus.Stopped)
        {
            if (json)
                JsonOutput.Write(new { status = "skipped", message = "Deployment is already stopped.", deploymentId });
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  ⚠ Deployment '{deploymentId}' is already stopped.");
                Console.WriteLine();
            }
            return;
        }

        // ── 2. Resolve deployer from the record's platform ───────────────────
        var deployer = host.ResolveDeployer(record.Platform);
        if (deployer is null)
        {
            var hint = DeployCommand.AdapterInstallHint(record.Platform);
            if (json)
                JsonOutput.Write(new { status = "error", message = $"No adapter registered for '{record.Platform}'.", installHint = hint });
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  ✗ No adapter registered for '{record.Platform}'.");
                Console.Error.WriteLine($"    {hint}");
                Console.Error.WriteLine();
            }
            return;
        }

        // ── 3. Call platform teardown ────────────────────────────────────────
        try
        {
            await deployer.TeardownAsync(deploymentId);
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Teardown failed: {ex.Message}", deploymentId });
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  ✗ Teardown failed: {ex.Message}");
                Console.Error.WriteLine();
            }
            return;
        }

        // ── 4. Update registry ───────────────────────────────────────────────
        await host.Registry.UpdateStatusAsync(deploymentId, DeploymentStatus.Stopped);

        if (json)
            JsonOutput.Write(new { status = "torn-down", deploymentId, platform = record.Platform, workflow = record.WorkflowName });
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  ✓ Torn down deployment '{deploymentId}'");
            Console.WriteLine($"    Workflow : {record.WorkflowName}");
            Console.WriteLine($"    Platform : {record.Platform}");
            Console.WriteLine();
        }
    }
}
