using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// A model that knows which model it is, and how big its context window actually is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a property on the adapter.</b> Found live: an OpenAI run's context baseline
/// reported <c>window known 0</c> and <c>n/a</c> for fill and truncation for every call, so the
/// instrument that exists to measure context pressure measured nothing against a real provider. The
/// obvious fix — have the adapter state its own window — is wrong, and would be confidently wrong:
/// <c>OpenAIChatAgentModel</c> is the adapter for Ollama, LM Studio, vLLM and Azure deployments too,
/// and a name-keyed table inside it would answer for <c>llama3.2</c> with OpenAI's numbers. It would
/// also be a <em>second</em> place model metadata lives, beside <see cref="ModelCatalog"/>.
/// </para>
/// <para>
/// <b>So the window comes from a profile, and this makes attaching one a line of code.</b> The
/// metadata already has a home — a <see cref="ModelProfileTemplate"/> per known model — and a router
/// already resolves windows from it. What was missing is that binding one model to its own facts
/// required building a router to hold a single route.
/// </para>
/// <para>
/// <b>The window is the effective one, not the advertised one.</b> A local deployment that serves a
/// 128K model with an 8K window says 8K here, and is right to: what the strategy needs is the window
/// this endpoint will actually honour.
/// </para>
/// </remarks>
public sealed class ProfiledAgentModel : IStreamingAgentModel, IModelContextResolver
{
    private readonly IAgentModel _inner;
    private readonly ModelContextWindow _window;

    /// <summary>Binds <paramref name="inner"/> to the model name and window it is serving.</summary>
    /// <param name="inner">The model that does the work.</param>
    /// <param name="modelName">What it is, for per-model reporting.</param>
    /// <param name="contextWindowTokens">
    /// The window this endpoint honours. <c>0</c> means unknown, which is the same as not wrapping.
    /// </param>
    public ProfiledAgentModel(IAgentModel inner, string modelName, int contextWindowTokens)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentOutOfRangeException.ThrowIfNegative(contextWindowTokens);

        _inner = inner;
        _window = new ModelContextWindow(modelName, contextWindowTokens);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same answer for every request. A router answers per request because it may pick a
    /// different model each time; one model bound to one profile cannot.
    /// </remarks>
    public ModelContextWindow ResolveContextWindow(AgentRequest request) => _window;

    /// <inheritdoc />
    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
        _inner.GenerateAsync(request, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request, CancellationToken ct = default) =>
        _inner is IStreamingAgentModel streaming
            ? streaming.GenerateStreamAsync(request, ct)
            : BufferAsync(request, ct);

    /// <summary>A non-streaming model, answered as one chunk and its completion.</summary>
    private async IAsyncEnumerable<AgentStreamChunk> BufferAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await _inner.GenerateAsync(request, ct).ConfigureAwait(false);

        if (response.Text is not null)
            yield return new AgentStreamChunk { TextDelta = response.Text };

        yield return new AgentStreamChunk { CompletedResponse = response };
    }
}

/// <summary>Telling a model which model it is.</summary>
public static class AgentModelProfileExtensions
{
    /// <summary>
    /// Binds <paramref name="model"/> to <paramref name="template"/>, so everything that reads a
    /// context window — the context strategy, the baseline, truncation and fill — has one to read.
    /// </summary>
    /// <example>
    /// <code>
    /// var worker = OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini")
    ///     .WithProfile(ModelCatalog.OpenAI.Gpt4_1Mini);
    /// </code>
    /// </example>
    public static ProfiledAgentModel WithProfile(this IAgentModel model, ModelProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ProfiledAgentModel(model, template.Name, template.MaxContextTokens);
    }

    /// <summary>
    /// Binds <paramref name="model"/> to a name and the window this endpoint actually honours — for
    /// a deployment whose window is not the published one, or a model no catalogue knows.
    /// </summary>
    public static ProfiledAgentModel WithContextWindow(
        this IAgentModel model, string modelName, int contextWindowTokens) =>
        new(model, modelName, contextWindowTokens);
}
