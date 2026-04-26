using AutoNate.Web.Authorization.Selectors;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

public sealed class SelectorParserTests
{
    [Theory]
    [InlineData("/recordtype/*")]
    [InlineData("/recordtype/myrecordtype")]
    [InlineData("/record/*[recordtype=myrecordtype]")]
    [InlineData("/record/*[creator=user]")]
    [InlineData("/record/*[assignee=user]")]
    [InlineData("/user/*[supervisor=user]")]
    [InlineData("/record/*[assignee=user[supervisor=user]]")]
    [InlineData("/workflowexecution/*[processkey=lead;startedby=user]")]
    public void Parse_RoundTrip_PreservesCanonicalForm(string input)
    {
        var ast = SelectorParser.Parse(input);
        var canonical = SelectorPrinter.ToCanonicalString(ast);
        var reparsed = SelectorParser.Parse(canonical);

        Assert.Equal(canonical, SelectorPrinter.ToCanonicalString(reparsed));
    }

    [Fact]
    public void Parse_PathOnly_NoPredicate()
    {
        var ast = SelectorParser.Parse("/record/*");
        Assert.Single(ast.Path.Kinds, "record");
        Assert.NotNull(ast.Path.Ids);
        Assert.True(ast.Path.IdsAreWildcard);
        Assert.Null(ast.Predicate);
    }

    [Fact]
    public void Parse_KindSet_CapturesAllNames()
    {
        var ast = SelectorParser.Parse("/{record,recordtype}/*");
        Assert.Equal(new[] { "record", "recordtype" }, ast.Path.Kinds);
    }

    [Fact]
    public void Parse_TagEquality_ProducesTagExpr()
    {
        var ast = SelectorParser.Parse("/record/*[recordtype=lead]");
        Assert.NotNull(ast.Predicate);
        var expr = Assert.Single(ast.Predicate.Expressions);
        var tag = Assert.IsType<TagExpr>(expr);
        Assert.Equal("recordtype", tag.Tag);
        var literal = Assert.IsType<LiteralValue>(tag.Value);
        Assert.Equal("lead", literal.Text);
        Assert.Null(tag.Nested);
    }

    [Fact]
    public void Parse_CurrentUserValue_IsSeparateNode()
    {
        var ast = SelectorParser.Parse("/record/*[assignee=user]");
        var tag = Assert.IsType<TagExpr>(ast.Predicate!.Expressions[0]);
        Assert.IsType<CurrentUserValue>(tag.Value);
    }

    [Fact]
    public void Parse_NestedPredicateOnUser_WalksFurther()
    {
        var ast = SelectorParser.Parse("/record/*[assignee=user[supervisor=user]]");
        var tag = Assert.IsType<TagExpr>(ast.Predicate!.Expressions[0]);
        Assert.IsType<CurrentUserValue>(tag.Value);
        Assert.NotNull(tag.Nested);
        var nested = Assert.IsType<TagExpr>(tag.Nested.Expressions[0]);
        Assert.Equal("supervisor", nested.Tag);
        Assert.IsType<CurrentUserValue>(nested.Value);
    }

    [Fact]
    public void Parse_QualifiedValue_RoleSupervisor()
    {
        var ast = SelectorParser.Parse("/record/*[scope=role:supervisor]");
        var tag = Assert.IsType<TagExpr>(ast.Predicate!.Expressions[0]);
        var qual = Assert.IsType<QualifiedValue>(tag.Value);
        Assert.Equal("role", qual.Qualifier);
        Assert.Equal("supervisor", qual.Name);
    }

    [Fact]
    public void Parse_SemicolonAndCommaSeparators_BothWork()
    {
        var withSemi = SelectorParser.Parse("/record/*[recordtype=lead;assignee=user]");
        var withComma = SelectorParser.Parse("/record/*[recordtype=lead,assignee=user]");
        Assert.Equal(2, withSemi.Predicate!.Expressions.Count);
        Assert.Equal(2, withComma.Predicate!.Expressions.Count);
    }

    [Fact]
    public void Parse_PinnedUserId_CapturesId()
    {
        var ast = SelectorParser.Parse("/record/*[assignee=user/abc-123]");
        var tag = Assert.IsType<TagExpr>(ast.Predicate!.Expressions[0]);
        var user = Assert.IsType<CurrentUserValue>(tag.Value);
        Assert.Equal("abc-123", user.PinnedId);
    }

    [Theory]
    [InlineData("recordtype/*")]            // missing leading /
    [InlineData("/record/*[")]              // unterminated predicate
    [InlineData("/record/*[recordtype]")]    // no = or :
    [InlineData("/record/*[=foo]")]          // missing key
    [InlineData("/{}")]                      // empty set
    public void Parse_InvalidInput_Throws(string input)
    {
        Assert.Throws<SelectorParseException>(() => SelectorParser.Parse(input));
    }

    [Fact]
    public void Parse_TrailingPathSeparatorBeforePredicate_Tolerated()
    {
        // The user's original spec used `/workflowexecution/{a,b,c}/[scope=role:supervisor;assignee=user]`
        // — an extra `/` before the predicate. The parser accepts and normalizes it.
        var ast = SelectorParser.Parse("/workflowexecution/{a,b,c}/[scope=role:supervisor;assignee=user]");
        Assert.Equal(new[] { "a", "b", "c" }, ast.Path.Ids);
        Assert.Equal(2, ast.Predicate!.Expressions.Count);
    }
}
