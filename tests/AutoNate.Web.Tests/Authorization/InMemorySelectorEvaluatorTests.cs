using AutoNate.Web.Authorization.Selectors;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

public sealed class InMemorySelectorEvaluatorTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SelectorAst Parse(string s) => SelectorParser.Parse(s);

    private static IReadOnlyDictionary<string, string?> Facts(params (string K, string? V)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    [Fact]
    public void Wildcard_PathOnly_AlwaysMatches()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        Assert.True(eval.Matches(Parse("/workflowtask/*"), "task-1", Facts()));
    }

    [Fact]
    public void SpecificId_MatchesOnlyThatId()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/task-7");
        Assert.True(eval.Matches(ast, "task-7", Facts()));
        Assert.False(eval.Matches(ast, "task-9", Facts()));
    }

    [Fact]
    public void AssigneeUser_MatchesWhenFactEqualsActor()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/*[assignee=user]");
        Assert.True(eval.Matches(ast, "t1", Facts(("assignee", Actor.ToString()))));
        Assert.False(eval.Matches(ast, "t1", Facts(("assignee", Guid.NewGuid().ToString()))));
        Assert.False(eval.Matches(ast, "t1", Facts(("assignee", null))));
    }

    [Fact]
    public void LiteralTagMatch_CaseInsensitive()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/*[processkey=lead]");
        Assert.True(eval.Matches(ast, "t1", Facts(("processkey", "LEAD"))));
        Assert.False(eval.Matches(ast, "t1", Facts(("processkey", "deal"))));
    }

    [Fact]
    public void MultipleTags_AreAnded()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/*[processkey=lead;assignee=user]");
        Assert.True(eval.Matches(ast, "t1", Facts(
            ("processkey", "lead"), ("assignee", Actor.ToString()))));
        Assert.False(eval.Matches(ast, "t1", Facts(
            ("processkey", "lead"), ("assignee", Guid.NewGuid().ToString()))));
    }

    [Fact]
    public void ScopeExpression_UnsupportedFailsClosed()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/*[scope:supervisor]");
        Assert.False(eval.Matches(ast, "t1", Facts()));
    }

    [Fact]
    public void QualifiedValue_UnsupportedFailsClosed()
    {
        var eval = new InMemorySelectorEvaluator(Actor);
        var ast = Parse("/workflowtask/*[role=role:supervisor]");
        Assert.False(eval.Matches(ast, "t1", Facts(("role", "supervisor"))));
    }
}
