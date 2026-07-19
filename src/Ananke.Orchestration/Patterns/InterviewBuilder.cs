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
    /// it when its own pending-input wait exceeds <see cref="TurnTimeout"/>. By design,
    /// a turn timeout pauses, it never aborts — the execution stays checkpointed and resumable,
    /// and the next reply resumes it via <see cref="FoldAnswer"/> exactly like an on-time one.
    /// </summary>
    public const string DefaultPauseMessage =
        "No rush — I've paused our conversation. Just reply whenever and we'll pick up right where we left off.";

    /// <summary>The assembled workflow: welcome → icebreaker → loop(ask_question, exit: End).</summary>
    public Workflow<TState> Workflow { get; }

    /// <summary>
    /// Reads the question to show the user from the paused state (the <c>WithQuestion</c>
    /// selector). Pure — does not touch conversation memory. Call this when the run/resume
    /// result is <see cref="ExecutionStatus.Interrupted"/> at the turn job, then call
    /// <see cref="CommitTurnAsync"/> once the turn is actually complete.
    /// </summary>
    public Func<TState, CancellationToken, Task<string>> GetQuestion { get; }

    /// <summary>
    /// Folds the user's free-text reply into state (the <c>WithNavigation</c> expand/skip/update
    /// delegate). Pure — does not touch conversation memory. The reply only exists at resume
    /// time, so call this <em>before</em>
    /// <see cref="Workflow{TState}.ResumeAsync(string, Func{TState, TState}, CancellationToken)"/>
    /// and pass a transform that returns the already-computed result, e.g.
    /// <c>var next = await interview.FoldAnswer(state, reply, ct); await workflow.ResumeAsync(id, _ =&gt; next, ct);</c>.
    /// Call <see cref="CommitTurnAsync"/> after the resume succeeds to persist the turn.
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

    private readonly IConversationMemory? _memory;
    private readonly Func<TState, string>? _conversationId;

    internal Interview(
        Workflow<TState> workflow,
        Func<TState, CancellationToken, Task<string>> getQuestion,
        Func<TState, string, CancellationToken, Task<TState>> foldAnswer,
        TimeSpan? turnTimeout,
        string pauseMessage,
        IConversationMemory? memory,
        Func<TState, string>? conversationId)
    {
        Workflow = workflow;
        GetQuestion = getQuestion;
        FoldAnswer = foldAnswer;
        TurnTimeout = turnTimeout;
        PauseMessage = pauseMessage;
        _memory = memory;
        _conversationId = conversationId;
    }

    /// <summary>
    /// Persists a completed turn (the question shown and the answer received) to conversation
    /// memory, as one assistant(<paramref name="question"/>) + user(<paramref name="answer"/>)
    /// write — a no-op when <c>WithMemory</c> was not configured.
    /// </summary>
    /// <param name="state">The state as of the resumed turn — used to resolve the conversation id.</param>
    /// <param name="question">The exact question text shown to the user (the value <see cref="GetQuestion"/> returned).</param>
    /// <param name="answer">The exact reply text the user gave.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Call this <em>after</em> <see cref="Workflow{TState}.ResumeAsync(string, Func{TState, TState}, CancellationToken)"/>
    /// has succeeded, not before — writing the transcript before the resume is confirmed risks a
    /// desync where memory shows an answer the workflow itself never recorded (if the resume then
    /// fails). This replaces the old behavior where <see cref="GetQuestion"/>/<see cref="FoldAnswer"/>
    /// wrote to memory eagerly, which could duplicate the question (if a host called
    /// <see cref="GetQuestion"/> more than once, e.g. across a retry) or desync the answer.
    /// </para>
    /// <para>
    /// <b>Idempotent</b>: safe to call more than once for the same turn — if the tail of the
    /// existing history already matches this exact (question, answer) pair, the call is a no-op
    /// rather than writing a duplicate. There is no separate turn counter; hosts are expected to
    /// call this once per successful resume, and the tail-comparison exists to tolerate an
    /// accidental repeat (e.g. a host retry), not to replace that expectation.
    /// </para>
    /// </remarks>
    public async Task CommitTurnAsync(
        TState state, string question, string answer, CancellationToken ct = default)
    {
        if (_memory is null || _conversationId is null)
            return;

        var sessionId = _conversationId(state);
        var history = await _memory.GetHistoryAsync(sessionId, ct);

        if (history.Count >= 2)
        {
            var priorQuestion = history[^2];
            var priorAnswer = history[^1];
            if (priorQuestion.Role == AgentRole.Assistant && priorQuestion.Content == question &&
                priorAnswer.Role == AgentRole.User && priorAnswer.Content == answer)
                return; // already committed
        }

        await _memory.AddAsync(
            sessionId, [AgentMessage.Assistant(question), AgentMessage.User(answer)], ct);
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
///   <item>Once the resume call above has returned successfully, call
///   <c>await interview.CommitTurnAsync(next, question, reply, ct)</c> to persist the turn to
///   conversation memory (no-op if <c>WithMemory</c> wasn't configured). <c>GetQuestion</c> and
///   <c>FoldAnswer</c> are pure — persistence only happens here, and only once the resume is
///   confirmed, so a failed resume never leaves a transcript entry for a turn the workflow
///   didn't actually record.</item>
/// </list>
/// <para>
/// Welcome and icebreaker are ordinary jobs (run once, before the loop) — provide them to
/// open with a greeting before the first question. Both are optional; when neither is
/// configured, <see cref="Build"/> registers a no-op <c>start</c> job as the workflow's entry
/// point instead (<c>start → ask_question ⇄ loop(...)</c>) — <c>ask_question</c> pauses via
/// <c>AwaitInput</c>, which cannot be applied to a workflow's entry job, so something has to
/// occupy that slot even when there's nothing to say before the first question.
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

        if (previous is null)
        {
            // No welcome or icebreaker: ask_question pauses via AwaitInput(), which cannot be
            // applied to the workflow's entry job, so a no-op job takes the entry slot instead.
            workflow.Job("start", (state, _) => Task.FromResult(state));
            previous = "start";
        }

        workflow.Job("ask_question", (state, _) => Task.FromResult(state));
        workflow.Then(previous, "ask_question");

        workflow
            .AwaitInput("ask_question")
            .Loop("ask_question", loopTarget: "ask_question", exitTarget: Workflow.End,
                  until: _until, maxIterations: _maxTurns);

        var question = _question;
        var navigation = _navigation;

        // Pure — no conversation-memory writes here. Persistence happens post-commit via
        // Interview<TState>.CommitTurnAsync, called by the host after ResumeAsync succeeds.
        Func<TState, CancellationToken, Task<string>> getQuestion =
            (state, _) => Task.FromResult(question(state));

        Func<TState, string, CancellationToken, Task<TState>> foldAnswer =
            (state, answer, _) => Task.FromResult(navigation(answer, state));

        return new Interview<TState>(
            workflow, getQuestion, foldAnswer, _turnTimeout, Interview<TState>.DefaultPauseMessage,
            _memory, _conversationId);
    }
}
