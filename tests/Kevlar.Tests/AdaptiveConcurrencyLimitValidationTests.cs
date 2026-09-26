namespace Kevlar.Tests;

public class AdaptiveConcurrencyLimitValidationTests
{
    [Test]
    public async Task Invalid_Options_Report_Their_Property_For_Both_Builders()
    {
        (Action<AdaptiveConcurrencyLimitOptions> Configure, string Property)[] cases =
        [
            (options => options.Algorithm = (AdaptiveConcurrencyLimitAlgorithm)42, "Algorithm"),
            (options => options.MinLimit = 0, "MinLimit"),
            (options => options.MaxLimit = 0, "MaxLimit"),
            (options => options.InitialLimit = 0, "InitialLimit"),
            (options => options.InitialLimit = 101, "InitialLimit"),
            (options => options.SamplingWindow = TimeSpan.Zero, "SamplingWindow"),
            (options => options.SamplingWindow = TimeSpan.FromTicks(-1), "SamplingWindow"),
            (options => options.DecreaseFactor = 0, "DecreaseFactor"),
            (options => options.DecreaseFactor = 1, "DecreaseFactor"),
            (options => options.DecreaseFactor = double.NaN, "DecreaseFactor"),
            (options => options.DecreaseFactor = double.PositiveInfinity, "DecreaseFactor"),
            (options => options.LatencyTolerance = 0.9, "LatencyTolerance"),
            (options => options.LatencyTolerance = double.NaN, "LatencyTolerance"),
            (options => options.LatencyTolerance = double.PositiveInfinity, "LatencyTolerance"),
        ];
        foreach (var item in cases)
        {
            var options = new AdaptiveConcurrencyLimitOptions();
            item.Configure(options);
            var untyped = await Assert.That(() => Shield.ConcurrencyLimit(options)).Throws<KevlarConfigurationException>();
            var typed = await Assert.That(() => Shield.For<int>().ConcurrencyLimit(options)).Throws<KevlarConfigurationException>();
            await Assert.That(untyped!.Message).Contains($"AdaptiveConcurrencyLimitOptions.{item.Property}");
            await Assert.That(typed!.Message).Contains($"AdaptiveConcurrencyLimitOptions.{item.Property}");
        }
    }

    [Test]
    public async Task Null_Options_Are_Rejected_By_All_Factories()
    {
        AdaptiveConcurrencyLimitOptions options = null!;
        await Assert.That(() => Shield.ConcurrencyLimit(options)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Empty.ConcurrencyLimit(options)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.For<int>().ConcurrencyLimit(options)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.When<IOException>().ConcurrencyLimit(options)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.For<int>().When<IOException>().ConcurrencyLimit(options)).Throws<ArgumentNullException>();
    }
}
