using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;

namespace Ananke.Orchestration.Agents.Simulation;

/// <summary>How a <see cref="SimulatedAgentModel"/> should present itself.</summary>
public sealed record SimulatedModelOptions
{
    /// <summary>Input tokens to report for every call.</summary>
    public int InputTokens { get; init; } = 100;

    /// <summary>Output tokens to report for every call.</summary>
    public int OutputTokens { get; init; } = 50;

    /// <summary>
    /// The context window to declare, or <c>0</c> to declare none.
    /// </summary>
    /// <remarks>
    /// Worth setting whenever the run is being measured. Context instruments report fill and
    /// truncation against a known window, and a model that declares none degrades every one of those
    /// figures to "no call knew its window" — which reads like an instrument fault rather than a
    /// missing declaration.
    /// </remarks>
    public int ContextWindowTokens { get; init; }

    /// <summary>The name this model reports itself under.</summary>
    public string ModelName { get; init; } = "simulated";
}

/// <summary>
/// A model that answers from a script instead of from a provider.
/// </summary>
/// <remarks>
/// <para>
/// For demos, tests and a first workflow written before any key exists. It is deliberately the same
/// shape every hand-written fake converges on — a <see cref="Func{T,TResult}"/> from request to
/// reply — with the three ways of supplying one that people keep re-implementing: a fixed answer, a
/// serialized object, and a script.
/// </para>
/// <para>
/// <b>It knows nothing about what it is being used for.</b> It is handed a request and returns a
/// reply, which is the whole of what a model does. Anything a scripted answer needs to depend on has
/// to be visible <em>in the request</em>, because that is all a real model gets — a script keyed on
/// something passed alongside the request would pass while testing a channel that does not exist.
/// </para>
/// <para>
/// Every request is recorded. Asserting on what the model was <em>sent</em> — that a contract
/// reached it, that a tool result came back — is usually the point of using a fake at all.
/// </para>
/// </remarks>
public sealed class SimulatedAgentModel : IStreamingAgentModel, IModelContextResolver
{
    private readonly Func<AgentRequest, string> _responder;
    private readonly SimulatedModelOptions _options;
    private readonly ConcurrentQueue<AgentRequest> _requests = new();

    /// <summary>Creates a model that answers with <paramref name="responder"/>.</summary>
    public SimulatedAgentModel(
        Func<AgentRequest, string> responder, SimulatedModelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responder = responder;
        _options = options ?? new SimulatedModelOptions();
    }

    /// <summary>Every request this model has been sent, in order.</summary>
    public IReadOnlyList<AgentRequest> Requests => [.. _requests];

    /// <summary>How many times it has been called.</summary>
    public int Calls => _requests.Count;

    /// <summary>Answers <paramref name="reply"/> to everything.</summary>
    public static SimulatedAgentModel Fixed(string reply, SimulatedModelOptions? options = null) =>
        new(_ => reply, options);

    /// <summary>Answers with <paramref name="reply"/> serialized as JSON, to everything.</summary>
    public static SimulatedAgentModel Json<T>(T reply, SimulatedModelOptions? options = null) =>
        new(_ => JsonSerializer.Serialize(reply), options);

    /// <summary>
    /// Answers <paramref name="replies"/> in order, repeating the last one once they run out.
    /// </summary>
    public static SimulatedAgentModel Sequence(
        IReadOnlyList<string> replies, SimulatedModelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(replies);
        if (replies.Count == 0)
            throw new ArgumentException("A sequence needs at least one reply.", nameof(replies));

        return FromScript(
            new SimulatedScript { Responses = [new SimulatedResponse { Replies = replies }] }, options);
    }

    /// <summary>Answers from <paramref name="script"/>.</summary>
    public static SimulatedAgentModel FromScript(
        SimulatedScript script, SimulatedModelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        return new SimulatedAgentModel(new ScriptedResponder(script).Respond, options);
    }

    /// <summary>Answers from the JSON script at <paramref name="path"/>.</summary>
    /// <remarks>
    /// The reason this exists rather than being left to each caller: a scripted scenario in a file is
    /// a reviewable artifact. The same scenario written as a class is a code change that happens to
    /// alter what a model says, which is the one thing a reviewer of a scripted run needs to see.
    /// </remarks>
    public static SimulatedAgentModel FromFile(string path, SimulatedModelOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var script = JsonSerializer.Deserialize<SimulatedScript>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })
            ?? throw new InvalidOperationException($"'{path}' holds no script.");

        return FromScript(script, options);
    }

    /// <inheritdoc />
    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);

        return Task.FromResult(new AgentResponse
        {
            Text = _responder(request),
            Usage = new TokenUsage
            {
                InputTokens = _options.InputTokens,
                OutputTokens = _options.OutputTokens
            }
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GenerateAsync(request, ct).ConfigureAwait(false);

        foreach (var word in (response.Text ?? string.Empty).Split(' '))
        {
            ct.ThrowIfCancellationRequested();
            yield return new AgentStreamChunk { TextDelta = word + " " };
        }

        yield return new AgentStreamChunk { CompletedResponse = response };
    }

    /// <inheritdoc />
    public ModelContextWindow ResolveContextWindow(AgentRequest request) =>
        _options.ContextWindowTokens > 0
            ? new ModelContextWindow(_options.ModelName, _options.ContextWindowTokens)
            : ModelContextWindow.Unknown;

    /// <summary>
    /// Matches a request against a script, and remembers how often each entry has answered.
    /// </summary>
    private sealed class ScriptedResponder
    {
        private readonly SimulatedScript _script;
        private readonly ConcurrentDictionary<int, int> _uses = new();

        public ScriptedResponder(SimulatedScript script)
        {
            for (var i = 0; i < script.Responses.Count; i++)
            {
                if (script.Responses[i].Replies.Count == 0)
                    throw new ArgumentException(
                        $"Script entry {i} ({script.Responses[i].When ?? "any request"}) has no replies.",
                        nameof(script));
            }

            _script = script;
        }

        public string Respond(AgentRequest request)
        {
            var sent = Sent(request, SimulatedRequestPart.Anywhere);

            for (var i = 0; i < _script.Responses.Count; i++)
            {
                var response = _script.Responses[i];
                if (response.When is not null
                    && !Sent(request, response.WhenIn).Contains(response.When, StringComparison.Ordinal))
                    continue;

                var use = _uses.AddOrUpdate(i, 0, (_, n) => n + 1);
                return response.Replies[Math.Min(use, response.Replies.Count - 1)];
            }

            // Answering anyway would turn a request the script does not describe into a passing run
            // that proves nothing. What reached the model is the interesting part, so it is quoted.
            throw new InvalidOperationException(
                $"No scripted response matches this request. It was sent:{Environment.NewLine}{sent}");
        }

        /// <summary>What the model was sent, as one string to match against.</summary>
        private static string Sent(AgentRequest request, SimulatedRequestPart part)
        {
            var sent = new StringBuilder(
                part is SimulatedRequestPart.Messages ? null : request.SystemPrompt);

            if (part is not SimulatedRequestPart.SystemPrompt)
            {
                foreach (var message in request.Messages)
                    sent.AppendLine().Append(message.Content);
            }

            return sent.ToString();
        }
    }
}
