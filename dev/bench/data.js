window.BENCHMARK_DATA = {
  "lastUpdate": 1787254577856,
  "repoUrl": "https://github.com/thomhurst/Kevlar",
  "entries": {
    "Kevlar Benchmarks": [
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "distinct": true,
          "id": "c928edae99b8435ebc87c54082a7a315cc8e7d5a",
          "message": "ci: auto-publish benchmark results to docs\n\nbenchmarks.yml runs the suite on pushes to main, weekly, and on\ndispatch; history lands on an orphan benchmark-data branch via\ngithub-action-benchmark (150% regression alert), and\nbenchmark_docs.py regenerates docs/docs/benchmarks.md from the\nmedian ratio over the last 10 runs so one noisy CI run cannot swing\nthe published numbers. The docs deploy is dispatched explicitly\nbecause GITHUB_TOKEN pushes do not trigger workflows. Filtered\nmanual runs skip publishing so partial results never strip page\nsections.\n\nClaude-Session: https://claude.ai/code/session_01E9VCHZwdgEt4zdTzM5CNwy",
          "timestamp": "2026-08-20T20:23:31+01:00",
          "tree_id": "1b7bd0107b2c34ea997d547c192105d17acf1e73",
          "url": "https://github.com/thomhurst/Kevlar/commit/c928edae99b8435ebc87c54082a7a315cc8e7d5a"
        },
        "date": 1787254576906,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 117.06342768669128,
            "unit": "ns",
            "range": "± 0.99545549858149"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 162.49196362495422,
            "unit": "ns",
            "range": "± 3.0310134163928284"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 3083.4595260620117,
            "unit": "ns",
            "range": "± 63.5510027730717"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 3158.653085708618,
            "unit": "ns",
            "range": "± 105.44715389901431"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 115.9642682671547,
            "unit": "ns",
            "range": "± 1.9816427033231403"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 130.12674164772034,
            "unit": "ns",
            "range": "± 1.8442612650784564"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 93.80264097452164,
            "unit": "ns",
            "range": "± 1.8874416068252415"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 75.0907574892044,
            "unit": "ns",
            "range": "± 0.8428013756136437"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1386.2774333953857,
            "unit": "ns",
            "range": "± 23.38834660943413"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1393.1145658493042,
            "unit": "ns",
            "range": "± 36.00321512297395"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 300.2508373260498,
            "unit": "ns",
            "range": "± 6.128569779786006"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 271.7838673591614,
            "unit": "ns",
            "range": "± 5.857128379042228"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 1.1152225900441408,
            "unit": "ns",
            "range": "± 0.04322484988843765"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 35.4762749671936,
            "unit": "ns",
            "range": "± 0.5076571679007505"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 0.9568706192076206,
            "unit": "ns",
            "range": "± 0.050066100684415575"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 45.66857501864433,
            "unit": "ns",
            "range": "± 1.1735716267343335"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 1.2480846904218197,
            "unit": "ns",
            "range": "± 0.02800858307988049"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 24.053346127271652,
            "unit": "ns",
            "range": "± 0.5135273184609411"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 436.3092112541199,
            "unit": "ns",
            "range": "± 8.424017929883872"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 542.686692237854,
            "unit": "ns",
            "range": "± 4.13456216010249"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 252.2953817844391,
            "unit": "ns",
            "range": "± 4.061032478259581"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 392.2318456172943,
            "unit": "ns",
            "range": "± 8.06141747712238"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 135.74993014335632,
            "unit": "ns",
            "range": "± 1.9251076097242485"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 100.66366302967072,
            "unit": "ns",
            "range": "± 1.759584820162895"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 103.83612316846848,
            "unit": "ns",
            "range": "± 1.8260305653898077"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 154.05614280700684,
            "unit": "ns",
            "range": "± 1.4281124981012059"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 2249.986991882324,
            "unit": "ns",
            "range": "± 27.625412204345338"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 2561.3002738952637,
            "unit": "ns",
            "range": "± 31.19289542242184"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 140.78344023227692,
            "unit": "ns",
            "range": "± 2.7743647902593755"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 126.53377401828766,
            "unit": "ns",
            "range": "± 1.881076687268139"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 99.38469672203064,
            "unit": "ns",
            "range": "± 2.6396681892678497"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 125.10037243366241,
            "unit": "ns",
            "range": "± 1.476050208895139"
          }
        ]
      }
    ]
  }
}