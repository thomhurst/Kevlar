using System.Text.Json;
using Kevlar.StressTests;

var options = StressOptions.Parse(args);
var result = await StressRunner.RunAsync(options);

var outputPath = Path.GetFullPath(options.OutputPath);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(result, StressJsonContext.Default.StressRunResult));

Console.WriteLine($"Results written to {outputPath}");
