using System.Globalization;
using Ananke.Orchestration.Tools;

namespace BasicAgentDemo.Workflow;

/// <summary>
/// Mock stock-market tools used by Level 4 (streaming chat workflow).
/// </summary>
internal static class StockTools
{
    private static readonly Dictionary<string, StockData> MockMarketData = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAPL"] = new(228.50m, 2.3m, 1.02, "Technology", 35_200_000),
        ["MSFT"] = new(449.20m, 5.1m, 0.72, "Technology", 22_100_000),
        ["GOOGL"] = new(176.80m, -1.2m, 0.51, "Technology", 28_400_000),
        ["AMZN"] = new(205.70m, 3.8m, 0.00, "Consumer Cyclical", 41_500_000),
        ["TSLA"] = new(352.80m, -4.5m, 0.00, "Automotive", 95_200_000),
        ["NVDA"] = new(135.40m, 8.2m, 0.01, "Technology", 310_000_000),
        ["META"] = new(595.30m, 1.9m, 0.52, "Technology", 15_800_000),
        ["JPM"] = new(253.10m, 0.8m, 2.05, "Financial", 8_900_000),
    };

    private static readonly Dictionary<string, int> Portfolio = new(StringComparer.OrdinalIgnoreCase);
    private static decimal _cashBalance = 100_000m;

    internal static ToolKit Create() => new ToolKit("stock")
        .AddTool(
            "get_stock_price",
            "Gets the current stock price, daily change, and volume for a given ticker symbol.",
            GetStockPrice,
            "symbol", "The stock ticker symbol (e.g. AAPL, MSFT)")
        .AddTool(
            "get_stock_fundamentals",
            "Gets fundamental data including P/E ratio, dividend yield, and sector for a stock.",
            GetStockFundamentals,
            "symbol", "The stock ticker symbol")
        .AddTool(
            "get_market_news",
            "Gets the latest market news headlines relevant to a stock or sector.",
            GetMarketNews,
            "query", "The stock ticker symbol or sector name")
        .AddTool("buy_shares",
            "Buys a specified number of shares of a stock at the current market price.",
            b => b
                .Param("symbol", "The stock ticker symbol (e.g. AAPL, MSFT)")
                .Param("quantity", "The number of shares to buy")
                .OnExecute(args => BuyShares(args.Get("symbol"), args.Get("quantity"))))
        .AddTool("sell_shares",
            "Sells a specified number of shares of a stock at the current market price.",
            b => b
                .Param("symbol", "The stock ticker symbol (e.g. AAPL, MSFT)")
                .Param("quantity", "The number of shares to sell")
                .OnExecute(args => SellShares(args.Get("symbol"), args.Get("quantity"))));

    private static ToolResult BuyShares(string symbol, string quantityStr)
    {
        if (!int.TryParse(quantityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            return $"Invalid quantity: {quantityStr}. Provide a positive whole number.";

        var upper = symbol.ToUpperInvariant();
        if (!MockMarketData.TryGetValue(upper, out var data))
            return $"Symbol {symbol} not found. Try AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, or JPM.";

        var cost = data.Price * quantity;
        if (cost > _cashBalance)
            return $"Insufficient funds. Cost: ${cost:N2}, Available cash: ${_cashBalance:N2}.";

        _cashBalance -= cost;
        Portfolio.TryGetValue(upper, out var held);
        Portfolio[upper] = held + quantity;

        return $"Bought {quantity} shares of {upper} at ${data.Price:F2} each. " +
               $"Total cost: ${cost:N2}. Remaining cash: ${_cashBalance:N2}. " +
               $"Now holding {Portfolio[upper]} shares of {upper}.";
    }

    private static ToolResult SellShares(string symbol, string quantityStr)
    {
        if (!int.TryParse(quantityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            return $"Invalid quantity: {quantityStr}. Provide a positive whole number.";

        var upper = symbol.ToUpperInvariant();
        if (!MockMarketData.TryGetValue(upper, out var data))
            return $"Symbol {symbol} not found. Try AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, or JPM.";

        Portfolio.TryGetValue(upper, out var held);
        if (quantity > held)
            return $"Cannot sell {quantity} shares of {upper}. Currently holding {held} shares.";

        var proceeds = data.Price * quantity;
        _cashBalance += proceeds;
        Portfolio[upper] = held - quantity;
        if (Portfolio[upper] == 0)
            Portfolio.Remove(upper);

        return $"Sold {quantity} shares of {upper} at ${data.Price:F2} each. " +
               $"Total proceeds: ${proceeds:N2}. Cash balance: ${_cashBalance:N2}. " +
               $"Now holding {Portfolio.GetValueOrDefault(upper)} shares of {upper}.";
    }

    private static ToolResult GetStockPrice(string symbol)
    {
        if (MockMarketData.TryGetValue(symbol.ToUpperInvariant(), out var data))
        {
            var direction = data.DailyChange >= 0 ? "+" : "";
            return $"Symbol: {symbol.ToUpperInvariant()}, " +
                   $"Price: ${data.Price:F2}, " +
                   $"Change: {direction}{data.DailyChange:F2}%, " +
                   $"Volume: {data.Volume:N0}";
        }

        return $"Symbol {symbol} not found. Try AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, or JPM.";
    }

    private static ToolResult GetStockFundamentals(string symbol)
    {
        if (MockMarketData.TryGetValue(symbol.ToUpperInvariant(), out var data))
        {
            var pe = data.Price / (data.Price * 0.03m);
            return $"Symbol: {symbol.ToUpperInvariant()}, " +
                   $"P/E Ratio: {pe:F1}, " +
                   $"Dividend Yield: {data.DividendYield:F2}%, " +
                   $"Sector: {data.Sector}";
        }

        return $"No fundamental data available for {symbol}.";
    }

    private static ToolResult GetMarketNews(string query)
    {
        return query.ToUpperInvariant() switch
        {
            "AAPL" => "1) Apple announces new AI features for iPhone 17. 2) Services revenue hits record high. 3) Analysts raise price targets.",
            "MSFT" => "1) Microsoft Azure growth accelerates to 35%. 2) Copilot adoption doubles in enterprise. 3) Gaming division reports strong quarter.",
            "TSLA" => "1) Tesla deliveries miss expectations. 2) New factory in Mexico faces delays. 3) FSD v13 receives positive reviews.",
            "NVDA" => "1) NVIDIA Blackwell chips see unprecedented demand. 2) Data center revenue surges 150%. 3) New partnerships with cloud providers announced.",
            _ => "1) Markets trade mixed amid economic uncertainty. 2) Fed signals cautious approach to rate decisions. 3) Tech sector leads gains."
        };
    }

    private record StockData(
        decimal Price,
        decimal DailyChange,
        double DividendYield,
        string Sector,
        long Volume);
}
