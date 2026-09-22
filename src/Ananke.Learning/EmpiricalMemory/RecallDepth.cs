namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// How much of a recalled entry to render. The lever that makes a token budget <em>spendable</em>
/// rather than merely enforced.
/// </summary>
/// <remarks>
/// Recall has always returned whole entries, so coverage could only be traded for tokens at a fixed
/// per-entry price: five entries or four, never "five entries, shallowly". These are three prices
/// for the same entry, which is what lets an allocation buy either breadth or detail.
/// </remarks>
public enum RecallDepth
{
    /// <summary>One line: what it is, how well it matched, and its summary. Cheapest.</summary>
    Abstract = 0,

    /// <summary>The summary plus the fields that decide whether the entry applies here.</summary>
    Entry = 1,

    /// <summary>Everything the entry holds, including procedure, mechanism and provenance.</summary>
    Full = 2
}
