namespace Kevlar.Tests;

[NotInParallel]
public class KevlarPropertiesTests
{
    private static readonly KevlarKey<int> ValueKey = new("value");

    [Test]
    public async Task Remove_Returns_True_When_Present_And_Clears_Value()
    {
        var properties = new KevlarProperties();
        properties.Set(ValueKey, 42);

        var removed = properties.Remove(ValueKey);

        await Assert.That(removed).IsTrue();
        await Assert.That(properties.TryGet(ValueKey, out _)).IsFalse();
        await Assert.That(properties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_Returns_False_When_Absent()
    {
        var properties = new KevlarProperties();

        await Assert.That(properties.Remove(ValueKey)).IsFalse();
    }

    [Test]
    public async Task Contains_Reflects_Set_And_Remove()
    {
        var properties = new KevlarProperties();

        await Assert.That(properties.Contains(ValueKey)).IsFalse();
        properties.Set(ValueKey, 42);
        await Assert.That(properties.Contains(ValueKey)).IsTrue();
        properties.Remove(ValueKey);
        await Assert.That(properties.Contains(ValueKey)).IsFalse();
    }

    [Test]
    public async Task Count_Tracks_Entries()
    {
        var properties = new KevlarProperties();
        var otherKey = new KevlarKey<string>("other");

        properties.Set(ValueKey, 1);
        properties.Set(ValueKey, 2);
        properties.Set(otherKey, "text");
        await Assert.That(properties.Count).IsEqualTo(2);

        properties.Remove(ValueKey);
        await Assert.That(properties.Count).IsEqualTo(1);
        properties.Set(ValueKey, 3);
        await Assert.That(properties.Count).IsEqualTo(2);
        properties.Remove(ValueKey);
        properties.Remove(otherKey);
        await Assert.That(properties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Default_Key_Throws_Clear_Message_On_Use()
    {
        var properties = new KevlarProperties();
        var defaultKey = default(KevlarKey<int>);

        await AssertInvalidKey(() => properties.Set(defaultKey, 1));
        await AssertInvalidKey(() => properties.TryGet(defaultKey, out _));
        await AssertInvalidKey(() => properties.GetOrDefault(defaultKey));
        await AssertInvalidKey(() => properties.Remove(defaultKey));
        await AssertInvalidKey(() => properties.Contains(defaultKey));
    }

    [Test]
    public async Task Properties_Are_Cleared_When_Context_Returns_To_Pool()
    {
        await Shield.Empty.ExecuteWithContextAsync(
            ValueKey,
            static (key, properties) => properties.Set(key, 42),
            static (_, _) => ValueTask.CompletedTask);

        var count = -1;
        await Shield.Empty.ExecuteWithContextAsync(
            0,
            (_, properties) => count = properties.Count,
            static (_, _) => ValueTask.CompletedTask);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_Is_Visible_To_Later_Strategies_In_Same_Execution()
    {
        var remover = new RemovePropertyStrategy(ValueKey);
        var observer = new ObservePropertyStrategy(ValueKey);
        var shield = Shield.Empty.Use(remover).Use(observer);

        await shield.ExecuteWithContextAsync(
            ValueKey,
            static (key, properties) => properties.Set(key, 42),
            static (_, _) => ValueTask.CompletedTask);

        await Assert.That(remover.Removed).IsTrue();
        await Assert.That(observer.Contains).IsFalse();
    }

    [Test]
    public async Task Returned_Properties_Reject_Remove_And_Contains_In_Debug_Builds()
    {
        KevlarProperties? retained = null;
        await Shield.Empty.ExecuteWithContextAsync(context =>
        {
            retained = context.Properties;
            return ValueTask.CompletedTask;
        });

#if DEBUG
        await Assert.That(() => retained!.Remove(ValueKey)).Throws<InvalidOperationException>();
        await Assert.That(() => retained!.Contains(ValueKey)).Throws<InvalidOperationException>();
#else
        await Assert.That(retained!.Contains(ValueKey)).IsFalse();
#endif
    }

    private static async Task AssertInvalidKey(Action action)
    {
        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("KevlarKey<T> must be created with a name");
    }

    private sealed class RemovePropertyStrategy(KevlarKey<int> key) : Strategy
    {
        public bool Removed { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Removed = context.Properties.Remove(key);
            return next.InvokeAsync(context);
        }
    }

    private sealed class ObservePropertyStrategy(KevlarKey<int> key) : Strategy
    {
        public bool Contains { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Contains = context.Properties.Contains(key);
            return next.InvokeAsync(context);
        }
    }
}
