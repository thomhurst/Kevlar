using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Kevlar.Benchmarks;

/// <summary>Compares the former Exception.Data proxy lookup with the current type check.</summary>
[MemoryDiagnoser(displayGenColumns: false)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class OutcomeExceptionBenchmarks
{
    private const string ExceptionProxyDataKey =
        "Kevlar.Internal.ExceptionProxy.6b21d876-5f0c-45d4-a873-cd6d83e9158b";

    private readonly Exception _legacyPlain = new InvalidOperationException("plain");
    private readonly Exception _legacyProxy = new Exception("proxy");
    private readonly Outcome<int> _currentPlain;
    private readonly Outcome<int> _currentProxy;

    public OutcomeExceptionBenchmarks()
    {
        var original = new InvalidOperationException("original");
        // Keep one-time dictionary materialization outside the recurring lookup measurement.
        _ = _legacyPlain.Data.Count;
        _legacyProxy.Data[ExceptionProxyDataKey] = original;
        _currentPlain = Outcome<int>.FromException(_legacyPlain);
        _currentProxy = Outcome<int>.FromException(new BenchmarkProxyException(original));
    }

    [BenchmarkCategory("PlainException"), Benchmark(Baseline = true)]
    public Exception Legacy_Plain_DataLookup() =>
        _legacyPlain.Data[ExceptionProxyDataKey] as Exception ?? _legacyPlain;

    [BenchmarkCategory("PlainException"), Benchmark]
    public Exception Current_Plain_TypeCheck() => _currentPlain.Exception!;

    [BenchmarkCategory("ProxyException"), Benchmark(Baseline = true)]
    public Exception Legacy_Proxy_DataLookup() =>
        _legacyProxy.Data[ExceptionProxyDataKey] as Exception ?? _legacyProxy;

    [BenchmarkCategory("ProxyException"), Benchmark]
    public Exception Current_Proxy_TypeCheck() => _currentProxy.Exception!;

    private sealed class BenchmarkProxyException(Exception originalException)
        : KevlarProxyException(originalException);
}
