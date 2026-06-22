using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration.Patterns;

/// <summary>
/// The product of <see cref="InterviewBuilder{TState}.Build"/>: the assembled
/// <see cref="Workflow{TState}"/> plus the two host-side hooks a conversational turn needs
/// but the workflow itself cannot run, because they happen outside a job execution — at
/// pause (<see cref="GetQuestion"/>) and at resume (<see cref="FoldAnswer"/>).
/// </summary>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed class Interview<TState>
{
    /// <summary>
    /// Default message posted when a turn has gone quiet long enough that the host treats it
    /// as paused rather than abandoned. The framework never sends this itself — the host shows
    /// it when its own pending-input wait exceeds <see cref="TurnTimeout"/>. See ADR-arch-023 §4.4:
    /// a turn timeout pauses, it never aborts — the execution stays checkpointed and resumable,
    /// and the next reply resumes it via <see cref="FoldAnswer"/> exactly like an on-time one.
    /// </summary>
    public const string DefaultPauseMessage =
        "No rush — I've paused our conversation. Just reply whenever and we'll pick up right where we left off.";

    /// <summary>The assembled workflow: welcome → icebreaker → loop(ask_question, exit: End).</summary>
    public Workflow<TState> Workflow { get; }

    /// <summary>
    /// Reads the question to show the user from the paused state (the <c>WithQuestion</c>
    /// selector), writing it to conversation memory first if <c>WithMemory</c> was configured.
    /// Call this when the run/resume result is <see cref="ExecutionStatus.Interrupted"/> at the
    /// turn job.
    /// </summary>
    public Func<TState, CancellationToken, Task<string>> GetQuestion { get; }

    /// <summary>
    /// Folds the user's free-text reply into state (the <c>WithNavigation</c> expand/skip/update
    /// delegate), writing the reply to conversation memory first if <c>WithMemory</c> was
    /// configured. The reply only exists at resume time, so call this <em>before</em>
    /// <see cref="Workflow{TState}.ResumeAsync(string, Func{TState, TState}, CancellationToken)"/>
    /// and pass a transform that returns the already-computed result, e.g.
    /// <c>var next = await interview.FoldAnswer(state, reply, ct); await workflow.ResumeAsync(id, _ =&gt; next, ct);</c>.
    /// </summary>
    public Func<TState, string, CancellationToken, Task<TState>> FoldAnswer { get; }

    /// <summary>
    /// Optional turn timeout, set via <c>WithTurnTimeout</c>. The framework does not run a
    /// timer itself — the host owns the pending-input wait and consults this value to decide
    /// when a turn has gone quiet long enough to show <see cref="PauseMessage"/>.
    /// </summary>
    public TimeSpan? TurnTimeout { get; }

    /// <summary>The message to show the user when a turn times out. See <see cref="DefaultPauseMessage"/>.</summary>
    public string PauseMessage { get; }

    internal Interview(
        Workflow<TState> workflow,
        Func<TState, CancellationToken, Task<string>> getQuestion,
        Func<TState, string, CancellationToken, Task<TState>> foldAnswer,
        TimeSpan? turnTimeout,
        string pauseMessage)
    {
        Workflow = workflow;
        GetQuestion = getQuestion;
        FoldAnswer = foldAnswer;
        TurnTimeout = turnTimeout;
        PauseMessage = pauseMessage;
    }
}

/// <summary>
/// Fluent builder for the <b>Interview</b> (conversational) agentic pattern: a multi-turn,
/// human-driven exchange that walks a question agenda held in <typeparamref name="TState"/>.
/// </summary>
/// <remarks>
/// <para><b>Generated workflow topology:</b></para>
/// <code>
/// welcome → icebreaker → ask_question ⇄ loop(ask_question, exit: __end__)
///                              ↑ AwaitInput — pauses before each turn
/// </code>
/// <para>
/// <c>ask_question</c> is a no-op anchor job. The question text and the navigation
/// (expand/skip/update) both happen <em>outside</em> the workflow run, because the user's
/// reply only exists at resume time, not while a job is executing:
/// </para>
/// <list type="number">
///   <item>Run/resume the workflow; on <see cref="ExecutionStatus.Interrupted"/>, call
///   <see cref="Interview{TState}.GetQuestion"/> on the checkpointed state and show it to
///   the user.</item>
///   <item>On reply, resume via
///   <c>var next = await interview.FoldAnswer(state, reply, ct); await workflow.ResumeAsync(executionId, _ =&gt; next, ct);</c>.</item>
/// </list>
/// <para>
/// Welcome and icebreaker are ordinary jobs (run once, before the loop) — provide them to
/// open with a greeting before the first question.
/// </para>
/// <para>
/// Create instances via <see cref="AgenticPattern.Interview{TState}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed class InterviewBuilder<TState>
{
    private const int DefaultMaxTurns = 20;

    private readonly string _name;
    private Func<TState, CancellationToken, Task<TState>>? _welcome;
    private Func<TState, CancellationToken, Task<TState>>? _icebreaker;
    private Func<TState, string>? _question;
    private Func<string, TState, TState>? _navigation;
    private Func<TState, bool>? _until;
    private int _maxTurns = DefaultMaxTurns;
    private TimeSpan? _turnTimeout;
    private IConversationMemory? _memory;
    private Func<TState, string>? _conversationId;

    internal InterviewBuilder(string name) => _name = name;

    /// <summary>Optional opening message, run once before the icebreaker/first question.</summary>
    public InterviewBuilder<TState> WithWelcome(Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _welcome = execute;
        return this;
    }

    /// <summary>Optional icebreaker message, run once after the welcome and before the first question.</summary>
    public InterviewBuilder<TState> WithIcebreaker(Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _icebreaker = execute;
        return this;
    }

    /// <summary>
    /// Selects the question to show the user on each turn — typically the agenda head. Read by
    /// the host via <see cref="Interview{TState}.GetQuestion"/> after the workflow pauses; never
    /// executed inside the workflow.
    /// </summary>
    public InterviewBuilder<TState> WithQuestion(Func<TState, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _question = selector;
        return this;
    }

    /// <summary>
    /// The LLM-driven expand/skip/update fold applied to the user's reply. Invoked by the host
    /// via <see cref="Interview{TState}.FoldAnswer"/> as the resume <c>stateTransform</c> — not
    /// executed inside the workflow, because the reply only exists at resume time.
    /// </summary>
    public InterviewBuilder<TState> WithNavigation(Func<string, TState, TState> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _navigation = navigate;
        return this;
    }

    /// <summary>Sets the loop-exit predicate, evaluated after each turn (e.g. agenda empty).</summary>
    public InterviewBuilder<TState> Until(Func<TState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _until = predicate;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of turns. Default is 20. When reached without
    /// <see cref="Until"/> returning <c>true</c>, the loop exits with the current state.
    /// </summary>
    public InterviewBuilder<TState> MaxTurns(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxTurns = max;
        return this;
    }

    /// <summary>
    /// Sets the turn timeout, exposed on the built <see cref="Interview{TState}"/> as
    /// <see cref="Interview{TState}.TurnTimeout"/>. The framework does not enforce this itself
    /// — the host owns the pending-input wait and decides when a quiet turn should be treated
    /// as paused (not aborted).
    /// </summary>
    public InterviewBuilder<TState> WithTurnTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _turnTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Writes each turn (question and answer) to <paramref name="memory"/> automatically, keyed
    /// by <paramref name="conversationId"/>(state). Optional — without it,
    /// <see cref="Interview{TState}.GetQuestion"/>/<see cref="Interview{TState}.FoldAnswer"/>
    /// still work, they just don't persist a transcript.
    /// </summary>
    public InterviewBuilder<TState> WithMemory(IConversationMemory memory, Func<TState, string> conversationId)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(conversationId);
        _memory = memory;
        _conversationId = conversationId;
        return this;
    }

    /// <summary>
    /// Validates the configuration and builds the <see cref="Interview{TState}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required part is missing.</exception>
    public Interview<TState> Build()
    {
        if (_question is null)
            throw new InvalidOperationException(
                $"Interview '{_name}': question selector is required. Call WithQuestion().");

        if (_navigation is null)
            throw new InvalidOperationException(
                $"Interview '{_name}': navigation delegate is required. Call WithNavigation().");

        if (_until is null)
            throw new InvalidOperationException(
                $"Interview '{_name}': termination predicate is required. Call Until().");

        if (_welcome is null && _icebreaker is null)
            throw new InvalidOperationException(
                $"Interview '{_name}': at least one of WithWelcome() or WithIcebreaker() is " +
                "required — the turn job pauses via AwaitInput(), which cannot be applied to " +
                "the workflow's entry job.");

        var workflow = new Workflow<TState>(_name);

        string? previous = null;
        if (_welcome is not null)
        {
            workflow.Job("welcome", _welcome);
            previous = "welcome";
        }

        if (_icebreaker is not null)
        {
            workflow.Job("icebreaker", _icebreaker);
            if (previous is not null)
                workflow.Then(previous, "icebreaker");
            previous = "icebreaker";
        }

        workflow.Job("ask_question", (state, _) => Task.FromResult(state));
        if (previous is not null)
            workflow.Then(previous, "ask_question");

        workflow
            .AwaitInput("ask_question")
            .Loop("ask_question", loopTarget: "ask_question", exitTarget: Workflow.End,
                  until: _until, maxIterations: _maxTurns);

        var question = _question;
        var navigation = _navigation;
        var memory = _memory;
        var conversationId = _conversationId;

        Func<TState, CancellationToken, Task<string>> getQuestion = memory is null
            ? (state, _) => Task.FromResult(question(state))
            : async (state, ct) =>
            {
                var text = question(state);
                await memory.AddAsync(conversationId!(state), AgentMessage.Assistant(text), ct);
                return text;
            };

        Func<TState, string, CancellationToken, Task<TState>> foldAnswer = memory is null
            ? (state, answer, _) => Task.FromResult(navigation(answer, state))
            : async (state, answer, ct) =>
            {
                await memory.AddAsync(conversationId!(state), AgentMessage.User(answer), ct);
                return navigation(answer, state);
            };

        return new Interview<TState>(
            workflow, getQuestion, foldAnswer, _turnTimeout, Interview<TState>.DefaultPauseMessage);
    }
}
