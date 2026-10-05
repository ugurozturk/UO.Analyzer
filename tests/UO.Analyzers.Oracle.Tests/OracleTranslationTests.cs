using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace UO.Analyzers.Oracle.Tests;

// ToQueryString exercises the stock provider translation pipeline without opening a connection.
public sealed class OracleTranslationTests
{
    public static IEnumerable<object[]> UnsupportedPredicates()
    {
        yield return Case(x => x.Name.ToLowerInvariant() == "i");
        yield return Case(x => x.Name.ToUpperInvariant() == "I");
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.GetCultureInfo("en-US") })
        {
            yield return Case(x => x.Name.ToLower(culture) == "i");
            yield return Case(x => x.Name.ToUpper(culture) == "I");
        }
        foreach (var comparison in Enum.GetValues<StringComparison>())
        {
            yield return Case(x => x.Name.Contains("i", comparison));
            yield return Case(x => x.Name.StartsWith("i", comparison));
            yield return Case(x => x.Name.EndsWith("i", comparison));
            yield return Case(x => x.Name.Equals("i", comparison));
            yield return Case(x => string.Equals(x.Name, "i", comparison));
        }
    }

    public static IEnumerable<object[]> SupportedPredicates()
    {
        yield return Case(x => x.Name.ToLower() == "i");
        yield return Case(x => x.Name.ToUpper() == "I");
        yield return Case(x => x.Name.Contains("i"));
        yield return Case(x => x.Name.StartsWith("i"));
        yield return Case(x => x.Name.EndsWith("i"));
        yield return Case(x => x.Name.Equals("i"));
        yield return Case(x => string.Equals(x.Name, "i"));
        var term = "I";
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        yield return Case(x => x.Name == term.ToLower(culture));
    }

    [Theory]
    [MemberData(nameof(UnsupportedPredicates))]
    public void CataloguedOverloadsFailSqlTranslation(Expression<Func<Customer, bool>> predicate)
    {
        using var db = new OracleContext();
        var error = Assert.Throws<InvalidOperationException>(() => db.Set<Customer>().Where(predicate).ToQueryString());
        Assert.Contains("could not be translated", error.Message);
    }

    [Theory]
    [MemberData(nameof(SupportedPredicates))]
    public void NeighboringOverloadsAndCapturedValuesTranslate(Expression<Func<Customer, bool>> predicate)
    {
        using var db = new OracleContext();
        var sql = db.Set<Customer>().Where(predicate).ToQueryString();
        Assert.Contains("SELECT", sql);
        Assert.Contains("WHERE", sql);
    }

    private static object[] Case(Expression<Func<Customer, bool>> predicate) => [predicate];

    public sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class OracleContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseOracle("User Id=unused;Password=unused;Data Source=unused");

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Customer>();
    }
}
