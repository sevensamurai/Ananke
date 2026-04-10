using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ananke.Platforms.Discord;

/// <summary>
/// DI registration extensions for the Ananke Discord adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Discord adapter (<see cref="DiscordAdapter"/>) and its dependencies.
    /// The adapter connects via the Discord Gateway (WebSocket) and dispatches incoming
    /// messages to the registered <see cref="IPlatformMessageHandler"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure <see cref="DiscordAdapterOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddAnankeDiscord(options =&gt;
    /// {
    ///     options.BotToken = config["Discord:BotToken"]!;
    /// });
    /// services.AddSingleton&lt;IPlatformMessageHandler, MyAgentHandler&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddAnankeDiscord(
        this IServiceCollection services,
        Action<DiscordAdapterOptions> configure)
    {
        var options = new DiscordAdapterOptions { BotToken = string.Empty };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BotToken))
            throw new ArgumentException("DiscordAdapterOptions.BotToken is required.", nameof(configure));

        services.AddSingleton(options);

        services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = options.GatewayIntents
        }));

        services.AddSingleton<DiscordAdapter>(sp =>
        {
            var client = sp.GetRequiredService<DiscordSocketClient>();
            var handler = sp.GetRequiredService<IPlatformMessageHandler>();
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger<DiscordAdapter>();
            return new DiscordAdapter(options, handler, client, logger);
        });

        services.AddSingleton<IMessagePlatformAdapter>(sp => sp.GetRequiredService<DiscordAdapter>());

        services.AddSingleton<IHostedService>(sp => new DiscordHostedService(
            sp.GetRequiredService<DiscordAdapter>()));

        return services;
    }
}

/// <summary>
/// Hosted service that starts and stops the <see cref="DiscordAdapter"/> with the application.
/// </summary>
internal sealed class DiscordHostedService(DiscordAdapter adapter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        adapter.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        adapter.StopAsync(cancellationToken);
}
