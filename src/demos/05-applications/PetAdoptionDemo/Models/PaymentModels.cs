internal static class PaymentConstants
{
    internal const string QueueName = "payment-queue";
}

internal sealed record PaymentRequest
{
    public required string SessionId { get; init; }
    public required string CardNumber { get; init; }
}

internal sealed record PaymentHandoff
{
    public required string SessionId { get; init; }
    public required string Last4 { get; init; }
}

internal sealed record PaymentState
{
    public required string SessionId { get; init; }
    public required string Last4 { get; init; }
    public bool CardValid { get; init; }
    public string? TransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}

internal sealed record PaymentResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? TransactionId { get; init; }
    public string? InvoiceId { get; init; }
}
