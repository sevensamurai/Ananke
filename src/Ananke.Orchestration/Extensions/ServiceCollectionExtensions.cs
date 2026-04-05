using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ananke.Orchestration.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ananke workflow orchestration infrastructure: <see cref="IWorkflowRunner"/> with
    /// optional checkpointing and tracing resolved from the container.
    /// </summary>
    public static IServiceCollection AddWorkflowOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRunner>(sp =>
        {
            var store = sp.GetService<ICheckpointStore>();
            var tracer = sp.GetService<IWorkflowTracer>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new WorkflowRunner(store, tracer: tracer, loggerFactory: loggerFactory);
        });
        return services;
    }

    /// <summary>
    /// Registers the Ananke workflow orchestration infrastructure with the specified options.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddWorkflowOrchestration(o => o
    ///     .UseCheckpointing()
    ///     .UseMemoryCleanup(TimeSpan.FromMinutes(5))
    ///     .StoreCompletions(false)
    ///     .WithCheckpointTtl(TimeSpan.FromDays(14)));
    /// </code>
    /// </example>
    public static IServiceCollection AddWorkflowOrchestration(
        this IServiceCollection services,
        Action<OrchestrationOptions> configure)
    {
        var options = new OrchestrationOptions();
        configure(options);

        if (options.UseInMemoryCheckpoints)
            services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();

        services.AddSingleton<IWorkflowRunner>(sp =>
        {
            var store = sp.GetService<ICheckpointStore>();
            var tracer = sp.GetService<IWorkflowTracer>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new WorkflowRunner(store, tracer: tracer,
                storeCompletions: options.StoreCompletionsEnabled,
                loggerFactory: loggerFactory,
                checkpointTtl: options.CheckpointTtl);
        });

        if (options.MemoryCleanupInterval is { } interval)
        {
            services.AddSingleton(sp =>
            {
                var memory = sp.GetRequiredService<IConversationMemory>();
                var loggerFactory = sp.GetService<ILoggerFactory>();
                return new ConversationMemoryCleanupTimer(memory, interval, loggerFactory);
            });
        }

        return services;
    }
}

/// <summary>
/// Configuration options for the workflow orchestration DI registration.
/// </summary>
public class OrchestrationOptions
{
    internal bool UseInMemoryCheckpoints { get; private set; }
    internal bool StoreCompletionsEnabled { get; private set; } = true;
    internal TimeSpan CheckpointTtl { get; private set; } = TimeSpan.FromDays(7);
    internal TimeSpan? MemoryCleanupInterval { get; private set; }

    public OrchestrationOptions UseCheckpointing()
    {
        UseInMemoryCheckpoints = true;
        return this;
    }

    /// <summary>
    /// Controls whether LLM completions are stored in the provider's platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>true</c>.
    /// </summary>
    public OrchestrationOptions StoreCompletions(bool enabled)
    {
        StoreCompletionsEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets how long checkpoints are retained after creation. Defaults to 7 days.
    /// </summary>
    public OrchestrationOptions WithCheckpointTtl(TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        CheckpointTtl = ttl;
        return this;
    }

    /// <summary>
    /// Enables periodic cleanup of expired conversation memory sessions.
    /// Requires an <see cref="IConversationMemory"/> to be registered in the container.
    /// </summary>
    /// <param name="interval">
    /// How often to run cleanup. Typical values: 1–10 minutes for in-memory,
    /// 30–60 minutes for external stores.
    /// </param>
    public OrchestrationOptions UseMemoryCleanup(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        MemoryCleanupInterval = interval;
        return this;
    }
}
