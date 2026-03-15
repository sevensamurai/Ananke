using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Memory;
using Ananke.StateMachine;
using Connect4Demo;

// ═══════════════════════════════════════════════════════════════════
//  Connect 4 — Empirical Learning Demo
//
//  The agent starts knowing ONLY the rules (legal moves + win check).
//  Through play and post-game analysis, it discovers patterns, skills,
//  and heuristics — stored in IEmpiricalMemory — and improves.
//
//  No LLM required. The learning loop is the star:
//    play → analyze → commit → recall → play better
//
//  Usage:
//    Connect4Demo                    — play interactively
//    Connect4Demo --train            — train (50 iterations) then play
//    Connect4Demo --train 200        — train (200 iterations) then play
//    Connect4Demo --train --analyze  — train, print analysis report, exit
//    Connect4Demo --analyze 300      — analyze 300 iterations then exit
// ═══════════════════════════════════════════════════════════════════

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── Parse arguments ──────────────────────────────────────────────
var trainMode = false;
var analyzeMode = false;
var trainIterations = 50;

for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--train", StringComparison.OrdinalIgnoreCase))
    {
        trainMode = true;
    }
    else if (args[i].Equals("--analyze", StringComparison.OrdinalIgnoreCase))
    {
        analyzeMode = true;
        trainMode = true; // --analyze implies training
    }
    else if (int.TryParse(args[i], out var n) && n > 0)
    {
        trainIterations = n;
    }
}

// ── Memory setup (swap to QdrantEmpiricalMemory for persistence) ──
var embedder = new InMemoryEmbedder();
// Training happens in ms — use near-zero cooldown so reinforcement actually
// modulates strength. Interactive play has natural human-speed gaps anyway.
var affectOptions = trainMode
    ? new AffectOptions { ReinforcementCooldownHours = 0.001f }
    : new AffectOptions();
var memory = new InMemoryEmpiricalMemory(embedder, dedupThreshold: 0.85f, affectOptions: affectOptions);
var agent = new Connect4Agent(memory);
var analyzer = new GameAnalyzer(memory);
var stats = new GameStats();
var display = new Display(stats, memory);

// ── Training / analysis phase (optional) ─────────────────────────
if (trainMode)
{
    var trainer = new Trainer(memory, analyzer);

    if (analyzeMode)
    {
        await trainer.TrainAndAnalyzeAsync(trainIterations);
        return; // analysis-only mode — exit after report
    }

    await trainer.TrainAsync(trainIterations, stats);

    // Reset win/loss stats for interactive play but keep the memory
    stats.TotalGames = 0;
    stats.AgentWins = 0;
    stats.HumanWins = 0;
    stats.Draws = 0;
}

// ── State machine: Idle ↔ Playing ↔ Analyzing → Idle ────────────────
var machine = StateMachine.Create<Phase, Action>(Phase.Idle, b => b
    .From(Phase.Idle).On(Action.StartGame).To(Phase.Playing)
    .From(Phase.Playing).On(Action.GameOver).To(Phase.Analyzing)
    .From(Phase.Analyzing).On(Action.AnalysisDone).To(Phase.Idle));

// Insight handler — receives discoveries from the game analyzer
machine.OnInsight<string>((insight, state) =>
{
    display.AddInsight(insight);
    return Task.CompletedTask;
});

// ── Game loop ────────────────────────────────────────────────────────
while (true)
{
    await machine.FireAsync(Action.StartGame);

    var board = new Board();
    var winner = 0;
    var humanFirst = stats.TotalGames % 2 == 0; // alternate who goes first
    var currentPlayer = humanFirst ? 1 : 2;

    display.ClearReasoning();
    display.ClearInsights();
    display.ClearError();

    if (!humanFirst)
        display.SetStatus("Agent goes first this game.");
    else
        display.SetStatus("Your turn — enter column 1-7.");

    display.Render(board);

    while (winner == 0 && !board.IsFull())
    {
        int col, row;

        if (currentPlayer == 1) // Human turn
        {
            display.SetStatus("Your turn — enter 1-7, 'q' quit, 'm' memory");
            display.Render(board);

            Console.Write("  Your move: ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();
            var input = key.KeyChar.ToString();

            if (input is "q" or "Q")
            {
                await display.RenderFinalAsync();
                return;
            }

            if (input is "m" or "M")
            {
                await display.RenderMemoryAsync(board);
                continue;
            }

            if (!int.TryParse(input, out var parsed) || parsed < 1 || parsed > 7)
            {
                display.SetError("Enter a number 1-7, 'q' to quit, or 'm' for memory.");
                display.Render(board);
                continue;
            }

            col = parsed - 1;
            if (board.Height(col) >= Board.Rows)
            {
                display.SetError("That column is full. Try another.");
                display.Render(board);
                continue;
            }

            display.ClearError();
            row = board.Drop(col, 1);
        }
        else // Agent turn
        {
            display.SetStatus("Agent thinking...");
            display.ClearReasoning();
            display.Render(board);

            var (agentCol, reasoning) = await agent.ChooseMoveAsync(board);
            display.SetReasoning(reasoning);

            col = agentCol;
            row = board.Drop(col, 2);

            stats.TotalAgentMoves++;
            if (col == 3) stats.AgentCenterMoves++;

            display.SetStatus($"Agent plays column {col + 1}.");
            display.Render(board);
        }

        if (board.CheckWin(col, row))
            winner = currentPlayer;

        currentPlayer = currentPlayer == 1 ? 2 : 1;
    }

    // ── Game over ────────────────────────────────────────────────
    if (winner == 1)
    {
        display.SetStatus("🏆 You win!");
        stats.HumanWins++;
    }
    else if (winner == 2)
    {
        display.SetStatus("🤖 Agent wins!");
        stats.AgentWins++;
    }
    else
    {
        display.SetStatus("🤝 Draw!");
        stats.Draws++;
    }

    stats.TotalGames++;
    display.Render(board);

    // ── Post-game analysis ───────────────────────────────────────
    await machine.FireAsync(Action.GameOver);

    display.ClearInsights();
    var insights = await analyzer.AnalyzeAsync(board, winner, stats.TotalGames);
    foreach (var insight in insights)
        await machine.SignalInsightAsync(insight);

    if (insights.Count == 0)
        display.AddInsight("(no new insights this game)");

    display.RenderAnalysis(board);

    await machine.FireAsync(Action.AnalysisDone);

    Console.Write("  Play again? (y/n): ");
    var rematchKey = Console.ReadKey(intercept: true);
    var again = rematchKey.KeyChar.ToString();
    if (again is "n" or "N")
    {
        await display.RenderFinalAsync();
        break;
    }

    display.ClearInsights();
}

// ── State machine types ──────────────────────────────────────────────
internal enum Phase { Idle, Playing, Analyzing }
internal enum Action { StartGame, GameOver, AnalysisDone }
