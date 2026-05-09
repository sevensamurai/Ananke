using Ananke.Orchestration.Tools;

namespace OrganicKernelDemo;

/// <summary>
/// Sets up the tool registry for the bookstore demo.
/// Catalog tools (search, details, inventory, recommendations) and
/// order tools (payment, orders, shipping, discounts, returns, customers).
/// </summary>
static class BookstoreTools
{
    public static ToolKit CreateCatalogTools() => new ToolKit("catalog-tools")
        .AddTool("search_catalog", "Search books by title, author, or genre",
            (query) => ToolResult.Ok($"Found 12 results for '{query}': 'The Great Gatsby', 'Moby Dick', ..."),
            "query", "Search query text")
        .AddTool("get_book_details", "Get full details for a book by ISBN",
            (isbn) => ToolResult.Ok($"'{isbn}': 'The Great Gatsby' by F. Scott Fitzgerald, $12.99, ★★★★½, 218 pages"),
            "isbn", "ISBN identifier")
        .AddTool("check_inventory", "Check stock levels for a book",
            (isbn) => ToolResult.Ok($"'{isbn}': 47 in stock (warehouse A), 3 in store #12"),
            "isbn", "ISBN identifier")
        .AddTool("get_recommendations", "Get personalized book recommendations",
            (genre) => ToolResult.Ok($"Top {genre} picks: '1984', 'Brave New World', 'Fahrenheit 451'"),
            "genre", "Genre to get recommendations for");

    public static ToolKit CreateOrderTools() => new ToolKit("orders-tools")
        .AddTool("process_payment", "Process a customer payment",
            (amount) => ToolResult.Ok($"Payment of ${amount} processed — txn #TXN-{Random.Shared.Next(10000, 99999)}"),
            "amount", "Payment amount")
        .AddTool("create_order", "Create a new customer order",
            (items) => ToolResult.Ok($"Order #ORD-{Random.Shared.Next(1000, 9999)} created with items: {items}"),
            "items", "Comma-separated ISBNs")
        .AddTool("track_shipment", "Track shipment status for an order",
            (orderId) => ToolResult.Ok($"Order {orderId}: shipped via FedEx, ETA 2 days, tracking #1Z999..."),
            "order_id", "Order identifier")
        .AddTool("apply_discount", "Apply a discount code to an order",
            (code) => ToolResult.Ok($"Discount '{code}' applied: 15% off, new total $42.49"),
            "code", "Discount code")
        .AddTool("manage_returns", "Process a product return",
            (orderId) => ToolResult.Ok($"Return initiated for {orderId}: RMA #RMA-{Random.Shared.Next(100, 999)}, prepaid label sent"),
            "order_id", "Order identifier")
        .AddTool("customer_lookup", "Look up customer profile",
            (email) => ToolResult.Ok($"Customer '{email}': Gold tier, 47 orders, member since 2019"),
            "email", "Customer email address");

    public static ToolKit CreateFullRegistry(ToolKit catalog, ToolKit orders) =>
        new ToolKit("registry").Merge(catalog).Merge(orders);

    public static Ananke.Design.WorkflowManifest BuildMinimalManifest(string name)
    {
        string[] lines =
        [
            $"name: {name}",
            "models:",
            "  default:",
            "    provider: openai",
            "    model: gpt-4o-mini",
            "connections:",
            "  - handle-request -> respond",
            "jobs:",
            "  handle-request:",
            "    type: agent",
            "    model: default",
            "  respond:",
            "    type: code"
        ];
        return Ananke.Design.WorkflowManifest.Parse(lines);
    }
}
