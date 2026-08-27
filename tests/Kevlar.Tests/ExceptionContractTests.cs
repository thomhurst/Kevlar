using System.Reflection;
using Kevlar.Chaos;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.RateLimiting;
using Kevlar.Internal;
using Kevlar.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class ExceptionContractTests
{
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(Shield).Assembly,
        typeof(Kevlar.Analyzers.PipelineHazardAnalyzer).Assembly,
        typeof(ChaosShield).Assembly,
        typeof(KevlarServiceCollectionExtensions).Assembly,
        typeof(GrpcShield).Assembly,
        typeof(HttpShield).Assembly,
        typeof(ShieldRateLimiterExtensions).Assembly,
        typeof(TelemetryRecorder).Assembly,
    ];

    [Test]
    public async Task Every_Core_Rejection_Derives_From_ExecutionRejectedException()
    {
        Type[] rejectionTypes =
        [
            typeof(CircuitOpenException),
            typeof(RateLimitExceededException),
            typeof(ConcurrencyLimitExceededException),
        ];

        foreach (var rejectionType in rejectionTypes)
        {
            await Assert.That(rejectionType.IsSubclassOf(typeof(ExecutionRejectedException))).IsTrue();
        }
    }

    [Test]
    public async Task Timeout_Is_Not_An_Execution_Rejection()
    {
        await Assert.That(
            typeof(ExecutionRejectedException).IsAssignableFrom(typeof(TimeoutExceededException)))
            .IsFalse();
        await Assert.That(typeof(KevlarException).IsAssignableFrom(typeof(TimeoutExceededException)))
            .IsTrue();
    }

    [Test]
    public async Task Catching_ExecutionRejectedException_Exposes_Common_RetryAfter()
    {
        var retryAfter = TimeSpan.FromSeconds(5);
        ExecutionRejectedException[] rejections =
        [
            new CircuitOpenException(retryAfter, isIsolated: false, lastException: null),
            new RateLimitExceededException(retryAfter),
            new ConcurrencyLimitExceededException(),
        ];

        foreach (var rejection in rejections)
        {
            var caught = CatchRejection(rejection);
            await Assert.That(ReferenceEquals(caught, rejection)).IsTrue();
        }

        await Assert.That(rejections[0].RetryAfter).IsEqualTo(retryAfter);
        await Assert.That(rejections[1].RetryAfter).IsEqualTo(retryAfter);
        await Assert.That(rejections[2].RetryAfter).IsNull();
    }

    [Test]
    public async Task Default_Handling_Excludes_Rejections_But_Allows_Timeouts()
    {
        var customRejection = Outcome<int>.FromException(new TestExecutionRejectedException());
        var timeout = Outcome<int>.FromException(
            new TimeoutExceededException(TimeSpan.FromSeconds(1)));

        await Assert.That(OutcomeJudge.Default.ShouldHandle(in customRejection)).IsFalse();
        await Assert.That(OutcomeJudge.Default.ShouldHandle(in timeout)).IsTrue();
    }

    [Test]
    public async Task Every_Concrete_Public_Exception_Has_Standard_Constructors()
    {
        foreach (var exceptionType in GetPublicExceptionTypes().Where(static type => !type.IsAbstract))
        {
            await Assert.That(exceptionType.GetConstructor(Type.EmptyTypes)).IsNotNull();
            await Assert.That(exceptionType.GetConstructor([typeof(string)])).IsNotNull();
            await Assert.That(exceptionType.GetConstructor([typeof(string), typeof(Exception)])).IsNotNull();
        }
    }

    [Test]
    public async Task Core_Exception_Bases_Have_Protected_Standard_Constructors()
    {
        Type[] baseTypes = [typeof(KevlarException), typeof(ExecutionRejectedException)];
        Type[][] signatures =
        [
            Type.EmptyTypes,
            [typeof(string)],
            [typeof(string), typeof(Exception)],
        ];

        foreach (var baseType in baseTypes)
        {
            foreach (var signature in signatures)
            {
                var constructor = baseType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    signature,
                    modifiers: null);
                await Assert.That(constructor).IsNotNull();
            }
        }
    }

    [Test]
    public async Task Standard_Constructors_Preserve_Message_And_Inner_Exception()
    {
        var innerException = new InvalidOperationException("cause");
        foreach (var exceptionType in GetPublicExceptionTypes().Where(static type => !type.IsAbstract))
        {
            var withMessage = (Exception)Activator.CreateInstance(exceptionType, "custom")!;
            var withCause = (Exception)Activator.CreateInstance(exceptionType, "custom", innerException)!;

            await Assert.That(withMessage.Message).IsEqualTo("custom");
            await Assert.That(ReferenceEquals(withCause.InnerException, innerException)).IsTrue();
        }
    }

    [Test]
    public async Task Satellite_Exceptions_Keep_Their_Domain_Base_Classes()
    {
        var expectedNonCoreExceptions = new Dictionary<Type, Type>
        {
            [typeof(KevlarProxyException)] = typeof(Exception),
            [typeof(ChaosInjectedException)] = typeof(Exception),
            [typeof(ShieldAssertionException)] = typeof(Exception),
        };
        var actualNonCoreExceptions = GetPublicExceptionTypes()
            .Where(static type => !typeof(KevlarException).IsAssignableFrom(type))
            .ToDictionary(static type => type, static type => type.BaseType!);

        await Assert.That(actualNonCoreExceptions).IsEquivalentTo(expectedNonCoreExceptions);
    }

    [Test]
    public async Task Exceptions_Are_Sealed_Or_Abstract()
    {
        foreach (var exceptionType in GetPublicExceptionTypes())
        {
            await Assert.That(exceptionType.IsSealed || exceptionType.IsAbstract).IsTrue();
        }
    }

    [Test]
    public async Task CircuitOpenException_Preserves_Isolation_And_Last_Failure()
    {
        var lastException = new InvalidOperationException("last failure");
        var open = new CircuitOpenException(
            TimeSpan.FromSeconds(2),
            isIsolated: false,
            lastException: null);
        var isolated = new CircuitOpenException(
            retryAfter: null,
            isIsolated: true,
            lastException);

        await Assert.That(open.InnerException).IsNull();
        await Assert.That(open.IsIsolated).IsFalse();
        await Assert.That(isolated.IsIsolated).IsTrue();
        await Assert.That(ReferenceEquals(isolated.InnerException, lastException)).IsTrue();
    }

    [Test]
    public async Task TimeoutExceededException_Preserves_Triggering_Cancellation()
    {
        var cancellation = new OperationCanceledException("timed out");
        var exception = new TimeoutExceededException(TimeSpan.FromSeconds(3), cancellation);

        await Assert.That(ReferenceEquals(exception.InnerException, cancellation)).IsTrue();
        await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task Default_Exception_Messages_Are_Stable()
    {
        var expectedMessages = new Dictionary<Exception, string>
        {
            [new CircuitOpenException()] = "The circuit is open and is rejecting executions.",
            [new RateLimitExceededException()] = "The rate limit has been exceeded.",
            [new ConcurrencyLimitExceededException()] =
                "The concurrency limit's concurrency and queue limits are both full.",
            [new TimeoutExceededException()] = "The execution exceeded its allotted timeout.",
            [new ChaosInjectedException()] = "A fault was injected by Kevlar.Chaos.",
            [new HttpRequestReplayException()] = "The HTTP request could not be replayed safely.",
            [new ShieldAssertionException()] = "A shield assertion failed.",
        };

        foreach (var (exception, expectedMessage) in expectedMessages)
        {
            await Assert.That(exception.Message).IsEqualTo(expectedMessage);
        }
    }

    private static ExecutionRejectedException CatchRejection(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (ExecutionRejectedException rejection)
        {
            return rejection;
        }
    }

    private static Type[] GetPublicExceptionTypes() => ShippedAssemblies
        .SelectMany(static assembly => assembly.ExportedTypes)
        .Where(static type => typeof(Exception).IsAssignableFrom(type))
        .OrderBy(static type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    private sealed class TestExecutionRejectedException()
        : ExecutionRejectedException("Test rejection.");
}
