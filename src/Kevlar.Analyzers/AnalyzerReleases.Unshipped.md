; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
KEV001 | Reliability | Warning | Execution delegate ignores its CancellationToken
KEV002 | Reliability | Warning | Statically known multi-attempt hedging requires asynchronous execution
KEV003 | Configuration | Warning | Fallback makes a reactive strategy unreachable
