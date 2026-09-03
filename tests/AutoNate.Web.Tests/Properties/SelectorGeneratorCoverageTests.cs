using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Tests.Properties.Generators;
using FsCheck.Fluent;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Asserts the selector generators reach every construct the grammar has.
/// </summary>
/// <remarks>
/// Without this the agreement property could pass by only ever generating
/// trivial selectors — #72 calls this out explicitly, and it is the difference
/// between a suite that checks something and one that merely runs.
/// </remarks>
public sealed class SelectorGeneratorCoverageTests
{
    private const int Samples = 2000;

    private static IEnumerable<PredicateExpr> Exprs(SelectorAst ast)
    {
        if (ast.Predicate is null) yield break;
        foreach (var e in ast.Predicate.Expressions)
        {
            yield return e;
            if (e is TagExpr { Nested: not null } t)
            {
                foreach (var inner in t.Nested.Expressions) yield return inner;
            }
        }
    }

    [Fact]
    public void The_full_generator_reaches_every_grammar_construct()
    {
        var asts = SelectorGenerators.AnySelector().Sample(Samples, 20).ToList();
        var missing = new List<string>();

        void Require(string what, Func<SelectorAst, bool> p)
        {
            if (!asts.Any(p)) missing.Add(what);
        }

        Require("no predicate", a => a.Predicate is null);
        Require("predicate", a => a.Predicate is not null);
        Require("multiple conjuncts", a => a.Predicate is { Expressions.Count: > 1 });
        Require("wildcard kind", a => a.Path.KindsAreWildcard);
        Require("multiple kinds", a => a.Path.Kinds.Count > 1);
        Require("explicit ids", a => a.Path.Ids is { Count: > 0 } && !a.Path.IdsAreWildcard);
        Require("wildcard ids", a => a.Path.IdsAreWildcard);
        Require("no ids", a => a.Path.Ids is null);

        Require("TagExpr", a => Exprs(a).OfType<TagExpr>().Any());
        Require("ScopeExpr", a => Exprs(a).OfType<ScopeExpr>().Any());
        Require("nested predicate (multi-hop)", a => Exprs(a).OfType<TagExpr>().Any(t => t.Nested is not null));

        Require("LiteralValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is LiteralValue));
        Require("WildcardValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is WildcardValue));
        Require("CurrentUserValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is CurrentUserValue { PinnedId: null }));
        Require("CurrentUserValue pinned", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is CurrentUserValue { PinnedId: not null }));
        Require("QualifiedValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is QualifiedValue));

        // Values needing quoting in canonical form — the round-trip property is
        // only meaningful if the printer's quoting path is actually exercised.
        Require("value needing quoting", a => Exprs(a).OfType<TagExpr>()
            .Any(t => t.Value is LiteralValue l
                      && (l.Text.Contains(' ') || l.Text.Contains('"') || l.Text.Contains('['))));

        Assert.True(
            missing.Count == 0,
            $"The full selector generator never produced: {string.Join(", ", missing)}. "
            + $"An unreachable construct is untested rather than merely uncommon. (Sampled {Samples}.)");
    }

    [Fact]
    public void The_shared_generator_reaches_every_construct_both_paths_implement()
    {
        var asts = SelectorGenerators.SharedSelector().Sample(Samples, 20).ToList();
        var missing = new List<string>();

        void Require(string what, Func<SelectorAst, bool> p)
        {
            if (!asts.Any(p)) missing.Add(what);
        }

        foreach (var tag in new[] { "processkey", "definitionkey", "assignee" })
        {
            var captured = tag;
            Require($"tag {captured}", a => Exprs(a).OfType<TagExpr>().Any(t => t.Tag == captured));
        }

        Require("LiteralValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is LiteralValue));
        Require("CurrentUserValue", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is CurrentUserValue { PinnedId: null }));
        Require("CurrentUserValue pinned", a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is CurrentUserValue { PinnedId: not null }));
        Require("multiple conjuncts", a => a.Predicate is { Expressions.Count: > 1 });

        Assert.True(
            missing.Count == 0,
            $"The shared selector generator never produced: {string.Join(", ", missing)}. "
            + $"The agreement property is only as strong as what this reaches. (Sampled {Samples}.)");

        // The wildcard must stay out until GHSA-vrw7-qxhw-m9q8 is resolved.
        // Left in, it buries every future divergence under the known one — the
        // first run reported 69 leaks and 539 lockouts, all of them this.
        Assert.DoesNotContain(
            asts,
            a => Exprs(a).OfType<TagExpr>().Any(t => t.Value is WildcardValue));
    }
}
