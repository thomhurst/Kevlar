using System.Collections.Immutable;
using System.Composition.Hosting;
using Kevlar.Analyzers;
using Kevlar.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Kevlar.Analyzers.Tests;

public class CancellationTokenCodeFixTests
{
    [Test]
    public async Task Roslyn_48_Mef_Host_Discovers_The_Code_Fix()
    {
        using var container = new ContainerConfiguration()
            .WithAssembly(typeof(IgnoredCancellationTokenCodeFixProvider).Assembly).CreateContainer();
        var fixer = container.GetExports<CodeFixProvider>().Single();
        await Assert.That(fixer.FixableDiagnosticIds.Single()).IsEqualTo("KEV001");
        await Assert.That(typeof(CodeFixProvider).Assembly.GetName().Version!.ToString()).IsEqualTo("4.8.0.0");
    }

    [Test]
    [Arguments("ct => client.GetAsync(url)", "ct => client.GetAsync(url, cancellationToken: ct)")]
    [Arguments("token => client.GetAsync(requestUri: url)", "token => client.GetAsync(requestUri: url, cancellationToken: token)")]
    [Arguments("async ct => await client.GetAsync(url)", "async ct => await client.GetAsync(url, cancellationToken: ct)")]
    [Arguments("ct => { return client.GetAsync(url); }", "ct => { return client.GetAsync(url, cancellationToken: ct); }")]
    [Arguments("ct => client.GetAsync(/* keep */ url)", "ct => client.GetAsync(/* keep */ url, cancellationToken: ct)")]
    [Arguments("@event => client.GetAsync(url)", "@event => client.GetAsync(url, cancellationToken: @event)")]
    public async Task Forwards_Http_Token_Without_Changing_Delegate(string before, string after)
    {
        await AssertFixAsync(Source($"await Shield.Empty.ExecuteAsync({before});"), before, after);
    }

    [Test]
    [Arguments("static int Work(int value, CancellationToken stop = default) => value;", "Work(1)", "Work(1, stop: ct)")]
    [Arguments("static int Work(int value) => value; static int Work(int value, CancellationToken @event) => value;", "Work(value: 1)", "Work(value: 1, @event: ct)")]
    [Arguments("static T Work<T>(T value) => value; static T Work<T>(T value, CancellationToken stop) => value;", "Work(1)", "Work(1, stop: ct)")]
    [Arguments("static int Work(int value, bool flag = false) => value; static int Work(int value, CancellationToken stop, bool flag = false) => value;", "Work(1)", "Work(1, stop: ct)")]
    [Arguments("static int Work() => 1; static int Work(CancellationToken stop) => 1;", "Work()", "Work(stop: ct)")]
    public async Task Selects_Semantic_Overload_And_Actual_Parameter_Name(string members, string before, string after)
    {
        await AssertFixAsync(Source($"Shield.Empty.Execute(ct => {before});", members), before, after);
    }

    [Test]
    public async Task Supports_State_And_Typed_Execution()
    {
        await AssertFixAsync(Source("Shield<int>.Empty.Execute(1, (state, ct) => Work(state));",
            "static int Work(int value, CancellationToken stop = default) => value;"),
            "Work(state)", "Work(state, stop: ct)");
    }

    [Test]
    public async Task Supports_Void_Block_And_Extension_Methods()
    {
        await AssertFixAsync(Source("Shield.Empty.Execute(ct => { Work(); });",
            "static void Work(CancellationToken stop = default) { }"), "Work()", "Work(stop: ct)");
        var source = Source("Shield.Empty.Execute(ct => url.Work());") + """

            static class Extensions
            {
                public static int Work(this string value) => 1;
                public static int Work(this string value, CancellationToken stop) => 1;
            }
            """;
        await AssertFixAsync(source, "url.Work()", "url.Work(stop: ct)");
    }

    [Test]
    [Arguments("static int Work(string value) => 1; static int Work(object value, CancellationToken stop) => 1;", "Work(url)")]
    [Arguments("static int Work(int value, bool flag = false) => value; static int Work(int value, CancellationToken stop, bool flag = true) => value;", "Work(1)")]
    [Arguments("static int Work(int value) => value; static long Work(int value, CancellationToken stop) => value;", "Work(1)")]
    [Arguments("static int Work(int value) => value; static int Work(int other, CancellationToken stop) => other;", "Work(1)")]
    [Arguments("static int Work(int value) => value; static int Work(int value, CancellationToken first, CancellationToken second) => value;", "Work(1)")]
    [Arguments("static int Work(int value, CancellationToken stop = default) => value;", "Work(1, default)")]
    [Arguments("static int Work(int value) => value;", "Work(1)")]
    [Arguments("static int Work(int value) => value; static int Work(int value, CancellationToken stop, bool extra = false) => value;", "Work(1)")]
    [Arguments("static int Work(int value) => value; static int Work(int value, in CancellationToken stop) => value;", "Work(1)")]
    public async Task Does_Not_Change_Unrelated_Call_Semantics(string members, string call)
    {
        await AssertNoFixAsync(Source($"Shield.Empty.Execute(ct => {call});", members));
    }

    [Test]
    public async Task Ambiguous_Extension_Overloads_Have_No_Fix()
    {
        var source = Source("Shield.Empty.Execute(ct => url.Work());") + """

            static class First
            {
                public static int Work(this string value) => 1;
                public static int Work(this string value, CancellationToken stop) => 1;
            }
            static class Second
            {
                public static int Work(this string value, CancellationToken stop) => 1;
            }
            """;
        await AssertNoFixAsync(source);
    }

    [Test]
    [Arguments("ct => { client.GetAsync(url); return client.GetAsync(url); }")]
    [Arguments("ct => client.GetAsync(url)?.GetAwaiter().GetResult()")]
    [Arguments("ct => client.GetAsync(url, CancellationToken.None)")]
    public async Task Complex_Delegates_And_Explicit_Tokens_Have_No_Fix(string lambda)
    {
        await AssertNoFixAsync(Source($"Shield.Empty.Execute({lambda});"));
    }

    [Test]
    public async Task Context_Delegates_Remain_Diagnostic_Only()
    {
        await AssertNoFixAsync(Source("Shield.Empty.ExecuteWithContext((KevlarContext)null!, context => client.GetAsync(url));"));
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    [Arguments(FixAllScope.Solution)]
    public async Task Fix_All_Updates_Supported_Delegates_And_Leaves_Unsupported_Ones(FixAllScope scope)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, Source("""
            await Shield.Empty.ExecuteAsync(ct => client.GetAsync(url));
            await Shield.Empty.ExecuteAsync(token => client.GetAsync(requestUri: url));
            Shield.Empty.Execute(ct => 1);
            """));
        var second = document.Project.AddDocument("Second.cs", SourceText.From(
            Source("await Shield.Empty.ExecuteAsync(ct => client.GetAsync(url));").Replace("class Subject", "class Second", StringComparison.Ordinal)));
        document = second.Project.GetDocument(document.Id)!;
        var fixer = new IgnoredCancellationTokenCodeFixProvider();
        var diagnostics = await DiagnosticsAsync(document);
        var actions = await ActionsAsync(document, diagnostics[0]);
        var context = new FixAllContext(document, fixer, scope, actions.Single().EquivalenceKey,
            fixer.FixableDiagnosticIds, new DocumentDiagnostics(), CancellationToken.None);
        var action = await fixer.GetFixAllProvider().GetFixAsync(context);
        var changed = await ApplyAsync(document, action!);
        await Assert.That((await DiagnosticsAsync(changed)).Length).IsEqualTo(1);
        var text = (await changed.GetTextAsync()).ToString();
        await Assert.That(text).Contains("client.GetAsync(url, cancellationToken: ct)");
        await Assert.That(text).Contains("client.GetAsync(requestUri: url, cancellationToken: token)");
        await Assert.That(text).Contains("Shield.Empty.Execute(ct => 1)");
        var changedSecond = changed.Project.GetDocument(second.Id)!;
        await Assert.That((await DiagnosticsAsync(changedSecond)).Length)
            .IsEqualTo(scope == FixAllScope.Document ? 1 : 0);
        await AssertCompilesAsync(changed);
    }

    private static async Task AssertFixAsync(string source, string before, string after)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        await AssertCompilesAsync(document);
        var diagnostics = await DiagnosticsAsync(document);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var actions = await ActionsAsync(document, diagnostics.Single());
        await Assert.That(actions.Count).IsEqualTo(1);
        var changed = await ApplyAsync(document, actions.Single());
        var offset = source.IndexOf(before, StringComparison.Ordinal);
        await Assert.That((await changed.GetTextAsync()).ToString())
            .IsEqualTo(source[..offset] + after + source[(offset + before.Length)..]);
        await AssertCompilesAsync(changed);
        await Assert.That((await DiagnosticsAsync(changed)).Length).IsEqualTo(0);
    }

    private static async Task AssertNoFixAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        await AssertCompilesAsync(document);
        var diagnostics = await DiagnosticsAsync(document);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That((await ActionsAsync(document, diagnostics.Single())).Count).IsEqualTo(0);
    }

    private static async Task<List<CodeAction>> ActionsAsync(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new IgnoredCancellationTokenCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    private static async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location));
        var project = workspace.AddProject("CodeFixTests", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithMetadataReferences(references);
        return project.AddDocument("Test.cs", SourceText.From(source));
    }

    private static async Task AssertCompilesAsync(Document document)
    {
        var compilation = (await document.Project.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        await Assert.That(string.Join(Environment.NewLine, errors)).IsEqualTo("");
    }

    private static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(Document document)
    {
        var compilation = (await document.Project.GetCompilationAsync())!;
        var tree = await document.GetSyntaxTreeAsync();
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new IgnoredCancellationTokenAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == tree)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static string Source(string body, string members = "") => $$"""
        using System;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Kevlar;
        class Subject
        {
            async Task Run(HttpClient client, string url)
            {
                {{body}}
            }
            {{members}}
        }
        """;

    private sealed class DocumentDiagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            await DiagnosticsAsync(document);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents) { diagnostics.AddRange(await DiagnosticsAsync(document)); }
            return diagnostics;
        }
    }
}
