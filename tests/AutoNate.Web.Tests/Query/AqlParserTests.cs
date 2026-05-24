using System.Linq;
using AutoNate.Web.Services.Query;
using Xunit;

namespace AutoNate.Web.Tests.Query;

public sealed class AqlParserTests
{
    [Fact]
    public void From_Defaults_To_Records_When_Omitted()
    {
        var q = AqlParser.Parse("Name = \"x\"");
        Assert.Equal("Records", q.Entity);
        Assert.IsType<AqlCompare>(q.Where);
    }

    [Fact]
    public void Where_Keyword_Is_Optional_When_From_Omitted()
    {
        var q = AqlParser.Parse("RecordType = \"Car\"");
        var cmp = Assert.IsType<AqlCompare>(q.Where);
        Assert.Equal("RecordType", cmp.Field);
        Assert.Equal("=", cmp.Op);
        Assert.Equal("Car", ((AqlString)cmp.Value).Value);
    }

    [Fact]
    public void Case_Insensitive_Keywords()
    {
        var q = AqlParser.Parse("from records where Name = \"x\" order by Name desc limit 5");
        Assert.Equal("records", q.Entity);
        Assert.NotNull(q.Where);
        Assert.True(q.OrderBy[0].Descending);
        Assert.Equal(5, q.Limit);
    }

    [Fact]
    public void Strict_Clause_Order_Throws_On_Reversal()
    {
        Assert.Throws<AqlValidationException>(() =>
            AqlParser.Parse("FROM Records ORDER BY Name WHERE Name = \"x\""));
    }

    [Fact]
    public void And_Or_Precedence_With_Parens()
    {
        var q = AqlParser.Parse("a = 1 OR b = 2 AND c = 3");
        var top = Assert.IsType<AqlBinary>(q.Where);
        Assert.Equal("OR", top.Op);
        var right = Assert.IsType<AqlBinary>(top.Right);
        Assert.Equal("AND", right.Op);
    }

    [Fact]
    public void Parens_Group_Subexpression()
    {
        var q = AqlParser.Parse("(a = 1 OR b = 2) AND c = 3");
        var top = Assert.IsType<AqlBinary>(q.Where);
        Assert.Equal("AND", top.Op);
    }

    [Fact]
    public void Contains_Parses_As_Predicate()
    {
        var q = AqlParser.Parse("FROM Records WHERE CONTAINS(Name, \"abc\")");
        var ct = Assert.IsType<AqlContains>(q.Where);
        Assert.Equal("Name", ct.Field);
        Assert.Equal("abc", ct.Substr);
    }

    [Fact]
    public void Function_Compare_For_Scalar_Functions()
    {
        var q = AqlParser.Parse("FROM Workflows WHERE NUMNODES() > 0");
        var fcmp = Assert.IsType<AqlFunctionCompare>(q.Where);
        Assert.Equal("NUMNODES", fcmp.FnName);
        Assert.Equal(">", fcmp.Op);
        Assert.Equal(0d, ((AqlNumber)fcmp.Value).Value);
    }

    [Fact]
    public void Aggregate_Columns_Parse()
    {
        // Clause order per the AQL spec: COLUMNS comes before GROUP.
        var q = AqlParser.Parse(
            "FROM Records COLUMNS(RecordType, COUNT()) GROUP(RecordType)");
        Assert.NotNull(q.Columns);
        Assert.Equal(2, q.Columns!.Count);
        Assert.True(q.Columns[1].IsAggregate);
        Assert.Equal("COUNT", q.Columns[1].AggregateFn);
        Assert.NotNull(q.Group);
        Assert.Single(q.Group!);
    }

    [Fact]
    public void Limit_Must_Be_Positive_Integer()
    {
        Assert.Throws<AqlValidationException>(() => AqlParser.Parse("FROM Records LIMIT 0"));
    }

    [Fact]
    public void Column_Alias_On_Field()
    {
        var q = AqlParser.Parse("FROM Records COLUMNS(Name AS DisplayName)");
        var item = q.Columns![0];
        Assert.Equal("Name", item.Field);
        Assert.Equal("DisplayName", item.Alias);
        Assert.Equal("DisplayName", item.DisplayName);
    }

    [Fact]
    public void Column_Alias_On_Aggregate()
    {
        var q = AqlParser.Parse("FROM Records COLUMNS(RecordType, COUNT() AS MyCount) GROUP(RecordType)");
        var agg = q.Columns![1];
        Assert.True(agg.IsAggregate);
        Assert.Equal("COUNT", agg.AggregateFn);
        Assert.Equal("MyCount", agg.Alias);
        Assert.Equal("MyCount", agg.DisplayName);
    }

    [Fact]
    public void Missing_Alias_After_As_Throws()
    {
        Assert.Throws<AqlValidationException>(() =>
            AqlParser.Parse("FROM Records COLUMNS(Name AS)"));
    }

    [Fact]
    public void Alias_Is_Case_Insensitive_Keyword()
    {
        var q = AqlParser.Parse("FROM Records COLUMNS(Name as Display)");
        Assert.Equal("Display", q.Columns![0].Alias);
    }

    [Fact]
    public void Infix_IN_Single_Value_Parses()
    {
        var q = AqlParser.Parse("FROM Flows WHERE Status IN (\"In-progress\")");
        var inFilter = Assert.IsType<AqlIn>(q.Where);
        Assert.Equal("Status", inFilter.Field);
        Assert.Single(inFilter.Values);
        Assert.Equal("In-progress", Assert.IsType<AqlString>(inFilter.Values[0]).Value);
    }

    [Fact]
    public void Infix_IN_Multiple_Values_Parses()
    {
        var q = AqlParser.Parse(
            "FROM Flows WHERE Status IN (\"In-progress\", \"Errored\", \"Suspended\")");
        var inFilter = Assert.IsType<AqlIn>(q.Where);
        Assert.Equal(3, inFilter.Values.Count);
        Assert.Equal("In-progress", Assert.IsType<AqlString>(inFilter.Values[0]).Value);
        Assert.Equal("Errored",     Assert.IsType<AqlString>(inFilter.Values[1]).Value);
        Assert.Equal("Suspended",   Assert.IsType<AqlString>(inFilter.Values[2]).Value);
    }

    [Fact]
    public void Infix_IN_Is_Case_Insensitive()
    {
        var q = AqlParser.Parse("FROM Flows WHERE Status in (\"x\")");
        Assert.IsType<AqlIn>(q.Where);
    }

    [Fact]
    public void Prefix_IN_Still_Parses_The_Same_Way()
    {
        // Both syntaxes should produce the identical AqlIn node.
        var infix = AqlParser.Parse("FROM Flows WHERE Status IN (\"a\", \"b\")");
        var prefix = AqlParser.Parse("FROM Flows WHERE IN(Status, \"a\", \"b\")");
        var ai = Assert.IsType<AqlIn>(infix.Where);
        var ap = Assert.IsType<AqlIn>(prefix.Where);
        Assert.Equal(ai.Field, ap.Field);
        Assert.Equal(ai.Values.Count, ap.Values.Count);
    }

    [Fact]
    public void Infix_IN_With_Empty_List_Throws()
    {
        Assert.Throws<AqlValidationException>(() =>
            AqlParser.Parse("FROM Flows WHERE Status IN ()"));
    }
}
