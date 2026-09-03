using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Tests.Properties.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Property-based tests for the authorization selector grammar.
/// </summary>
/// <remarks>
/// The selector grammar decides who can see which rows, and it is implemented
/// twice. Two implementations of one grammar is a bug waiting to be found: a
/// row one path hides and the other returns is an authorization defect, and it
/// will be on a selector nobody wrote by hand.
///
/// <para><b>Which two implementations.</b> #72 nominates
/// <c>InMemorySelectorEvaluator</c> against <c>RecordSelectorSqlCompiler</c>.
/// Those never evaluate the same thing. <c>InMemorySelectorEvaluator</c> is
/// constructed in exactly three places — <c>FlowableInstanceAuthorizers</c>
/// (tasks and executions) and <c>ExecutionEndpoints</c> — all WorkflowTask and
/// WorkflowExecution. Records go through the record compilers only, with no
/// in-memory path to disagree with.</para>
///
/// <para>The kinds that genuinely have two implementations are WorkflowTask and
/// WorkflowExecution: in memory against live Flowable data, and in SQL against
/// the <c>workflow_*_cache</c> tables. That is the duplicated grammar, so that
/// is what the agreement property targets. Records are excluded here for the
/// stated reason rather than silently.</para>
///
/// <para><b>What the grammar does not have.</b> #72 asks for negation and
/// disjunction to be generated. Neither exists: <c>PredicateNode</c> is a flat
/// list of expressions combined with AND on every path, and the parser has no
/// syntax for either. The generator covers what is real — conjunction, the
/// multi-hop nested form, ScopeExpr, and all four value kinds.</para>
/// </remarks>
public sealed class SelectorGrammarProperties
{
    private const int Runs = 300;

    [Property(MaxTest = Runs, Replay = "(2468013579,1357924681)")]
    public Property Parse_either_returns_an_ast_or_throws_SelectorParseException()
    {
        return Prop.ForAll(Arb.From(HostileSelectorText()), text =>
        {
            try
            {
                SelectorParser.Parse(text);
                return true;
            }
            catch (SelectorParseException)
            {
                return true;
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Parse threw {ex.GetType().Name} instead of SelectorParseException.\n"
                    + $"Input: {text}\nMessage: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Canonical printing must not change what a selector permits.
    /// </summary>
    /// <remarks>
    /// Stored selectors round-trip through <c>ToCanonicalString</c>, so a lossy
    /// print silently rewrites an authorization rule — the failure would show
    /// up as someone quietly gaining or losing access, with nothing in the
    /// audit log to explain it.
    /// </remarks>
    [Property(MaxTest = Runs, Replay = "(2468013579,1357924681)")]
    public Property Canonical_printing_round_trips()
    {
        return Prop.ForAll(SelectorGenerators.AnySelectorArb(), ast =>
        {
            var printed = SelectorPrinter.ToCanonicalString(ast);

            SelectorAst reparsed;
            try
            {
                reparsed = SelectorParser.Parse(printed);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"A canonically printed selector did not parse.\n  printed: {printed}\n"
                    + $"  {ex.GetType().Name}: {ex.Message}");
            }

            var again = SelectorPrinter.ToCanonicalString(reparsed);
            if (again != printed)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Canonical printing is not stable.\n  first : {printed}\n  second: {again}");
            }

            return true;
        });
    }

    private static Gen<string> HostileSelectorText()
    {
        var fragments = Gen.Elements(
            "[", "]", "[[", "]]", "(", ")", "{", "}",
            "=", "==", "=[", "[=]", ",", ":", "::", "*", "**",
            "\"", "\"unterminated", "\\", "a\\", "'",
            "workflowtask", "record", "*", "user", "role:", ":supervisor",
            "workflowtask[", "workflowtask[assignee", "workflowtask[assignee=",
            "workflowtask[assignee=user[", "/", "//", "///",
            " ", "\t", "\n", "\0");

        return Gen.Frequency(
            (6, Gen.ListOf(fragments).Select(parts => string.Concat(parts))),
            (2, Gen.Choose(20, 200).Select(n =>
                "workflowtask[" + string.Concat(Enumerable.Repeat("assignee=user[", n))
                + "assignee=x" + string.Concat(Enumerable.Repeat("]", n)) + "]")),
            (3, ArbMap.Default.GeneratorFor<string>().Select(s => s ?? string.Empty)));
    }
}

/// <summary>
/// The cross-evaluator agreement property: the in-memory evaluator and the SQL
/// compiler must accept exactly the same rows.
/// </summary>
/// <remarks>
/// Separated from the parser properties because it needs a real database. The
/// SQL side is executed by Postgres through EF Core, never by LINQ-to-objects
/// — evaluating the compiled expression in memory would test the wrong thing
/// entirely and hide every provider translation difference, which is most of
/// what could go wrong.
///
/// One database and one row set per property run, with many generated
/// selectors evaluated against them, rather than a database per generated
/// case: this runs inside a CI shard that now finishes in about four minutes,
/// and #67's gains should survive this story.
/// </remarks>
public sealed class SelectorEvaluatorAgreementProperties
{
    [Fact]
    public async Task The_two_evaluators_accept_exactly_the_same_rows()
    {
        // Booting the app runs DatabaseSchemaInitializer.EnsureAsync, which is
        // what creates workflow_task_cache. PostgresTestDatabase alone applies
        // only BaseSchema.sql, where the projection cache tables do not live —
        // the first version of this test failed with
        // `42P01: relation "workflow_task_cache" does not exist`.
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        // One fixture, materialised once, seen by both paths.
        var rows = SelectorGenerators.TaskRows(40).Sample(1, 42).Single();

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(rows);
            await seed.SaveChangesAsync();
        }

        var selectors = SelectorGenerators.SharedSelector().Sample(200, 40).ToList();

        var compiler = new WorkflowTaskCacheSelectorCompiler();
        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);

        var leaks = new List<string>();
        var lockouts = new List<string>();

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);

        foreach (var selector in selectors)
        {
            var predicate = compiler.Compile(selector, context);

            // Executed by Postgres. Never .AsEnumerable() before Where().
            var fromSql = (await db.WorkflowTaskCache
                    .Where(predicate)
                    .Select(t => t.FlowableTaskId)
                    .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

            var fromMemory = rows
                .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
                .Select(r => r.FlowableTaskId)
                .ToHashSet(StringComparer.Ordinal);

            // The two directions are different severities and the message says
            // which: SQL returning a row memory would hide is a potential leak;
            // memory accepting a row SQL excluded is a lockout.
            var text = SelectorPrinter.ToCanonicalString(selector);

            if (fromSql.SetEquals(fromMemory)) continue;

            // Shrink before reporting. This property is a Fact rather than an
            // FsCheck Property — one database and one row set serve 200
            // selectors, because a database per generated case would undo
            // #67's sharding gains — so FsCheck's shrinker never runs. Doing it
            // by hand costs a few extra queries only on failure, and the
            // difference is real: the first disagreement found here arrived as
            // a three-conjunct selector when one conjunct was responsible.
            var minimal = await ShrinkDisagreement(selector, rows, db, compiler, context, evaluator);
            var minimalText = SelectorPrinter.ToCanonicalString(minimal);

            var minSql = (await db.WorkflowTaskCache
                    .Where(compiler.Compile(minimal, context))
                    .Select(t => t.FlowableTaskId).ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
            var minMemory = rows
                .Where(r => evaluator.Matches(minimal, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
                .Select(r => r.FlowableTaskId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var id in minSql.Except(minMemory))
            {
                leaks.Add($"  LEAK    {minimalText} -> SQL returned {id}, memory hid it"
                          + (minimalText == text ? string.Empty : $"   (shrunk from {text})"));
            }

            foreach (var id in minMemory.Except(minSql))
            {
                lockouts.Add($"  LOCKOUT {minimalText} -> memory allowed {id}, SQL excluded it"
                             + (minimalText == text ? string.Empty : $"   (shrunk from {text})"));
            }
        }

        Assert.True(
            leaks.Count == 0 && lockouts.Count == 0,
            $"The in-memory evaluator and the SQL compiler disagree on "
            + $"{leaks.Count} leak(s) and {lockouts.Count} lockout(s) across "
            + $"{selectors.Count} selectors and {rows.Length} rows.\n"
            + string.Join("\n", leaks.Take(10).Concat(lockouts.Take(10))));
    }

    /// <summary>
    /// The wildcard divergence — a real authorization defect, pinned.
    /// </summary>
    /// <remarks>
    /// Draft advisory <b>GHSA-vrw7-qxhw-m9q8</b>. The agreement property found
    /// it on its first run: 69 leaks and 539 lockouts across 200 selectors and
    /// 40 rows, every one a wildcard value.
    ///
    /// <para><c>ResolveTagValue</c> maps <c>WildcardValue</c> to null, and
    /// <c>CompileStringEquals</c> reads a null value as an explicit request to
    /// match NULL — the branch meant for <c>tag=null</c>. So <c>assignee=*</c>
    /// compiles to <c>assignee IS NULL</c>, while
    /// <c>InMemorySelectorEvaluator</c> reads the same wildcard as
    /// <c>actual is not null</c>. Exact complements.</para>
    ///
    /// <para><b>Not fixed here, deliberately.</b> Correcting it widens what
    /// existing grants permit — a <c>tag=*</c> grant would start matching rows
    /// it currently excludes — and that is a decision for a person rather than
    /// something to slip into a test story. This test pins the broken behaviour
    /// so it cannot drift while the decision is pending, and fails loudly the
    /// moment either side changes.</para>
    /// </remarks>
    [Fact]
    public async Task The_wildcard_divergence_still_holds()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var assigned = NewRow("task-assigned", assignee: "alice");
        var unassigned = NewRow("task-unassigned", assignee: null);

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(assigned, unassigned);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[assignee=*]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).ToListAsync();

        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);
        var fromMemory = new[] { assigned, unassigned }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .ToList();

        // The defect, stated as an assertion. If either of these fails, the
        // wildcard semantics changed — reconcile with GHSA-vrw7-qxhw-m9q8,
        // and if it is fixed, remove the exclusion in
        // SelectorGenerators.ValueFor so the agreement property covers it.
        Assert.Equal(["task-unassigned"], fromSql);
        Assert.Equal(["task-assigned"], fromMemory);
    }

    /// <summary>
    /// The smallest selector derived from <paramref name="selector"/> on which
    /// the two evaluators still disagree.
    /// </summary>
    /// <remarks>
    /// Greedy: repeatedly take the first candidate that still disagrees, then
    /// start again from it, exactly as FsCheck's shrinker would. Candidates
    /// come from the same generator-side shrinker the AQL properties use, so
    /// the two suites behave the same way on failure.
    /// </remarks>
    private static async Task<SelectorAst> ShrinkDisagreement(
        SelectorAst selector,
        WorkflowTaskCache[] rows,
        AutoNateDbContext db,
        WorkflowTaskCacheSelectorCompiler compiler,
        CompilationContext context,
        InMemorySelectorEvaluator evaluator)
    {
        var current = selector;

        for (var step = 0; step < 20; step++)
        {
            var improved = false;

            foreach (var candidate in SelectorGenerators.ShrinkForTests(current))
            {
                HashSet<string> sql;
                try
                {
                    sql = (await db.WorkflowTaskCache
                            .Where(compiler.Compile(candidate, context))
                            .Select(t => t.FlowableTaskId).ToListAsync())
                        .ToHashSet(StringComparer.Ordinal);
                }
                catch (SelectorCompilationException)
                {
                    // A candidate the SQL side refuses outright is not a
                    // smaller example of the same disagreement.
                    continue;
                }

                var memory = rows
                    .Where(r => evaluator.Matches(candidate, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
                    .Select(r => r.FlowableTaskId)
                    .ToHashSet(StringComparer.Ordinal);

                if (!sql.SetEquals(memory))
                {
                    current = candidate;
                    improved = true;
                    break;
                }
            }

            if (!improved) break;
        }

        return current;
    }

    private static WorkflowTaskCache NewRow(string id, string? assignee) => new()
    {
        FlowableTaskId = id,
        FlowableInstanceId = "inst-1",
        ProcessDefinitionKey = "onboarding",
        TaskDefinitionKey = "approve",
        Assignee = assignee,
        CandidateUsers = [],
        CandidateGroups = [],
        CreatedTime = DateTime.UtcNow,
        Status = "active",
        LastSyncAtUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// The known divergence, pinned so it cannot widen unnoticed.
    /// </summary>
    /// <remarks>
    /// <c>FlowableInstanceAuthorizers.BuildFacts</c> supplies three facts —
    /// assignee, processkey, definitionkey — and its comment records why
    /// candidategroup is absent: Flowable's task summary endpoint returns no
    /// identity links, so "grants like [candidategroup=...] silently miss".
    /// <c>WorkflowTaskCacheSelectorCompiler</c> supports both candidate tags.
    ///
    /// So a grant on either candidate tag is honoured by the SQL path and
    /// refused by the in-memory path. That is a real, deliberate inconsistency
    /// rather than a bug this story introduced, and it is excluded from the
    /// agreement property above so it does not drown out new divergences.
    /// This test asserts it still behaves exactly as documented — if the
    /// in-memory path ever gains those facts, this fails and the exclusion
    /// above should be removed.
    /// </remarks>
    [Fact]
    public async Task The_known_candidate_tag_divergence_still_holds()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var row = new WorkflowTaskCache
        {
            FlowableTaskId = "task-candidate",
            FlowableInstanceId = "inst-1",
            ProcessDefinitionKey = "onboarding",
            TaskDefinitionKey = "approve",
            Assignee = null,
            CandidateUsers = ["alice"],
            CandidateGroups = ["finance"],
            CreatedTime = DateTime.UtcNow,
            Status = "active",
            LastSyncAtUtc = DateTime.UtcNow,
        };

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.Add(row);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[candidategroup=finance]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var sqlMatches = await db.WorkflowTaskCache.Where(predicate).AnyAsync();
        var memoryMatches = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId)
            .Matches(selector, row.FlowableTaskId, SelectorGenerators.FactsFor(row));

        Assert.True(sqlMatches, "The SQL path should honour [candidategroup=finance].");
        Assert.False(
            memoryMatches,
            "The in-memory path unexpectedly honoured [candidategroup=finance]. If "
            + "FlowableInstanceAuthorizers.BuildFacts now supplies candidate facts, this "
            + "divergence is fixed — remove the exclusion in SelectorGenerators.SharedTags "
            + "so the agreement property covers these tags too.");
    }
}
