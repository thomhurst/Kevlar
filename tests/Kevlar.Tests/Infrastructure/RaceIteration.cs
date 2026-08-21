namespace Kevlar.Tests;

internal readonly record struct RaceIteration(
    string Name,
    int Iteration,
    int Seed,
    RaceOrder Order)
{
    public string Description =>
        $"race '{Name}', iteration {Iteration}, seed {Seed}, order {Order}";

    public Random CreateRandom() => new(Seed);

    public void Apply(Action first, Action second)
    {
        if (Order == RaceOrder.FirstThenSecond)
        {
            first();
            second();
        }
        else
        {
            second();
            first();
        }
    }
}
