using Ananke.Design;
using Ananke.Orchestration;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// SubFlow — nested workflow composition.
/// draft ──► refine (SubFlow: edit ↔ validate loop) ──► publish ──► End
/// </summary>
public static class NestedSubFlowExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 4 · SubFlow (nested workflow) ━━━");
        Console.WriteLine();

        var editLoop = new Workflow<EditState>("edit-loop")
            .Job("edit", async (state, ct) =>
            {
                Console.WriteLine($"    [edit] Editing (attempt {state.Attempts + 1})...");
                await Task.Delay(100, ct);
                return state with
                {
                    Text = $"v{state.Attempts + 1}: polished content",
                    Attempts = state.Attempts + 1
                };
            })
            .Job("validate", async (state, ct) =>
            {
                Console.WriteLine($"    [validate] Validating (attempt {state.Attempts})...");
                await Task.Delay(50, ct);
                return state with { Valid = state.Attempts >= 2 };
            })
            .Then("edit", "validate")
            .Then("validate", Workflow.Decide<EditState>(s => s.Valid ? Workflow.End : "edit"));

        var workflow = new Workflow<DocState>("document-pipeline")
            .Job("draft", async (state, ct) =>
            {
                Console.WriteLine("  [draft] Creating initial draft...");
                await Task.Delay(150, ct);
                return state with { Draft = "rough draft content" };
            })
            .SubFlow("refine", editLoop,
                parent => new EditState { Text = parent.Draft },
                (parent, child) => parent with { Draft = child.Text })
            .Job("publish", async (state, ct) =>
            {
                Console.WriteLine("  [publish] Publishing final content...");
                await Task.Delay(100, ct);
                return state with { Published = true };
            })
            .Chain("draft", "refine", "publish")
            .Then("publish", Workflow.End);

        var result = await workflow.RunAsync(new DocState());

        ConsoleLogger<DocState>.PrintResults(result, workflow.ToMermaid(),
            s => $"'{s.Draft}' | Published: {s.Published}");
    }

    record EditState
    {
        public string Text { get; init; } = "";
        public int Attempts { get; init; }
        public bool Valid { get; init; }
    }

    record DocState
    {
        public string Draft { get; init; } = "";
        public bool Published { get; init; }
    }
}
