using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Kevlar.Tests;

public class ExecutionOverloadContractTests
{
    private static readonly ExecutionCase[] Cases =
    [
        new("Shield.ExecuteAsync:ValueTaskResult", true, false, InvokeShieldValueTaskResult),
        new("Shield.ExecuteAsync:ValueTaskResult:state", true, true, InvokeShieldValueTaskResultState),
        new("Shield.ExecuteAsync:ValueTaskVoid", true, false, InvokeShieldValueTaskVoid),
        new("Shield.ExecuteAsync:ValueTaskVoid:state", true, true, InvokeShieldValueTaskVoidState),
        new("Shield.ExecuteWithContextAsync:ValueTaskResult:state", true, true, InvokeShieldContextValueTaskResultState),
        new("Shield.ExecuteWithContextAsync:ValueTaskVoid:state", true, true, InvokeShieldContextValueTaskVoidState),
        new("Shield.ExecuteWithContextAsync:ValueTaskResult", true, false, InvokeShieldContextValueTaskResult),
        new("Shield.ExecuteWithContextAsync:ValueTaskVoid", true, false, InvokeShieldContextValueTaskVoid),
        new("Shield.ExecuteOutcomeAsync:ValueTaskResult", true, false, InvokeShieldValueTaskOutcome, CapturesOutcome: true),
        new("Shield.ExecuteOutcomeAsync:ValueTaskResult:state", true, true, InvokeShieldValueTaskOutcomeState, CapturesOutcome: true),
        new("Shield.Execute:SyncResult", false, false, InvokeShieldSyncResult),
        new("Shield.Execute:SyncResult:state", false, true, InvokeShieldSyncResultState),
        new("Shield.Execute:SyncVoid", false, false, InvokeShieldSyncVoid),
        new("Shield.Execute:SyncVoid:state", false, true, InvokeShieldSyncVoidState),
        new("Shield.ExecuteWithContext:SyncResult:state", false, true, InvokeShieldContextSyncResultState),
        new("Shield.ExecuteWithContext:SyncVoid:state", false, true, InvokeShieldContextSyncVoidState),
        new("Shield.ExecuteWithContext:SyncResult", false, false, InvokeShieldContextSyncResult),
        new("Shield.ExecuteWithContext:SyncVoid", false, false, InvokeShieldContextSyncVoid),

        new("Shield<TResult>.ExecuteAsync:ValueTaskResult", true, false, InvokeTypedValueTaskResult),
        new("Shield<TResult>.ExecuteAsync:ValueTaskResult:state", true, true, InvokeTypedValueTaskResultState),
        new("Shield<TResult>.ExecuteWithContextAsync:ValueTaskResult:state", true, true, InvokeTypedContextValueTaskResultState),
        new("Shield<TResult>.ExecuteWithContextAsync:ValueTaskResult", true, false, InvokeTypedContextValueTaskResult),
        new("Shield<TResult>.ExecuteOutcomeAsync:ValueTaskResult", true, false, InvokeTypedValueTaskOutcome, CapturesOutcome: true),
        new("Shield<TResult>.ExecuteOutcomeAsync:ValueTaskResult:state", true, true, InvokeTypedValueTaskOutcomeState, CapturesOutcome: true),
        new("Shield<TResult>.Execute:SyncResult", false, false, InvokeTypedSyncResult),
        new("Shield<TResult>.Execute:SyncResult:state", false, true, InvokeTypedSyncResultState),
        new("Shield<TResult>.ExecuteWithContext:SyncResult:state", false, true, InvokeTypedContextSyncResultState),
        new("Shield<TResult>.ExecuteWithContext:SyncResult", false, false, InvokeTypedContextSyncResult),

        new("VoidShield.ExecuteAsync:ValueTaskVoid", true, false, InvokeVoidShieldValueTaskVoid),
        new("VoidShield.ExecuteAsync:ValueTaskVoid:state", true, true, InvokeVoidShieldValueTaskVoidState),
        new("VoidShield.ExecuteWithContextAsync:ValueTaskVoid:state", true, true, InvokeVoidShieldContextValueTaskVoidState),
        new("VoidShield.ExecuteWithContextAsync:ValueTaskVoid", true, false, InvokeVoidShieldContextValueTaskVoid),
        new("VoidShield.Execute:SyncVoid", false, false, InvokeVoidShieldSyncVoid),
        new("VoidShield.Execute:SyncVoid:state", false, true, InvokeVoidShieldSyncVoidState),
        new("VoidShield.ExecuteWithContext:SyncVoid:state", false, true, InvokeVoidShieldContextSyncVoidState),
        new("VoidShield.ExecuteWithContext:SyncVoid", false, false, InvokeVoidShieldContextSyncVoid),

        new("ShieldTaskExtensions[Shield].ExecuteAsync:TaskResult", true, false, InvokeShieldTaskResult),
        new("ShieldTaskExtensions[Shield].ExecuteAsync:TaskResult:state", true, true, InvokeShieldTaskResultState),
        new("ShieldTaskExtensions[Shield].ExecuteAsync:TaskVoid", true, false, InvokeShieldTaskVoid),
        new("ShieldTaskExtensions[Shield].ExecuteAsync:TaskVoid:state", true, true, InvokeShieldTaskVoidState),
        new("ShieldTaskExtensions[Shield].ExecuteWithContextAsync:TaskResult:state", true, true, InvokeShieldContextTaskResultState),
        new("ShieldTaskExtensions[Shield].ExecuteWithContextAsync:TaskVoid:state", true, true, InvokeShieldContextTaskVoidState),
        new("ShieldTaskExtensions[Shield].ExecuteWithContextAsync:TaskResult", true, false, InvokeShieldContextTaskResult),
        new("ShieldTaskExtensions[Shield].ExecuteWithContextAsync:TaskVoid", true, false, InvokeShieldContextTaskVoid),
        new("ShieldTaskExtensions[Shield].ExecuteOutcomeAsync:TaskResult", true, false, InvokeShieldTaskOutcome, CapturesOutcome: true),
        new("ShieldTaskExtensions[Shield].ExecuteOutcomeAsync:TaskResult:state", true, true, InvokeShieldTaskOutcomeState, CapturesOutcome: true),

        new("ShieldTaskExtensions[Shield<TResult>].ExecuteAsync:TaskResult", true, false, InvokeTypedTaskResult),
        new("ShieldTaskExtensions[Shield<TResult>].ExecuteAsync:TaskResult:state", true, true, InvokeTypedTaskResultState),
        new("ShieldTaskExtensions[Shield<TResult>].ExecuteWithContextAsync:TaskResult:state", true, true, InvokeTypedContextTaskResultState),
        new("ShieldTaskExtensions[Shield<TResult>].ExecuteWithContextAsync:TaskResult", true, false, InvokeTypedContextTaskResult),
        new("ShieldTaskExtensions[Shield<TResult>].ExecuteOutcomeAsync:TaskResult", true, false, InvokeTypedTaskOutcome, CapturesOutcome: true),
        new("ShieldTaskExtensions[Shield<TResult>].ExecuteOutcomeAsync:TaskResult:state", true, true, InvokeTypedTaskOutcomeState, CapturesOutcome: true),

        new("ShieldTaskExtensions[VoidShield].ExecuteAsync:TaskVoid", true, false, InvokeVoidShieldTaskVoid),
        new("ShieldTaskExtensions[VoidShield].ExecuteAsync:TaskVoid:state", true, true, InvokeVoidShieldTaskVoidState),
        new("ShieldTaskExtensions[VoidShield].ExecuteWithContextAsync:TaskVoid:state", true, true, InvokeVoidShieldContextTaskVoidState),
        new("ShieldTaskExtensions[VoidShield].ExecuteWithContextAsync:TaskVoid", true, false, InvokeVoidShieldContextTaskVoid),
    ];

    [Test]
    public async Task Every_Public_Execution_Overload_Is_In_The_Contract_Catalog()
    {
        var reflected = GetExecutionMethods().Select(Describe).Order().ToArray();
        var catalogued = Cases.Select(executionCase => executionCase.Name).Order().ToArray();

        await Assert.That(reflected).IsEquivalentTo(
            catalogued,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Every_Overload_Has_Empty_And_NonEmpty_Success_Parity()
    {
        foreach (var executionCase in Cases)
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.SynchronousSuccess);
                var result = await Run(executionCase, pipelined, probe);

                Require(result.Exception is null, executionCase, pipelined, "success returned an exception");
                Require(result.Value == ExecutionProbe.ExpectedResult, executionCase, pipelined, "result changed");
                Require(probe.InvocationCount == 1, executionCase, pipelined, "delegate was not invoked exactly once");
                Require(probe.SeenToken == probe.Cancellation.Token, executionCase, pipelined, "caller token identity changed");
                Require(!executionCase.UsesState || probe.SawExactState, executionCase, pipelined, "state identity changed");
            }
        }
    }

    [Test]
    public async Task Async_Overloads_Preserve_Deferred_Completion()
    {
        foreach (var executionCase in Cases.Where(static executionCase => executionCase.IsAsync))
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.WaitForRelease);
                var execution = executionCase.Invoke(pipelined, probe, probe.Cancellation.Token).AsTask();

                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!execution.IsCompleted, executionCase, pipelined, "execution completed before release");
                probe.Release.TrySetResult();

                var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Require(result.Exception is null, executionCase, pipelined, "deferred success returned an exception");
                Require(result.Value == ExecutionProbe.ExpectedResult, executionCase, pipelined, "deferred result changed");
                Require(probe.InvocationCount == 1, executionCase, pipelined, "deferred delegate was not invoked exactly once");
            }
        }
    }

    [Test]
    public async Task Every_Overload_Preserves_Synchronous_Exceptions()
    {
        foreach (var executionCase in Cases)
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.SynchronousThrow);
                var exception = await CaptureFailure(executionCase, pipelined, probe);

                Require(ReferenceEquals(exception, probe.Failure), executionCase, pipelined, "exception instance changed");
                Require(exception.StackTrace?.Contains(nameof(ExecutionProbe.ThrowOriginal), StringComparison.Ordinal) == true,
                    executionCase,
                    pipelined,
                    "original exception stack was lost");
                Require(probe.InvocationCount == 1, executionCase, pipelined, "throwing delegate was not invoked exactly once");
            }
        }
    }

    [Test]
    public async Task Async_Overloads_Preserve_Faulted_Task_And_ValueTask_Exceptions()
    {
        foreach (var executionCase in Cases.Where(static executionCase => executionCase.IsAsync))
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.Faulted);
                var exception = await CaptureFailure(executionCase, pipelined, probe);

                Require(ReferenceEquals(exception, probe.Failure), executionCase, pipelined, "faulted exception instance changed");
                Require(exception.StackTrace?.Contains(nameof(ExecutionProbe.ThrowOriginal), StringComparison.Ordinal) == true,
                    executionCase,
                    pipelined,
                    "faulted exception stack was lost");
                Require(probe.InvocationCount == 1, executionCase, pipelined, "faulted delegate was not invoked exactly once");
            }
        }
    }

    [Test]
    public async Task PreCancellation_Skips_Every_Delegate_And_Preserves_The_Caller_Token()
    {
        foreach (var executionCase in Cases)
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.SynchronousSuccess);
                probe.Cancellation.Cancel();

                var exception = await CaptureFailure(executionCase, pipelined, probe);
                var cancellation = exception as OperationCanceledException;

                Require(cancellation is not null, executionCase, pipelined, "pre-cancellation returned the wrong exception type");
                Require(cancellation!.CancellationToken == probe.Cancellation.Token,
                    executionCase,
                    pipelined,
                    "pre-cancellation token identity changed");
                Require(probe.InvocationCount == 0, executionCase, pipelined, "pre-cancelled delegate was invoked");
            }
        }
    }

    [Test]
    public async Task Outcome_State_Reaches_Every_Retry_And_Hedge_Attempt()
    {
        Shield[] shields =
        [
            Shield.Retry(2, Backoff.None),
            Shield.Hedge(3, System.Threading.Timeout.InfiniteTimeSpan),
        ];

        foreach (var shield in shields)
        {
            using var probe = new ExecutionProbe(ProbeBehavior.SynchronousThrow);

            var outcome = await shield.ExecuteOutcomeAsync(
                probe.State,
                static (state, cancellationToken) => state.Probe.ValueTaskResultState(state, cancellationToken));

            await Assert.That(ReferenceEquals(outcome.Exception, probe.Failure)).IsTrue();
            await Assert.That(probe.InvocationCount).IsEqualTo(3);
            await Assert.That(probe.SawExactState).IsTrue();
        }
    }

    [Test]
    public async Task MidFlight_Cancellation_Preserves_The_Caller_Token()
    {
        foreach (var executionCase in Cases)
        {
            foreach (var pipelined in PipelineModes)
            {
                using var probe = new ExecutionProbe(ProbeBehavior.WaitForRelease);
                var execution = Task.Run(async () =>
                    await CaptureFailure(executionCase, pipelined, probe));

                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                probe.Cancellation.Cancel();
                probe.Release.TrySetResult();

                var exception = await execution.WaitAsync(TimeSpan.FromSeconds(5));
                var cancellation = exception as OperationCanceledException;

                Require(cancellation is not null, executionCase, pipelined, "mid-flight cancellation returned the wrong exception type");
                Require(cancellation!.CancellationToken == probe.Cancellation.Token,
                    executionCase,
                    pipelined,
                    "mid-flight cancellation token identity changed");
                Require(probe.InvocationCount == 1, executionCase, pipelined, "mid-flight delegate was not invoked exactly once");
            }
        }
    }

    [Test]
    public async Task Every_Action_And_Extension_Receiver_Has_An_Exact_Null_Guard()
    {
        foreach (var method in GetExecutionMethods())
        {
            var closedMethod = CloseGenericMethod(method);
            var target = closedMethod.IsStatic ? null : CreateShield(closedMethod.DeclaringType!);
            var arguments = CreateArguments(closedMethod, nullReceiver: false);
            var actionGuard = CaptureReflectionFailure(() => closedMethod.Invoke(target, arguments));

            await Assert.That(actionGuard).IsTypeOf<ArgumentNullException>();
            await Assert.That(((ArgumentNullException)actionGuard).ParamName).IsEqualTo("action");

            if (!closedMethod.IsStatic)
            {
                continue;
            }

            arguments = CreateArguments(closedMethod, nullReceiver: true);
            var receiverGuard = CaptureReflectionFailure(() => closedMethod.Invoke(null, arguments));
            await Assert.That(receiverGuard).IsTypeOf<ArgumentNullException>();
            await Assert.That(((ArgumentNullException)receiverGuard).ParamName).IsEqualTo("shield");
        }
    }

    private static IEnumerable<MethodInfo> GetExecutionMethods() =>
        typeof(Shield).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Concat(typeof(Shield<int>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Concat(typeof(VoidShield).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Concat(typeof(ShieldTaskExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(static method => method.Name is "Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"
                or "ExecuteWithContext" or "ExecuteWithContextAsync");

    private static string Describe(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var actionIndex = Array.FindIndex(parameters, static parameter => parameter.Name == "action");
        var actionReturn = parameters[actionIndex].ParameterType.GetMethod("Invoke")!.ReturnType;
        var receiverOffset = method.IsStatic ? 1 : 0;
        var hasState = actionIndex > receiverOffset;
        var owner = method.IsStatic
            ? $"ShieldTaskExtensions[{DescribeShield(parameters[0].ParameterType)}]"
            : DescribeShield(method.DeclaringType!);

        return $"{owner}.{method.Name}:{DescribeActionReturn(actionReturn)}{(hasState ? ":state" : string.Empty)}";
    }

    private static string DescribeShield(Type type) => type == typeof(VoidShield)
        ? "VoidShield"
        : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Shield<>)
            ? "Shield<TResult>"
            : "Shield";

    private static string DescribeActionReturn(Type type)
    {
        if (type == typeof(void))
        {
            return "SyncVoid";
        }

        if (type == typeof(Task))
        {
            return "TaskVoid";
        }

        if (type == typeof(ValueTask))
        {
            return "ValueTaskVoid";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return "TaskResult";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return "ValueTaskResult";
        }

        return "SyncResult";
    }

    private static MethodInfo CloseGenericMethod(MethodInfo method) => method.IsGenericMethodDefinition
        ? method.MakeGenericMethod(Enumerable.Repeat(typeof(int), method.GetGenericArguments().Length).ToArray())
        : method;

    private static object CreateShield(Type type) => type == typeof(Shield<int>)
        ? Shield<int>.Empty
        : type == typeof(VoidShield)
            ? Shield.Fallback(static _ => ValueTask.CompletedTask)
            : Shield.Empty;

    private static object?[] CreateArguments(MethodInfo method, bool nullReceiver)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            if (method.IsStatic && index == 0)
            {
                arguments[index] = nullReceiver ? null : CreateShield(parameterType);
            }
            else if (parameters[index].Name == "initializeProperties")
            {
                arguments[index] = (Action<int, KevlarProperties>)InitializeProperties;
            }
            else if (typeof(Delegate).IsAssignableFrom(parameterType))
            {
                arguments[index] = null;
            }
            else if (parameterType == typeof(CancellationToken))
            {
                arguments[index] = default(CancellationToken);
            }
            else
            {
                arguments[index] = Activator.CreateInstance(parameterType);
            }
        }

        return arguments;
    }

    private static Exception CaptureReflectionFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected a null guard failure.");
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException!;
        }
    }

    private static void InitializeProperties(int _, KevlarProperties __)
    {
    }

    private static async ValueTask<InvocationResult> Run(
        ExecutionCase executionCase,
        bool pipelined,
        ExecutionProbe probe) =>
        await executionCase.Invoke(pipelined, probe, probe.Cancellation.Token);

    private static async Task<Exception> CaptureFailure(
        ExecutionCase executionCase,
        bool pipelined,
        ExecutionProbe probe)
    {
        try
        {
            var result = await Run(executionCase, pipelined, probe);
            if (executionCase.CapturesOutcome && result.Exception is not null)
            {
                return result.Exception;
            }

            throw new InvalidOperationException($"{executionCase.Name} ({DescribePipeline(pipelined)}) did not fail.");
        }
        catch (Exception exception) when (!executionCase.CapturesOutcome)
        {
            return exception;
        }
    }

    private static void Require(bool condition, ExecutionCase executionCase, bool pipelined, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{executionCase.Name} ({DescribePipeline(pipelined)}): {message}.");
        }
    }

    private static string DescribePipeline(bool pipelined) => pipelined ? "non-empty" : "empty";

    private static Shield GetShield(bool pipelined) => pipelined
        ? Shield.Retry(0, Backoff.None)
        : Shield.Empty;

    private static Shield<int> GetTypedShield(bool pipelined) => pipelined
        ? Shield.For<int>().Retry(0, Backoff.None)
        : Shield<int>.Empty;

    private static VoidShield GetVoidShield(bool pipelined) => pipelined
        ? Shield.When<UnusedFallbackException>().Fallback(static (_, _) => ValueTask.CompletedTask).Retry(0, Backoff.None)
        : Shield.When<UnusedFallbackException>().Fallback(static (_, _) => ValueTask.CompletedTask);

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteAsync(probe.ValueTaskResult, token));

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteAsync(probe.ValueTaskVoid, token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskVoidState(state, cancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldValueTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteAsync(probe.ValueTaskVoid, token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldValueTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskVoidState(state, cancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskOutcome(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetShield(pipelined).ExecuteOutcomeAsync(probe.ValueTaskResult, token));

    private static async ValueTask<InvocationResult> InvokeShieldValueTaskOutcomeState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetShield(pipelined).ExecuteOutcomeAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextValueTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.ValueTaskResultState(state, context.CancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextValueTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.ValueTaskVoidState(state, context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeShieldContextValueTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteWithContextAsync(
            context => probe.ValueTaskResult(context.CancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextValueTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteWithContextAsync(
            context => probe.ValueTaskVoid(context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldContextValueTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.ValueTaskVoidState(state, context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldContextValueTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteWithContextAsync(
            context => probe.ValueTaskVoid(context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static ValueTask<InvocationResult> InvokeShieldContextSyncResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetShield(pipelined).ExecuteWithContext(
            context => probe.SyncResult(context.CancellationToken),
            token)));

    private static ValueTask<InvocationResult> InvokeShieldContextSyncVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetShield(pipelined).ExecuteWithContext(
            context => probe.SyncVoid(context.CancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeVoidShieldContextSyncVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetVoidShield(pipelined).ExecuteWithContext(
            context => probe.SyncVoid(context.CancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static async ValueTask<InvocationResult> InvokeTypedContextValueTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteWithContextAsync(
            context => probe.ValueTaskResult(context.CancellationToken),
            token));

    private static ValueTask<InvocationResult> InvokeTypedContextSyncResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetTypedShield(pipelined).ExecuteWithContext(
            context => probe.SyncResult(context.CancellationToken),
            token)));

    private static async ValueTask<InvocationResult> InvokeShieldContextTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteWithContextAsync(
            context => probe.TaskResult(context.CancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteWithContextAsync(
            context => probe.TaskVoid(context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeTypedContextTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteWithContextAsync(
            context => probe.TaskResult(context.CancellationToken),
            token));

    private static ValueTask<InvocationResult> InvokeShieldSyncResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetShield(pipelined).Execute(probe.SyncResult, token)));

    private static ValueTask<InvocationResult> InvokeShieldSyncResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetShield(pipelined).Execute(
            probe.State,
            static (state, cancellationToken) => state.Probe.SyncResultState(state, cancellationToken),
            token)));

    private static ValueTask<InvocationResult> InvokeShieldSyncVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetShield(pipelined).Execute(probe.SyncVoid, token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeShieldSyncVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetShield(pipelined).Execute(
            probe.State,
            static (state, cancellationToken) => state.Probe.SyncVoidState(state, cancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeVoidShieldSyncVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetVoidShield(pipelined).Execute(probe.SyncVoid, token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeVoidShieldSyncVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetVoidShield(pipelined).Execute(
            probe.State,
            static (state, cancellationToken) => state.Probe.SyncVoidState(state, cancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeShieldContextSyncResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetShield(pipelined).ExecuteWithContext(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.SyncResultState(state, context.CancellationToken),
            token)));

    private static ValueTask<InvocationResult> InvokeShieldContextSyncVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetShield(pipelined).ExecuteWithContext(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.SyncVoidState(state, context.CancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static ValueTask<InvocationResult> InvokeVoidShieldContextSyncVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        GetVoidShield(pipelined).ExecuteWithContext(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.SyncVoidState(state, context.CancellationToken),
            token);
        return new ValueTask<InvocationResult>(InvocationResult.Success(ExecutionProbe.ExpectedResult));
    }

    private static async ValueTask<InvocationResult> InvokeTypedValueTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteAsync(probe.ValueTaskResult, token));

    private static async ValueTask<InvocationResult> InvokeTypedValueTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeTypedValueTaskOutcome(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetTypedShield(pipelined).ExecuteOutcomeAsync(probe.ValueTaskResult, token));

    private static async ValueTask<InvocationResult> InvokeTypedValueTaskOutcomeState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetTypedShield(pipelined).ExecuteOutcomeAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.ValueTaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeTypedContextValueTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.ValueTaskResultState(state, context.CancellationToken),
            token));

    private static ValueTask<InvocationResult> InvokeTypedSyncResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetTypedShield(pipelined).Execute(probe.SyncResult, token)));

    private static ValueTask<InvocationResult> InvokeTypedSyncResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetTypedShield(pipelined).Execute(
            probe.State,
            static (state, cancellationToken) => state.Probe.SyncResultState(state, cancellationToken),
            token)));

    private static ValueTask<InvocationResult> InvokeTypedContextSyncResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        new(InvocationResult.Success(GetTypedShield(pipelined).ExecuteWithContext(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.SyncResultState(state, context.CancellationToken),
            token)));

    private static async ValueTask<InvocationResult> InvokeShieldTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteAsync(probe.TaskResult, token));

    private static async ValueTask<InvocationResult> InvokeShieldTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteAsync(probe.TaskVoid, token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeShieldTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskVoidState(state, cancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteAsync(probe.TaskVoid, token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskVoidState(state, cancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeShieldTaskOutcome(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetShield(pipelined).ExecuteOutcomeAsync(probe.TaskResult, token));

    private static async ValueTask<InvocationResult> InvokeShieldTaskOutcomeState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetShield(pipelined).ExecuteOutcomeAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.TaskResultState(state, context.CancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeShieldContextTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.TaskVoidState(state, context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldContextTaskVoidState(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.TaskVoidState(state, context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeVoidShieldContextTaskVoid(bool pipelined, ExecutionProbe probe, CancellationToken token)
    {
        await GetVoidShield(pipelined).ExecuteWithContextAsync(
            context => probe.TaskVoid(context.CancellationToken),
            token);
        return InvocationResult.Success(ExecutionProbe.ExpectedResult);
    }

    private static async ValueTask<InvocationResult> InvokeTypedTaskResult(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteAsync(probe.TaskResult, token));

    private static async ValueTask<InvocationResult> InvokeTypedTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeTypedTaskOutcome(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetTypedShield(pipelined).ExecuteOutcomeAsync(probe.TaskResult, token));

    private static async ValueTask<InvocationResult> InvokeTypedTaskOutcomeState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.FromOutcome(await GetTypedShield(pipelined).ExecuteOutcomeAsync(
            probe.State,
            static (state, cancellationToken) => state.Probe.TaskResultState(state, cancellationToken),
            token));

    private static async ValueTask<InvocationResult> InvokeTypedContextTaskResultState(bool pipelined, ExecutionProbe probe, CancellationToken token) =>
        InvocationResult.Success(await GetTypedShield(pipelined).ExecuteWithContextAsync(
            probe.State,
            static (_, _) => { },
            static (state, context) => state.Probe.TaskResultState(state, context.CancellationToken),
            token));

    private static readonly bool[] PipelineModes = [false, true];

    private sealed record ExecutionCase(
        string Name,
        bool IsAsync,
        bool UsesState,
        Func<bool, ExecutionProbe, CancellationToken, ValueTask<InvocationResult>> Invoke,
        bool CapturesOutcome = false);

    private sealed class UnusedFallbackException : Exception;

    private readonly record struct InvocationResult(int Value, Exception? Exception)
    {
        public static InvocationResult Success(int value) => new(value, null);

        public static InvocationResult FromOutcome(Outcome<int> outcome) => outcome.IsSuccess
            ? Success(outcome.Result)
            : new InvocationResult(default, outcome.Exception);
    }

    private sealed class ExecutionState(ExecutionProbe probe)
    {
        public ExecutionProbe Probe { get; } = probe;
    }

    private sealed class ExecutionProbe : IDisposable
    {
        private readonly ProbeBehavior _behavior;

        public ExecutionProbe(ProbeBehavior behavior)
        {
            _behavior = behavior;
            State = new ExecutionState(this);
            Failure = CaptureOriginalException();
        }

        public const int ExpectedResult = 42;

        public CancellationTokenSource Cancellation { get; } = new();

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ExecutionState State { get; }

        public InvalidOperationException Failure { get; }

        public int InvocationCount { get; private set; }

        public CancellationToken SeenToken { get; private set; }

        public bool SawExactState { get; private set; } = true;

        public ValueTask<int> ValueTaskResult(CancellationToken token)
        {
            Record(token);
            return _behavior switch
            {
                ProbeBehavior.SynchronousSuccess => new ValueTask<int>(ExpectedResult),
                ProbeBehavior.SynchronousThrow => ThrowValueTaskResult(),
                ProbeBehavior.Faulted => new ValueTask<int>(Task.FromException<int>(Failure)),
                _ => new ValueTask<int>(WaitForReleaseAsync(token)),
            };
        }

        public ValueTask<int> ValueTaskResultState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            return ValueTaskResult(token);
        }

        public ValueTask ValueTaskVoid(CancellationToken token)
        {
            Record(token);
            return _behavior switch
            {
                ProbeBehavior.SynchronousSuccess => default,
                ProbeBehavior.SynchronousThrow => ThrowValueTaskVoid(),
                ProbeBehavior.Faulted => new ValueTask(Task.FromException(Failure)),
                _ => new ValueTask(WaitForReleaseVoidAsync(token)),
            };
        }

        public ValueTask ValueTaskVoidState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            return ValueTaskVoid(token);
        }

        public Task<int> TaskResult(CancellationToken token)
        {
            Record(token);
            return _behavior switch
            {
                ProbeBehavior.SynchronousSuccess => Task.FromResult(ExpectedResult),
                ProbeBehavior.SynchronousThrow => ThrowTaskResult(),
                ProbeBehavior.Faulted => Task.FromException<int>(Failure),
                _ => WaitForReleaseAsync(token),
            };
        }

        public Task<int> TaskResultState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            return TaskResult(token);
        }

        public Task TaskVoid(CancellationToken token)
        {
            Record(token);
            return _behavior switch
            {
                ProbeBehavior.SynchronousSuccess => Task.CompletedTask,
                ProbeBehavior.SynchronousThrow => ThrowTaskVoid(),
                ProbeBehavior.Faulted => Task.FromException(Failure),
                _ => WaitForReleaseVoidAsync(token),
            };
        }

        public Task TaskVoidState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            return TaskVoid(token);
        }

        public int SyncResult(CancellationToken token)
        {
            Record(token);
            return _behavior switch
            {
                ProbeBehavior.SynchronousSuccess => ExpectedResult,
                ProbeBehavior.SynchronousThrow => ThrowSyncResult(),
                _ => WaitForRelease(token),
            };
        }

        public int SyncResultState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            return SyncResult(token);
        }

        public void SyncVoid(CancellationToken token)
        {
            Record(token);
            switch (_behavior)
            {
                case ProbeBehavior.SynchronousSuccess:
                    return;
                case ProbeBehavior.SynchronousThrow:
                    ThrowFailure();
                    return;
                default:
                    WaitForRelease(token);
                    return;
            }
        }

        public void SyncVoidState(ExecutionState state, CancellationToken token)
        {
            RecordState(state);
            SyncVoid(token);
        }

        public static void ThrowOriginal() => throw new InvalidOperationException("original execution failure");

        public void Dispose() => Cancellation.Dispose();

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

        private async Task<int> WaitForReleaseAsync(CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return ExpectedResult;
        }

        private async Task WaitForReleaseVoidAsync(CancellationToken token)
        {
            _ = await WaitForReleaseAsync(token).ConfigureAwait(false);
        }

        private int WaitForRelease(CancellationToken token)
        {
            Entered.TrySetResult();
            Release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            return ExpectedResult;
        }

        private void Record(CancellationToken token)
        {
            InvocationCount++;
            SeenToken = token;
        }

        private void RecordState(ExecutionState state) =>
            SawExactState &= ReferenceEquals(state, State);

        private void ThrowFailure() => ExceptionDispatchInfo.Capture(Failure).Throw();

        private int ThrowSyncResult()
        {
            ThrowFailure();
            return default;
        }

        private ValueTask<int> ThrowValueTaskResult()
        {
            ThrowFailure();
            return default;
        }

        private ValueTask ThrowValueTaskVoid()
        {
            ThrowFailure();
            return default;
        }

        private Task<int> ThrowTaskResult()
        {
            ThrowFailure();
            return null!;
        }

        private Task ThrowTaskVoid()
        {
            ThrowFailure();
            return null!;
        }
    }

    private enum ProbeBehavior
    {
        SynchronousSuccess,
        WaitForRelease,
        SynchronousThrow,
        Faulted,
    }
}
