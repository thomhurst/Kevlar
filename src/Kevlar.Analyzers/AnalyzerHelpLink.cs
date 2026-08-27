namespace Kevlar.Analyzers;

internal static class AnalyzerHelpLink
{
    private const string DocumentationBase =
        "https://thomhurst.github.io/Kevlar/docs/analyzers#";

    public static string Create(string ruleId, string slug) =>
        $"{DocumentationBase}{ruleId.ToLowerInvariant()}-{slug}";
}
