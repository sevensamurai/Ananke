namespace OrganicKernelDemo;

record BookstoreState
{
    public string Request { get; init; } = "";
    public string? Response { get; init; }
}
