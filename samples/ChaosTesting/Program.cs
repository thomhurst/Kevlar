using Kevlar;
using Kevlar.Chaos;

var decisions = 0;
var injections = 0;
var actionCalls = 0;
var chaos = ChaosShield.Fault(options =>
{
    options.Enabled = true;
    options.Operation = "sample-smoke";
    options.EnabledGenerator = _ => new(Interlocked.Increment(ref decisions) == 1);
    options.Exception = new ChaosInjectedException();
    options.OnInjected = _ =>
    {
        Interlocked.Increment(ref injections);
        return default;
    };
});
var shield = Shield.When<ChaosInjectedException>()
    .Retry(1, Backoff.None)
    .Wrap(chaos);

using (ChaosScope.Begin(operation: "sample-smoke", environment: "test"))
{
    await shield.ExecuteAsync(_ =>
    {
        Interlocked.Increment(ref actionCalls);
        return ValueTask.CompletedTask;
    });
}

if (injections != 1 || actionCalls != 1 || decisions != 2)
{
    throw new InvalidOperationException(
        $"Expected one injected fault followed by recovery; injections={injections}, actions={actionCalls}, decisions={decisions}.");
}

Console.WriteLine("Chaos testing sample passed with one bounded injected fault.");
