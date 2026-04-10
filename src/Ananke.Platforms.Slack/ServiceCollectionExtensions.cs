using Ananke.Platforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlackNet.Extensions.DependencyInjection;

namespace Ananke.Platforms.Slack;

/// <summary>
/// DI registration extensions for the Ananke Slack adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Slack adapter (<see cref="SlackAdapter"/>) and its dependencies.
    /// The adapter connects via Socket Mode by default and dispatches incoming
    /// messages to the registered <see cref="IPlatformMessageHandler"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure <see cref="SlackAdapterOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddAnankeSlack(options =&gt;
    /// {
    ///     options.BotToken = config["Slack:BotToken"]!;
    ///     options.AppToken = config["Slack:AppToken"]!;
    ///     options.UseSocketMode = true;
    /// });
    /// services.AddSingleton&lt;IPlatformMessageHandler, MyAgentHandler&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddAnankeSlack(
        this IServiceCollection services,
        Action<SlackAdapterOptions> configure)
    {
        var options = new SlackAdapterOptions { BotToken = string.Empty };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BotToken))
            throw new ArgumentException("SlackAdapterOptions.BotToken is required.", nameof(configure));

        services.AddSingleton(options);

        services.AddSlackNet(c =>
        {
            c.UseApiToken(options.BotToken);

            if (!string.IsNullOrWhiteSpace(options.AppToken))
                c.UseAppLevelToken(options.AppToken);
        });

        services.AddSingleton<SlackAdapter>(sp =>
        {
            var slackServices = sp.SlackServices();
            var handler = sp.GetRequiredService<IPlatformMessageHandler>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger<SlackAdapter>();
            return new SlackAdapter(options, handler, slackServices, logger);
        });

        services.AddSingleton<IMessagePlatformAdapter>(sp => sp.GetRequiredService<SlackAdapter>());

        services.AddSingleton<IHostedService>(sp => new SlackHostedService(
            sp.GetRequiredService<SlackAdapter>()));

        return services;
    }
}

/// <summary>
/// Hosted service that starts and stops the <see cref="SlackAdapter"/> with the application.
/// </summary>
internal sealed class SlackHostedService(SlackAdapter adapter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        adapter.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        adapter.StopAsync(cancellationToken);
}
