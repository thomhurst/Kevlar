namespace Kevlar.Chaos.Internal;

internal readonly struct ChaosDecision
{
    public ChaosDecision(string? operation, string? environment, double rate, double sample)
    {
        Operation = operation;
        Environment = environment;
        Rate = rate;
        Sample = sample;
    }

    public string? Operation { get; }

    public string? Environment { get; }

    public double Rate { get; }

    public double Sample { get; }
}
