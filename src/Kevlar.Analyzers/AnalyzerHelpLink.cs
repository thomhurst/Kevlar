namespace Kevlar.Analyzers;

internal static class AnalyzerHelpLink
{
    private const string DocumentationBase =
        "https://kevlar.tomlonghurst.dev/docs/analyzers#";

    public static string Create(string ruleId, string slug) =>
        $"{DocumentationBase}{ruleId.ToLowerInvariant()}-{slug}";
}
