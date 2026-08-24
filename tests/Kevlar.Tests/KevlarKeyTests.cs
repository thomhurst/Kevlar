namespace Kevlar.Tests;

public class KevlarKeyTests
{
    [Test]
    public async Task Keys_With_Same_Name_And_Type_Are_Equal()
    {
        var first = new KevlarKey<int>("attempt");
        var second = new KevlarKey<int>("attempt");

        await Assert.That(first == second).IsTrue();
        await Assert.That(first != second).IsFalse();
        await Assert.That(first.Equals(second)).IsTrue();
        await Assert.That(first.Equals((object)second)).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Keys_With_Same_Name_And_Different_Type_Are_Not_Equal()
    {
        object number = new KevlarKey<int>("shared");
        object text = new KevlarKey<string>("shared");

        await Assert.That(number.Equals(text)).IsFalse();
    }

    [Test]
    public async Task Keys_Are_Case_Sensitive()
    {
        var upper = new KevlarKey<int>("Key");
        var lower = new KevlarKey<int>("key");

        await Assert.That(upper == lower).IsFalse();
    }

    [Test]
    public async Task Key_Works_As_Dictionary_Key()
    {
        var stored = new KevlarKey<int>("value");
        var dictionary = new Dictionary<KevlarKey<int>, int> { [stored] = 42 };

        await Assert.That(dictionary[new KevlarKey<int>("value")]).IsEqualTo(42);
    }

    [Test]
    public async Task ToString_Returns_Name()
    {
        var key = new KevlarKey<int>("attempt-count");

        await Assert.That(key.ToString()).IsEqualTo("attempt-count");
    }
}
