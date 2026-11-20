; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ANANKE001 | Ananke.Orchestration | Warning | Undefined job name referenced in workflow builder method
ANNKE002 | Ananke.Models | Warning | String literal equals a Deprecated model identifier from model-lifecycle.json
ANNKE003 | Ananke.Models | Error | String literal equals a Retired model identifier from model-lifecycle.json
