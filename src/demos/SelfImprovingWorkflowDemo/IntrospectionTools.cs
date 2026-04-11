using Ananke.Design;
using Ananke.Orchestration.Tools;

namespace SelfImprovingWorkflowDemo;

/// <summary>
/// Self-diagnosis tools that an overseer agent can use to introspect
/// the current workflow and query framework documentation.
/// These simulate the <c>nnke inspect</c> and <c>nnke docs</c> commands
/// described in ADR-U004, exposed as in-process <see cref="ToolKit"/> tools.
/// </summary>
internal static class IntrospectionTools
{
    /// <summary>
    /// Creates a toolkit that gives an agent the ability to inspect
    /// the current workflow manifest and search framework documentation.
    /// </summary>
    public static ToolKit Create(WorkflowManifest manifest) =>
        new ToolKit("introspection")
            .AddTool("inspect_workflow", "Inspects the current workflow manifest and returns a health report as JSON",
                () => InspectWorkflow(manifest))
            .AddTool("list_available_tools", "Lists tools currently bound to each agent job in the workflow",
                () => ListBoundTools(manifest))
            .AddTool("search_docs", "Searches Ananke framework documentation for a topic",
                (string query) => SearchDocs(query),
                "query", "Search query (e.g. 'currency conversion', 'adding tools to agent jobs')")
            .AddTool("suggest_fix", "Given a problem description, suggests a manifest change",
                (string problem) => SuggestFix(problem),
                "problem", "Description of the problem to fix");

    private static string InspectWorkflow(WorkflowManifest manifest)
    {
        var agentJobs = manifest.Jobs
            .Where(j => j.Value.Type == "agent")
            .Select(j => j.Key)
            .ToList();

        var codeJobs = manifest.Jobs
            .Where(j => j.Value.Type == "code")
            .Select(j => j.Key)
            .ToList();

        // Detect potential gaps
        var issues = new List<string>();

        // Check: do any system prompts mention currencies but no conversion tool exists?
        var mentionsCurrency = manifest.Jobs.Values
            .Any(j => j.SystemPrompt?.Contains("USD", StringComparison.OrdinalIgnoreCase) == true
                   || j.SystemPrompt?.Contains("currenc", StringComparison.OrdinalIgnoreCase) == true);

        var hasConversionJob = manifest.Jobs.Keys
            .Any(k => k.Contains("convert", StringComparison.OrdinalIgnoreCase)
                    || k.Contains("currency", StringComparison.OrdinalIgnoreCase));

        if (mentionsCurrency && !hasConversionJob)
            issues.Add("System prompts reference currency normalization (USD) but no currency conversion job or tool is present in the workflow.");

        // Check: are there code jobs that might be missing?
        if (codeJobs.Count == 0)
            issues.Add("No code jobs found — consider adding data extraction or transformation steps.");

        var report = new
        {
            workflow = manifest.Name,
            models = manifest.Models.Keys.ToList(),
            agentJobs,
            codeJobs,
            connections = manifest.Connections,
            connectionCount = manifest.Connections.Count,
            issues,
            status = issues.Count == 0 ? "healthy" : "issues_detected"
        };

        return System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string ListBoundTools(WorkflowManifest manifest)
    {
        // In a real implementation, this would introspect the actual ToolKit
        // bindings on each agent job. Here we report what the manifest knows.
        var jobs = manifest.Jobs.Select(j => new
        {
            name = j.Key,
            type = j.Value.Type,
            hasToolCapability = j.Value.MaxToolRounds > 0,
            note = j.Value.MaxToolRounds > 0
                ? "Agent can use tools but check which tools are actually bound in code"
                : "No tool rounds configured"
        });

        return System.Text.Json.JsonSerializer.Serialize(jobs,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string SearchDocs(string query)
    {
        // Simulates `nnke docs --search` — returns relevant snippets.
        // In production this would search embedded/filesystem docs.
        var lowerQuery = query.ToLowerInvariant();

        if (lowerQuery.Contains("tool") || lowerQuery.Contains("toolkit"))
            return """
                {
                  "results": [
                    {
                      "topic": "tools",
                      "section": "Adding tools to agent jobs",
                      "snippet": "Use ToolKit to register tools with names, descriptions, and typed parameters for LLM function calling. Attach tools to agent jobs via .WithTools(toolkit). Tools can convert data, call APIs, or perform calculations.",
                      "guide": "docs/guides/04-tools.md"
                    },
                    {
                      "topic": "design-tooling",
                      "section": "Code jobs vs agent jobs",
                      "snippet": "Code jobs (type: code) are bound as lambdas or IJob implementations. They run deterministic logic — data extraction, transformation, API calls. Agent jobs (type: agent) use LLMs. For deterministic transformations like currency conversion, prefer code jobs.",
                      "guide": "docs/guides/13-design-tooling.md"
                    }
                  ]
                }
                """;

        if (lowerQuery.Contains("currency") || lowerQuery.Contains("convert"))
            return """
                {
                  "results": [
                    {
                      "topic": "patterns",
                      "section": "ETL pattern — data normalization",
                      "snippet": "For data normalization (e.g. currency conversion, unit standardization), add a code job between extraction and analysis. Code jobs are deterministic and cheaper than agent calls. Example: extract -> convert -> analyze -> End",
                      "guide": "docs/guides/13-design-tooling.md"
                    },
                    {
                      "topic": "dsl-syntax",
                      "section": "Adding jobs to topology",
                      "snippet": "To insert a new job between existing ones, update the connections in .ananke.yml. Example: change 'extract -> analyze' to 'extract -> convert_currencies' and 'convert_currencies -> analyze'.",
                      "guide": "docs/reference/workflow-dsl.md"
                    }
                  ]
                }
                """;

        if (lowerQuery.Contains("manifest") || lowerQuery.Contains("yaml") || lowerQuery.Contains("dsl"))
            return """
                {
                  "results": [
                    {
                      "topic": "dsl-syntax",
                      "section": "Workflow DSL Reference",
                      "snippet": "The .ananke.yml manifest declares topology (connections), model aliases, and job metadata. Code jobs are declared with 'type: code' and bound in C# via scaffold.Bind().",
                      "guide": "docs/reference/workflow-dsl.md"
                    }
                  ]
                }
                """;

        return """{ "results": [], "message": "No results found for query." }""";
    }

    private static string SuggestFix(string problem)
    {
        var lowerProblem = problem.ToLowerInvariant();

        if (lowerProblem.Contains("currency") || lowerProblem.Contains("convert")
                                               || lowerProblem.Contains("normalize"))
            return """
                {
                  "fix": {
                    "action": "add_code_job",
                    "jobName": "convert_currencies",
                    "insertBetween": ["extract", "analyze"],
                    "description": "Add a code job that converts all foreign currency amounts to USD using a conversion tool before the analysis agent processes them.",
                    "manifestChange": "1. Add 'convert_currencies: type: code' to the jobs section. 2. Change connections from 'extract -> analyze' to 'extract -> convert_currencies' and 'convert_currencies -> analyze'.",
                    "alternativeManifest": "expense-analyzer-v2.ananke.yml"
                  }
                }
                """;

        return """
            {
              "fix": {
                "action": "unknown",
                "description": "Could not determine a specific fix. Consider using 'inspect_workflow' and 'search_docs' for more context."
              }
            }
            """;
    }
}
