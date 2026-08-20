window.BENCHMARK_DATA = {
  "lastUpdate": 1787262043941,
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
      }
    ]
  }
}