#if !NET

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Enables C# init-only properties when targeting netstandard2.0.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit;
}

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class NotNullAttribute : Attribute;
}

#endif
