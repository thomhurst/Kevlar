namespace Kevlar.Tests;

internal static class RaceRunner
{
    public static async Task RunBothOrdersAsync(
        string name,
        int repetitions,
        Func<RaceIteration, Task> race,
        int seed = 0x4B45564C)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetitions);

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var iterationSeed = unchecked(seed + (repetition * 397));
            var firstOrder = new Random(iterationSeed).Next(2) == 0
                ? RaceOrder.FirstThenSecond
                : RaceOrder.SecondThenFirst;

            for (var orderIndex = 0; orderIndex < 2; orderIndex++)
            {
                var order = orderIndex == 0
                    ? firstOrder
                    : Reverse(firstOrder);
                var context = new RaceIteration(name, (repetition * 2) + orderIndex, iterationSeed, order);

                try
                {
                    await TestHelpers.WaitAsync(race(context), context.Description);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Race failed: {context.Description}.", exception);
                }
            }
        }
    }

    private static RaceOrder Reverse(RaceOrder order) => order == RaceOrder.FirstThenSecond
        ? RaceOrder.SecondThenFirst
        : RaceOrder.FirstThenSecond;
}
