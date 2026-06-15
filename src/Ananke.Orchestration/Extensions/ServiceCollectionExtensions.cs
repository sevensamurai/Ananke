using System.Threading.Channels;
using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Trajectory;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Agents.Trajectory;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tools.Gating;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
            services.AddHostedService(sp =>
            {
                var memory = sp.GetRequiredService<IConversationMemory>();
                var loggerFactory = sp.GetService<ILoggerFactory>();
                var timeProvider = sp.GetService<TimeProvider>();
                return new ConversationMemoryCleanupTimer(memory, interval, loggerFactory, timeProvider);
            });
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="CompositeAdaptiveHarnessPolicy"/> as both
    /// <see cref="IAdaptiveHarnessPolicy"/> and <see cref="ITrajectoryObserver"/>.
    /// Also registers a <see cref="ToolAffinityTracker"/> singleton if one is not already present.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddAdaptiveHarness(o =>
    /// {
    ///     o.KitName                = "ops";
    ///     o.HallucinationThreshold = 2;
    ///     o.AbandonedFaultPenalty  = -0.8f;
    ///     o.SuccessReward          = 1.0f;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAdaptiveHarness(
        this IServiceCollection services,
        Action<AdaptiveHarnessOptions>? configure = null)
    {
        var options = new AdaptiveHarnessOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ToolAffinityTracker>();
        services.AddSingleton(options);

        services.AddSingleton<CompositeAdaptiveHarnessPolicy>(sp =>
            new CompositeAdaptiveHarnessPolicy(
                sp.GetRequiredService<ToolAffinityTracker>(),
                options,
                sp.GetService<ILearningCycleTrigger>(),
                sp.GetService<ILogger<CompositeAdaptiveHarnessPolicy>>()));

        services.AddSingleton<IAdaptiveHarnessPolicy>(sp =>
            sp.GetRequiredService<CompositeAdaptiveHarnessPolicy>());

        // Background channel: bounded so heavy hallucination runs don't OOM.
        // ChannelTrajectoryObserver writes non-blocking; AdaptationQueueWorker drains off the hot path.
        var channel = Channel.CreateBounded<TrajectorySnapshot>(
            new BoundedChannelOptions(options.AdaptationChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
        services.AddSingleton(channel);

        services.AddSingleton<ITrajectoryObserver>(sp =>
            new ChannelTrajectoryObserver(
                sp.GetRequiredService<Channel<TrajectorySnapshot>>(),
                sp.GetService<ILogger<ChannelTrajectoryObserver>>()));

        services.AddHostedService(sp =>
            new AdaptationQueueWorker(
                sp.GetRequiredService<Channel<TrajectorySnapshot>>(),
                sp.GetRequiredService<IAdaptiveHarnessPolicy>(),
                sp.GetService<ILogger<AdaptationQueueWorker>>()));

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
