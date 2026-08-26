using System.Collections.Immutable;
using Kevlar.Analyzers;
using Kevlar.Chaos;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Kevlar.Analyzers.Tests;

public class PipelineHazardAnalyzerTests
{
    [Test]
    public async Task KEV013_Flags_Async_Work_Assigned_To_Synchronous_Callbacks()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(o => o.OnRetry = async _ => await Task.Yield());",
            "_ = Shield.Retry(o => o.OnRetry ??= async _ => await Task.Yield());",
            "_ = Shield.Timeout(o => o.OnTimeout = async _ => await Task.Yield());",
            "_ = Shield.CircuitBreaker(o => o.OnStateChanged = async _ => await Task.Yield());",
            "_ = Shield.Hedge(o => o.OnHedge = _ => Task.Delay(1));",
            "_ = Shield.Fallback(_ => ValueTask.CompletedTask, o => o.OnFallback = async _ => await Task.Yield());",
            "_ = Shield.RateLimit(o => o.OnRejected = async _ => await Task.Yield());",
            "_ = Shield.ConcurrencyLimit(o => o.OnRejected = async _ => await Task.Yield());",
            "_ = ChaosShield.Latency(o => o.OnInjected = async _ => await Task.Yield());",
            "_ = Shield.Empty.UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!, o => o.OnRejected = async _ => await Task.Yield());",
            "_ = Shield.Retry(o => o.OnRetry = new Action<RetryEvent>(async _ => await Task.Yield()));",
        };

        await AssertEachAsync(cases, "KEV013", "KEV006");
    }

    [Test]
    public async Task KEV014_Flags_Async_Lambda_Event_Use_After_Await()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                _ = item.Context.ShieldName;
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Known_Completed_Awaitables()
    {
        var awaitExpressions = new[]
        {
            "Task.CompletedTask",
            "Task.CompletedTask.ConfigureAwait(false)",
            "Task.FromResult(0)",
            "Task.FromResult(0).ConfigureAwait(false)",
            "Task.FromException(new InvalidOperationException())",
            "Task.FromException(new InvalidOperationException()).ConfigureAwait(false)",
            "Task.FromException<int>(new InvalidOperationException())",
            "Task.FromCanceled(new CancellationToken(canceled: true))",
            "Task.FromCanceled(new CancellationToken(canceled: true)).ConfigureAwait(false)",
            "Task.FromCanceled<int>(new CancellationToken(canceled: true))",
            "Task.Delay(0)",
            "Task.Delay(0).ConfigureAwait(false)",
            "Task.Delay(TimeSpan.Zero)",
            "Task.Delay(0, CancellationToken.None)",
            "Task.Delay(TimeSpan.Zero, CancellationToken.None)",
            "ValueTask.CompletedTask",
            "ValueTask.CompletedTask.ConfigureAwait(false)",
            "ValueTask.FromResult(0)",
            "ValueTask.FromResult(0).ConfigureAwait(false)",
            "new ValueTask()",
            "new ValueTask().ConfigureAwait(false)",
            "new ValueTask<int>(0)",
            "default(ValueTask)",
            "default(ValueTask<int>)",
        };
        foreach (var awaitExpression in awaitExpressions)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = async item =>
                {
                    await {{awaitExpression}};
                    Console.WriteLine(item.Context.ShieldName);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV013");
        }
    }

    [Test]
    public async Task KEV014_Flags_ValueTask_Constructed_From_Pending_Task()
    {
        var awaitExpressions = new[]
        {
            "Task.Delay(1)",
            "new ValueTask(Task.Delay(1))",
            "new ValueTask<int>(Task.Run(() => 1))",
        };
        foreach (var awaitExpression in awaitExpressions)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = async item =>
                {
                    await {{awaitExpression}};
                    Console.WriteLine(item.Context.ShieldName);
                });
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Flags_Unknown_Object_Creation_Awaitables()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        await new PendingAwaitable();
                        Console.WriteLine(item.Context.ShieldName);
                    });

                private readonly struct PendingAwaitable
                {
                    public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() =>
                        Task.Delay(1).GetAwaiter();
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_PostAwait_Local_Function_Calls()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                ReadContext();

                void ReadContext() => Console.WriteLine(item.Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Maps_PostAwait_Local_Function_Parameters()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                Read(item);

                static void Read(RetryEvent value) =>
                    Console.WriteLine(value.Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_PostAwait_Source_Method_Calls()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        _event = item;
                        await Task.Yield();
                        Read();
                    });

                private void Read() => Consume(_event);
                private static void Consume(RetryEvent item) { }
            }
            """);
        var parameterFlow = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        await Task.Yield();
                        Read(item);
                    });

                private static void Read(RetryEvent item)
                {
                    ReadNested(item);
                }

                private static void ReadNested(RetryEvent item) => Consume(item);
                private static void Consume(RetryEvent item) { }
            }
            """);
        var scalarParameter = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        await Task.Yield();
                        Read(42);
                    });

                private static void Read(int retryNumber) => Console.WriteLine(retryNumber);
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(parameterFlow, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(parameterFlow, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(scalarParameter, "KEV013");
    }

    [Test]
    public async Task KEV014_Maps_Retained_Wrappers_To_Source_Parameters()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Read(holder);
                    });

                private static void Read(Holder value) =>
                    Console.WriteLine(value.Event.Context.ShieldName);

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Event = item;
                    }

                    public RetryEvent Event { get; }
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_PostAwait_Explicit_Delegate_Invoke()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                Action use = () => Console.WriteLine(item.Context.ShieldName);
                use.Invoke();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Completed_Awaits_In_Source_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = AuditAsync();
                    });

                private async Task AuditAsync()
                {
                    await Task.CompletedTask;
                    Console.WriteLine(_event.Context.ShieldName);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Tracks_PostAwait_Event_Values_In_Wrapper_Locals()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder { Event = item };
                        await Task.Yield();
                        Consume(holder.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);
        var snapshot = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var snapshot = new RetrySnapshot(item);
                        await Task.Yield();
                        Console.WriteLine(snapshot.RetryNumber);
                    });

                private sealed class RetrySnapshot
                {
                    public RetrySnapshot(RetryEvent item)
                    {
                        RetryNumber = item.RetryNumber;
                    }

                    public int RetryNumber { get; }
                }
            }
            """);
        var constructorWrapper = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Event = item;
                    }

                    public RetryEvent Event { get; }
                }
            }
            """);
        var emptyInitializer = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder { };
                        await Task.Yield();
                        Console.WriteLine(holder.Value);
                    });

                private sealed class Holder
                {
                    public int Value { get; set; }
                }
            }
            """);
        var delegatedConstructor = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                        : this(item, 0)
                    {
                    }

                    private Holder(RetryEvent item, int _)
                    {
                        Event = item;
                    }

                    public RetryEvent Event { get; }
                }
            }
            """);
        var unrelatedConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Console.WriteLine(holder.Value);
                    });

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        new Sink().Event = item;
                    }

                    public int Value { get; }
                }

                private sealed class Sink
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);
        var fieldConstructorWrapper = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    private readonly RetryEvent _event;

                    public Holder(RetryEvent item)
                    {
                        _event = item;
                    }

                    public RetryEvent Event => _event;
                }
            }
            """);
        var nestedConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.State.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        State.Event = item;
                    }

                    public State State { get; } = new();
                }

                private sealed class State
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);
        var compositeConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Events[0].Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Events = new[] { item };
                    }

                    public RetryEvent[] Events { get; }
                }
            }
            """);
        var mutatingConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Events[0].Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Events.Add(item);
                    }

                    public System.Collections.Generic.List<RetryEvent> Events { get; } = new();
                }
            }
            """);
        var helperConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Event.Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Store(item);
                    }

                    public RetryEvent Event { get; private set; }

                    private void Store(RetryEvent item)
                    {
                        Event = item;
                    }
                }
            }
            """);
        var staticHelperConstructorStore = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Events[0].Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Store(Events, item);
                    }

                    public System.Collections.Generic.List<RetryEvent> Events { get; } = new();

                    private static void Store(
                        System.Collections.Generic.List<RetryEvent> events,
                        RetryEvent item) => events.Add(item);
                }
            }
            """);
        var compositeDelegatedConstructor = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Consume(holder.Events[0].Context);
                    });

                private static void Consume(KevlarContext context) { }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                        : this(new[] { item })
                    {
                    }

                    private Holder(RetryEvent[] events)
                    {
                        Events = events;
                    }

                    public RetryEvent[] Events { get; }
                }
            }
            """);
        var uninvokedNestedStores = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Console.WriteLine(holder.Value);
                    });

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        void Store() => Events.Add(item);
                        Action store = () => Events.Add(item);
                    }

                    public System.Collections.Generic.List<RetryEvent> Events { get; } = new();
                    public int Value { get; }
                }
            }
            """);
        var collectionWrapper = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var events = new System.Collections.Generic.List<RetryEvent> { default, item };
                        await Task.Yield();
                        Console.WriteLine(events[1].RetryNumber);
                    });
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(snapshot, "KEV013");
        await AssertRuleAsync(Without(constructorWrapper, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(constructorWrapper, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(emptyInitializer, "KEV013");
        await AssertRuleAsync(Without(delegatedConstructor, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(delegatedConstructor, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(unrelatedConstructorStore, "KEV013");
        await AssertRuleAsync(Without(fieldConstructorWrapper, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(fieldConstructorWrapper, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(nestedConstructorStore, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(nestedConstructorStore, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(compositeConstructorStore, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(compositeConstructorStore, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(mutatingConstructorStore, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(mutatingConstructorStore, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(helperConstructorStore, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(helperConstructorStore, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(staticHelperConstructorStore, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(staticHelperConstructorStore, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(Without(compositeDelegatedConstructor, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(compositeDelegatedConstructor, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(uninvokedNestedStores, "KEV013");
        await AssertRuleAsync(Without(collectionWrapper, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(collectionWrapper, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Framework_Collections_In_PostAwait_Aliases()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>(new[] { item });
                await Task.Yield();
                Console.WriteLine(events[0].Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Instance_Array_Stores_In_Constructors()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Console.WriteLine(holder.Events[0].Context.ShieldName);
                    });

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Events = new RetryEvent[1];
                        Events[0] = item;
                    }

                    public RetryEvent[] Events { get; }
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_PostAwait_Array_Element_Writes()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                var events = new RetryEvent[1];
                events[0] = item;
                await Task.Yield();
                Console.WriteLine(events[0].Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Constructor_Stores_From_Helper_Results()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        var holder = new Holder(item);
                        await Task.Yield();
                        Console.WriteLine(holder.Event.Context.ShieldName);
                    });

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Event = Identity(item);
                    }

                    public RetryEvent Event { get; }

                    private static RetryEvent Identity(RetryEvent item) => item;
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Nested_PostAwait_Local_Function_Calls()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                ReadContext();

                void ReadContext() => ReadInner();
                void ReadInner() => Console.WriteLine(item.Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Unrelated_Member_Names_In_Local_Functions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var holder = (item: 42, other: 0);
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                Read();

                void Read() => Console.WriteLine(holder.item);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Inspects_Async_Anonymous_Function_Syntaxes()
    {
        var callbacks = new[]
        {
            "async (RetryEvent item) => { await Task.Yield(); _ = item.Context.ShieldName; }",
            "async delegate(RetryEvent item) { await Task.Yield(); _ = item.Context.ShieldName; }",
        };
        foreach (var callback in callbacks)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = {{callback}});
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Inspects_Invoked_Async_Local_Functions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                async void Start()
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                }

                Start();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Invoked_Async_Delegate_Locals()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                Action start = async () =>
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                };
                start();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Explicit_Delegate_Invoke_Calls()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                Action work = async () =>
                {
                    await Task.Yield();
                    Console.WriteLine(item.Context.ShieldName);
                };
                work.Invoke();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Immediately_Invoked_Async_Delegates()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                ((Action)(async () =>
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                }))();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Invoked_Delegates_After_Await()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                Action use = () => Console.WriteLine(item.Context.ShieldName);
                use();
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Nameof_After_Await()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                await Task.Yield();
                _ = nameof(item);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Requires_Reachable_Suspension_Before_Event_Use()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                if (Environment.TickCount == 0)
                {
                    await Task.Yield();
                    return;
                }

                _ = item.Context.ShieldName;
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Ignores_Aliases_On_Paths_That_Exit_Before_Await()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                KevlarContext? retained = null;
                if (Environment.TickCount == 0)
                {
                    retained = item.Context;
                    return;
                }

                await Task.Yield();
                if (retained is not null)
                {
                    Console.WriteLine(retained.ShieldName);
                }
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Tracks_Aliases_At_Later_Reachable_Awaits()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                if (Environment.TickCount == 0)
                {
                    await Task.Yield();
                    return;
                }

                var retained = item;
                await Task.Yield();
                Console.WriteLine(retained.Context.ShieldName);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Uses_Reached_Through_Loop_BackEdges()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                while (Environment.TickCount >= 0)
                {
                    Console.WriteLine(item.Context.ShieldName);
                    await Task.Yield();
                }
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Keeps_Aliases_On_Their_Suspension_Path()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                RetryEvent retained = default;
                if (Environment.TickCount == 0)
                {
                    retained = item;
                    await Task.Yield();
                    return;
                }

                await Task.Yield();
                Console.WriteLine(retained.RetryNumber);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Keeps_Alias_Propagation_On_One_Control_Flow_Path()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                RetryEvent first = default;
                RetryEvent retained = default;
                if (Environment.TickCount == 0)
                {
                    first = item;
                }
                else
                {
                    retained = first;
                }

                await Task.Yield();
                Console.WriteLine(retained.RetryNumber);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Removes_Aliases_Cleared_On_Every_Incoming_Path()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                var retained = item;
                if (Environment.TickCount == 0)
                {
                    retained = default;
                }
                else
                {
                    retained = default;
                }

                await Task.Yield();
                Console.WriteLine(retained.RetryNumber);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Keeps_Aliases_Reintroduced_After_Exhaustive_Clears()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                var retained = item;
                if (Environment.TickCount == 0)
                {
                    retained = default;
                }
                else
                {
                    retained = default;
                }

                retained = item;
                await Task.Yield();
                Console.WriteLine(retained.RetryNumber);
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Combined_Async_Delegates()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            Action<RetryEvent> existing = _ => { };
            _ = Shield.Retry(options => options.OnRetry = existing
                + (Action<RetryEvent>)(async item =>
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                }));
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Allows_Awaited_And_Synchronous_Callbacks()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(o =>
            {
                o.OnRetry = _ => { };
                o.OnRetryAsync = async _ => await Task.Yield();
            });
            _ = Shield.Timeout(o => o.OnTimeoutAsync = _ => ValueTask.CompletedTask);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV013_Ignores_Similarly_Named_Application_Callbacks()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            namespace KevlarApplication;

            public sealed class RetryOptions
            {
                public Action<Kevlar.RetryEvent>? OnRetry { get; set; }
            }

            public sealed class TestSubject
            {
                public void Configure()
                {
                    var options = new RetryOptions();
                    options.OnRetry = async _ => await Task.Yield();
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV013_Flags_Task_Returning_Method_Group_On_Synchronous_Callback()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = RetryAsync);

                private static Task RetryAsync(RetryEvent item) => Task.CompletedTask;
            }
            """, allowCompilationErrors: true);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV013_Flags_Async_Void_Method_Group_On_Synchronous_Callback()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = RetryAsyncVoid);

                private static async void RetryAsyncVoid(RetryEvent item) => await Task.Yield();
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Inspects_Async_Void_Method_Group_After_Await()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = RetryAsyncVoid);

                private static async void RetryAsyncVoid(RetryEvent item)
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Async_Void_Event_Use_Before_Await()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = RetryAsyncVoid);

                private static async void RetryAsyncVoid(RetryEvent item)
                {
                    _ = item.Context.ShieldName;
                    await Task.Yield();
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Uses_Declaration_Tree_Control_Flow_For_Method_Groups()
    {
        var diagnostics = await AnalyzeSourcesAsync(
            """
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = CallbackHost.OnRetry);
            }
            """,
            """
            public static class CallbackHost
            {
                public static async void OnRetry(RetryEvent item)
                {
                    if (Environment.TickCount == 0)
                    {
                        await Task.Yield();
                        return;
                    }

                    _ = item.Context.ShieldName;
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Inspects_Constructed_Async_Void_Method_Groups()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry =
                        new Action<RetryEvent>(RetryAsyncVoid));

                private static async void RetryAsyncVoid(RetryEvent item)
                {
                    await Task.Yield();
                    _ = item.Context.ShieldName;
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Async_Void_Event_Aliases_After_Await()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = RetryAsyncVoid);

                private static async void RetryAsyncVoid(RetryEvent item)
                {
                    var retained = item;
                    await Task.Yield();
                    _ = retained.Context.ShieldName;
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Async_Event_Context_Aliases_After_Await()
    {
        var expressions = new[]
        {
            (Initializer: "item", Use: "retained.Context.ShieldName"),
            (Initializer: "(RetryEvent)item", Use: "retained.Context.ShieldName"),
            (Initializer: "(item)", Use: "retained.Context.ShieldName"),
            (Initializer: "item!", Use: "retained.Context.ShieldName"),
            (Initializer: "item.Context", Use: "retained.ShieldName"),
            (Initializer: "item.Context.Properties", Use: "retained.Count"),
        };
        foreach (var (initializer, use) in expressions)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = async item =>
                {
                    var retained = {{initializer}};
                    await Task.Yield();
                    _ = {{use}};
                });
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Ignores_Reassigned_Async_Event_Aliases()
    {
        var assignments = new[] { "retained = default;", "{ retained = default; }" };
        foreach (var assignment in assignments)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = async item =>
                {
                    var retained = item;
                    {{assignment}}
                    await Task.Yield();
                    _ = retained.Context.ShieldName;
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV013");
        }
    }

    [Test]
    public async Task KEV014_Distinguishes_Member_And_Parameter_Names()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var holder = (item: default(RetryEvent), other: 0);
            _ = Shield.Retry(options => options.OnRetry = async item =>
            {
                holder.item = default;
                await Task.Yield();
                _ = item.Context.ShieldName;
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Assigned_Event_Fields_After_Await()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        _event = item;
                        await Task.Yield();
                        _ = _event.Context.ShieldName;
                    });
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Event_Fields_On_Paths_Reaching_Await()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        _event = item;
                        if (Environment.TickCount == 0)
                        {
                            _event = default;
                            return;
                        }

                        await Task.Yield();
                        _ = _event.Context.ShieldName;
                    });
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Follows_Stable_Callback_Local_Initializers()
    {
        var trailingStatements = new[] { "", "callback = _ => { };" };
        foreach (var trailingStatement in trailingStatements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                Action<RetryEvent> callback = item => AuditAsync(item);
                _ = Shield.Retry(options => options.OnRetry = callback);
                {{trailingStatement}}

                static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV013_Inspects_Unobserved_Task_Calls_In_Block_Lambdas()
    {
        var cases = new[]
        {
            (Setup: "", Statement: "_ = AuditAsync(item);"),
            (Setup: "", Statement: "AuditAsync(item);"),
            (Setup: "", Statement: "var pending = AuditAsync(item);"),
            (Setup: "", Statement: "AuditAsync(item).Wait(0);"),
            (Setup: "", Statement: "Task.WaitAll(new[] { AuditAsync(item) }, 0);"),
            (
                Setup: "",
                Statement: "Task.WaitAll(new[] { AuditAsync(item) }, CancellationToken.None);"),
            (Setup: "Task? pending = null;", Statement: "pending = AuditAsync(item);"),
            (Setup: "var enabled = true;", Statement: "if (enabled) _ = AuditAsync(item);"),
            (
                Setup: "var pendingTasks = new System.Collections.Generic.List<Task>();",
                Statement: "pendingTasks.Add(AuditAsync(item));"),
        };
        foreach (var (setup, statement) in cases)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                {{setup}}
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                });

                static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Ignores_Scalar_Constructor_Snapshots_In_Discarded_Tasks()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                        _ = AuditAsync(new RetrySnapshot(item)));

                private static Task AuditAsync(RetrySnapshot snapshot) => Task.CompletedTask;

                private sealed class RetrySnapshot
                {
                    public RetrySnapshot(RetryEvent item)
                    {
                        RetryNumber = item.RetryNumber;
                    }

                    public int RetryNumber { get; }
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Ignores_Metadata_Scalar_Constructor_Snapshots()
    {
        var snapshotReference = CreateMetadataReference("""
            public sealed class RetrySnapshot
            {
                public RetrySnapshot(RetryEvent item)
                {
                    RetryNumber = item.RetryNumber;
                }

                public int RetryNumber { get; }
            }
            """);
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                        _ = AuditAsync(new RetrySnapshot(item)));

                private static Task AuditAsync(RetrySnapshot snapshot) => Task.CompletedTask;
            }
            """, additionalReference: snapshotReference);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV013_Inspects_Unobserved_Task_Calls_In_Handling_Predicates()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.HandlesExceptionWithContext = item =>
            {
                _ = AuditAsync(item);
                return true;
            });

            static Task AuditAsync(HandlingEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Inspects_Context_Bearing_Synchronous_Funcs()
    {
        var cases = new[]
        {
            """
            _ = Shield.For<int>().Retry(options => options.HandlesResultWithContext = item =>
            {
                _ = AuditAsync(item.Context);
                return true;
            });
            """,
            """
            _ = Shield.Retry(options => options.DelayGenerator = item =>
            {
                _ = AuditAsync(item.Context);
                return TimeSpan.Zero;
            });
            """,
            """
            _ = ChaosShield.Latency(options => options.Predicate = context =>
            {
                _ = AuditAsync(context);
                return true;
            });
            """,
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                {{body}}

                static Task AuditAsync(KevlarContext context) => Task.CompletedTask;
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Flags_Direct_Context_Captured_By_Scheduled_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = ChaosShield.Latency(options => options.Predicate = context =>
            {
                _ = Task.Run(() => Consume(context));
                return true;
            });

            static void Consume(KevlarContext context) =>
                Console.WriteLine(context.ShieldName);
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Inspects_Unobserved_Task_Calls_In_Expression_Lambdas()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item => AuditAsync(item).Wait(0));

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Reduced_Extension_Receivers()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public static class AuditExtensions
            {
                public static Task AuditAsync(this RetryEvent item) => Task.CompletedTask;
            }

            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item => item.AuditAsync());
            }
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Converted_Arguments()
    {
        var arguments = new[] { "(object)item", "new object[] { item }" };
        foreach (var argument in arguments)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item => ProcessAsync({{argument}}));

                static Task ProcessAsync(object item) => Task.CompletedTask;
                """);

            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Follows_Retained_Locals_And_Delegates()
    {
        var statements = new[]
        {
            "object state = item; ProcessObjectAsync(state);",
            "object state = item; ProcessObjectAsync(state); state = null!;",
            "ProcessDelegateAsync(() => item.Context.ShieldName);",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                });

                static Task ProcessObjectAsync(object state) => Task.CompletedTask;
                static Task ProcessDelegateAsync(Func<string?> state) => Task.CompletedTask;
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Ignores_Detached_Argument_Projections()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ProcessAsync(item.Context.ShieldName));

            static Task ProcessAsync(string shieldName) => Task.CompletedTask;
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
        await Assert.That(Without(diagnostics, "KEV013")).IsEmpty();
    }

    [Test]
    public async Task KEV013_Ignores_Uninvoked_Nested_Functions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                void Local() => _ = AuditAsync(item);
                Action nested = () => _ = AuditAsync(item);
            });

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV013_Follows_Invoked_Local_Functions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                void Start() => _ = AuditAsync(item);
                Start();
            });

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Task_Returning_Local_Functions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                Task Start() => AuditAsync(item);
                _ = Start();
            });

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Ignores_Synchronously_Observed_Task_Calls()
    {
        var statements = new[]
        {
            "AuditAsync(item).GetAwaiter().GetResult();",
            "AuditAsync(item).ConfigureAwait(false).GetAwaiter().GetResult();",
            "AuditAsync(item).Wait();",
            "_ = AuditAsync(item).Result;",
            "Task.WaitAll(AuditAsync(item));",
            "Task.WhenAll(AuditAsync(item), FlushAsync()).GetAwaiter().GetResult();",
            "Task.WhenAll(new[] { AuditAsync(item), FlushAsync() }).GetAwaiter().GetResult();",
            "AuditValueAsync(item).AsTask().GetAwaiter().GetResult();",
            "AuditValueAsync(item).ConfigureAwait(false).GetAwaiter().GetResult();",
            "AuditValueAsync(item).AsTask().Wait();",
            "FlushAsync().GetAwaiter().GetResult();",
            "FlushAsync().ConfigureAwait(false).GetAwaiter().GetResult();",
            "FlushValueAsync().GetAwaiter().GetResult();",
            "FlushValueAsync().ConfigureAwait(false).GetAwaiter().GetResult();",
            "var pending = AuditAsync(item); pending.GetAwaiter().GetResult();",
            "var pending = AuditAsync(item); pending.Wait();",
            "var pending = AuditAsync(item); _ = pending.Result;",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                });

                static Task<RetryEvent> AuditAsync(RetryEvent item) => Task.FromResult(item);
                static ValueTask<RetryEvent> AuditValueAsync(RetryEvent item) =>
                    ValueTask.FromResult(item);
                static Task FlushAsync() => Task.CompletedTask;
                static ValueTask FlushValueAsync() => ValueTask.CompletedTask;
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV013_Rejects_Unrelated_GetResult_Extensions()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public static class TaskExtensions
            {
                public static void GetResult(this Task task) { }
            }

            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                        AuditAsync(item).GetResult());

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Rejects_Task_Local_Joins_After_Early_Returns()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var pending = AuditAsync(item);
                if (Environment.TickCount == 0)
                {
                    return;
                }

                pending.GetAwaiter().GetResult();
            });

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Rejects_Conditional_Task_Local_Joins()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var skip = Environment.TickCount == 0;
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var pending = AuditAsync(item);
                _ = skip ? 0 : pending.GetAwaiter().GetResult();
            });

            static Task<int> AuditAsync(RetryEvent item) => Task.FromResult(0);
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Rejects_Task_Local_Joins_After_Throwing_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var pending = AuditAsync(item);
                MightThrow();
                pending.GetAwaiter().GetResult();
            });

            static void MightThrow() => throw new InvalidOperationException();
            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Rejects_Task_Local_Joins_Of_Unrelated_Composite_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var pending = (AuditAsync(item), Task.CompletedTask).Item2;
                pending.GetAwaiter().GetResult();
            });

            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Rejects_Filtered_WhenAll_Collections()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            using System.Linq;

            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                        Task.WhenAll(new[] { AuditAsync(item) }.Where(_ => false))
                            .GetAwaiter()
                            .GetResult());

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Every_Task_In_A_Combinator()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var pendingTasks = new System.Collections.Generic.List<Task>();
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                pendingTasks.Add(Task.WhenAll(FlushAsync(), AuditAsync(item)));
            });

            static Task FlushAsync() => Task.CompletedTask;
            static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV013_Inspects_Conditional_Callback_Branches()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var enabled = true;
            _ = Shield.Retry(options => options.OnRetry = enabled
                ? async item => await Task.Yield()
                : _ => { });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV006"), "KEV013");
    }

    [Test]
    public async Task KEV013_Inspects_Coalesced_Callback_Operands()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            Action<RetryEvent>? existing = null;
            _ = Shield.Retry(options => options.OnRetry = existing
                ?? (async item => await Task.Yield()));
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV006"), "KEV013");
    }

    [Test]
    public async Task KEV013_Inspects_Switch_Expression_Callback_Arms()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var mode = 0;
            _ = Shield.Retry(options => options.OnRetry = mode switch
            {
                0 => async item => await Task.Yield(),
                _ => _ => { },
            });
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV006"), "KEV013");
    }

    [Test]
    public async Task KEV013_Code_Fix_Only_Renames_Compatible_Lambdas()
    {
        var compatible = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async _ => await Task.Yield());
            }
            """);
        var compatibleValueTask = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = _ => AuditAsync());

                private static ValueTask AuditAsync() => ValueTask.CompletedTask;
            }
            """);
        var incompatible = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Hedge(options => options.OnHedge = _ => Task.Delay(1));
            }
            """);
        var incompatibleGenericValueTask = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = _ => GetValueAsync());

                private static ValueTask<int> GetValueAsync() => ValueTask.FromResult(1);
            }
            """);
        var incompatibleAdditiveAssignment = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry += async _ => await Task.Yield());
            }
            """);
        var incompatibleCoalescingAssignment = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry ??= async _ => await Task.Yield());
            }
            """);
        var incompatibleDiscardingBlock = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _ = AuditAsync(item);
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var compatibleLocalFunction = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async _ =>
                    {
                        Log();
                        await Task.Yield();

                        static void Log() => Console.WriteLine("retrying");
                    });
            }
            """);
        var incompatibleAsyncDiscardingBlock = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        _ = AuditAsync(item);
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleLocalFunctionDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Start();
                        await Task.Yield();

                        void Start() => _ = AuditAsync(item);
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleDelegateLocalDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Action start = () => _ = AuditAsync(item);
                        start();
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleImmediatelyInvokedDelegate = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        ((Action)(() => _ = AuditAsync(item)))();
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleImmediatelyInvokedDelegateInvoke = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        ((Action)(() => _ = AuditAsync(item))).Invoke();
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleSourceMethodDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Start(item);
                        await Task.Yield();
                    });

                private static void Start(RetryEvent item)
                {
                    StartCore(item);
                }

                private static void StartCore(RetryEvent item) => _ = AuditAsync(item);
                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleForwardedDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Action start = () => _ = AuditAsync(item);
                        Run(() => Console.WriteLine(item.RetryNumber));
                        Run(start);
                        await Task.Yield();
                    });

                private static void Run(Action action) => action();
                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleOpaqueDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Array.ForEach(new[] { 0 }, _ => _ = AuditAsync(item));
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleLocalFunctionMethodGroup = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Array.ForEach(new[] { 0 }, Work);
                        await Task.Yield();

                        void Work(int _) => _ = AuditAsync(item);
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleConstructedDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Array.ForEach(
                            new[] { 0 },
                            new Action<int>(_ => _ = AuditAsync(item)));
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleConditionalDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(bool enabled) =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Array.ForEach(
                            new[] { 0 },
                            enabled ? _ => _ = AuditAsync(item) : _ => { });
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleConditionalDelegateLocalDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(bool enabled) =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Action<int> work = enabled
                            ? _ => _ = AuditAsync(item)
                            : _ => { };
                        Array.ForEach(new[] { 0 }, work);
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleSwitchDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(int mode) =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Array.ForEach(
                            new[] { 0 },
                            mode switch
                            {
                                0 => _ => _ = AuditAsync(item),
                                _ => _ => { },
                            });
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleCoalescedDelegateDiscard = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                    {
                        Action<int>? audit = _ => _ = AuditAsync(item);
                        Array.ForEach(new[] { 0 }, audit ?? (_ => { }));
                        await Task.Yield();
                    });

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleAwaitWrapper = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = async item =>
                        await Forward(AuditAsync(item)));

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
                private static ValueTask Forward(Task ignored) => ValueTask.CompletedTask;
            }
            """);
        var incompatibleTimedWait = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item => AuditAsync(item).Wait(0));

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var incompatibleCollectionAdd = await GetCodeFixAsync("""
            public class TestSubject
            {
                private readonly System.Collections.Generic.List<Task> _pending = new();

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item => _pending.Add(AuditAsync(item)));

                private static Task AuditAsync(RetryEvent item) => Task.CompletedTask;
            }
            """);
        var alreadyAssignedAsyncTwin = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options =>
                    {
                        options.OnRetryAsync = _ => ValueTask.CompletedTask;
                        options.OnRetry = async _ => await Task.Yield();
                    });
            }
            """);

        var alreadyAssignedInHelperBlock = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(RetryOptions options)
                {
                    options.OnRetryAsync = _ => ValueTask.CompletedTask;
                    options.OnRetry = async _ => await Task.Yield();
                }
            }
            """);
        var alreadyAssignedInOuterBlock = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(RetryOptions options, bool enabled)
                {
                    options.OnRetryAsync = _ => ValueTask.CompletedTask;
                    if (enabled)
                    {
                        options.OnRetry = async _ => await Task.Yield();
                    }
                }
            }
            """);
        var assignedOnDifferentReceiver = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(RetryOptions first, RetryOptions second)
                {
                    first.OnRetryAsync = _ => ValueTask.CompletedTask;
                    second.OnRetry = async _ => await Task.Yield();
                }
            }
            """);
        var alreadyAssignedThroughAlias = await GetCodeFixAsync("""
            public class TestSubject
            {
                public void Configure(RetryOptions options)
                {
                    var alias = options;
                    options.OnRetryAsync = _ => ValueTask.CompletedTask;
                    alias.OnRetry = async _ => await Task.Yield();
                }
            }
            """);

        await Assert.That(compatible.ActionCount).IsEqualTo(1);
        await Assert.That(compatible.ChangedText).Contains("options.OnRetryAsync = async");
        await Assert.That(compatibleValueTask.ActionCount).IsEqualTo(1);
        await Assert.That(compatibleValueTask.ChangedText)
            .Contains("options.OnRetryAsync = _ => AuditAsync()");
        await Assert.That(compatibleLocalFunction.ActionCount).IsEqualTo(1);
        await Assert.That(compatibleLocalFunction.ChangedText)
            .Contains("options.OnRetryAsync = async");
        await Assert.That(incompatible.ActionCount).IsEqualTo(0);
        await Assert.That(incompatible.ChangedText).IsNull();
        await Assert.That(incompatibleGenericValueTask.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleGenericValueTask.ChangedText).IsNull();
        await Assert.That(incompatibleAdditiveAssignment.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleAdditiveAssignment.ChangedText).IsNull();
        await Assert.That(incompatibleCoalescingAssignment.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleCoalescingAssignment.ChangedText).IsNull();
        await Assert.That(incompatibleDiscardingBlock.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleDiscardingBlock.ChangedText).IsNull();
        await Assert.That(incompatibleAsyncDiscardingBlock.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleAsyncDiscardingBlock.ChangedText).IsNull();
        await Assert.That(incompatibleLocalFunctionDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleLocalFunctionDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleDelegateLocalDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleDelegateLocalDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleImmediatelyInvokedDelegate.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleImmediatelyInvokedDelegate.ChangedText).IsNull();
        await Assert.That(incompatibleImmediatelyInvokedDelegateInvoke.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleImmediatelyInvokedDelegateInvoke.ChangedText).IsNull();
        await Assert.That(incompatibleSourceMethodDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleSourceMethodDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleForwardedDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleForwardedDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleOpaqueDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleOpaqueDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleLocalFunctionMethodGroup.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleLocalFunctionMethodGroup.ChangedText).IsNull();
        await Assert.That(incompatibleConstructedDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleConstructedDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleConditionalDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleConditionalDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleConditionalDelegateLocalDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleConditionalDelegateLocalDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleSwitchDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleSwitchDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleCoalescedDelegateDiscard.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleCoalescedDelegateDiscard.ChangedText).IsNull();
        await Assert.That(incompatibleAwaitWrapper.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleAwaitWrapper.ChangedText).IsNull();
        await Assert.That(incompatibleTimedWait.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleTimedWait.ChangedText).IsNull();
        await Assert.That(incompatibleCollectionAdd.ActionCount).IsEqualTo(0);
        await Assert.That(incompatibleCollectionAdd.ChangedText).IsNull();
        await Assert.That(alreadyAssignedAsyncTwin.ActionCount).IsEqualTo(0);
        await Assert.That(alreadyAssignedAsyncTwin.ChangedText).IsNull();
        await Assert.That(alreadyAssignedInHelperBlock.ActionCount).IsEqualTo(0);
        await Assert.That(alreadyAssignedInHelperBlock.ChangedText).IsNull();
        await Assert.That(alreadyAssignedInOuterBlock.ActionCount).IsEqualTo(0);
        await Assert.That(alreadyAssignedInOuterBlock.ChangedText).IsNull();
        await Assert.That(assignedOnDifferentReceiver.ActionCount).IsEqualTo(1);
        await Assert.That(assignedOnDifferentReceiver.ChangedText)
            .Contains("second.OnRetryAsync = async");
        await Assert.That(alreadyAssignedThroughAlias.ActionCount).IsEqualTo(0);
        await Assert.That(alreadyAssignedThroughAlias.ChangedText).IsNull();
    }

    [Test]
    public async Task KEV014_Ignores_Synchronously_Joined_TaskRun()
    {
        var statements = new[]
        {
            "Task.Run(() => item.Context.ShieldName).GetAwaiter().GetResult();",
            "Task.Run(() => item.Context.ShieldName).Wait();",
            "_ = Task.Run(() => item.Context.ShieldName).Result;",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Ignores_Synchronously_Joined_Delegate_Locals()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                Func<Task> work = async () =>
                {
                    await Task.Yield();
                    Console.WriteLine(item.Context.ShieldName);
                };
                work().GetAwaiter().GetResult();
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Awaited_TaskRun()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetryAsync = async item =>
                await Task.Run(() => Consume(item.Context)));

            static void Consume(KevlarContext context) =>
                Console.WriteLine(context.ShieldName);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Task_Locals_Joined_After_Constant_Declarations()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetryAsync = async item =>
            {
                var pending = Task.Run(() => Consume(item.Context));
                var marker = 0;
                await pending;
                _ = marker;
            });

            static void Consume(KevlarContext context) =>
                Console.WriteLine(context.ShieldName);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Returned_ValueTask_Wrappers()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetryAsync = item =>
                new ValueTask(Task.Run(() => Consume(item.Context))));

            static void Consume(KevlarContext context) =>
                Console.WriteLine(context.ShieldName);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Flags_Awaited_TaskRun_In_Async_Void_Callbacks()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = async item =>
                await Task.Run(() => Consume(item.Context)));

            static void Consume(KevlarContext context) =>
                Console.WriteLine(context.ShieldName);
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Event_Context_Captured_By_Deferred_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                Task.Run(() => Console.WriteLine(item.Context.ShieldName)));
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Pooled_Context_Field_In_Deferred_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private KevlarContext _context;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _context = item.Context;
                        _ = Task.Run(() => Console.WriteLine(_context.ShieldName));
                    });
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Pooled_Event_Field_In_Deferred_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = Task.Run(() => Console.WriteLine(_event.Context.ShieldName));
                    });
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Unproven_Event_Fields_In_Scheduled_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Schedule()
                {
                    _event = default;
                    _ = Task.Run(() => Console.WriteLine(_event.RetryNumber));
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Unproven_Event_Properties_In_Scheduled_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent Unrelated => default;

                public void Schedule() =>
                    _ = Task.Run(() => Console.WriteLine(Unrelated.RetryNumber));
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Flags_Proven_Event_Fields_In_Scheduled_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Event_Members_From_Explicit_Callback_Forms()
    {
        var parenthesized = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent Stored { get; set; }

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = (RetryEvent item) =>
                    {
                        Stored = item;
                        _ = Task.Run(() => Consume(Stored));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);
        var anonymousMethod = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = delegate(RetryEvent item)
                    {
                        _event = item;
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await AssertRuleAsync(parenthesized, "KEV014", DiagnosticSeverity.Warning);
        await AssertRuleAsync(anonymousMethod, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Event_Member_Provenance_Across_Branches()
    {
        var retainedOnOneBranch = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        if (Environment.TickCount == 0)
                        {
                            _event = item;
                        }
                        else
                        {
                            _event = default;
                        }

                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);
        var clearedOnEveryBranch = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        if (Environment.TickCount == 0)
                        {
                            _event = default;
                        }
                        else
                        {
                            _event = default;
                        }

                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);
        var partiallyClearedNestedBranch = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        if (Environment.TickCount == 0)
                        {
                            if (Environment.TickCount == 1)
                            {
                                _event = default;
                            }
                        }
                        else
                        {
                            _event = default;
                        }

                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await AssertRuleAsync(retainedOnOneBranch, "KEV014", DiagnosticSeverity.Warning);
        await Assert.That(clearedOnEveryBranch).IsEmpty();
        await AssertRuleAsync(
            partiallyClearedNestedBranch,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Event_Member_Provenance_Across_Sequential_Writes()
    {
        var cleared = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _event = (RetryEvent)default;
                        _event = (default(RetryEvent));
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);
        var reintroduced = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _event = default;
                        _event = item;
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await Assert.That(cleared).IsEmpty();
        await AssertRuleAsync(reintroduced, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Conditionally_Assigned_Captured_Locals()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        RetryEvent retained = default;
                        if (Environment.TickCount == 0)
                        {
                            retained = item;
                        }

                        _ = Task.Run(() => Consume(retained));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Unproven_Member_Assignments_In_Scheduled_Work()
    {
        var unrelatedCallback = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;
                public Action<RetryEvent>? Callback { get; set; }

                public void Configure() => Callback = item =>
                {
                    _event = item;
                    _ = Task.Run(() => Console.WriteLine(_event.RetryNumber));
                };
            }
            """);
        var parameterlessCallback = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure(RetryEvent initial) =>
                    _ = Shield.Retry(options => options.OnRetry = delegate
                    {
                        _event = initial;
                        _ = Task.Run(() => Console.WriteLine(_event.RetryNumber));
                    });
            }
            """);
        var shadowedField = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        RetryEvent _event = item;
                        _ = Task.Run(() => Consume(this._event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);
        var memberNameCollision = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private readonly Holder _holder = new();
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = _holder.item;
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }

                private sealed class Holder
                {
                    public RetryEvent item;
                }
            }
            """);
        var conditionOnlyReference = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item.RetryNumber > 0
                            ? default(RetryEvent)
                            : default(RetryEvent);
                        _ = Task.Run(() => Consume(_event));
                    });

                private static void Consume(RetryEvent item) { }
            }
            """);

        await Assert.That(unrelatedCallback).IsEmpty();
        await Assert.That(parameterlessCallback).IsEmpty();
        await Assert.That(shadowedField).IsEmpty();
        await Assert.That(memberNameCollision).IsEmpty();
        await Assert.That(conditionOnlyReference).IsEmpty();
    }

    [Test]
    public async Task KEV014_Inspects_Scheduled_Instance_Method_Bodies()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = Task.Run(ProcessStored);
                    });

                private void ProcessStored() =>
                    Console.WriteLine(_event.Context.ShieldName);
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Proven_Event_Fields_Into_Scheduled_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = Task.Run(ProcessStored);
                    });

                private void ProcessStored() => Consume(_event);
                private static void Consume(RetryEvent item) { }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Fresh_Defaults_Passed_To_Scheduled_Helpers()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Schedule()
                {
                    _ = Task.Run(() => Consume(default(RetryEvent)));
                    ThreadPool.QueueUserWorkItem<RetryEvent>(
                        Consume,
                        default,
                        preferLocal: false);
                }

                private static void Consume(RetryEvent item) { }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Factory_Produced_Event_Defaults()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Schedule()
                {
                    var unrelated = CreateDefault();
                    _ = Task.Run(() => Console.WriteLine(unrelated.RetryNumber));
                }

                private static RetryEvent CreateDefault() => default;
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Member_Defaults_From_Source_Helpers()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = CreateDefault(item);
                        _ = Task.Run(() => Consume(_event));
                    });

                private static RetryEvent CreateDefault(RetryEvent ignored) => default;
                private static void Consume(RetryEvent item) { }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_Factory_Results_From_Event_Arguments()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var retained = Identity(item);
                        _ = Task.Run(() => Console.WriteLine(retained.RetryNumber));
                    });

                private static RetryEvent Identity(RetryEvent item) => item;
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Scalar_Factory_Results_From_Event_Arguments()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var retryNumber = GetRetryNumber(item);
                        _ = Task.Run(() => Console.WriteLine(retryNumber));
                    });

                private static int GetRetryNumber(RetryEvent item) => item.RetryNumber;
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Inspects_Unobserved_Async_Instance_Method_Bodies()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = AuditStoredAsync();
                    });

                private async Task AuditStoredAsync()
                {
                    await Task.Yield();
                    Console.WriteLine(_event.Context.ShieldName);
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Retained_Wrappers_In_Async_Instance_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private Holder _holder = null!;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _holder = new Holder(item);
                        _ = AuditStoredAsync();
                    });

                private async Task AuditStoredAsync()
                {
                    await Task.Yield();
                    Console.WriteLine(_holder.Event.Context.ShieldName);
                }

                private sealed class Holder
                {
                    public Holder(RetryEvent item)
                    {
                        Event = item;
                    }

                    public RetryEvent Event { get; }
                }
            }
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Member_Aliases_In_Async_Instance_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = AuditStoredAsync();
                    });

                private async Task AuditStoredAsync()
                {
                    var retained = this._event;
                    await Task.Yield();
                    Console.WriteLine(retained.Context.ShieldName);
                }
            }
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Unrelated_Event_Members_In_Async_Instance_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _unrelated;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _ = AuditAsync();
                    });

                private async Task AuditAsync()
                {
                    await Task.Yield();
                    Console.WriteLine(_unrelated.RetryNumber);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Honors_Cleared_Event_Members_In_Async_Instance_Methods()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _event = default;
                        _ = AuditStoredAsync();
                    });

                private async Task AuditStoredAsync()
                {
                    await Task.Yield();
                    Console.WriteLine(_event.RetryNumber);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Ignores_Async_Method_Aliases_On_Exiting_Paths()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = AuditAsync(Environment.TickCount == 0);
                    });

                private async Task AuditAsync(bool stop)
                {
                    RetryEvent retained = default;
                    if (stop)
                    {
                        retained = _event;
                        return;
                    }

                    await Task.Yield();
                    Console.WriteLine(retained.RetryNumber);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV013");
    }

    [Test]
    public async Task KEV014_Tracks_Async_Method_Aliases_At_Later_Awaits()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent _event;

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        _event = item;
                        _ = AuditAsync(Environment.TickCount == 0);
                    });

                private async Task AuditAsync(bool skip)
                {
                    if (skip)
                    {
                        await Task.Yield();
                        return;
                    }

                    var retained = _event;
                    await Task.Yield();
                    Console.WriteLine(retained.Context.ShieldName);
                }
            }
            """);

        await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Pooled_Event_Property_In_Deferred_Work()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private RetryEvent StoredEvent { get; set; }

                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        StoredEvent = item;
                        _ = Task.Run(() => Console.WriteLine(StoredEvent.Context.ShieldName));
                    });
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Event_Forwarded_To_Deferred_Helper()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                Task.Run(() => Process(item)));

            static void Process(RetryEvent item) =>
                Console.WriteLine(item.Context.ShieldName);
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Handling_Event_Captured_By_Deferred_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.HandlesExceptionWithContext = item =>
            {
                _ = Task.Run(() => Consume(item));
                return true;
            });

            static void Consume(HandlingEvent item) =>
                Console.WriteLine(item.Context.ShieldName);
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Sibling_Assembly_Events_Forwarded_To_Deferred_Work()
    {
        var chaosDiagnostics = await AnalyzeBodyAsync("""
            _ = ChaosShield.Latency(options => options.OnInjected = item =>
                Task.Run(() => Process(item)));

            static void Process(ChaosEvent item) =>
                Console.WriteLine(item.Context.ShieldName);
            """);
        var rateLimiterDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Empty.UseRateLimiter(
                (System.Threading.RateLimiting.RateLimiter)null!,
                options => options.OnRejected = item =>
                    Task.Run(() => Process(item)));

            static void Process(RateLimiterAdapterRejectedEvent item) =>
                Console.WriteLine(item.Context.ShieldName);
            """);

        await AssertRuleAsync(
            Without(chaosDiagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            Without(rateLimiterDiagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Erased_Event_Local_Captured_By_Deferred_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                object boxed = item;
                _ = Task.Run(() => Process((RetryEvent)boxed));
            });

            static void Process(RetryEvent item) =>
                Console.WriteLine(item.Context.ShieldName);
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Captured_Local_Function_Method_Groups()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                void Work() => Console.WriteLine(item.Context.ShieldName);
                _ = Task.Run(Work);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Local_Function_Calls_From_Scheduled_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                void Inner() => Console.WriteLine(item.Context.ShieldName);
                void Work() => Inner();
                _ = Task.Run(Work);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Allows_Copied_Context_Values_In_Deferred_Work()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var shieldName = item.Context.ShieldName;
                _ = Task.Run(() => Console.WriteLine(shieldName));
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Allows_Context_Value_Passed_To_Eager_Delegate_Factory()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            Action CreateWork(string value) => () => Console.WriteLine(value);
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                _ = Task.Run(CreateWork(item.Context.ShieldName));
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Allows_Context_Value_Used_To_Construct_Method_Group_Receiver()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class Worker(string value)
            {
                public void Run() => Console.WriteLine(value);
            }

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item)
                {
                    _ = Task.Run(new Worker(item.Context.ShieldName).Run);
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Allows_Context_Value_Passed_As_Eager_State()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static value => Console.WriteLine(value),
                    item.Context.ShieldName));
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Follows_Event_Context_Local_Aliases()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var context = item.Context;
                _ = Task.Run(() => Console.WriteLine(context.ShieldName));
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Similarly_Named_Application_Context_Types()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            namespace Foo.Kevlar
            {
                public sealed class KevlarContext
                {
                    public string ShieldName => "application";
                }
            }

            public sealed class ApplicationEvent
            {
                public Foo.Kevlar.KevlarContext Context { get; } = new();
            }

            public sealed class TestSubject
            {
                public void Observe(ApplicationEvent item) =>
                    _ = Task.Run(() => Console.WriteLine(item.Context.ShieldName));
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Static_Event_Context_Properties()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class ApplicationEvent
            {
                public static KevlarContext Context => default;
            }

            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = retryEvent =>
                    {
                        var item = new ApplicationEvent();
                        _ = Task.Run(() => Use(item));
                    });

                private static void Use(ApplicationEvent item) { }
            }
            """);

        await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Computed_Event_Context_Properties()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class ApplicationEvent
            {
                public KevlarContext Context => default;
            }

            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = retryEvent =>
                    {
                        var item = new ApplicationEvent();
                        _ = Task.Run(() => Use(item));
                    });

                private static void Use(ApplicationEvent item) { }
            }
            """);

        await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
    }

    [Test]
    public async Task KEV014_Flags_Event_Context_Passed_As_Deferred_State()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (KevlarContext context) => Console.WriteLine(context.ShieldName),
                    item.Context,
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Event_Context_Nested_In_Deferred_State()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static ((KevlarContext Context, int Value) state) =>
                        Console.WriteLine(state.Context.ShieldName),
                    (Context: item.Context, Value: 1),
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Flags_Event_Context_In_NonGeneric_Deferred_State()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class WorkState
            {
                public KevlarContext Context { get; set; } = null!;
            }

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item)
                {
                    var state = new WorkState { Context = item.Context };
                    ThreadPool.QueueUserWorkItem(
                        static value => Console.WriteLine(value.Context.ShieldName),
                        state,
                        preferLocal: false);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Constructor_Arguments_In_Deferred_State()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class WorkState
            {
                public WorkState(KevlarContext context) => Context = context;

                public KevlarContext Context { get; }
            }

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item) =>
                    ThreadPool.QueueUserWorkItem(
                        static state => Console.WriteLine(state.Context.ShieldName),
                        new WorkState(item.Context),
                        preferLocal: false);
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Array_State_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (object[] state) =>
                        Console.WriteLine(((KevlarContext)state[0]).ShieldName),
                    new object[] { item.Context },
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Object_Erased_Tuple_State_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static state =>
                        Console.WriteLine(((KevlarContext)state.Context).ShieldName),
                    (Context: (object)item.Context, Other: 0),
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Conditionally_Selected_Erased_State()
    {
        var cases = new[]
        {
            "enabled ? (object)item.Context : new object()",
            "enabled ? new object() : (object)item.Context",
            "(object?)item.Context ?? new object()",
            "(object?)null ?? item.Context",
            "enabled switch { true => (object)item.Context, false => new object() }",
        };

        foreach (var state in cases)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                var enabled = true;
                _ = Shield.Retry(options => options.OnRetry = item =>
                    ThreadPool.QueueUserWorkItem(
                        static (object value) => Console.WriteLine(value),
                        {{state}},
                        preferLocal: false));
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Inspects_Retained_Composite_Task_Arguments()
    {
        var statements = new[]
        {
            "_ = AuditAsync(enabled ? (object)item : new object());",
            "_ = AuditAsync((object?)item ?? new object());",
            "_ = AuditAsync(enabled switch { true => (object)item, false => new object() });",
            "_ = AuditAsync(new object[] { item });",
            "_ = AuditAsync(((object)item, 0));",
            "_ = AuditAsync(new WorkState((object)item));",
            "_ = AuditAsync(new WorkState(new object()) { Value = (object)item });",
            "_ = AuditAsync(new System.Collections.Generic.List<object> { item });",
            "var template = new WorkState(new object()); _ = AuditAsync(template with { Value = (object)item });",
            "_ = AuditAsync(new { Value = (object)item });",
            "_ = AuditArrayAsync([item]);",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeSourceAsync($$"""
                public sealed record WorkState(object Value);

                public sealed class TestSubject
                {
                    public void Configure()
                    {
                        var enabled = true;
                        _ = Shield.Retry(options => options.OnRetry = item =>
                        {
                            {{statement}}
                        });
                    }

                    private static Task AuditAsync(object state) => Task.CompletedTask;
                    private static Task AuditArrayAsync(object[] state) => Task.CompletedTask;
                }
                """);

            await AssertRuleAsync(Without(diagnostics, "KEV014"), "KEV013");
            await AssertRuleAsync(
                Without(diagnostics, "KEV013"),
                "KEV014",
                DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Ignores_Nested_Delegate_Event_Parameters()
    {
        var selectors = new[]
        {
            "(RetryEvent other) => other.RetryNumber",
            "(RetryEvent other) => other.Context.ShieldName.Length",
            "(RetryEvent other) => { var copy = other; return copy.RetryNumber; }",
        };
        foreach (var selector in selectors)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                    ProcessAsync({{selector}}));

                static Task ProcessAsync(Func<RetryEvent, int> selector) => Task.CompletedTask;
                """);

            await AssertRuleAsync(diagnostics, "KEV013");
        }
    }

    [Test]
    public async Task KEV014_Ignores_Nested_Scheduler_Delegate_Parameters()
    {
        var selectors = new[]
        {
            "(RetryEvent other) => other.RetryNumber",
            "(RetryEvent other) => { var copy = other; return copy.RetryNumber; }",
        };
        foreach (var selector in selectors)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    _ = Task.Run(() => Consume({{selector}}));
                });

                static void Consume(Func<RetryEvent, int> selector) { }
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Ignores_Provably_Empty_Deferred_State()
    {
        var states = new[]
        {
            "new RetryEvent[0]",
            "Array.Empty<RetryEvent>()",
            "new System.Collections.Generic.List<RetryEvent>()",
            "default(RetryEvent)",
        };
        foreach (var state in states)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    ThreadPool.QueueUserWorkItem(
                        static _ => { },
                        {{state}});
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Tracks_Deferred_State_Added_To_Local_Collections()
    {
        var mutations = new[]
        {
            "events.Add(item);",
            "events.AddRange(new[] { item });",
            "var alias = events; alias.Add(item);",
        };
        foreach (var mutation in mutations)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new System.Collections.Generic.List<RetryEvent>();
                    {{mutation}}
                    ThreadPool.QueueUserWorkItem(
                        static (System.Collections.Generic.List<RetryEvent> state) =>
                            Console.WriteLine(state[0].Context.ShieldName),
                        events,
                        preferLocal: false);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Tracks_Deferred_State_Added_To_Local_Arrays()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent[1];
                events[0] = item;
                ThreadPool.QueueUserWorkItem(
                    static (RetryEvent[] state) =>
                        Console.WriteLine(state[0].Context.ShieldName),
                    events,
                    preferLocal: false);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Static_Array_Mutations()
    {
        var fillDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent[1];
                Array.Fill(events, item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var clearDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new[] { item };
                Array.Clear(events);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var conditionalClearDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new[] { item };
                if (DateTime.UtcNow.Ticks == 0)
                {
                    Array.Clear(events);
                }

                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(fillDiagnostics, "KEV014", DiagnosticSeverity.Warning);
        await Assert.That(clearDiagnostics).IsEmpty();
        await AssertRuleAsync(
            conditionalClearDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Static_Array_Copies()
    {
        var copies = new[]
        {
            "Array.Copy(new object[] { item }, events, 1);",
            "Array.ConstrainedCopy(new object[] { item }, 0, events, 0, 1);",
        };
        foreach (var copy in copies)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    object[] events = new object[1];
                    {{copy}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }

        var overwriteDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new[] { item };
                Array.Copy(new[] { default(RetryEvent) }, events, 1);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var copiedSlices = new[]
        {
            "Array.Copy(new[] { item, default(RetryEvent) }, 1, events, 0, 1);",
            "Array.ConstrainedCopy(new[] { item, default(RetryEvent) }, 1, events, 0, 1);",
        };

        await Assert.That(overwriteDiagnostics).IsEmpty();
        foreach (var copy in copiedSlices)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new RetryEvent[1];
                    {{copy}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Distinguishes_Nested_Deferred_State_Slots()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent[2][];
                events[0] = new RetryEvent[1];
                events[1] = new RetryEvent[1];
                events[0][0] = item;
                events[1][0] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var dynamicIndexDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var index = 0;
                var events = new RetryEvent[2];
                events[index] = item;
                events[1] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var distinctDynamicIndicesDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var i = Environment.TickCount;
                var j = Environment.ProcessId;
                var events = new RetryEvent[2];
                events[i] = item;
                events[j] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var reassignedIndexDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var index = 0;
                var events = new RetryEvent[2];
                events[index] = item;
                index = 1;
                events[index] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            dynamicIndexDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            distinctDynamicIndicesDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            reassignedIndexDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Distinguishes_Deferred_Object_State_Slots()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var state = new Holder();
                        state.First.Event = item;
                        state.Second.Event = default;
                        state.Second.Field = default;
                        ThreadPool.QueueUserWorkItem(static value => { }, state);
                    });

                private sealed class Holder
                {
                    public Bucket First { get; } = new();
                    public Bucket Second { get; } = new();
                }

                private sealed class Bucket
                {
                    public RetryEvent Event { get; set; }
                    public RetryEvent Field;
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Honors_Overwritten_Object_Initializer_Slots()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var state = new Holder { Event = item };
                        state.Event = default;
                        ThreadPool.QueueUserWorkItem(static value => { }, state);
                    });

                private sealed class Holder
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Honors_Overwritten_Nested_Initializer_Slots()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var state = new Holder
                        {
                            Child = new Bucket { Event = item },
                        };
                        state.Child.Event = default;
                        ThreadPool.QueueUserWorkItem(static value => { }, state);
                    });

                private sealed class Holder
                {
                    public Bucket Child { get; set; } = new();
                }

                private sealed class Bucket
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Parent_Overwrites_Kill_Nested_Slots()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                    {
                        var state = new Holder();
                        state.Child.Event = item;
                        state.Child = new Bucket();
                        ThreadPool.QueueUserWorkItem(static value => { }, state);
                    });

                private sealed class Holder
                {
                    public Bucket Child { get; set; } = new();
                }

                private sealed class Bucket
                {
                    public RetryEvent Event { get; set; }
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_Coalescing_Assignment_Mutations()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent?[1];
                events[0] ??= item;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Does_Not_Treat_Coalescing_Assignment_As_Overwrite()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent?[1];
                events[0] = item;
                events[0] ??= default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Deferred_Mutations_Through_Loop_BackEdges()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                while (Environment.TickCount >= 0)
                {
                    ThreadPool.QueueUserWorkItem(
                        static (System.Collections.Generic.List<RetryEvent> state) =>
                            Console.WriteLine(state.Count),
                        events,
                        preferLocal: false);
                    events.Add(item);
                }
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Invoked_Local_Function_Mutations()
    {
        var invocationDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                void Add() => events.Add(item);

                Add();
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var assignmentDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent[1];
                void Set() => events[0] = item;

                Set();
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var parameterDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                void Add(System.Collections.Generic.List<RetryEvent> target) =>
                    target.Add(item);

                Add(events);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var erasedValueDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<object>();
                void Add(System.Collections.Generic.List<object> target, object value) =>
                    target.Add(value);

                Add(events, item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var nestedInvocationDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                void Inner() => events.Add(item);
                void Outer() => Inner();

                Outer();
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(
            invocationDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            assignmentDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            parameterDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            erasedValueDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await AssertRuleAsync(
            nestedInvocationDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Invoked_Local_Function_Cleanup()
    {
        var clearDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent> { item };
                void Clear() => events.Clear();

                Clear();
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var overwriteDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new RetryEvent[1];
                void Set() => events[0] = item;

                Set();
                events[0] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var conditionalDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent> { item };
                void MaybeClear(bool clear)
                {
                    if (clear)
                    {
                        events.Clear();
                    }
                }

                MaybeClear(false);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(clearDiagnostics).IsEmpty();
        await Assert.That(overwriteDiagnostics).IsEmpty();
        await AssertRuleAsync(
            conditionalDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Deferred_State_Cleared_Before_Scheduling()
    {
        var statements = new[]
        {
            "var events = new System.Collections.Generic.List<RetryEvent>(); events.Add(item); events.Clear();",
            "var events = new System.Collections.Generic.List<RetryEvent> { item }; events.Clear();",
            "System.Collections.Generic.List<RetryEvent> events = []; events.Add(item); events.Remove(item);",
            "var events = new System.Collections.Generic.List<RetryEvent>(); events.Add(item); events.Remove(item);",
            "var events = new System.Collections.Generic.Stack<RetryEvent>(); events.Push(item); events.Pop();",
            "var events = new System.Collections.Generic.Queue<RetryEvent>(); events.Enqueue(item); events.Dequeue();",
            "var events = new System.Collections.Generic.Queue<RetryEvent>(new[] { item }); events.Dequeue();",
            "System.Collections.Generic.List<RetryEvent> events = []; events.Insert(0, item); events.RemoveAt(0);",
            "var events = new System.Collections.Concurrent.ConcurrentDictionary<int, RetryEvent>(); events.TryAdd(0, item); events.TryRemove(0, out _);",
            "var events = new RetryEvent[1]; events[0] = item; events[0] = default;",
            "var events = new[] { item }; events[0] = default;",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Honors_Overwritten_Collection_Expression_Slots()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                System.Collections.Generic.List<RetryEvent> events = [item];
                events[0] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Honors_Overwritten_Append_Slots()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                events.Add(item);
                events[0] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Does_Not_Stabilize_Appends_Across_Branches()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                if (Environment.TickCount == 0)
                {
                    events.Add(default);
                }
                else
                {
                    events.Add(default);
                }

                events.Add(item);
                events.Add(default);
                events[2] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Invalidates_Slots_After_Structural_Mutations()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                System.Collections.Generic.List<RetryEvent?> events = [item];
                events.Insert(0, default);
                events[0] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Handles_Exact_RemoveAt_Kills()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                System.Collections.Generic.List<RetryEvent> events = [item];
                events.RemoveAt(0);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Invalidates_Slots_For_All_Structural_Mutations()
    {
        var statements = new[]
        {
            "events.InsertRange(0, new RetryEvent?[] { default }); events[0] = default;",
            "events.Remove(default); events[1] = default;",
            "events.RemoveAt(0); events[1] = default;",
            "events.RemoveRange(0, 1); events[1] = default;",
            "events.Reverse(); events[0] = default;",
            "events.Sort(); events[0] = default;",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    System.Collections.Generic.List<RetryEvent?> events = [default, item];
                    {{statement}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Context_Assignments_Kill_Prior_Slot_Contents()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var other = item with { };
                System.Collections.Generic.List<RetryEvent> events = [item];
                events[0] = other;
                events.Remove(other);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_Collection_Expression_Spreads_And_Sets()
    {
        var spreadDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var source = new[] { item };
                System.Collections.Generic.List<RetryEvent> events = [.. source];
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var setDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                System.Collections.Generic.HashSet<RetryEvent> events = [item];
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(spreadDiagnostics, "KEV014", DiagnosticSeverity.Warning);
        await AssertRuleAsync(setDiagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Handles_Set_Union_Removal()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.HashSet<RetryEvent>();
                events.UnionWith(new[] { item });
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Consumes_Duplicates_Across_Matching_Removals()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                events.Add(item);
                events.Add(item);
                events.Remove(item);
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Scopes_Removals_To_Nested_Collection_Receivers()
    {
        var mutations = new[]
        {
            "state[1].Clear();",
            "state[1].Remove(item);",
        };
        foreach (var mutation in mutations)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var state = new[]
                    {
                        new System.Collections.Generic.List<RetryEvent>(),
                        new System.Collections.Generic.List<RetryEvent>(),
                    };
                    state[0].Add(item);
                    {{mutation}}
                    ThreadPool.QueueUserWorkItem(static value => { }, state);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Matches_Duplicate_Values_Per_Receiver()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var state = new[]
                {
                    new System.Collections.Generic.List<RetryEvent>(),
                    new System.Collections.Generic.List<RetryEvent>(),
                };
                state[0].Add(item);
                state[1].Add(item);
                state[0].Remove(item);
                state[1].Clear();
                ThreadPool.QueueUserWorkItem(static value => { }, state);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Does_Not_Treat_Dictionary_Keys_As_Removed_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<RetryEvent, RetryEvent>();
                events.Add(default, item);
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Matches_Dictionary_Removals_To_Retained_Keys()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<RetryEvent, RetryEvent>();
                events.Add(item, default);
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_Concurrent_Dictionary_Upserts()
    {
        var mutations = new[]
        {
            "events.GetOrAdd(item, default(RetryEvent));",
            "events.AddOrUpdate(item, default(RetryEvent), static (_, current) => current);",
        };
        foreach (var mutation in mutations)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new System.Collections.Concurrent.ConcurrentDictionary<RetryEvent, RetryEvent>();
                    {{mutation}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Ignores_Concurrent_Dictionary_Factory_Arguments()
    {
        var mutations = new[]
        {
            "events.GetOrAdd(0, static (_, state) => state.RetryNumber, item);",
            "events.AddOrUpdate(0, static (_, state) => state.RetryNumber, static (_, current, state) => current + state.RetryNumber, item);",
        };
        foreach (var mutation in mutations)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
                    {{mutation}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Tracks_Concurrent_Dictionary_Factory_Returns()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Concurrent.ConcurrentDictionary<int, object>();
                events.GetOrAdd(0, _ => item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Tracks_Concurrent_Dictionary_TryUpdate_Values()
    {
        var retainedDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Concurrent.ConcurrentDictionary<int, object>();
                var previous = new object();
                events.TryAdd(0, previous);
                events.TryUpdate(0, item, previous);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var comparisonDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Concurrent.ConcurrentDictionary<int, object>();
                events.TryAdd(0, new object());
                events.TryUpdate(0, new object(), item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(
            retainedDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
        await Assert.That(comparisonDiagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_Dictionary_Indexer_Keys()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<RetryEvent, RetryEvent>();
                events[item] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Distinguishes_Mixed_Type_Dictionary_Keys()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<object, RetryEvent>();
                events[1] = item;
                events["1"] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Distinguishes_Enum_Type_Dictionary_Keys()
    {
        var diagnostics = await AnalyzeBodyAsync(
            """
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<object, RetryEvent>();
                events[First.Key] = item;
                events[Second.Key] = default;
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """,
            """
            private enum First
            {
                Key,
            }

            private enum Second
            {
                Key,
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Matches_Dictionary_Initializer_Removals()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Dictionary<int, RetryEvent>
                {
                    { 0, item },
                };
                events.Remove(0);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Preserves_Nested_Receiver_Alias_Paths()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var state = new[]
                {
                    new System.Collections.Generic.List<RetryEvent>(),
                    new System.Collections.Generic.List<RetryEvent>(),
                };
                state[0].Add(item);
                var second = state[1];
                second.Clear();
                ThreadPool.QueueUserWorkItem(static value => { }, state);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Distinguishes_Removal_Value_Receivers()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var other = item with { };
                var contexts = new System.Collections.Generic.List<KevlarContext>();
                contexts.Add(item.Context);
                contexts.Remove(other.Context);
                ThreadPool.QueueUserWorkItem(static state => { }, contexts);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Handles_Mutually_Exclusive_Matching_Adds()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                if (Environment.TickCount == 0)
                {
                    events.Add(item);
                }
                else
                {
                    events.Add(item);
                }

                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Handles_Exclusive_Matching_Adds_In_Loops()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                while (Environment.TickCount >= 0)
                {
                    if (Environment.TickCount == 0)
                    {
                        events.Add(item);
                    }
                    else
                    {
                        events.Add(item);
                    }

                    events.Remove(item);
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                }
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Keeps_Deferred_State_When_Removals_Leave_Retained_Values()
    {
        var statements = new[]
        {
            "var events = new System.Collections.Generic.List<RetryEvent>(); events.Add(item); events.Add(item); events.Remove(item);",
            "var events = new System.Collections.Generic.List<RetryEvent> { item, item }; events.Remove(item);",
            "System.Collections.Generic.List<RetryEvent> events = [item, item]; events.Remove(item);",
            "var events = new System.Collections.Generic.List<RetryEvent>(); events.AddRange(new[] { item, item }); events.Remove(item);",
            "var events = new System.Collections.Generic.Queue<RetryEvent>(); events.Enqueue(default); events.Enqueue(item); events.Dequeue();",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    {{statement}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Combines_Constructor_And_Initializer_Origins()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>(
                    new[] { item })
                {
                    item,
                };
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Honors_Singleton_Bulk_Mutation_Cardinality()
    {
        var arrayDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                events.AddRange(new[] { item });
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var collectionDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                events.AddRange([item]);
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);
        var spreadDiagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                events.AddRange([.. new[] { item }]);
                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(arrayDiagnostics).IsEmpty();
        await Assert.That(collectionDiagnostics).IsEmpty();
        await AssertRuleAsync(
            spreadDiagnostics,
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Does_Not_Multiply_Repeated_Fixed_Slot_Assignments()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                System.Collections.Generic.List<RetryEvent?> events = [default];
                for (var index = 0; index < 2; index++)
                {
                    events[0] = item;
                }

                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Preserves_Repeated_Loop_Retentions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                for (var index = 0; index < 2; index++)
                {
                    events.Add(item);
                }

                events.Remove(item);
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Preserves_Repeated_Queue_Retentions()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.Queue<RetryEvent>();
                for (var index = 0; index < 2; index++)
                {
                    events.Enqueue(item);
                }

                events.Dequeue();
                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Handles_Per_Iteration_Matching_Removals()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                for (var index = 0; index < 2; index++)
                {
                    events.Add(item);
                    events.Remove(item);
                }

                ThreadPool.QueueUserWorkItem(static state => { }, events);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Tracks_LinkedList_Node_Values()
    {
        var mutations = new[]
        {
            "events.AddFirst(new System.Collections.Generic.LinkedListNode<RetryEvent>(item));",
            "events.AddLast(new System.Collections.Generic.LinkedListNode<RetryEvent>(item));",
            "var anchor = events.AddFirst(default(RetryEvent)); events.AddBefore(anchor, new System.Collections.Generic.LinkedListNode<RetryEvent>(item));",
            "var anchor = events.AddFirst(default(RetryEvent)); events.AddAfter(anchor, new System.Collections.Generic.LinkedListNode<RetryEvent>(item));",
        };
        foreach (var mutation in mutations)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new System.Collections.Generic.LinkedList<RetryEvent>();
                    {{mutation}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Handles_LinkedList_Endpoint_Removals()
    {
        var statements = new[]
        {
            "events.AddFirst(item); events.RemoveFirst();",
            "events.AddLast(item); events.RemoveLast();",
        };
        foreach (var statement in statements)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    var events = new System.Collections.Generic.LinkedList<RetryEvent>();
                    {{statement}}
                    ThreadPool.QueueUserWorkItem(static state => { }, events);
                });
                """);

            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV014_Ignores_Deferred_State_Mutations_On_Exiting_Paths()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                var events = new System.Collections.Generic.List<RetryEvent>();
                if (Environment.TickCount == 0)
                {
                    events.Add(item);
                    return;
                }

                ThreadPool.QueueUserWorkItem(
                    static (System.Collections.Generic.List<RetryEvent> state) =>
                        Console.WriteLine(state.Count),
                    events,
                    preferLocal: false);
            });
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Ignores_Framework_Collection_Capacity_State()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (System.Collections.Generic.List<RetryEvent> state) =>
                        Console.WriteLine(state.Count),
                    new System.Collections.Generic.List<RetryEvent>(capacity: 1),
                    preferLocal: false));
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Collection_Expression_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (object[] state) =>
                        Console.WriteLine(((KevlarContext)state[0]).ShieldName),
                    [item.Context],
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Object_Initializer_Values()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class WorkState
            {
                public object? Context { get; set; }
            }

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item)
                {
                    var state = new WorkState { Context = (object)item.Context };
                    ThreadPool.QueueUserWorkItem(
                        static value => Console.WriteLine(((KevlarContext)value.Context!).ShieldName),
                        state,
                        preferLocal: false);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Anonymous_Object_State_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static state =>
                        Console.WriteLine(((KevlarContext)state.State).ShieldName),
                    new { State = (object)item.Context },
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Record_With_State_Values()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed record WorkState(object? State);

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item)
                {
                    var template = new WorkState(null);
                    ThreadPool.QueueUserWorkItem(
                        static state =>
                            Console.WriteLine(((KevlarContext)state.State!).ShieldName),
                        template with { State = (object)item.Context },
                        preferLocal: false);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Copied_Record_State_Values()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed record WorkState(object? State, int Flag);

            public sealed class TestSubject
            {
                public void Observe(RetryEvent item)
                {
                    var template = new WorkState((object)item.Context, 0);
                    ThreadPool.QueueUserWorkItem(
                        static state =>
                            Console.WriteLine(((KevlarContext)state.State!).ShieldName),
                        template with { Flag = 1 },
                        preferLocal: false);
                }
            }
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Erased_Collection_Initializer_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (System.Collections.Generic.List<object> state) =>
                        Console.WriteLine(((KevlarContext)state[0]).ShieldName),
                    new System.Collections.Generic.List<object> { item.Context },
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Follows_Stable_Deferred_Delegate_Initializers()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
            {
                Action work = () => Console.WriteLine(item.Context.ShieldName);
                _ = Task.Run(work);
            });
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Framework_Constructor_State_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (System.Collections.Generic.List<RetryEvent> state) =>
                        Console.WriteLine(state[0].Context.ShieldName),
                    new System.Collections.Generic.List<RetryEvent>(new[] { item }),
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Inspects_Metadata_Container_State_Values()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            _ = Shield.Retry(options => options.OnRetry = item =>
                ThreadPool.QueueUserWorkItem(
                    static (System.Collections.Generic.KeyValuePair<int, RetryEvent> state) =>
                        Console.WriteLine(state.Value.Context.ShieldName),
                    new System.Collections.Generic.KeyValuePair<int, RetryEvent>(0, item),
                    preferLocal: false));
            """);

        await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task KEV014_Ignores_Constructor_Snapshots_In_Deferred_State()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class RetrySnapshot
            {
                public RetrySnapshot(RetryEvent item)
                {
                    RetryNumber = item.RetryNumber;
                }

                public int RetryNumber { get; }
            }

            public sealed class TestSubject
            {
                public void Configure() =>
                    _ = Shield.Retry(options => options.OnRetry = item =>
                        ThreadPool.QueueUserWorkItem(
                            static (RetrySnapshot snapshot) =>
                                Console.WriteLine(snapshot.RetryNumber),
                            new RetrySnapshot(item),
                            preferLocal: false));
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV014_Inspects_Conditional_Deferred_Delegate_Initializers()
    {
        var initializers = new[]
        {
            "enabled ? (Action)(() => Console.WriteLine(item.Context.ShieldName)) : () => { }",
            "enabled ? (Action)(() => { }) : () => Console.WriteLine(item.Context.ShieldName)",
            "existing ?? (() => Console.WriteLine(item.Context.ShieldName))",
        };
        foreach (var initializer in initializers)
        {
            var diagnostics = await AnalyzeBodyAsync($$"""
                var enabled = true;
                Action? existing = null;
                _ = Shield.Retry(options => options.OnRetry = item =>
                {
                    Action work = {{initializer}};
                    _ = Task.Run(work);
                });
                """);

            await AssertRuleAsync(diagnostics, "KEV014", DiagnosticSeverity.Warning);
        }
    }

    [Test]
    public async Task KEV014_Inspects_Switch_Selected_Deferred_Delegates()
    {
        var diagnostics = await AnalyzeBodyAsync("""
            var mode = 0;
            _ = Shield.Retry(options => options.OnRetry = item =>
                Task.Run(mode switch
                {
                    0 => (Action)(() => Console.WriteLine(item.Context.ShieldName)),
                    _ => () => { },
                }));
            """);

        await AssertRuleAsync(
            Without(diagnostics, "KEV013"),
            "KEV014",
            DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task Public_Surface_Uses_One_Hedge_Stem()
    {
        var legacyNames = typeof(PipelineHazardAnalyzer).Assembly.ExportedTypes
            .SelectMany(static type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(static member => member.Name)
                .Append(type.Name))
            .Where(static name => name.Contains("Hedging", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();

        await Assert.That(legacyNames).IsEmpty();
    }

    [Test]
    public async Task Custom_Strategy_Can_Declare_Single_Invocation_Without_Diagnostics()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class SingleInvocationStrategy : Strategy
            {
                protected override bool InvokesContinuationAtMostOnce => true;

                public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
                    Continuation<T, TState> next,
                    KevlarContext context) => next.InvokeAsync(context);
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Typed_Fallback_Keeps_Only_The_Bare_And_Configure_Tiers()
    {
        var supported = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    _ = Shield.For<int>().FallbackTo(42);
                    _ = Shield.For<int>().Fallback(_ => new ValueTask<int>(42));
                    _ = Shield.For<int>().Fallback((_, _) => new ValueTask<int>(42));
                    _ = Shield.For<int>().FallbackTo(42, options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().Fallback(_ => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().Fallback((_, _) => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42);
                    _ = Shield.For<int>().When<Exception>().Fallback(_ => new ValueTask<int>(42));
                    _ = Shield.For<int>().When<Exception>().Fallback((_, _) => new ValueTask<int>(42));
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42, options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback(_ => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback((_, _) => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                }
            }
            """));
        var supportedErrors = supported.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(supportedErrors).IsEmpty();

        var legacyCallback = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    Action<FallbackEvent<int>> callback = _ => { };
                    _ = Shield.For<int>().FallbackTo(42, callback);
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42, callback);
                }
            }
            """));
        var legacyErrors = legacyCallback.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(legacyErrors).Count().IsEqualTo(2);
        await Assert.That(legacyErrors).All(static diagnostic => diagnostic.Id == "CS1503");
    }

    [Test]
    public async Task KEV004_Flags_Inline_Stateful_Shields_For_All_Execution_Forms()
    {
        var cases = new[]
        {
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            "await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.ConcurrencyLimit(2).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.For<int>().ConcurrencyLimit(2).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "_ = Shield.Empty.UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!).Execute(_ => 1);",
            "await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => Task.FromResult(1));",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteOutcomeAsync(_ => Task.FromResult(1));",
            "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => { });",
            "_ = Shield.RateLimit(10, TimeSpan.FromSeconds(1)).Execute(1, (state, _) => state);",
            "await Shield.ConcurrencyLimit(2).ExecuteAsync(_ => ValueTask.CompletedTask);",
            "await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(1, (state, _) => Task.FromResult(state));",
            "await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteOutcomeAsync(1, (state, _) => new ValueTask<int>(state));",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(1, (state, _) => state);",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(1, (state, _) => Task.FromResult(state));",
            "await Shield.For<int>().ConcurrencyLimit(2).ExecuteOutcomeAsync(1, (state, _) => new ValueTask<int>(state));",
        };

        await AssertEachAsync(cases, "KEV004", "KEV012", "KEV013");
    }

    [Test]
    public async Task KEV004_Flags_Single_Use_Locals_Aliases_And_Extension_Syntax()
    {
        var cases = new[]
        {
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); _ = shield.Execute(_ => 1);",
            "var shield = Shield.RateLimit(10, TimeSpan.FromSeconds(1)); var alias = shield; await alias.ExecuteAsync(_ => new ValueTask<int>(1));",
            "_ = ShieldExtensions.ConcurrencyLimit(Shield.Empty, 2).Execute(_ => 1);",
            "Func<ValueTask<int>> run = () => Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1)); await run();",
        };

        await AssertEachAsync(cases, "KEV004");

        var typeAlias = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public int Run() => KShield.RateLimit(10, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """);
        await AssertRuleAsync(typeAlias, "KEV004");
    }

    [Test]
    public async Task KEV004_Flags_Stateful_Composition_Operands()
    {
        var cases = new[]
        {
            "_ = Shield.Empty.Wrap(Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.Compose(Shield.Empty, Shield.RateLimit(10, TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.Compose([Shield.ConcurrencyLimit(2)]).Execute(_ => 1);",
            "_ = Shield.Compose([.. new[] { Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)) }]).Execute(_ => 1);",
            "var parts = new[] { Shield.RateLimit(10, TimeSpan.FromSeconds(1)) }; _ = Shield.Compose(parts).Execute(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV004");
    }

    [Test]
    public async Task KEV004_Flags_Per_Execution_Partition_Providers()
    {
        var cases = new[]
        {
            "_ = new PartitionedShield<string>(_ => Shield.Empty).GetShield(\"tenant\").Execute(_ => 1);",
            "new PartitionedShield<string>(_ => Shield.Fallback(static _ => ValueTask.CompletedTask)).GetShield(\"tenant\").Execute(static _ => { });",
            "await new PartitionedShield<string, int>(_ => Shield<int>.Empty).GetShield(\"tenant\").ExecuteAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); await partitions.GetShield(\"tenant\").ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); var shield = partitions.GetShield(\"tenant\"); _ = shield.Execute(_ => 1);",
            "await (await PartitionedShield<string>.CreateAsync(_ => new ValueTask<Shield>(Shield.Empty)).GetShieldAsync(\"tenant\")).ExecuteAsync(_ => new ValueTask<int>(1));",
            "var partitions = PartitionedShield<string, int>.CreateAsync(_ => new ValueTask<Shield<int>>(Shield<int>.Empty)); await (await partitions.GetShieldAsync(\"tenant\")).ExecuteAsync(_ => new ValueTask<int>(1));",
        };

        await AssertEachAsync(cases, "KEV004");
    }

    [Test]
    public async Task KEV004_Skips_Stateless_Reused_And_Ambiguous_Shields()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(2).Execute(_ => 1);",
            "await Shield.Timeout(TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.For<int>().FallbackTo(0).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); _ = shield.Execute(_ => 1); _ = shield.Execute(_ => 2);",
            "var shield = CreateShield(); _ = shield.Execute(_ => 1);",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); _ = partitions.GetShield(\"one\"); _ = partitions.GetShield(\"two\").Execute(_ => 1);",
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); Func<int> run = () => shield.Execute(_ => 1); _ = run();",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static Shield CreateShield() => Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV004_Skips_Fields_Parameters_Factories_Registrations_And_Test_Assemblies()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private static readonly Shield Shared = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));
                private readonly PartitionedShield<string> _partitions = new(_ => Shield.Empty);

                public int FromField() => Shared.Execute(_ => 1);
                public int FromParameter(Shield shield) => shield.Execute(_ => 1);
                public int FromPartitionField() => _partitions.GetShield("tenant").Execute(_ => 1);
                public Shield Create() => Shield.RateLimit(10, TimeSpan.FromSeconds(1));
                public void Configure() => Register(Shield.ConcurrencyLimit(2));
                private static void Register(Shield shield) { }
            }
            """);
        var testAssemblyDiagnostics = await AnalyzeBodyAsync(
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            assemblyName: "Sample.Tests");
        var testMethodDiagnostics = await AnalyzeSourceAsync("""
            namespace Xunit
            {
                public sealed class FactAttribute : Attribute { }
            }

            public sealed class TestSubject
            {
                [Xunit.Fact]
                public int IsolatedExecution() =>
                    Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(testAssemblyDiagnostics).IsEmpty();
        await Assert.That(testMethodDiagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV004_Ignores_Lookalikes_Generated_Code_And_Malformed_Code()
    {
        var lookalike = await AnalyzeSourceAsync("""
            public sealed class OtherShield
            {
                public static OtherShield CircuitBreaker() => new();
                public int Execute(Func<CancellationToken, int> action) => action(default);
            }

            public class TestSubject
            {
                public int Run() => OtherShield.CircuitBreaker().Execute(_ => 1);
            }
            """);
        var generated = await AnalyzeBodyAsync(
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            isGenerated: true);
        var malformed = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ =>
                }
            }
            """, allowCompilationErrors: true);
        var malformedAttribute = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                [MissingTest]
                public int Run() =>
                    Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """, allowCompilationErrors: true);

        await Assert.That(lookalike).IsEmpty();
        await Assert.That(generated).IsEmpty();
        await Assert.That(malformed.Any(diagnostic => diagnostic.Id == "AD0001")).IsFalse();
        await Assert.That(malformedAttribute.Any(diagnostic => diagnostic.Id == "AD0001")).IsFalse();
    }

    [Test]
    public async Task KEV004_Diagnostic_Contract_Location_And_Suppression_Are_Exact()
    {
        const string construction = "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1))";
        var source = $$"""
            public class TestSubject
            {
                public int Run() => {{construction}}.Execute(_ => 1);
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public int Run()
                {
            #pragma warning disable KEV004 // Isolated execution is intentional.
                    return Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            #pragma warning restore KEV004
                }
            }
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV004");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'CircuitBreaker' creates resilience state for one execution. Store and reuse the shield or partition provider as a field, singleton/keyed DI registration, or registry entry.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(construction, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(construction.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV004_Concurrent_Analyzer_Runs_Are_Deterministic()
    {
        var source = CreateSource("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    _ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
                    await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));
                    await new PartitionedShield<string>(_ => Shield.Empty).GetShield("tenant").ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
                }
            }
            """);
        var compilation = CreateCompilation(source);
        var runs = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => GetAnalyzerDiagnosticsAsync(compilation)));
        var expected = runs[0]
            .Where(diagnostic => diagnostic.Id == "KEV004")
            .Select(diagnostic => diagnostic.Location.SourceSpan)
            .OrderBy(span => span.Start)
            .ToArray();

        await Assert.That(expected.Length).IsEqualTo(3);
        foreach (var run in runs)
        {
            var actual = run
                .Where(diagnostic => diagnostic.Id == "KEV004")
                .Select(diagnostic => diagnostic.Location.SourceSpan)
                .OrderBy(span => span.Start)
                .ToArray();
            await Assert.That(actual).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task Void_Fallback_Preserves_The_Shield_Type_And_Fluent_Surface()
    {
        var compilation = CreateCompilation(CreateSource("""
            public sealed class TestSubject
            {
                public async Task Run()
                {
                    Shield fromFactory = Shield.Fallback(static _ => ValueTask.CompletedTask);
                    Shield fromExtension = Shield.Retry(1).Fallback(static _ => ValueTask.CompletedTask);
                    Shield fromBuilder = Shield.When<InvalidOperationException>()
                        .Fallback(static (_, _) => ValueTask.CompletedTask);
                    Shield chained = fromExtension
                        .Retry()
                        .Timeout(TimeSpan.FromSeconds(1))
                        .CircuitBreaker(2, TimeSpan.FromSeconds(1))
                        .RateLimit(10, TimeSpan.FromSeconds(1))
                        .ConcurrencyLimit(2)
                        .Hedge(0, TimeSpan.Zero)
                        .When<TimeoutException>()
                        .Or<InvalidOperationException>()
                        .Retry(1, Backoff.None)
                        .WhenAnyError()
                        .WithName("void")
                        .WithTimeProvider(TimeProvider.System);
                    Shield outer = Shield.Timeout(TimeSpan.FromSeconds(1)).Wrap(chained);
                    Shield inner = chained.Wrap(Shield.Retry(1));

                    fromFactory.Execute(static _ => { });
                    fromBuilder.Execute(1, static (_, _) => { });
                    await outer.ExecuteAsync(static _ => ValueTask.CompletedTask);
                    await inner.ExecuteAsync(1, static (_, _) => ValueTask.CompletedTask);
                    await chained.ExecuteAsync(static _ => Task.CompletedTask);
                    await chained.ExecuteWithContextAsync(static _ => ValueTask.CompletedTask);
                    await chained.ExecuteWithContextAsync(1, static (_, _) => { }, static (_, _) => ValueTask.CompletedTask);
                }
            }
            """));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task KEV005_Flags_Void_Fallback_For_Each_Result_Execution_Method()
    {
        var cases = new[]
        {
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => new ValueTask<int>(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContext(0, static (_, _) => { }, static (_, _) => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => new ValueTask<int>(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => Task.FromResult(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => Task.FromResult(1));",
        };

        await AssertEachAsync(cases, "KEV005", "KEV012", "KEV013");
    }

    [Test]
    public async Task KEV005_Follows_Aliases_Builders_Result_Lifts_And_Composition()
    {
        var cases = new[]
        {
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = shield.Execute(static _ => 1);",
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); var alias = shield; _ = alias.Execute(static _ => 1);",
            "var shield = Shield.When<InvalidOperationException>().Fallback(static (_, _) => ValueTask.CompletedTask); _ = shield.Execute(static _ => 1);",
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = shield.For<int>().Execute(static _ => 1);",
            "_ = Shield.Empty.Wrap(Shield.Empty.Fallback(static _ => ValueTask.CompletedTask)).Execute(static _ => 1);",
            "var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = Shield.Compose(Shield.Empty, fallback).Execute(static _ => 1);",
        };

        await AssertEachAsync(cases, "KEV005", "KEV012", "KEV013");
    }

    [Test]
    public async Task KEV005_Follows_The_Rate_Limiter_Adapter()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask)" +
            ".UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!).Execute(static _ => 1);");

        await AssertRuleAsync(Without(diagnostics, "KEV004", "KEV012", "KEV013"), "KEV005");
    }

    [Test]
    public async Task KEV005_Skips_Typed_Fallbacks_And_Void_Executions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().FallbackTo(0).Execute(static _ => 1);",
            "Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => { });",
            "await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => ValueTask.CompletedTask);",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcome(static _ => { });",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcome(0, static (_, _) => { });",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(0, static (_, _) => ValueTask.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => Task.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(0, static (_, _) => Task.CompletedTask);",
            "Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContext(0, static (_, _) => { }, static (_, _) => { });",
            "await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => ValueTask.CompletedTask);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(Without(diagnostics, "KEV012", "KEV013")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV005_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);");
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV005 // Result use is validated elsewhere.
            _ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);
            #pragma warning restore KEV005
            """);

        var kev005Diagnostics = diagnostics.Where(static diagnostic => diagnostic.Id == "KEV005").ToArray();
        await Assert.That(kev005Diagnostics.Length).IsEqualTo(1);
        var diagnostic = kev005Diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV005");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Fallback on a non-generic Shield applies only to void executions. " +
            "For executions that return a value, build a result-aware shield with " +
            "Shield.For<T>() and use its Fallback overloads.");
        await Assert.That(Without(suppressed, "KEV012", "KEV013")).IsEmpty();
    }

    [Test]
    public async Task KEV002_Flags_Known_Hedging_Shields_Used_Synchronously()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(1, TimeSpan.Zero).Execute(_ => 1);",
            "Shield.Empty.Hedge(1, TimeSpan.Zero).Execute(_ => { });",
            "_ = Shield.For<int>().Hedge(1, TimeSpan.Zero).Execute(_ => 1);",
            "var shield = Shield.Hedge(1, TimeSpan.Zero); var alias = shield; _ = alias.Execute(_ => 1);",
            "Shield? shield = Shield.Hedge(1, TimeSpan.Zero); _ = shield?.Execute(_ => 1);",
            "var shield = ShieldExtensions.Hedge(Shield.Empty, 2, TimeSpan.Zero); _ = shield.Execute(_ => 1);",
            "_ = Shield.Empty.Wrap(Shield.Hedge(1, TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Compose(Shield.Empty, Shield.Hedge(1, TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Compose([Shield.Hedge(1, TimeSpan.Zero)]).Execute(_ => 1);",
            "_ = Shield.Compose([.. new[] { Shield.Hedge(1, TimeSpan.Zero) }]).Execute(_ => 1);",
            "var parts = new[] { Shield.Hedge(1, TimeSpan.Zero) }; _ = Shield.Compose(parts).Execute(_ => 1);",
            "_ = Shield<int>.Empty.Wrap(Shield.Hedge(1, TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Hedge(options => options.MaxHedgedAttempts = 1).Execute(_ => 1);",
            "_ = Shield.Hedge(options => options.MaxHedgedAttempts += 0).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; options.MaxHedgedAttempts += 1; }).Execute(_ => 1);",
            "var replacement = new HedgeOptions(); _ = Shield.Hedge(options => { options = replacement; options.MaxHedgedAttempts = 0; }).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var alias = options; alias.MaxHedgedAttempts = 1; }).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var aliases = new[] { options }; aliases[0].MaxHedgedAttempts = 1; }).Execute(_ => 1);",
            "var mutator = new System.Collections.Generic.Dictionary<HedgeOptions, int>(); _ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; mutator[options] = 1; }).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var (alias, _) = (options, 0); alias.MaxHedgedAttempts = 1; }).Execute(_ => 1);",
            "var skip = true; _ = Shield.Hedge(options => { _ = skip switch { true => 1, false => (options.MaxHedgedAttempts = 0) }; }).Execute(_ => 1);",
            "var other = new HedgeOptions(); _ = Shield.Hedge(options => other.MaxHedgedAttempts = 0).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { Action deferred = () => options.MaxHedgedAttempts = 0; }).Execute(_ => 1);",
            "var disable = false; _ = Shield.Hedge(options => { _ = disable && (options.MaxHedgedAttempts = 0) == 0; }).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; _ = new WeakReference<HedgeOptions>(options); }).Execute(_ => 1);",
            "_ = Shield.Hedge(async options => { options.MaxHedgedAttempts = 1; await Task.Yield(); options.MaxHedgedAttempts = 0; }).Execute(_ => 1);",
            "_ = Shield.Hedge(1, TimeSpan.Zero).ExecuteOutcome(_ => 1);",
            "_ = Shield.For<int>().Hedge(1, TimeSpan.Zero).ExecuteOutcome(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV002", "KEV006");
    }

    [Test]
    public async Task KEV002_Supports_Type_Aliases_And_Generic_Result_Shields()
    {
        var aliasDiagnostics = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public int Run() => KShield.Hedge(1, TimeSpan.Zero).Execute(_ => 1);
            }
            """);
        var genericDiagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public T Run<T>() => Shield.For<T>().Hedge(1, TimeSpan.Zero).Execute(_ => default!);
            }
            """);

        await AssertRuleAsync(Without(aliasDiagnostics, "KEV006"), "KEV002");
        await AssertRuleAsync(genericDiagnostics, "KEV002");
    }

    [Test]
    public async Task KEV002_Skips_Async_Unknown_And_Reassigned_Shields()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(1, TimeSpan.Zero).ExecuteAsync(_ => new ValueTask<int>(1));",
            "var shield = CreateShield(); _ = shield.Execute(_ => 1);",
            "var shield = Shield.Hedge(1, TimeSpan.Zero); shield = Shield.Empty; _ = shield.Execute(_ => 1);",
            "_ = Shield.Empty.Execute(_ => 1);",
            "_ = Shield.Hedge(0, TimeSpan.Zero).Execute(_ => 1);",
            "var attempts = DateTime.Now.Day; _ = Shield.Hedge(attempts, TimeSpan.Zero).Execute(_ => 1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, "private static Shield CreateShield() => Shield.Hedge(1, TimeSpan.Zero);");
            await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV002_Skips_Aliased_And_Mutated_Shield_Arrays()
    {
        var cases = new[]
        {
            "var parts = new[] { Shield.Hedge(1, TimeSpan.Zero) }; parts[0] = Shield.Empty; _ = Shield.Compose(parts).Execute(_ => 1);",
            "var parts = new[] { Shield.Hedge(1, TimeSpan.Zero) }; var alias = parts; alias[0] = Shield.Empty; _ = Shield.Compose(parts).Execute(_ => 1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV006_Flags_Hedging_On_Untyped_Shields_And_Builders()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(1, TimeSpan.Zero);",
            "_ = Shield.Hedge(options => options.MaxHedgedAttempts = 1);",
            "_ = Shield.Hedge(options => options.MaxHedgedAttempts += 0);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; options.MaxHedgedAttempts += 1; });",
            "var replacement = new HedgeOptions(); _ = Shield.Hedge(options => { options = replacement; options.MaxHedgedAttempts = 0; });",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var alias = options; alias.MaxHedgedAttempts = 1; });",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var aliases = new[] { options }; aliases[0].MaxHedgedAttempts = 1; });",
            "var mutator = new System.Collections.Generic.Dictionary<HedgeOptions, int>(); _ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; mutator[options] = 1; });",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; var (alias, _) = (options, 0); alias.MaxHedgedAttempts = 1; });",
            "var skip = true; _ = Shield.Hedge(options => { _ = skip switch { true => 1, false => (options.MaxHedgedAttempts = 0) }; });",
            "_ = Shield.Empty.Hedge(1, TimeSpan.Zero);",
            "_ = Shield.Empty.Hedge(options => options.MaxHedgedAttempts = 1);",
            "_ = ShieldExtensions.Hedge(Shield.Empty, 2, TimeSpan.Zero);",
            "_ = Shield.When<InvalidOperationException>().Hedge(1, TimeSpan.Zero);",
            "_ = Shield.When<InvalidOperationException>().Hedge(options => options.MaxHedgedAttempts = 1);",
            "_ = Shield.Timeout(TimeSpan.FromSeconds(1)).Hedge(1, TimeSpan.Zero).Retry(1);",
            "var other = new HedgeOptions(); _ = Shield.Hedge(options => other.MaxHedgedAttempts = 0);",
            "_ = Shield.Hedge(options => { Action deferred = () => options.MaxHedgedAttempts = 0; });",
            "var disable = false; _ = Shield.Hedge(options => { _ = disable && (options.MaxHedgedAttempts = 0) == 0; });",
            "var keepDefault = true; _ = Shield.Hedge(options => { _ = keepDefault || (options.MaxHedgedAttempts = 0) == 0; });",
            "int? configured = 1; _ = Shield.Hedge(options => { _ = configured ?? (options.MaxHedgedAttempts = 0); });",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; _ = new WeakReference<HedgeOptions>(options); });",
            "_ = Shield.Hedge(async options => { options.MaxHedgedAttempts = 1; await Task.Yield(); options.MaxHedgedAttempts = 0; });",
        };

        await AssertEachAsync(cases, "KEV006");
    }

    [Test]
    public async Task KEV006_Skips_Zero_Attempt_Hedges()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(0, TimeSpan.Zero);",
            "_ = Shield.Empty.Hedge(0, TimeSpan.Zero);",
            "_ = ShieldExtensions.Hedge(Shield.Empty, 0, TimeSpan.Zero);",
            "_ = Shield.Timeout(TimeSpan.FromSeconds(1)).Hedge(0, TimeSpan.Zero);",
            "_ = Shield.Hedge(options => options.MaxHedgedAttempts = 0);",
            "_ = Shield.Empty.Hedge(options => options.MaxHedgedAttempts = 0);",
            "_ = Shield.When<InvalidOperationException>().Hedge(options => { options.Delay = TimeSpan.Zero; options.MaxHedgedAttempts = 0; });",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV006_Skips_Typed_Shields_And_Typed_Builders()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Hedge(1, TimeSpan.Zero);",
            "_ = Shield.For<int>().Hedge(options => options.MaxHedgedAttempts = 1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Hedge(1, TimeSpan.Zero);",
            "_ = Shield.For<int>().WhenResult(0).Hedge(options => options.MaxHedgedAttempts = 1);",
            "_ = Shield.Empty.For<int>().Hedge(1, TimeSpan.Zero);",
            "_ = Shield.For<int>().Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV006_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string construction = "Shield.Hedge(1, TimeSpan.Zero)";
        var source = $$"""
            public class TestSubject
            {
                public Shield Build() => {{construction}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV006 // The documented action is idempotent.
            _ = Shield.Hedge(1, TimeSpan.Zero);
            #pragma warning restore KEV006
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV006");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Hedging on an untyped Shield runs the execution delegate more than once, concurrently. "
            + "Build a result-aware shield with Shield.For<T>() so result clauses can select the "
            + "winning attempt, or confirm the action is idempotent.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(construction, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(construction.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV007_Flags_Clause_Builders_That_Never_Reach_A_Strategy()
    {
        var cases = new[]
        {
            "Shield.When<InvalidOperationException>();",
            "Shield.When<InvalidOperationException>().Or<TimeoutException>();",
            "Shield.WhenContext((HandlingEvent handling) => handling.Attempt == 0);",
            "Shield.When<InvalidOperationException>().Or<TimeoutException>().Or(static exception => exception is null);",
            "_ = Shield.When<InvalidOperationException>();",
            "_ = Shield.Empty.When<InvalidOperationException>();",
            "_ = Shield.For<int>().WhenResult(static value => value < 0);",
            "_ = Shield.For<int>().WhenResultIsDefault().Or<TimeoutException>();",
            "var clause = Shield.When<InvalidOperationException>().Or<TimeoutException>();",
            "var clause = Shield.For<int>().When<InvalidOperationException>();",

            // Builders are immutable, so an Or… whose new builder is dropped extends nothing —
            // the stored builder still carries InvalidOperationException alone.
            "var clause = Shield.When<InvalidOperationException>(); clause.Or<TimeoutException>(); _ = clause.Retry(1);",
            "var clause = Shield.For<int>().When<InvalidOperationException>(); clause.OrResultIsDefault(); _ = clause.Retry(1);",
        };

        // The int cases also draw KEV010: a default-result clause on a value type is its own hint.
        await AssertEachAsync(cases, "KEV007", "KEV010");
    }

    [Test]
    public async Task KEV007_Flags_A_Clause_Replaced_Before_Any_Reactive_Strategy()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).WhenAnyError().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(static options => options.Timeout = TimeSpan.FromSeconds(1)).WhenAnyError().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).When<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().RateLimit(1, TimeSpan.FromSeconds(1)).When<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Use((Strategy)null!).When<TimeoutException>().Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).WhenResult(static value => value < 0).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Use((Strategy)null!).WhenResult(static value => value < 0).Retry(1);",
        };

        await AssertEachAsync(cases, "KEV007", "KEV004");
    }

    [Test]
    public async Task KEV007_Leaves_Consumed_And_Escaping_Clauses_Alone()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Fallback(static (_, _) => default);",
            "_ = Shield.When<InvalidOperationException>().Fallback(static _ => default);",
            "_ = Shield.When<InvalidOperationException>().Use(static clause => (Strategy)null!).When<TimeoutException>().Retry(1);",
            "_ = Shield.For<int>().WhenResult(static value => value < 0).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Use(static clause => (Strategy)null!).WhenResult(static value => value < 0).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Retry(1);",
            "var clause = Shield.When<InvalidOperationException>(); _ = clause.Retry(1);",
            "var clause = Shield.When<InvalidOperationException>(); _ = clause.Or<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Wrap(Shield.Empty).When<TimeoutException>().Retry(1);",
            "_ = Clause().Retry(1);",
            "_ = Shield.Empty.WhenAnyError().Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static ShieldBuilder Clause() => Shield.When<InvalidOperationException>();");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV007_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string clause = "Shield.When<InvalidOperationException>().Or<TimeoutException>()";
        var source = $$"""
            public class TestSubject
            {
                public void Build() => {{clause}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV007 // The clause is asserted on elsewhere.
            Shield.When<InvalidOperationException>();
            #pragma warning restore KEV007
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV007");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "This handling clause never reaches a reactive strategy, so it has no effect: "
            + "the ShieldBuilder it returns is discarded. Finish the clause with Retry, "
            + "CircuitBreaker, Hedge, Fallback, or Use, or remove it.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(clause, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(clause.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV008_Flags_Static_Instance_And_Builder_Chains_Used_As_Statements()
    {
        var cases = new[]
        {
            "Shield.Retry(3);",
            "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "Shield.Empty.Retry(3);",
            "Shield.Empty.WithName(\"api\");",
            "Shield.Empty.For<int>();",
            "Shield.For<int>().Retry(3);",
            "Shield.When<InvalidOperationException>().Retry(3);",
            "Shield.For<int>().WhenResult(static value => value < 0).FallbackTo(0);",
            "Shield.When<InvalidOperationException>().Fallback(static _ => default);",
            "Shield.Compose(Shield.Empty, Shield.Empty);",
            "var shield = Shield.Empty; shield.Timeout(TimeSpan.FromSeconds(1));",
        };

        await AssertEachAsync(cases, "KEV008");
    }

    [Test]
    public async Task KEV008_Leaves_Used_Results_And_Executions_Alone()
    {
        var cases = new[]
        {
            "var shield = Shield.Retry(3);",
            "var shield = Shield.Empty; shield = shield.Retry(3);",
            "_ = Shield.Retry(3);",
            "Consume(Shield.Empty.Retry(3));",
            "Consume(Build());",
            "Shield.Empty.Execute(static _ => { });",
            "await Shield.Empty.ExecuteAsync(static _ => ValueTask.CompletedTask);",
            "_ = await Shield.For<int>().Retry(1).ExecuteAsync(static _ => new ValueTask<int>(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                """
                private static Shield Build() => Shield.Retry(3);

                private static void Consume(Shield shield)
                {
                }
                """);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV008_Defers_Discarded_Clause_Builders_To_KEV007()
    {
        var cases = new[]
        {
            "Shield.When<InvalidOperationException>();",
            "Shield.Empty.When<InvalidOperationException>().Or<TimeoutException>();",
            "Shield.For<int>().WhenResultIsDefault();",
        };

        await AssertEachAsync(cases, "KEV007", "KEV010");
    }

    [Test]
    public async Task KEV008_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string chain = "Shield.Empty.Retry(3)";
        var source = $$"""
            public class TestSubject
            {
                public void Build() => {{chain}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV008 // Construction is asserted on elsewhere.
            Shield.Empty.Retry(3);
            #pragma warning restore KEV008
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV008");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'Retry' returns a new shield instead of changing this one, and its result is discarded "
            + "here, so this statement configures nothing. Assign the returned shield, or continue "
            + "the chain from it.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(chain, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(chain.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV003_Flags_Unreachable_Reactive_Strategies()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Retry(1).FallbackTo(0);",
            "_ = Shield.For<int>().Hedge(1, TimeSpan.Zero).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.ConsecutiveFailures = 2).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.BreakDuration = TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.HandlesException = null).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.OnStateChanged = _ => options.HandlesException = exception => exception is TimeoutException).FallbackTo(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).FallbackTo(0, static options => options.OnFallback = static _ => { });",
            "_ = Shield.Retry(1).Fallback(static _ => ValueTask.CompletedTask, static options => options.OnFallback = static _ => { });",
            "_ = Shield.For<int>().Retry(1).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero).When<InvalidOperationException>().Timeout(TimeSpan.Zero).WhenAnyError().FallbackTo(0);",
            "var shield = Shield.For<int>().Retry(1); var alias = shield; _ = alias.FallbackTo(0);",
            "Shield<int>? shield = Shield.For<int>().Retry(1); _ = shield?.FallbackTo(0);",
            "_ = ShieldExtensions.Fallback(ShieldExtensions.Retry(Shield.Empty, 1), static _ => ValueTask.CompletedTask);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Timeout(TimeSpan.FromSeconds(1))).FallbackTo(0);",
            "_ = Shield<int>.Empty.Wrap(Shield.Retry(1)).FallbackTo(0);",
            "_ = Shield.Compose(Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.Compose(Shield.Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.Compose([Shield.Retry(1)]).For<int>().FallbackTo(0);",
            "var parts = new[] { Shield.Retry(1) }; _ = Shield.Compose(parts).For<int>().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1))).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Wrap(Shield.Retry(1)).FallbackTo(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.Compose(Shield.Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero), Shield.Empty).For<int>().FallbackTo(0);",
            "var retry = Shield.Retry(1); var fallback = Shield.For<int>().FallbackTo(0); _ = retry.Wrap(fallback);",
            "var retry = Shield.Retry(1); var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = retry.Wrap(fallback);",
        };

        // Some cases replace a clause that only a timeout ever saw, which is exactly what KEV007
        // reports, and some chain a fallback behind a reactive strategy under one clause, which is
        // what KEV009 makes visible; the fallback reachability under test is unaffected by either.
        await AssertEachAsync(cases, "KEV003", "KEV007", "KEV009");
    }

    [Test]
    public async Task KEV003_Supports_Type_Aliases_And_Generic_Result_Shields()
    {
        var aliasDiagnostics = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public Shield<int> Build() => KShield.For<int>().Retry(1).FallbackTo(0);
            }
            """);
        var genericDiagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public Shield<T> Build<T>() => Shield.For<T>().Retry(1).FallbackTo((T)default!);
            }
            """);

        await AssertRuleAsync(aliasDiagnostics, "KEV003");
        await AssertRuleAsync(genericDiagnostics, "KEV003");
    }

    [Test]
    public async Task KEV003_Skips_Reactive_Strategy_With_Local_Handling_Override()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException).FallbackTo(0);");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Recognizes_Compound_Local_Handling_Assignments()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException ??= exception => exception is TimeoutException).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException += exception => exception is TimeoutException).FallbackTo(0);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV003_Skips_Fallback_With_Local_Handling_Override()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().Retry(1).FallbackTo(0, options => options.HandlesException = exception => exception is TimeoutException);");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Resolves_Reusable_Local_Handling_Configurators()
    {
        var diagnostics = await AnalyzeBodyAsync(
            """
            Action<CircuitBreakerOptions<int>> configure = ConfigureBreaker;
            _ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);
            _ = Shield.For<int>().CircuitBreaker(configure).FallbackTo(0);
            _ = Shield.For<int>().Retry(1).FallbackTo(0, ConfigureFallback);
            """,
            """
            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;

            private static void ConfigureFallback(FallbackOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Follows_Source_Configurator_Helper_Calls()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);",
            """
            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                ApplyHandling(options);

            private static void ApplyHandling(CircuitBreakerOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Propagates_Unknown_From_Nested_Configurator_Call()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);",
            """
            private static Action<CircuitBreakerOptions<int>> SharedConfigure { get; } =
                options => options.HandlesException = exception => exception is TimeoutException;

            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                SharedConfigure(options);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Skips_Opaque_Local_Handling_Configurator()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Build(options => options.HandlesException = exception => exception is TimeoutException);",
            """
            private static Shield<int> Build(Action<CircuitBreakerOptions<int>> configure) =>
                Shield.For<int>()
                    .When<InvalidOperationException>()
                    .CircuitBreaker(configure)
                    .FallbackTo(0);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Skips_Valid_Or_Unknown_Compositions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().FallbackTo(0).Retry(1);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().When<ArgumentException>().Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero).CircuitBreaker(2, TimeSpan.FromSeconds(1)).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Timeout(TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "var shield = CreateShield(); _ = shield.FallbackTo(0);",
            "var shield = Shield.For<int>().Retry(1); shield = Shield<int>.Empty; _ = shield.FallbackTo(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Retry(1), Shield.Empty).For<int>().FallbackTo(0);",
            "var clause = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = outer.Wrap(clause).FallbackTo(0);",
            "var clause = Shield.When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = Shield.Compose(outer, clause).For<int>().FallbackTo(0);",
            "var parts = new[] { Shield.Retry(1) }; parts[0] = Shield.Empty; _ = Shield.Compose(parts).For<int>().FallbackTo(0);",
            "var builder = Shield.For<int>().When<InvalidOperationException>(); var retry = builder.Retry(1); _ = retry.Wrap(builder.Timeout(TimeSpan.Zero)).FallbackTo(0);",
            "var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); var retry = Shield.Retry(1); _ = fallback.Wrap(retry);",
            "var retry = Shield.When<InvalidOperationException>().Retry(1); var fallback = Shield.For<int>().When<TimeoutException>().FallbackTo(0); _ = retry.Wrap(fallback);",
            "var outer = Shield.Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero); var fallback = Shield.For<int>().When<InvalidOperationException>().FallbackTo(0); _ = outer.Wrap(fallback);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, "private static Shield<int> CreateShield() => Shield.For<int>().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV009_Flags_Reactive_Strategies_That_Inherit_An_Earlier_Clause()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.WhenContext((HandlingEvent handling) => handling.Attempt == 0).Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).RetryForever(Backoff.None);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Hedge(1, TimeSpan.Zero);",
            "_ = Shield.For<int>().When<InvalidOperationException>().FallbackTo(0).Retry(1);",
            "_ = Shield.For<int>().WhenResultIsDefault().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "var outer = Shield.When<InvalidOperationException>().Retry(1); _ = outer.CircuitBreaker(2, TimeSpan.FromSeconds(1));",
        };

        await AssertEachAsync(cases, "KEV009", DiagnosticSeverity.Info, "KEV010");
    }

    [Test]
    public async Task KEV009_Flags_Every_Strategy_After_The_First_Across_Proactive_Links()
    {
        var diagnostics = await AnalyzeBodyAsync(
            """
            _ = Shield.When<InvalidOperationException>()
                .Retry(1)
                .Timeout(TimeSpan.FromSeconds(1))
                .CircuitBreaker(2, TimeSpan.FromSeconds(1))
                .RateLimit(10, TimeSpan.FromSeconds(1))
                .RetryForever(Backoff.None);
            """);

        // The retry states the clause at its own call site; the breaker and the forever-retry
        // inherit it across the timeout and rate limit, which carry no clause of their own.
        await Assert.That(diagnostics.Length).IsEqualTo(2);
        await Assert.That(diagnostics).All(static diagnostic => diagnostic.Id == "KEV009");
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "CircuitBreaker", "RetryForever" });
    }

    private static string[] MarkedText(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(static diagnostic => diagnostic.Location.SourceTree!.ToString()
                .Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length))
            .ToArray();

    [Test]
    public async Task KEV009_Skips_Reset_Replaced_Absent_Overridden_And_Sealed_Clauses()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1);",
            "_ = Shield.Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).WhenAnyError().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).When<TimeoutException>().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).RateLimit(10, TimeSpan.FromSeconds(1)).ConcurrencyLimit(2).Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Retry(1).Wrap(Shield.Empty).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Retry(1), Shield.Empty).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException);",
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).CircuitBreaker(options => options.HandlesExceptionWithContext = handling => handling.Attempt == 0);",
            "_ = CreateShield().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static Shield CreateShield() => Shield.When<InvalidOperationException>().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV009_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.When<InvalidOperationException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));";
        var diagnostics = await AnalyzeBodyAsync(body);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV009 // The inherited clause is deliberate here.
            {body}
            #pragma warning restore KEV009
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV009");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "This strategy inherits the handling clause declared earlier in the chain "
            + "('When<InvalidOperationException>…'); only those exceptions or results count toward "
            + "it. Declare a new clause, or call 'WhenAnyError()' first, to give it different "
            + "handling.");

        // The hint marks only the inheriting strategy's name, not the whole chain.
        var span = diagnostic.Location.SourceSpan;
        await Assert.That(diagnostic.Location.SourceTree!.ToString().Substring(span.Start, span.Length))
            .IsEqualTo("CircuitBreaker");
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV010_Flags_Default_Result_Clauses_Written_For_A_Value_Type()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<bool>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<TimeSpan>().WhenResultIsDefault().FallbackTo(TimeSpan.MaxValue);",
            "_ = Shield.For<int>().When<InvalidOperationException>().OrResultIsDefault().Retry(1);",
            "_ = Shield.For<int>().WhenResultIsDefault().Or<InvalidOperationException>().Retry(1);",
            "var clause = Shield.For<int>().WhenResultIsDefault(); _ = clause.Retry(1);",
        };

        await AssertEachAsync(cases, "KEV010", DiagnosticSeverity.Info);
    }

    [Test]
    public async Task KEV010_Skips_Reference_Types_Nullables_Generic_Results_And_Explicit_Values()
    {
        var cases = new[]
        {
            "_ = Shield.For<string>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<string>().WhenResultIsNull().Retry(1);",
            "_ = Shield.For<string>().When<InvalidOperationException>().OrResultIsNull().Retry(1);",
            "_ = Shield.For<int?>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1);",
            "_ = Build<int>();",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                // Generic code has no result to name but default(T), so the clause is all it can write.
                "private static Shield<T> Build<T>() => Shield.For<T>().WhenResultIsDefault().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV010_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.For<int>().WhenResultIsDefault().Retry(1);";
        var diagnostics = await AnalyzeBodyAsync(body);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV010 // Zero really is the failure here.
            {body}
            #pragma warning restore KEV010
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV010");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'WhenResultIsDefault' handles 'default(int)', which for a value type — 0, false, an "
            + "empty struct — is as often a legitimate result as a failure. Confirm that is "
            + "intended, or select the failing results with 'WhenResult'/'OrResult'.");

        // The hint marks only the clause's name, not the whole chain.
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "WhenResultIsDefault" });
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV011_Flags_Reactive_Strategies_With_Implicit_Default_Handling()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(3);",
            "_ = Shield.For<int>().CircuitBreaker(3, TimeSpan.FromSeconds(1));",
            "_ = Shield.Empty.Hedge(1, TimeSpan.Zero);",
            "_ = Shield.For<int>().FallbackTo(0);",
            "var baseline = Shield.Empty; _ = baseline.RetryForever();",
            "_ = Shield.Empty.WhenAnyError().Wrap(Shield.Empty).Retry(1);",
            "_ = Shield.Compose(Shield.Empty.WhenAnyError(), Shield.Empty).Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
            await AssertRuleAsync(Without(diagnostics, "KEV006"), "KEV011", DiagnosticSeverity.Info);
        }
    }

    [Test]
    public async Task KEV011_Skips_Explicit_Ambient_Local_And_Reset_Handling()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(3);",
            "_ = Shield.For<int>().WhenResult(-1).FallbackTo(0);",
            "_ = Shield.Retry(options => options.HandlesException = exception => exception is InvalidOperationException);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.HandlesResult = value => value < 0);",
            "_ = Shield.When<InvalidOperationException>().Retry(1).WhenAnyError().Retry(1);",
            "_ = Shield.Timeout(TimeSpan.FromSeconds(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
            await Assert.That(Without(diagnostics, "KEV009")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV011_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.Retry(3);";
        var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV011 // Retrying all ordinary errors is deliberate.
            {body}
            #pragma warning restore KEV011
            """, enableImplicitDefaultHandlingRule: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV011");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'Retry' uses Kevlar's default handling, which includes programming errors. Declare "
            + "a When clause or local HandlesException override when only expected failures should be handled.");
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "Retry" });
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV012_Flags_Known_Async_Configuration_Used_Synchronously()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.Retry(options => options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Timeout(options => options.TimeoutGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.CircuitBreaker(options => { options.ConsecutiveFailures = 1; options.BreakDurationGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1)); }).Execute(_ => 1);",
            "_ = Shield.RateLimit(options => options.OnRejectedAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.ConcurrencyLimit(options => options.OnRejectedAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.For<int>().FallbackTo(0, options => options.OnFallbackAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).ExecuteWithContext(static _ => 1);",
            "_ = Shield.Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).ExecuteOutcome(static _ => 1);",
            "_ = Shield.Empty.UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!, options => options.OnRejectedAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.For<int>().Fallback(static _ => ValueTask.FromResult(0)).Execute(_ => 1);",
            "_ = Shield.Empty.UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => { options.Enabled = true; options.Behavior = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => { options.Enabled = false; if (DateTime.UtcNow.Ticks > 0) { options.Enabled = true; } options.Behavior = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { var alias = options; alias.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.MaxRetries = 0; options.OnRetryAsync = static _ => ValueTask.CompletedTask; options.MaxRetries = 1; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.MaxRetries = 0; if (DateTime.UtcNow.Ticks > 0) { options.MaxRetries = 1; } options.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.MaxRetries = 1; options.OnRetryAsync = static _ => ValueTask.CompletedTask; if (DateTime.UtcNow.Ticks > 0) return; options.MaxRetries = 0; }).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => { options.Enabled = true; options.Behavior = static _ => ValueTask.CompletedTask; if (DateTime.UtcNow.Ticks > 0) return; options.Enabled = false; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.OnRetryAsync = static _ => ValueTask.CompletedTask; if (DateTime.UtcNow.Ticks > 0) return; options.OnRetryAsync = null; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.OnRetryAsync = static _ => ValueTask.CompletedTask; options.OnRetryAsync = null; if (DateTime.UtcNow.Ticks > 0) options.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV012", "KEV004", "KEV011");

        var additionalOptionShapes = new[]
        {
            "_ = Shield.For<int>().Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.For<int>().Retry(options => options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Hedge(options => options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan>(TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.For<int>().Hedge(options => options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan>(TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Timeout(options => options.OnTimeoutAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.CircuitBreaker(options => options.OnStateChangedAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = Shield.For<int>().CircuitBreaker(options => { options.ConsecutiveFailures = 1; options.BreakDurationGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1)); }).Execute(_ => 1);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.OnStateChangedAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "Shield.Fallback(static _ => ValueTask.CompletedTask, options => options.OnFallbackAsync = static _ => ValueTask.CompletedTask).Execute(static _ => { });",
        };
        foreach (var body in additionalOptionShapes)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics.Any(static diagnostic => diagnostic.Id == "KEV012"))
                .IsTrue();
        }
    }

    [Test]
    public async Task KEV012_Skips_Async_Execution_Sync_Configuration_And_Unknown_Configuration()
    {
        var cases = new[]
        {
            "_ = await Shield.Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).ExecuteAsync(_ => new ValueTask<int>(1));",
            "_ = Shield.Timeout(options => options.TimeoutGeneratorSync = static _ => TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            "_ = Shield.CircuitBreaker(options => { options.ConsecutiveFailures = 1; options.BreakDurationGeneratorSync = static _ => TimeSpan.FromSeconds(1); }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.MaxRetries = 0; options.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero); options.MaxRetries = 0; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { if (DateTime.UtcNow.Ticks > 0) { options.MaxRetries = 1; } options.MaxRetries = 0; options.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Hedge(options => { options.MaxHedgedAttempts = 0; options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan>(TimeSpan.Zero); }).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => options.Behavior = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => { options.Enabled = true; options.InjectionRate = 0; options.Behavior = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = ChaosShield.Behavior(options => { options.Enabled = true; options.Enabled = false; options.Behavior = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => options.OnRetryAsync = null).Execute(_ => 1);",
            "_ = Shield.Retry(options => { options.OnRetryAsync = static _ => ValueTask.CompletedTask; options.OnRetryAsync = null; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => { if (DateTime.UtcNow.Ticks > 0) options.OnRetryAsync = static _ => ValueTask.CompletedTask; options.OnRetryAsync = null; }).Execute(_ => 1);",
            "_ = Shield.Retry(options => options.OnRetry = _ => options.OnRetryAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1);",
            "var unrelated = new TimeoutOptions(); _ = Shield.Retry(_ => unrelated.TimeoutGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.Retry(options => { var alias = options; alias = new RetryOptions(); alias.OnRetryAsync = static _ => ValueTask.CompletedTask; }).Execute(_ => 1);",
            "Action<RetryOptions> configure = static options => options.OnRetryAsync = static _ => ValueTask.CompletedTask; _ = Shield.Retry(configure).Execute(_ => 1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(Without(diagnostics, "KEV004")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV012_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string execution = "Shield.Retry(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask).Execute(_ => 1)";
        var source = $$"""
            public class TestSubject
            {
                public int Run() => {{execution}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync($$"""
            #pragma warning disable KEV012 // This call is migrated separately.
            _ = {{execution}};
            #pragma warning restore KEV012
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV012");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "This shield configures 'RetryOptions.OnRetryAsync', which cannot run through synchronous "
            + "'Execute'. Use 'ExecuteAsync' or configure its synchronous counterpart.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(execution, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(execution.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task Non_Kevlar_Methods_And_Generated_Code_Are_Ignored()
    {
        var unrelated = await AnalyzeSourceAsync("""
            public sealed class OtherShield
            {
                public OtherShield Hedge() => this;
                public OtherShield Retry() => this;
                public OtherShield Fallback() => this;
                public int Execute(Func<CancellationToken, int> action) => action(default);
            }

            public class TestSubject
            {
                public int Run() => new OtherShield().Hedge().Retry().Fallback().Execute(_ => 1);
            }
            """);
        var generated = await AnalyzeBodyAsync(
            "_ = Shield.Hedge(1, TimeSpan.Zero).Execute(_ => 1); _ = Shield.For<int>().Retry(1).FallbackTo(0);",
            isGenerated: true);

        await Assert.That(unrelated).IsEmpty();
        await Assert.That(generated).IsEmpty();
    }

    [Test]
    public async Task KEV012_Ignores_Custom_Options_In_A_Kevlar_Prefixed_Namespace()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            namespace Kevlar.Extensions.Custom
            {
                public sealed class CustomOptions
                {
                    public Func<int, ValueTask>? OnRetryAsync { get; set; }
                    public Func<int, ValueTask<TimeSpan?>>? DelayGeneratorAsync { get; set; }
                    public Func<int, ValueTask>? OnTimeoutAsync { get; set; }
                    public Func<int, ValueTask<TimeSpan>>? TimeoutGenerator { get; set; }
                    public Func<int, ValueTask>? OnFallbackAsync { get; set; }
                    public Func<int, ValueTask>? OnStateChangedAsync { get; set; }
                    public Func<int, ValueTask<TimeSpan>>? BreakDurationGenerator { get; set; }
                    public Func<int, ValueTask>? OnRejectedAsync { get; set; }
                    public Func<int, ValueTask>? Behavior { get; set; }
                }

                public static class CustomShieldExtensions
                {
                    public static Shield Custom(this Shield shield, Action<CustomOptions> configure)
                    {
                        configure(new CustomOptions());
                        return shield;
                    }
                }

                public sealed class TestSubject
                {
                    public int Run() => Shield.Empty
                        .Custom(options =>
                        {
                            options.OnRetryAsync = static _ => ValueTask.CompletedTask;
                            options.DelayGeneratorAsync = static _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
                            options.OnTimeoutAsync = static _ => ValueTask.CompletedTask;
                            options.TimeoutGenerator = static _ => ValueTask.FromResult(TimeSpan.Zero);
                            options.OnFallbackAsync = static _ => ValueTask.CompletedTask;
                            options.OnStateChangedAsync = static _ => ValueTask.CompletedTask;
                            options.BreakDurationGenerator = static _ => ValueTask.FromResult(TimeSpan.Zero);
                            options.OnRejectedAsync = static _ => ValueTask.CompletedTask;
                            options.Behavior = static _ => ValueTask.CompletedTask;
                        })
                        .Execute(_ => 1);
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV012_Ignores_Lookalike_Options_In_The_Kevlar_Namespace()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            namespace Kevlar
            {
                public sealed class RetryOptions
                {
                    public Func<int, ValueTask>? OnRetryAsync { get; set; }
                }

                public static class LookalikeShieldExtensions
                {
                    public static Shield Custom(this Shield shield, Action<RetryOptions> configure)
                    {
                        configure(new RetryOptions());
                        return shield;
                    }
                }

                public sealed class TestSubject
                {
                    public int Run() => Shield.Empty
                        .Custom(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask)
                        .Execute(_ => 1);
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV012_Ignores_Known_Options_On_Custom_Fluent_Extensions()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            namespace Custom;

            public static class InspectionExtensions
            {
                public static Shield Inspect(this Shield shield, Action<Kevlar.RetryOptions> configure)
                {
                    configure(new Kevlar.RetryOptions());
                    return shield;
                }
            }

            public sealed class TestSubject
            {
                public int Run() => Shield.Empty
                    .Inspect(options => options.OnRetryAsync = static _ => ValueTask.CompletedTask)
                    .Execute(_ => 1);
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertEachAsync(
        IEnumerable<string> cases,
        string expectedRule,
        params string[] expectedCompanionRules)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await AssertRuleAsync(Without(diagnostics, expectedCompanionRules), expectedRule);
        }
    }

    private static async Task AssertEachAsync(
        IEnumerable<string> cases,
        string expectedRule,
        DiagnosticSeverity expectedSeverity,
        params string[] expectedCompanionRules)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await AssertRuleAsync(Without(diagnostics, expectedCompanionRules), expectedRule, expectedSeverity);
        }
    }

    /// <summary>
    /// Drops rules a case is expected to trigger in addition to the rule under test — untyped
    /// hedging cases, for instance, always also report KEV006.
    /// </summary>
    private static ImmutableArray<Diagnostic> Without(
        ImmutableArray<Diagnostic> diagnostics,
        params string[] ruleIds) =>
        ruleIds.Length == 0
            ? diagnostics
            : diagnostics
                .Where(diagnostic => !ruleIds.Contains(diagnostic.Id, StringComparer.Ordinal))
                .ToImmutableArray();

    private static async Task AssertRuleAsync(
        ImmutableArray<Diagnostic> diagnostics,
        string expectedRule,
        DiagnosticSeverity expectedSeverity = DiagnosticSeverity.Warning)
    {
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo(expectedRule);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(expectedSeverity);
    }

    private static async Task<(int ActionCount, string? ChangedText)> GetCodeFixAsync(
        string declarations)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "CodeFixTest",
            "CodeFixTest",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(),
            metadataReferences: GetMetadataReferences()));
        var document = workspace.AddDocument(
            project.Id,
            "Test.cs",
            SourceText.From(CreateSource(declarations)));
        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync())!;
        var diagnostic = (await GetAnalyzerDiagnosticsAsync(compilation))
            .Single(static item => item.Id == "KEV013");
        var actions = new List<CodeAction>();
        var provider = new AsyncCallbackCodeFixProvider();

        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None));
        if (actions.Count != 1)
        {
            return (actions.Count, null);
        }

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changedText = await changedSolution.GetDocument(document.Id)!.GetTextAsync();
        return (actions.Count, changedText.ToString());
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeBodyAsync(
        string body,
        string members = "",
        bool isGenerated = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject",
        bool enableImplicitDefaultHandlingRule = false) =>
        AnalyzeSourceAsync($$"""
            public class TestSubject
            {
                {{members}}

                public async Task RunAsync()
                {
                    {{body}}
                }
            }
            """, isGenerated, assemblyName: assemblyName, enableImplicitDefaultHandlingRule: enableImplicitDefaultHandlingRule);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(
        string declarations,
        bool isGenerated = false,
        bool allowCompilationErrors = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject",
        bool enableImplicitDefaultHandlingRule = false,
        MetadataReference? additionalReference = null)
    {
        var source = CreateSource(declarations, isGenerated, enableImplicitDefaultHandlingRule);
        var compilation = CreateCompilation(source, assemblyName, additionalReference);
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (!allowCompilationErrors && errors.Length > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", errors.Select(static error => error.ToString())));
        }

        return await GetAnalyzerDiagnosticsAsync(compilation);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourcesAsync(
        params string[] declarations)
    {
        var compilation = CSharpCompilation.Create(
            "PipelineHazardAnalyzerTestSubject",
            declarations.Select(declaration =>
                CSharpSyntaxTree.ParseText(CreateSource(declaration))),
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "Test source does not compile: "
                + string.Join("; ", errors.Select(static error => error.ToString())));
        }

        return await GetAnalyzerDiagnosticsAsync(compilation);
    }

    private static string CreateSource(
        string declarations,
        bool isGenerated = false,
        bool enableImplicitDefaultHandlingRule = false) =>
        (isGenerated ? "// <auto-generated/>\n" : string.Empty)
        + (enableImplicitDefaultHandlingRule ? string.Empty : "#pragma warning disable KEV011\n")
        + $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kevlar;
            using Kevlar.Chaos;
            using Kevlar.Extensions.RateLimiting;

            {{declarations}}
            """;

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "PipelineHazardAnalyzerTestSubject",
        MetadataReference? additionalReference = null)
    {
        var references = GetMetadataReferences();
        if (additionalReference is not null)
        {
            references = references.Append(additionalReference);
        }

        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CreateMetadataReference(string declarations)
    {
        var compilation = CreateCompilation(
            CreateSource(declarations),
            "PipelineHazardAnalyzerExternalTestSubject");
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Metadata test source does not compile: "
                + string.Join("; ", result.Diagnostics.Select(static error => error.ToString())));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Kevlar.Chaos.ChaosShield).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(Kevlar.Extensions.RateLimiting.ShieldRateLimiterExtensions).Assembly.Location));

    private static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(CSharpCompilation compilation) =>
        compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineHazardAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
}
