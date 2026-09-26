using Kevlar.OutageTests;
using TUnit.Assertions;
using TUnit.Core;

namespace Kevlar.OutageTests.Tests;

public class MeasurementTests
{
    [Test]
    public async Task Percentiles_Use_Nearest_Rank_And_Empty_Data_Is_Absent()
    {
        await Assert.That(Measurements.Percentile(Enumerable.Range(1, 100).Select(value => (double)value), .95)).IsEqualTo(95);
        await Assert.That(Measurements.Percentile([4, 1, 3, 2], .99)).IsEqualTo(4);
        await Assert.That(Measurements.Percentile([], .99)).IsNull();
    }

    [Test]
    public async Task Recovery_Requires_A_Complete_Successful_Window_Including_Shed_Arrivals()
    {
        RequestMeasurement[] requests =
        [
            Request(0, 3, 3.01, "success"), Request(1, 3.1, 3.1, "harness_rejected"),
            Request(2, 3.25, 3.3, "success"), Request(3, 3.35, 3.6, "success"),
            Request(4, 3.5, 3.55, "circuit_rejected"), Request(5, 3.6, 3.65, "success"),
            Request(6, 3.75, 3.8, "success"), Request(7, 3.85, 3.9, "success")
        ];
        await Assert.That(Math.Abs(Measurements.Recovery(requests, 3, 4, .25, 2)!.Value - .6)).IsLessThan(.00001);
        requests[2].Outcome = "dependency_failed";
        await Assert.That(Measurements.Recovery(requests, 3, 4, .25, 2)).IsEqualTo(1);
        requests[6].Outcome = "concurrency_rejected";
        await Assert.That(Measurements.Recovery(requests, 3, 4, .25, 2)).IsNull();
    }

    [Test]
    public async Task Summary_Keeps_Scheduled_Latency_Shedding_And_Late_Cleanup_Visible()
    {
        var request = Request(0, 1, 1.5, "success");
        request.StartedSeconds = 1.1;
        request.Attempts = 2;
        var shed = Request(1, 1.1, 1.2, "harness_rejected");
        shed.StartedSeconds = 1.2;
        AttemptMeasurement[] attempts =
        [
            new(request, 1.1) { CompletedSeconds = 1.7, Cancelled = true },
            new(request, 1.2) { CompletedSeconds = 1.5 }
        ];
        var result = OutageRunner.Summarize("test", "test", new(PhaseSeconds: 1), [request, shed], attempts, 4, 1, 2, 0);
        var phase = result.Phases[1];
        await Assert.That(phase.Offered).IsEqualTo(2);
        await Assert.That(phase.Submitted).IsEqualTo(1);
        await Assert.That(phase.Admitted).IsEqualTo(1);
        await Assert.That(phase.Failed).IsEqualTo(1);
        await Assert.That(phase.HarnessRejected).IsEqualTo(1);
        await Assert.That(phase.AttemptsPerOfferedRequest).IsEqualTo(1);
        await Assert.That(phase.AttemptsPerAdmittedRequest).IsEqualTo(2);
        await Assert.That(phase.LatencyP99Milliseconds).IsEqualTo(500);
        await Assert.That(result.LosersPendingAtCallerCompletion).IsEqualTo(1);
        await Assert.That(Math.Abs(result.CleanupAfterCallerMaximumMilliseconds - 200)).IsLessThan(.00001);
    }

    [Test]
    public async Task Invalid_Workloads_Are_Rejected_Before_Allocating_Results()
    {
        await Assert.That(() => OutageOptions.Parse(["--phase-seconds", "0"])).Throws<ArgumentException>();
        await Assert.That(() => OutageOptions.Parse(["--rate", "10001"])).Throws<ArgumentException>();
        await Assert.That(() => OutageOptions.Parse(["--max-inflight", "0"])).Throws<ArgumentException>();
        await Assert.That(() => OutageOptions.Parse(["--rate"])).Throws<ArgumentException>();
    }

    private static RequestMeasurement Request(int id, double scheduled, double completed, string outcome) =>
        new(id, scheduled) { CompletedSeconds = completed, Outcome = outcome };
}
