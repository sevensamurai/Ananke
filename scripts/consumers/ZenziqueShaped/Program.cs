using Ananke.Design.Tools;
using Ananke.Documents;
using Ananke.Embeddings.Onnx;
using Ananke.MCP;
using Ananke.Orchestration.Extensions;
using Ananke.Orchestration.Google.Extensions;
using Ananke.Orchestration.Knowledge.Linking;
using Ananke.Orchestration.OpenAI.Extensions;
using Ananke.Skills;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddWorkflowOrchestration();
services.AddOrchestrationOpenAI("key");
services.AddOrchestrationGoogle("key");

Type[] used =
[
    typeof(LinkedKnowledgeStore),
    typeof(DocumentExtractorFactory),
#pragma warning disable ANNKEEXP004 // experimental before 1.0, and taken by Zenzique's knowledge base
    typeof(OnnxEmbeddingModel),
#pragma warning restore ANNKEEXP004
    typeof(RouterStageFactory),
    typeof(McpToolInvoker),
    typeof(SkillScore),
];

// A deprecated model id: the packed analyzer must report ANNKE002 here from the
// model-lifecycle.json it embeds (the build gives it no AdditionalFiles).
const string model = "gpt-4o";

await Pause.RunAsync();
Console.WriteLine($"{services.Count} services, {used.Length} more types, model {model}");

internal static class Pause
{
    // An await without ConfigureAwait(false) in an internal method: this repository's own rule
    // (ANANKE_ASYNC_001) would flag it, and must not reach a product.
    internal static async Task RunAsync()
    {
        await Task.Yield();
    }
}
