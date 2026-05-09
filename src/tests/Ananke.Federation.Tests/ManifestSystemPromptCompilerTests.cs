using Ananke.Design;
using Ananke.Federation.Prompts;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class ManifestSystemPromptCompilerTests
{
    private ManifestSystemPromptCompiler _compiler = null!;

    [SetUp]
    public void SetUp() => _compiler = new ManifestSystemPromptCompiler();

    private static WorkflowManifest MakeManifest(string? systemPrompt = "You are a helpful assistant.") => new()
    {
        Name = "test-workflow",
        Models = new() { ["default"] = new() },
        Jobs = new()
        {
            ["agent1"] = new()
            {
                Type = "agent",
                ModelAlias = "default",
                SystemPrompt = systemPrompt
            }
        },
        Connections = ["agent1"]
    };

    [Test]
    public void Compile_includes_workflow_and_job_name()
    {
        var prompt = _compiler.Compile(MakeManifest(), "agent1");
        prompt.ShouldContain("'agent1'");
        prompt.ShouldContain("'test-workflow'");
    }

    [Test]
    public void Compile_includes_system_prompt()
    {
        var prompt = _compiler.Compile(MakeManifest(), "agent1");
        prompt.ShouldContain("You are a helpful assistant.");
    }

    [Test]
    public void Compile_includes_skills()
    {
        var skills = new List<string> { "Can summarize documents", "Knows SQL" };
        var prompt = _compiler.Compile(MakeManifest(), "agent1", skills);

        prompt.ShouldContain("Learned Skills");
        prompt.ShouldContain("Can summarize documents");
        prompt.ShouldContain("Knows SQL");
    }

    [Test]
    public void Compile_without_system_prompt_still_works()
    {
        var prompt = _compiler.Compile(MakeManifest(systemPrompt: null), "agent1");
        prompt.ShouldContain("'agent1'");
    }

    [Test]
    public void Compile_throws_for_unknown_job()
    {
        Should.Throw<ArgumentException>(
            () => _compiler.Compile(MakeManifest(), "nonexistent"));
    }
}
