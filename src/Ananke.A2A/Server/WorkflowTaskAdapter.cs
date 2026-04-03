using A2A;
using Ananke.Orchestration;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;

namespace Ananke.A2A.Server;

/// <summary>
/// Adapts an Ananke workflow execution function to the A2A <see cref="TaskManager"/> callback model.
/// Bridges incoming A2A messages to workflow runs and maps results back to A2A responses.
/// </summary>
/// <remarks>
/// Attach this adapter to a <see cref="TaskManager"/> to expose any Ananke workflow
/// (or custom processing function) as an A2A-compliant agent endpoint.
/// </remarks>
/// <example>
/// <code>
/// var taskManager = new TaskManager();
/// var adapter = new WorkflowTaskAdapter(async (text, ct) =>
/// {
///     var result = await workflow.RunAsync(new MyState { Input = text }, ct);
///     return result.FinalState.Output;
/// });
///
/// adapter.Attach(taskManager, agentCardBuilder.Build("http://localhost:5100/agent"));
/// app.MapA2A(taskManager, "/agent");
/// </code>
/// </example>
public sealed class WorkflowTaskAdapter
{
    private readonly Func<string, CancellationToken, Task<string>> _processAsync;

    /// <summary>
    /// Creates an adapter with a simple text-in / text-out processing function.
    /// </summary>
    /// <param name="processAsync">
    /// An async function that receives the user's text input and returns the agent's text output.
    /// This is the bridge point where you invoke your Ananke workflow, agent job, or any processing logic.
    /// </param>
    public WorkflowTaskAdapter(Func<string, CancellationToken, Task<string>> processAsync)
    {
        ArgumentNullException.ThrowIfNull(processAsync);
        _processAsync = processAsync;
    }

    /// <summary>
    /// Wires this adapter into the given <see cref="TaskManager"/>, configuring
    /// message handling and agent card queries.
    /// </summary>
    /// <param name="taskManager">The A2A task manager to attach to.</param>
    /// <param name="agentCard">The <see cref="AgentCard"/> describing this agent.</param>
    public void Attach(TaskManager taskManager, AgentCard agentCard)
    {
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentNullException.ThrowIfNull(agentCard);

        taskManager.OnMessageReceived = async (messageSendParams, ct) =>
        {
            var message = await ProcessMessageAsync(messageSendParams, ct).ConfigureAwait(false);
            return message;
        };

        taskManager.OnAgentCardQuery = (agentUrl, ct) =>
        {
            var card = new AgentCard
            {
                Name = agentCard.Name,
                Description = agentCard.Description,
                Url = agentUrl,
                Version = agentCard.Version,
                DefaultInputModes = agentCard.DefaultInputModes,
                DefaultOutputModes = agentCard.DefaultOutputModes,
                Capabilities = agentCard.Capabilities,
                Skills = agentCard.Skills
            };
            return Task.FromResult(card);
        };
    }

    private async Task<global::A2A.AgentMessage> ProcessMessageAsync(
        MessageSendParams messageSendParams,
        CancellationToken ct)
    {
        var inputText = ExtractInputText(messageSendParams.Message);

        string resultText;
        try
        {
            resultText = await _processAsync(inputText, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            resultText = $"Error: {ex.Message}";
        }

        return new global::A2A.AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = messageSendParams.Message.ContextId,
            Parts = [new global::A2A.TextPart { Text = resultText }]
        };
    }

    private static string ExtractInputText(global::A2A.AgentMessage message)
    {
        if (message.Parts is null or { Count: 0 })
            return string.Empty;

        var texts = message.Parts
            .OfType<global::A2A.TextPart>()
            .Select(p => p.Text)
            .Where(t => !string.IsNullOrEmpty(t));

        return string.Join("\n", texts);
    }

    /// <summary>
    /// Creates a <see cref="WorkflowTaskAdapter"/> from an <see cref="IAgentModel"/>,
    /// converting the A2A message to an <see cref="AgentRequest"/> and mapping the
    /// <see cref="AgentResponse"/> back.
    /// </summary>
    /// <param name="model">The Ananke agent model to wrap.</param>
    /// <param name="systemPrompt">Optional system prompt for the model.</param>
    public static WorkflowTaskAdapter FromAgentModel(IAgentModel model, string? systemPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new WorkflowTaskAdapter(async (input, ct) =>
        {
            var request = new AgentRequest
            {
                SystemPrompt = systemPrompt,
                Messages = [global::Ananke.Abstractions.Agents.AgentMessage.User(input)]
            };

            var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
            return response.Text ?? string.Empty;
        });
    }
}
