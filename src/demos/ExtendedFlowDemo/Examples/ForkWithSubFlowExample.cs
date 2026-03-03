using Ananke.Design;
using Ananke.Orchestration;
using Ananke.Orchestration.Routing;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Fork + SubFlow combined — parallel branches where one branch
/// is an entire nested workflow.
///
/// plan ──► fork(write_draft, find_images)
///          write_draft = SubFlow(write ↔ review loop)
///          find_images = simple job
///          └──► layout ──► End
/// </summary>
public static class ForkWithSubFlowExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 6 · Fork + SubFlow combined ━━━");
        Console.WriteLine();

        // Inner workflow: write → review loop (runs as one fork branch)
        var writeLoop = new Workflow<DraftState>("write-loop")
            .Job("write", async (state, ct) =>
            {
                Console.WriteLine($"    [write] Writing draft (attempt {state.Revisions + 1})...");
                await Task.Delay(200, ct);
                return state with
                {
                    Text = $"Draft v{state.Revisions + 1}: compelling article",
                    Revisions = state.Revisions + 1
                };
            })
            .Job("review", async (state, ct) =>
            {
                Console.WriteLine($"    [review] Reviewing draft v{state.Revisions}...");
                await Task.Delay(100, ct);
                return state with { Approved = state.Revisions >= 2 };
            })
            .Then("write", "review")
            .Then("review", Workflow.Decide<DraftState>(s => s.Approved ? Workflow.End : "write"));

        // Outer workflow: plan → fork(write_draft, find_images) → layout → End
        var workflow = new Workflow<ArticleState>("article-pipeline")
            .Job("plan", async (state, ct) =>
            {
                Console.WriteLine("  [plan] Planning article...");
                await Task.Delay(50, ct);
                return state with { Topic = "Ananke Workflow Engine" };
            })
            .SubFlow("write_draft", writeLoop,
                parent => new DraftState { Text = parent.Topic },
                (parent, child) => parent with { Body = child.Text })
            .Job("find_images", async (state, ct) =>
            {
                Console.WriteLine("  [find_images] Searching stock photos...");
                await Task.Delay(400, ct);
                return state with { Images = ["hero.jpg", "diagram.png", "logo.svg"] };
            })
            .Job("layout", async (state, ct) =>
            {
                Console.WriteLine("  [layout] Composing final article...");
                await Task.Delay(100, ct);
                return state with
                {
                    Output = $"'{state.Body}' with {state.Images.Count} images"
                };
            })
            .Then("plan", Workflow.Fork("write_draft", "find_images"))
            .Join(["write_draft", "find_images"], "layout", branches =>
            {
                var draft = branches.FirstOrDefault(b => b.Body is not null);
                var images = branches.FirstOrDefault(b => b.Images.Count > 0);
                return new ArticleState
                {
                    Topic = branches[0].Topic,
                    Body = draft?.Body ?? "",
                    Images = images?.Images ?? []
                };
            })
            .Then("layout", Workflow.End);

        var result = await workflow.RunAsync(new ArticleState());

        ConsoleLogger<ArticleState>.PrintResults(result, workflow.ToMermaid(), s => s.Output ?? "");
    }

    record DraftState
    {
        public string Text { get; init; } = "";
        public int Revisions { get; init; }
        public bool Approved { get; init; }
    }

    record ArticleState
    {
        public string Topic { get; init; } = "";
        public string? Body { get; init; }
        public List<string> Images { get; init; } = [];
        public string? Output { get; init; }
    }
}
