using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Factory that pre-registers all built-in local emulators into a
/// <see cref="PlatformNativeExecutorRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Calling <see cref="Register"/> gives you a fully wired registry suitable
/// for <c>nnke run --emulate &lt;platform&gt;</c>, CI local-design-loop testing,
/// and developer workflows. All capabilities listed in
/// <c>platform-capabilities.json</c> are covered — either by a real emulator
/// (backed by an HTTP client, process, or in-memory store) or by a documented
/// stub that returns deterministic fixture data.
/// </para>
/// <para>
/// <b>Real emulators</b> (require local tooling or network):
/// <c>web_search</c>, <c>web_fetch</c>, <c>bash</c>, <c>text_editor</c>,
/// <c>code_execution</c>, <c>code_interpreter</c>,
/// <c>vertex_extension:code_interpreter</c>, <c>file_search</c>,
/// <c>memory</c>, <c>memory_bank</c>, <c>memory_profiles</c>, <c>memory_search</c>.
/// </para>
/// <para>
/// <b>Stubs</b> (deterministic, no network/credentials needed):
/// <c>bing_search</c>, <c>bing_grounding</c>, <c>bing_custom_search</c>,
/// <c>azure_ai_search</c>, <c>sharepoint</c>, <c>sharepoint_grounding</c>,
/// <c>microsoft_fabric</c>, <c>google_search</c>, <c>google_search_retrieval</c>,
/// <c>url_context</c>, <c>computer_use</c>, <c>browser_automation</c>,
/// <c>image_generation</c>, <c>deep_research</c>,
/// <c>bigquery</c>, <c>spanner</c>, <c>bigtable</c>, <c>pubsub</c>,
/// <c>maps</c>, <c>artifact_service</c>, <c>capture_structured_outputs</c>.
/// </para>
/// </remarks>
public static class DefaultPlatformNativeExecutors
{
    /// <summary>
    /// Registers all built-in emulators into <paramref name="registry"/>.
    /// </summary>
    /// <param name="registry">The registry to populate.</param>
    /// <param name="sandboxRoot">
    /// Optional root directory for the bash/text-editor/code-execution sandbox.
    /// When <see langword="null"/>, a temporary directory is created automatically.
    /// </param>
    /// <param name="fileSearchRoot">
    /// Optional root directory for file search. Defaults to the current working directory.
    /// </param>
    /// <returns><paramref name="registry"/> for fluent chaining.</returns>
    public static PlatformNativeExecutorRegistry Register(
        PlatformNativeExecutorRegistry registry,
        string? sandboxRoot = null,
        string? fileSearchRoot = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // ── Real emulators ───────────────────────────────────────────

        // HTTP-based
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Ananke-LocalEmulator/1.0");
        var webSearch = new WebSearchExecutor(http);
        var webFetch = new WebFetchExecutor(http);

        registry.Register(webSearch);
        registry.Register(webFetch);

        // Shell / sandbox
        var bash = new BashExecutor(sandboxRoot);
        registry.Register(bash);

        var textEditor = new TextEditorExecutor(bash.SandboxRoot);
        registry.Register(textEditor);

        // code_execution, code_interpreter, vertex_extension:code_interpreter all delegate to bash
        registry.Register(new CodeExecutionExecutor(bash, "code_execution"));
        registry.Register(new CodeExecutionExecutor(bash, "code_interpreter"));
        registry.Register(new CodeExecutionExecutor(bash, "vertex_extension:code_interpreter"));

        // File search
        registry.Register(new FileSearchExecutor(fileSearchRoot));

        // Memory (shared store across all memory capabilities)
        foreach (var memExec in MemoryExecutor.CreateAll())
            registry.Register(memExec);

        // ── Stubs ────────────────────────────────────────────────────

        // Bing variants
        registry.Register(new BingSearchExecutor("bing_search"));
        registry.Register(new BingSearchExecutor("bing_grounding"));
        registry.Register(new BingSearchExecutor("bing_custom_search"));

        // Azure AI services
        registry.Register(new AzureAiSearchExecutor());
        registry.Register(new SharePointExecutor("sharepoint"));
        registry.Register(new SharePointExecutor("sharepoint_grounding"));
        registry.Register(new MicrosoftFabricExecutor());

        // Google search / retrieval (stub — use WebSearchExecutor for real search)
        registry.Register(new GoogleSearchStubExecutor("google_search"));
        registry.Register(new GoogleSearchStubExecutor("google_search_retrieval"));
        registry.Register(new GoogleSearchStubExecutor("url_context"));

        // UI / browser
        registry.Register(new ComputerUseExecutor());
        registry.Register(new BrowserAutomationExecutor());

        // Image generation
        registry.Register(new ImageGenerationExecutor());

        // Deep research (composes web_search + web_fetch)
        registry.Register(new DeepResearchExecutor(webSearch, webFetch));

        // Google Cloud data services
        foreach (var cap in new[] { "bigquery", "spanner", "bigtable", "pubsub", "maps", "artifact_service" })
            registry.Register(new GoogleDataServiceExecutor(cap));

        // Structured outputs (capture_structured_outputs) — passthrough stub
        registry.Register(new CaptureStructuredOutputsExecutor());

        return registry;
    }
}
