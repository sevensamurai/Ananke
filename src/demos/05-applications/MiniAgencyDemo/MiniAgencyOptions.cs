using Microsoft.Extensions.Configuration;

namespace MiniAgencyDemo;

internal sealed record MiniAgencyOptions
{
    public required string SlackBotToken { get; init; }

    public string? SlackAppToken { get; init; }

    public string? SlackSigningSecret { get; init; }

    public bool UseSocketMode { get; init; } = true;

    public required string LocalModelName { get; init; }

    public required string LocalApiKey { get; init; }

    public required Uri LocalEndpoint { get; init; }

    public string WorkflowName { get; init; } = "build-and-review";

    public long BudgetCap { get; init; } = 100_000;

    public TimeSpan BudgetWindow { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan HumanReviewTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool EnableBudgetMetrics { get; init; }

    public static MiniAgencyOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var endpointText = ResolveRequired(configuration,
            "MiniAgency:LocalEndpoint",
            "ANANKE_LOCAL_ENDPOINT",
            "OpenAI:Endpoint");

        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException($"Mini-agency local endpoint is not a valid absolute URI: '{endpointText}'.");

        return new MiniAgencyOptions
        {
            SlackBotToken = ResolveRequired(configuration, "Slack:BotToken", "SLACK_BOT_TOKEN"),
            SlackAppToken = ResolveOptional(configuration, "Slack:AppToken", "SLACK_APP_TOKEN"),
            SlackSigningSecret = ResolveOptional(configuration, "Slack:SigningSecret", "SLACK_SIGNING_SECRET"),
            UseSocketMode = configuration.GetValue("Slack:UseSocketMode", true),
            LocalModelName = ResolveOptional(configuration,
                "MiniAgency:LocalModel",
                "ANANKE_LOCAL_MODEL") ?? "llama3.2:3b",
            LocalApiKey = ResolveOptional(configuration,
                "MiniAgency:LocalApiKey",
                "ANANKE_LOCAL_API_KEY",
                "OpenAI:ApiKey") ?? "ollama",
            LocalEndpoint = endpoint,
            WorkflowName = ResolveOptional(configuration, "MiniAgency:WorkflowName") ?? "build-and-review",
            BudgetCap = configuration.GetValue<long?>("MiniAgency:BudgetCap") ?? 100_000,
            BudgetWindow = TimeSpan.FromMinutes(configuration.GetValue<double?>("MiniAgency:BudgetWindowMinutes") ?? 60),
            HumanReviewTimeout = TimeSpan.FromSeconds(configuration.GetValue<double?>("MiniAgency:HumanReviewTimeoutSeconds") ?? 15),
            EnableBudgetMetrics = configuration.GetValue("MiniAgency:EnableBudgetMetrics", false)
        };
    }

    private static string ResolveRequired(IConfiguration configuration, params string[] keys) =>
        ResolveOptional(configuration, keys)
        ?? throw new InvalidOperationException($"Missing required configuration value. Checked: {string.Join(", ", keys)}");

    private static string? ResolveOptional(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
