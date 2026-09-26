namespace Kevlar.Strategies;

internal interface IRateLimitState
{
    (long Available, int Queued) CaptureState(TimeProvider timeProvider);
}
