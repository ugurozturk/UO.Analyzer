using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using EfAnalyzerTest = UO.Analyzers.Tests.Infrastructure.AnalyzerTest<UO.Analyzers.Rules.UO0001UnsupportedEfCoreTranslation.UnsupportedEfCoreTranslationAnalyzer>;

namespace UO.Analyzers.Tests.Analyzers;

public sealed class UnsupportedEfCoreTranslationTests
{
    public static IEnumerable<object[]> UnsupportedOverloads()
    {
        (string Expression, string Signature)[] cases =
        [
            ("x.Name.ToLowerInvariant()", "string.ToLowerInvariant()"),
            ("x.Name.ToUpperInvariant()", "string.ToUpperInvariant()"),
            ("x.Name.ToLower(CultureInfo.InvariantCulture)", "string.ToLower(CultureInfo)"),
            ("x.Name.ToUpper(CultureInfo.CurrentCulture)", "string.ToUpper(CultureInfo)"),
            ("x.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)", "string.Contains(string, StringComparison)"),
            ("x.Name.StartsWith(term, StringComparison.Ordinal)", "string.StartsWith(string, StringComparison)"),
            ("x.Name.EndsWith(term, StringComparison.OrdinalIgnoreCase)", "string.EndsWith(string, StringComparison)"),
            ("x.Name.Equals(term, StringComparison.Ordinal)", "string.Equals(string, StringComparison)"),
            ("string.Equals(term, x.Name, StringComparison.OrdinalIgnoreCase)", "string.Equals(string, string, StringComparison)"),
        ];
        foreach (var (expression, signature) in cases)
        {
            foreach (var (profile, display) in new[]
            {
                ("npgsql-8.0.0", "Npgsql 8.0.0"),
                ("sqlite-8.0.0", "SQLite 8.0.0"),
                ("npgsql-8.0.0,sqlite-8.0.0", "Npgsql 8.0.0, SQLite 8.0.0"),
            })
                yield return [expression, signature, profile, display];
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedOverloads))]
    public async Task ExactOverloadsOnEfRows(string expression, string signature, string profile, string display)
    {
        var test = Create("_ = db.Customers.Where(x => {|#0:" + expression + "|} == default);", profile);
        test.ExpectedDiagnostics.Add(Expected(signature, display));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("Where")]
    [InlineData("Any")]
    [InlineData("All")]
    [InlineData("Count")]
    [InlineData("LongCount")]
    [InlineData("First")]
    [InlineData("FirstOrDefault")]
    [InlineData("Single")]
    [InlineData("SingleOrDefault")]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    public async Task SupportedPredicateContexts(string method)
    {
        var test = Create("_ = db.Set<Customer>()." + method + "(x => {|#0:x.Name.ToLowerInvariant()|} == term);");
        test.ExpectedDiagnostics.Add(Expected(context: "Queryable." + method));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("var source = db.Customers; var q = source.AsNoTracking().Where(x => x.Name != null); _ = q.Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);")]
    [InlineData("_ = Queryable.Where(source: db.Customers, predicate: x => {|#0:x.Name.ToLowerInvariant()|} == term);")]
    [InlineData("_ = Queryable.Where(predicate: (Customer x) => {|#0:x.Name.ToLowerInvariant()|} == term, source: db.Customers);")]
    [InlineData("_ = db.Customers.Select(x => x.Name).Where(x => {|#0:x.ToLowerInvariant()|} == term);")]
    [InlineData("_ = db.Customers.AsQueryable().Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);")]
    [InlineData("_ = db.Customers.Where(x => {|#0:((string)x.Name!).ToLowerInvariant()|} == term);")]
    [InlineData("_ = db.Customers.Where(x => {|#0:x.NullableName!.ToLowerInvariant()|} == term);")]
    public async Task ProvenQueryChainsAndConversions(string body)
    {
        var test = Create(body);
        test.ExpectedDiagnostics.Add(Expected());
        await test.RunAsync();
    }

    [Theory]
    [InlineData("query")]
    [InlineData("GetQuery()")]
    [InlineData("QueryProperty")]
    public async Task ExplicitOptInForAbstractions(string source)
    {
        var test = Create("_ = " + source + ".Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);", assumeEf: true);
        test.ExpectedDiagnostics.Add(Expected());
        await test.RunAsync();
    }

    [Fact]
    public async Task OptInLocalAlias()
    {
        var test = Create("var alias = GetQuery(); _ = alias.Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);", assumeEf: true);
        test.ExpectedDiagnostics.Add(Expected());
        await test.RunAsync();
    }

    [Fact]
    public async Task AwaitedRepositoryBoundaryRequiresOptIn()
    {
        var test = Create("var q = await GetQueryAsync(); _ = q.Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);", assumeEf: true);
        test.ExpectedDiagnostics.Add(Expected());
        await test.RunAsync();
        await Create("var q = await GetQueryAsync(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);").RunAsync();
    }

    [Fact]
    public async Task ImplicitConversionStillDependsOnTheRow()
    {
        var test = Create("_ = db.Customers.Where(x => {|#0:string.Equals(x.Name, token, StringComparison.Ordinal)|});");
        test.ExpectedDiagnostics.Add(Expected("string.Equals(string, string, StringComparison)"));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("x.Name")]
    [InlineData("y.Name")]
    public async Task CorrelatedPredicatesTrackBothRows(string receiver)
    {
        var test = Create("_ = db.Customers.Where(x => db.Customers.Any(y => {|#0:" + receiver + ".ToLowerInvariant()|} == term));");
        test.ExpectedDiagnostics.Add(Expected(context: "Queryable.Any"));
        await test.RunAsync();
    }

    [Fact]
    public async Task MaterializationBelongsToItsOwnChain()
    {
        var test = Create("var unrelated = db.Customers.ToList(); _ = db.Customers.Where(x => {|#0:x.Name.ToLowerInvariant()|} == term).ToList();");
        test.ExpectedDiagnostics.Add(Expected());
        await test.RunAsync();
    }

    [Theory]
    [InlineData("_ = db.Customers.Where(x => x.Name.ToLower().Contains(term));")]
    [InlineData("_ = db.Customers.Where(x => x.Name.ToUpper().StartsWith(term) || x.Name.EndsWith(term));")]
    [InlineData("_ = db.Customers.Where(x => x.Name.Equals(term) || string.Equals(x.Name, term));")]
    [InlineData("_ = db.Customers.Where(x => x.Name.Contains('a')); ")]
    [InlineData("var normalized = term.ToLowerInvariant(); _ = db.Customers.Where(x => x.Name.ToLower().Contains(normalized));")]
    [InlineData("_ = db.Customers.Where(x => x.Name.Contains(term.ToLowerInvariant()));")]
    [InlineData("_ = db.Customers.Where(x => term.ToLowerInvariant() == x.Name);")]
    [InlineData("_ = db.Customers.Where(x => string.Equals(term, term, StringComparison.Ordinal) && x.Name != null);")]
    [InlineData("_ = db.Customers.Where(x => nameof(x.Name).ToLowerInvariant() == term);")]
    [InlineData("_ = memory.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.AsEnumerable().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.ToList().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("var rows = await db.Customers.ToListAsync(); _ = rows.AsQueryable().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.ToArray().AsQueryable().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.AsEnumerable().AsQueryable().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("var list = db.Customers.ToList(); _ = list.AsQueryable().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("IQueryable<Customer> q = db.Customers; q = memory.AsQueryable(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("IQueryable<Customer> q = db.Customers; Action change = () => q = memory.AsQueryable(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("IQueryable<Customer> q = db.Customers; Reset(ref q); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("IQueryable<Customer> q = db.Customers; ref IQueryable<Customer> alias = ref q; alias = memory.AsQueryable(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.Select(x => x.Name.ToLowerInvariant()).ToList();")]
    [InlineData("_ = db.Customers.Select(x => x.Name.ToLowerInvariant()).Where(x => x == term);")]
    [InlineData("_ = db.Customers.Where((x, index) => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.OrderBy(x => x.Name.ToLowerInvariant());")]
    [InlineData("Expression<Func<Customer, bool>> predicate = x => x.Name.ToLowerInvariant() == term; _ = db.Customers.Where(predicate);")]
    [InlineData("_ = db.Customers.Where(x => memory.Any(y => y.Name.ToLowerInvariant() == x.Name));")]
    [InlineData("_ = db.Customers.Where(x => Helper(() => x.Name.ToLowerInvariant()));")]
    [InlineData("_ = db.Customers.Where(x => x.Custom.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.Where(x => x.ToLowerInvariant() == term);")]
    [InlineData("_ = db.Customers.Where(x => x.Name.Contains(term, true, CultureInfo.InvariantCulture));")]
    [InlineData("_ = new OtherProvider().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("IQueryable<Customer> q = term == null ? db.Customers : memory.AsQueryable(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    public async Task SupportedOrUnprovenCallsAreNotDiagnosedEvenWithOptIn(string body)
    {
        await Create(body, assumeEf: true).RunAsync();
    }

    [Theory]
    [InlineData("_ = query.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = GetQuery().Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = QueryProperty.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("var q = memory.AsQueryable(); _ = q.Where(x => x.Name.ToLowerInvariant() == term);")]
    [InlineData("_ = new EnumerableQuery<Customer>(memory).Where(x => x.Name.ToLowerInvariant() == term);")]
    public async Task EfReferenceAloneDoesNotOptIn(string body)
    {
        await Create(body).RunAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("npgsql")]
    [InlineData("sqlite-9.0.0")]
    [InlineData("unknown")]
    public async Task NoKnownProfileMeansNoClaim(string profile)
    {
        await Create("_ = db.Customers.Where(x => x.Name.ToLowerInvariant() == term);", profile).RunAsync();
    }

    [Fact]
    public async Task UnknownProfileIsNotNamedInTheDiagnostic()
    {
        var test = Create("_ = db.Customers.Where(x => {|#0:x.Name.ToLowerInvariant()|} == term);", "unknown, SQLITE-8.0.0,sqlite-8.0.0");
        test.ExpectedDiagnostics.Add(Expected(profile: "SQLite 8.0.0"));
        await test.RunAsync();
    }

    [Fact]
    public async Task GeneratedSourceIsIgnored()
    {
        var test = Create("_ = db.Customers.Where(x => x.Name.ToLowerInvariant() == term);", generated: true);
        await test.RunAsync();
    }

    [Theory]
    [InlineData("_ = db.Customers.Where(x => x.Name.ToLowerInvariant( == term);")]
    [InlineData("_ = db.Customers.Where(x => x.Missing.ToLowerInvariant() == term);")]
    public async Task BrokenSourceDoesNotCrashOrGuess(string body)
    {
        var test = Create(body);
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        await test.RunAsync();
    }

    [Fact]
    public async Task SourceDefinedEfLookalikeIsNotTrusted()
    {
        var test = new EfAnalyzerTest
        {
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Linq;
                namespace Microsoft.EntityFrameworkCore
                {
                    public class DbSet<T> : EnumerableQuery<T>
                    {
                        public DbSet(IEnumerable<T> items) : base(items) { }
                    }
                }
                class Service
                {
                    void Run(Microsoft.EntityFrameworkCore.DbSet<string> source)
                    {
                        _ = source.Where(x => x.ToLowerInvariant() == "x");
                    }
                }
                """,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n[*.cs]\ndotnet_code_quality.UO0001.ef_core_providers = sqlite-8.0.0"));
        await test.RunAsync();
    }

    private static DiagnosticResult Expected(string signature = "string.ToLowerInvariant()",
        string profile = "Npgsql 8.0.0", string context = "Queryable.Where") =>
        new DiagnosticResult("UO0001", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(signature, profile, context);

    private static EfAnalyzerTest Create(string body,
        string profile = "npgsql-8.0.0", bool assumeEf = false, bool generated = false)
    {
        var test = new EfAnalyzerTest
        {
            TestCode = (generated ? "// <auto-generated/>\n" : "") + $$"""
                #nullable enable
                using System;
                using System.Collections.Generic;
                using System.Globalization;
                using System.Linq;
                using System.Linq.Expressions;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;
                class Customer
                {
                    public string Name { get; set; } = "";
                    public string? NullableName { get; set; }
                    public CustomText Custom { get; set; } = new();
                }
                class CustomText { public string ToLowerInvariant() => ""; }
                class Token { public static implicit operator string(Token value) => ""; }
                class OtherProvider { public bool Where(Expression<Func<Customer, bool>> predicate) => true; }
                static class CustomExtensions
                {
                    public static string ToLowerInvariant(this Customer value) => "";
                    public static bool Contains(this string value, string term, bool ignore, CultureInfo culture) => false;
                }
                class Context : DbContext { public DbSet<Customer> Customers => Set<Customer>(); }
                class Service
                {
                    IQueryable<Customer> GetQuery() => throw new NotImplementedException();
                    Task<IQueryable<Customer>> GetQueryAsync() => Task.FromResult(GetQuery());
                    IQueryable<Customer> QueryProperty => GetQuery();
                    static bool Helper(Func<string> func) => true;
                    static void Reset(ref IQueryable<Customer> query) { }
                    async Task Run(Context db, IQueryable<Customer> query, List<Customer> memory, string term, Token token)
                    {
                        await Task.CompletedTask;
                        {{body}}
                    }
                }
                """,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(DbContext).Assembly.Location));
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(DeleteBehavior).Assembly.Location));
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $$"""
            root = true
            [*.cs]
            dotnet_code_quality.UO0001.ef_core_providers = {{profile}}
            dotnet_code_quality.UO0001.assume_ef_core_queryable = {{assumeEf.ToString().ToLowerInvariant()}}
            """));
        return test;
    }
}
