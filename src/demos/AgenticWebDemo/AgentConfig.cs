using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using OpenAI.Chat;
using System.ClientModel;

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

    // Reads the API key and model name from configuration and creates the streaming agent model.
    internal static IStreamingAgentModel CreateModel(IConfiguration config)
    {
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey not configured in secrets.json");
        var modelName = config["OpenAI:Model"] ?? "gpt-4.1-mini";
        return new OpenAIChatAgentModel(new ChatClient(modelName, new ApiKeyCredential(apiKey)));
    }
}
