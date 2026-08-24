using Kevlar;

var attempts = 0;
var shield = Shield.When<InvalidOperationException>()
    .Retry(2, Backoff.None)
    .WithName("console-retry");

var result = await shield.ExecuteAsync(_ =>
{
    var attempt = Interlocked.Increment(ref attempts);
    return attempt < 3
        ? ValueTask.FromException<string>(new InvalidOperationException("transient"))
        : new ValueTask<string>("recovered");
});

if (result != "recovered" || attempts != 3)
{
    throw new InvalidOperationException($"Expected recovery on attempt 3; result={result}, attempts={attempts}.");
}

Console.WriteLine($"Console retry sample passed after {attempts} attempts.");
