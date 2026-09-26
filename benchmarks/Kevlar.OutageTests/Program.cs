using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using Kevlar.OutageTests;

var options = OutageOptions.Parse(args);
var timestamp = DateTimeOffset.UtcNow;
var results = new List<ScenarioResult>();
foreach (var scenario in OutageRunner.Scenarios)
{
    var result = await OutageRunner.RunAsync(scenario, options);
    results.Add(result);
    Console.WriteLine($"{scenario}: {result.Phases.Sum(phase => phase.Offered)} offered, {result.Phases.Sum(phase => phase.Succeeded)} succeeded, {result.CancelledAttempts} cancelled attempts, {result.ActiveAfterDrain} active after drain.");
}
var buildVersion = typeof(OutageRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
var commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? buildVersion?.Split('+').ElementAtOrDefault(1) ?? "local-unrecorded";
var report = new OutageResult(1, timestamp, commit,
    RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
    Environment.ProcessorCount, options,
    new DependencySettings(OutageOptions.HealthyMilliseconds, OutageOptions.SlowMilliseconds, OutageOptions.AlternateMilliseconds,
        OutageOptions.FailureMilliseconds, OutageOptions.CleanupMilliseconds, OutageOptions.HedgeMilliseconds,
        OutageOptions.BreakMilliseconds, OutageOptions.Concurrency, OutageOptions.QueueLimit), results);
var output = Path.GetFullPath(options.OutputPath);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, OutageJsonContext.Default.OutageResult));
Console.WriteLine($"Results written to {output}");
