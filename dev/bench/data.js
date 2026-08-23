window.BENCHMARK_DATA = {
  "lastUpdate": 1787513335773,
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
          "id": "86185a23074e82be7ba158136239e8ad56ae77c9",
          "message": "feat(fallback): add asynchronous notifications (#95)\n\n* feat(fallback): add async notifications\n\n* docs(fallback): clarify callback snapshotting",
          "timestamp": "2026-08-21T18:05:50+01:00",
          "tree_id": "9f7cdcb1a7beb1abd51f75af720465884b298cc2",
          "url": "https://github.com/thomhurst/Kevlar/commit/86185a23074e82be7ba158136239e8ad56ae77c9"
        },
        "date": 1787333304199,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 192.10811066627502,
            "unit": "ns",
            "range": "± 0.7782004887565557"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 283.47530698776245,
            "unit": "ns",
            "range": "± 1.4504862641946843"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5625.33927154541,
            "unit": "ns",
            "range": "± 12.178678802728426"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5559.272663116455,
            "unit": "ns",
            "range": "± 16.53908094161212"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 199.74787497520447,
            "unit": "ns",
            "range": "± 0.18911919910720737"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 228.098562002182,
            "unit": "ns",
            "range": "± 1.726634790502309"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2393.5628089904785,
            "unit": "ns",
            "range": "± 2.4955567819623132"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2402.7697143554688,
            "unit": "ns",
            "range": "± 8.177555368422425"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2417.9572582244873,
            "unit": "ns",
            "range": "± 10.05140293805779"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5965.205276489258,
            "unit": "ns",
            "range": "± 51.152426944041714"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 135.81953048706055,
            "unit": "ns",
            "range": "± 0.07983213436502842"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 137.49761486053467,
            "unit": "ns",
            "range": "± 0.17537147403670145"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2393.8694229125977,
            "unit": "ns",
            "range": "± 9.846348115059923"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2474.225643157959,
            "unit": "ns",
            "range": "± 5.794511742643085"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 208.54332745075226,
            "unit": "ns",
            "range": "± 0.3565528577662921"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 484.8193054199219,
            "unit": "ns",
            "range": "± 0.8002216201862041"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.27977254986763,
            "unit": "ns",
            "range": "± 0.013691999115341788"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.37420958280563,
            "unit": "ns",
            "range": "± 0.3923639788956246"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 96.61771535873413,
            "unit": "ns",
            "range": "± 0.20830431102232969"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 117.29833269119263,
            "unit": "ns",
            "range": "± 0.44070293059793403"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.36882209777832,
            "unit": "ns",
            "range": "± 0.014807646769598962"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.85223716497421,
            "unit": "ns",
            "range": "± 0.0450064608779847"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 9.158465325832367,
            "unit": "ns",
            "range": "± 0.05665955375992569"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.230629354715347,
            "unit": "ns",
            "range": "± 0.03258944555861504"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 505.1562991142273,
            "unit": "ns",
            "range": "± 28.86144810031643"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 500.6576089859009,
            "unit": "ns",
            "range": "± 14.730914719583744"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 5096.6662521362305,
            "unit": "ns",
            "range": "± 108.16116890791523"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 21.435629665851593,
            "unit": "ns",
            "range": "± 0.05395400853352727"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 598.6198778152466,
            "unit": "ns",
            "range": "± 0.5869897436780196"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 999.6620101928711,
            "unit": "ns",
            "range": "± 1.0973059181927407"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 377.7135651111603,
            "unit": "ns",
            "range": "± 0.520370744685752"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 694.0674314498901,
            "unit": "ns",
            "range": "± 1.2042156313906953"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 165.47210049629211,
            "unit": "ns",
            "range": "± 0.23057868405106355"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 162.8501946926117,
            "unit": "ns",
            "range": "± 0.10840007124217295"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8940684422850609,
            "unit": "ns",
            "range": "± 0.002126256642994227"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.9737365655601025,
            "unit": "ns",
            "range": "± 0.0027258401674501794"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 163.0246934890747,
            "unit": "ns",
            "range": "± 0.1019447747204382"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 288.32888412475586,
            "unit": "ns",
            "range": "± 0.22691233478278786"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3904.395709991455,
            "unit": "ns",
            "range": "± 13.636037941051743"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4466.709327697754,
            "unit": "ns",
            "range": "± 5.751216655538426"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2061.6015281677246,
            "unit": "ns",
            "range": "± 7.308367504377677"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2089.9643421173096,
            "unit": "ns",
            "range": "± 9.208163520906014"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2087.260082244873,
            "unit": "ns",
            "range": "± 3.89493130565814"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5330.281005859375,
            "unit": "ns",
            "range": "± 46.0429919022732"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.312708660960197,
            "unit": "ns",
            "range": "± 0.012562509059798764"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 165.7711559534073,
            "unit": "ns",
            "range": "± 0.3052118857219192"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 181.615300655365,
            "unit": "ns",
            "range": "± 0.21832972971264508"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 165.8324077129364,
            "unit": "ns",
            "range": "± 0.4224180388150172"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 220.8133246898651,
            "unit": "ns",
            "range": "± 0.13900606202903312"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 128.89794611930847,
            "unit": "ns",
            "range": "± 0.11353689324221095"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 272.9662733078003,
            "unit": "ns",
            "range": "± 0.26082151786682234"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 429.16957426071167,
            "unit": "ns",
            "range": "± 0.3450286068107744"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 419.8171110153198,
            "unit": "ns",
            "range": "± 0.3322030233830379"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 445.44151759147644,
            "unit": "ns",
            "range": "± 0.35313699568993245"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 201.19881296157837,
            "unit": "ns",
            "range": "± 0.7873922436535148"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 226.24056005477905,
            "unit": "ns",
            "range": "± 0.3836266744830251"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 197.43555057048798,
            "unit": "ns",
            "range": "± 0.3533084596677432"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1647.1602420806885,
            "unit": "ns",
            "range": "± 6.119907584789566"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 215.5509399175644,
            "unit": "ns",
            "range": "± 0.32526653761979635"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 170.56616187095642,
            "unit": "ns",
            "range": "± 0.05834863642461837"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 199.23575735092163,
            "unit": "ns",
            "range": "± 0.08387771326020246"
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
          "id": "08d822f18268b5eff500b6ac98af3d2dfb2400ba",
          "message": "ci(mutation): stop flaky score failures\n\nIdentical strategy sources and tests vary across the 74% threshold. Keep reports informative while preserving operational failures, lower runner concurrency, and cancel superseded runs.",
          "timestamp": "2026-08-21T19:38:20+01:00",
          "tree_id": "127bc749ae3f09e5ec50ff7d796832446b0e100d",
          "url": "https://github.com/thomhurst/Kevlar/commit/08d822f18268b5eff500b6ac98af3d2dfb2400ba"
        },
        "date": 1787338799319,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 183.24783420562744,
            "unit": "ns",
            "range": "± 0.23376289712051143"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 269.1507821083069,
            "unit": "ns",
            "range": "± 0.6212062247592637"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5643.833740234375,
            "unit": "ns",
            "range": "± 7.498065882395903"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5481.998435974121,
            "unit": "ns",
            "range": "± 14.441045017533524"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 199.58360934257507,
            "unit": "ns",
            "range": "± 0.15303845135474062"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 201.91349732875824,
            "unit": "ns",
            "range": "± 0.41047770173927756"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2427.7039794921875,
            "unit": "ns",
            "range": "± 5.3122317957068095"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2406.9759273529053,
            "unit": "ns",
            "range": "± 3.9730652209286275"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2414.987678527832,
            "unit": "ns",
            "range": "± 6.5173565724021305"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 6014.490875244141,
            "unit": "ns",
            "range": "± 101.99960399937265"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 132.2102665901184,
            "unit": "ns",
            "range": "± 0.16660202546086444"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 141.6307876110077,
            "unit": "ns",
            "range": "± 0.17912927078207722"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2396.0023708343506,
            "unit": "ns",
            "range": "± 6.423169872251969"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2496.714241027832,
            "unit": "ns",
            "range": "± 6.041716384613844"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Kevlar_PrimaryWins",
            "value": 204.4796905517578,
            "unit": "ns",
            "range": "± 0.08341944273620754"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.Polly_PrimaryWins",
            "value": 505.57549476623535,
            "unit": "ns",
            "range": "± 1.5508260334966744"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.268146008253098,
            "unit": "ns",
            "range": "± 0.018830295596698448"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.72647351026535,
            "unit": "ns",
            "range": "± 0.08214533564594637"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 14.52714079618454,
            "unit": "ns",
            "range": "± 0.01170041397620225"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 139.91875684261322,
            "unit": "ns",
            "range": "± 0.18657881244312266"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.897118538618088,
            "unit": "ns",
            "range": "± 0.02064891067474322"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 12.21139571070671,
            "unit": "ns",
            "range": "± 0.0738097967559286"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.36694160103798,
            "unit": "ns",
            "range": "± 0.017601291927425607"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.72003531455994,
            "unit": "ns",
            "range": "± 0.03671744664315248"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 8.637234143912792,
            "unit": "ns",
            "range": "± 0.007002800997024105"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 29.466486006975174,
            "unit": "ns",
            "range": "± 0.0585536279526316"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 441.3399119377136,
            "unit": "ns",
            "range": "± 7.817672473298104"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 474.21468806266785,
            "unit": "ns",
            "range": "± 4.072359192970767"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 4905.636207580566,
            "unit": "ns",
            "range": "± 134.16225490329523"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 21.023309022188187,
            "unit": "ns",
            "range": "± 0.030994701854030044"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 597.3707523345947,
            "unit": "ns",
            "range": "± 0.9382005539655298"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 1014.0639238357544,
            "unit": "ns",
            "range": "± 1.7371667064973624"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 385.14112758636475,
            "unit": "ns",
            "range": "± 0.6914445787069472"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 725.0431027412415,
            "unit": "ns",
            "range": "± 1.5212073527481285"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 164.78340590000153,
            "unit": "ns",
            "range": "± 0.3438878008438749"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 164.50176095962524,
            "unit": "ns",
            "range": "± 0.08169270671459153"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8952733725309372,
            "unit": "ns",
            "range": "± 0.00146593246631712"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.9750332646071911,
            "unit": "ns",
            "range": "± 0.010257120730403663"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 166.60717940330505,
            "unit": "ns",
            "range": "± 0.10783932836677897"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 258.3449878692627,
            "unit": "ns",
            "range": "± 0.5177777735438983"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3928.1494178771973,
            "unit": "ns",
            "range": "± 11.292497434885473"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4554.315372467041,
            "unit": "ns",
            "range": "± 4.141118420066917"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2124.794620513916,
            "unit": "ns",
            "range": "± 9.285053619410046"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2114.93017578125,
            "unit": "ns",
            "range": "± 3.9416847827822443"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2174.6475372314453,
            "unit": "ns",
            "range": "± 2.69453810533592"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5371.921844482422,
            "unit": "ns",
            "range": "± 69.61927677276414"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.29335656762123,
            "unit": "ns",
            "range": "± 0.009302113592451118"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 168.08195543289185,
            "unit": "ns",
            "range": "± 0.17865299993013858"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 188.67909407615662,
            "unit": "ns",
            "range": "± 0.21394441836951275"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 176.13613831996918,
            "unit": "ns",
            "range": "± 0.1268405491628149"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 202.04907035827637,
            "unit": "ns",
            "range": "± 0.6400340557553083"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 128.8867859840393,
            "unit": "ns",
            "range": "± 0.1212093890435555"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 274.9491386413574,
            "unit": "ns",
            "range": "± 0.3879806839860476"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 437.2034749984741,
            "unit": "ns",
            "range": "± 0.2897494238939958"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 418.464195728302,
            "unit": "ns",
            "range": "± 0.34097836963120826"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 457.6075780391693,
            "unit": "ns",
            "range": "± 0.35464003045686465"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 212.2431960105896,
            "unit": "ns",
            "range": "± 0.2734947608225348"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 223.21993565559387,
            "unit": "ns",
            "range": "± 0.35443825860082595"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 216.17449736595154,
            "unit": "ns",
            "range": "± 0.19612522736033075"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1684.4367389678955,
            "unit": "ns",
            "range": "± 6.623861888715471"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 202.7184133529663,
            "unit": "ns",
            "range": "± 0.17459831155251945"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 170.1810803413391,
            "unit": "ns",
            "range": "± 0.18328897680346237"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 209.41193175315857,
            "unit": "ns",
            "range": "± 0.3657821902018245"
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
          "id": "4b8ef8649112fa98e1d36cb7df752279d7ad0cd7",
          "message": "feat(grpc): add unary client resilience (#71)\n\n* feat(grpc): add unary client resilience\n\n* test(grpc): cover loopback hedge cleanup\n\n* fix(grpc): preserve caller cancellation token\n\n* test(docs): compile gRPC package samples\n\n* fix(grpc): release completed call lifetime\n\n* fix(grpc): dispose superseded retry calls\n\n* test(packaging): cover gRPC extension\n\n* fix(grpc): address lifecycle review\n\n* fix(grpc): preserve attempt lifecycle\n\n* perf(grpc): defer failure tracking state\n\n* fix(grpc): disambiguate reused failures\n\n* test(grpc): order reused failure selection\n\n* fix(grpc): materialize failure headers\n\n* fix(grpc): forward single-attempt headers\n\n* fix(grpc): preserve terminal call semantics\n\n* fix(grpc): stop retries at deadline\n\n* fix(grpc): preserve failure attempt identity\n\n* fix(grpc): preserve retry exception matching\n\n* test(grpc): cover invocation semantics\n\n* feat(grpc): add streaming resilience (#107)\n\n* fix(grpc): normalize deadline races\n\n* test(grpc): cover DI shield overloads\n\n* fix(grpc): preserve attempt semantics\n\nKeep public predicates on original exceptions while retaining unique per-attempt selection and metadata. Normalize streaming cancellation, avoid response-wait limiter deadlocks, and unblock netstandard writes.\n\n* fix(grpc): expose in-flight stream headers\n\nAllow header observation during an active first read and normalize deadline admission cancellation across concurrent hedges.\n\n* fix(grpc): preserve streaming progress\n\n* test(grpc): cover streaming DI overloads\n\n* test(grpc): cover streaming failure paths\n\n* fix(grpc): preserve stream selection semantics\n\nStop retries after headers commit an attempt and map duplex read cancellation back to the caller token.\n\n* fix(grpc): preserve response cancellation",
          "timestamp": "2026-08-21T21:25:14+01:00",
          "tree_id": "37d4ebc19d9360239fe5b85f5820c87feb4c748a",
          "url": "https://github.com/thomhurst/Kevlar/commit/4b8ef8649112fa98e1d36cb7df752279d7ad0cd7"
        },
        "date": 1787345604361,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 134.27454257011414,
            "unit": "ns",
            "range": "± 0.16306099786410821"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 85.32954096794128,
            "unit": "ns",
            "range": "± 0.07308386940585204"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 133.86728358268738,
            "unit": "ns",
            "range": "± 0.07498696307854041"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 18.38190460205078,
            "unit": "ns",
            "range": "± 0.019507111885040312"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 118.72143864631653,
            "unit": "ns",
            "range": "± 0.10931154686145035"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 125.00272727012634,
            "unit": "ns",
            "range": "± 0.10907164283128142"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_ClosedHappyPath",
            "value": 154.83094036579132,
            "unit": "ns",
            "range": "± 0.19695072936276567"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_ClosedHappyPath",
            "value": 279.57510638237,
            "unit": "ns",
            "range": "± 0.9157993946008003"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 216.78675365447998,
            "unit": "ns",
            "range": "± 0.24513880230940333"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 232.6473524570465,
            "unit": "ns",
            "range": "± 0.43116065801959963"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_OpenFastFail",
            "value": 5552.972091674805,
            "unit": "ns",
            "range": "± 9.902728274916434"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_OpenFastFail",
            "value": 5607.976566314697,
            "unit": "ns",
            "range": "± 18.2889579238308"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 186.60296201705933,
            "unit": "ns",
            "range": "± 0.17804441280238054"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 200.59940719604492,
            "unit": "ns",
            "range": "± 0.12878517342153242"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2376.4242782592773,
            "unit": "ns",
            "range": "± 8.335081449832094"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2371.07527923584,
            "unit": "ns",
            "range": "± 2.8543930936581186"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2343.1607971191406,
            "unit": "ns",
            "range": "± 2.9203594754671633"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5833.239784240723,
            "unit": "ns",
            "range": "± 58.886563250140206"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 132.1154327392578,
            "unit": "ns",
            "range": "± 0.12872394550721777"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 131.495614528656,
            "unit": "ns",
            "range": "± 0.31791345598476506"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2366.6270847320557,
            "unit": "ns",
            "range": "± 5.569848125331574"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2501.5607109069824,
            "unit": "ns",
            "range": "± 6.79145940432457"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.4275191016495228,
            "unit": "ns",
            "range": "± 0.002498270437035804"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 100.20224320888519,
            "unit": "ns",
            "range": "± 0.2791298708367591"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 36.26705330610275,
            "unit": "ns",
            "range": "± 0.3081858417977038"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 462.8354277610779,
            "unit": "ns",
            "range": "± 3.1708581267028646"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 22.263360485434532,
            "unit": "ns",
            "range": "± 0.22174428700135027"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 344.0941483974457,
            "unit": "ns",
            "range": "± 1.918309854617103"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 4063.4423599243164,
            "unit": "ns",
            "range": "± 8.784659823991776"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 4020.212203979492,
            "unit": "ns",
            "range": "± 5.67779765361905"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3941.7908210754395,
            "unit": "ns",
            "range": "± 29.149452079850565"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 8198.75375366211,
            "unit": "ns",
            "range": "± 250.31613225799586"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 4064.3455657958984,
            "unit": "ns",
            "range": "± 5.040779290378017"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 214.37197995185852,
            "unit": "ns",
            "range": "± 0.10032335713442077"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 480.56882190704346,
            "unit": "ns",
            "range": "± 3.8872897653180556"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2260.5736656188965,
            "unit": "ns",
            "range": "± 9.212499595645852"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 841.5596208572388,
            "unit": "ns",
            "range": "± 4.628301092408852"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 292.5146608352661,
            "unit": "ns",
            "range": "± 2.77326131275004"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.259390965104103,
            "unit": "ns",
            "range": "± 0.004892089590091994"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 57.84739464521408,
            "unit": "ns",
            "range": "± 0.09118316409750715"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 14.53496079146862,
            "unit": "ns",
            "range": "± 0.004522211709669742"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 140.58326411247253,
            "unit": "ns",
            "range": "± 0.18846663049516962"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.973661839962006,
            "unit": "ns",
            "range": "± 0.014518668256684256"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 12.02083832025528,
            "unit": "ns",
            "range": "± 0.019023677239663018"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.388647466897964,
            "unit": "ns",
            "range": "± 0.00963417957101614"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.866686940193176,
            "unit": "ns",
            "range": "± 0.10042740256156894"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 8.97496597468853,
            "unit": "ns",
            "range": "± 0.021288031197966243"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.40204468369484,
            "unit": "ns",
            "range": "± 0.023327604828983255"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 435.7376277446747,
            "unit": "ns",
            "range": "± 2.7936522912751065"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 459.44896149635315,
            "unit": "ns",
            "range": "± 4.104460399089344"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 4705.869934082031,
            "unit": "ns",
            "range": "± 72.76572187904758"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 16.88727965950966,
            "unit": "ns",
            "range": "± 0.015772020881633108"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 99.48197340965271,
            "unit": "ns",
            "range": "± 5.685696216960178"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_FiveStrategyChain",
            "value": 520.3394641876221,
            "unit": "ns",
            "range": "± 0.8474477153323001"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_FiveStrategyChain",
            "value": 986.063892364502,
            "unit": "ns",
            "range": "± 1.0773898169262726"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TimeoutRetryBreaker",
            "value": 331.9767255783081,
            "unit": "ns",
            "range": "± 0.6306850614735834"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TimeoutRetryBreaker",
            "value": 698.4683113098145,
            "unit": "ns",
            "range": "± 1.947821099491532"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_Uncontended",
            "value": 165.05064451694489,
            "unit": "ns",
            "range": "± 0.1431942385796278"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_Uncontended",
            "value": 161.20837426185608,
            "unit": "ns",
            "range": "± 0.3462634695457467"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8937212601304054,
            "unit": "ns",
            "range": "± 0.0022449655740432975"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.9734248667955399,
            "unit": "ns",
            "range": "± 0.0023270752057429527"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 163.0929948091507,
            "unit": "ns",
            "range": "± 0.149088089290192"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 256.02028584480286,
            "unit": "ns",
            "range": "± 0.19998744097065654"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3816.383834838867,
            "unit": "ns",
            "range": "± 7.768234809873555"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4603.296920776367,
            "unit": "ns",
            "range": "± 6.71422371846958"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2130.5827293395996,
            "unit": "ns",
            "range": "± 5.42056160813483"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2121.0807304382324,
            "unit": "ns",
            "range": "± 3.0887471455059865"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2057.2439193725586,
            "unit": "ns",
            "range": "± 3.4785793733976385"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5314.112335205078,
            "unit": "ns",
            "range": "± 67.9192870779117"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.277568608522415,
            "unit": "ns",
            "range": "± 0.0043199104891760405"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 170.51714754104614,
            "unit": "ns",
            "range": "± 0.09722626398028744"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 153.29456782341003,
            "unit": "ns",
            "range": "± 0.13753313226366098"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 164.7059372663498,
            "unit": "ns",
            "range": "± 0.03924066605943326"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 184.6992564201355,
            "unit": "ns",
            "range": "± 0.1192370747306066"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 130.50812244415283,
            "unit": "ns",
            "range": "± 0.06630402209029572"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 279.88455057144165,
            "unit": "ns",
            "range": "± 0.18511181929952492"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 418.7999596595764,
            "unit": "ns",
            "range": "± 0.29067165766403186"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 413.7664110660553,
            "unit": "ns",
            "range": "± 0.21843798049298271"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 414.7763156890869,
            "unit": "ns",
            "range": "± 0.5324526166787945"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 202.00378966331482,
            "unit": "ns",
            "range": "± 1.1155963745993638"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 221.8437750339508,
            "unit": "ns",
            "range": "± 0.4457041028034228"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 210.41688561439514,
            "unit": "ns",
            "range": "± 0.11091821149056368"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1653.4249534606934,
            "unit": "ns",
            "range": "± 6.590974951904792"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 203.0672676563263,
            "unit": "ns",
            "range": "± 0.10025673406419816"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 169.10895478725433,
            "unit": "ns",
            "range": "± 0.07501679569738158"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 197.79964065551758,
            "unit": "ns",
            "range": "± 0.15309129635421273"
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
          "id": "ab371564caf4801923adf92e52ceb4849dfc5558",
          "message": "feat(http): reload standard configuration (#141)\n\n* feat(http): reload standard configuration\n\nBind standard HTTP pipelines from IConfiguration and atomically publish complete replacements after valid changes. In-flight requests retain their captured snapshot; invalid reloads keep the last valid pipeline.\n\nRefs #128\n\n* test(http): wait for handler rotation\n\nPoll for the HttpClientFactory expiry callback instead of assuming it runs within 1.1 seconds under coverage instrumentation.\n\nRefs #128",
          "timestamp": "2026-08-21T23:51:57+01:00",
          "tree_id": "b1027e90e524bc62491f7d1996044a98c1c0f84e",
          "url": "https://github.com/thomhurst/Kevlar/commit/ab371564caf4801923adf92e52ceb4849dfc5558"
        },
        "date": 1787354771624,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 118.36390852928162,
            "unit": "ns",
            "range": "± 0.12710890208347053"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 76.8967416882515,
            "unit": "ns",
            "range": "± 0.11959185685462885"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 113.34773403406143,
            "unit": "ns",
            "range": "± 0.07263317464797023"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 13.255709528923035,
            "unit": "ns",
            "range": "± 0.012859611412986314"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 103.62062871456146,
            "unit": "ns",
            "range": "± 0.05960848705290987"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 105.0952742099762,
            "unit": "ns",
            "range": "± 0.07191660543389342"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4942.316600799561,
            "unit": "ns",
            "range": "± 6.017204341979489"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 5041.132911682129,
            "unit": "ns",
            "range": "± 9.024378959156623"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 206.85627341270447,
            "unit": "ns",
            "range": "± 0.15878104246022695"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 243.16173338890076,
            "unit": "ns",
            "range": "± 0.7230487909798085"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 222.3126164674759,
            "unit": "ns",
            "range": "± 0.12493871043500365"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 219.59188628196716,
            "unit": "ns",
            "range": "± 0.07967608259348206"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 143.55958652496338,
            "unit": "ns",
            "range": "± 0.30294810494915064"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 192.39421105384827,
            "unit": "ns",
            "range": "± 0.06813633793439691"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 153.86288166046143,
            "unit": "ns",
            "range": "± 0.05886224197238206"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2183.7159061431885,
            "unit": "ns",
            "range": "± 7.9461538453559095"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2140.1515464782715,
            "unit": "ns",
            "range": "± 8.534784996550817"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2144.881732940674,
            "unit": "ns",
            "range": "± 6.786368380241169"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5690.1074295043945,
            "unit": "ns",
            "range": "± 88.84444803021272"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 116.38860034942627,
            "unit": "ns",
            "range": "± 0.09331195493992035"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 114.34384596347809,
            "unit": "ns",
            "range": "± 0.057966059586241916"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2179.3596115112305,
            "unit": "ns",
            "range": "± 6.679450401490225"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2266.8066062927246,
            "unit": "ns",
            "range": "± 4.749326642204802"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.1538096405565739,
            "unit": "ns",
            "range": "± 0.0016729493063376913"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 100.0696451663971,
            "unit": "ns",
            "range": "± 0.05223013275629605"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 39.517642110586166,
            "unit": "ns",
            "range": "± 0.8473510346275392"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 530.1972370147705,
            "unit": "ns",
            "range": "± 3.0807413992188506"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 25.97161829471588,
            "unit": "ns",
            "range": "± 0.13645022682964464"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 387.60657930374146,
            "unit": "ns",
            "range": "± 1.7364890546515437"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3719.94633102417,
            "unit": "ns",
            "range": "± 7.133935410545954"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3648.0867958068848,
            "unit": "ns",
            "range": "± 11.119785959437325"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3731.168201446533,
            "unit": "ns",
            "range": "± 10.399550829586074"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 7299.589309692383,
            "unit": "ns",
            "range": "± 176.31874413591638"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3874.646827697754,
            "unit": "ns",
            "range": "± 8.791079275531951"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 205.5345150232315,
            "unit": "ns",
            "range": "± 0.08671965911224096"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 425.2054958343506,
            "unit": "ns",
            "range": "± 4.225661908440857"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2242.191562652588,
            "unit": "ns",
            "range": "± 6.762267320656442"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 879.6067428588867,
            "unit": "ns",
            "range": "± 5.274154949659107"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 308.2923638820648,
            "unit": "ns",
            "range": "± 1.7187445572773512"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 949.0794630050659,
            "unit": "ns",
            "range": "± 1.323999190182884"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3912.2733459472656,
            "unit": "ns",
            "range": "± 8.37996291674339"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3785.7971515655518,
            "unit": "ns",
            "range": "± 11.23240360914883"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 13.242156207561493,
            "unit": "ns",
            "range": "± 0.014524405849054893"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 54.46800398826599,
            "unit": "ns",
            "range": "± 0.04056425054644519"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 10.404046788811684,
            "unit": "ns",
            "range": "± 0.010774002777836128"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 100.94463843107224,
            "unit": "ns",
            "range": "± 0.07380420167023852"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 7.176867812871933,
            "unit": "ns",
            "range": "± 0.005909438170019214"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 11.298662155866623,
            "unit": "ns",
            "range": "± 0.00539016463238298"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 10.262761309742928,
            "unit": "ns",
            "range": "± 0.041810613969888735"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 55.94799739122391,
            "unit": "ns",
            "range": "± 0.04001215272321075"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.551366478204727,
            "unit": "ns",
            "range": "± 0.0024137647988650554"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 35.58930152654648,
            "unit": "ns",
            "range": "± 0.03038622267228601"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 864.6341323852539,
            "unit": "ns",
            "range": "± 22.81706071768758"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 686.7562165260315,
            "unit": "ns",
            "range": "± 31.366667158886933"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 6202.5943603515625,
            "unit": "ns",
            "range": "± 143.16886074933134"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 18.757434338331223,
            "unit": "ns",
            "range": "± 0.010455865107165927"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 171.72210001945496,
            "unit": "ns",
            "range": "± 29.352707927099104"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 356.0339756011963,
            "unit": "ns",
            "range": "± 0.19678020942536872"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 647.4646248817444,
            "unit": "ns",
            "range": "± 2.4011777225407562"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 503.6074447631836,
            "unit": "ns",
            "range": "± 0.4510092274260535"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 960.1564197540283,
            "unit": "ns",
            "range": "± 3.6669283855878563"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 142.12420773506165,
            "unit": "ns",
            "range": "± 0.042514025291920235"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 142.12641525268555,
            "unit": "ns",
            "range": "± 0.037706785976207094"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 148.60418951511383,
            "unit": "ns",
            "range": "± 0.10577230443345018"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 137.34211564064026,
            "unit": "ns",
            "range": "± 0.05539633402002057"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 164.06378376483917,
            "unit": "ns",
            "range": "± 0.1503153472267221"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8866797387599945,
            "unit": "ns",
            "range": "± 0.013324638159673007"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 1.104275368154049,
            "unit": "ns",
            "range": "± 0.001904093482248563"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 104.55182337760925,
            "unit": "ns",
            "range": "± 0.028130641162534276"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 212.46041750907898,
            "unit": "ns",
            "range": "± 0.27920153624122346"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3593.279079437256,
            "unit": "ns",
            "range": "± 19.412096125738557"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3960.188331604004,
            "unit": "ns",
            "range": "± 13.016301467672413"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1868.9412212371826,
            "unit": "ns",
            "range": "± 7.526026881620299"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1886.0607643127441,
            "unit": "ns",
            "range": "± 9.300006264973598"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1892.565902709961,
            "unit": "ns",
            "range": "± 6.351700078157926"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5075.810569763184,
            "unit": "ns",
            "range": "± 71.0066995675626"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 13.285608530044556,
            "unit": "ns",
            "range": "± 0.008968032085881482"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 108.83549582958221,
            "unit": "ns",
            "range": "± 0.04874386712838049"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 152.83147525787354,
            "unit": "ns",
            "range": "± 0.06770833444332197"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 152.7521915435791,
            "unit": "ns",
            "range": "± 0.19039958191381362"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 142.7902204990387,
            "unit": "ns",
            "range": "± 0.06023367560606967"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 96.81192183494568,
            "unit": "ns",
            "range": "± 0.05119981941984217"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 186.76360821723938,
            "unit": "ns",
            "range": "± 0.12021285433310121"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 433.9436321258545,
            "unit": "ns",
            "range": "± 0.04337299861831536"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 398.5808410644531,
            "unit": "ns",
            "range": "± 0.10797419855896144"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 359.1601529121399,
            "unit": "ns",
            "range": "± 0.2798191165474821"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 202.03729724884033,
            "unit": "ns",
            "range": "± 0.12461463301471441"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 199.2096827030182,
            "unit": "ns",
            "range": "± 0.17781105596847188"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 200.57121121883392,
            "unit": "ns",
            "range": "± 0.1497099298608712"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 2062.4023456573486,
            "unit": "ns",
            "range": "± 14.515493512558992"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 202.9103033542633,
            "unit": "ns",
            "range": "± 0.20234051385186713"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 111.53653746843338,
            "unit": "ns",
            "range": "± 0.030946381533041876"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 159.73783898353577,
            "unit": "ns",
            "range": "± 0.051548624346558075"
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
          "id": "6a2465ec42d66d7a7adf42a045d497f576f83c1f",
          "message": "chore(deps): update actions/checkout action to v7 (#149)\n\nCo-authored-by: Renovate Bot <renovate@whitesourcesoftware.com>",
          "timestamp": "2026-08-22T15:59:20+01:00",
          "tree_id": "49bf5d03ce740bd746aaffb1edadc76b59bc34cb",
          "url": "https://github.com/thomhurst/Kevlar/commit/6a2465ec42d66d7a7adf42a045d497f576f83c1f"
        },
        "date": 1787412830351,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 91.87986946105957,
            "unit": "ns",
            "range": "± 0.899839507195834"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 60.66370898485184,
            "unit": "ns",
            "range": "± 0.5272024616683526"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 84.11835241317749,
            "unit": "ns",
            "range": "± 0.8214340483858127"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 11.724740579724312,
            "unit": "ns",
            "range": "± 0.04925336746160376"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 78.24756264686584,
            "unit": "ns",
            "range": "± 0.9510128347408511"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 77.14674305915833,
            "unit": "ns",
            "range": "± 0.6133773958570274"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 3940.0672912597656,
            "unit": "ns",
            "range": "± 46.56714260192384"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4018.3406143188477,
            "unit": "ns",
            "range": "± 37.23008309046929"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 171.3764660358429,
            "unit": "ns",
            "range": "± 1.4783173829591556"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 206.71383929252625,
            "unit": "ns",
            "range": "± 2.86631924437197"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 190.01931715011597,
            "unit": "ns",
            "range": "± 1.103664508589668"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 203.0236349105835,
            "unit": "ns",
            "range": "± 3.2565807897092283"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 123.09366965293884,
            "unit": "ns",
            "range": "± 1.1394653118127664"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 163.73794770240784,
            "unit": "ns",
            "range": "± 0.8001672949211008"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 135.7550311088562,
            "unit": "ns",
            "range": "± 1.3262335605203273"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 1634.783634185791,
            "unit": "ns",
            "range": "± 37.43815390381527"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 1593.6636428833008,
            "unit": "ns",
            "range": "± 19.86120482443613"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 1660.6930141448975,
            "unit": "ns",
            "range": "± 27.83539536564574"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 4534.829097747803,
            "unit": "ns",
            "range": "± 57.690145736201934"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 89.76194566488266,
            "unit": "ns",
            "range": "± 0.960489109096602"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 97.5700215101242,
            "unit": "ns",
            "range": "± 1.3132206795889692"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1656.5255508422852,
            "unit": "ns",
            "range": "± 15.931827661826123"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1681.856258392334,
            "unit": "ns",
            "range": "± 20.739943182786547"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.229662749916315,
            "unit": "ns",
            "range": "± 0.0029115076376357786"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 80.64786612987518,
            "unit": "ns",
            "range": "± 0.29703900221923196"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 43.21370670199394,
            "unit": "ns",
            "range": "± 0.7560637155830944"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 473.1969919204712,
            "unit": "ns",
            "range": "± 3.0470580111461407"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 22.36620804667473,
            "unit": "ns",
            "range": "± 0.21145175361721114"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 370.9271492958069,
            "unit": "ns",
            "range": "± 1.5495818416119518"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 2941.9812965393066,
            "unit": "ns",
            "range": "± 24.04543084919293"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3030.923210144043,
            "unit": "ns",
            "range": "± 42.46303506505887"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3002.081771850586,
            "unit": "ns",
            "range": "± 15.026010244781023"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 6615.380645751953,
            "unit": "ns",
            "range": "± 69.92886780988867"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3055.5464782714844,
            "unit": "ns",
            "range": "± 27.134500101804825"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 170.43645024299622,
            "unit": "ns",
            "range": "± 1.5722519683939278"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 350.35361337661743,
            "unit": "ns",
            "range": "± 3.0921467772528284"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 1556.8787097930908,
            "unit": "ns",
            "range": "± 11.53482077359176"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 652.7963390350342,
            "unit": "ns",
            "range": "± 6.358801673813653"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 236.6511254310608,
            "unit": "ns",
            "range": "± 1.3183447362149359"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 696.2825908660889,
            "unit": "ns",
            "range": "± 5.550933456887897"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 2611.1254959106445,
            "unit": "ns",
            "range": "± 19.685948061840648"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 2595.68705368042,
            "unit": "ns",
            "range": "± 21.458050895971624"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 12.029753096401691,
            "unit": "ns",
            "range": "± 0.034844527368351356"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 46.536835104227066,
            "unit": "ns",
            "range": "± 0.4561877921142303"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 9.603113070130348,
            "unit": "ns",
            "range": "± 0.02264927046349584"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 75.98630303144455,
            "unit": "ns",
            "range": "± 0.22615750599490214"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 6.787763491272926,
            "unit": "ns",
            "range": "± 0.022433621119739785"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 7.049951478838921,
            "unit": "ns",
            "range": "± 0.016590292279015303"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 9.561735436320305,
            "unit": "ns",
            "range": "± 0.03823961455363243"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 53.28199175000191,
            "unit": "ns",
            "range": "± 0.12759661606839617"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 5.932799011468887,
            "unit": "ns",
            "range": "± 0.025610059553416276"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 37.225668370723724,
            "unit": "ns",
            "range": "± 0.10546994842714073"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 790.8932275772095,
            "unit": "ns",
            "range": "± 25.315514483825407"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 935.8780064582825,
            "unit": "ns",
            "range": "± 34.1638456453524"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 5945.711013793945,
            "unit": "ns",
            "range": "± 163.5212198211775"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 17.43434339761734,
            "unit": "ns",
            "range": "± 0.05103716947212403"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 214.24425554275513,
            "unit": "ns",
            "range": "± 35.203072357214104"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 281.90831780433655,
            "unit": "ns",
            "range": "± 0.7591740860275817"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 493.76485776901245,
            "unit": "ns",
            "range": "± 0.9730125225092535"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 413.2226560115814,
            "unit": "ns",
            "range": "± 0.6698645178044432"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 702.1637535095215,
            "unit": "ns",
            "range": "± 2.5550783983646292"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 125.57426118850708,
            "unit": "ns",
            "range": "± 0.6394038516008059"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 120.84977889060974,
            "unit": "ns",
            "range": "± 0.6922747391558531"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 125.12839078903198,
            "unit": "ns",
            "range": "± 0.2920982145667096"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 119.45054984092712,
            "unit": "ns",
            "range": "± 0.6594749066342429"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 145.19314408302307,
            "unit": "ns",
            "range": "± 0.72949996595436"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.6675173677504063,
            "unit": "ns",
            "range": "± 0.028989079374995414"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.7608033381402493,
            "unit": "ns",
            "range": "± 0.006824938896058668"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 81.51877808570862,
            "unit": "ns",
            "range": "± 0.5921742064882118"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 189.10786128044128,
            "unit": "ns",
            "range": "± 1.2817836359990693"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 2711.1331100463867,
            "unit": "ns",
            "range": "± 10.405857420213652"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3212.351104736328,
            "unit": "ns",
            "range": "± 47.89155145847069"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1350.9199199676514,
            "unit": "ns",
            "range": "± 21.002307600666363"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1429.5727787017822,
            "unit": "ns",
            "range": "± 21.916043159446094"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1380.1834316253662,
            "unit": "ns",
            "range": "± 27.697834396633965"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 4237.424835205078,
            "unit": "ns",
            "range": "± 76.73284771364817"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 12.060743048787117,
            "unit": "ns",
            "range": "± 0.05113466961289559"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 78.61927968263626,
            "unit": "ns",
            "range": "± 0.22929677093654025"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 121.59141933917999,
            "unit": "ns",
            "range": "± 0.32349057672876186"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 123.45362138748169,
            "unit": "ns",
            "range": "± 1.9317335616162024"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 119.99126541614532,
            "unit": "ns",
            "range": "± 1.3465262192431502"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 89.3921394944191,
            "unit": "ns",
            "range": "± 0.5799048209796791"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 162.57327783107758,
            "unit": "ns",
            "range": "± 0.5228475202988005"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 349.3206672668457,
            "unit": "ns",
            "range": "± 2.2756998467696374"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 340.82216787338257,
            "unit": "ns",
            "range": "± 3.8061556464894535"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 333.9055595397949,
            "unit": "ns",
            "range": "± 1.3671305494481973"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 155.23504316806793,
            "unit": "ns",
            "range": "± 0.7716934497342195"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 160.12268781661987,
            "unit": "ns",
            "range": "± 0.7502632521719346"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 158.01904034614563,
            "unit": "ns",
            "range": "± 0.6585557212314487"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1897.881103515625,
            "unit": "ns",
            "range": "± 17.587207805645654"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 154.63725566864014,
            "unit": "ns",
            "range": "± 0.8423100296530937"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 86.27571856975555,
            "unit": "ns",
            "range": "± 0.33538067156679413"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 147.70996606349945,
            "unit": "ns",
            "range": "± 0.9244280470922653"
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
          "id": "743ad78541a86a4d513121aaad665eec7060a39c",
          "message": "chore(deps): update actions/download-artifact action to v8 (#151)\n\nCo-authored-by: Renovate Bot <renovate@whitesourcesoftware.com>",
          "timestamp": "2026-08-22T17:08:00+01:00",
          "tree_id": "57538f539e156d32a7678134d424d9608854c328",
          "url": "https://github.com/thomhurst/Kevlar/commit/743ad78541a86a4d513121aaad665eec7060a39c"
        },
        "date": 1787416973434,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 116.87001782655716,
            "unit": "ns",
            "range": "± 0.16140116381832245"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 77.28182816505432,
            "unit": "ns",
            "range": "± 0.12545554671633483"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 118.00793129205704,
            "unit": "ns",
            "range": "± 0.2343400342393041"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 13.248131766915321,
            "unit": "ns",
            "range": "± 0.01608042444913043"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 107.04247522354126,
            "unit": "ns",
            "range": "± 0.05555114624962867"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 101.97181624174118,
            "unit": "ns",
            "range": "± 0.23570034592765884"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4960.98250579834,
            "unit": "ns",
            "range": "± 6.100560114973862"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4941.958709716797,
            "unit": "ns",
            "range": "± 16.957566032429416"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 203.60390603542328,
            "unit": "ns",
            "range": "± 0.08539206621141379"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 237.63683462142944,
            "unit": "ns",
            "range": "± 0.6513309713427974"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 225.74296879768372,
            "unit": "ns",
            "range": "± 2.246968413617901"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 223.05237412452698,
            "unit": "ns",
            "range": "± 0.1006896089970278"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 142.02258825302124,
            "unit": "ns",
            "range": "± 0.2215840676469021"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 186.49713516235352,
            "unit": "ns",
            "range": "± 0.20002141659650102"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 144.66437554359436,
            "unit": "ns",
            "range": "± 0.1004825337501625"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2159.8384857177734,
            "unit": "ns",
            "range": "± 9.750279333384091"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2149.9799194335938,
            "unit": "ns",
            "range": "± 7.4641710624749305"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2141.5148162841797,
            "unit": "ns",
            "range": "± 9.749040137318383"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5740.966995239258,
            "unit": "ns",
            "range": "± 79.94442001681277"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 116.3100363612175,
            "unit": "ns",
            "range": "± 0.036330466389164"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 117.06005501747131,
            "unit": "ns",
            "range": "± 0.09033756095622066"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2137.9195251464844,
            "unit": "ns",
            "range": "± 4.15826185796101"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2211.7463760375977,
            "unit": "ns",
            "range": "± 10.085532532446258"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.1538057811558247,
            "unit": "ns",
            "range": "± 0.0008747617812820398"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 96.23772484064102,
            "unit": "ns",
            "range": "± 0.11880177480108225"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 39.12179175019264,
            "unit": "ns",
            "range": "± 0.7903993016743006"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 508.69497776031494,
            "unit": "ns",
            "range": "± 4.535321142937747"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 26.152865886688232,
            "unit": "ns",
            "range": "± 0.3124049920653585"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 388.8779625892639,
            "unit": "ns",
            "range": "± 2.0269465753383487"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3673.9733276367188,
            "unit": "ns",
            "range": "± 4.844733173994168"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3769.425392150879,
            "unit": "ns",
            "range": "± 8.350597791143155"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3816.948402404785,
            "unit": "ns",
            "range": "± 6.465259799056647"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 8116.463287353516,
            "unit": "ns",
            "range": "± 222.4257180893749"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3825.470054626465,
            "unit": "ns",
            "range": "± 13.115772555005675"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 214.1078863143921,
            "unit": "ns",
            "range": "± 0.10245521721644965"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 429.4613995552063,
            "unit": "ns",
            "range": "± 0.380192455305304"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2241.7915058135986,
            "unit": "ns",
            "range": "± 4.8938360786246635"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 862.2376594543457,
            "unit": "ns",
            "range": "± 1.840058125679567"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 309.6998176574707,
            "unit": "ns",
            "range": "± 2.2257849769966622"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 1021.6527900695801,
            "unit": "ns",
            "range": "± 1.8056102735947557"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3775.536445617676,
            "unit": "ns",
            "range": "± 4.6432301496985655"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3808.1788330078125,
            "unit": "ns",
            "range": "± 11.172392089653618"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 13.240024149417877,
            "unit": "ns",
            "range": "± 0.008246560350795849"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 56.73547840118408,
            "unit": "ns",
            "range": "± 0.027807576160576233"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 11.268834307789803,
            "unit": "ns",
            "range": "± 0.005064245487513958"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 103.7151307463646,
            "unit": "ns",
            "range": "± 0.03281424067650433"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 7.154467090964317,
            "unit": "ns",
            "range": "± 0.006185401763690971"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 10.769518002867699,
            "unit": "ns",
            "range": "± 0.010256359760837493"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 11.195793971419334,
            "unit": "ns",
            "range": "± 0.039915468194442284"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 69.5791729092598,
            "unit": "ns",
            "range": "± 0.0627868294066918"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 8.266905270516872,
            "unit": "ns",
            "range": "± 0.0019646962383036075"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 36.23726776242256,
            "unit": "ns",
            "range": "± 0.12887925594203142"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 893.7701244354248,
            "unit": "ns",
            "range": "± 10.292627366081126"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 976.1322193145752,
            "unit": "ns",
            "range": "± 32.73593656955109"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 6288.880645751953,
            "unit": "ns",
            "range": "± 186.6859810002701"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 19.36000856757164,
            "unit": "ns",
            "range": "± 0.011707933965777623"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 238.90579903125763,
            "unit": "ns",
            "range": "± 55.63990690610482"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 356.5602955818176,
            "unit": "ns",
            "range": "± 0.08279715472228241"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 619.8577308654785,
            "unit": "ns",
            "range": "± 1.376713995049369"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 490.068416595459,
            "unit": "ns",
            "range": "± 0.2381431286203128"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 990.9233303070068,
            "unit": "ns",
            "range": "± 3.754614952994432"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 143.8150519132614,
            "unit": "ns",
            "range": "± 0.07950553742458857"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 146.2506172657013,
            "unit": "ns",
            "range": "± 0.061707160789821594"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 139.57079756259918,
            "unit": "ns",
            "range": "± 0.43988815142205967"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 139.00308668613434,
            "unit": "ns",
            "range": "± 0.05843911755428296"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 190.3321591615677,
            "unit": "ns",
            "range": "± 0.2514037213138517"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.9650319442152977,
            "unit": "ns",
            "range": "± 0.011275501448098135"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 1.2527630552649498,
            "unit": "ns",
            "range": "± 0.002850007286319489"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 104.36405730247498,
            "unit": "ns",
            "range": "± 0.026062235377978287"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 205.80214762687683,
            "unit": "ns",
            "range": "± 0.19004420010520412"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3611.5146293640137,
            "unit": "ns",
            "range": "± 17.798171995907392"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3978.61612701416,
            "unit": "ns",
            "range": "± 9.403058007599892"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1880.1936626434326,
            "unit": "ns",
            "range": "± 6.699243931502267"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1897.8894844055176,
            "unit": "ns",
            "range": "± 2.236694175047818"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1886.6539764404297,
            "unit": "ns",
            "range": "± 7.22590691379487"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5211.737129211426,
            "unit": "ns",
            "range": "± 105.17538891006478"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 13.472932666540146,
            "unit": "ns",
            "range": "± 0.021757538865529873"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 101.90512216091156,
            "unit": "ns",
            "range": "± 0.0350333250378262"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 159.79292953014374,
            "unit": "ns",
            "range": "± 0.052123787870945966"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 140.99601542949677,
            "unit": "ns",
            "range": "± 0.1645415272086848"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 147.56164979934692,
            "unit": "ns",
            "range": "± 0.1663802958496085"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 97.75519353151321,
            "unit": "ns",
            "range": "± 0.05839178042215338"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 186.75228548049927,
            "unit": "ns",
            "range": "± 0.1665958382223225"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 436.1808454990387,
            "unit": "ns",
            "range": "± 0.353447108588192"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 375.56300592422485,
            "unit": "ns",
            "range": "± 0.21729996005605084"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 361.45356011390686,
            "unit": "ns",
            "range": "± 0.16930360272180986"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 206.0482453107834,
            "unit": "ns",
            "range": "± 0.04554334908251878"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 208.670827627182,
            "unit": "ns",
            "range": "± 0.0754059982374798"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 215.6586263179779,
            "unit": "ns",
            "range": "± 0.2572405130907167"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 2099.6163177490234,
            "unit": "ns",
            "range": "± 18.351406116857518"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 202.91538727283478,
            "unit": "ns",
            "range": "± 0.10672762993932415"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 103.71837991476059,
            "unit": "ns",
            "range": "± 0.056076032916910205"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 160.18214440345764,
            "unit": "ns",
            "range": "± 0.10526683330595345"
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
          "id": "d72d915aac468e2c1222c314fe82809db9c70500",
          "message": "chore(deps): update actions/setup-dotnet action to v6 (#152)\n\nCo-authored-by: Renovate Bot <renovate@whitesourcesoftware.com>",
          "timestamp": "2026-08-22T18:40:58+01:00",
          "tree_id": "4cb391d5b651bb3a9c3d1cb2955ff1d0d6187591",
          "url": "https://github.com/thomhurst/Kevlar/commit/d72d915aac468e2c1222c314fe82809db9c70500"
        },
        "date": 1787422317035,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 142.03491032123566,
            "unit": "ns",
            "range": "± 0.21139101297303325"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 82.54526168107986,
            "unit": "ns",
            "range": "± 0.07080012789433507"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 133.54159259796143,
            "unit": "ns",
            "range": "± 0.19010817590293183"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 18.616395190358162,
            "unit": "ns",
            "range": "± 0.013189131803333143"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 117.71675252914429,
            "unit": "ns",
            "range": "± 0.19182946054978048"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 124.0839855670929,
            "unit": "ns",
            "range": "± 0.6373684016496202"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 5323.57918548584,
            "unit": "ns",
            "range": "± 14.963901793883805"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 5260.034858703613,
            "unit": "ns",
            "range": "± 14.5697892545057"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 231.32952213287354,
            "unit": "ns",
            "range": "± 0.4099061968277974"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 286.7669475078583,
            "unit": "ns",
            "range": "± 0.9142597558889438"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 240.79298734664917,
            "unit": "ns",
            "range": "± 0.20854918703336633"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 242.75313234329224,
            "unit": "ns",
            "range": "± 0.3490220744152853"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 160.53583145141602,
            "unit": "ns",
            "range": "± 0.2303153756690662"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 210.17342019081116,
            "unit": "ns",
            "range": "± 0.13545235631883082"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 157.38562500476837,
            "unit": "ns",
            "range": "± 0.12965096195678028"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2333.764907836914,
            "unit": "ns",
            "range": "± 3.847360458041165"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2333.073440551758,
            "unit": "ns",
            "range": "± 3.257803920792235"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2347.9721450805664,
            "unit": "ns",
            "range": "± 5.499855564391071"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5447.180770874023,
            "unit": "ns",
            "range": "± 37.66572224642452"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 140.96627235412598,
            "unit": "ns",
            "range": "± 0.13625745107170606"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 142.64278030395508,
            "unit": "ns",
            "range": "± 0.154609301430884"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2325.947196960449,
            "unit": "ns",
            "range": "± 3.155683722971223"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2383.214698791504,
            "unit": "ns",
            "range": "± 1.7843647620687502"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.7583353035151958,
            "unit": "ns",
            "range": "± 0.0010310898187130132"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 108.79172277450562,
            "unit": "ns",
            "range": "± 0.3041609836797228"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 37.966725528240204,
            "unit": "ns",
            "range": "± 0.14275884646153858"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 453.6896014213562,
            "unit": "ns",
            "range": "± 7.085263173561964"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 23.242091566324234,
            "unit": "ns",
            "range": "± 0.11122794349095688"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 341.0508966445923,
            "unit": "ns",
            "range": "± 4.453632204475574"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3876.8842391967773,
            "unit": "ns",
            "range": "± 18.057316221140006"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 4026.5486602783203,
            "unit": "ns",
            "range": "± 8.616638913190323"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3895.483039855957,
            "unit": "ns",
            "range": "± 16.27037951052361"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 8205.795944213867,
            "unit": "ns",
            "range": "± 110.93023360671047"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3978.4071083068848,
            "unit": "ns",
            "range": "± 8.96647509364259"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 224.323179602623,
            "unit": "ns",
            "range": "± 0.1502526160151262"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 465.8323082923889,
            "unit": "ns",
            "range": "± 0.6018935945907883"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2370.771017074585,
            "unit": "ns",
            "range": "± 21.607042727699053"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 853.995135307312,
            "unit": "ns",
            "range": "± 10.614859137180055"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 268.1746783256531,
            "unit": "ns",
            "range": "± 1.0451816212805998"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 932.8189544677734,
            "unit": "ns",
            "range": "± 1.4283350350921844"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 4132.339286804199,
            "unit": "ns",
            "range": "± 17.247615268214982"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 4085.620002746582,
            "unit": "ns",
            "range": "± 19.55999907438541"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.779721334576607,
            "unit": "ns",
            "range": "± 0.05882410504800898"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 62.60762107372284,
            "unit": "ns",
            "range": "± 0.04804045435715876"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 14.3309126496315,
            "unit": "ns",
            "range": "± 0.004364897717427671"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 120.97649908065796,
            "unit": "ns",
            "range": "± 0.6129181739842163"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 9.454363949596882,
            "unit": "ns",
            "range": "± 0.015452670496152485"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 11.7520182877779,
            "unit": "ns",
            "range": "± 0.016213483883375325"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 13.646192952990532,
            "unit": "ns",
            "range": "± 0.005454043489252941"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 63.3303005695343,
            "unit": "ns",
            "range": "± 0.025251965657702408"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 11.698476284742355,
            "unit": "ns",
            "range": "± 0.013930022972041206"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 31.140052646398544,
            "unit": "ns",
            "range": "± 0.01655146586811132"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 364.94813418388367,
            "unit": "ns",
            "range": "± 3.139272953983963"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 442.306583404541,
            "unit": "ns",
            "range": "± 4.75530378765423"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 4733.239456176758,
            "unit": "ns",
            "range": "± 192.11746374724834"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 17.087018221616745,
            "unit": "ns",
            "range": "± 0.011728921110917744"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 104.354900598526,
            "unit": "ns",
            "range": "± 4.54893971816584"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 379.62232542037964,
            "unit": "ns",
            "range": "± 0.2820726372415951"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 725.9300274848938,
            "unit": "ns",
            "range": "± 3.570716934541482"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 519.7619886398315,
            "unit": "ns",
            "range": "± 0.5863415725094064"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 1007.623613357544,
            "unit": "ns",
            "range": "± 1.0271257337745645"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 171.8396337032318,
            "unit": "ns",
            "range": "± 0.13569378854605002"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 152.24778819084167,
            "unit": "ns",
            "range": "± 0.2502896349762797"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 169.26007533073425,
            "unit": "ns",
            "range": "± 0.1402225833561896"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 150.85132265090942,
            "unit": "ns",
            "range": "± 0.22553129935498775"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 181.49834084510803,
            "unit": "ns",
            "range": "± 0.14031243778421013"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.33371221274137497,
            "unit": "ns",
            "range": "± 0.009224491326998352"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.41152916848659515,
            "unit": "ns",
            "range": "± 0.19002063953583892"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 125.95198798179626,
            "unit": "ns",
            "range": "± 0.26242261525619137"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 256.68868017196655,
            "unit": "ns",
            "range": "± 0.5759529226312168"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3899.6356353759766,
            "unit": "ns",
            "range": "± 5.5522676587072155"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4426.956226348877,
            "unit": "ns",
            "range": "± 5.543542464108965"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2132.3466300964355,
            "unit": "ns",
            "range": "± 7.750827648703868"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2092.2400283813477,
            "unit": "ns",
            "range": "± 1.300310596742869"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2067.7836990356445,
            "unit": "ns",
            "range": "± 5.743567536934904"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 4976.341697692871,
            "unit": "ns",
            "range": "± 38.365467573867626"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.63705277442932,
            "unit": "ns",
            "range": "± 0.02048027299819798"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 124.1044569015503,
            "unit": "ns",
            "range": "± 0.1537766784616652"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 169.45197200775146,
            "unit": "ns",
            "range": "± 0.19073195551120078"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 170.40159058570862,
            "unit": "ns",
            "range": "± 0.07652916299839202"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 159.86821603775024,
            "unit": "ns",
            "range": "± 0.14247164068595636"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 135.72329342365265,
            "unit": "ns",
            "range": "± 0.1456633576694726"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 236.11749935150146,
            "unit": "ns",
            "range": "± 0.30428606998628055"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 441.7287845611572,
            "unit": "ns",
            "range": "± 0.2185045443217885"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 461.50498962402344,
            "unit": "ns",
            "range": "± 0.8404539058364592"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 444.3070755004883,
            "unit": "ns",
            "range": "± 2.791000832186673"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 205.23022890090942,
            "unit": "ns",
            "range": "± 0.09954072868454211"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 206.54780316352844,
            "unit": "ns",
            "range": "± 0.5141622488726189"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 211.94657349586487,
            "unit": "ns",
            "range": "± 0.15895148520790806"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1739.7796936035156,
            "unit": "ns",
            "range": "± 13.423852633330666"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 204.64732253551483,
            "unit": "ns",
            "range": "± 0.16881724437792198"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 125.74511790275574,
            "unit": "ns",
            "range": "± 0.12267521685230282"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 213.85159850120544,
            "unit": "ns",
            "range": "± 0.1257601034972539"
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
          "id": "4e2428440be6adfa9e46cead285801b147d7e6b9",
          "message": "chore(deps): update actions/upload-artifact action to v7 (#154)\n\nCo-authored-by: Renovate Bot <renovate@whitesourcesoftware.com>",
          "timestamp": "2026-08-22T19:54:37+01:00",
          "tree_id": "133db0b3a1bfb150825105ad3ecf6f01dc120b7d",
          "url": "https://github.com/thomhurst/Kevlar/commit/4e2428440be6adfa9e46cead285801b147d7e6b9"
        },
        "date": 1787427039813,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 97.97970652580261,
            "unit": "ns",
            "range": "± 1.6924947192629665"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 66.10647535324097,
            "unit": "ns",
            "range": "± 0.19361565066787942"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 88.86406242847443,
            "unit": "ns",
            "range": "± 0.5227946479883221"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 11.78808456659317,
            "unit": "ns",
            "range": "± 0.043952329616344286"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 85.04333597421646,
            "unit": "ns",
            "range": "± 0.4170516545319258"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 86.77199596166611,
            "unit": "ns",
            "range": "± 1.3166325547670708"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4345.542724609375,
            "unit": "ns",
            "range": "± 56.15342048447947"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4440.350681304932,
            "unit": "ns",
            "range": "± 40.29875299890289"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 198.19886565208435,
            "unit": "ns",
            "range": "± 3.5236226010934364"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 222.90091252326965,
            "unit": "ns",
            "range": "± 2.441538739270045"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 211.6208369731903,
            "unit": "ns",
            "range": "± 1.7085399262213907"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 203.57556581497192,
            "unit": "ns",
            "range": "± 2.3884221127743936"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 136.31535077095032,
            "unit": "ns",
            "range": "± 0.9952889188621371"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 179.98504889011383,
            "unit": "ns",
            "range": "± 2.045147849906457"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 134.98448038101196,
            "unit": "ns",
            "range": "± 2.5493551146617044"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 1884.277515411377,
            "unit": "ns",
            "range": "± 16.538094630075264"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 1903.9448013305664,
            "unit": "ns",
            "range": "± 30.489522323260527"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 1885.923267364502,
            "unit": "ns",
            "range": "± 47.575559073780795"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5216.495307922363,
            "unit": "ns",
            "range": "± 141.94115818663226"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 101.71094065904617,
            "unit": "ns",
            "range": "± 2.4818134248180392"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 106.23309248685837,
            "unit": "ns",
            "range": "± 2.1765678102117443"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1887.9438438415527,
            "unit": "ns",
            "range": "± 18.604584037562393"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2003.8292236328125,
            "unit": "ns",
            "range": "± 19.9457368955483"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.1592931058257818,
            "unit": "ns",
            "range": "± 0.06435203235408846"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 87.06151175498962,
            "unit": "ns",
            "range": "± 0.7417946122298341"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 34.09928834438324,
            "unit": "ns",
            "range": "± 1.2288452731178126"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 491.90862464904785,
            "unit": "ns",
            "range": "± 6.975076698309458"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 22.134476453065872,
            "unit": "ns",
            "range": "± 0.5565560375536798"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 332.99423265457153,
            "unit": "ns",
            "range": "± 5.416923120381794"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3263.442626953125,
            "unit": "ns",
            "range": "± 26.631822200097158"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3334.071132659912,
            "unit": "ns",
            "range": "± 31.402796142918998"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3275.5389881134033,
            "unit": "ns",
            "range": "± 16.580736076130883"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 7319.390609741211,
            "unit": "ns",
            "range": "± 51.21904736967063"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3321.256790161133,
            "unit": "ns",
            "range": "± 56.996492825646435"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 186.42502093315125,
            "unit": "ns",
            "range": "± 1.5760448999112981"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 373.0517044067383,
            "unit": "ns",
            "range": "± 3.9974931327361403"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 1770.8064880371094,
            "unit": "ns",
            "range": "± 32.58896688900263"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 660.4171853065491,
            "unit": "ns",
            "range": "± 7.103315653524151"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 238.83766341209412,
            "unit": "ns",
            "range": "± 6.909461401418688"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 757.9380207061768,
            "unit": "ns",
            "range": "± 8.7658573415228"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 2891.2224254608154,
            "unit": "ns",
            "range": "± 29.287919175834435"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3004.9559059143066,
            "unit": "ns",
            "range": "± 22.944335174232556"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 11.627345360815525,
            "unit": "ns",
            "range": "± 0.11758320781980686"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 52.840161979198456,
            "unit": "ns",
            "range": "± 0.47056030732941573"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 12.413495734333992,
            "unit": "ns",
            "range": "± 0.164877259277586"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 92.94697231054306,
            "unit": "ns",
            "range": "± 1.5144170135755077"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 6.603558510541916,
            "unit": "ns",
            "range": "± 0.11369634906330411"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 6.299013063311577,
            "unit": "ns",
            "range": "± 0.0745750695940081"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 8.254245221614838,
            "unit": "ns",
            "range": "± 0.07374434086213892"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 52.83683857321739,
            "unit": "ns",
            "range": "± 0.3324305350411362"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 5.420400038361549,
            "unit": "ns",
            "range": "± 0.06466944188890682"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 34.782976537942886,
            "unit": "ns",
            "range": "± 0.18915803902390466"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 803.1454544067383,
            "unit": "ns",
            "range": "± 29.31423759535056"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 989.4141149520874,
            "unit": "ns",
            "range": "± 14.751600372547866"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 5815.413711547852,
            "unit": "ns",
            "range": "± 92.0568244218206"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 18.597231909632683,
            "unit": "ns",
            "range": "± 0.2811502415043131"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 196.68301284313202,
            "unit": "ns",
            "range": "± 30.179997492640027"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 319.1864995956421,
            "unit": "ns",
            "range": "± 5.069722439503243"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 561.1910791397095,
            "unit": "ns",
            "range": "± 8.03682672954385"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 462.95060634613037,
            "unit": "ns",
            "range": "± 5.728796544533724"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 750.153290271759,
            "unit": "ns",
            "range": "± 16.00581713590258"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 132.42070031166077,
            "unit": "ns",
            "range": "± 1.5744852889308432"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 128.0997495651245,
            "unit": "ns",
            "range": "± 2.237317202999553"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 135.10516738891602,
            "unit": "ns",
            "range": "± 2.583206992054072"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 126.95319056510925,
            "unit": "ns",
            "range": "± 1.378232512948865"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 148.7208330631256,
            "unit": "ns",
            "range": "± 0.9452314484405004"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8822378516197205,
            "unit": "ns",
            "range": "± 0.050237419633516384"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 1.0305863581597805,
            "unit": "ns",
            "range": "± 0.05193601273079636"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 89.86201882362366,
            "unit": "ns",
            "range": "± 0.4617484352500202"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 192.16437911987305,
            "unit": "ns",
            "range": "± 1.3380903580480181"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3015.5845832824707,
            "unit": "ns",
            "range": "± 33.34497350138799"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3491.361581802368,
            "unit": "ns",
            "range": "± 57.836490028322714"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1553.4697933197021,
            "unit": "ns",
            "range": "± 12.46203630044018"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1583.8389434814453,
            "unit": "ns",
            "range": "± 38.08622045639344"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1581.8492097854614,
            "unit": "ns",
            "range": "± 24.14971561916008"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 4675.746505737305,
            "unit": "ns",
            "range": "± 73.87820322824881"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 11.981260411441326,
            "unit": "ns",
            "range": "± 0.12169125587500626"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 86.18365430831909,
            "unit": "ns",
            "range": "± 0.9295615625828368"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 136.47171819210052,
            "unit": "ns",
            "range": "± 1.26149582612315"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 138.94103169441223,
            "unit": "ns",
            "range": "± 2.1229325632094795"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 132.27993440628052,
            "unit": "ns",
            "range": "± 1.742049332810206"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 93.96672284603119,
            "unit": "ns",
            "range": "± 0.6985689837959458"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 181.94219875335693,
            "unit": "ns",
            "range": "± 1.667422190869819"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 384.5004725456238,
            "unit": "ns",
            "range": "± 3.6118547528111775"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 377.2766647338867,
            "unit": "ns",
            "range": "± 4.349497449759264"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 343.6839234828949,
            "unit": "ns",
            "range": "± 5.758873365509026"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 172.72001338005066,
            "unit": "ns",
            "range": "± 2.9901684181414856"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 171.91314125061035,
            "unit": "ns",
            "range": "± 1.5106821022010906"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 170.77819800376892,
            "unit": "ns",
            "range": "± 1.2692282656438076"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 2021.8997268676758,
            "unit": "ns",
            "range": "± 20.138712258011836"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 179.97567284107208,
            "unit": "ns",
            "range": "± 3.426361505578299"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 90.38884538412094,
            "unit": "ns",
            "range": "± 0.6706546442366083"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 157.80534327030182,
            "unit": "ns",
            "range": "± 2.4804247092334584"
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
          "id": "86690c4afd8fc8934a9c703182c6f6775a3ccc33",
          "message": "feat(shield): add WhenAnyError reset (#167)\n\n* feat(shield): add default handling reset\n\nRefs #158\n\n* fix(shield): preserve reset through composition\n\nRefs #158\n\n* fix(analyzers): recognize reset boundary\n\nTreat WhenAnyError as a handling-clause boundary so KEV003 does not cross the reset.\n\nRefs #158\n\n* fix(analyzers): preserve default reset hazards\n\n* fix(analyzers): scan before default reset\n\n* fix(analyzers): scan all default segments",
          "timestamp": "2026-08-23T15:49:07+01:00",
          "tree_id": "c635ba8fbd9ac33ff2e12d44c31c1923644b235e",
          "url": "https://github.com/thomhurst/Kevlar/commit/86690c4afd8fc8934a9c703182c6f6775a3ccc33"
        },
        "date": 1787498444075,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 136.1060492992401,
            "unit": "ns",
            "range": "± 0.12044725836882673"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 90.24682760238647,
            "unit": "ns",
            "range": "± 0.1868205278933365"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 132.70392382144928,
            "unit": "ns",
            "range": "± 0.3011586937562301"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 18.288647904992104,
            "unit": "ns",
            "range": "± 0.011348854035249623"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 127.27898859977722,
            "unit": "ns",
            "range": "± 0.12592622959188826"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 122.17423033714294,
            "unit": "ns",
            "range": "± 0.11995425361320168"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 5416.840766906738,
            "unit": "ns",
            "range": "± 10.229972400569025"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 5598.949661254883,
            "unit": "ns",
            "range": "± 10.140017435506088"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 216.96126425266266,
            "unit": "ns",
            "range": "± 0.1773604546199936"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 269.6820752620697,
            "unit": "ns",
            "range": "± 0.7619964330604627"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 235.5902066230774,
            "unit": "ns",
            "range": "± 0.35347273370405863"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 233.05077576637268,
            "unit": "ns",
            "range": "± 0.3186028432086779"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 154.2221418619156,
            "unit": "ns",
            "range": "± 0.23259679520989981"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 213.24791514873505,
            "unit": "ns",
            "range": "± 0.2579054764857448"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 158.69161081314087,
            "unit": "ns",
            "range": "± 0.10227256188268892"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2454.5256385803223,
            "unit": "ns",
            "range": "± 5.907289585862496"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2416.9185009002686,
            "unit": "ns",
            "range": "± 2.726804598814211"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2474.385021209717,
            "unit": "ns",
            "range": "± 10.766840341791008"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5955.298629760742,
            "unit": "ns",
            "range": "± 83.37348505056258"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 138.33266949653625,
            "unit": "ns",
            "range": "± 0.13248071816231238"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 137.69345712661743,
            "unit": "ns",
            "range": "± 0.7105262819692597"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2487.628837585449,
            "unit": "ns",
            "range": "± 5.657628259981016"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2504.614061355591,
            "unit": "ns",
            "range": "± 10.65966548328954"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.4313160590827465,
            "unit": "ns",
            "range": "± 0.0011987646513664477"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 103.3037737607956,
            "unit": "ns",
            "range": "± 0.24803034585226316"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 36.29466447234154,
            "unit": "ns",
            "range": "± 0.6003478520939184"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 506.6859073638916,
            "unit": "ns",
            "range": "± 7.3647644145304145"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 22.536485359072685,
            "unit": "ns",
            "range": "± 0.18001491303729464"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 343.69813203811646,
            "unit": "ns",
            "range": "± 1.7642697751341678"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 4039.6862297058105,
            "unit": "ns",
            "range": "± 13.552442980396442"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 4076.3755264282227,
            "unit": "ns",
            "range": "± 21.14676698030747"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 4033.838165283203,
            "unit": "ns",
            "range": "± 8.29803884475667"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 8831.591705322266,
            "unit": "ns",
            "range": "± 275.5620141887403"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 4046.539794921875,
            "unit": "ns",
            "range": "± 7.625711343858768"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 210.94853019714355,
            "unit": "ns",
            "range": "± 0.2749676407692363"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 484.9698715209961,
            "unit": "ns",
            "range": "± 0.8067400665699089"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2364.356903076172,
            "unit": "ns",
            "range": "± 14.708137057548932"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 929.8303098678589,
            "unit": "ns",
            "range": "± 5.270731362564675"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 285.77330350875854,
            "unit": "ns",
            "range": "± 2.321198800841261"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 964.9161643981934,
            "unit": "ns",
            "range": "± 3.9523221321583644"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3983.373825073242,
            "unit": "ns",
            "range": "± 30.681303383662556"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 4045.6074180603027,
            "unit": "ns",
            "range": "± 13.081616532628018"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.28817868232727,
            "unit": "ns",
            "range": "± 0.007666161366112763"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 60.939288854599,
            "unit": "ns",
            "range": "± 0.06086973378599137"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 14.891605198383331,
            "unit": "ns",
            "range": "± 0.005320398326181873"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 114.9567442536354,
            "unit": "ns",
            "range": "± 0.1860112824718606"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 12.136898681521416,
            "unit": "ns",
            "range": "± 0.008175681094629938"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 11.265861250460148,
            "unit": "ns",
            "range": "± 0.006886507314504347"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.649918362498283,
            "unit": "ns",
            "range": "± 0.022571724024165876"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.31884300708771,
            "unit": "ns",
            "range": "± 0.12388720202130017"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 8.61982898414135,
            "unit": "ns",
            "range": "± 0.005707417094544938"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.219250559806824,
            "unit": "ns",
            "range": "± 0.022037486158146494"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 480.80733728408813,
            "unit": "ns",
            "range": "± 21.469112701677307"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 478.46100425720215,
            "unit": "ns",
            "range": "± 9.496955354723362"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 4783.551086425781,
            "unit": "ns",
            "range": "± 126.1615120330895"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 16.78173565864563,
            "unit": "ns",
            "range": "± 0.012855433268465392"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 94.11307144165039,
            "unit": "ns",
            "range": "± 5.186866111795769"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 355.6937551498413,
            "unit": "ns",
            "range": "± 0.20206082949395002"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 730.3768863677979,
            "unit": "ns",
            "range": "± 1.7842282722230065"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 468.36481761932373,
            "unit": "ns",
            "range": "± 1.5722086342437176"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 988.7496948242188,
            "unit": "ns",
            "range": "± 3.4383297348656168"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 169.52041721343994,
            "unit": "ns",
            "range": "± 0.1436053677218606"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 160.9724998474121,
            "unit": "ns",
            "range": "± 0.21146843665536605"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 167.96157884597778,
            "unit": "ns",
            "range": "± 0.1973075118050018"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 154.75415325164795,
            "unit": "ns",
            "range": "± 0.10391521417773036"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 184.56837344169617,
            "unit": "ns",
            "range": "± 0.22726764678165623"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.976039681583643,
            "unit": "ns",
            "range": "± 0.0024384994546852545"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.896176490932703,
            "unit": "ns",
            "range": "± 0.00290083074285794"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 123.0300817489624,
            "unit": "ns",
            "range": "± 0.0681785370153503"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 266.55127215385437,
            "unit": "ns",
            "range": "± 0.15172835928990913"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 4002.2827911376953,
            "unit": "ns",
            "range": "± 11.497226339821335"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4480.794174194336,
            "unit": "ns",
            "range": "± 10.007259773819058"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2109.898654937744,
            "unit": "ns",
            "range": "± 4.506622807747263"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2138.4714317321777,
            "unit": "ns",
            "range": "± 8.988139663735755"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2145.5893020629883,
            "unit": "ns",
            "range": "± 5.3026878456384035"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5444.3327713012695,
            "unit": "ns",
            "range": "± 51.331056778431275"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.31391215324402,
            "unit": "ns",
            "range": "± 0.012619737812971356"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 128.7941586971283,
            "unit": "ns",
            "range": "± 0.08208046486422042"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 184.3276002407074,
            "unit": "ns",
            "range": "± 0.6773359548760074"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 165.19999730587006,
            "unit": "ns",
            "range": "± 0.07917199578477732"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 154.4991399049759,
            "unit": "ns",
            "range": "± 0.07477423728218784"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 128.82847368717194,
            "unit": "ns",
            "range": "± 0.12368137411324569"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 235.63580679893494,
            "unit": "ns",
            "range": "± 0.32795735594775777"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 409.6868681907654,
            "unit": "ns",
            "range": "± 0.22582664772042763"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 420.372031211853,
            "unit": "ns",
            "range": "± 0.3359089493979907"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 402.84159111976624,
            "unit": "ns",
            "range": "± 0.5914204912152979"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 206.1260917186737,
            "unit": "ns",
            "range": "± 0.050015446856739386"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 210.42808961868286,
            "unit": "ns",
            "range": "± 0.13195504794007043"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 209.30019736289978,
            "unit": "ns",
            "range": "± 0.13071465493211096"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1666.0388355255127,
            "unit": "ns",
            "range": "± 5.164206514015494"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 210.3660614490509,
            "unit": "ns",
            "range": "± 0.24951676625909944"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 127.57325553894043,
            "unit": "ns",
            "range": "± 0.08043938259142812"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 211.964546084404,
            "unit": "ns",
            "range": "± 0.42397090830567674"
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
          "id": "68995f8daa1083645e5080b84bc010da757da00f",
          "message": "feat(options): add per-strategy handling overrides (#175)\n\n* feat(options): add local handling overrides\n\n* fix(analyzers): honor local overrides\n\n* fix(analyzers): resolve handling configurators\n\n* fix(analyzers): recognize compound overrides\n\n* fix(analyzers): treat opaque config as unknown\n\n* fix(analyzers): follow configurator helpers\n\n* fix(analyzers): preserve config uncertainty\n\n* fix(options): preserve handling semantics",
          "timestamp": "2026-08-23T16:36:49+01:00",
          "tree_id": "51ac59b3eea47313b3635d2fe7b15134dc1e74b5",
          "url": "https://github.com/thomhurst/Kevlar/commit/68995f8daa1083645e5080b84bc010da757da00f"
        },
        "date": 1787501518134,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 122.36363542079926,
            "unit": "ns",
            "range": "± 0.2249975419933442"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 73.84014546871185,
            "unit": "ns",
            "range": "± 0.10467313245717903"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 109.07528525590897,
            "unit": "ns",
            "range": "± 0.07281031982454439"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 12.972420379519463,
            "unit": "ns",
            "range": "± 0.015618157452921876"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 101.83082270622253,
            "unit": "ns",
            "range": "± 0.06275892562218438"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 102.49658632278442,
            "unit": "ns",
            "range": "± 0.051149526378234715"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4963.354316711426,
            "unit": "ns",
            "range": "± 10.549423319090835"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4958.855239868164,
            "unit": "ns",
            "range": "± 12.296970708812088"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 212.8877054452896,
            "unit": "ns",
            "range": "± 0.06166566183344578"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 246.6203155517578,
            "unit": "ns",
            "range": "± 0.6665596375677687"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 221.506844997406,
            "unit": "ns",
            "range": "± 0.2178762277055644"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 231.91194200515747,
            "unit": "ns",
            "range": "± 0.06853121272290591"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 144.93328666687012,
            "unit": "ns",
            "range": "± 0.6987469837949825"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 185.4224603176117,
            "unit": "ns",
            "range": "± 0.3104270606871029"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 143.51856648921967,
            "unit": "ns",
            "range": "± 0.1511111079731198"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2152.2986030578613,
            "unit": "ns",
            "range": "± 7.002592034036087"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2131.917631149292,
            "unit": "ns",
            "range": "± 7.243617112632699"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2165.923988342285,
            "unit": "ns",
            "range": "± 10.142267835126185"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5602.782066345215,
            "unit": "ns",
            "range": "± 104.69462538686173"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 112.84895044565201,
            "unit": "ns",
            "range": "± 0.07481043149753655"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 115.42704701423645,
            "unit": "ns",
            "range": "± 0.2773940148871034"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2156.2894897460938,
            "unit": "ns",
            "range": "± 4.443260021991292"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2242.8352584838867,
            "unit": "ns",
            "range": "± 13.936197583903995"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.1536020264029503,
            "unit": "ns",
            "range": "± 0.0007212586742143671"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 99.25564521551132,
            "unit": "ns",
            "range": "± 0.2835323934171623"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 43.84599554538727,
            "unit": "ns",
            "range": "± 1.4537690569743924"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 543.0464601516724,
            "unit": "ns",
            "range": "± 7.617221298346625"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 27.453223943710327,
            "unit": "ns",
            "range": "± 0.3484383581570468"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 410.19898414611816,
            "unit": "ns",
            "range": "± 3.9229169441885148"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3710.827350616455,
            "unit": "ns",
            "range": "± 6.064767979113084"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3799.424388885498,
            "unit": "ns",
            "range": "± 13.533118140541331"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3788.349582672119,
            "unit": "ns",
            "range": "± 10.742998668498965"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 7325.329833984375,
            "unit": "ns",
            "range": "± 254.53984622629974"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3803.0196895599365,
            "unit": "ns",
            "range": "± 7.510483265558479"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 199.89127933979034,
            "unit": "ns",
            "range": "± 0.27107594713579397"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 435.7420256137848,
            "unit": "ns",
            "range": "± 0.5351852210669922"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2256.1881408691406,
            "unit": "ns",
            "range": "± 12.038634713701402"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 896.5974082946777,
            "unit": "ns",
            "range": "± 8.908615524218725"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 317.00842785835266,
            "unit": "ns",
            "range": "± 2.1934641912780695"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 954.4362325668335,
            "unit": "ns",
            "range": "± 1.9709593885815087"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3821.091339111328,
            "unit": "ns",
            "range": "± 24.88420013292918"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3745.185344696045,
            "unit": "ns",
            "range": "± 9.935992823104653"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 13.232547506690025,
            "unit": "ns",
            "range": "± 0.01192355869067338"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 54.50770080089569,
            "unit": "ns",
            "range": "± 0.035873292931703445"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 10.96985686570406,
            "unit": "ns",
            "range": "± 0.006238110436585814"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 101.75156885385513,
            "unit": "ns",
            "range": "± 0.11907221442193375"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.781109906733036,
            "unit": "ns",
            "range": "± 0.4612578982400624"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 10.70831348001957,
            "unit": "ns",
            "range": "± 0.012740410676670684"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 10.761965990066528,
            "unit": "ns",
            "range": "± 0.014776370156586125"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 56.07029002904892,
            "unit": "ns",
            "range": "± 0.050588410313793006"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 7.9771950244903564,
            "unit": "ns",
            "range": "± 0.005048537463463932"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 36.45432472229004,
            "unit": "ns",
            "range": "± 0.021231305015884532"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 893.806972026825,
            "unit": "ns",
            "range": "± 14.320304983317701"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 915.5558590888977,
            "unit": "ns",
            "range": "± 29.10982846594082"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 6846.685478210449,
            "unit": "ns",
            "range": "± 111.98730390490071"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 18.7366823554039,
            "unit": "ns",
            "range": "± 0.01661598224425946"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 153.8738498687744,
            "unit": "ns",
            "range": "± 2.4351495028947654"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 348.50692796707153,
            "unit": "ns",
            "range": "± 1.103442308341694"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 640.4394903182983,
            "unit": "ns",
            "range": "± 1.5840258683048425"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 487.9486150741577,
            "unit": "ns",
            "range": "± 1.6186351389287246"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 951.1853656768799,
            "unit": "ns",
            "range": "± 3.6427560611616414"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 147.43870306015015,
            "unit": "ns",
            "range": "± 0.050580888531717896"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 145.47091674804688,
            "unit": "ns",
            "range": "± 0.07639991396622578"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 144.5381724834442,
            "unit": "ns",
            "range": "± 0.21830680150084578"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 144.12065768241882,
            "unit": "ns",
            "range": "± 0.17407098940321036"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 166.58224487304688,
            "unit": "ns",
            "range": "± 0.16669854192499034"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.9379417151212692,
            "unit": "ns",
            "range": "± 0.016041025614396586"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 1.0949135459959507,
            "unit": "ns",
            "range": "± 0.0028397944563048325"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 105.90214025974274,
            "unit": "ns",
            "range": "± 0.2092560133483191"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 210.48977267742157,
            "unit": "ns",
            "range": "± 0.23133240607489283"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3557.787977218628,
            "unit": "ns",
            "range": "± 12.608289487055343"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3957.7806701660156,
            "unit": "ns",
            "range": "± 7.410567536876739"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1848.9129581451416,
            "unit": "ns",
            "range": "± 5.166071563548722"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1896.6389846801758,
            "unit": "ns",
            "range": "± 4.220551131046759"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1857.8747053146362,
            "unit": "ns",
            "range": "± 3.731521190052752"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5058.914432525635,
            "unit": "ns",
            "range": "± 92.05684231171588"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 13.250237360596657,
            "unit": "ns",
            "range": "± 0.007965177181029574"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 101.16760402917862,
            "unit": "ns",
            "range": "± 0.15789904381478329"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 151.79814648628235,
            "unit": "ns",
            "range": "± 0.15250563940644973"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 142.51983726024628,
            "unit": "ns",
            "range": "± 0.1373157879896484"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 142.88958930969238,
            "unit": "ns",
            "range": "± 0.16910446216427566"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 96.72583615779877,
            "unit": "ns",
            "range": "± 0.04675003442748676"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 191.09182119369507,
            "unit": "ns",
            "range": "± 0.0879441441735652"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 433.0865716934204,
            "unit": "ns",
            "range": "± 0.3684382638926686"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 392.7891752719879,
            "unit": "ns",
            "range": "± 0.09981499254643397"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 353.78323101997375,
            "unit": "ns",
            "range": "± 0.26956875998245317"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 202.2788542509079,
            "unit": "ns",
            "range": "± 0.08302054260225213"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 206.2316017150879,
            "unit": "ns",
            "range": "± 4.353126034743072"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 207.83132326602936,
            "unit": "ns",
            "range": "± 0.12820438673337858"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 2105.939826965332,
            "unit": "ns",
            "range": "± 17.294435154610937"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 201.74184477329254,
            "unit": "ns",
            "range": "± 3.3537052529953106"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 111.266517162323,
            "unit": "ns",
            "range": "± 0.07212662469276086"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 164.49201917648315,
            "unit": "ns",
            "range": "± 0.06457213061416477"
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
          "id": "3fb217fe4944c75a5063bbcd83d65fb5e8d82c4b",
          "message": "+semver:minor - refactor(api): pre-release API cleanups from review (#176)\n\nFive changes from the API review. The library is pre-release, so the\nrenames are breaking with no [Obsolete] shims.\n\n- Rename WhenDefault/OrDefault to WhenResultDefault/OrResultDefault.\n  \"Default\" meant default(TResult) while the neighbouring WhenAnyError()\n  means \"reset to default handling\" - two meanings in one clause family.\n  The new names join the existing WhenResult/OrResult pairing.\n- Replace OrWhen(Func<Exception, bool>) with Or(Func<Exception, bool>) on\n  ShieldBuilder and ShieldBuilder<TResult>, mirroring how When(predicate)\n  coexists with When<TException>(predicate). A bare lambda cannot infer\n  TException, so it binds to the non-generic overload.\n- Document on every Retry overload and on RetryOptions(.MaxRetries) that\n  the value counts retries, not attempts: Retry(3) makes up to 4 total\n  attempts.\n- Add context-only ExecuteWithContext/ExecuteWithContextAsync overloads\n  that take just the context-aware action, for callers reading\n  KevlarContext without the properties ceremony. They delegate to the\n  state-based overloads, passing the delegate itself as state.\n- Add KEV006: hedging added to an untyped Shield/ShieldBuilder or the\n  static Shield.Hedge factory runs the action concurrently more than\n  once, so it must be idempotent. Typed shields are not flagged.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ",
          "timestamp": "2026-08-23T17:17:19+01:00",
          "tree_id": "96a26381e40e839f39a73196befb22fc2259aabc",
          "url": "https://github.com/thomhurst/Kevlar/commit/3fb217fe4944c75a5063bbcd83d65fb5e8d82c4b"
        },
        "date": 1787503598741,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 133.041086435318,
            "unit": "ns",
            "range": "± 0.4256533723762757"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 85.7325496673584,
            "unit": "ns",
            "range": "± 0.11656141417828189"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 137.24649858474731,
            "unit": "ns",
            "range": "± 0.5301444719600972"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 18.290550380945206,
            "unit": "ns",
            "range": "± 0.021689827719053565"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 117.3383309841156,
            "unit": "ns",
            "range": "± 0.12455549661778532"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 121.04406583309174,
            "unit": "ns",
            "range": "± 0.22576101576143584"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 5494.331298828125,
            "unit": "ns",
            "range": "± 14.044882806020057"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 5598.53849029541,
            "unit": "ns",
            "range": "± 7.359862517830593"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 217.61531054973602,
            "unit": "ns",
            "range": "± 0.16389186119442506"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 267.8789396286011,
            "unit": "ns",
            "range": "± 0.9176804389227725"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 238.7884521484375,
            "unit": "ns",
            "range": "± 0.1744561813659967"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 228.31878781318665,
            "unit": "ns",
            "range": "± 0.49571476593578384"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 162.36005902290344,
            "unit": "ns",
            "range": "± 0.13731123873878032"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 208.88026130199432,
            "unit": "ns",
            "range": "± 0.17582552824944842"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 152.0813149213791,
            "unit": "ns",
            "range": "± 0.10865752797143122"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 2445.6239700317383,
            "unit": "ns",
            "range": "± 5.3804388961294105"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 2520.7017765045166,
            "unit": "ns",
            "range": "± 6.7148618950050825"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 2471.2999267578125,
            "unit": "ns",
            "range": "± 5.6791270958727464"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 6031.302291870117,
            "unit": "ns",
            "range": "± 35.96328200943711"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 137.45635986328125,
            "unit": "ns",
            "range": "± 0.25555467104878365"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 134.57466971874237,
            "unit": "ns",
            "range": "± 0.0819512169094264"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 2480.383159637451,
            "unit": "ns",
            "range": "± 3.7628391128890626"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 2560.567440032959,
            "unit": "ns",
            "range": "± 6.649674479821463"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.555735046043992,
            "unit": "ns",
            "range": "± 0.002823595941908409"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 103.05896699428558,
            "unit": "ns",
            "range": "± 0.07003822505037069"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 40.982279539108276,
            "unit": "ns",
            "range": "± 0.51027900767669"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 498.3948497772217,
            "unit": "ns",
            "range": "± 4.701409472470444"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 24.34100466966629,
            "unit": "ns",
            "range": "± 0.0592103282351349"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 374.6287169456482,
            "unit": "ns",
            "range": "± 0.8433417156514067"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 4155.916221618652,
            "unit": "ns",
            "range": "± 6.1842536885820145"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 4143.862594604492,
            "unit": "ns",
            "range": "± 15.512745781845851"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 4264.559432983398,
            "unit": "ns",
            "range": "± 8.901886198071407"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 8736.044189453125,
            "unit": "ns",
            "range": "± 151.25130269230226"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 4153.821403503418,
            "unit": "ns",
            "range": "± 8.588511074461143"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 211.07849097251892,
            "unit": "ns",
            "range": "± 0.11854798487898512"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 479.1654939651489,
            "unit": "ns",
            "range": "± 0.46255643009057257"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 2399.1934852600098,
            "unit": "ns",
            "range": "± 4.122354191545666"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 952.7697887420654,
            "unit": "ns",
            "range": "± 2.998237430958661"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 315.0656614303589,
            "unit": "ns",
            "range": "± 1.4655135971476663"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 962.3988523483276,
            "unit": "ns",
            "range": "± 2.460781539900939"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 4167.127151489258,
            "unit": "ns",
            "range": "± 5.509399522684149"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3996.757110595703,
            "unit": "ns",
            "range": "± 8.827793252102339"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 18.299669206142426,
            "unit": "ns",
            "range": "± 0.009867958834115034"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 58.06447285413742,
            "unit": "ns",
            "range": "± 0.03482880242537799"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 14.556277111172676,
            "unit": "ns",
            "range": "± 0.006301736253150978"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 116.81198310852051,
            "unit": "ns",
            "range": "± 0.10048937074245325"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.965372301638126,
            "unit": "ns",
            "range": "± 0.021460178013916496"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 11.226227186620235,
            "unit": "ns",
            "range": "± 0.029786530256165032"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 14.38893347978592,
            "unit": "ns",
            "range": "± 0.008441615286082994"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 61.057326793670654,
            "unit": "ns",
            "range": "± 0.036464888103493645"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 8.63283234834671,
            "unit": "ns",
            "range": "± 0.005189979492976415"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 30.553446799516678,
            "unit": "ns",
            "range": "± 0.015049538918034703"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 547.562611579895,
            "unit": "ns",
            "range": "± 10.16678380651166"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 564.388331413269,
            "unit": "ns",
            "range": "± 12.065380173110569"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 5518.963111877441,
            "unit": "ns",
            "range": "± 52.952693758933385"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 16.780532553792,
            "unit": "ns",
            "range": "± 0.01395004911667354"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 93.13796067237854,
            "unit": "ns",
            "range": "± 0.5549584906998184"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 342.0808598995209,
            "unit": "ns",
            "range": "± 0.31162791875873663"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 687.7452707290649,
            "unit": "ns",
            "range": "± 1.4949438765058916"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 479.73146533966064,
            "unit": "ns",
            "range": "± 1.009370927794324"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 1013.8280839920044,
            "unit": "ns",
            "range": "± 1.1226425924432055"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 165.8844292163849,
            "unit": "ns",
            "range": "± 0.40767711599915435"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 160.9672075510025,
            "unit": "ns",
            "range": "± 0.09479994466903277"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 168.94843673706055,
            "unit": "ns",
            "range": "± 0.08082513910842812"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 154.80617213249207,
            "unit": "ns",
            "range": "± 0.4061456930905696"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 196.2268807888031,
            "unit": "ns",
            "range": "± 0.09784164587323164"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.8972365185618401,
            "unit": "ns",
            "range": "± 0.00315289379839699"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.9717499762773514,
            "unit": "ns",
            "range": "± 0.0022638880300412047"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 127.04414308071136,
            "unit": "ns",
            "range": "± 0.16740983312636246"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 259.5565986633301,
            "unit": "ns",
            "range": "± 0.5275294082876213"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 4083.2974014282227,
            "unit": "ns",
            "range": "± 9.805892447726428"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 4693.548309326172,
            "unit": "ns",
            "range": "± 5.931414222812203"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 2132.9078216552734,
            "unit": "ns",
            "range": "± 4.308728943338945"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 2223.54966545105,
            "unit": "ns",
            "range": "± 6.668874782664595"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 2252.950168609619,
            "unit": "ns",
            "range": "± 8.741859119702612"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 5429.530818939209,
            "unit": "ns",
            "range": "± 62.08192461822535"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 18.2845918238163,
            "unit": "ns",
            "range": "± 0.010207901855668894"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 127.66584300994873,
            "unit": "ns",
            "range": "± 0.33215880189021035"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 151.33079409599304,
            "unit": "ns",
            "range": "± 0.08583820232768544"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 166.28864431381226,
            "unit": "ns",
            "range": "± 0.15160819089253127"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 167.4835466146469,
            "unit": "ns",
            "range": "± 0.096804435316872"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 136.7135877609253,
            "unit": "ns",
            "range": "± 0.10723931290801401"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 234.944659948349,
            "unit": "ns",
            "range": "± 0.11322858875068913"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 424.1502208709717,
            "unit": "ns",
            "range": "± 0.7718866520025824"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 425.50491404533386,
            "unit": "ns",
            "range": "± 0.6198517160198385"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 399.3903841972351,
            "unit": "ns",
            "range": "± 0.8909952478485996"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 208.8337800502777,
            "unit": "ns",
            "range": "± 0.8545404874786533"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 237.7073016166687,
            "unit": "ns",
            "range": "± 0.218977757434563"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 214.6921284198761,
            "unit": "ns",
            "range": "± 0.7541949849716769"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1665.1382369995117,
            "unit": "ns",
            "range": "± 8.53401806103959"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 210.36507940292358,
            "unit": "ns",
            "range": "± 0.33071170787794424"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 123.95265173912048,
            "unit": "ns",
            "range": "± 0.12820992328436007"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 201.6561098098755,
            "unit": "ns",
            "range": "± 0.25737391313871716"
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
          "id": "3730820a3bc99b7299b1164ed8d89e959dbd6e1d",
          "message": "+semver:minor - refactor(api): second round of pre-release API cleanups (#177)\n\n* refactor(fallback)!: drop the typed onFallback overload tier\n\nShield<TResult> and ShieldBuilder<TResult> each carried three fallback\nshapes across three tiers: bare, a positional Action<FallbackEvent<T>>\nonFallback, and an Action<FallbackOptions<T>> configure. The middle tier\nexisted only as [Obsolete(error: true)] migration shims that stopped old\npositional callbacks from silently binding as configurators.\n\nRemove all six. Notifications stay reachable through\nconfigure(o => o.OnFallback = ...), which is the only spelling the\nuntyped Shield/ShieldExtensions fallbacks ever had, so both matrices are\nnow bare + configure. A leftover positional Action<FallbackEvent<T>>\ncallback now fails with CS1503 instead of CS0619.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* refactor(hedging)!: rename HedgingOptions to HedgeOptions\n\nEvery other strategy shares a stem with its options type — Retry/RetryOptions,\nTimeout/TimeoutOptions, CircuitBreaker/CircuitBreakerOptions — but Hedge took\nHedgingOptions. Rename HedgingOptions and HedgingOptions<TResult> to\nHedgeOptions and HedgeOptions<TResult> across the core package.\n\nKevlar.Extensions.Http keeps StandardHedgingShieldOptions and\nAddStandardHedgingShield: those read as \"the standard hedging shield\", not as a\nstrategy/options pair, and are out of scope.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* feat(shield): add Shield.Fallback factories and typed Compose\n\nTwo symmetry gaps from the API review.\n\nShield gained static Retry/Timeout/CircuitBreaker/RateLimit/ConcurrencyLimit/\nHedge factories but not Fallback, so a fallback-first chain had to start from\nShield.Empty. Add the four static Fallback factories mirroring the untyped\nShieldExtensions.Fallback overloads (with and without the Exception parameter,\neach with and without an Action<FallbackOptions> configure), delegating to\nShieldExtensions.Fallback(Empty, ...) like the other factories.\n\nShield<TResult> had Wrap but no Compose, while the untyped Shield had both. Add\nstatic Shield<TResult>.Compose(params Shield<TResult>[]) with the untyped\nsemantics: first shield outermost, first non-null Name and TimeProvider win,\nambient clause sealed. Both Compose implementations now share a new internal\nShield.Concat(Strategy[][]) overload.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* feat(analyzers): add KEV007 for handling clauses that go nowhere\n\nA When/Or clause only changes behaviour once a reactive strategy consumes\nit, so a clause that never reaches one is a silent no-op. KEV007 reports\ntwo shapes:\n\n- the ShieldBuilder is discarded — dropped as a statement, assigned to a\n  discard, or stored in a local nothing ever reads. Only the outermost\n  link of a dead Or chain is reported.\n- a later When.../WhenAnyError() replaces the clause while only proactive\n  strategies (timeout, rate limit, concurrency limit) stood between them,\n  so nothing ever consulted it.\n\nThe walk follows one fluent chain and stays quiet wherever it loses\nsight of the clause: builders that are returned, passed as arguments or\nassigned to fields, and Wrap/Compose boundaries.\n\nTwo KEV003 test cases build exactly the second shape to exercise clause\nreplacement; they now declare KEV007 as an expected companion or add the\nreactive strategy that makes the clause live.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* docs: make clause scoping, DI build order, and RetryEvent boxing explicit\n\n+semver:minor\n\n- README and the composition page now state the ambient clause rule up\n  front — a clause applies to the strategy it is attached to and to every\n  reactive strategy chained after it, until replaced, reset, or sealed by\n  Wrap/Compose — with a circuit breaker inheriting an earlier clause.\n- ShieldDefinition, ShieldDefinition.Build and the DI page spell out the\n  fixed strategy order Build produces, outermost first: Timeout → Retry →\n  CircuitBreaker → RateLimit → ConcurrencyLimit → AttemptTimeout, and that\n  configuration cannot reorder it.\n- RetryEvent.Result documents that the untyped event boxes value-type\n  results and points to RetryOptions<TResult>/RetryEvent<TResult>, whose\n  typed Outcome<TResult> avoids the box and the cast.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* docs(handling-failures): point the ambient clause section at KEV007\n\n+semver:minor\n\nSay \"the strategy it is attached to\" rather than \"the strategy it\ncreates\", and note that a clause reaching no reactive strategy does\nnothing — with a link to the new analyzer rule.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ",
          "timestamp": "2026-08-23T18:07:15+01:00",
          "tree_id": "7efb3856b6657af24d0477d196b0aa5501761f50",
          "url": "https://github.com/thomhurst/Kevlar/commit/3730820a3bc99b7299b1164ed8d89e959dbd6e1d"
        },
        "date": 1787506834111,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 107.96222686767578,
            "unit": "ns",
            "range": "± 0.055223367328666825"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 63.83517628908157,
            "unit": "ns",
            "range": "± 0.09725279269751212"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 106.99234360456467,
            "unit": "ns",
            "range": "± 0.1437320724414922"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 14.391334176063538,
            "unit": "ns",
            "range": "± 0.020781663737605857"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 92.60264837741852,
            "unit": "ns",
            "range": "± 0.1172989627412914"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 98.35680842399597,
            "unit": "ns",
            "range": "± 0.05204475544221212"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4099.425392150879,
            "unit": "ns",
            "range": "± 15.246868260462081"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4125.723670959473,
            "unit": "ns",
            "range": "± 6.112751125309476"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 173.92667150497437,
            "unit": "ns",
            "range": "± 0.08947341966575358"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 211.23791074752808,
            "unit": "ns",
            "range": "± 0.1578686870698475"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 190.2732799053192,
            "unit": "ns",
            "range": "± 0.1318211097296269"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 190.0393304824829,
            "unit": "ns",
            "range": "± 0.4795684304896535"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 121.55562329292297,
            "unit": "ns",
            "range": "± 0.13224409975383714"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 169.4449737071991,
            "unit": "ns",
            "range": "± 0.09983869733417457"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 118.00791716575623,
            "unit": "ns",
            "range": "± 0.22341456885013847"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 1838.5369930267334,
            "unit": "ns",
            "range": "± 3.4724374012264856"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 1864.329303741455,
            "unit": "ns",
            "range": "± 4.7774252065052485"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 1866.9495258331299,
            "unit": "ns",
            "range": "± 3.6668825086557297"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 4142.134071350098,
            "unit": "ns",
            "range": "± 41.97824218887487"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 119.34944319725037,
            "unit": "ns",
            "range": "± 0.37989421159578124"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 106.87408363819122,
            "unit": "ns",
            "range": "± 0.0850821636292987"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1852.8931465148926,
            "unit": "ns",
            "range": "± 4.373373149402082"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1890.540205001831,
            "unit": "ns",
            "range": "± 1.5805748249543754"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.3637033812701702,
            "unit": "ns",
            "range": "± 0.0004705764733076499"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 80.94565385580063,
            "unit": "ns",
            "range": "± 0.16182286229386972"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 29.561019957065582,
            "unit": "ns",
            "range": "± 0.20013213298181046"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 340.61613368988037,
            "unit": "ns",
            "range": "± 4.377151442796899"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 18.500419706106186,
            "unit": "ns",
            "range": "± 0.1115391294984743"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 281.2829575538635,
            "unit": "ns",
            "range": "± 2.860193898592285"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3083.031261444092,
            "unit": "ns",
            "range": "± 4.484324679833472"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3098.5895614624023,
            "unit": "ns",
            "range": "± 3.8574980835883745"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3058.1566772460938,
            "unit": "ns",
            "range": "± 10.510170976948396"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 6233.554908752441,
            "unit": "ns",
            "range": "± 32.93986141226721"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3124.2657146453857,
            "unit": "ns",
            "range": "± 5.551411818409738"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 169.8799433708191,
            "unit": "ns",
            "range": "± 0.4269763806126531"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 391.2112560272217,
            "unit": "ns",
            "range": "± 0.20260869751242813"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 1906.337100982666,
            "unit": "ns",
            "range": "± 10.21041574301378"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 668.9716181755066,
            "unit": "ns",
            "range": "± 5.735564473274922"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 209.07056784629822,
            "unit": "ns",
            "range": "± 1.9794350925661695"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 787.2164440155029,
            "unit": "ns",
            "range": "± 2.8015366577729397"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3227.81600189209,
            "unit": "ns",
            "range": "± 17.036742285346428"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3161.9327392578125,
            "unit": "ns",
            "range": "± 14.060794888330257"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 14.31526604294777,
            "unit": "ns",
            "range": "± 0.012675593943809085"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 47.33025872707367,
            "unit": "ns",
            "range": "± 0.03202029219705352"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 11.122674904763699,
            "unit": "ns",
            "range": "± 0.0010412581330126183"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 90.15829360485077,
            "unit": "ns",
            "range": "± 0.19624348201480046"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.857960745692253,
            "unit": "ns",
            "range": "± 0.004472326785999279"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 8.08266369253397,
            "unit": "ns",
            "range": "± 0.004652357282207114"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 10.585323706269264,
            "unit": "ns",
            "range": "± 0.004069006148482537"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 48.552889078855515,
            "unit": "ns",
            "range": "± 0.014718870648660603"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 6.930301189422607,
            "unit": "ns",
            "range": "± 0.00862257318177123"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 23.87730512022972,
            "unit": "ns",
            "range": "± 0.021806730170944335"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 315.0808033943176,
            "unit": "ns",
            "range": "± 4.730491963734"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 370.6581380367279,
            "unit": "ns",
            "range": "± 4.116280727989877"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 3996.446449279785,
            "unit": "ns",
            "range": "± 107.2594185424927"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 13.403314724564552,
            "unit": "ns",
            "range": "± 0.020339426519257943"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 68.64346265792847,
            "unit": "ns",
            "range": "± 2.246260446088709"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 294.44146490097046,
            "unit": "ns",
            "range": "± 0.2548414660697229"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 549.5365362167358,
            "unit": "ns",
            "range": "± 1.9514803687172848"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 385.9323353767395,
            "unit": "ns",
            "range": "± 0.13794720331593316"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 814.4775743484497,
            "unit": "ns",
            "range": "± 1.2084118302779914"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 130.63247513771057,
            "unit": "ns",
            "range": "± 0.21826525073308595"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 122.945507645607,
            "unit": "ns",
            "range": "± 0.12532984707694766"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 129.6218466758728,
            "unit": "ns",
            "range": "± 0.12134754941089716"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 117.06744891405106,
            "unit": "ns",
            "range": "± 0.10381636107240745"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 140.4223334789276,
            "unit": "ns",
            "range": "± 0.06987502597371213"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.5636079907417297,
            "unit": "ns",
            "range": "± 0.0007568583453487109"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.5318028535693884,
            "unit": "ns",
            "range": "± 0.13466223073483116"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 100.77126181125641,
            "unit": "ns",
            "range": "± 0.10439296804736439"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 197.64350247383118,
            "unit": "ns",
            "range": "± 0.09376207226393561"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3056.2456283569336,
            "unit": "ns",
            "range": "± 3.5379208202331656"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3483.0718307495117,
            "unit": "ns",
            "range": "± 4.0117249292213755"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1624.3628721237183,
            "unit": "ns",
            "range": "± 3.9736718293525493"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1604.18989944458,
            "unit": "ns",
            "range": "± 1.0754596438970367"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1645.3890857696533,
            "unit": "ns",
            "range": "± 2.902343386644846"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 3845.524154663086,
            "unit": "ns",
            "range": "± 21.30529759743263"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 14.411452323198318,
            "unit": "ns",
            "range": "± 0.018421522939574234"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 95.48886555433273,
            "unit": "ns",
            "range": "± 0.056020696911251514"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 126.74395680427551,
            "unit": "ns",
            "range": "± 0.19742078595897866"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 128.80868983268738,
            "unit": "ns",
            "range": "± 0.15756380043864576"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 119.75655496120453,
            "unit": "ns",
            "range": "± 0.1705164950369656"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 121.55363965034485,
            "unit": "ns",
            "range": "± 0.10468368469674406"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 198.97700667381287,
            "unit": "ns",
            "range": "± 0.14156563246706608"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 350.44280433654785,
            "unit": "ns",
            "range": "± 0.27164058261051466"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 365.52212262153625,
            "unit": "ns",
            "range": "± 0.718552969604329"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 370.21681022644043,
            "unit": "ns",
            "range": "± 0.3841419670421601"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 157.63135933876038,
            "unit": "ns",
            "range": "± 0.1414272065154712"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 160.73546695709229,
            "unit": "ns",
            "range": "± 0.0882382346077231"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 159.82711255550385,
            "unit": "ns",
            "range": "± 0.1304457510128308"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1323.8921375274658,
            "unit": "ns",
            "range": "± 4.74651726316306"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 164.90846371650696,
            "unit": "ns",
            "range": "± 0.35533856289914295"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 115.2880322933197,
            "unit": "ns",
            "range": "± 0.21545041853724706"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 159.56372606754303,
            "unit": "ns",
            "range": "± 0.13444414212747677"
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
          "id": "848f0d0c74e10ba15f96b623ced51ac684aec9b8",
          "message": "+semver:minor - refactor(api): third round of pre-release API cleanups (#179)\n\n* +semver:minor - feat(describe): surface handling clauses in pipeline descriptions\n\nDescribe()/ToString() showed strategy configuration but not which handling\nclause each reactive strategy judged outcomes with, so the ambient clause was\ninvisible at runtime. Clauses now record a human-readable term as they are\nbuilt, and the pipeline description prefixes each run of strategies sharing a\nnon-default clause with [when ...]; a strategy whose options replaced the clause\nlocally is marked (local handling). Shields on default handling are unchanged.\n\nAlso connects the two handling vocabularies in the XML docs: every\nHandlesException/HandlesResult summary now leads with the fact that setting it\nmakes that strategy ignore the ambient When... clause, and both ShieldBuilder\ntypes document the per-strategy override from the clause side.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* +semver:minor - feat(analyzers): add KEV008 for discarded fluent chaining results\n\nShield and Shield<TResult> are immutable, so `shield.Retry(3);` as a statement\nbuilds a new shield and throws it away. KEV008 reports a Kevlar fluent call that\nyields a shield and is discarded as an expression statement; assigned, returned\nand argument-passed results stay quiet, as do executions, and discarded clause\nbuilders continue to report as KEV007.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* +semver:minor - fix(options): name both trip modes when a circuit breaker sets both\n\nConsecutiveFailures and FailureRatio select different trip modes and already\nrejected being set together, but the message said only \"not both\" and the\nConsecutiveFailures range error reported `options` as its parameter name. The\nmessage now names both properties and spells out each way to resolve it,\nincluding leaving both unset for the 5-consecutive-failure default, and covers\nCircuitBreakerOptions<TResult> through the same core.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ\n\n* +semver:minor - feat(context): poison pooled KevlarContext instances in debug builds\n\nThe \"never retain the context\" contract was documented but unenforced, so a\nretained context silently observed a later execution's state. Debug builds now\nmark a context invalid as it goes back to the pool and throw from every public\nmember until it is rented again; renting revives it, and the pool's reset path\nwrites fields directly so legitimate reuse is untouched. The guard and its flag\nare [Conditional(\"DEBUG\")], so release builds read no extra state.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ",
          "timestamp": "2026-08-23T18:52:14+01:00",
          "tree_id": "a9772ed6858310083a4736da3e1f74a27bb4d6dd",
          "url": "https://github.com/thomhurst/Kevlar/commit/848f0d0c74e10ba15f96b623ced51ac684aec9b8"
        },
        "date": 1787509459514,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 111.00625652074814,
            "unit": "ns",
            "range": "± 0.1662705863376355"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 63.472043633461,
            "unit": "ns",
            "range": "± 0.06611716683235433"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 102.89194530248642,
            "unit": "ns",
            "range": "± 0.09693142623198717"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 14.30962997674942,
            "unit": "ns",
            "range": "± 0.022537404117551764"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 94.8057923913002,
            "unit": "ns",
            "range": "± 0.07283646906406799"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 92.74602872133255,
            "unit": "ns",
            "range": "± 0.13655247241963991"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4156.091354370117,
            "unit": "ns",
            "range": "± 5.6251453715047095"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4115.322044372559,
            "unit": "ns",
            "range": "± 10.429686656669585"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 172.15575194358826,
            "unit": "ns",
            "range": "± 0.20607398064392898"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 208.11673593521118,
            "unit": "ns",
            "range": "± 0.3516823103399163"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 184.06984305381775,
            "unit": "ns",
            "range": "± 0.14079737791300811"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 188.03027963638306,
            "unit": "ns",
            "range": "± 0.24199546331712202"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 120.00788998603821,
            "unit": "ns",
            "range": "± 0.1242461192186747"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 159.77651929855347,
            "unit": "ns",
            "range": "± 0.22788728383707355"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 141.57884621620178,
            "unit": "ns",
            "range": "± 0.18014618048455436"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 1832.0021572113037,
            "unit": "ns",
            "range": "± 3.55312045089694"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 1894.3988513946533,
            "unit": "ns",
            "range": "± 2.7138502794802823"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 1874.6305694580078,
            "unit": "ns",
            "range": "± 3.525580641884889"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 4157.002510070801,
            "unit": "ns",
            "range": "± 31.325687054232176"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 110.36718755960464,
            "unit": "ns",
            "range": "± 0.07242954352964269"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 119.23341691493988,
            "unit": "ns",
            "range": "± 0.15933862900295487"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1927.209300994873,
            "unit": "ns",
            "range": "± 6.121442795790575"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1907.7568855285645,
            "unit": "ns",
            "range": "± 3.712644998574489"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.3644816987216473,
            "unit": "ns",
            "range": "± 0.0013559011858430162"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 81.97434663772583,
            "unit": "ns",
            "range": "± 0.10682382758328385"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 28.730649143457413,
            "unit": "ns",
            "range": "± 0.1439759543055308"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 332.9167642593384,
            "unit": "ns",
            "range": "± 2.5781472362237334"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 18.061740085482597,
            "unit": "ns",
            "range": "± 0.04442088127171606"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 258.86005997657776,
            "unit": "ns",
            "range": "± 1.4487687557875422"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3082.436080932617,
            "unit": "ns",
            "range": "± 4.18885451912784"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3056.264347076416,
            "unit": "ns",
            "range": "± 5.368911059485573"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3079.7865676879883,
            "unit": "ns",
            "range": "± 5.471371887840705"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 6292.915962219238,
            "unit": "ns",
            "range": "± 50.04438728328462"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3145.3881645202637,
            "unit": "ns",
            "range": "± 10.724028858117492"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 166.5549178123474,
            "unit": "ns",
            "range": "± 0.10808978275154178"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 386.64623403549194,
            "unit": "ns",
            "range": "± 0.18428722305652026"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 1780.0157089233398,
            "unit": "ns",
            "range": "± 8.337115355666471"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 658.0520668029785,
            "unit": "ns",
            "range": "± 2.7898454605623537"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 205.6967649459839,
            "unit": "ns",
            "range": "± 0.676540485477236"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 696.5395865440369,
            "unit": "ns",
            "range": "± 2.1527144588608174"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 3069.5262145996094,
            "unit": "ns",
            "range": "± 19.40605540675465"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3111.920097351074,
            "unit": "ns",
            "range": "± 24.443507064685946"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 14.333701491355896,
            "unit": "ns",
            "range": "± 0.012146232630145137"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 46.48035368323326,
            "unit": "ns",
            "range": "± 0.05966667502424863"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 11.125143893063068,
            "unit": "ns",
            "range": "± 0.005104031859930748"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 92.23898589611053,
            "unit": "ns",
            "range": "± 0.25080723457631104"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 8.818776369094849,
            "unit": "ns",
            "range": "± 0.006116681696700852"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 8.082165986299515,
            "unit": "ns",
            "range": "± 0.015308875079853028"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 10.590880915522575,
            "unit": "ns",
            "range": "± 0.0029000688986975855"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 47.6102312207222,
            "unit": "ns",
            "range": "± 0.02293024603177631"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 6.707035094499588,
            "unit": "ns",
            "range": "± 0.004771912424553821"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 23.335814654827118,
            "unit": "ns",
            "range": "± 0.01002046168125371"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 312.5628261566162,
            "unit": "ns",
            "range": "± 3.525500776200143"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 349.6982979774475,
            "unit": "ns",
            "range": "± 1.7935486571777781"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 3882.8939666748047,
            "unit": "ns",
            "range": "± 75.93796440559011"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 14.78640940785408,
            "unit": "ns",
            "range": "± 0.014612443027939575"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 77.24300682544708,
            "unit": "ns",
            "range": "± 2.698156498772923"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 288.1026840209961,
            "unit": "ns",
            "range": "± 0.48300221691599143"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 536.4885683059692,
            "unit": "ns",
            "range": "± 1.4640647709520627"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 393.5939474105835,
            "unit": "ns",
            "range": "± 0.18196311608779653"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 791.1054244041443,
            "unit": "ns",
            "range": "± 0.7817074503376487"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 131.05742621421814,
            "unit": "ns",
            "range": "± 0.14828133384809386"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 123.17153191566467,
            "unit": "ns",
            "range": "± 0.10418816029404353"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 145.51529264450073,
            "unit": "ns",
            "range": "± 0.2716451109268854"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 120.44014525413513,
            "unit": "ns",
            "range": "± 0.912107338143126"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 137.85575699806213,
            "unit": "ns",
            "range": "± 0.07526069327091349"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.5645992569625378,
            "unit": "ns",
            "range": "± 0.0011662271638548673"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 0.8054958563297987,
            "unit": "ns",
            "range": "± 0.13074395577892"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 99.05918234586716,
            "unit": "ns",
            "range": "± 0.04407645701107675"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 197.6772346496582,
            "unit": "ns",
            "range": "± 0.11587644528820164"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3067.0720348358154,
            "unit": "ns",
            "range": "± 3.70276325464388"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3423.0214309692383,
            "unit": "ns",
            "range": "± 4.39823652005027"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1627.4034729003906,
            "unit": "ns",
            "range": "± 2.2906074253296995"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1665.1589221954346,
            "unit": "ns",
            "range": "± 4.5225165943307575"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1639.720790863037,
            "unit": "ns",
            "range": "± 2.937530046415645"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 3848.013195037842,
            "unit": "ns",
            "range": "± 20.448229611811207"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 14.313267797231674,
            "unit": "ns",
            "range": "± 0.02553350892034131"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 101.6282177567482,
            "unit": "ns",
            "range": "± 0.10058218066755216"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 126.13669943809509,
            "unit": "ns",
            "range": "± 0.14272028766481631"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 130.0390740633011,
            "unit": "ns",
            "range": "± 0.11834870841927124"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 119.69649076461792,
            "unit": "ns",
            "range": "± 0.15109372081245961"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 120.88543725013733,
            "unit": "ns",
            "range": "± 0.11873083992818331"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 195.96378695964813,
            "unit": "ns",
            "range": "± 0.1928293246350385"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 347.58073830604553,
            "unit": "ns",
            "range": "± 0.2709656561790757"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 357.3211226463318,
            "unit": "ns",
            "range": "± 0.377000779705275"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 321.5679249763489,
            "unit": "ns",
            "range": "± 0.24457798961686314"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 156.87766528129578,
            "unit": "ns",
            "range": "± 0.19942607724944575"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 159.66393959522247,
            "unit": "ns",
            "range": "± 0.1396951030054924"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 158.61383938789368,
            "unit": "ns",
            "range": "± 0.11248101167485244"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1353.7278499603271,
            "unit": "ns",
            "range": "± 6.833911039606167"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 160.03874444961548,
            "unit": "ns",
            "range": "± 0.3709718125695564"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 96.96088027954102,
            "unit": "ns",
            "range": "± 0.13372383575287375"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 159.364262342453,
            "unit": "ns",
            "range": "± 0.10100935863161108"
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
          "id": "0c469a77fb79d975f880913d4cecf0dccb38bae0",
          "message": "+semver:minor - refactor(api): API review cleanups (#180)\n\nRename `WhenResultDefault`/`OrResultDefault` to `WhenResultIsDefault`/\n`OrResultIsDefault`. Straight pre-release rename, no [Obsolete] shim. The\n`Is` removes the last reading in which \"Default\" could mean Kevlar's\ndefault handling, so the remark that existed only to disambiguate it is\ngone; the summary still points at `WhenAnyError()` for resetting handling.\n\nSnapshot the accumulated clause when a builder seals it. `ShieldBuilder`\nand `ShieldBuilder<TResult>` now copy both predicate lists and render the\ndescription at Seal time, unconditionally, so extending a builder held in\na variable with further `Or…` terms cannot change a shield already built\nfrom it. The class docs state the remaining rule: `Or…` returns the same\nbuilder, so branch chains from a fresh `When…` rather than a shared one.\n\nAdd KEV009, an Info-severity hint on every reactive strategy that\ninherits a handling clause declared earlier in its chain, so the clause's\nspan is visible in the IDE. Retry, RetryForever, CircuitBreaker, Hedge\nand Fallback are flagged from the second one onward; proactive strategies\ncarry no clause and are never flagged, nor are strategies with a local\nHandlesException/HandlesResult override, nor anything past WhenAnyError()\nor a Wrap/Compose boundary. The hint marks the strategy name alone.\n\nDocument that `TimeoutExceededException` derives from `KevlarException`,\nnot `System.TimeoutException` — the trap Polly's `TimeoutRejectedException`\nalso set — with a callout and a worked catch example in the Polly\nmigration guide, and a sentence on the timeout strategy page.\n\nClaude-Session: https://claude.ai/code/session_01DarFLgjrFgAsDiGr3cwWkZ",
          "timestamp": "2026-08-23T19:52:27+01:00",
          "tree_id": "f746dac7104cb7aef8a534cdcc77f8839da4e7a3",
          "url": "https://github.com/thomhurst/Kevlar/commit/0c469a77fb79d975f880913d4cecf0dccb38bae0"
        },
        "date": 1787513335008,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Zero_Latency",
            "value": 92.96325773000717,
            "unit": "ns",
            "range": "± 0.16583687689801377"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Typed_Outcome",
            "value": 67.01452672481537,
            "unit": "ns",
            "range": "± 1.44788015437776"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Completed_Behavior",
            "value": 91.5342887043953,
            "unit": "ns",
            "range": "± 0.545728040171206"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Empty_Shield",
            "value": 12.0764734223485,
            "unit": "ns",
            "range": "± 0.1987771781414411"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Disabled_Chaos",
            "value": 87.20473152399063,
            "unit": "ns",
            "range": "± 1.2900928023025207"
          },
          {
            "name": "Kevlar.Benchmarks.ChaosBenchmarks.Excluded_Chaos",
            "value": 85.65403604507446,
            "unit": "ns",
            "range": "± 0.5586745493082266"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_IsolatedFastFail",
            "value": 4431.331512451172,
            "unit": "ns",
            "range": "± 47.08004753154629"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_IsolatedFastFail",
            "value": 4387.498916625977,
            "unit": "ns",
            "range": "± 48.35169905679234"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_RatioClosedHappyPath",
            "value": 185.10480344295502,
            "unit": "ns",
            "range": "± 1.4505241048999187"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Polly_RatioClosedHappyPath",
            "value": 218.73490810394287,
            "unit": "ns",
            "range": "± 2.43288816838688"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_DynamicDurationConfigured",
            "value": 196.37246358394623,
            "unit": "ns",
            "range": "± 1.4798706369528971"
          },
          {
            "name": "Kevlar.Benchmarks.CircuitBreakerBenchmarks.Kevlar_AsyncCallbackConfigured",
            "value": 199.25355184078217,
            "unit": "ns",
            "range": "± 0.7360947585120207"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_Uncontended",
            "value": 134.3058741092682,
            "unit": "ns",
            "range": "± 1.5618201650245145"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Polly_Uncontended",
            "value": 176.20943868160248,
            "unit": "ns",
            "range": "± 1.758212767212701"
          },
          {
            "name": "Kevlar.Benchmarks.ConcurrencyLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 133.6938920021057,
            "unit": "ns",
            "range": "± 1.0748097377246462"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_NoNotification",
            "value": 1863.6367530822754,
            "unit": "ns",
            "range": "± 19.209394383882604"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_SyncNotification",
            "value": 1900.525297164917,
            "unit": "ns",
            "range": "± 30.43148997365455"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_CompletedAsyncNotification",
            "value": 1924.3439083099365,
            "unit": "ns",
            "range": "± 22.658880891473057"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_YieldingAsyncNotification",
            "value": 5338.591796875,
            "unit": "ns",
            "range": "± 99.02122795113485"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_PassThrough",
            "value": 99.29975479841232,
            "unit": "ns",
            "range": "± 0.9658089818304412"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_PassThrough",
            "value": 103.64110660552979,
            "unit": "ns",
            "range": "± 0.9736693040155775"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Kevlar_Triggered",
            "value": 1880.6651420593262,
            "unit": "ns",
            "range": "± 33.728465806753356"
          },
          {
            "name": "Kevlar.Benchmarks.FallbackBenchmarks.Polly_Triggered",
            "value": 1972.2434463500977,
            "unit": "ns",
            "range": "± 24.655774048844705"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteDirect",
            "value": 1.1659618504345417,
            "unit": "ns",
            "range": "± 0.039737162065395085"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.WriteShielded",
            "value": 86.34595638513565,
            "unit": "ns",
            "range": "± 1.619083674598235"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerDirect",
            "value": 41.36386024951935,
            "unit": "ns",
            "range": "± 1.0402815185721233"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcStreamingBenchmarks.ServerShielded",
            "value": 522.5239024162292,
            "unit": "ns",
            "range": "± 5.751164922641385"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Direct",
            "value": 23.995630890130997,
            "unit": "ns",
            "range": "± 0.44495634804772716"
          },
          {
            "name": "Kevlar.Benchmarks.GrpcUnaryBenchmarks.Shielded",
            "value": 349.8996376991272,
            "unit": "ns",
            "range": "± 5.392517454866342"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.FixedHedge",
            "value": 3205.2208557128906,
            "unit": "ns",
            "range": "± 25.56177955840912"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.SyncHook",
            "value": 3330.868278503418,
            "unit": "ns",
            "range": "± 29.168281755419496"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.CompletedAsyncHook",
            "value": 3247.005039215088,
            "unit": "ns",
            "range": "± 33.28599651955821"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.YieldingAsyncHook",
            "value": 7210.973419189453,
            "unit": "ns",
            "range": "± 172.5818091793866"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.GeneratedAction",
            "value": 3333.4873847961426,
            "unit": "ns",
            "range": "± 39.39736570642149"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.KevlarPrimaryWins",
            "value": 191.8813374042511,
            "unit": "ns",
            "range": "± 2.1134314710958613"
          },
          {
            "name": "Kevlar.Benchmarks.HedgingBenchmarks.PollyPrimaryWins",
            "value": 386.7857050895691,
            "unit": "ns",
            "range": "± 6.45544291660799"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.BufferedContent_WithRetry",
            "value": 1766.8915843963623,
            "unit": "ns",
            "range": "± 15.488191951807677"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.RequestFactory_WithRetry",
            "value": 706.5050868988037,
            "unit": "ns",
            "range": "± 7.448933470888436"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Direct_NoContent",
            "value": 254.7462465763092,
            "unit": "ns",
            "range": "± 2.8662217968906076"
          },
          {
            "name": "Kevlar.Benchmarks.HttpReplayBenchmarks.Standard_NoContent",
            "value": 761.0614528656006,
            "unit": "ns",
            "range": "± 6.336644462385963"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.ManualComposition",
            "value": 2918.3497428894043,
            "unit": "ns",
            "range": "± 30.355431514000358"
          },
          {
            "name": "Kevlar.Benchmarks.HttpStandardHedgingBenchmarks.StandardRegistration",
            "value": 3016.56148147583,
            "unit": "ns",
            "range": "± 34.31076393067556"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_Empty",
            "value": 12.522512905299664,
            "unit": "ns",
            "range": "± 0.16996805898932343"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_Empty",
            "value": 53.41892156004906,
            "unit": "ns",
            "range": "± 0.36101664347715723"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyReferenceState",
            "value": 9.062540158629417,
            "unit": "ns",
            "range": "± 0.1351737163006016"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyContextState",
            "value": 80.03072136640549,
            "unit": "ns",
            "range": "± 0.5690625473752124"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyOutcomeState",
            "value": 5.003091737627983,
            "unit": "ns",
            "range": "± 0.07035568737223787"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyTaskOutcomeState",
            "value": 7.072672188282013,
            "unit": "ns",
            "range": "± 0.14930022945651822"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptyState",
            "value": 9.578231811523438,
            "unit": "ns",
            "range": "± 0.18611874935725917"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptyState",
            "value": 54.637702107429504,
            "unit": "ns",
            "range": "± 1.0060242996546902"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Kevlar_EmptySync",
            "value": 5.808901250362396,
            "unit": "ns",
            "range": "± 0.152027992586615"
          },
          {
            "name": "Kevlar.Benchmarks.OverheadBenchmarks.Polly_EmptySync",
            "value": 37.11673989892006,
            "unit": "ns",
            "range": "± 0.3987568755987672"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Capacity_Eviction",
            "value": 915.4380869865417,
            "unit": "ns",
            "range": "± 33.022476826480265"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Cold_FirstCreation",
            "value": 991.5555324554443,
            "unit": "ns",
            "range": "± 41.556252981229015"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.High_Key_Concurrency",
            "value": 5953.230087280273,
            "unit": "ns",
            "range": "± 120.40193192289568"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Lookup",
            "value": 18.676313012838364,
            "unit": "ns",
            "range": "± 0.180259637669083"
          },
          {
            "name": "Kevlar.Benchmarks.PartitioningBenchmarks.Warm_Concurrent_Lookups",
            "value": 194.60450148582458,
            "unit": "ns",
            "range": "± 19.930748862100508"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_RatioTimeoutRetryBreaker",
            "value": 316.1341209411621,
            "unit": "ns",
            "range": "± 5.05997483212632"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_RatioTimeoutRetryBreaker",
            "value": 539.8745231628418,
            "unit": "ns",
            "range": "± 7.412042477301313"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Kevlar_TokenBucketRatioFiveStrategyChain",
            "value": 458.18684816360474,
            "unit": "ns",
            "range": "± 6.465936494711376"
          },
          {
            "name": "Kevlar.Benchmarks.PipelineBenchmarks.Polly_TokenBucketRatioFiveStrategyChain",
            "value": 796.995753288269,
            "unit": "ns",
            "range": "± 15.530744837627763"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_TokenBucketUncontended",
            "value": 136.57280433177948,
            "unit": "ns",
            "range": "± 2.0291869558055207"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Polly_TokenBucketUncontended",
            "value": 127.95611095428467,
            "unit": "ns",
            "range": "± 0.5416566622602764"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_WithHooks_Uncontended",
            "value": 133.46188354492188,
            "unit": "ns",
            "range": "± 2.9037847617055492"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_FrameworkAdapter_Uncontended",
            "value": 126.8834787607193,
            "unit": "ns",
            "range": "± 0.8627853337560042"
          },
          {
            "name": "Kevlar.Benchmarks.RateLimitBenchmarks.Kevlar_PartitionedFrameworkAdapter_Uncontended",
            "value": 149.48097336292267,
            "unit": "ns",
            "range": "± 2.024497382303263"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.DirectSnapshot",
            "value": 0.7976409941911697,
            "unit": "ns",
            "range": "± 0.058225152013900855"
          },
          {
            "name": "Kevlar.Benchmarks.ReloadingShieldProviderBenchmarks.ReloadAwareCurrent",
            "value": 1.0371135622262955,
            "unit": "ns",
            "range": "± 0.06538951800816947"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_HappyPath",
            "value": 92.74856317043304,
            "unit": "ns",
            "range": "± 0.7757664482930008"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_HappyPath",
            "value": 203.5966305732727,
            "unit": "ns",
            "range": "± 1.551664346587945"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Kevlar_Recovery",
            "value": 3104.062623977661,
            "unit": "ns",
            "range": "± 59.990457157852006"
          },
          {
            "name": "Kevlar.Benchmarks.RetryBenchmarks.Polly_Recovery",
            "value": 3742.253246307373,
            "unit": "ns",
            "range": "± 71.55941729190396"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Fixed",
            "value": 1653.7725715637207,
            "unit": "ns",
            "range": "± 35.66860574190503"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.Synchronous",
            "value": 1651.2140064239502,
            "unit": "ns",
            "range": "± 34.884086697320726"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncCompleted",
            "value": 1648.3606414794922,
            "unit": "ns",
            "range": "± 30.718230721017274"
          },
          {
            "name": "Kevlar.Benchmarks.RetryDelayGeneratorBenchmarks.AsyncYielding",
            "value": 4684.6276931762695,
            "unit": "ns",
            "range": "± 140.13207532232815"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: False)",
            "value": 11.860895082354546,
            "unit": "ns",
            "range": "± 0.23638443799652092"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: False)",
            "value": 86.04964703321457,
            "unit": "ns",
            "range": "± 0.7400811287598247"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: False)",
            "value": 138.37347292900085,
            "unit": "ns",
            "range": "± 2.0001336220730788"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: False)",
            "value": 135.2812786102295,
            "unit": "ns",
            "range": "± 1.7164519826452898"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: False)",
            "value": 133.14463317394257,
            "unit": "ns",
            "range": "± 1.0098600940436453"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.EmptyShield(ListenerEnabled: True)",
            "value": 96.05329811573029,
            "unit": "ns",
            "range": "± 1.2730945153969841"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RetryHappyPath(ListenerEnabled: True)",
            "value": 176.01323199272156,
            "unit": "ns",
            "range": "± 4.06066290724465"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.CircuitBreakerHappyPath(ListenerEnabled: True)",
            "value": 400.09467101097107,
            "unit": "ns",
            "range": "± 3.216907451098334"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.RateLimitHappyPath(ListenerEnabled: True)",
            "value": 379.92062854766846,
            "unit": "ns",
            "range": "± 2.2090174483600804"
          },
          {
            "name": "Kevlar.Benchmarks.TelemetryBenchmarks.ConcurrencyLimitHappyPath(ListenerEnabled: True)",
            "value": 346.54180431365967,
            "unit": "ns",
            "range": "± 1.9548690802734798"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_HappyPath",
            "value": 166.38978481292725,
            "unit": "ns",
            "range": "± 1.1396837478103026"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Polly_HappyPath",
            "value": 170.40396749973297,
            "unit": "ns",
            "range": "± 1.643833441403388"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_SynchronousGenerator_HappyPath",
            "value": 168.4332230091095,
            "unit": "ns",
            "range": "± 1.2863271690740208"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsynchronousGenerator_HappyPath",
            "value": 1966.322120666504,
            "unit": "ns",
            "range": "± 17.54575463883794"
          },
          {
            "name": "Kevlar.Benchmarks.TimeoutBenchmarks.Kevlar_AsyncHookConfigured_HappyPath",
            "value": 169.42419266700745,
            "unit": "ns",
            "range": "± 1.8422477461774007"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Kevlar_ResultJudged",
            "value": 92.58540844917297,
            "unit": "ns",
            "range": "± 0.5868396040138728"
          },
          {
            "name": "Kevlar.Benchmarks.TypedResultBenchmarks.Polly_ResultJudged",
            "value": 153.694589138031,
            "unit": "ns",
            "range": "± 1.4077621200371486"
          }
        ]
      }
    ]
  }
}