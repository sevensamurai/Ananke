using System.Text.Json;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Jobs;
using Microsoft.Extensions.Logging;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// The one place a tool call is parsed and invoked. A malformed argument or a delegate that
/// throws becomes a <see cref="ToolResult.Error(string)"/> the model can see and correct,
/// instead of failing the run outright.
/// </summary>
internal static class ToolDispatch
{
    /// <summary>
    /// Parses a tool call's raw JSON arguments. Missing or whitespace-only arguments are treated
    /// as none. Arguments that do not parse, or that parse to something other than a JSON object,
    /// are reported as a retryable <see cref="ToolResult.Error(string)"/> rather than thrown.
    /// </summary>
    public static bool TryParseArguments(
        string? arguments,
        string toolName,
        out IReadOnlyDictionary<string, object?> args,
        out ToolResult error)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            args = new Dictionary<string, object?>();
            error = default!;
            return true;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(arguments);
        }
        catch (JsonException)
        {
            args = null!;
            error = ToolResult.Error(
                $"Invalid JSON arguments for '{toolName}'. Re-emit the call with a JSON object.");
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                args = null!;
                error = ToolResult.Error(
                    $"Invalid JSON arguments for '{toolName}'. Re-emit the call with a JSON object.");
                return false;
            }

            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();
            args = dict;
            error = default!;
            return true;
        }
    }

    /// <summary>
    /// Runs a tool's delegate, turning anything it throws into a <see cref="ToolResult.Error(string)"/>
    /// the model can see and correct, rather than failing the whole job. Cancellation, and the
    /// exceptions that exist to unwind the run itself, are never caught: a guardrail, an interrupted
    /// sub-flow, and a model the tool called that repeated itself or ran out of time
    /// (<see cref="TerminalProviderError.IsRepeatOrTimeout"/>), wrapped or not.
    /// </summary>
    /// <remarks>
    /// An error that only reads like a depleted account is still the tool's to report: the service a
    /// tool calls is not the model provider, and its quota does not end the run.
    /// </remarks>
    public static async Task<ToolResult> InvokeAsync(
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct,
        ILogger? logger = null)
    {
        try
        {
            return await tool.ExecuteAsync(args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (GuardrailException) { throw; }
        catch (SubFlowInterruptedException) { throw; }
        catch (Exception ex) when (TerminalProviderError.IsRepeatOrTimeout(ex)) { throw; }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, "Tool '{Tool}' rejected its arguments", tool.Name);
            return ToolResult.Error(
                $"Tool '{tool.Name}' rejected its arguments: {ex.Message} Re-emit the call with corrected arguments.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Tool '{Tool}' failed", tool.Name);
            return ToolResult.Error($"Tool '{tool.Name}' failed: {ex.Message}");
        }
    }
}
