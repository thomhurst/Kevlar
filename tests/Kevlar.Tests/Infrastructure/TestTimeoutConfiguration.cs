using TUnit.Core;
using TUnit.Core.Executors;

[assembly: TestExecutor<Kevlar.Tests.ThreadPoolTestExecutor>]

namespace Kevlar.Tests;

/// <summary>
/// Bounds every test so a deadlock fails the test that caused it instead of stalling the run.
/// </summary>
public static class TestTimeoutConfiguration
{
    /// <summary>
    /// Generous enough for the slowest race and model tests on a heavily loaded machine.
    /// A test that needs longer must say so with its own <c>[Timeout]</c>.
    /// </summary>
    public static readonly TimeSpan DefaultTestTimeout = TimeSpan.FromMinutes(2);

    [Before(HookType.TestDiscovery)]
    public static void Configure(BeforeTestDiscoveryContext context)
    {
        // Without an explicit default, TUnit runs tests that lack [Timeout] with no timeout at all.
        context.Settings.Timeouts.DefaultTestTimeout = DefaultTestTimeout;
    }
}

/// <summary>
/// Starts each test on a thread-pool thread so TUnit's timeout always receives a task to observe.
/// </summary>
/// <remarks>
/// TUnit's default executor invokes the test on the calling thread. A test that blocks synchronously
/// before its first await (for example <c>ManualResetEventSlim.Wait()</c>) then never returns its task,
/// so neither the per-test timeout nor the session <c>--timeout</c> can end it, and the run hangs.
/// Remove this executor once https://github.com/thomhurst/TUnit/issues/7501 is fixed.
/// </remarks>
public sealed class ThreadPoolTestExecutor : GenericAbstractExecutor
{
    /// <inheritdoc />
    protected override ValueTask ExecuteAsync(Func<ValueTask> action) =>
        new(Task.Run(async () => await action().ConfigureAwait(false)));
}
