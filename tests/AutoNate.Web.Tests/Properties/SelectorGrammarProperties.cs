using AutoNate.Web.Authorization;
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

            // The actor's outbound user edges, in the table the SQL subquery
            // reads (#575). The in-memory evaluator is handed the SAME
            // declaration below, so nesting is compared across one world rather
            // than two.
            foreach (var (edgeKind, targets) in SelectorGenerators.ActorOutboundEdges)
            {
                foreach (var target in targets)
                {
                    seed.EntityEdges.Add(new EntityEdge
                    {
                        Id = Guid.NewGuid(),
                        EdgeKind = edgeKind,
                        FromKind = EntityKinds.User,
                        FromId = SelectorGenerators.ActorUserId.ToString(),
                        ToKind = EntityKinds.User,
                        ToId = target,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedBy = SelectorGenerators.ActorUserId,
                    });
                }
            }

            await seed.SaveChangesAsync();
        }

        var selectors = SelectorGenerators.SharedSelector().Sample(200, 40).ToList();

        var compiler = new WorkflowTaskCacheSelectorCompiler();
        var evaluator = new InMemorySelectorEvaluator(
            SelectorGenerators.ActorUserId,
            SelectorGenerators.ActorOutboundEdges);

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
    /// The wildcard agrees on both evaluation paths (#574).
    /// </summary>
    /// <remarks>
    /// <para>This was <c>The_wildcard_divergence_still_holds</c>, and it pinned
    /// the defect rather than the fix: <c>tag=*</c> compiled to <c>IS NULL</c>
    /// while <see cref="InMemorySelectorEvaluator"/> read it as
    /// <c>actual is not null</c> — exact complements, measured at 69 leaks and
    /// 539 lockouts (GHSA-vrw7-qxhw-m9q8).</para>
    ///
    /// <para><b>Inverted, not deleted.</b> The old test's own comment said what
    /// to do when the decision came: <i>"if it is fixed, remove the exclusion in
    /// SelectorGenerators.ValueFor so the agreement property covers it."</i>
    /// Deleting it would have removed the only direct assertion on wildcard
    /// semantics and left the change visible nowhere; inverting it keeps the
    /// same two rows, the same two paths, and flips what they must agree on.</para>
    ///
    /// <para><b>This is the widening, asserted.</b> The owner's decision
    /// (2026-09-18) was that <c>*</c> means "has any value", accepting that a
    /// stored <c>tag=*</c> grant starts matching rows it currently excludes and
    /// stops matching rows it currently returns. Both halves are asserted below,
    /// because a test that checked only the newly-matched row would pass against
    /// a compiler that matched everything.</para>
    /// </remarks>
    [Fact]
    public async Task The_wildcard_agrees_and_means_has_any_value()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var assigned = NewRow("task-assigned", assignee: "alice");
        var unassigned = NewRow("task-unassigned", assignee: null);
        // A third row with a DIFFERENT value. Without it the wildcard and the
        // literal `assignee=alice` would return the same single row, and the
        // "they compile to different predicates" claim below would be true by
        // coincidence rather than by meaning.
        var other = NewRow("task-other", assignee: "bob");

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(assigned, unassigned, other);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[assignee=*]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        // Ordered explicitly: Postgres makes no ordering promise without an
        // ORDER BY, and the two sides are compared to each other below.
        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId)
            .OrderBy(id => id)
            .ToListAsync();

        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);
        var fromMemory = new[] { assigned, unassigned, other }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Both rows that HAVE a value match, on both paths; the unset one does
        // not. Before #574 the SQL path returned exactly the complement of this.
        Assert.Equal(["task-assigned", "task-other"], fromSql);
        Assert.Equal(["task-assigned", "task-other"], fromMemory);

        // And they agree with each other, which is the property the whole
        // thread exists to restore. Asserting each against a literal would pass
        // if both changed together in some third, wrong direction.
        Assert.Equal(fromMemory, fromSql);

        // #574 AC: the wildcard and a literal compile to DIFFERENT predicates,
        // asserted as different row sets rather than by comparing expression
        // trees. `assignee=alice` selects one of the two rows the wildcard
        // selects — so the wildcard is not silently compiling to an equality
        // against the actor, or to "match everything".
        var literal = SelectorParser.Parse("/workflowtask[assignee=alice]");
        var literalPredicate = new WorkflowTaskCacheSelectorCompiler().Compile(literal, context);
        var fromLiteral = await db.WorkflowTaskCache.Where(literalPredicate)
            .Select(t => t.FlowableTaskId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(["task-assigned"], fromLiteral);
        Assert.NotEqual(fromSql, fromLiteral);
    }

    /// <summary>
    /// A wildcard DENY stops denying unset rows and starts denying set ones (#574).
    /// </summary>
    /// <remarks>
    /// <para>The owner's decision was framed on allows — "existing <c>tag=*</c>
    /// grants widen". Both compilers serve denies as well as allows
    /// (<c>Authorizer</c> splits them), so the same inversion runs the other way
    /// too: a <c>tag=*</c> deny that today withholds unset-tag rows will now
    /// withhold set-tag ones instead.</para>
    ///
    /// <para>That is the worse half to leave unasserted — an allow that widens
    /// is visible the first time someone sees a row they did not expect, while a
    /// deny that silently stops denying is visible to nobody. Asserted here as
    /// its own fact rather than folded into the agreement test above, because it
    /// is a different claim about the same change.</para>
    /// </remarks>
    [Fact]
    public async Task A_wildcard_deny_withholds_the_rows_that_have_a_value()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var assigned = NewRow("deny-assigned", assignee: "alice");
        var unassigned = NewRow("deny-unassigned", assignee: null);

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(assigned, unassigned);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[assignee=*]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var denied = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        // What a deny covers is what the predicate matches; the rows that
        // survive it are its complement.
        var withheld = await db.WorkflowTaskCache.Where(denied)
            .Select(t => t.FlowableTaskId).ToListAsync();
        var survives = await db.WorkflowTaskCache.Where(ExpressionUtilities.Not(denied))
            .Select(t => t.FlowableTaskId).ToListAsync();

        Assert.Equal(["deny-assigned"], withheld);
        Assert.Equal(["deny-unassigned"], survives);
    }

    /// <summary>An array tag's wildcard means the array is non-empty (#574).</summary>
    /// <remarks>
    /// <c>candidateuser=*</c> reaches the same resolver and used to throw
    /// <c>requires a non-null value</c> — so it failed to <b>compile</b>, and an
    /// uncompilable grant is skipped with a warning, which for a deny fails open
    /// (#577). Both array columns are <c>NOT NULL DEFAULT ARRAY[]::TEXT[]</c>,
    /// so a null array cannot occur and emptiness is the only decidable thing.
    /// </remarks>
    [Fact]
    public async Task An_array_tags_wildcard_matches_a_non_empty_array()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var withCandidates = NewRow("arr-has", assignee: null);
        withCandidates.CandidateUsers = ["alice"];
        var withoutCandidates = NewRow("arr-empty", assignee: null);
        withoutCandidates.CandidateUsers = [];

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(withCandidates, withoutCandidates);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[candidateuser=*]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).ToListAsync();

        // Non-empty matches; empty does not. Before #574 this selector threw at
        // compile time and the grant was skipped entirely.
        Assert.Equal(["arr-has"], fromSql);
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

    /// <summary>
    /// The execution compiler widens the same way (#574).
    /// </summary>
    /// <remarks>
    /// <para>The fix touched two compilers and only one of them had any property
    /// coverage, so this is the execution side's own assertion rather than an
    /// inference from the task side passing.</para>
    ///
    /// <para><c>startedby</c> is the tag that can demonstrate it:
    /// <c>process_definition_key</c>, <c>process_definition_id</c> and
    /// <c>status</c> are <c>NOT NULL</c>, so a wildcard over them cannot
    /// distinguish anything. <c>tenant</c> would have been the other candidate
    /// and was hardcoded null by the projection; #576 removed it from the
    /// advertised tag set entirely, so it is no longer a tag to test.</para>
    /// </remarks>
    [Fact]
    public async Task The_execution_compilers_wildcard_matches_rows_that_have_a_starter()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var started = NewExecutionRow("exec-started", startedBy: "alice");
        var unstarted = NewExecutionRow("exec-unstarted", startedBy: null);

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowExecutionCache.AddRange(started, unstarted);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowexecution[startedby=*]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowExecutionCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowExecutionCache.Where(predicate)
            .Select(e => e.FlowableInstanceId).ToListAsync();

        // Before #574 this returned exactly the other row.
        Assert.Equal(["exec-started"], fromSql);
    }


    /// <summary>
    /// A path-id selector filters to those ids in SQL, and excludes the rest (#575).
    /// </summary>
    /// <remarks>
    /// <para>Before this, nothing in either cache compiler read
    /// <c>ast.Path</c>. <c>/workflowtask/task-a[...]</c> matched exactly one row
    /// in memory and EVERY row in SQL — not a wider answer to the same
    /// question, an answer to a different one.</para>
    ///
    /// <para>Both directions are asserted. A compiler that ignores path ids
    /// passes any test that only checks the named row is present, because it
    /// returns the named row along with everything else.</para>
    /// </remarks>
    [Fact]
    public async Task A_path_id_selector_filters_to_those_ids_and_excludes_the_rest()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var named = NewRow("task-a", assignee: "alice");
        var alsoNamed = NewRow("task-b", assignee: "bob");
        var outside = NewRow("task-c", assignee: "alice");

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(named, alsoNamed, outside);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask/{task-a,task-b}");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = (await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).OrderBy(id => id).ToListAsync());

        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);
        var fromMemory = new[] { named, alsoNamed, outside }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["task-a", "task-b"], fromSql);
        Assert.Equal(fromMemory, fromSql);

        // The complement, stated as its own assertion so the failure names it:
        // the row outside the path is absent. Before #575 it was present.
        Assert.DoesNotContain("task-c", fromSql);
    }

    /// <summary>
    /// A path id naming nothing selects nothing, rather than everything (#575).
    /// </summary>
    /// <remarks>
    /// The degenerate case, and the one that shows the old behaviour at its
    /// worst: a selector scoped to an id that does not exist used to return the
    /// whole table.
    /// </remarks>
    [Fact]
    public async Task A_path_id_that_matches_no_row_selects_nothing()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var row = NewRow("task-real", assignee: "alice");

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.Add(row);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask/task-does-not-exist");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).ToListAsync();

        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);
        var fromMemory = new[] { row }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .ToList();

        Assert.Empty(fromSql);
        Assert.Empty(fromMemory);
    }

    /// <summary>
    /// The execution compiler honours path ids too, and excludes the rest (#575).
    /// </summary>
    /// <remarks>
    /// Its own fact rather than a theory row on the task one: the two compilers
    /// are separate files that each grew their own gap, and a shared test would
    /// let one of them be fixed while reporting both.
    /// </remarks>
    [Fact]
    public async Task The_execution_compiler_filters_to_the_path_ids()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var named = NewExecutionRow("exec-a", startedBy: "alice");
        var outside = NewExecutionRow("exec-b", startedBy: "alice");

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowExecutionCache.AddRange(named, outside);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowexecution/exec-a");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowExecutionCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowExecutionCache.Where(predicate)
            .Select(e => e.FlowableInstanceId).ToListAsync();

        Assert.Equal(["exec-a"], fromSql);
        Assert.DoesNotContain("exec-b", fromSql);
    }

    /// <summary>
    /// A nested multi-hop predicate resolves the same subject set in SQL as in
    /// memory, and refuses subjects outside it (#575).
    /// </summary>
    /// <remarks>
    /// <para><c>[assignee=user[supervisor=user]]</c> means "the assignee is
    /// someone the actor supervises". The compiler used to drop
    /// <c>tag.Nested</c> silently and emit <c>assignee = &lt;actor&gt;</c> — a
    /// different set, which leaks one way (rows assigned to the actor, who is
    /// not necessarily supervised by themselves) and locks out the other (rows
    /// assigned to a supervisee).</para>
    ///
    /// <para>The fixture makes both visible at once: one row assigned to a user
    /// the actor supervises, one to a user they do not, and one assigned to the
    /// actor themselves — the row the OLD predicate would have returned and the
    /// new one must not.</para>
    /// </remarks>
    [Fact]
    public async Task A_nested_predicate_resolves_the_supervised_subject_set()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var actorId = SelectorGenerators.ActorUserId;
        var supervised = NewRow("task-supervised", assignee: SelectorGenerators.SupervisedUser);
        var unsupervised = NewRow("task-unsupervised", assignee: "bob");
        var assignedToActor = NewRow("task-actor", assignee: actorId.ToString());

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.AddRange(supervised, unsupervised, assignedToActor);
            seed.EntityEdges.Add(new EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = "supervisor",
                FromKind = EntityKinds.User,
                FromId = actorId.ToString(),
                ToKind = EntityKinds.User,
                ToId = SelectorGenerators.SupervisedUser,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = actorId,
            });
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowtask[assignee=user[supervisor=user]]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, actorId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).OrderBy(id => id).ToListAsync();

        var evaluator = new InMemorySelectorEvaluator(actorId, SelectorGenerators.ActorOutboundEdges);
        var fromMemory = new[] { supervised, unsupervised, assignedToActor }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["task-supervised"], fromSql);
        Assert.Equal(fromMemory, fromSql);

        // The complements, named separately so a failure says which half broke.
        Assert.DoesNotContain("task-unsupervised", fromSql);

        // This is the one the dropped-nesting bug returned INSTEAD: the old
        // predicate was `assignee = <actor>`, so it matched exactly this row and
        // missed the supervised one.
        Assert.DoesNotContain("task-actor", fromSql);
    }

    /// <summary>
    /// A nested predicate the grammar allows but neither path resolves compiles
    /// to "matches nothing", not to the un-nested predicate (#575).
    /// </summary>
    /// <remarks>
    /// <para>Three hops is the case: <c>InMemorySelectorEvaluator</c> answers
    /// false ("recursion deeper than two hops not supported"), so the SQL path
    /// must answer false too. What it must NOT do is what it used to — ignore
    /// the nesting and compile the outer tag alone, which is a predicate that
    /// matches real rows for a selector nobody can satisfy.</para>
    ///
    /// <para>AlwaysFalse rather than a thrown
    /// <c>SelectorCompilationException</c>: a throw makes Authorizer skip the
    /// grant, and a skipped deny stops denying (#577). Agreeing with the
    /// evaluator is the safer and the specified behaviour.</para>
    /// </remarks>
    [Fact]
    public async Task A_nesting_neither_path_resolves_matches_nothing()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var actorId = SelectorGenerators.ActorUserId;
        var assignedToActor = NewRow("task-actor", assignee: actorId.ToString());

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowTaskCache.Add(assignedToActor);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse(
            "/workflowtask[assignee=user[supervisor=user[supervisor=user]]]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, actorId);
        var predicate = new WorkflowTaskCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowTaskCache.Where(predicate)
            .Select(t => t.FlowableTaskId).ToListAsync();

        var evaluator = new InMemorySelectorEvaluator(actorId, SelectorGenerators.ActorOutboundEdges);
        var fromMemory = new[] { assignedToActor }
            .Where(r => evaluator.Matches(selector, r.FlowableTaskId, SelectorGenerators.FactsFor(r)))
            .Select(r => r.FlowableTaskId)
            .ToList();

        // Both empty. The old code returned ["task-actor"] here, because it
        // dropped the nesting and compiled `assignee = <actor>`.
        Assert.Empty(fromSql);
        Assert.Empty(fromMemory);
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

    private static WorkflowExecutionCache NewExecutionRow(string id, string? startedBy) => new()
    {
        FlowableInstanceId = id,
        ProcessDefinitionKey = "onboarding",
        ProcessDefinitionId = "onboarding:1:1",
        Status = "active",
        StartedBy = startedBy,
        StartTime = DateTime.UtcNow,
        LastSyncAtUtc = DateTime.UtcNow
    };
}
