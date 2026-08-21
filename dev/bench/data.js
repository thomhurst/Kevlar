window.BENCHMARK_DATA = {
  "lastUpdate": 1787311587505,
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
      }
    ]
  }
}