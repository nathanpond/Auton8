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

    /// <summary>An event referencing signal `Sig_1`, optionally declaring a scope.</summary>
    private static string Event(string localName, string id, string? scope, string extra = "")
    {
        var ext = scope is null
            ? ""
            : $"""
                 <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="{scope}" />
                  </bpmn:extensionElements>
               """;
        return $"""
              <bpmn:{localName} id="{id}" name="{id}"{extra}>
                {ext}
                <bpmn:signalEventDefinition signalRef="Sig_1" />
              </bpmn:{localName}>
            """;
    }

    /// <summary>The ordinary single root, carrying no scope of its own.</summary>
    private const string PlainRoot = """<bpmn:signal id="Sig_1" name="the.signal" />""";

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

            var emitted = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(xml))
                .Descendants(Bpmn + "signal")
                .Single()
                .Attribute(Flowable + "scope")?.Value;

            // What the diagram carried before publish. A refused diagram must be
            // left at exactly this — publishing must not half-apply a scope the
            // author is about to be told is contradictory.
            var carried = rootLabel == "pre-scoped" ? "processInstance" : null;

            if (refused)
            {
                Assert.True(emitted == carried,
                    $"{because}: refused at publish, but the expansion still wrote " +
                    $"scope='{emitted ?? "(none)"}' where the diagram carried " +
                    $"'{carried ?? "(none)"}'.");
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

}
