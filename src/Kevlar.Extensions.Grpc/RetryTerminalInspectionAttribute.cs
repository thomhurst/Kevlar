namespace Kevlar;

/// <summary>Opts a delay generator into inspecting the terminal retry outcome.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RetryTerminalInspectionAttribute : Attribute
{
}
