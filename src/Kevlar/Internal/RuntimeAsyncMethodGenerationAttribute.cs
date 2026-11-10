#if NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

// The compiler recognises this attribute by name; .NET 11 does not expose it publicly.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class RuntimeAsyncMethodGenerationAttribute(bool runtimeAsync) : Attribute
{
    public bool RuntimeAsync { get; } = runtimeAsync;
}
#endif
