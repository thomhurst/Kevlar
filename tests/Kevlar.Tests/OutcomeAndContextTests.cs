namespace Kevlar.Tests;

public class OutcomeAndContextTests
{
    private const string LegacyExceptionProxyDataKey =
        "Kevlar.Internal.ExceptionProxy.6b21d876-5f0c-45d4-a873-cd6d83e9158b";

    [Test]
    public async Task FromResult_Is_Success()
    {
        var outcome = Outcome<int>.FromResult(42);

        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(42);
        await Assert.That(outcome.Exception).IsNull();
        await Assert.That(outcome.ToString()).IsEqualTo("42");
    }

    [Test]
    public async Task FromResult_Formats_Null_And_Custom_Values()
    {
        var nullOutcome = Outcome<string?>.FromResult(null);
        var customOutcome = Outcome<CustomValue>.FromResult(new CustomValue());

        await Assert.That(nullOutcome.IsSuccess).IsTrue();
        await Assert.That(nullOutcome.Result).IsNull();
        await Assert.That(nullOutcome.ToString()).IsEqualTo(string.Empty);
        await Assert.That(customOutcome.ToString()).IsEqualTo("custom-value");
    }

    [Test]
    public async Task FromException_Preserves_Exception_Reference()
    {
        var exception = new InvalidOperationException("boom");
        var outcome = Outcome<int>.FromException(exception);

        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task Exception_Access_Does_Not_Materialize_Exception_Data()
    {
        var exception = new InvalidOperationException("boom");
        var outcome = Outcome<int>.FromException(exception);

        for (var access = 0; access < 100; access++)
        {
            _ = outcome.Exception;
        }

        var dataField = typeof(Exception).GetField(
            "_data",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        await Assert.That(dataField).IsNotNull();
        await Assert.That(dataField!.GetValue(exception)).IsNull();
        await Assert.That(exception.Data.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FromException_Rejects_Null()
    {
        await Assert.That(() => Outcome<int>.FromException(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetResultOrRethrow_Throws_The_Original_Exception_Instance()
    {
        var original = CaptureOriginalException();
        var outcome = Outcome<int>.FromException(original);

        try
        {
            outcome.GetResultOrRethrow();
            throw new Exception("should not be reached");
        }
        catch (InvalidOperationException caught)
        {
            await Assert.That(ReferenceEquals(caught, original)).IsTrue();
            await Assert.That(caught.StackTrace!.Contains(nameof(ThrowOriginal))).IsTrue();
        }
    }

    [Test]
    public async Task TryGetResult_Returns_Value_For_Successful_Outcomes()
    {
        var valueOutcome = Outcome<int>.FromResult(42);
        var referenceOutcome = Outcome<string>.FromResult("value");

        await Assert.That(valueOutcome.TryGetResult(out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(42);
        await Assert.That(referenceOutcome.TryGetResult(out var reference)).IsTrue();
        await Assert.That(reference).IsEqualTo("value");
        await Assert.That(GetLength(referenceOutcome)).IsEqualTo(5);
    }

    [Test]
    public async Task TryGetResult_Returns_Default_And_Preserves_Exception_For_Failures()
    {
        var exception = new InvalidOperationException("boom");
        var valueOutcome = Outcome<int>.FromException(exception);
        var referenceOutcome = Outcome<string>.FromException(exception);

        await Assert.That(valueOutcome.TryGetResult(out var value)).IsFalse();
        await Assert.That(value).IsEqualTo(default);
        await Assert.That(valueOutcome.Exception).IsSameReferenceAs(exception);
        await Assert.That(referenceOutcome.TryGetResult(out var reference)).IsFalse();
        await Assert.That(reference).IsNull();
        await Assert.That(referenceOutcome.Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task TryGetResult_Preserves_Proxy_Exception_Unwrapping()
    {
        var original = new InvalidOperationException("original");
        var proxy = new TestProxyException(original);
        var outcome = Outcome<int>.FromException(proxy);

        await Assert.That(outcome.TryGetResult(out var result)).IsFalse();
        await Assert.That(result).IsEqualTo(default);
        await Assert.That(outcome.Exception).IsSameReferenceAs(original);
    }

    [Test]
    public async Task Proxy_Exception_Unwraps_To_Original()
    {
        var original = CaptureOriginalException();
        var outcome = Outcome<int>.FromException(new TestProxyException(original));

        var actual = await Assert.That(() => outcome.GetResultOrRethrow())
            .Throws<InvalidOperationException>();

        await Assert.That(outcome.Exception).IsSameReferenceAs(original);
        await Assert.That(actual).IsSameReferenceAs(original);
        await Assert.That(actual!.StackTrace).Contains(nameof(ThrowOriginal));
    }

    [Test]
    public async Task Hedging_Preserves_Proxy_Exception_For_Outer_Strategies()
    {
        var original = new InvalidOperationException("original");
        var observer = new ProxyObserverStrategy();
        var shield = Shield
            .Use(observer)
            .Hedge(1, TimeSpan.Zero);

        var actual = await Assert.That(async () => await shield.ExecuteAsync<int>(
                _ => throw new TestProxyException(original)))
            .Throws<TestProxyException>();

        await Assert.That(observer.SawProxy).IsTrue();
        await Assert.That(actual!.OriginalException).IsSameReferenceAs(original);
    }

    [Test]
    public async Task Legacy_Grpc_Proxy_Exception_Unwraps_To_Original()
    {
        var original = new InvalidOperationException("original");
        var proxy = new AttemptFailureException(original);
        var outcome = Outcome<int>.FromException(proxy);

        await Assert.That(outcome.Exception).IsSameReferenceAs(original);
    }

    [Test]
    public async Task Proxy_Exception_Rejects_Null_Original()
    {
        var exception = await Assert.That(() => new TestProxyException(null!))
            .Throws<ArgumentNullException>();

        await Assert.That(exception!.ParamName).IsEqualTo("originalException");
    }

    [Test]
    public async Task Judge_Sees_Unwrapped_Proxy_Exception()
    {
        var attempts = 0;
        var shield = Shield.When<HttpRequestException>().Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            if (++attempts == 1)
            {
                throw new TestProxyException(new HttpRequestException("transient"));
            }

            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Default_Outcome_Is_A_Success_With_The_Default_Value()
    {
        var referenceOutcome = default(Outcome<string>);
        var valueOutcome = default(Outcome<int>);

        await Assert.That(referenceOutcome.IsSuccess).IsTrue();
        await Assert.That(referenceOutcome.Result).IsNull();
        await Assert.That(referenceOutcome.ToString()).IsEqualTo(string.Empty);
        await Assert.That(valueOutcome.IsSuccess).IsTrue();
        await Assert.That(valueOutcome.Result).IsEqualTo(0);
        await Assert.That(valueOutcome.ToString()).IsEqualTo("0");
    }

    [Test]
    public async Task KevlarKey_Exposes_Its_Name()
    {
        var key = new KevlarKey<int>("attempt-count");

        await Assert.That(key.Name).IsEqualTo("attempt-count");
        await Assert.That(key.ToString()).IsEqualTo("attempt-count");
    }

    [Test]
    public async Task KevlarKey_Rejects_A_Null_Name()
    {
        await Assert.That(() => new KevlarKey<int>(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Properties_Set_And_TryGet_Round_Trip()
    {
        var properties = CapturedProperties();
        var key = new KevlarKey<string>("k");

        properties.Set(key, "value");

        await Assert.That(properties.TryGet(key, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo("value");
    }

    [Test]
    public async Task Properties_Reject_A_Default_Key()
    {
        var properties = CapturedProperties();

        await Assert.That(() => properties.Set(default(KevlarKey<int>), 1)).Throws<InvalidOperationException>();
        await Assert.That(() => properties.TryGet(default(KevlarKey<int>), out _)).Throws<InvalidOperationException>();
        await Assert.That(() => properties.GetOrDefault(default(KevlarKey<int>))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Properties_Set_Overwrites_An_Existing_Value()
    {
        var properties = CapturedProperties();
        var key = new KevlarKey<int>("k");

        properties.Set(key, 1);
        properties.Set(key, 2);

        await Assert.That(properties.GetOrDefault(key)).IsEqualTo(2);
    }

    [Test]
    public async Task Properties_TryGet_Is_False_For_A_Missing_Key()
    {
        var properties = CapturedProperties();

        await Assert.That(properties.TryGet(new KevlarKey<int>("missing"), out _)).IsFalse();
        await Assert.That(properties.GetOrDefault(new KevlarKey<int>("missing"), 9)).IsEqualTo(9);
    }

    [Test]
    public async Task Properties_TryGet_Is_False_When_The_Stored_Type_Differs()
    {
        var properties = CapturedProperties();
        properties.Set(new KevlarKey<string>("k"), "text");

        await Assert.That(properties.TryGet(new KevlarKey<int>("k"), out _)).IsFalse();
    }

    [Test]
    public async Task Properties_Distinguish_Stored_Null_From_Missing()
    {
        var properties = CapturedProperties();
        var stored = new KevlarKey<string?>("stored-null");

        properties.Set(stored, null);

        await Assert.That(properties.TryGet(stored, out var value)).IsTrue();
        await Assert.That(value).IsNull();
        await Assert.That(properties.GetOrDefault(stored, "fallback")).IsNull();
        await Assert.That(properties.TryGet(new KevlarKey<string?>("missing"), out _)).IsFalse();
        await Assert.That(properties.GetOrDefault(new KevlarKey<string?>("missing"), "fallback")).IsEqualTo("fallback");
    }

    [Test]
    public async Task Properties_Use_Name_And_Value_Type_As_Key_Identity()
    {
        var properties = CapturedProperties();
        var text = new KevlarKey<string>("shared");
        var number = new KevlarKey<int>("shared");

        properties.Set(text, "value");
        properties.Set(number, 42);

        await Assert.That(properties.GetOrDefault<string>(new KevlarKey<string>("shared"))).IsEqualTo("value");
        await Assert.That(properties.GetOrDefault(new KevlarKey<int>("shared"))).IsEqualTo(42);
    }

    [Test]
    public async Task Property_Names_Are_Case_Sensitive_And_May_Be_Empty()
    {
        var properties = CapturedProperties();
        properties.Set(new KevlarKey<int>("Key"), 1);
        properties.Set(new KevlarKey<int>("key"), 2);
        properties.Set(new KevlarKey<int>(string.Empty), 3);

        await Assert.That(properties.GetOrDefault(new KevlarKey<int>("Key"))).IsEqualTo(1);
        await Assert.That(properties.GetOrDefault(new KevlarKey<int>("key"))).IsEqualTo(2);
        await Assert.That(properties.GetOrDefault(new KevlarKey<int>(string.Empty))).IsEqualTo(3);
    }

    [Test]
    public async Task Properties_Are_Cleared_Between_Executions()
    {
        // The context is pooled; whatever instance an execution rents must start with
        // empty properties even if a previous execution stored values in it.
        var key = new KevlarKey<string>("leak-check");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                retry.Context.Properties.Set(key, "left-behind");
                return default;
            };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        var leaked = false;
        var probePolicy = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                leaked = retry.Context.Properties.TryGet(key, out _);
                return default;
            };
        });

        await Assert.That(async () => await probePolicy.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(leaked).IsFalse();
    }

    [Test]
    public async Task Completion_Property_Copy_Waits_For_Source_Capture()
    {
        using var blocker = new BlockingEquality();
        var key = new KevlarKey<BlockingEquality>("completion-copy-lock");
        var baseline = new KevlarProperties();
        var completed = new KevlarProperties();
        baseline.Set(key, blocker);
        completed.Set(key, blocker);
        var source = KevlarContext.Rent(
            CancellationToken.None,
            isSynchronous: false,
            TimeProvider.System,
            shieldName: null);
        var target = KevlarContext.Rent(
            CancellationToken.None,
            isSynchronous: false,
            TimeProvider.System,
            shieldName: null);
        source.Properties.Set(key, blocker);
        var capture = Task.CompletedTask;
        var copy = Task.CompletedTask;

        try
        {
            capture = Task.Run(() => source.CaptureCompletionProperties(completed, baseline));
            blocker.WaitUntilEntered();
            copy = Task.Run(() => source.CopyCompletionPropertiesTo(target));

            await Assert.That(SpinWait.SpinUntil(() => copy.IsCompleted, TimeSpan.FromMilliseconds(250)))
                .IsFalse();

            blocker.Release();
            await Task.WhenAll(capture, copy);
            _ = target.PropertiesForCompletion.TryGet(key, out var copied);
            await Assert.That(copied).IsSameReferenceAs(blocker);
        }
        finally
        {
            blocker.Release();
            await Task.WhenAll(capture, copy);
            KevlarContext.Return(source);
            KevlarContext.Return(target);
        }
    }

    [Test]
    public async Task Clear_Does_Not_Reset_Shared_Additional_Attempt_Suppression()
    {
        var owner = new KevlarProperties();
        var source = new KevlarProperties();
        var staleSnapshot = new KevlarProperties();
        source.ShareAdditionalAttemptStateWith(owner);
        source.CopyTo(staleSnapshot);
        owner.SuppressAdditionalAttempts = true;

        source.Clear();
        staleSnapshot.CopyTo(source);

        await Assert.That(owner.SuppressAdditionalAttempts).IsTrue();
        await Assert.That(source.SuppressAdditionalAttempts).IsTrue();
    }

    [Test]
    public async Task Retry_Terminal_Inspection_Does_Not_Alias_Strategy_Indices()
    {
        var context = KevlarContext.Rent(
            CancellationToken.None,
            isSynchronous: false,
            TimeProvider.System,
            shieldName: null);

        try
        {
            context.RequestRetryTerminalInspection(64);

            await Assert.That(context.IsRetryTerminalInspectionRequested(0)).IsFalse();
            await Assert.That(context.IsRetryTerminalInspectionRequested(64)).IsTrue();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    [Test]
    public async Task Context_Reports_Synchronous_And_Asynchronous_Executions()
    {
        bool? sawSynchronous = null;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                sawSynchronous = retry.Context.IsSynchronous;
                return default;
            };
        });

        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(sawSynchronous!.Value).IsTrue();

        sawSynchronous = null;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(sawSynchronous!.Value).IsFalse();
    }

    // The internal constructor is reachable via InternalsVisibleTo.
    private static KevlarProperties CapturedProperties() => new();

    private static InvalidOperationException CaptureOriginalException()
    {
        try
        {
            ThrowOriginal();
            throw new InvalidOperationException("Unreachable.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static void ThrowOriginal() => throw new InvalidOperationException("boom");

    private static int GetLength(Outcome<string> outcome) =>
        outcome.TryGetResult(out var result) ? result.Length : -1;

    private sealed class CustomValue
    {
        public override string ToString() => "custom-value";
    }

    private sealed class BlockingEquality : IEquatable<BlockingEquality>, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();

        public bool Equals(BlockingEquality? other)
        {
            _entered.Set();
            _release.Wait();
            return ReferenceEquals(this, other);
        }

        public override bool Equals(object? obj) => Equals(obj as BlockingEquality);

        public override int GetHashCode() => 0;

        public void WaitUntilEntered()
        {
            if (!_entered.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Completion-property capture did not reach equality comparison.");
            }
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class AttemptFailureException : Exception
    {
        public AttemptFailureException(Exception originalException)
            : base(originalException.Message, originalException)
        {
            Data[LegacyExceptionProxyDataKey] = originalException;
        }
    }

    private sealed class ProxyObserverStrategy : Strategy
    {
        public bool SawProxy { get; private set; }

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var outcome = await next.InvokeAsync(context);
            try
            {
                _ = outcome.GetResultOrRethrowInternal();
            }
            catch (TestProxyException)
            {
                SawProxy = true;
            }

            return outcome;
        }
    }
}

internal sealed class TestProxyException(Exception originalException)
    : KevlarProxyException(originalException);
