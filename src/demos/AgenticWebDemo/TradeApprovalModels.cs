/// <summary>State threaded through the trade-approval workflow.</summary>
record TradeState
{
    /// <summary>The original user request (e.g. "Buy 10 shares of AAPL").</summary>
    public required string UserRequest { get; init; }

    /// <summary>Agent-generated analysis populated by the "analyze" job.</summary>
    public string? Analysis { get; init; }

    /// <summary>Human approval flag, set via <c>ResumeAsync</c> state transform.</summary>
    public bool Approved { get; init; }

    /// <summary>Final execution result populated by the "execute" job.</summary>
    public string? Result { get; init; }
}

/// <summary>Request to start a trade analysis.</summary>
record TradeAnalysisRequest(string Message);

/// <summary>Request to approve or reject a pending trade.</summary>
record TradeApprovalRequest(string ExecutionId, bool Approved);
