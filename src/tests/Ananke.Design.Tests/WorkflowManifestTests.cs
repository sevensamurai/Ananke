using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class WorkflowManifestTests
{
    // ── Name parsing ─────────────────────────────────────────────────

    [Test]
    public void Parse_Name_Extracted()
    {
        var manifest = WorkflowManifest.Parse([
            "name: my-workflow",
            "models:",
            "jobs:",
            "connections:",
        ]);

        manifest.Name.ShouldBe("my-workflow");
    }

    [Test]
    public void Parse_MissingName_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowManifest.Parse([
                "models:",
                "jobs:",
                "connections:",
            ]));
    }

    // ── Models section ───────────────────────────────────────────────

    [Test]
    public void Parse_Models_ExtractsProviderAndModel()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "  smart:",
            "    provider: anthropic",
            "    model: claude-sonnet-5",
            "jobs:",
            "connections:",
        ]);

        manifest.Models.Count.ShouldBe(2);

        manifest.Models["fast"].Provider.ShouldBe("openai");
        manifest.Models["fast"].Model.ShouldBe("gpt-4.1-mini");

        manifest.Models["smart"].Provider.ShouldBe("anthropic");
        manifest.Models["smart"].Model.ShouldBe("claude-sonnet-5");
    }

    [Test]
    public void Parse_ModelWithEndpoint_ExtractsEndpoint()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local:",
            "    provider: openai",
            "    model: llama3",
            "    endpoint: http://localhost:11434/v1",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["local"].Endpoint.ShouldBe("http://localhost:11434/v1");
    }

    [Test]
    public void Parse_ModelDefaults_AppliedWhenFieldsMissing()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  minimal:",
            "    provider: openai",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["minimal"].Model.ShouldBe(Models.OpenAI.Gpt54Mini);
        manifest.Models["minimal"].Endpoint.ShouldBeNull();
    }

    // ── Jobs section ─────────────────────────────────────────────────

    [Test]
    public void Parse_Jobs_ExtractsTypeAndModel()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  classify:",
            "    type: agent",
            "    model: fast",
            "  transform:",
            "    type: code",
            "connections:",
        ]);

        manifest.Jobs.Count.ShouldBe(2);

        manifest.Jobs["classify"].Type.ShouldBe("agent");
        manifest.Jobs["classify"].ModelAlias.ShouldBe("fast");

        manifest.Jobs["transform"].Type.ShouldBe("code");
    }

    [Test]
    public void Parse_JobMaxToolRounds_Parsed()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  agent_job:",
            "    type: agent",
            "    max_tool_rounds: 5",
            "connections:",
        ]);

        manifest.Jobs["agent_job"].MaxToolRounds.ShouldBe(5);
    }

    [Test]
    public void Parse_JobDefaults_AppliedWhenFieldsMissing()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  minimal:",
            "    type: code",
            "connections:",
        ]);

        manifest.Jobs["minimal"].ModelAlias.ShouldBeNull();
        manifest.Jobs["minimal"].SystemPrompt.ShouldBeNull();
        manifest.Jobs["minimal"].Tools.ShouldBeEmpty();
        manifest.Jobs["minimal"].Semantic.ShouldBeFalse();
        manifest.Jobs["minimal"].MaxToolRounds.ShouldBe(3);
    }

    [Test]
    public void Parse_Tools_ExtractsMetadataAndBinding()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "tools:",
            "  web_search:",
            "    name: web_search",
            "    description: Search the public web",
            "    tags: [search, web, retrieval]",
            "    binding:",
            "      kind: mcp",
            "      reference: web.search",
            "jobs:",
            "connections:",
        ]);

        manifest.Tools.Count.ShouldBe(1);
        manifest.Tools["web_search"].Key.ShouldBe("web_search");
        manifest.Tools["web_search"].Name.ShouldBe("web_search");
        manifest.Tools["web_search"].Description.ShouldBe("Search the public web");
        manifest.Tools["web_search"].Tags.ShouldBe(["search", "web", "retrieval"]);
        manifest.Tools["web_search"].Binding.Kind.ShouldBe("mcp");
        manifest.Tools["web_search"].Binding.Reference.ShouldBe("web.search");
    }

    [Test]
    public void Parse_JobToolsAndSemantic_Extracted()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "tools:",
            "  web_search:",
            "    name: web_search",
            "    description: Search the public web",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    tools:",
            "      - web_search",
            "    semantic: true",
            "connections:",
        ]);

        manifest.Jobs["plan"].Tools.ShouldBe(["web_search"]);
        manifest.Jobs["plan"].Semantic.ShouldBeTrue();
    }

    // ── System prompt (inline) ───────────────────────────────────────

    [Test]
    public void Parse_InlineSystemPrompt_Extracted()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  classify:",
            "    type: agent",
            "    system_prompt: You are a classifier.",
            "connections:",
        ]);

        manifest.Jobs["classify"].SystemPrompt.ShouldBe("You are a classifier.");
    }

    // ── System prompt (multi-line block) ─────────────────────────────

    [Test]
    public void Parse_MultiLineSystemPrompt_Extracted()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  classify:",
            "    type: agent",
            "    system_prompt: |",
            "      You are a support ticket classifier.",
            "      Analyze severity on a 1-10 scale.",
            "connections:",
        ]);

        var prompt = manifest.Jobs["classify"].SystemPrompt;
        prompt.ShouldNotBeNull();
        prompt.ShouldContain("support ticket classifier");
        prompt.ShouldContain("1-10 scale");
    }

    [Test]
    public void Parse_MultiLineSystemPrompt_FieldAfterPrompt_BothParsed()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  classify:",
            "    type: agent",
            "    system_prompt: |",
            "      You are a classifier.",
            "    max_tool_rounds: 7",
            "connections:",
        ]);

        manifest.Jobs["classify"].SystemPrompt.ShouldBe("You are a classifier.");
        manifest.Jobs["classify"].MaxToolRounds.ShouldBe(7);
    }

    // ── Connections section ───────────────────────────────────────────

    [Test]
    public void Parse_Connections_ExtractsDslLines()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
            "  - classify -> escalate",
            "  - escalate -> End",
        ]);

        manifest.Connections.Count.ShouldBe(2);
        manifest.Connections[0].ShouldBe("classify -> escalate");
        manifest.Connections[1].ShouldBe("escalate -> End");
    }

    [Test]
    public void Parse_ConnectionsWithLoop_PassesThroughToScaffold()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
            "  - start -> code_cycle",
            "  - code_cycle -> await_ci",
            "  - await_ci -> loop(code_cycle, exit: report)",
            "  - report -> End",
        ]);

        manifest.Connections.ShouldContain("await_ci -> loop(code_cycle, exit: report)");

        var scaffold = WorkflowScaffold.Parse<object>(manifest.Name, manifest.Connections);

        scaffold.UnboundLoops.ShouldContain("await_ci");
        scaffold.GetTopologyDsl().ShouldContain("await_ci -> loop(code_cycle, exit: report)");
    }

    // ── Comments and blank lines ─────────────────────────────────────

    [Test]
    public void Parse_CommentsAndBlankLines_Skipped()
    {
        var manifest = WorkflowManifest.Parse([
            "# Top-level comment",
            "name: test",
            "",
            "# Models section",
            "models:",
            "jobs:",
            "connections:",
        ]);

        manifest.Name.ShouldBe("test");
    }

    // ── Full manifest round-trip ─────────────────────────────────────

    [Test]
    public void Parse_FullManifest_AllSectionsPopulated()
    {
        var manifest = WorkflowManifest.Parse([
            "name: support-triage",
            "",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "",
            "jobs:",
            "  classify:",
            "    type: agent",
            "    model: fast",
            "    system_prompt: Classify the ticket.",
            "  notify:",
            "    type: code",
            "",
            "connections:",
            "  - classify -> notify",
            "  - notify -> End",
        ]);

        manifest.Name.ShouldBe("support-triage");
        manifest.Models.Count.ShouldBe(1);
        manifest.Jobs.Count.ShouldBe(2);
        manifest.Connections.Count.ShouldBe(2);
    }

    // ── Profiles section ─────────────────────────────────────────────

    [Test]
    public void Parse_Profiles_InlineFormat()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
            "profiles:",
            "  azure-ai:",
            "    tools:",
            "      search: { platform: bing_search }",
            "      code: { platform: code_interpreter }",
        ]);

        manifest.Profiles.Count.ShouldBe(1);
        manifest.Profiles["azure-ai"].Tools.Count.ShouldBe(2);
        manifest.Profiles["azure-ai"].Tools["search"].Execute.ShouldBe("platform");
        manifest.Profiles["azure-ai"].Tools["search"].Platform.ShouldBe("bing_search");
        manifest.Profiles["azure-ai"].Tools["code"].Platform.ShouldBe("code_interpreter");
    }

    [Test]
    public void Parse_Profiles_BlockFormat()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
            "profiles:",
            "  local:",
            "    tools:",
            "      search:",
            "        execute: local",
            "      code:",
            "        execute: callback",
            "        endpoint: https://example.com/code",
        ]);

        manifest.Profiles["local"].Tools["search"].Execute.ShouldBe("local");
        manifest.Profiles["local"].Tools["code"].Execute.ShouldBe("callback");
        manifest.Profiles["local"].Tools["code"].Endpoint.ShouldBe("https://example.com/code");
    }

    [Test]
    public void Parse_Profiles_MultipleProfiles()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
            "profiles:",
            "  azure-ai:",
            "    tools:",
            "      search: { platform: bing_search }",
            "  vertex-ai:",
            "    tools:",
            "      search: { platform: google_search }",
            "  local:",
            "    tools:",
            "      search: { execute: local }",
        ]);

        manifest.Profiles.Count.ShouldBe(3);
        manifest.Profiles["azure-ai"].Tools["search"].Platform.ShouldBe("bing_search");
        manifest.Profiles["vertex-ai"].Tools["search"].Platform.ShouldBe("google_search");
        manifest.Profiles["local"].Tools["search"].Execute.ShouldBe("local");
    }

    [Test]
    public void Parse_NoProfiles_DefaultsToEmpty()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "connections:",
        ]);

        manifest.Profiles.ShouldBeEmpty();
    }

    // ── Flow mappings in models: (L3) ────────────────────────────────

    /// <summary>
    /// Before L3 a flow-mapped alias was dropped entirely, so every job referencing it reported
    /// <c>FED011</c> "model alias not defined" about an alias plainly present in the file. Five of
    /// the reference manifests in this repository failed this way.
    /// </summary>
    [Test]
    public void Parse_ModelFlowMapping_DeclaresTheAlias()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  coder:    { ref: devstral2 }",
            "jobs:",
            "connections:",
        ]);

        manifest.Models.ShouldContainKey("coder");
        manifest.Models["coder"].Ref.ShouldBe("devstral2");
    }

    [Test]
    public void Parse_ModelFlowMapping_ReadsMultipleFields()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local: { provider: openai, model: devstral2:latest, endpoint: http://localhost:11434/v1 }",
            "jobs:",
            "connections:",
        ]);

        var model = manifest.Models["local"];
        model.Provider.ShouldBe("openai");
        model.Model.ShouldBe("devstral2:latest");
        model.Endpoint.ShouldBe("http://localhost:11434/v1");
    }

    /// <summary>
    /// Aliases in real manifests are routinely annotated, and a comment that lands inside the value
    /// is the same silent-corruption shape the flow mapping itself had.
    /// </summary>
    [Test]
    public void Parse_ModelFlowMapping_StripsTrailingComment()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  escalation: { ref: opus }     # used via `ask_opus`, not as a primary model",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["escalation"].Ref.ShouldBe("opus");
    }

    [Test]
    public void Parse_ModelBlockForm_ReadsRefAndStripsTrailingComment()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  router:",
            "    ref: devstral2        # resolved from roles.ananke.yml",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["router"].Ref.ShouldBe("devstral2");
    }

    /// <summary>
    /// A comment marker only starts a comment where it follows whitespace, so a URL fragment
    /// survives.
    /// </summary>
    [Test]
    public void Parse_ModelEndpoint_KeepsAHashThatIsNotAComment()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  m:",
            "    endpoint: http://localhost:11434/v1#frag",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["m"].Endpoint.ShouldBe("http://localhost:11434/v1#frag");
    }

    /// <summary>
    /// Flow mappings stay scoped to <c>models:</c>. This pins that a following block-form alias is
    /// still read — i.e. the flow branch does not leave a stale current block behind.
    /// </summary>
    [Test]
    public void Parse_FlowMapping_DoesNotSwallowTheNextAlias()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  a: { ref: x }",
            "  b:",
            "    provider: anthropic",
            "    model: claude-opus-4",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["a"].Ref.ShouldBe("x");
        manifest.Models["b"].Ref.ShouldBeNull();
        manifest.Models["b"].Provider.ShouldBe("anthropic");
    }

    /// <summary>
    /// An alias with no <c>ref</c> must not gain one — <see cref="ModelDefinition.Ref"/> being null
    /// is what lets the validator tell a declared model from an unresolved reference.
    /// </summary>
    [Test]
    public void Parse_OrdinaryModel_HasNoRef()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  m:",
            "    provider: openai",
            "    model: gpt-5.6-terra",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["m"].Ref.ShouldBeNull();
    }

    // ── temperature is a model field ─────────────────

    [Test]
    public void Parse_ModelTemperature_IsRead()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  devstral2:",
            "    provider: openai",
            "    model: devstral2:latest",
            "    temperature: 0",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["devstral2"].Temperature.ShouldBe(0d);
    }

    [Test]
    public void Parse_ModelTemperature_InAFlowMapping()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  warm: { provider: openai, model: gpt-5.6-terra, temperature: 0.7 }",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["warm"].Temperature.ShouldBe(0.7d);
    }

    /// <summary>
    /// Unset must stay distinguishable from <c>0</c>, or every model silently pins a temperature
    /// nobody chose.
    /// </summary>
    [Test]
    public void Parse_ModelWithoutTemperature_LeavesItUnset()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  m:",
            "    provider: openai",
            "jobs:",
            "connections:",
        ]);

        manifest.Models["m"].Temperature.ShouldBeNull();
    }

    /// <summary>
    /// Decimal temperatures must not depend on the host's locale — a comma-decimal culture would
    /// otherwise read <c>0.7</c> as <c>7</c>.
    /// </summary>
    [Test]
    public void Parse_ModelTemperature_IsCultureInvariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var manifest = WorkflowManifest.Parse([
                "name: test",
                "models:",
                "  m: { temperature: 0.7 }",
                "jobs:",
                "connections:",
            ]);

            manifest.Models["m"].Temperature.ShouldBe(0.7d);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
