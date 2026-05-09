using Ananke.Federation.Execution;
using Ananke.Federation.LocalEmulators;
using Shouldly;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// Phase C conformance tests for the real-emulator implementations.
/// Verifies that all capabilities in the emulation matrix are covered and that
/// real emulators return well-formed <see cref="Ananke.Orchestration.Tools.ToolResult"/>
/// values. Network-dependent tests (web_search, web_fetch) are marked as
/// <c>[Explicit]</c> and skipped in standard CI.
/// </summary>
[TestFixture]
public class EmulatorImplementationTests
{
    // ── DefaultPlatformNativeExecutors coverage ───────────────────────

    [Test]
    public void DefaultPlatformNativeExecutors_Register_CoversAllMatrixCapabilities()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var required = new[]
        {
            "web_search", "web_fetch",
            "bash", "text_editor",
            "code_execution", "code_interpreter", "vertex_extension:code_interpreter",
            "file_search",
            "memory", "memory_bank", "memory_profiles", "memory_search",
            "bing_search", "bing_grounding", "bing_custom_search",
            "azure_ai_search", "sharepoint", "sharepoint_grounding", "microsoft_fabric",
            "google_search", "google_search_retrieval", "url_context",
            "computer_use", "browser_automation",
            "image_generation", "deep_research",
            "bigquery", "spanner", "bigtable", "pubsub", "maps", "artifact_service",
            "capture_structured_outputs"
        };

        foreach (var cap in required)
        {
            registry.TryResolve(cap).ShouldNotBeNull(
                $"No executor registered for capability '{cap}'");
        }
    }

    [Test]
    public void DefaultPlatformNativeExecutors_StubCapabilities_HaveIsStubTrue()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var stubs = new[]
        {
            "bing_search", "bing_grounding", "bing_custom_search",
            "azure_ai_search", "sharepoint", "sharepoint_grounding", "microsoft_fabric",
            "google_search", "google_search_retrieval", "url_context",
            "computer_use", "browser_automation", "image_generation", "deep_research",
            "bigquery", "spanner", "bigtable", "pubsub", "maps", "artifact_service",
            "capture_structured_outputs"
        };

        foreach (var cap in stubs)
        {
            var executor = registry.TryResolve(cap);
            executor.ShouldNotBeNull(cap);
            executor!.IsStub.ShouldBeTrue($"Capability '{cap}' should be a stub");
        }
    }

    [Test]
    public void DefaultPlatformNativeExecutors_RealCapabilities_HaveIsStubFalse()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var real = new[]
        {
            "web_search", "web_fetch", "bash", "text_editor",
            "code_execution", "code_interpreter", "vertex_extension:code_interpreter",
            "file_search", "memory", "memory_bank", "memory_profiles", "memory_search"
        };

        foreach (var cap in real)
        {
            var executor = registry.TryResolve(cap);
            executor.ShouldNotBeNull(cap);
            executor!.IsStub.ShouldBeFalse($"Capability '{cap}' should be a real emulator");
        }
    }

    // ── MemoryExecutor ────────────────────────────────────────────────

    [Test]
    public async Task MemoryExecutor_StoreAndRecall_RoundTrip()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var mem = registry.TryResolve("memory")!;

        var storeArgs = new Dictionary<string, object?>
        {
            ["operation"] = "store",
            ["key"] = "greeting",
            ["value"] = "hello world"
        };
        var stored = await mem.ExecuteAsync(storeArgs);
        stored.IsError.ShouldBeFalse();

        var recallArgs = new Dictionary<string, object?> { ["operation"] = "recall", ["key"] = "greeting" };
        var recalled = await mem.ExecuteAsync(recallArgs);
        recalled.IsError.ShouldBeFalse();
        recalled.Value.ShouldBe("hello world");
    }

    [Test]
    public async Task MemoryExecutor_Search_FindsStoredEntry()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var mem = registry.TryResolve("memory_bank")!;

        await mem.ExecuteAsync(new Dictionary<string, object?>
            { ["operation"] = "store", ["key"] = "deployment-tip", ["value"] = "use canary deploys" });

        var result = await mem.ExecuteAsync(new Dictionary<string, object?>
            { ["operation"] = "search", ["query"] = "canary" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("canary");
    }

    [Test]
    public async Task MemoryExecutor_Delete_RemovesEntry()
    {
        var registry = new PlatformNativeExecutorRegistry();
        DefaultPlatformNativeExecutors.Register(registry);

        var mem = registry.TryResolve("memory")!;

        await mem.ExecuteAsync(new Dictionary<string, object?>
            { ["operation"] = "store", ["key"] = "temp", ["value"] = "x" });
        await mem.ExecuteAsync(new Dictionary<string, object?>
            { ["operation"] = "delete", ["key"] = "temp" });

        var result = await mem.ExecuteAsync(new Dictionary<string, object?>
            { ["operation"] = "recall", ["key"] = "temp" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("No memory found");
    }

    [Test]
    public async Task MemoryExecutor_MissingKey_ReturnsFatalError()
    {
        var mem = MemoryExecutor.CreateAll()[0];
        var result = await mem.ExecuteAsync(new Dictionary<string, object?> { ["operation"] = "store" });
        result.IsError.ShouldBeTrue();
        result.IsRetryable.ShouldBeFalse();
    }

    // ── Stub executors ────────────────────────────────────────────────

    [Test]
    public async Task BingSearchExecutor_ReturnsFixtureResult()
    {
        var executor = new BingSearchExecutor("bing_search");
        var result = await executor.ExecuteAsync(new Dictionary<string, object?> { ["query"] = "test query" });
        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("test query");
    }

    [Test]
    public async Task ComputerUseExecutor_RecordsActionLog()
    {
        var executor = new ComputerUseExecutor();
        await executor.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "screenshot" });
        await executor.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "click" });
        executor.ActionLog.Count.ShouldBe(2);
        executor.ActionLog[0].ShouldBe("screenshot");
    }

    [Test]
    public async Task ImageGenerationExecutor_ReturnsFixtureUrl()
    {
        var executor = new ImageGenerationExecutor();
        var result = await executor.ExecuteAsync(new Dictionary<string, object?> { ["prompt"] = "a cat" });
        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("placehold");
    }

    [Test]
    public async Task GoogleDataServiceExecutor_BigQuery_ReturnsFixtureRows()
    {
        var executor = new GoogleDataServiceExecutor("bigquery");
        var result = await executor.ExecuteAsync(new Dictionary<string, object?> { ["query"] = "SELECT * FROM t" });
        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("fixture");
    }

    [TestCase("spanner")]
    [TestCase("bigtable")]
    [TestCase("pubsub")]
    [TestCase("maps")]
    [TestCase("artifact_service")]
    public async Task GoogleDataServiceExecutor_AllVariants_ReturnSuccess(string cap)
    {
        var executor = new GoogleDataServiceExecutor(cap);
        var result = await executor.ExecuteAsync(new Dictionary<string, object?>());
        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("STUB");
    }

    // ── BashExecutor (no subprocess needed — arg-validation path) ─────

    [Test]
    public async Task BashExecutor_MissingCommand_ReturnsFatal()
    {
        using var bash = new BashExecutor();
        var result = await bash.ExecuteAsync(new Dictionary<string, object?>());
        result.IsError.ShouldBeTrue();
        result.IsRetryable.ShouldBeFalse();
    }

    // ── TextEditorExecutor ────────────────────────────────────────────

    [Test]
    public async Task TextEditorExecutor_CreateViewStrReplace_RoundTrip()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"ananke-test-{Guid.NewGuid():N}");
        try
        {
            var editor = new TextEditorExecutor(sandbox);

            // Create
            var created = await editor.ExecuteAsync(new Dictionary<string, object?>
            {
                ["command"] = "create",
                ["path"] = "notes.txt",
                ["file_text"] = "hello world"
            });
            created.IsError.ShouldBeFalse();

            // View
            var viewed = await editor.ExecuteAsync(new Dictionary<string, object?>
                { ["command"] = "view", ["path"] = "notes.txt" });
            viewed.IsError.ShouldBeFalse();
            viewed.Value.ShouldBe("hello world");

            // StrReplace
            var replaced = await editor.ExecuteAsync(new Dictionary<string, object?>
            {
                ["command"] = "str_replace",
                ["path"] = "notes.txt",
                ["old_str"] = "world",
                ["new_str"] = "Ananke"
            });
            replaced.IsError.ShouldBeFalse();

            var afterReplace = await editor.ExecuteAsync(new Dictionary<string, object?>
                { ["command"] = "view", ["path"] = "notes.txt" });
            afterReplace.Value.ShouldBe("hello Ananke");
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Test]
    public async Task TextEditorExecutor_UnknownCommand_ReturnsFatal()
    {
        using var bash = new BashExecutor();
        var editor = new TextEditorExecutor(bash.SandboxRoot);
        var result = await editor.ExecuteAsync(new Dictionary<string, object?> { ["command"] = "explode" });
        result.IsError.ShouldBeTrue();
        result.IsRetryable.ShouldBeFalse();
    }

    // ── FileSearchExecutor ────────────────────────────────────────────

    [Test]
    public async Task FileSearchExecutor_MissingQuery_ReturnsFatal()
    {
        var executor = new FileSearchExecutor();
        var result = await executor.ExecuteAsync(new Dictionary<string, object?>());
        result.IsError.ShouldBeTrue();
        result.IsRetryable.ShouldBeFalse();
    }

    [Test]
    public async Task FileSearchExecutor_SearchInTempDir_FindsMatchingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ananke-filesearch-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "notes.md"), "# Ananke local emulator notes");

            var executor = new FileSearchExecutor(searchRoot: root);
            var result = await executor.ExecuteAsync(new Dictionary<string, object?>
                { ["query"] = "emulator" });

            result.IsError.ShouldBeFalse();
            result.Value.ShouldContain("notes.md");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
