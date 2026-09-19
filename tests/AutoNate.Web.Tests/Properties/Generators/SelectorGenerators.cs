using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence.Scaffolded;
using FsCheck;
using FsCheck.Fluent;

namespace AutoNate.Web.Tests.Properties.Generators;

/// <summary>
/// Grammar-driven generation for authorization selectors, plus the candidate
/// rows both evaluators are run against.
/// </summary>
/// <remarks>
/// Scoped to the WorkflowTask tag vocabulary, because that is where the
/// grammar genuinely has two implementations — see SelectorGrammarProperties
/// for why the story's nominated pair (records) has only one.
///
/// The value pools are deliberately tiny. The agreement property is looking
/// for a semantic disagreement between two evaluators, and that needs generated
/// selectors and generated rows to actually *overlap*: with wide pools almost
/// every selector matches nothing on both sides and the property passes
/// vacuously while looking busy. A handful of values makes real matches common.
/// </remarks>
internal static class SelectorGenerators
{
    private static readonly string[] ProcessKeys = ["onboarding", "invoice"];
    private static readonly string[] DefinitionKeys = ["approve", "review"];
    private static readonly string[] Users = ["alice", "bob"];
    private static readonly string[] Groups = ["finance", "ops"];

    /// <summary>The actor every generated selector and evaluator shares.</summary>
    public static readonly Guid ActorUserId = new("11111111-2222-3333-4444-555555555555");

    // Tags both paths implement -- which, as of #581, is all of them.
    //
    // `candidateuser` and `candidategroup` used to be excluded here as a known
    // divergence. They were not a divergence so much as a pair of tags backed by
    // nothing: the task projection writes empty arrays unconditionally, so they
    // matched nothing in SQL and denied everything in memory. They are no longer
    // advertised or compiled, so there is nothing left to exclude.
    private static readonly string[] SharedTags = ["processkey", "definitionkey", "assignee"];

    private static Gen<ValueNode> ValueFor(string tag) => tag switch
    {
        "processkey" => Gen.Elements(ProcessKeys).Select(v => (ValueNode)new LiteralValue { Text = v }),
        "definitionkey" => Gen.Elements(DefinitionKeys).Select(v => (ValueNode)new LiteralValue { Text = v }),
        _ => Gen.Frequency(
            (4, Gen.Elements(Users).Select(v => (ValueNode)new LiteralValue { Text = v })),
            // The actor-relative form, which resolves to the actor's id on both
            // paths and is the construct most likely to diverge.
            (2, Gen.Constant((ValueNode)new CurrentUserValue())),
            (1, Gen.Constant((ValueNode)new CurrentUserValue { PinnedId = ActorUserId.ToString() })),
            // WildcardValue is BACK IN, as of #574. It was excluded while the
            // two paths read it as exact complements — `IS NOT NULL` in memory,
            // `IS NULL` in SQL (GHSA-vrw7-qxhw-m9q8) — because leaving it in
            // buried every future divergence under the known one: the first run
            // reported 69 leaks and 539 lockouts, all of them this.
            //
            // The exclusion was always meant to be temporary, and the test that
            // pinned the defect said so in as many words: "if it is fixed,
            // remove the exclusion in SelectorGenerators.ValueFor so the
            // agreement property covers it." That is what this is.
            //
            // Weighted at 1 rather than 4: it is one construct among several and
            // over-sampling it would crowd out the literal and actor-relative
            // forms the property is also there to exercise.
            (1, Gen.Constant((ValueNode)new WildcardValue()))),
    };

    private static Gen<PredicateExpr> SharedTagExpr() =>
        Gen.Elements(SharedTags).SelectMany(tag =>
            ValueFor(tag).Select(v => (PredicateExpr)new TagExpr { Tag = tag, Value = v }));

    /// <summary>The one user the actor supervises in the shared fixture (#575).</summary>
    public const string SupervisedUser = "alice";

    /// <summary>
    /// The actor's outbound user→user edges, as the agreement fixture seeds them
    /// into <c>entity_edges</c> (#575).
    /// </summary>
    /// <remarks>
    /// Exposed rather than built inline so the in-memory evaluator's map and the
    /// rows the SQL subquery reads come from ONE declaration. Two hand-kept
    /// copies of a fixture is how an agreement property starts comparing two
    /// different worlds and calling the result agreement.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ActorOutboundEdges { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["supervisor"] = new HashSet<string>(StringComparer.Ordinal) { SupervisedUser },
        };

    // The multi-hop form, in scope for the shared property as of #575. The outer
    // value must be `=user` — both paths answer false to anything else — and the
    // inner tag is the edge kind the fixture seeds.
    private static Gen<PredicateExpr> SharedNestedExpr() =>
        Gen.Constant((PredicateExpr)new TagExpr
        {
            Tag = "assignee",
            Value = new CurrentUserValue(),
            Nested = new PredicateNode
            {
                Expressions = [new TagExpr { Tag = "supervisor", Value = new CurrentUserValue() }],
            },
        });

    private static Gen<PredicateExpr> SharedExpr() =>
        Gen.Frequency((6, SharedTagExpr()), (1, SharedNestedExpr()));

    // Path ids are in scope as of #575, when the SQL compilers started reading
    // ast.Path at all. `task-99` is deliberately absent from the 40-row fixture:
    // a selector naming only missing ids must return the empty set on BOTH
    // paths, which is the complement direction — a compiler that ignores path
    // ids returns everything there.
    private static Gen<PathNode> SharedPath() =>
        Gen.Frequency(
            (6, Gen.Constant(new PathNode { Kinds = ["workflowtask"] })),
            (1, Gen.Constant(new PathNode { Kinds = ["workflowtask"], Ids = ["*"] })),
            (3, Gen.NonEmptyListOf(Gen.Elements("task-0", "task-3", "task-17", "task-99"))
                .Select(ids => new PathNode
                {
                    Kinds = ["workflowtask"],
                    Ids = ids.Distinct().ToList(),
                })));

    /// <summary>A selector both the in-memory and the SQL path can evaluate.</summary>
    public static Gen<SelectorAst> SharedSelector() =>
        SharedPath().SelectMany(path =>
            Gen.Choose(1, 3).SelectMany(n =>
                Gen.ListOf(SharedExpr(), n).Select(exprs => new SelectorAst
                {
                    Path = path,
                    Predicate = new PredicateNode { Expressions = exprs.ToList() },
                })));

    public static Arbitrary<SelectorAst> SharedSelectorArb() =>
        Arb.From(SharedSelector(), ShrinkSelector);

    /// <summary>
    /// The full grammar, including constructs only one side supports.
    /// </summary>
    /// <remarks>
    /// Used by the totality and round-trip properties, which must cover
    /// everything the parser and printer can represent rather than only the
    /// agreeing subset.
    /// </remarks>
    public static Gen<SelectorAst> AnySelector()
    {
        var value = Gen.Frequency(
            (4, Gen.Elements("alice", "finance", "onboarding", "a b", "with\"quote", "[bracket]")
                .Select(v => (ValueNode)new LiteralValue { Text = v })),
            (2, Gen.Constant((ValueNode)new WildcardValue())),
            (2, Gen.Constant((ValueNode)new CurrentUserValue())),
            (1, Gen.Constant((ValueNode)new CurrentUserValue { PinnedId = ActorUserId.ToString() })),
            (1, Gen.Elements("role", "group").SelectMany(q =>
                Gen.Elements("supervisor", "manager")
                    .Select(n => (ValueNode)new QualifiedValue { Qualifier = q, Name = n }))));

        var tags = Gen.Elements(
            "processkey", "definitionkey", "assignee", "candidateuser", "candidategroup",
            "status", "creator", "recordtype");

        Gen<PredicateExpr> Expr(int depth)
        {
            var leaf = Gen.Frequency(
                (8, tags.SelectMany(t => value.Select(v =>
                    (PredicateExpr)new TagExpr { Tag = t, Value = v }))),
                // ScopeExpr: the `tag:qualifier` form. Both paths refuse it —
                // in-memory fails closed, SQL throws — but the parser and
                // printer must still handle it.
                (1, tags.SelectMany(t => Gen.Elements("owner", "member")
                    .Select(q => (PredicateExpr)new ScopeExpr { Tag = t, Qualifier = q }))));

            if (depth <= 0) return leaf;

            // The multi-hop `assignee=user[...]` form — the only nesting the
            // grammar has.
            return Gen.Frequency(
                (6, leaf),
                (2, Gen.Elements("assignee", "creator").SelectMany(t =>
                    Gen.ListOf(Expr(depth - 1), 1).Select(inner =>
                        (PredicateExpr)new TagExpr
                        {
                            Tag = t,
                            Value = new CurrentUserValue(),
                            Nested = new PredicateNode { Expressions = inner.ToList() },
                        }))));
        }

        var path = Gen.Frequency(
            (4, Gen.Elements("workflowtask", "record", "workflowexecution")
                .Select(k => new PathNode { Kinds = [k] })),
            (2, Gen.Constant(new PathNode { Kinds = ["*"] })),
            (2, Gen.Elements("workflowtask", "record")
                .Select(k => new PathNode { Kinds = [k], Ids = ["*"] })),
            (2, Gen.Elements("workflowtask", "record").SelectMany(k =>
                Gen.NonEmptyListOf(Gen.Elements("a1", "b2", "c3"))
                    .Select(ids => new PathNode { Kinds = [k], Ids = ids.Distinct().ToList() }))),
            (1, Gen.Constant(new PathNode { Kinds = ["record", "workflowtask"] })));

        return path.SelectMany(p => Gen.Frequency(
            (1, Gen.Constant<PredicateNode?>(null)),
            (5, Gen.Choose(1, 3).SelectMany(n =>
                Gen.ListOf(Expr(1), n)
                    .Select(es => (PredicateNode?)new PredicateNode { Expressions = es.ToList() }))))
            .Select(pred => new SelectorAst { Path = p, Predicate = pred }));
    }

    public static Arbitrary<SelectorAst> AnySelectorArb() =>
        Arb.From(AnySelector(), ShrinkSelector);

    // Same shape as AqlGenerators.QueryArb's shrinker (#69): drop whole parts
    // first, because that removes the most noise per step, then simplify what
    // is left. FsCheck takes the first candidate that still falsifies and
    // recurses, so the cheap large reductions belong at the front.
    /// <summary>The shrinker, exposed so the agreement Fact can shrink by hand.</summary>
    public static IEnumerable<SelectorAst> ShrinkForTests(SelectorAst ast) => ShrinkSelector(ast);

    private static IEnumerable<SelectorAst> ShrinkSelector(SelectorAst ast)
    {
        if (ast.Predicate is not null) yield return ast with { Predicate = null };

        if (ast.Path.Ids is not null)
            yield return ast with { Path = ast.Path with { Ids = null } };

        if (ast.Path.Kinds.Count > 1)
            yield return ast with { Path = ast.Path with { Kinds = [ast.Path.Kinds[0]] } };

        if (ast.Predicate is { Expressions.Count: > 1 } pred)
        {
            // Each expression alone, so a failure caused by one of three
            // conjuncts reduces to exactly that conjunct.
            foreach (var expr in pred.Expressions)
            {
                yield return ast with { Predicate = new PredicateNode { Expressions = [expr] } };
            }
        }

        if (ast.Predicate is { } p2)
        {
            for (var i = 0; i < p2.Expressions.Count; i++)
            {
                if (p2.Expressions[i] is TagExpr { Nested: not null } nested)
                {
                    var flattened = p2.Expressions.ToList();
                    flattened[i] = nested with { Nested = null };
                    yield return ast with { Predicate = new PredicateNode { Expressions = flattened } };
                }
            }
        }
    }

    /// <summary>Candidate task rows, generated so real matches are common.</summary>
    public static Gen<WorkflowTaskCache[]> TaskRows(int count) =>
        Gen.ListOf(TaskRow(), count).Select(rows =>
        {
            // Ids must be unique — they are the primary key, and the property
            // compares sets of them.
            var list = rows.ToList();
            for (var i = 0; i < list.Count; i++) list[i].FlowableTaskId = $"task-{i}";
            return list.ToArray();
        });

    private static Gen<WorkflowTaskCache> TaskRow() =>
        Gen.Elements(ProcessKeys).SelectMany(pk =>
        Gen.Elements(DefinitionKeys).SelectMany(dk =>
        // Assignee is sometimes the actor's own id, so CurrentUserValue
        // selectors match something rather than always failing on both sides.
        Gen.Frequency(
                (3, Gen.Elements(Users).Select(u => (string?)u)),
                (2, Gen.Constant((string?)ActorUserId.ToString())),
                (1, Gen.Constant((string?)null)))
            .SelectMany(assignee =>
        Gen.SubListOf(Users).SelectMany(cu =>
        Gen.SubListOf(Groups).Select(cg => new WorkflowTaskCache
        {
            FlowableTaskId = Guid.NewGuid().ToString(),
            FlowableInstanceId = "inst-1",
            ProcessDefinitionKey = pk,
            TaskDefinitionKey = dk,
            Name = "generated",
            Assignee = assignee,
            Owner = null,
            CandidateUsers = cu.ToArray(),
            CandidateGroups = cg.ToArray(),
            CreatedTime = DateTime.UtcNow,
            // Both NOT NULL in workflow_task_cache. Neither participates in any
            // selector tag, so they are fixed rather than generated.
            Status = "active",
            LastSyncAtUtc = DateTime.UtcNow,
        })))));

    /// <summary>
    /// The facts dictionary for a row, mirroring
    /// <c>FlowableInstanceAuthorizers.BuildFacts</c> exactly.
    /// </summary>
    /// <remarks>
    /// Three keys, matching production. Notably absent: candidateuser and
    /// candidategroup, which production cannot supply because Flowable's task
    /// summary endpoint returns no identity links. Reproducing that absence
    /// faithfully is the point — a fixture that helpfully added them would
    /// hide the very divergence this suite is meant to detect.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> FactsFor(WorkflowTaskCache row) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["assignee"] = row.Assignee,
            ["processkey"] = row.ProcessDefinitionKey,
            ["definitionkey"] = row.TaskDefinitionKey,
        };
}
