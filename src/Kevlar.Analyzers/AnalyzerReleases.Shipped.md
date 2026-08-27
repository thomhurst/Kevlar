; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
KEV001 | Reliability | Warning | Execution delegate ignores its CancellationToken
KEV002 | Reliability | Warning | Statically known multi-attempt hedging requires asynchronous execution
KEV003 | Configuration | Warning | Fallback makes a reactive strategy unreachable
KEV004 | Reliability | Warning | Stateful shield or partition provider is constructed per execution
KEV005 | Configuration | Warning | Void fallback is used with a result-returning execution
KEV006 | Reliability | Warning | Hedging on an untyped Shield requires an idempotent action
KEV007 | Configuration | Warning | Handling clause never reaches a reactive strategy
KEV008 | Configuration | Warning | Fluent chaining result is discarded as a statement
KEV009 | Configuration | Disabled | Strategy inherits a handling clause declared earlier in the chain
KEV010 | Configuration | Disabled | Default-result clause handles a value type's default
KEV011 | Configuration | Disabled | Reactive strategy uses implicit default handling
KEV012 | Reliability | Warning | Asynchronous strategy configuration requires ExecuteAsync
KEV014 | Reliability | Warning | Pooled event context is captured by deferred work
