using Ananke.Design.Dsl;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class WorkflowDslParserTests
{
    // ── Direct connections ────────────────────────────────────────────

    [Test]
    public void Parse_Direct_ReturnsDirect()
    {
        var result = WorkflowDslParser.Parse("a -> b");

        result.Count.ShouldBe(1);
        var direct = result[0].ShouldBeOfType<ConnectionLine.Direct>();
        direct.From.ShouldBe("a");
        direct.To.ShouldBe("b");
    }

    [Test]
    public void Parse_DirectToEnd_ReturnsDirectWithEndTarget()
    {
        var result = WorkflowDslParser.Parse("cleanup -> End");

        result.Count.ShouldBe(1);
        var direct = result[0].ShouldBeOfType<ConnectionLine.Direct>();
        direct.From.ShouldBe("cleanup");
        direct.To.ShouldBe("End");
    }

    [Test]
    public void Parse_DirectWithExtraSpaces_Succeeds()
    {
        var result = WorkflowDslParser.Parse("  a   ->   b  ");

        result.Count.ShouldBe(1);
        var direct = result[0].ShouldBeOfType<ConnectionLine.Direct>();
        direct.From.ShouldBe("a");
        direct.To.ShouldBe("b");
    }

    // ── Fork connections ─────────────────────────────────────────────

    [Test]
    public void Parse_Fork_ReturnsForkWithTargets()
    {
        var result = WorkflowDslParser.Parse("start -> fork(branch_a, branch_b)");

        result.Count.ShouldBe(1);
        var fork = result[0].ShouldBeOfType<ConnectionLine.Fork>();
        fork.From.ShouldBe("start");
        fork.Targets.ShouldBe(["branch_a", "branch_b"]);
        fork.Mode.ShouldBeNull();
    }

    [Test]
    public void Parse_ForkWithMode_ReturnsForkWithMode()
    {
        var result = WorkflowDslParser.Parse("start -> fork(a, b, mode: best-effort)");

        var fork = result[0].ShouldBeOfType<ConnectionLine.Fork>();
        fork.Targets.ShouldBe(["a", "b"]);
        fork.Mode.ShouldBe("best-effort");
    }

    [Test]
    public void Parse_ForkThreeTargets_AllCaptured()
    {
        var result = WorkflowDslParser.Parse("plan -> fork(fetch_a, fetch_b, fetch_c)");

        var fork = result[0].ShouldBeOfType<ConnectionLine.Fork>();
        fork.Targets.Length.ShouldBe(3);
        fork.Targets.ShouldBe(["fetch_a", "fetch_b", "fetch_c"]);
    }

    [Test]
    public void Parse_ForkWithOneTarget_Throws()
    {
        Should.Throw<FormatException>(() =>
            WorkflowDslParser.Parse("a -> fork(only)"));
    }

    // ── Join connections ─────────────────────────────────────────────

    [Test]
    public void Parse_Join_ReturnsJoinWithSourcesAndTarget()
    {
        var result = WorkflowDslParser.Parse("join(a, b) -> merge");

        result.Count.ShouldBe(1);
        var join = result[0].ShouldBeOfType<ConnectionLine.Join>();
        join.Sources.ShouldBe(["a", "b"]);
        join.Target.ShouldBe("merge");
    }

    [Test]
    public void Parse_JoinThreeSources_AllCaptured()
    {
        var result = WorkflowDslParser.Parse("join(x, y, z) -> combine");

        var join = result[0].ShouldBeOfType<ConnectionLine.Join>();
        join.Sources.Length.ShouldBe(3);
    }

    [Test]
    public void Parse_JoinWithOneSource_Throws()
    {
        Should.Throw<FormatException>(() =>
            WorkflowDslParser.Parse("join(only) -> target"));
    }

    // ── Router connections ───────────────────────────────────────────

    [Test]
    public void Parse_Router_ReturnsRouterWithOptions()
    {
        var result = WorkflowDslParser.Parse("classify -> router(escalate, auto_resolve, End)");

        result.Count.ShouldBe(1);
        var router = result[0].ShouldBeOfType<ConnectionLine.Router>();
        router.From.ShouldBe("classify");
        router.Options.ShouldBe(["escalate", "auto_resolve", "End"]);
    }

    [Test]
    public void Parse_RouterWithOneOption_Throws()
    {
        Should.Throw<FormatException>(() =>
            WorkflowDslParser.Parse("a -> router(only)"));
    }

    // ── Multi-line / comments / blank lines ──────────────────────────

    [Test]
    public void Parse_MultipleLines_ReturnsAll()
    {
        var dsl = """
            a -> b
            b -> c
            c -> End
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(3);
        result[0].ShouldBeOfType<ConnectionLine.Direct>();
        result[1].ShouldBeOfType<ConnectionLine.Direct>();
        result[2].ShouldBeOfType<ConnectionLine.Direct>();
    }

    [Test]
    public void Parse_BlankLinesAreSkipped()
    {
        var dsl = """
            a -> b

            b -> End
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(2);
    }

    [Test]
    public void Parse_CommentLinesAreSkipped()
    {
        var dsl = """
            # This is a comment
            a -> b
            # Another comment
            b -> End
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(2);
    }

    [Test]
    public void Parse_InlineCommentsAreStripped()
    {
        var result = WorkflowDslParser.Parse("a -> b  # connect a to b");

        result.Count.ShouldBe(1);
        var direct = result[0].ShouldBeOfType<ConnectionLine.Direct>();
        direct.From.ShouldBe("a");
        direct.To.ShouldBe("b");
    }

    [Test]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var result = WorkflowDslParser.Parse("");
        result.Count.ShouldBe(0);
    }

    [Test]
    public void Parse_OnlyComments_ReturnsEmpty()
    {
        var dsl = """
            # comment 1
            # comment 2
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(0);
    }

    // ── Error cases ──────────────────────────────────────────────────

    [Test]
    public void Parse_InvalidSyntax_Throws()
    {
        Should.Throw<FormatException>(() =>
            WorkflowDslParser.Parse("not valid syntax at all"));
    }

    // ── SubFlow directive ────────────────────────────────────────────

    [Test]
    public void Parse_SubFlow_ReturnsSubFlow()
    {
        var result = WorkflowDslParser.Parse("subflow(refine)");

        result.Count.ShouldBe(1);
        var sf = result[0].ShouldBeOfType<ConnectionLine.SubFlow>();
        sf.Name.ShouldBe("refine");
    }

    [Test]
    public void Parse_SubFlowCaseInsensitive_Succeeds()
    {
        var result = WorkflowDslParser.Parse("SubFlow(myJob)");

        result.Count.ShouldBe(1);
        result[0].ShouldBeOfType<ConnectionLine.SubFlow>().Name.ShouldBe("myJob");
    }

    // ── Interrupt directive ──────────────────────────────────────────

    [Test]
    public void Parse_Interrupt_ReturnsInterrupt()
    {
        var result = WorkflowDslParser.Parse("interrupt(publish)");

        result.Count.ShouldBe(1);
        var intr = result[0].ShouldBeOfType<ConnectionLine.Interrupt>();
        intr.JobName.ShouldBe("publish");
    }

    [Test]
    public void Parse_InterruptCaseInsensitive_Succeeds()
    {
        var result = WorkflowDslParser.Parse("INTERRUPT(deploy)");

        result.Count.ShouldBe(1);
        result[0].ShouldBeOfType<ConnectionLine.Interrupt>().JobName.ShouldBe("deploy");
    }

    [Test]
    public void Parse_ToolDirective_ReturnsTool()
    {
        var result = WorkflowDslParser.Parse("tool(web_search, tags: [search, web], description: \"Search the public web\")");

        var tool = result[0].ShouldBeOfType<ConnectionLine.Tool>();
        tool.Name.ShouldBe("web_search");
        tool.Tags.ShouldBe(["search", "web"]);
        tool.Description.ShouldBe("Search the public web");
    }

    [Test]
    public void Parse_UseDirective_ReturnsJobToolsAndSemantic()
    {
        var result = WorkflowDslParser.Parse("use(plan, web_search, memory, semantic: true)");

        var use = result[0].ShouldBeOfType<ConnectionLine.Use>();
        use.JobName.ShouldBe("plan");
        use.ToolNames.ShouldBe(["web_search", "memory"]);
        use.Semantic.ShouldBeTrue();
    }

    // ── IEnumerable<string> overload ─────────────────────────────────

    [Test]
    public void Parse_LinesEnumerable_Works()
    {
        var lines = new[] { "a -> b", "b -> End" };

        var result = WorkflowDslParser.Parse(lines);
        result.Count.ShouldBe(2);
    }

    // ── Mixed topology ───────────────────────────────────────────────

    [Test]
    public void Parse_MixedTopology_AllTypesRecognized()
    {
        var dsl = """
            plan -> fork(fetch_a, fetch_b)
            fetch_a -> transform_a
            fetch_b -> transform_b
            join(transform_a, transform_b) -> combine
            combine -> router(publish, archive)
            publish -> End
            archive -> End
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(7);
        result[0].ShouldBeOfType<ConnectionLine.Fork>();
        result[1].ShouldBeOfType<ConnectionLine.Direct>();
        result[2].ShouldBeOfType<ConnectionLine.Direct>();
        result[3].ShouldBeOfType<ConnectionLine.Join>();
        result[4].ShouldBeOfType<ConnectionLine.Router>();
        result[5].ShouldBeOfType<ConnectionLine.Direct>();
        result[6].ShouldBeOfType<ConnectionLine.Direct>();
    }

    [Test]
    public void Parse_MixedTopologyWithDirectives_AllTypesRecognized()
    {
        var dsl = """
            draft -> refine
            refine -> review
            review -> publish
            publish -> End
            subflow(refine)
            interrupt(publish)
            """;

        var result = WorkflowDslParser.Parse(dsl);
        result.Count.ShouldBe(6);
        result[0].ShouldBeOfType<ConnectionLine.Direct>();
        result[1].ShouldBeOfType<ConnectionLine.Direct>();
        result[2].ShouldBeOfType<ConnectionLine.Direct>();
        result[3].ShouldBeOfType<ConnectionLine.Direct>();
        result[4].ShouldBeOfType<ConnectionLine.SubFlow>().Name.ShouldBe("refine");
        result[5].ShouldBeOfType<ConnectionLine.Interrupt>().JobName.ShouldBe("publish");
    }
}
