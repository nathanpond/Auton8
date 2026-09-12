using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Every neighbouring signal-scope case, in one table (#273, #274).
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of how #270 failed verification. Its fix was correct
/// for the case it was written for and wrong for the two cases beside it: a
/// scoped catch sharing a name with an unscoped THROW had its scope silently
/// dropped (#273), and an event-subprocess start event was refused as though it
/// forced the signal global (#274).
/// </para>
/// <para>
/// The lesson was not "write a test for #273 and a test for #274" — that would
/// leave the next neighbour unexamined. It was that the code conflated two
/// distinct notions, so the cases have to be enumerated as a grid:
/// </para>
/// <list type="bullet">
/// <item>who declares a scope — nobody, one event, or two disagreeing;</item>
/// <item>what kind of event shares the name — throw, catch, boundary,
/// event-subprocess start, or process-level start.</item>
/// </list>
/// <para>
/// The rule that falls out: <b>only a process-level start event forces a signal
/// global</b>, because only it is triggered from outside any instance. Everything
/// else may share an instance-scoped signal. Declaring nothing is not a conflict;
/// declaring the opposite is.
/// </para>
/// <para>
/// <c>SignalScopeExecutionTests.Every_accepted_signal_scope_case_deploys</c> feeds
/// each accepted row here to a real engine, because "the expansion is correct" and
/// "the engine takes it" are different claims and #270 proved the gap between them.
/// </para>
/// </remarks>
public sealed class SignalScopeCasesTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";

    /// <summary>An event referencing a signal, optionally declaring a scope.</summary>
    /// <summary>
    /// An event referencing a signal, optionally declaring a scope (#351).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="siblingFirst"/> is the seventh axis. Every diagram this
    /// repository has ever tested made <c>autonateSignalScope</c> the <b>sole
    /// child</b> of <c>extensionElements</c> — and the studio never writes it that
    /// way. <c>workflow.js:1193-1206</c> does
    /// <c>values: [...keptExtensions, scopeElement]</c>: it <em>appends</em>, so
    /// any event carrying an execution listener, or imported from another
    /// modeller, puts the scope second.
    /// </para>
    /// <para>
    /// Pointing <c>RawSignalScope</c> at a bare <c>.FirstOrDefault()</c> left
    /// 623/623 green while restoring #278 exactly: the same event, the same
    /// <c>value="instance"</c>, silently loses its scope and runs engine-wide.
    /// </para>
    /// <para>
    /// The generator had been built from what previous bugs looked like rather
    /// than from what the producer emits. This row exists because the producer
    /// emits it.
    /// </para>
    /// </remarks>
    private static string Event(
        string localName, string id, string? scope, string extra = "",
        string signalRef = "Sig_1", bool siblingFirst = false)
    {
        // Plain concatenation rather than a nested raw literal: the indentation
        // rules for those make an interpolated multi-line fragment a trap.
        var sibling = siblingFirst
            ? "<flowable:executionListener event=\"start\" "
              + "delegateExpression=\"${autonateBehaviorDelegate}\" />"
            : "";

        var ext = scope is null
            ? ""
            : "<bpmn:extensionElements>" + sibling
              + $"<flowable:autonateSignalScope value=\"{scope}\" />"
              + "</bpmn:extensionElements>";

        return $"""
              <bpmn:{localName} id="{id}" name="{id}"{extra}>
                {ext}
                <bpmn:signalEventDefinition signalRef="{signalRef}" />
              </bpmn:{localName}>
            """;
    }

    /// <summary>The ordinary single root, carrying no scope of its own.</summary>
    private const string PlainRoot = """<bpmn:signal id="Sig_1" name="the.signal" />""";

    /// <summary>
    /// Two INDEPENDENT signals, so <c>signalRef</c> resolution is exercised (#341).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator built exactly one root and pointed every event at it, and
    /// the property asserted <c>Descendants(signal).Count() == 1</c> — so a
    /// two-signal diagram was structurally unreachable. Production keys scope by
    /// signal <b>name</b> (<c>SignalScopeUse</c>), and that keying was
    /// unexercised: replacing the <c>signalRef</c> lookup with
    /// <c>.FirstOrDefault()</c> left <c>FullyQualifiedName~Workflow</c> green at
    /// 585/585, while a diagram with two independent signals was <em>refused</em>
    /// with a message naming the wrong one.
    /// </para>
    /// <para>
    /// That was the <b>fifth</b> consecutive frozen axis in this one generator —
    /// vocabulary (#278), arity (#290), kind (#306), container (#314), identity.
    /// The pattern is that it varies whatever the last bug was about. Two signals
    /// is not a complete answer to that, but it removes the specific freeze and
    /// the floors below make a silent re-freeze fail loudly.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A root declaring <c>global</c> — the other half of the carried-scope axis (#345).
    /// </summary>
    /// <remarks>
    /// <c>PreScopedRoot</c> only ever carried <c>processInstance</c>, so the root's
    /// own <c>global</c> declaration was never generated and dropping it from
    /// <c>CollectSignalScopeUses</c> left 609/609 green — while silently NARROWING
    /// a signal the diagram explicitly declared global.
    /// Measured: a global root with nobody disagreeing is emitted with its scope
    /// attribute <b>removed</b> (global is the absence of a scope); a global root
    /// contradicted by an instance declaration is refused and left as authored.
    /// </remarks>
    private const string GlobalRoot =
        """<bpmn:signal id="Sig_1" name="the.signal" flowable:scope="global" />""";

    /// <summary>
    /// Two signals where the FIRST carries a scope (#345).
    /// </summary>
    /// <remarks>
    /// The (carried scope × two signals) cell was empty everywhere, so reading the
    /// carried scope off <c>Descendants(signal).First()</c> instead of the signal's
    /// own root left 609/609 green — and made Sig_2 inherit Sig_1's scope.
    /// Measured: Sig_1 keeps <c>processInstance</c>, Sig_2 gets nothing.
    /// </remarks>
    private const string TwoRootsFirstScoped = """
          <bpmn:signal id="Sig_1" name="the.signal" flowable:scope="processInstance" />
          <bpmn:signal id="Sig_2" name="other.signal" />
        """;

    /// <summary>
    /// Two signals where the SECOND carries the scope (#351).
    /// </summary>
    /// <remarks>
    /// <c>TwoRootsFirstScoped</c> covered only root #1, and the root loop was
    /// therefore never asked to look past the first entry: <c>uses.Take(1)</c>
    /// left 623/623 green. Measured — Sig_2 keeps <c>processInstance</c>, Sig_1
    /// gets nothing.
    /// </remarks>
    private const string TwoRootsSecondScoped = """
          <bpmn:signal id="Sig_1" name="the.signal" />
          <bpmn:signal id="Sig_2" name="other.signal" flowable:scope="processInstance" />
        """;

    private const string TwoIndependentRoots = """
          <bpmn:signal id="Sig_1" name="the.signal" />
          <bpmn:signal id="Sig_2" name="other.signal" />
        """;

    internal static string Diagram(string body, string roots = PlainRoot) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
        {roots}
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Work" />
        {body}
          </bpmn:process>
        </bpmn:definitions>
        """;

    // ── The accepted cases, and what scope the shared root must end up with ──

    public static TheoryData<string, string, string?> Accepted() => new()
    {
        // one event declares instance, nobody disagrees
        {
            "a lone scoped catch",
            Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        // #273. An unscoped THROW is not a conflict — it raises the signal, and
        // the scope decides who hears it. This used to silently drop the scope.
        {
            "a scoped catch beside an unscoped throw",
            Event("intermediateThrowEvent", "th", null) + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        {
            "a scoped catch beside an unscoped boundary event",
            Event("boundaryEvent", "b", null, " attachedToRef=\"t\"")
                + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        {
            "a scoped catch beside a second catch that declares nothing",
            Event("intermediateCatchEvent", "c2", null) + Event("intermediateCatchEvent", "c", "instance"),
            "processInstance"
        },
        // Two events agreeing is not a conflict either.
        {
            "two catches both scoped to the instance",
            Event("intermediateCatchEvent", "c1", "instance") + Event("intermediateCatchEvent", "c2", "instance"),
            "processInstance"
        },
        // #274. An event-subprocess start is an IN-INSTANCE handler.
        {
            "a scoped throw beside an event-subprocess signal start",
            Event("intermediateThrowEvent", "th", "instance")
                + $"""
                      <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                    {Event("startEvent", "hs", null, " isInterrupting=\"false\"")}
                      </bpmn:subProcess>
                   """,
            "processInstance"
        },
        // Everything global stays global, and nothing is written.
        {
            "a catch explicitly declaring global",
            Event("intermediateCatchEvent", "c", "global"),
            null
        },
        {
            "a process-level signal start with an unscoped catch",
            Event("startEvent", "ss", null) + Event("intermediateCatchEvent", "c", null),
            null
        },
        // #278. The vocabulary axis this grid had frozen at one value. Flowable's
        // own spelling published clean and had its scope silently dropped, so the
        // signal ran engine-wide — the exact leak the scope exists to prevent,
        // invisible to a grid that only ever spelt it "instance".
        {
            "a catch scoped with Flowable's own spelling",
            Event("intermediateCatchEvent", "c", "processInstance"),
            "processInstance"
        },
        {
            "a catch scoped in capitals",
            Event("intermediateCatchEvent", "c", "PROCESSINSTANCE"),
            "processInstance"
        },
    };

    /// <summary>
    /// This grid and the deployment grid stay the same size (#280).
    /// </summary>
    /// <remarks>
    /// The other half is <c>SignalScopeExecutionTests.AcceptedScopeCases</c>,
    /// which cannot share a fixture with this one — the E2E project cannot
    /// reference this assembly. #280 found it five rows short, so the engine-side
    /// half of the grid was silently checking a subset. Both sides assert the
    /// same literal; adding a row to one and not the other fails here.
    /// </remarks>
    [Fact]
    public void The_unit_grid_and_the_deployment_grid_are_the_same_size()
    {
        Assert.Equal(10, Accepted().Count());
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void An_accepted_case_publishes_and_carries_the_scope_the_author_asked_for(
        string because, string body, string? expectedScope)
    {
        var xml = Diagram(body);

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));

        // Exactly one root. Two sharing a name is what Flowable refuses (#270).
        var root = Assert.Single(expanded.Descendants(Bpmn + "signal"));

        // #273's defect was invisible here until this assertion existed: the
        // diagram published clean and the scope was simply absent.
        Assert.Equal(expectedScope, root.Attribute(Flowable + "scope")?.Value);
        Assert.Equal("the.signal", root.Attribute("name")?.Value);
        _ = because;
    }

    // ── The refused cases ────────────────────────────────────────────────────

    public static TheoryData<string, string> Refused() => new()
    {
        {
            "a scoped catch beside a process-level signal start",
            Event("startEvent", "ss", null) + Event("intermediateCatchEvent", "c", "instance")
        },
        {
            "two events declaring opposite scopes",
            Event("intermediateThrowEvent", "th", "global") + Event("intermediateCatchEvent", "c", "instance")
        },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_contradiction_is_refused_naming_the_signal(string because, string body)
    {
        var result = WorkflowBpmnXml.ValidateProcess(Diagram(body));

        var error = Assert.Single(result.Errors, e => e.Contains("the.signal", StringComparison.Ordinal));
        Assert.Contains("one scope per signal name", error, StringComparison.Ordinal);
        _ = because;
    }

    [Fact]
    public void A_refused_diagram_still_emits_only_one_signal_root()
    {
        // Belt and braces. Validation refuses these, but if a caller ever skipped
        // validation the expansion must not hand Flowable two roots of one name —
        // that is the 500 #270 was filed for.
        foreach (var row in Refused())
        {
            var body = (string)row[1]!;
            var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Diagram(body)));
            Assert.Single(expanded.Descendants(Bpmn + "signal"));
        }
    }

    // ── The vocabulary axis (#278) ───────────────────────────────────────────

    /// <summary>
    /// Every spelling of "scope this to the instance" that must reach the engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grid above froze this axis at one value. That is how #278 hid: the
    /// expansion tested <c>declared == "instance"</c> while the validator
    /// accepted "instance" OR "processInstance", so a diagram spelling it
    /// Flowable's way passed validation with <b>zero errors</b> and had its scope
    /// silently dropped — a signal the author narrowed, running engine-wide.
    /// </para>
    /// <para>
    /// "processInstance" is not hypothetical: it is Flowable's own spelling and
    /// what a diagram round-tripped through another modeller carries.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("instance")]           // the studio's spelling
    [InlineData("processInstance")]    // Flowable's own — the #278 leak
    [InlineData("PROCESSINSTANCE")]
    [InlineData("Instance")]
    [InlineData("  instance  ")]       // moddle round-trips whitespace
    public void Every_accepted_spelling_of_instance_reaches_the_engine(string spelling)
    {
        var xml = Diagram(Event("intermediateCatchEvent", "c", spelling));

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        var root = Assert.Single(
            XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml)).Descendants(Bpmn + "signal"));

        Assert.Equal("processInstance", root.Attribute(Flowable + "scope")?.Value);
    }

    [Theory]
    [InlineData("global")]
    [InlineData("GLOBAL")]
    public void Every_accepted_spelling_of_global_leaves_the_signal_unscoped(string spelling)
    {
        var xml = Diagram(Event("intermediateCatchEvent", "c", spelling));

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        var root = Assert.Single(
            XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml)).Descendants(Bpmn + "signal"));

        Assert.Null(root.Attribute(Flowable + "scope"));
    }

    /// <summary>A spelling nothing recognises is refused, not read as global.</summary>
    /// <remarks>
    /// Before #278 a typo fell through to "not instance" — so it published clean
    /// and ran engine-wide, which is precisely the leak the scope exists to
    /// prevent, arriving through the one input an author is most likely to get
    /// wrong. Silence is the worst of the three possible answers.
    /// </remarks>
    [Theory]
    [InlineData("instnace")]
    [InlineData("process-instance")]
    [InlineData("local")]
    [InlineData("true")]
    public void A_spelling_nothing_recognises_is_refused_and_quoted_back(string typo)
    {
        var xml = Diagram(Event("intermediateCatchEvent", "c", typo));

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains(typo, StringComparison.Ordinal));

        // Both alternatives offered — an author who typed "instnace" needs to be
        // told what the right word is, not merely that theirs is wrong.
        Assert.Contains("'instance'", error, StringComparison.Ordinal);
        Assert.Contains("'global'", error, StringComparison.Ordinal);

        // And nothing is emitted for a name the validator refused.
        var root = Assert.Single(
            XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml)).Descendants(Bpmn + "signal"));
        Assert.Null(root.Attribute(Flowable + "scope"));
    }

    // ── The authored-state axis (#278, #279) ─────────────────────────────────

    private const string PreScopedRoot =
        """<bpmn:signal id="Sig_1" name="the.signal" flowable:scope="processInstance" />""";

    /// <summary>
    /// A scope the diagram already carries survives a publish that says nothing.
    /// </summary>
    /// <remarks>
    /// Declaring nothing is not declaring "global". A diagram may already carry
    /// Flowable's own <c>flowable:scope</c> — written by hand, or by another
    /// modeller — and stripping it silently widens a signal its author
    /// deliberately narrowed. #278 measured the strip happening.
    /// </remarks>
    [Fact]
    public void A_scope_the_diagram_already_carries_survives_a_silent_publish()
    {
        var xml = Diagram(Event("intermediateCatchEvent", "c", null), PreScopedRoot);

        // #279: "declares nothing" is not a declaration, so an unscoped catch
        // beside a pre-scoped root is not a contradiction. It used to be refused.
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);

        var root = Assert.Single(
            XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml)).Descendants(Bpmn + "signal"));

        Assert.Equal("processInstance", root.Attribute(Flowable + "scope")?.Value);
    }

    [Fact]
    public void An_explicit_global_beside_a_pre_scoped_root_is_a_contradiction()
    {
        // The complement of the test above, and the reason the root's own scope is
        // recorded rather than ignored: saying "global" about a signal the diagram
        // has already narrowed is a real disagreement, and honouring either side
        // silently would be a guess.
        var xml = Diagram(Event("intermediateCatchEvent", "c", "global"), PreScopedRoot);

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("the.signal", StringComparison.Ordinal));
        Assert.Contains("one scope per signal name", error, StringComparison.Ordinal);
    }

    /// <summary>Two roots of one name are refused rather than 500ing at deploy.</summary>
    /// <remarks>
    /// Flowable identifies a signal by NAME: two <c>&lt;bpmn:signal&gt;</c> roots
    /// sharing one are refused at deployment with
    /// <c>flowable-signal-duplicate-name</c> — an HTTP 500 with no usable message.
    /// Nothing here caught it, so the studio reported a successful publish and the
    /// deployment failed behind it (#279).
    /// </remarks>
    [Fact]
    public void Two_signal_roots_sharing_a_name_are_refused_at_publish()
    {
        const string twoRoots = """
              <bpmn:signal id="Sig_1" name="the.signal" />
              <bpmn:signal id="Sig_2" name="the.signal" />
            """;

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(
                Diagram(Event("intermediateCatchEvent", "c", null), twoRoots)).Errors,
            e => e.Contains("the.signal", StringComparison.Ordinal));

        // Named so the author can find them: the count and both ids.
        Assert.Contains("Sig_1", error, StringComparison.Ordinal);
        Assert.Contains("Sig_2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_roots_of_different_names_are_fine()
    {
        // The complement. Distinct names deploy cleanly against 8.0.0, so refusing
        // them would be refusing a diagram that works — and a duplicate-name check
        // written as "more than one root" would do exactly that.
        const string twoRoots = """
              <bpmn:signal id="Sig_1" name="the.signal" />
              <bpmn:signal id="Sig_2" name="another.signal" />
            """;

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(
            Diagram(Event("intermediateCatchEvent", "c", "instance"), twoRoots)).Errors);
    }

    // ── The guard the previous four rounds were missing ──────────────────────

    /// <summary>
    /// The expansion and the validator never disagree about a declaration (#278).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the structural guard, and the reason the fix for #278 was a
    /// refactor rather than a sixth point fix. #156's signal scope failed
    /// verification in five consecutive rounds, and every failure was one family:
    /// <c>ApplySignalScopes</c> and <c>BuildSignalScopeErrors</c> read the
    /// author's declaration with their own private rules, and the rules drifted.
    /// Round 3 they disagreed about what "declares nothing" means; round 5, about
    /// how "instance" is spelt.
    /// </para>
    /// <para>
    /// Each round's fix repaired one disagreement without removing the ability to
    /// disagree, so the next spelling, the next authored state, the next event
    /// kind opened it again. Both callers now consume one interpretation
    /// function, and this test asserts the property that makes that worth
    /// something: <b>across the whole cross-product, a diagram that publishes
    /// clean gets the scope it asked for, and a diagram that is refused gets
    /// nothing written.</b>
    /// </para>
    /// <para>
    /// It is deliberately not a list of cases. A list is what froze the
    /// vocabulary axis and hid #278 — the cross-product is generated, so adding a
    /// spelling to the product adds rows here whether or not anyone remembers to.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_two_paths_never_disagree()
    {
        string?[] declarations =
            [null, "instance", "processInstance", "PROCESSINSTANCE", "global", "GLOBAL", "instnace", ""];
        (string LocalName, string Extra)[] kinds =
        [
            ("intermediateCatchEvent", ""),
            ("intermediateThrowEvent", ""),
            ("boundaryEvent", " attachedToRef=\"t\""),
            ("endEvent", ""),
            ("startEvent", "")                       // process-level: forces global
        ];
        (string Label, string Roots)[] roots = [("plain", PlainRoot), ("pre-scoped", PreScopedRoot)];

        var checkedCells = 0;

        foreach (var declared in declarations)
        foreach (var (localName, extra) in kinds)
        foreach (var (rootLabel, rootXml) in roots)
        {
            // A start event may not be the only node, and a boundary event needs
            // its task — both already in Diagram's body.
            var xml = Diagram(Event(localName, "x", declared, extra), rootXml);
            var because = $"{localName} declaring '{declared ?? "(nothing)"}' on a {rootLabel} root";

            var refused = WorkflowBpmnXml.ValidateProcess(xml).Errors
                .Any(e => e.Contains("the.signal", StringComparison.Ordinal));

            var expandedXml = WorkflowBpmnXml.ExpandForDeployment(xml);
            var emitted = XDocument.Parse(expandedXml)
                .Descendants(Bpmn + "signal")
                .Single()
                .Attribute(Flowable + "scope")?.Value;

            // What the diagram carried before publish. A refused diagram must be
            // left at exactly this — publishing must not half-apply a scope the
            // author is about to be told is contradictory.
            var carried = rootLabel == "pre-scoped" ? "processInstance" : null;

            if (refused)
            {
                // #290. `emitted == carried` was satisfiable by COINCIDENCE in
                // every refused cell this grid contains, so an expansion that
                // half-applied a scope on exactly the diagrams the validator
                // refuses stayed green:
                //
                //     var agreed = use.Agreed ?? (use.DeclaredBy.ContainsKey(Instance)
                //         ? Instance : Global);        // -> 30/30 passing
                //
                // Byte-identical is the honest claim. "Left alone" is what the
                // expansion must do with a diagram about to be refused; "ended up
                // equal" is what it happened to do.
                Assert.True(emitted == carried,
                    $"{because}: refused at publish, but the expansion still wrote " +
                    $"scope='{emitted ?? "(none)"}' where the diagram carried " +
                    $"'{carried ?? "(none)"}'.");

                // NOTE, because this is where #290 hid: the assertion above is
                // NOT discriminating on its own. `carried` has only two possible
                // values and so does `emitted`, so an expansion that writes the
                // wrong thing can still land on the right one by coincidence —
                // and in a SINGLE-EVENT grid it always does, because a plain-root
                // contradiction cannot exist when there is nothing for a lone
                // event to contradict.
                //
                // `The_two_paths_never_disagree_with_two_events_either` is the
                // grid that discriminates, and it is where the mutation proving
                // this property goes. Deleting it puts this file back to passing
                // against a half-applying expansion.
                //
                // A byte-identical assertion was tried here and is wrong:
                // ExpandForDeployment legitimately rewrites signal END events into
                // a service task, so "unchanged" is false for reasons that have
                // nothing to do with scope.
                checkedCells++;
                continue;
            }

            // Accepted. Then the emitted scope must be what the declaration
            // MEANS — computed here from the raw string, independently of the
            // production switch, so a switch that starts recognising "local" or
            // stops recognising "processInstance" fails this test.
            var meaning = (declared ?? "").Trim().ToLowerInvariant() switch
            {
                "instance" or "processinstance" => "processInstance",
                "global" => null,
                // Declared nothing, so whatever the diagram carried stands.
                "" => carried,
                _ => "UNREACHABLE — a spelling nothing recognises must be refused"
            };

            // A process-level start event forces global whatever it declares, and
            // a declaration disagreeing with that is refused above.
            if (localName == "startEvent") meaning = null;

            Assert.True(emitted == meaning,
                $"{because}: published clean, but the engine gets scope=" +
                $"'{emitted ?? "(none)"}' where the declaration means " +
                $"'{meaning ?? "(none)"}'.");
            checkedCells++;
        }

        // A cross-product that silently collapsed to nothing would pass every
        // assertion above. 8 declarations x 5 kinds x 2 roots.
        Assert.Equal(80, checkedCells);
    }


    /// <summary>
    /// The same property, with TWO events per diagram (#290).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="The_two_paths_never_disagree"/> is single-event, and that is the
    /// axis it froze. A plain-root contradiction — the one shape where a broken
    /// expansion would write <c>processInstance</c> against a carried
    /// <c>null</c> — <b>cannot exist</b> with one event, because with no carried
    /// scope there is nothing for a lone event to contradict. So every refused
    /// cell in that grid happened to satisfy <c>emitted == carried</c> whatever
    /// the expansion did, and a half-applying expansion stayed 30/30 green.
    /// </para>
    /// <para>
    /// Two events is where the disagreements actually live: #273 (a scoped catch
    /// beside an unscoped throw), #274 (an event-subprocess start), #279 (a
    /// pre-scoped root crossed with two events — never tested anywhere before
    /// this). 6 declarations x 6 declarations x 2 roots = 72 cells, and the
    /// second event is a boundary so that the pair is always legal BPMN.
    /// </para>
    /// <para>
    /// The lesson is not "add arity". It is that an enumerated grid freezes
    /// whatever its author did not think of, twice running now — vocabulary in
    /// #278, arity in #290 — so the assertions are written to be discriminating
    /// on their own rather than relying on the cells to be complete.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_two_paths_never_disagree_with_two_events_either()
    {
        string?[] declarations = [null, "instance", "processInstance", "global", "GLOBAL", "instnace"];
        (string Label, string Roots)[] roots = [("plain", PlainRoot), ("pre-scoped", PreScopedRoot)];

        var checkedCells = 0;

        foreach (var first in declarations)
        foreach (var second in declarations)
        foreach (var (rootLabel, rootXml) in roots)
        {
            var xml = Diagram(
                Event("intermediateCatchEvent", "c", first)
                    + Event("boundaryEvent", "b", second, " attachedToRef=\"t\""),
                rootXml);

            var because = $"catch '{first ?? "(nothing)"}' + boundary " +
                          $"'{second ?? "(nothing)"}' on a {rootLabel} root";

            var refused = WorkflowBpmnXml.ValidateProcess(xml).Errors
                .Any(e => e.Contains("the.signal", StringComparison.Ordinal));

            var expandedXml = WorkflowBpmnXml.ExpandForDeployment(xml);
            var expanded = XDocument.Parse(expandedXml);

            // Two roots of one name is the 500 (#270). Never, on any input.
            Assert.Single(expanded.Descendants(Bpmn + "signal"));

            var emitted = expanded.Descendants(Bpmn + "signal")
                .Single().Attribute(Flowable + "scope")?.Value;

            if (refused)
            {
                // Byte-identical, for the reason given on the single-event grid:
                // "ended up equal" is satisfiable by coincidence, "unchanged" is not.
                Assert.True(
                    XNode.DeepEquals(XDocument.Parse(xml), XDocument.Parse(expandedXml)),
                    $"{because}: refused at publish, and the expansion CHANGED the " +
                    "document rather than leaving it alone.");
                checkedCells++;
                continue;
            }

            // Accepted, so the two declarations agree or one of them is silent.
            // Whichever spoke decides; silence defers to the root.
            var carried = rootLabel == "pre-scoped" ? "processInstance" : null;

            static string? Meaning(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "instance" or "processinstance" => "processInstance",
                "global" => null,
                _ => "SILENT"
            };

            var firstMeaning = Meaning(first);
            var secondMeaning = Meaning(second);
            var spoke = firstMeaning != "SILENT" ? firstMeaning
                      : secondMeaning != "SILENT" ? secondMeaning
                      : carried;

            Assert.True(emitted == spoke,
                $"{because}: published clean, but the engine gets scope=" +
                $"'{emitted ?? "(none)"}' where the declarations mean " +
                $"'{spoke ?? "(none)"}'.");
            checkedCells++;
        }

        // 6 x 6 x 2. A cross-product that collapsed would pass every assertion.
        Assert.Equal(72, checkedCells);
    }


    /// <summary>A typo on the ROOT is refused, like a typo on an event (#291).</summary>
    /// <remarks>
    /// <para>
    /// #278 made <c>InterpretSignalScope</c> the one place a spelling is judged.
    /// <c>CollectSignalScopeUses</c> called it for the root's carried scope and
    /// then discarded the answer it did not have a branch for — the
    /// <c>Unrecognised</c> case fell out of an <c>if</c>. So the shared
    /// interpretation was consulted and ignored, which is the third time that
    /// exact shape has produced a defect in #156's history.
    /// </para>
    /// <para>
    /// The root is where publish itself writes the scope, so a
    /// published-then-reopened diagram carries it there and nowhere else. Flowable
    /// answers <c>HTTP 500 flowable-signal-invalid-scope</c>: "Only values 'global'
    /// and 'processInstance' are supported".
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("instnace")]
    [InlineData("process-instance")]
    [InlineData("local")]
    public void A_typo_on_the_signal_root_is_refused_and_quoted_back(string typo)
    {
        var roots = $"""<bpmn:signal id="Sig_1" name="the.signal" flowable:scope="{typo}" />""";
        var xml = Diagram(Event("intermediateCatchEvent", "c", null), roots);

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains(typo, StringComparison.Ordinal));

        // Named as the signal itself, not as one of the events referencing it —
        // that distinction is #279's, and an author told "'Await' asks for..."
        // would go and look at an event that says nothing.
        Assert.Contains("the.signal", error, StringComparison.Ordinal);
        Assert.Contains("'instance'", error, StringComparison.Ordinal);
        Assert.Contains("'global'", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("processInstance")]
    [InlineData("global")]
    public void A_spelling_the_root_carries_that_we_do_recognise_is_not_refused(string spelling)
    {
        // The complement. A refusal rule written as "the root carries a scope"
        // rather than "the root carries a scope we do not recognise" would refuse
        // every diagram publish has ever produced, since publish writes this
        // attribute itself.
        var roots = $"""<bpmn:signal id="Sig_1" name="the.signal" flowable:scope="{spelling}" />""";
        var xml = Diagram(Event("intermediateCatchEvent", "c", null), roots);

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);
    }


    // ── #306: the instrument, rather than another set of cells ──────────────

    /// <summary>
    /// The property, on diagrams nobody chose (#306).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three rounds running, this file was fixed by unfreezing the axis just
    /// found and freezing another:</b>
    /// </para>
    /// <list type="table">
    /// <item><term>#278</term><description>vocabulary — only ever spelt "instance"</description></item>
    /// <item><term>#290</term><description>arity — one event per cell, so a plain-root contradiction could not exist</description></item>
    /// <item><term>#306</term><description>kind — always (catch, boundary), so a start-event contradiction could not exist</description></item>
    /// </list>
    /// <para>
    /// Each version was complete with respect to the defect already known and
    /// blind to the next. That is what hand-enumerated grids do, and adding the
    /// missing cells a fourth time would buy one more round.
    /// </para>
    /// <para>
    /// The property under test needs no cases at all:
    /// </para>
    /// <para>
    /// <b>For any diagram: if <c>ValidateProcess</c> refuses it, the expansion
    /// writes no scope that was not already there; and if it accepts, the engine
    /// gets the scope the declarations mean.</b>
    /// </para>
    /// <para>
    /// So the diagrams are generated — event count, kinds, declarations and root
    /// state all drawn from their full ranges. A cell nobody thought of is still
    /// generated, which is the property the grids never had. The seed is fixed so
    /// a failure is reproducible, and printed so a failure can be replayed.
    /// </para>
    /// <para>
    /// The expected value is computed from an independent model below, NOT from
    /// the production enum — a test that asks the code what it should do proves
    /// only that it is consistent with itself.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(20260911)]
    [InlineData(1)]
    [InlineData(6306)]
    public void The_two_paths_never_disagree_on_generated_diagrams(int seed)
    {
        var random = new Random(seed);

        string?[] declarations =
            [null, "", "instance", "processInstance", "PROCESSINSTANCE", "Instance", "  instance  ",
             "global", "GLOBAL", "instnace", "process-instance", "local", "true"];

        (string LocalName, string Extra)[] kinds =
        [
            ("intermediateCatchEvent", ""),
            ("intermediateThrowEvent", ""),
            ("boundaryEvent", " attachedToRef=\"t\""),
            ("endEvent", ""),
            ("startEvent", ""),
        ];

        var refusedSeen = 0;
        var acceptedSeen = 0;
        var startsAtProcessLevel = 0;
        var startsInEventSubProcess = 0;
        var eventsOnSecondSignal = 0;
        var globalRootDiagrams = 0;
        var extensionSiblings = 0;
        var secondRootCarriesScope = 0;
        var nonCanonicalRootSpellings = 0;
        var twoRootsWithCarriedScope = 0;

        for (var iteration = 0; iteration < 400; iteration++)
        {
            // Five root shapes. #341 added the two-signal one; #345 added the
            // global-carrying root and the two-signal-with-a-carried-scope shape,
            // which were the two empty cells three surviving mutations lived in.
            var rootChoice = random.Next(6);
            var twoSignals = rootChoice is 3 or 4 or 5;

            // #351: the ROOT's spelling varies too. It was only ever the two
            // canonical words, so a hand-rolled second reader of it -- the exact
            // "two readers drift apart" shape #278's refactor exists to prevent --
            // left 623/623 green. Measured: every spelling below is accepted today
            // and normalises to processInstance, except GLOBAL which normalises to
            // no attribute at all.
            string[] rootSpellings =
                ["processInstance", "instance", "Instance", "PROCESSINSTANCE", "  processInstance  "];
            var spelling = rootSpellings[random.Next(rootSpellings.Length)];

            var rootXml = rootChoice switch
            {
                1 => PreScopedRoot.Replace("processInstance", spelling, StringComparison.Ordinal),
                2 => GlobalRoot,
                3 => TwoIndependentRoots,
                4 => TwoRootsFirstScoped.Replace("processInstance", spelling, StringComparison.Ordinal),
                5 => TwoRootsSecondScoped,
                _ => PlainRoot,
            };

            // What Sig_1's root carries, as AUTHORED -- a refused diagram is left
            // at exactly this, so the raw text matters, not its interpretation.
            var carried = rootChoice switch
            {
                1 or 4 => spelling,
                2 => "global",
                _ => null,
            };
            // And Sig_2's, which until #351 was always null.
            var carriedSecond = rootChoice == 5 ? "processInstance" : null;

            // One to four events. One is the shape #278 lived in; two is #290's;
            // three and four are shapes no grid has ever contained.
            var eventCount = random.Next(1, 5);
            var body = new System.Text.StringBuilder();
            var chosen = new List<(string Kind, string? Declared, bool InEventSubProcess, bool UsesSecondSignal)>();

            for (var e = 0; e < eventCount; e++)
            {
                var (localName, extra) = kinds[random.Next(kinds.Length)];
                var declared = declarations[random.Next(declarations.Length)];

                // #314: the CONTAINER axis. Every earlier version of this
                // generator put the whole body directly in <bpmn:process>, so
                // every start event was process-level and `ForcesGlobalSignal`'s
                // one discriminator -- is there an event-subprocess ancestor? --
                // was never varied. Reverting that method to "every startEvent is
                // global" passed this property 3/3.
                //
                // Only start events vary: a signal start event in a PLAIN embedded
                // subprocess is refused by a different rule (placement), which
                // would make refusals here mean two different things.
                var inEventSub = localName == "startEvent" && random.Next(2) == 0;

                // Which signal this event points at. Only meaningful when the
                // diagram declares two; otherwise everything uses Sig_1.
                var usesSecond = twoSignals && random.Next(2) == 0;

                // #351: does anything sit AHEAD of the scope declaration? The
                // studio always appends, so this is the shape the product
                // actually produces -- and it was never generated.
                var siblingFirst = declared is not null && random.Next(2) == 0;
                if (siblingFirst) extensionSiblings++;

                chosen.Add((localName, declared, inEventSub, usesSecond));

                var element = Event(localName, $"e{e}", declared, extra,
                    signalRef: usesSecond ? "Sig_2" : "Sig_1",
                    siblingFirst: siblingFirst);
                body.Append(inEventSub
                    ? $"""
                        <bpmn:subProcess id="es{e}" name="Handler {e}" triggeredByEvent="true">
                        {element}
                        </bpmn:subProcess>
                      """
                    : element);
            }

            if (rootChoice == 2) globalRootDiagrams++;
            if (rootChoice is 4 or 5) twoRootsWithCarriedScope++;
            if (rootChoice == 5) secondRootCarriesScope++;
            if (rootChoice is 1 or 4 && spelling != "processInstance") nonCanonicalRootSpellings++;
            eventsOnSecondSignal += chosen.Count(c => c.UsesSecondSignal);
            startsAtProcessLevel += chosen.Count(c => c.Kind == "startEvent" && !c.InEventSubProcess);
            startsInEventSubProcess += chosen.Count(c => c.Kind == "startEvent" && c.InEventSubProcess);

            var xml = Diagram(body.ToString(), rootXml);
            var because =
                $"seed {seed}, iteration {iteration}: root={rootChoice switch { 1 => $"pre-scoped('{spelling}')", 2 => "global-root", 3 => "two-signals", 4 => $"two-first-scoped('{spelling}')", 5 => "two-second-scoped", _ => "plain" }}, " +
                string.Join(" + ", chosen.Select(c =>
                    $"{c.Kind}('{c.Declared ?? "(null)"}')"
                    + (c.InEventSubProcess ? "@eventSubProcess" : "@process")
                    + (c.UsesSecondSignal ? "->Sig_2" : "->Sig_1")));

            // Either signal's name -- a conflict on Sig_2 is still a refusal, and
            // filtering on "the.signal" alone reported one as an acceptance (#341).
            var refused = WorkflowBpmnXml.ValidateProcess(xml).Errors
                .Any(er => er.Contains("the.signal", StringComparison.Ordinal)
                           || er.Contains("other.signal", StringComparison.Ordinal));

            var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml));

            // Two roots of one NAME is the 500 (#270). Two roots of different
            // names is legal and is now generated, so the invariant is stated as
            // what it always meant: no duplicate names.
            var emittedRoots = expanded.Descendants(Bpmn + "signal").ToList();
            var duplicateNames = emittedRoots
                .GroupBy(r => r.Attribute("name")?.Value ?? "", StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.True(duplicateNames.Count == 0,
                $"{because}: the expansion emitted two <bpmn:signal> roots sharing a name " +
                $"({string.Join(", ", duplicateNames)}), which Flowable refuses with " +
                "flowable-signal-duplicate-name.");
            Assert.True(emittedRoots.Count == (twoSignals ? 2 : 1),
                $"{because}: expected {(twoSignals ? 2 : 1)} signal root(s), got {emittedRoots.Count}.");

            // BOTH roots, not just Sig_1 (#345). Reading only the first meant the
            // product could write anything at all to the second -- or nothing --
            // and this property could not tell.
            string? ScopeOf(string id) => emittedRoots
                .FirstOrDefault(r => (r.Attribute("id")?.Value ?? "") == id)
                ?.Attribute(Flowable + "scope")?.Value;

            var emitted = ScopeOf("Sig_1");
            var emittedSecond = twoSignals ? ScopeOf("Sig_2") : null;

            var (shouldRefuse, expected, expectedSecond, firstSignalIsTheProblem) = ExpectedVerdict(chosen, carried, carriedSecond);

            // TWO-SIDED, and this is the half the first version of this test
            // omitted. Asserting only "what a refused diagram writes" lets a
            // product that refuses too much pass: with the #278 vocabulary gap
            // reintroduced, `processInstance` became Unrecognised, the diagram was
            // refused, and the refused branch was satisfied. The model has to be
            // held to the DECISION as well as to the value.
            Assert.True(refused == shouldRefuse,
                shouldRefuse
                    ? $"{because}: should have been REFUSED — the declarations either " +
                      "contradict each other or include a spelling nothing recognises — and " +
                      "publish accepted it."
                    : $"{because}: should have been ACCEPTED — the declarations agree and every " +
                      "spelling is one the product claims to know — and publish refused it. A " +
                      "refusal an author cannot act on is as bad as a missing one.");

            if (refused)
            {
                refusedSeen++;

                // A diagram refused BECAUSE OF Sig_1 gets nothing written for it.
                // This is where #290 lived: the old assertion compared two values
                // from a two-element set and was satisfiable by coincidence.
                //
                // A diagram refused because of Sig_2 is a different case, and one
                // the single-signal generator could not produce: Sig_1's scope was
                // still resolved correctly and writing it is not a defect. Holding
                // it to "left exactly as authored" asserted the wrong thing (#341).
                Assert.True(emitted == expected,
                    $"{because}: refused at publish, and the expansion wrote " +
                    $"scope='{emitted ?? "(none)"}' where " +
                    (firstSignalIsTheProblem
                        ? $"a diagram refused over Sig_1 must be left exactly as authored ('{carried ?? "(none)"}')."
                        : $"Sig_1's own declarations mean '{expected ?? "(none)"}' -- the refusal is about Sig_2."));
                continue;
            }

            acceptedSeen++;

            Assert.True(emitted == expected,
                $"{because}: published clean, but the engine gets scope='{emitted ?? "(none)"}' " +
                $"where the declarations mean '{expected ?? "(none)"}'.");

            // #345: and the second signal's own answer, which nothing checked.
            if (twoSignals)
            {
                Assert.True(emittedSecond == expectedSecond,
                    $"{because}: published clean, but Sig_2 gets scope='{emittedSecond ?? "(none)"}' " +
                    $"where ITS declarations mean '{expectedSecond ?? "(none)"}'. A scope the author " +
                    "narrowed to one instance, dropped, is broadcast engine-wide.");
            }
        }

        // A generator that produced only one side of the property would pass every
        // assertion above while testing half of it.
        Assert.True(refusedSeen > 20, $"seed {seed} generated only {refusedSeen} refused diagrams.");
        Assert.True(acceptedSeen > 20, $"seed {seed} generated only {acceptedSeen} accepted diagrams.");

        // #314: and the container axis was actually varied. Without this, a later
        // edit that stops emitting event subprocesses -- or a `kinds` table that
        // drops startEvent -- silently returns this property to the frozen state
        // it shipped in, and every assertion above still passes.
        Assert.True(startsAtProcessLevel > 20,
            $"seed {seed} generated only {startsAtProcessLevel} process-level start events.");
        Assert.True(startsInEventSubProcess > 20,
            $"seed {seed} generated only {startsInEventSubProcess} start events inside an event " +
            "subprocess — the container that distinguishes #274's fix from its bug.");

        // #341: and the IDENTITY axis. Without this, dropping the two-signal root
        // shape -- or never pointing an event at Sig_2 -- returns this property to
        // the state where `signalRef` resolution is unexercised and replacing it
        // with `.FirstOrDefault()` leaves 585/585 green.
        //
        // Five axes have now frozen in this one generator: vocabulary (#278),
        // arity (#290), kind (#306), container (#314), identity (#341). Each floor
        // below was added after the axis it names had already shipped frozen, so
        // the honest reading is that a sixth exists and this list is how it gets
        // found -- not proof that none does.
        Assert.True(eventsOnSecondSignal > 20,
            $"seed {seed} generated only {eventsOnSecondSignal} events pointing at a second, " +
            "independent signal — the axis that exercises signalRef resolution at all.");

        // #345. The two CARRIED-scope cells, each of which held a mutation that
        // survived at 609/609:
        //   a root declaring `global` (not just `processInstance`), and
        //   a carried scope in a diagram that has two roots.
        //
        // `twoSignalDiagrams` used to sit here and could never fire alone —
        // `usesSecond` requires `twoSignals`, so zeroing the shape zeroed
        // `eventsOnSecondSignal` too and that floor asserted first. A floor that
        // cannot fail is the thing this milestone keeps finding, so it is gone.
        Assert.True(globalRootDiagrams > 20,
            $"seed {seed} generated only {globalRootDiagrams} diagrams whose ROOT declares " +
            "global — without them, dropping the product's handling of a global root is invisible.");
        // #351: and the sibling-ordering axis. The declaration was an only child
        // in every diagram for seven rounds, while the studio has never written it
        // as one.
        Assert.True(extensionSiblings > 20,
            $"seed {seed} generated only {extensionSiblings} events with something ahead of the " +
            "scope declaration inside extensionElements — the shape workflow.js actually emits.");

        // #351: the two cells the root loop and the root's own spelling hid in.
        Assert.True(secondRootCarriesScope > 20,
            $"seed {seed} generated only {secondRootCarriesScope} diagrams where the SECOND root " +
            "carries the scope — the cell `uses.Take(1)` hid in.");
        Assert.True(nonCanonicalRootSpellings > 20,
            $"seed {seed} generated only {nonCanonicalRootSpellings} roots spelled anything other " +
            "than the canonical word — the cell a second, hand-rolled reader of the root hid in.");

        Assert.True(twoRootsWithCarriedScope > 20,
            $"seed {seed} generated only {twoRootsWithCarriedScope} diagrams with two roots AND a " +
            "carried scope — the cell where reading the scope off the wrong root hides.");
    }

    /// <summary>
    /// What a set of declarations MEANS, modelled independently of the product.
    /// </summary>
    /// <remarks>
    /// Deliberately not a call into <c>InterpretSignalScope</c>. A test that asks
    /// the code what it should do proves only self-consistency, which is exactly
    /// what three versions of the grid did when they reused the production
    /// vocabulary.
    /// </remarks>
    private static (bool ShouldRefuse, string? Scope, string? SecondScope, bool FirstSignalIsTheProblem) ExpectedVerdict(
        IReadOnlyList<(string Kind, string? Declared, bool InEventSubProcess, bool UsesSecondSignal)> events,
        string? carried, string? carriedSecond = null)
    {
        // #341. Scope is per-SIGNAL, so the two signals are modelled separately:
        // `wants` is what Sig_1's users ask for, `otherWants` what Sig_2's do. A
        // contradiction inside EITHER refuses the diagram, but a scope declared on
        // Sig_2 must never appear on Sig_1 -- that leak is the defect this axis
        // exists to catch, and the old model could not express it because every
        // event pointed at one root.
        var wants = new HashSet<string>(StringComparer.Ordinal);
        var otherWants = new HashSet<string>(StringComparer.Ordinal);
        var firstUnrecognised = false;
        var secondUnrecognised = false;

        // The root's own carried scope is a declaration by the SIGNAL, counted
        // once. Three things this line has had to learn, one per round:
        //   #345: it may be global, not only processInstance;
        //   #351: it is SPELLED as freely as an event's declaration is, so it
        //         goes through the same interpretation rather than being taken
        //         literally -- taking it literally is the "two readers drift
        //         apart" bug #278's refactor exists to prevent;
        //   #351: Sig_2 can carry one too.
        static string? Interpret(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "" => null,
            "instance" or "processinstance" => "processInstance",
            "global" => "global",
            _ => "?",                                    // nothing recognises it
        };

        var carriedFirst = Interpret(carried);
        if (carriedFirst == "?") return (true, carried, Interpret(carriedSecond), true);
        if (carriedFirst is not null) wants.Add(carriedFirst);

        var otherCarried = Interpret(carriedSecond);
        if (otherCarried == "?") return (true, carriedFirst, carriedSecond, false);
        if (otherCarried is not null) otherWants.Add(otherCarried);

        foreach (var (kind, declared, inEventSubProcess, usesSecondSignal) in events)
        {
            var bucket = usesSecondSignal ? otherWants : wants;
            // A PROCESS-LEVEL start event is global by nature whatever it says: it
            // exists to be triggered from outside any instance. A start event
            // inside an event subprocess is not one of these -- it is an
            // in-instance handler, and it may share an instance-scoped signal
            // quite happily (#274, measured: that shape deploys and runs).
            //
            // This distinction is the whole of `ForcesGlobalSignal`, and until
            // #314 this generator could not express it.
            if (kind == "startEvent" && !inEventSubProcess)
            {
                bucket.Add("global");
                continue;
            }

            switch ((declared ?? "").Trim().ToLowerInvariant())
            {
                case "": break;                                  // said nothing
                case "instance" or "processinstance": bucket.Add("processInstance"); break;
                case "global": bucket.Add("global"); break;

                // A spelling nothing recognises refuses the document -- but it is
                // a fact about the signal it was written on. An unrecognised word
                // on Sig_2 does not make Sig_1's own declarations unreadable, and
                // production resolves Sig_1 regardless (#341).
                default:
                    if (usesSecondSignal) secondUnrecognised = true;
                    else firstUnrecognised = true;
                    break;
            }
        }

        // A contradiction about EITHER signal refuses the whole diagram -- but
        // WHICH one decides whether Sig_1's own scope was still resolved. A
        // refusal caused by Sig_2 does not make Sig_1's answer wrong (#341).
        var firstConflicted = wants.Count > 1 || firstUnrecognised;
        var secondConflicted = otherWants.Count > 1 || secondUnrecognised;

        // Computed only once Sig_1 is known not to contradict itself -- `Single()`
        // throws on a two-element set, and evaluating it eagerly meant a diagram
        // whose Sig_1 conflicts crashed the oracle instead of being judged by it.
        // #345. Sig_2's own answer, modelled the same way. The generator has made
        // two signals since #341; the oracle only ever read Sig_1, so everything
        // the product computes for the second was unchecked in BOTH directions --
        // what gets written to it, and what gets read from it. Three mutations
        // survived on that, one of which broadcast an instance-scoped signal
        // engine-wide.
        //
        // Sig_2 never carries a root scope: PreScopedRoot is a single-root shape.
        // Global is the ABSENCE of a scope attribute, not the string "global" --
        // measured: a global root with nobody disagreeing is emitted with the
        // attribute removed. The carried value is already folded into `w`, so
        // there is no second source to reconcile.
        static string? Resolve(HashSet<string> w) =>
            w.Count == 0 || w.Single() == "global" ? null : "processInstance";

        if (firstConflicted) return (true, carried, secondConflicted ? carriedSecond : Resolve(otherWants), true);

        var firstScope = Resolve(wants);

        if (secondConflicted) return (true, firstScope, carriedSecond, false);

        // The answer is Sig_1's alone. If this returned otherWants' value for a
        // Sig_1-quiet diagram, that would be the leak this axis exists to catch.
        return (false, firstScope, Resolve(otherWants), false);
    }

}
