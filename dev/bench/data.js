window.BENCHMARK_DATA = {
  "lastUpdate": 1787330240734,
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
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "22100f44e2dafd604a4a0f510ce960bf497f7afd",
          "message": "Merge pull request #4 from thomhurst/perf/hedging-sync-fast-path\n\nEliminate hedging happy-path allocations",
          "timestamp": "2026-08-20T21:08:57+01:00",
          "tree_id": "cdaff43b43181fc513412dcd75f2a62c2c010bf0",
          "url": "https://github.com/thomhurst/Kevlar/commit/22100f44e2dafd604a4a0f510ce960bf497f7afd"
        },
        "date": 1787257205804,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 194.5891125202179,
            "unit": "ns",
            "range": "± 0.3405041359955794"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 272.53834772109985,
            "unit": "ns",
            "range": "± 0.877386718278511"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5267.294418334961,
            "unit": "ns",
            "range": "± 7.631661303685074"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5287.822277069092,
            "unit": "ns",
            "range": "± 5.513490872630535"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 200.7699737548828,
            "unit": "ns",
            "range": "± 0.2951466855654385"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 210.68152165412903,
            "unit": "ns",
            "range": "± 0.4373143946124162"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 184.7408492565155,
            "unit": "ns",
            "range": "± 0.4419159212729732"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 140.42570686340332,
            "unit": "ns",
            "range": "± 0.14075625905848918"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2450.396572113037,
            "unit": "ns",
            "range": "± 6.052114151729689"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2399.3247661590576,
            "unit": "ns",
            "range": "± 3.537351895005927"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 215.63521075248718,
            "unit": "ns",
            "range": "± 0.12158717717514393"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 476.5562734603882,
            "unit": "ns",
            "range": "± 0.4919649864821487"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.6192795112729073,
            "unit": "ns",
            "range": "± 0.013895649738990185"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 59.91749358177185,
            "unit": "ns",
            "range": "± 0.04486222205950008"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.684231076389551,
            "unit": "ns",
            "range": "± 0.001417901593743548"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 66.5176432132721,
            "unit": "ns",
            "range": "± 0.034312146003419815"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.5443640276789665,
            "unit": "ns",
            "range": "± 0.004509747811271117"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.795727759599686,
            "unit": "ns",
            "range": "± 0.029663611293806888"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 773.8171706199646,
            "unit": "ns",
            "range": "± 0.2961554835569568"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1041.9766092300415,
            "unit": "ns",
            "range": "± 1.5513466792824135"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 466.5920977592468,
            "unit": "ns",
            "range": "± 0.9411528881207948"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 712.2802724838257,
            "unit": "ns",
            "range": "± 1.9123313684675292"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 221.88284921646118,
            "unit": "ns",
            "range": "± 0.18848681041876972"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 158.43827152252197,
            "unit": "ns",
            "range": "± 0.21118451104151506"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 175.31442368030548,
            "unit": "ns",
            "range": "± 0.10639306984881712"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 273.3542184829712,
            "unit": "ns",
            "range": "± 0.3750256229675968"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3757.6221237182617,
            "unit": "ns",
            "range": "± 5.881908801436706"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4505.016166687012,
            "unit": "ns",
            "range": "± 11.603361667844673"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 263.2080669403076,
            "unit": "ns",
            "range": "± 0.4178745647715824"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 210.02257215976715,
            "unit": "ns",
            "range": "± 0.15300143092457272"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 174.63511216640472,
            "unit": "ns",
            "range": "± 0.10290953660841622"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 208.15491890907288,
            "unit": "ns",
            "range": "± 0.15243865689835231"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e10e3221c9133da2437ef81474bc42e4ead8bda8",
          "message": "Merge pull request #5 from thomhurst/perf/rate-limit-fast-path\n\nperf: speed up uncontended rate limiting",
          "timestamp": "2026-08-20T22:30:24+01:00",
          "tree_id": "e49ca25ad1ceef0d5d7c4e00455848c9a095094d",
          "url": "https://github.com/thomhurst/Kevlar/commit/e10e3221c9133da2437ef81474bc42e4ead8bda8"
        },
        "date": 1787262043109,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 175.3484764099121,
            "unit": "ns",
            "range": "± 0.5210613777479025"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 272.76741123199463,
            "unit": "ns",
            "range": "± 0.9496623026184718"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5523.054389953613,
            "unit": "ns",
            "range": "± 10.617391294993416"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5563.988090515137,
            "unit": "ns",
            "range": "± 7.131785626275244"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 176.35451471805573,
            "unit": "ns",
            "range": "± 0.09105976738078682"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 201.9664340019226,
            "unit": "ns",
            "range": "± 0.23605189053064543"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 158.24378538131714,
            "unit": "ns",
            "range": "± 0.12740433632722173"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 135.545663356781,
            "unit": "ns",
            "range": "± 0.6414553033666154"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2464.4937057495117,
            "unit": "ns",
            "range": "± 5.720332433152634"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2464.766845703125,
            "unit": "ns",
            "range": "± 4.81900603199537"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 193.96067357063293,
            "unit": "ns",
            "range": "± 1.7495232988484932"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 479.74834537506104,
            "unit": "ns",
            "range": "± 0.4468915691526307"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.711831644177437,
            "unit": "ns",
            "range": "± 0.004426201034648665"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.86036318540573,
            "unit": "ns",
            "range": "± 0.042609861805043454"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.481030337512493,
            "unit": "ns",
            "range": "± 0.0023475999841272234"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 63.7049565911293,
            "unit": "ns",
            "range": "± 0.11081350394239992"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.916892096400261,
            "unit": "ns",
            "range": "± 0.007031183386621339"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.28337949514389,
            "unit": "ns",
            "range": "± 0.06436341356457335"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 567.9508399963379,
            "unit": "ns",
            "range": "± 2.492575248394986"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1022.429220199585,
            "unit": "ns",
            "range": "± 2.477923326121882"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 432.75324535369873,
            "unit": "ns",
            "range": "± 1.1899592889304254"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 705.2974662780762,
            "unit": "ns",
            "range": "± 1.8235736671948002"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 157.7353527545929,
            "unit": "ns",
            "range": "± 0.2600304204846268"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 159.31968998908997,
            "unit": "ns",
            "range": "± 0.09196527485975699"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 153.62502551078796,
            "unit": "ns",
            "range": "± 0.11812402445000401"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 256.7043037414551,
            "unit": "ns",
            "range": "± 0.22336967739537378"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3840.5985107421875,
            "unit": "ns",
            "range": "± 7.6259016202742"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4564.186019897461,
            "unit": "ns",
            "range": "± 5.867609595446457"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 258.49675369262695,
            "unit": "ns",
            "range": "± 0.9971600928120498"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 219.79247748851776,
            "unit": "ns",
            "range": "± 0.13282932703709116"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 160.52886140346527,
            "unit": "ns",
            "range": "± 0.07979200900582749"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 202.2836718559265,
            "unit": "ns",
            "range": "± 0.1442769248257125"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "a0cb9757dba22f0f2ea857647e8ba3acf637ccf0",
          "message": "+semver:minor - Merge pull request #6 from thomhurst/perf/timeout-cts-pooling\n\nperf: eliminate timeout happy-path allocations",
          "timestamp": "2026-08-20T22:57:30+01:00",
          "tree_id": "221cd7f0c8ecfbe382d519df99422f690e0ec248",
          "url": "https://github.com/thomhurst/Kevlar/commit/a0cb9757dba22f0f2ea857647e8ba3acf637ccf0"
        },
        "date": 1787263715398,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 193.74925875663757,
            "unit": "ns",
            "range": "± 0.46189885070230907"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 270.23616218566895,
            "unit": "ns",
            "range": "± 0.7777816157307735"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5193.730995178223,
            "unit": "ns",
            "range": "± 11.28844194561129"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5287.090274810791,
            "unit": "ns",
            "range": "± 7.156823670183094"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 190.60129928588867,
            "unit": "ns",
            "range": "± 0.3042622869826142"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 209.26318049430847,
            "unit": "ns",
            "range": "± 0.29046000823924334"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 164.46887636184692,
            "unit": "ns",
            "range": "± 0.14455309332974134"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 144.66766965389252,
            "unit": "ns",
            "range": "± 0.3730772817458712"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2388.115453720093,
            "unit": "ns",
            "range": "± 3.1394662954916925"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2414.5093421936035,
            "unit": "ns",
            "range": "± 1.7574348349621152"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 193.78148126602173,
            "unit": "ns",
            "range": "± 0.3159354216587221"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 473.68568658828735,
            "unit": "ns",
            "range": "± 1.0380860996814854"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.6145161911845207,
            "unit": "ns",
            "range": "± 0.008077352349422839"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 59.244626581668854,
            "unit": "ns",
            "range": "± 0.05635084082238844"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.3334349617362022,
            "unit": "ns",
            "range": "± 0.002320405294147306"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 66.84676313400269,
            "unit": "ns",
            "range": "± 0.06336564909740368"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.705744318664074,
            "unit": "ns",
            "range": "± 0.004750794159366067"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.41891235113144,
            "unit": "ns",
            "range": "± 0.036628698163299014"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 520.4522552490234,
            "unit": "ns",
            "range": "± 0.44781319044022555"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1019.2769432067871,
            "unit": "ns",
            "range": "± 1.2431403422006573"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 384.9716229438782,
            "unit": "ns",
            "range": "± 0.41524854132005545"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 709.4096789360046,
            "unit": "ns",
            "range": "± 3.1400176318123245"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 169.79890275001526,
            "unit": "ns",
            "range": "± 0.6298085378718842"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 157.0365368127823,
            "unit": "ns",
            "range": "± 0.24712181349959086"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 179.05877447128296,
            "unit": "ns",
            "range": "± 0.9076476266872969"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 261.39799642562866,
            "unit": "ns",
            "range": "± 0.3317670379331667"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3759.2457885742188,
            "unit": "ns",
            "range": "± 6.445733024035888"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4430.300720214844,
            "unit": "ns",
            "range": "± 5.872248390240662"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 195.85106360912323,
            "unit": "ns",
            "range": "± 0.08801538521858"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 208.69257831573486,
            "unit": "ns",
            "range": "± 0.24675659877321152"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 166.92100167274475,
            "unit": "ns",
            "range": "± 0.10040179171064333"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 205.09269976615906,
            "unit": "ns",
            "range": "± 0.2850208673403999"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "dfe9580c6ac602cd2e0bc710ed0cca8897b77cf7",
          "message": "Merge pull request #7 from thomhurst/perf/fallback-sync-fast-path\n\nperf(fallback): add synchronous fast path",
          "timestamp": "2026-08-20T23:11:46+01:00",
          "tree_id": "5fe45092a9a374242f8503f10492ad8af116ea24",
          "url": "https://github.com/thomhurst/Kevlar/commit/dfe9580c6ac602cd2e0bc710ed0cca8897b77cf7"
        },
        "date": 1787264592640,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 143.79820680618286,
            "unit": "ns",
            "range": "± 0.3153791754308673"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 219.25444293022156,
            "unit": "ns",
            "range": "± 0.27219816808268493"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 4093.8331604003906,
            "unit": "ns",
            "range": "± 1.5283076563846796"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 4124.66658782959,
            "unit": "ns",
            "range": "± 9.920501401872155"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 139.628333568573,
            "unit": "ns",
            "range": "± 0.12254883690124199"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 160.4305295944214,
            "unit": "ns",
            "range": "± 0.18128154857068393"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 100.86512100696564,
            "unit": "ns",
            "range": "± 0.03249881839579824"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 106.83657884597778,
            "unit": "ns",
            "range": "± 0.08544083146359865"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1773.5913715362549,
            "unit": "ns",
            "range": "± 2.5643512436408544"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1916.767807006836,
            "unit": "ns",
            "range": "± 0.9783984623892361"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 150.0432105064392,
            "unit": "ns",
            "range": "± 0.21885287248648036"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 368.4981527328491,
            "unit": "ns",
            "range": "± 0.09630699534921973"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.388298261910677,
            "unit": "ns",
            "range": "± 0.0009281027287542278"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 48.045426696538925,
            "unit": "ns",
            "range": "± 0.06193564372530574"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.3711875900626183,
            "unit": "ns",
            "range": "± 0.00047914193664420207"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 47.65140897035599,
            "unit": "ns",
            "range": "± 0.014055562027388379"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.1142789758741856,
            "unit": "ns",
            "range": "± 0.0017240693536203313"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 24.17013882100582,
            "unit": "ns",
            "range": "± 0.02304989816818318"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 411.0842616558075,
            "unit": "ns",
            "range": "± 0.41834308625309724"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 761.4576811790466,
            "unit": "ns",
            "range": "± 0.5966621138988056"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 286.49379873275757,
            "unit": "ns",
            "range": "± 0.22269388836838358"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 565.070143699646,
            "unit": "ns",
            "range": "± 2.258255470762504"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 119.48387932777405,
            "unit": "ns",
            "range": "± 0.12310564656633968"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 120.03215003013611,
            "unit": "ns",
            "range": "± 0.12158241760765676"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 140.05921339988708,
            "unit": "ns",
            "range": "± 0.4149371386087743"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 201.48499989509583,
            "unit": "ns",
            "range": "± 0.21987232110233937"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 2954.7127208709717,
            "unit": "ns",
            "range": "± 3.4700353352205706"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3412.3622665405273,
            "unit": "ns",
            "range": "± 7.714149219632111"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 159.9245491027832,
            "unit": "ns",
            "range": "± 0.18267645419135356"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 159.12605798244476,
            "unit": "ns",
            "range": "± 0.11798622304760906"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 132.83644568920135,
            "unit": "ns",
            "range": "± 0.05238938979334316"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 159.02566146850586,
            "unit": "ns",
            "range": "± 0.610374144123943"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "8ccef29aaa8401e79ee783015906c3d5605f2642",
          "message": "Merge pull request #8 from thomhurst/feat/net10-target\n\nfeat: retarget net8.0 to net10.0, adopt System.Threading.Lock via Polyfill",
          "timestamp": "2026-08-20T23:23:20+01:00",
          "tree_id": "948213dabf5b870f563c82f31fb783afdbe496db",
          "url": "https://github.com/thomhurst/Kevlar/commit/8ccef29aaa8401e79ee783015906c3d5605f2642"
        },
        "date": 1787265239350,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 175.20496356487274,
            "unit": "ns",
            "range": "± 0.18322190756879225"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 268.4506850242615,
            "unit": "ns",
            "range": "± 1.1470377661163498"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5535.930358886719,
            "unit": "ns",
            "range": "± 21.214683722437794"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5612.260665893555,
            "unit": "ns",
            "range": "± 51.23815621696851"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 178.73329365253448,
            "unit": "ns",
            "range": "± 0.2442384850767285"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 201.977184176445,
            "unit": "ns",
            "range": "± 0.47833224093825555"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 122.29177451133728,
            "unit": "ns",
            "range": "± 0.18856413510030307"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 132.42938041687012,
            "unit": "ns",
            "range": "± 0.13707485469352437"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2428.1166191101074,
            "unit": "ns",
            "range": "± 9.184965633696835"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2480.250503540039,
            "unit": "ns",
            "range": "± 5.694386985126787"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 194.37548089027405,
            "unit": "ns",
            "range": "± 1.318637300736186"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 483.54058837890625,
            "unit": "ns",
            "range": "± 1.5558016131233314"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.745134264230728,
            "unit": "ns",
            "range": "± 0.0062610718095961046"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 62.88507044315338,
            "unit": "ns",
            "range": "± 0.07787396878154533"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.4845438823103905,
            "unit": "ns",
            "range": "± 0.0022545474986648247"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.61567187309265,
            "unit": "ns",
            "range": "± 0.14580951216357235"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.713713675737381,
            "unit": "ns",
            "range": "± 0.003254236402248223"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.949127674102783,
            "unit": "ns",
            "range": "± 0.04168194396991828"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 502.87380599975586,
            "unit": "ns",
            "range": "± 0.672619514053333"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1033.7328367233276,
            "unit": "ns",
            "range": "± 8.438148714809335"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 359.8683080673218,
            "unit": "ns",
            "range": "± 0.48947870088157414"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 691.097412109375,
            "unit": "ns",
            "range": "± 2.9418676327881608"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 150.722531914711,
            "unit": "ns",
            "range": "± 0.09931400673185131"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 161.10510098934174,
            "unit": "ns",
            "range": "± 0.27155509307488906"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 154.0893063545227,
            "unit": "ns",
            "range": "± 0.09912059526638603"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 311.2994034290314,
            "unit": "ns",
            "range": "± 0.1880512626265192"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3841.448944091797,
            "unit": "ns",
            "range": "± 12.167354581021417"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4596.247798919678,
            "unit": "ns",
            "range": "± 8.433760275096255"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 195.6488891839981,
            "unit": "ns",
            "range": "± 0.15209721455694086"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 228.19438934326172,
            "unit": "ns",
            "range": "± 0.36305970062511705"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 161.97254586219788,
            "unit": "ns",
            "range": "± 0.07396477704239768"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 208.42953765392303,
            "unit": "ns",
            "range": "± 0.22958396468117606"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "33fee45ee99b1e17c7a276915a58ddfb16b1d5fb",
          "message": "fix(retry): stop after hook cancellation (#36)\n\n* fix(retry): stop after hook cancellation\n\nRefs #12\n\n* test(retry): cover netstandard cancellation\n\nRefs #12",
          "timestamp": "2026-08-21T11:44:21+01:00",
          "tree_id": "ad40fc948646297882f28a06beeb595680365c8c",
          "url": "https://github.com/thomhurst/Kevlar/commit/33fee45ee99b1e17c7a276915a58ddfb16b1d5fb"
        },
        "date": 1787309723322,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 171.57414638996124,
            "unit": "ns",
            "range": "± 0.4565777758800575"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 281.1660313606262,
            "unit": "ns",
            "range": "± 0.851556053909598"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5509.437194824219,
            "unit": "ns",
            "range": "± 9.270193108823806"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5468.839725494385,
            "unit": "ns",
            "range": "± 16.277120836854785"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 177.40207600593567,
            "unit": "ns",
            "range": "± 0.11252999746891518"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 204.4289116859436,
            "unit": "ns",
            "range": "± 0.3579735193689485"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 127.856614112854,
            "unit": "ns",
            "range": "± 0.07411732675813548"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 141.11455845832825,
            "unit": "ns",
            "range": "± 0.14538865769149442"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2423.6024436950684,
            "unit": "ns",
            "range": "± 5.588049149718157"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2461.5144004821777,
            "unit": "ns",
            "range": "± 2.5356276751191014"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 201.94030618667603,
            "unit": "ns",
            "range": "± 0.17315350162309467"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 468.9002158641815,
            "unit": "ns",
            "range": "± 1.2721286744751437"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.7099401839077473,
            "unit": "ns",
            "range": "± 0.002202437484311596"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 62.345013201236725,
            "unit": "ns",
            "range": "± 0.14827048997862424"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.410478539764881,
            "unit": "ns",
            "range": "± 0.0048449687116973"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 64.02649974822998,
            "unit": "ns",
            "range": "± 0.10343599933425981"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.900264196097851,
            "unit": "ns",
            "range": "± 0.0051272066327470505"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.942317128181458,
            "unit": "ns",
            "range": "± 0.034476516269908525"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 530.9978642463684,
            "unit": "ns",
            "range": "± 0.6282967210177446"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1004.2455253601074,
            "unit": "ns",
            "range": "± 0.9118835725350269"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 349.20221042633057,
            "unit": "ns",
            "range": "± 0.24696558096462456"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 716.6151304244995,
            "unit": "ns",
            "range": "± 1.5834022782604915"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 150.252867937088,
            "unit": "ns",
            "range": "± 0.19258281206476482"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 158.81021285057068,
            "unit": "ns",
            "range": "± 0.2938844636757077"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 152.53026282787323,
            "unit": "ns",
            "range": "± 0.052552734662196655"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 262.9687833786011,
            "unit": "ns",
            "range": "± 0.5492792236797994"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3904.131820678711,
            "unit": "ns",
            "range": "± 31.674627786080407"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4612.451728820801,
            "unit": "ns",
            "range": "± 12.38708031956528"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 190.68266248703003,
            "unit": "ns",
            "range": "± 0.21199065100793285"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 212.9145519733429,
            "unit": "ns",
            "range": "± 0.30663971760295355"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 159.03479278087616,
            "unit": "ns",
            "range": "± 0.04719659382359479"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 204.35148906707764,
            "unit": "ns",
            "range": "± 0.5338276934203604"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "c5080dc6cb5b13a2f16e05b8496f9acdb6e1fd03",
          "message": "fix(strategy): define continuation contract (#38)\n\n* fix(strategy): define continuation contract\n\nDefault continuations previously leaked NullReferenceException instead of returning a failure outcome.\n\nRefs #27\n\n* test(strategy): strengthen reuse coverage\n\nRefs #27",
          "timestamp": "2026-08-21T11:56:44+01:00",
          "tree_id": "4b469810eb22270c2406ca3109aa2d2b9e28a309",
          "url": "https://github.com/thomhurst/Kevlar/commit/c5080dc6cb5b13a2f16e05b8496f9acdb6e1fd03"
        },
        "date": 1787310445071,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 186.06505346298218,
            "unit": "ns",
            "range": "± 0.5145162181572293"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 278.05726146698,
            "unit": "ns",
            "range": "± 0.9081528299278715"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5393.271392822266,
            "unit": "ns",
            "range": "± 8.553326100970958"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5535.578544616699,
            "unit": "ns",
            "range": "± 14.116969734403947"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 180.11939311027527,
            "unit": "ns",
            "range": "± 0.07726266162133336"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 221.0778797864914,
            "unit": "ns",
            "range": "± 0.262922909027777"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 120.56208086013794,
            "unit": "ns",
            "range": "± 0.07051093751057792"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 134.6368055343628,
            "unit": "ns",
            "range": "± 0.05644903548371651"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2330.9423122406006,
            "unit": "ns",
            "range": "± 5.224307949524082"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2437.6512126922607,
            "unit": "ns",
            "range": "± 5.017346470122245"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 193.59239733219147,
            "unit": "ns",
            "range": "± 0.07376653402649819"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 498.20390939712524,
            "unit": "ns",
            "range": "± 0.6369617709179212"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.7118968069553375,
            "unit": "ns",
            "range": "± 0.003246435013793947"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.85535180568695,
            "unit": "ns",
            "range": "± 0.03768107482254901"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.4814707934856415,
            "unit": "ns",
            "range": "± 0.00328807600735827"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 60.99282419681549,
            "unit": "ns",
            "range": "± 0.08481566913423802"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.89944963529706,
            "unit": "ns",
            "range": "± 0.005540306380754207"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.544742196798325,
            "unit": "ns",
            "range": "± 0.024488035957627535"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 537.415521144867,
            "unit": "ns",
            "range": "± 0.45896663096287893"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 998.3802127838135,
            "unit": "ns",
            "range": "± 1.3861733845851918"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 372.3619718551636,
            "unit": "ns",
            "range": "± 0.8487995885803769"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 708.563624382019,
            "unit": "ns",
            "range": "± 2.338255432246097"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 153.35149812698364,
            "unit": "ns",
            "range": "± 0.03237606151311915"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 160.92388319969177,
            "unit": "ns",
            "range": "± 0.24058901554325357"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 150.3301581144333,
            "unit": "ns",
            "range": "± 0.12322105111603514"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 265.77941513061523,
            "unit": "ns",
            "range": "± 0.2581375472515886"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3772.693592071533,
            "unit": "ns",
            "range": "± 5.909514232934097"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4481.5793380737305,
            "unit": "ns",
            "range": "± 9.523000733088553"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 210.58988499641418,
            "unit": "ns",
            "range": "± 0.23409563349242746"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 206.9563181400299,
            "unit": "ns",
            "range": "± 0.24215678827882417"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 163.32903027534485,
            "unit": "ns",
            "range": "± 0.07762420412180052"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 210.4509561061859,
            "unit": "ns",
            "range": "± 0.28506598066827954"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e540c196c5a60bfba6c84da4077b7192e499812e",
          "message": "fix(rate-limit): reclaim cancelled reservations (#40)\n\n* fix(rate-limit): reclaim cancelled slots\n\nRefs #15\n\n* fix(rate-limit): make handoff race-safe\n\nReplace disposable turn tokens with one-shot task signals so concurrent cancellation and promotion cannot race disposal.\n\nRefs #15",
          "timestamp": "2026-08-21T12:15:16+01:00",
          "tree_id": "9f2e15a7eefbf3ee968f995dcc70af7e6892fae7",
          "url": "https://github.com/thomhurst/Kevlar/commit/e540c196c5a60bfba6c84da4077b7192e499812e"
        },
        "date": 1787311586815,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 199.68953442573547,
            "unit": "ns",
            "range": "± 0.8597789660376689"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 271.8529095649719,
            "unit": "ns",
            "range": "± 0.395074602718618"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5419.137168884277,
            "unit": "ns",
            "range": "± 7.356481420907702"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5383.382019042969,
            "unit": "ns",
            "range": "± 26.07157456972864"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 176.17891108989716,
            "unit": "ns",
            "range": "± 0.03798706835418152"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 201.43549466133118,
            "unit": "ns",
            "range": "± 0.1972496309919912"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 126.35560846328735,
            "unit": "ns",
            "range": "± 0.2727041731053917"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 131.4339338541031,
            "unit": "ns",
            "range": "± 0.18713442671645636"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2300.9933910369873,
            "unit": "ns",
            "range": "± 4.515243538868633"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2394.2187519073486,
            "unit": "ns",
            "range": "± 7.398613406993124"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 197.8159544467926,
            "unit": "ns",
            "range": "± 0.4219061640759685"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 472.9457449913025,
            "unit": "ns",
            "range": "± 1.3791350917203646"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.709364429116249,
            "unit": "ns",
            "range": "± 0.001980338026488256"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.00918132066727,
            "unit": "ns",
            "range": "± 0.03207927304205786"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.479835368692875,
            "unit": "ns",
            "range": "± 0.0018558292045968892"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 64.05527514219284,
            "unit": "ns",
            "range": "± 0.019598022403572033"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.9037094116210938,
            "unit": "ns",
            "range": "± 0.006075453586954591"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.34384974837303,
            "unit": "ns",
            "range": "± 0.03748529469056075"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 501.00201320648193,
            "unit": "ns",
            "range": "± 1.178834452291353"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 995.4616842269897,
            "unit": "ns",
            "range": "± 2.1091256257952398"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 344.5991163253784,
            "unit": "ns",
            "range": "± 0.6669850745584261"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 721.2748870849609,
            "unit": "ns",
            "range": "± 2.0911527636259626"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 149.65593028068542,
            "unit": "ns",
            "range": "± 0.09323048572269985"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 160.36435616016388,
            "unit": "ns",
            "range": "± 0.19279791185213613"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 163.59381008148193,
            "unit": "ns",
            "range": "± 0.05474314402894745"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 255.79928255081177,
            "unit": "ns",
            "range": "± 0.1545714867142353"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3767.076972961426,
            "unit": "ns",
            "range": "± 8.520184082053003"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4371.014251708984,
            "unit": "ns",
            "range": "± 8.7964981225394"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 185.61612021923065,
            "unit": "ns",
            "range": "± 0.27935816178015654"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 213.64956331253052,
            "unit": "ns",
            "range": "± 0.3768050901302244"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 160.95757007598877,
            "unit": "ns",
            "range": "± 0.2727189639124016"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 194.58133268356323,
            "unit": "ns",
            "range": "± 0.2203246401604462"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "c3ffb96232244572b0bd6b2fc618eb77553f5ac9",
          "message": "test(ci): enforce coverage and mutation gates (#37)\n\n* test(ci): enforce quality gates\n\nRefs #9\n\n* fix(ci): address quality gate review\n\nRefs #9\n\n* fix(ci): cover shared mutation dependencies\n\n* fix(ci): trigger mutation for package changes\n\nInclude the central package manifest so test dependency updates cannot bypass the mutation gate.\n\nRefs #9\n\n* fix(ci): close quality-gate omissions",
          "timestamp": "2026-08-21T12:29:14+01:00",
          "tree_id": "2979dbb7c9cdc5d1294b29d7da747710219330ed",
          "url": "https://github.com/thomhurst/Kevlar/commit/c3ffb96232244572b0bd6b2fc618eb77553f5ac9"
        },
        "date": 1787312407227,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 201.10505771636963,
            "unit": "ns",
            "range": "± 0.5747349854787847"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 274.39078426361084,
            "unit": "ns",
            "range": "± 1.5324805688187997"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5259.1363525390625,
            "unit": "ns",
            "range": "± 13.739954772634302"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5279.814010620117,
            "unit": "ns",
            "range": "± 10.313161682560452"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 184.89420008659363,
            "unit": "ns",
            "range": "± 0.26347772569333566"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 208.64661574363708,
            "unit": "ns",
            "range": "± 0.13004540222387045"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 134.3325344324112,
            "unit": "ns",
            "range": "± 0.47923930066205966"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 142.3271862268448,
            "unit": "ns",
            "range": "± 0.1837662926529707"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2331.888328552246,
            "unit": "ns",
            "range": "± 10.340640725717185"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2410.02925491333,
            "unit": "ns",
            "range": "± 2.4179617997861063"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 188.23218095302582,
            "unit": "ns",
            "range": "± 0.29537021689531406"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 477.0956449508667,
            "unit": "ns",
            "range": "± 1.9493128624231362"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 3.371359661221504,
            "unit": "ns",
            "range": "± 0.003038533065077027"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 66.35040533542633,
            "unit": "ns",
            "range": "± 0.031829497514471755"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 3.033709794282913,
            "unit": "ns",
            "range": "± 0.0015832830812598144"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.1553755402565,
            "unit": "ns",
            "range": "± 0.26819314425513247"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.5436098352074623,
            "unit": "ns",
            "range": "± 0.003117735326725368"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.42852407693863,
            "unit": "ns",
            "range": "± 0.026633541541086433"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 583.1075258255005,
            "unit": "ns",
            "range": "± 0.4490663471264993"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1013.8645582199097,
            "unit": "ns",
            "range": "± 1.9124146965050302"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 393.36326146125793,
            "unit": "ns",
            "range": "± 0.5405039387617588"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 716.6436424255371,
            "unit": "ns",
            "range": "± 2.363917328189357"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 156.8432924747467,
            "unit": "ns",
            "range": "± 0.1470119916521297"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 158.1380295753479,
            "unit": "ns",
            "range": "± 0.18722406480081527"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 168.57353711128235,
            "unit": "ns",
            "range": "± 0.16865543944772565"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 270.7652542591095,
            "unit": "ns",
            "range": "± 0.14178640216620325"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3701.6615142822266,
            "unit": "ns",
            "range": "± 10.077272082959134"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4449.235618591309,
            "unit": "ns",
            "range": "± 6.038897941693431"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 208.77822637557983,
            "unit": "ns",
            "range": "± 0.16441681353084592"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 205.2902593612671,
            "unit": "ns",
            "range": "± 0.31964266002407077"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 175.2363177537918,
            "unit": "ns",
            "range": "± 0.2986976285684927"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 209.15527081489563,
            "unit": "ns",
            "range": "± 0.35353874724868806"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "1f2d8f1da63e1a30cf676af41d0c49b159dfc122",
          "message": "fix(circuit-breaker): normalize shared timelines (#53)\n\n* fix(circuit-breaker): normalize elapsed time\n\n* fix(circuit-breaker): rebase provider clocks\n\nAnchor newly observed providers to the shared logical timeline and calculate signed timestamp deltas without long overflow.\n\n* fix(circuit-breaker): preserve timestamp rollover",
          "timestamp": "2026-08-21T13:16:35+01:00",
          "tree_id": "50130ea5b15b57e4464201c3e84045d7eb458189",
          "url": "https://github.com/thomhurst/Kevlar/commit/1f2d8f1da63e1a30cf676af41d0c49b159dfc122"
        },
        "date": 1787315270860,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 186.96023893356323,
            "unit": "ns",
            "range": "± 0.25301956722423774"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 282.3452932834625,
            "unit": "ns",
            "range": "± 2.5550516858968138"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 6568.016693115234,
            "unit": "ns",
            "range": "± 20.614192974597856"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5586.487392425537,
            "unit": "ns",
            "range": "± 6.5532461186119955"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 182.68904638290405,
            "unit": "ns",
            "range": "± 0.11917617521259602"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 202.02890527248383,
            "unit": "ns",
            "range": "± 0.2848176035260273"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 121.32480907440186,
            "unit": "ns",
            "range": "± 0.060943090842477676"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 137.7025499343872,
            "unit": "ns",
            "range": "± 0.07241292702684982"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2431.922206878662,
            "unit": "ns",
            "range": "± 4.61986163814884"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2538.3523712158203,
            "unit": "ns",
            "range": "± 4.060976414816573"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 194.1320357322693,
            "unit": "ns",
            "range": "± 0.159783360270451"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 472.6305160522461,
            "unit": "ns",
            "range": "± 0.5629113791033027"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 2.72910039126873,
            "unit": "ns",
            "range": "± 0.0028467607807219322"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.98559236526489,
            "unit": "ns",
            "range": "± 0.1138397207540631"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 2.4828397557139397,
            "unit": "ns",
            "range": "± 0.004566543024712295"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 60.91642868518829,
            "unit": "ns",
            "range": "± 0.10611104874841663"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 2.808331675827503,
            "unit": "ns",
            "range": "± 0.0024800714150707646"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.633326947689056,
            "unit": "ns",
            "range": "± 0.0152148049178539"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 505.8077464103699,
            "unit": "ns",
            "range": "± 0.7814801991719031"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 994.791582107544,
            "unit": "ns",
            "range": "± 1.5502928917468426"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 360.0566520690918,
            "unit": "ns",
            "range": "± 0.8012772285187043"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 727.8408260345459,
            "unit": "ns",
            "range": "± 1.5538253994135303"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 166.60571193695068,
            "unit": "ns",
            "range": "± 5.0120006441365605"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 164.9230740070343,
            "unit": "ns",
            "range": "± 0.13478020326716786"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 152.52315604686737,
            "unit": "ns",
            "range": "± 0.09809993488373084"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 260.9508411884308,
            "unit": "ns",
            "range": "± 0.28859478654893295"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3847.0496253967285,
            "unit": "ns",
            "range": "± 15.626959256931528"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4522.052047729492,
            "unit": "ns",
            "range": "± 3.87209420994151"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 197.6869077682495,
            "unit": "ns",
            "range": "± 0.13016584860169986"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 209.2720766067505,
            "unit": "ns",
            "range": "± 0.346864265060476"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 166.19503951072693,
            "unit": "ns",
            "range": "± 0.13314928256795638"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 198.67738699913025,
            "unit": "ns",
            "range": "± 0.23722190173986818"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "2342c5d9c6185fb0264d6bad79c49364ad630f9f",
          "message": "test(model): add deterministic strategy invariants (#60)\n\nAdd replayable, minimized command models for breaker, rate-limit, concurrency, retry/backoff, and composition contracts, plus scheduled seeded sweeps. Fix Reset retaining a closed breaker failure streak.",
          "timestamp": "2026-08-21T13:35:19+01:00",
          "tree_id": "8a854f7c38cd2e633df30b6ed9c16df28e76f852",
          "url": "https://github.com/thomhurst/Kevlar/commit/2342c5d9c6185fb0264d6bad79c49364ad630f9f"
        },
        "date": 1787316363670,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 180.40636610984802,
            "unit": "ns",
            "range": "± 0.12855085609805925"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 271.7431745529175,
            "unit": "ns",
            "range": "± 0.8537701251924918"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5689.747627258301,
            "unit": "ns",
            "range": "± 6.825062240877951"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5740.021789550781,
            "unit": "ns",
            "range": "± 11.686469487037561"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 180.09997153282166,
            "unit": "ns",
            "range": "± 0.09724968102390139"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 202.4935839176178,
            "unit": "ns",
            "range": "± 0.3092619744632474"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 129.2688183784485,
            "unit": "ns",
            "range": "± 0.13284145820248314"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 136.4078643321991,
            "unit": "ns",
            "range": "± 0.08654284114365136"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2431.479652404785,
            "unit": "ns",
            "range": "± 4.8828503470448155"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2513.561304092407,
            "unit": "ns",
            "range": "± 6.222726058038694"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 205.27033138275146,
            "unit": "ns",
            "range": "± 0.16281573072110891"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 474.1064863204956,
            "unit": "ns",
            "range": "± 0.45303615288493143"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 22.100077390670776,
            "unit": "ns",
            "range": "± 0.010247066322816148"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.43663954734802,
            "unit": "ns",
            "range": "± 0.05712477131966674"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 13.989485010504723,
            "unit": "ns",
            "range": "± 0.019109018477111096"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.17881166934967,
            "unit": "ns",
            "range": "± 0.04511023226047903"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.07599213719368,
            "unit": "ns",
            "range": "± 0.0027305922799340946"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.617246568202972,
            "unit": "ns",
            "range": "± 0.018649869884370784"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 522.021185874939,
            "unit": "ns",
            "range": "± 0.39961875182628515"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1000.2208213806152,
            "unit": "ns",
            "range": "± 1.7554128387178805"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 367.4217138290405,
            "unit": "ns",
            "range": "± 0.44978570395994255"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 697.0597791671753,
            "unit": "ns",
            "range": "± 1.8212337179312297"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 150.08940434455872,
            "unit": "ns",
            "range": "± 0.06123890053570741"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 156.35427522659302,
            "unit": "ns",
            "range": "± 0.1628074213596718"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 161.09535837173462,
            "unit": "ns",
            "range": "± 0.08460142899113342"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 265.88448667526245,
            "unit": "ns",
            "range": "± 0.17128325718357099"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3883.7629013061523,
            "unit": "ns",
            "range": "± 5.896124350512261"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4606.16145324707,
            "unit": "ns",
            "range": "± 4.2206522804894835"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 189.56026887893677,
            "unit": "ns",
            "range": "± 0.14138655313917342"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 209.57928943634033,
            "unit": "ns",
            "range": "± 0.1509881371474704"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 166.61440193653107,
            "unit": "ns",
            "range": "± 0.1750397580591414"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 203.21978223323822,
            "unit": "ns",
            "range": "± 0.22418410522491922"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "da971fd27f8c2343795e680923f2e791fd87be9c",
          "message": "fix(metrics): follow OTel conventions (#65)",
          "timestamp": "2026-08-21T13:54:13+01:00",
          "tree_id": "f02eb23fe22922944637c1333059c8277e3974ee",
          "url": "https://github.com/thomhurst/Kevlar/commit/da971fd27f8c2343795e680923f2e791fd87be9c"
        },
        "date": 1787317485396,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 183.38107872009277,
            "unit": "ns",
            "range": "± 0.44154612630784373"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 285.5644040107727,
            "unit": "ns",
            "range": "± 1.257566156173143"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5533.143955230713,
            "unit": "ns",
            "range": "± 23.064602518214514"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5492.58967590332,
            "unit": "ns",
            "range": "± 24.245013466140048"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 178.75168764591217,
            "unit": "ns",
            "range": "± 0.20351866267759416"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 200.03366112709045,
            "unit": "ns",
            "range": "± 0.2649480007536973"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 131.36816024780273,
            "unit": "ns",
            "range": "± 0.21382376699881214"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 136.7875792980194,
            "unit": "ns",
            "range": "± 1.0778535647069647"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2392.982677459717,
            "unit": "ns",
            "range": "± 5.4740929169322845"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2484.519931793213,
            "unit": "ns",
            "range": "± 4.91137494067941"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 200.75033736228943,
            "unit": "ns",
            "range": "± 0.33129063681879256"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 482.335036277771,
            "unit": "ns",
            "range": "± 3.3610354548683055"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 22.10399693250656,
            "unit": "ns",
            "range": "± 0.0089092438446221"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 69.60576689243317,
            "unit": "ns",
            "range": "± 0.10837672679708184"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.006730288267136,
            "unit": "ns",
            "range": "± 0.028673465922366585"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 62.20450550317764,
            "unit": "ns",
            "range": "± 0.19283035899537385"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.07846886664629,
            "unit": "ns",
            "range": "± 0.0052863962999570545"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 45.46284395456314,
            "unit": "ns",
            "range": "± 0.152558157677066"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 508.38255405426025,
            "unit": "ns",
            "range": "± 0.5788455621748416"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 983.2603302001953,
            "unit": "ns",
            "range": "± 2.739990401447061"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 367.3995580673218,
            "unit": "ns",
            "range": "± 0.37793134568794906"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 693.2853784561157,
            "unit": "ns",
            "range": "± 1.493607677414836"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 150.39066743850708,
            "unit": "ns",
            "range": "± 0.14802584550823802"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 163.61648178100586,
            "unit": "ns",
            "range": "± 0.30473563948211063"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 161.28864407539368,
            "unit": "ns",
            "range": "± 0.09877421096753164"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 265.8059649467468,
            "unit": "ns",
            "range": "± 0.39872599971856154"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3867.4377975463867,
            "unit": "ns",
            "range": "± 6.762499024182467"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4454.0565757751465,
            "unit": "ns",
            "range": "± 5.901819920732001"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 190.50497579574585,
            "unit": "ns",
            "range": "± 0.25404636391637847"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 219.51552093029022,
            "unit": "ns",
            "range": "± 0.2264241019019963"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 169.68896746635437,
            "unit": "ns",
            "range": "± 0.14820207257138693"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 200.4799828529358,
            "unit": "ns",
            "range": "± 0.2733434968879794"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "3e48e9fbeb0cfb409eea6f92f814cf9326fd8a84",
          "message": "fix(circuit-breaker): serialize transitions (#61)\n\n* fix(circuit-breaker): serialize transitions\n\n* fix(circuit-breaker): contain metrics failures\n\n* fix(circuit-breaker): guard publication recovery\n\n* fix(circuit): attribute reentrant failures\n\n* test(circuit): follow OTel metric tags",
          "timestamp": "2026-08-21T14:17:23+01:00",
          "tree_id": "99e215bb590f8cabfc23e75c3ebb2ae7ca241751",
          "url": "https://github.com/thomhurst/Kevlar/commit/3e48e9fbeb0cfb409eea6f92f814cf9326fd8a84"
        },
        "date": 1787318902386,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 185.68898391723633,
            "unit": "ns",
            "range": "± 0.3309763262454414"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 269.56402587890625,
            "unit": "ns",
            "range": "± 1.7568160270713948"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5630.445137023926,
            "unit": "ns",
            "range": "± 16.308858155607293"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5598.202293395996,
            "unit": "ns",
            "range": "± 10.017261217179266"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 179.12815499305725,
            "unit": "ns",
            "range": "± 0.08353489076054038"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 212.60020089149475,
            "unit": "ns",
            "range": "± 0.1690404050386926"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 134.33460342884064,
            "unit": "ns",
            "range": "± 0.12009046155187955"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 132.82201838493347,
            "unit": "ns",
            "range": "± 0.1357599102097647"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2460.8343353271484,
            "unit": "ns",
            "range": "± 5.91647908610049"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2571.971996307373,
            "unit": "ns",
            "range": "± 7.570935800116206"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 204.33040308952332,
            "unit": "ns",
            "range": "± 0.4966829061350102"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 484.44569158554077,
            "unit": "ns",
            "range": "± 0.5438259599187413"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 22.264541566371918,
            "unit": "ns",
            "range": "± 0.010592740337085068"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.1355921626091,
            "unit": "ns",
            "range": "± 0.049548257352591475"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.007102817296982,
            "unit": "ns",
            "range": "± 0.014552174946853702"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.05902409553528,
            "unit": "ns",
            "range": "± 0.037671392155952554"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.075314179062843,
            "unit": "ns",
            "range": "± 0.015228077757360547"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 37.993572890758514,
            "unit": "ns",
            "range": "± 0.03040876547503083"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 503.8071050643921,
            "unit": "ns",
            "range": "± 0.4442941472934118"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 986.8740558624268,
            "unit": "ns",
            "range": "± 2.03593792081195"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 358.72507667541504,
            "unit": "ns",
            "range": "± 0.7077795670357583"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 696.7648830413818,
            "unit": "ns",
            "range": "± 3.810300239056009"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 154.30589485168457,
            "unit": "ns",
            "range": "± 0.10100182033262921"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 155.66207814216614,
            "unit": "ns",
            "range": "± 0.24173700225211994"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 161.68988037109375,
            "unit": "ns",
            "range": "± 0.059923170435718626"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 269.10399770736694,
            "unit": "ns",
            "range": "± 0.40150317594082846"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3873.1522827148438,
            "unit": "ns",
            "range": "± 4.774496005259676"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4545.155517578125,
            "unit": "ns",
            "range": "± 4.247355563172925"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 197.16088318824768,
            "unit": "ns",
            "range": "± 0.24727438824995426"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 208.63610863685608,
            "unit": "ns",
            "range": "± 0.38902498051894924"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 165.79024469852448,
            "unit": "ns",
            "range": "± 0.05821284383551027"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 204.1415364742279,
            "unit": "ns",
            "range": "± 0.2953931624185817"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "a0a3cb7a18040fc31121a06d2d7d29245fc7e1ac",
          "message": "test(packaging): verify publish compatibility (#64)\n\n* test(packaging): verify publish compatibility\n\n* fix(packaging): address compatibility review\n\nPreserve definition-owned defaults, improve configuration errors, and make publish consumers derive versions and feeds from repository inputs.\n\n* fix(di): reject empty configuration values\n\n* fix(config): preserve nullable binding\n\n* test(packaging): pin local Kevlar packages",
          "timestamp": "2026-08-21T14:44:13+01:00",
          "tree_id": "65fe1aa8c8183e6c295d5987666b51652883962b",
          "url": "https://github.com/thomhurst/Kevlar/commit/a0a3cb7a18040fc31121a06d2d7d29245fc7e1ac"
        },
        "date": 1787320466225,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 180.93350744247437,
            "unit": "ns",
            "range": "± 0.536260142622767"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 276.49785590171814,
            "unit": "ns",
            "range": "± 1.0773675383691113"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5397.847023010254,
            "unit": "ns",
            "range": "± 14.808979392861382"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5354.371551513672,
            "unit": "ns",
            "range": "± 6.0892576211771505"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 183.19624710083008,
            "unit": "ns",
            "range": "± 0.082595727011405"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 211.28061938285828,
            "unit": "ns",
            "range": "± 0.31075441832176987"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 125.82536482810974,
            "unit": "ns",
            "range": "± 0.3860134331815159"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 148.4819576740265,
            "unit": "ns",
            "range": "± 0.960482097347172"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2333.370090484619,
            "unit": "ns",
            "range": "± 2.526234743554601"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2407.987106323242,
            "unit": "ns",
            "range": "± 3.934007170639832"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 192.10118126869202,
            "unit": "ns",
            "range": "± 0.11390115175310263"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 477.8067727088928,
            "unit": "ns",
            "range": "± 0.31356624108471437"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 17.690571382641792,
            "unit": "ns",
            "range": "± 0.0036592027074790947"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 61.25165629386902,
            "unit": "ns",
            "range": "± 0.09196657397844332"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 13.514187827706337,
            "unit": "ns",
            "range": "± 0.010492346914943058"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 62.51282984018326,
            "unit": "ns",
            "range": "± 0.07591381844385517"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.951126404106617,
            "unit": "ns",
            "range": "± 0.006444017877158125"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.79310804605484,
            "unit": "ns",
            "range": "± 0.03527106236393591"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 528.5975832939148,
            "unit": "ns",
            "range": "± 0.8537017799976834"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1050.752275466919,
            "unit": "ns",
            "range": "± 2.2000001997866216"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 383.42095851898193,
            "unit": "ns",
            "range": "± 0.3395427533618238"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 700.4272413253784,
            "unit": "ns",
            "range": "± 1.968553922078751"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 152.88263463974,
            "unit": "ns",
            "range": "± 0.2838236948516159"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 154.10035109519958,
            "unit": "ns",
            "range": "± 0.1633999319600304"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 155.84832239151,
            "unit": "ns",
            "range": "± 0.22437141349363998"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 258.27237200737,
            "unit": "ns",
            "range": "± 0.9534767119239298"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3759.14412689209,
            "unit": "ns",
            "range": "± 16.463447883196324"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4438.523635864258,
            "unit": "ns",
            "range": "± 4.95446549665664"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 197.34421241283417,
            "unit": "ns",
            "range": "± 0.10504506608788144"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 206.42811107635498,
            "unit": "ns",
            "range": "± 0.27038211544402463"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 167.47592496871948,
            "unit": "ns",
            "range": "± 0.18015691713427281"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 205.1731470823288,
            "unit": "ns",
            "range": "± 0.2405465282642759"
          }
        ]
      },
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
          "id": "c6c77738d284b210bf4093808052e939918e99a2",
          "message": "docs: normalize benchmark sidebar labels",
          "timestamp": "2026-08-21T15:43:08+01:00",
          "tree_id": "f43e5330a9fb362b88d5fb581b69e5fc7844cf12",
          "url": "https://github.com/thomhurst/Kevlar/commit/c6c77738d284b210bf4093808052e939918e99a2"
        },
        "date": 1787324027948,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 172.02863121032715,
            "unit": "ns",
            "range": "± 0.17402223234885658"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 279.2033941745758,
            "unit": "ns",
            "range": "± 0.7906667920148767"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5600.158630371094,
            "unit": "ns",
            "range": "± 16.235124583068377"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5567.576110839844,
            "unit": "ns",
            "range": "± 17.608188625325926"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 181.6563514471054,
            "unit": "ns",
            "range": "± 0.04932083588974231"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 204.85730576515198,
            "unit": "ns",
            "range": "± 0.49144814414627885"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 131.83275175094604,
            "unit": "ns",
            "range": "± 0.06111520558224006"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 137.65768373012543,
            "unit": "ns",
            "range": "± 0.05161766086373435"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2420.2631816864014,
            "unit": "ns",
            "range": "± 3.7613698914810496"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2471.1032524108887,
            "unit": "ns",
            "range": "± 3.922637223110067"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 203.76507198810577,
            "unit": "ns",
            "range": "± 0.3441476728816389"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 469.28602361679077,
            "unit": "ns",
            "range": "± 0.3816255102091162"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 22.097232222557068,
            "unit": "ns",
            "range": "± 0.015583983538562801"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.896306931972504,
            "unit": "ns",
            "range": "± 0.29899447040608634"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 21.365587562322617,
            "unit": "ns",
            "range": "± 0.01190984225030573"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 60.6974436044693,
            "unit": "ns",
            "range": "± 0.053074295293931664"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.079784587025642,
            "unit": "ns",
            "range": "± 0.00415005745909324"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.03191912174225,
            "unit": "ns",
            "range": "± 0.03548974186934738"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 522.0656900405884,
            "unit": "ns",
            "range": "± 0.40892275130265004"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1008.4194831848145,
            "unit": "ns",
            "range": "± 1.5242745967427767"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 351.8734042644501,
            "unit": "ns",
            "range": "± 1.0047885758313357"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 711.8201704025269,
            "unit": "ns",
            "range": "± 2.6242874770416056"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 161.6415821313858,
            "unit": "ns",
            "range": "± 0.16878448531616064"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 161.048344373703,
            "unit": "ns",
            "range": "± 0.11983966936451132"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 164.67377877235413,
            "unit": "ns",
            "range": "± 0.11425336735730748"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 261.6488547325134,
            "unit": "ns",
            "range": "± 0.13125603224474686"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3873.155517578125,
            "unit": "ns",
            "range": "± 13.974593655491832"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4526.297435760498,
            "unit": "ns",
            "range": "± 14.663025618058493"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 194.16215133666992,
            "unit": "ns",
            "range": "± 0.36816911765168076"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 225.414324760437,
            "unit": "ns",
            "range": "± 0.25323438766220807"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 162.33350110054016,
            "unit": "ns",
            "range": "± 0.07797733939291156"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 201.3897807598114,
            "unit": "ns",
            "range": "± 0.1885832314689829"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "208e86841aecbd9a110ff708057b0667fee86685",
          "message": "feat(execution): add outcome state overloads (#82)\n\n* feat(execution): add outcome state overloads\n\nRefs #80\n\n* test(execution): tighten state coverage",
          "timestamp": "2026-08-21T16:10:40+01:00",
          "tree_id": "a40cfd8de2eedc74e6617f87a857b761b13b1b31",
          "url": "https://github.com/thomhurst/Kevlar/commit/208e86841aecbd9a110ff708057b0667fee86685"
        },
        "date": 1787325703456,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 179.46510136127472,
            "unit": "ns",
            "range": "± 0.410146490972913"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 292.81817603111267,
            "unit": "ns",
            "range": "± 0.9554515140788755"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5542.7989501953125,
            "unit": "ns",
            "range": "± 14.302391170092095"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5537.84415435791,
            "unit": "ns",
            "range": "± 10.226522327577444"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 176.18022775650024,
            "unit": "ns",
            "range": "± 0.08797575584429652"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 203.84555101394653,
            "unit": "ns",
            "range": "± 0.5353465206329668"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 131.87566351890564,
            "unit": "ns",
            "range": "± 0.049794953716427004"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 135.76665830612183,
            "unit": "ns",
            "range": "± 0.09560988855110104"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2407.6656074523926,
            "unit": "ns",
            "range": "± 6.155075952384566"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2539.995370864868,
            "unit": "ns",
            "range": "± 4.0751717621327"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 200.2328041791916,
            "unit": "ns",
            "range": "± 0.09556592686436231"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 483.6880602836609,
            "unit": "ns",
            "range": "± 1.346521668756093"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 21.804676800966263,
            "unit": "ns",
            "range": "± 0.008501173162947455"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.88833451271057,
            "unit": "ns",
            "range": "± 0.040833777446119476"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 86.94376713037491,
            "unit": "ns",
            "range": "± 0.04411562783755195"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 98.43590581417084,
            "unit": "ns",
            "range": "± 0.04450621189800369"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 13.963204652071,
            "unit": "ns",
            "range": "± 0.012771260771391882"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 62.54230314493179,
            "unit": "ns",
            "range": "± 0.037440470556406404"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.079867877066135,
            "unit": "ns",
            "range": "± 0.005729477894905013"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.512651354074478,
            "unit": "ns",
            "range": "± 0.014464552435504745"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 486.14370489120483,
            "unit": "ns",
            "range": "± 1.100913878360016"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1011.1846303939819,
            "unit": "ns",
            "range": "± 1.5809087142827543"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 345.8659932613373,
            "unit": "ns",
            "range": "± 0.1763814675433475"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 687.6553039550781,
            "unit": "ns",
            "range": "± 1.774607049438388"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 152.07716929912567,
            "unit": "ns",
            "range": "± 0.06048291290856612"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 157.16374385356903,
            "unit": "ns",
            "range": "± 0.04517943035412959"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 163.70572245121002,
            "unit": "ns",
            "range": "± 0.05376989942766565"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 263.14142179489136,
            "unit": "ns",
            "range": "± 0.19324899639218038"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3801.7878341674805,
            "unit": "ns",
            "range": "± 6.623395619157551"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4517.3134841918945,
            "unit": "ns",
            "range": "± 5.496100211943171"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 208.71267116069794,
            "unit": "ns",
            "range": "± 0.3478170606520436"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 216.10221672058105,
            "unit": "ns",
            "range": "± 0.20935027606969941"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 162.70142149925232,
            "unit": "ns",
            "range": "± 0.22909817478315578"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 204.26957714557648,
            "unit": "ns",
            "range": "± 0.09145372497220437"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "a1addff6d56b79b18eecf141be016b84faaeed9c",
          "message": "feat(di): support atomic config reload (#91)",
          "timestamp": "2026-08-21T16:41:15+01:00",
          "tree_id": "93e40fc0fd70b39a4709e367eb4966b50a196832",
          "url": "https://github.com/thomhurst/Kevlar/commit/a1addff6d56b79b18eecf141be016b84faaeed9c"
        },
        "date": 1787327599389,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 180.3982309103012,
            "unit": "ns",
            "range": "± 0.3004379897797641"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 279.3060646057129,
            "unit": "ns",
            "range": "± 0.8782077289548084"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5657.125015258789,
            "unit": "ns",
            "range": "± 11.897380464608684"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5565.3707275390625,
            "unit": "ns",
            "range": "± 6.396346901566056"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 182.5866506099701,
            "unit": "ns",
            "range": "± 0.1653795711679974"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 208.1427607536316,
            "unit": "ns",
            "range": "± 0.1858615578251447"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 132.26990258693695,
            "unit": "ns",
            "range": "± 0.17130579872891544"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 134.21591424942017,
            "unit": "ns",
            "range": "± 0.37433091946649955"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2419.440444946289,
            "unit": "ns",
            "range": "± 9.16542070404978"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2472.860794067383,
            "unit": "ns",
            "range": "± 3.1022193641601095"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 203.36835622787476,
            "unit": "ns",
            "range": "± 0.30648881290057456"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 464.3897376060486,
            "unit": "ns",
            "range": "± 0.5634357045147457"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 22.01589821279049,
            "unit": "ns",
            "range": "± 0.008961789309615989"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.650211215019226,
            "unit": "ns",
            "range": "± 0.1317728279689519"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 87.45006895065308,
            "unit": "ns",
            "range": "± 0.05837792359822355"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 95.3284997344017,
            "unit": "ns",
            "range": "± 0.15129878828275617"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 13.948310866951942,
            "unit": "ns",
            "range": "± 0.016704218537630863"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.71277058124542,
            "unit": "ns",
            "range": "± 0.05644357769804438"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.1476315557956696,
            "unit": "ns",
            "range": "± 0.034877475294553296"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.359005093574524,
            "unit": "ns",
            "range": "± 0.03507799773382522"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 503.8538055419922,
            "unit": "ns",
            "range": "± 0.9222361474139101"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1023.5759010314941,
            "unit": "ns",
            "range": "± 2.0727310791460445"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 350.95016050338745,
            "unit": "ns",
            "range": "± 0.4363269963334695"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 694.2814178466797,
            "unit": "ns",
            "range": "± 3.056027700269231"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 154.00945246219635,
            "unit": "ns",
            "range": "± 0.47116248250982506"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 160.13777470588684,
            "unit": "ns",
            "range": "± 0.1547665806771495"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.9757410511374474,
            "unit": "ns",
            "range": "± 0.009300495789454598"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.8968227803707123,
            "unit": "ns",
            "range": "± 0.0042540764293294495"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 160.8143446445465,
            "unit": "ns",
            "range": "± 0.05254908021727439"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 270.5694966316223,
            "unit": "ns",
            "range": "± 0.6685159603597121"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3914.9047775268555,
            "unit": "ns",
            "range": "± 13.318552111276516"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4413.851013183594,
            "unit": "ns",
            "range": "± 10.045469357786853"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 193.68235564231873,
            "unit": "ns",
            "range": "± 0.1400289642646979"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 210.47886753082275,
            "unit": "ns",
            "range": "± 0.3025899512260888"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 162.04079174995422,
            "unit": "ns",
            "range": "± 0.05817203404175898"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 204.18135499954224,
            "unit": "ns",
            "range": "± 0.11531980470739886"
          }
        ]
      },
      {
        "commit": {
          "author": {
            "email": "30480171+thomhurst@users.noreply.github.com",
            "name": "Tom Longhurst",
            "username": "thomhurst"
          },
          "committer": {
            "email": "noreply@github.com",
            "name": "GitHub",
            "username": "web-flow"
          },
          "distinct": true,
          "id": "e0f15319223a0ade737505eb426fa2e609bb5e97",
          "message": "feat(partitioning): add bounded shield providers (#85)\n\n* feat(partitioning): add bounded providers\n\n* fix(partitioning): run factories outside lock\n\n* fix(partitioning): publish creation atomically",
          "timestamp": "2026-08-21T17:18:00+01:00",
          "tree_id": "5999d4dcad21e5b975fb7d6596e772f0019a532f",
          "url": "https://github.com/thomhurst/Kevlar/commit/e0f15319223a0ade737505eb426fa2e609bb5e97"
        },
        "date": 1787330240153,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 202.1917450428009,
            "unit": "ns",
            "range": "± 0.4618780626653408"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 275.81876516342163,
            "unit": "ns",
            "range": "± 0.7439507942218732"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5488.499984741211,
            "unit": "ns",
            "range": "± 15.482701398044327"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5314.304302215576,
            "unit": "ns",
            "range": "± 9.000939288739875"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 197.29032492637634,
            "unit": "ns",
            "range": "± 0.1415738255443133"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 197.37583184242249,
            "unit": "ns",
            "range": "± 0.30720158149444843"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 133.84052801132202,
            "unit": "ns",
            "range": "± 0.22501366106724396"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 145.57659888267517,
            "unit": "ns",
            "range": "± 0.08598095979727217"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2319.4134979248047,
            "unit": "ns",
            "range": "± 8.501854459989456"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2437.6469764709473,
            "unit": "ns",
            "range": "± 4.313629638261826"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 203.50924444198608,
            "unit": "ns",
            "range": "± 0.09956382384910901"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 480.65029191970825,
            "unit": "ns",
            "range": "± 0.6518912989994853"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.28097201883793,
            "unit": "ns",
            "range": "± 0.01673703486540164"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.782832741737366,
            "unit": "ns",
            "range": "± 0.07139217744350687"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 92.4532760977745,
            "unit": "ns",
            "range": "± 0.09658798605196933"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 117.17231941223145,
            "unit": "ns",
            "range": "± 0.19144876037278838"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.243175700306892,
            "unit": "ns",
            "range": "± 0.019095872333424038"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 62.19695323705673,
            "unit": "ns",
            "range": "± 0.06499718157537382"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 9.06205353140831,
            "unit": "ns",
            "range": "± 0.049481410535238884"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 28.67004156112671,
            "unit": "ns",
            "range": "± 0.05180894297213652"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 489.80543327331543,
            "unit": "ns",
            "range": "± 3.798207035607791"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 461.7311067581177,
            "unit": "ns",
            "range": "± 2.3075150084155758"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 4513.715469360352,
            "unit": "ns",
            "range": "± 66.4208844848795"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 20.93821480870247,
            "unit": "ns",
            "range": "± 0.017167452156718357"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 602.4725751876831,
            "unit": "ns",
            "range": "± 1.6248526388869675"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1017.7120304107666,
            "unit": "ns",
            "range": "± 1.692103096423499"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 368.1451184749603,
            "unit": "ns",
            "range": "± 0.5145915939035652"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 710.5310282707214,
            "unit": "ns",
            "range": "± 1.8922045056456036"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 166.60048758983612,
            "unit": "ns",
            "range": "± 0.08057786380088333"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 161.875932097435,
            "unit": "ns",
            "range": "± 0.10810085925617545"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8952977359294891,
            "unit": "ns",
            "range": "± 0.003239376043767403"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.9687351733446121,
            "unit": "ns",
            "range": "± 0.0020662206393558583"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 165.9617109298706,
            "unit": "ns",
            "range": "± 0.16307983033011245"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 264.66966104507446,
            "unit": "ns",
            "range": "± 0.34144220683519844"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3751.3990020751953,
            "unit": "ns",
            "range": "± 11.740431682889284"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4366.747058868408,
            "unit": "ns",
            "range": "± 5.330223621750281"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2038.1396446228027,
            "unit": "ns",
            "range": "± 11.166047014567033"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2013.0635223388672,
            "unit": "ns",
            "range": "± 4.528645632932425"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2035.4525661468506,
            "unit": "ns",
            "range": "± 4.116803859899315"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5154.330718994141,
            "unit": "ns",
            "range": "± 56.40015302311057"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.30626404285431,
            "unit": "ns",
            "range": "± 0.005365682528926058"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 167.16887390613556,
            "unit": "ns",
            "range": "± 0.08881941983038423"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 191.59419429302216,
            "unit": "ns",
            "range": "± 0.10293252231075231"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 164.7454752922058,
            "unit": "ns",
            "range": "± 0.20034842192514996"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 202.88563525676727,
            "unit": "ns",
            "range": "± 0.2040707677539991"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 128.86750411987305,
            "unit": "ns",
            "range": "± 0.08297479105084213"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 271.73355531692505,
            "unit": "ns",
            "range": "± 0.28791049344014896"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 441.2944552898407,
            "unit": "ns",
            "range": "± 0.5900641272879693"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 418.90127086639404,
            "unit": "ns",
            "range": "± 0.18547319810166027"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 438.75679421424866,
            "unit": "ns",
            "range": "± 0.2526580763442637"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 205.35923290252686,
            "unit": "ns",
            "range": "± 0.20194512302228593"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 212.54607963562012,
            "unit": "ns",
            "range": "± 0.16944701690645977"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 210.77274882793427,
            "unit": "ns",
            "range": "± 0.07843679383543505"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1660.854663848877,
            "unit": "ns",
            "range": "± 9.337737196420086"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 198.14908146858215,
            "unit": "ns",
            "range": "± 0.6255659249348398"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 170.83709466457367,
            "unit": "ns",
            "range": "± 0.11871254807984788"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 197.33840942382812,
            "unit": "ns",
            "range": "± 0.05897048004225687"
          }
        ]
      }
    ]
  }
}