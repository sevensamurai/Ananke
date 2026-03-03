// ═══════════════════════════════════════════════════════════════════
// Workflow state + handoff DTOs
// ═══════════════════════════════════════════════════════════════════

/// <summary>Immutable state that flows through every workflow job.</summary>
record TicketState
{
    public string TicketId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string Description { get; init; } = "";
    public int Severity { get; init; }
    public string? Category { get; init; }
    public string? Resolution { get; init; }
    public string? ResolvedBy { get; init; }
    public int PriorInteractions { get; init; }
    public bool Notified { get; init; }
    public string? FsmState { get; init; }
}

/// <summary>Payload sent to the specialist service via the handoff channel.</summary>
record TicketHandoff
{
    public string TicketId { get; init; } = "";
    public string Summary { get; init; } = "";
    public int Severity { get; init; }
}

/// <summary>Response returned by the specialist service.</summary>
record SpecialistResult
{
    public string Resolution { get; init; } = "";
    public string HandledBy { get; init; } = "";
}
