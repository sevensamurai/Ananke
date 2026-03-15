using Ananke.AspNetCore.Configuration;
using Ananke.Orchestration.OpenAI;

// Centralises all AI agent configuration: the model client and the system prompt.
internal static class AgentConfig
{
    // The system prompt tells the model who it is and what tools it has.
    internal const string SystemPrompt = """
        You are a helpful stock market assistant. You can look up real-time stock data
        (prices, fundamentals, news) and execute trades (buy/sell shares) on the user's behalf.
        The user starts with $100,000 cash and an empty portfolio.
        Available stocks: AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, JPM.
        When the user asks you to buy or sell, use the appropriate tool directly.
        Always confirm the result of a trade and show the updated position.
        Format your responses using Markdown for clarity (tables, bold, bullet points, etc.).
        """;

    // Registers supported providers and reads the configuration.
    internal static ProviderProfile Configure(IConfiguration config)
    {
        AgentModelFactory.RegisterProvider("OpenAI",
            defaultModel: "gpt-4.1-mini",
            agentFactory: (key, model) => OpenAIChatAgentModel.Create(key, model));

        return AgentModelFactory.FromConfiguration(config);
    }
}
