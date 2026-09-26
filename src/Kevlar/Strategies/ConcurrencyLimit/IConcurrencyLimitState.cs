namespace Kevlar.Strategies;

internal interface IConcurrencyLimitState
{
    int MaxConcurrency { get; }

    int CurrentLimit { get; }

    (int Available, int Running, int Queued) CaptureState();
}
