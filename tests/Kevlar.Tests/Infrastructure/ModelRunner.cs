namespace Kevlar.Tests;

internal static class ModelRunner
{
    private static readonly int[] FixedSeeds = [0, 1, 17, 0x4B45564C, int.MaxValue];

    public static async Task RunAsync<TCommand>(
        string name,
        int commandCount,
        Func<Random, TCommand> generate,
        Func<IReadOnlyList<TCommand>, Task> execute)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandCount);
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(execute);

        foreach (var seed in GetSeeds())
        {
            var random = new Random(seed);
            var commands = Enumerable.Range(0, commandCount)
                .Select(_ => generate(random))
                .ToArray();

            try
            {
                await execute(commands);
            }
            catch (Exception exception)
            {
                var minimized = await MinimizeAsync(commands, execute);
                var minimizedException = exception;
                try
                {
                    await execute(minimized);
                }
                catch (Exception failure)
                {
                    minimizedException = failure;
                }

                throw new InvalidOperationException(
                    $"Model '{name}' failed with seed {seed}. Minimized commands: [{string.Join(", ", minimized)}].",
                    minimizedException);
            }
        }
    }

    private static IEnumerable<int> GetSeeds()
    {
        foreach (var seed in FixedSeeds)
        {
            yield return seed;
        }

        if (!int.TryParse(Environment.GetEnvironmentVariable("KEVLAR_MODEL_SWEEP_SEEDS"), out var sweepCount)
            || sweepCount <= 0)
        {
            yield break;
        }

        var sweepSeed = 0x13579BDF;
        for (var index = 0; index < sweepCount; index++)
        {
            sweepSeed = unchecked((sweepSeed * 1103515245) + 12345);
            yield return sweepSeed;
        }
    }

    private static async Task<IReadOnlyList<TCommand>> MinimizeAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        Func<IReadOnlyList<TCommand>, Task> execute)
    {
        var minimized = commands.ToList();
        for (var index = 0; index < minimized.Count;)
        {
            var candidate = minimized.Where((_, candidateIndex) => candidateIndex != index).ToArray();
            try
            {
                await execute(candidate);
                index++;
            }
            catch
            {
                minimized = candidate.ToList();
            }
        }

        return minimized;
    }
}
