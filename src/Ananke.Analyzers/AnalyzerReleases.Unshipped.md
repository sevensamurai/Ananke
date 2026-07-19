; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ANANKE001 | Ananke.Orchestration | Warning | Undefined job name referenced in workflow builder method
ANANKE_ASYNC_001 | Ananke.Async | Warning | Internal/private async method missing ConfigureAwait(false) on an await expression
ANANKE_TIME_001 | Ananke.Time | Warning | Direct read of DateTime.Now/UtcNow or DateTimeOffset.Now/UtcNow — inject a TimeProvider instead
ANNKE002 | Ananke.Models | Warning | String literal equals a Deprecated model identifier from model-lifecycle.json
ANNKE003 | Ananke.Models | Error | String literal equals a Retired model identifier from model-lifecycle.json
