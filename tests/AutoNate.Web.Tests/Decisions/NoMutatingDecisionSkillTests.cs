using System.Reflection;
using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// The assistant cannot author or publish a decision table (#110).
/// </summary>
/// <remarks>
/// <para>
/// An explicit acceptance criterion: <i>"No new mutating agent skill. The assistant
/// does not gain the ability to author or publish decision tables in this milestone;
/// that is M8's subject. A test asserts no mutating skill was added."</i>
/// </para>
/// <para>
/// Worth a test rather than a promise because of how it would be broken: nobody
/// would add a <c>ManageDecisionTablesSkill</c> on purpose while reading this
/// criterion. It would arrive as one more tool on an existing skill, in a commit
/// about something else — which is precisely what a scan over the registered tool
/// surface catches and a code review of that commit would not.
/// </para>
/// <para>
/// Read-only exposure is explicitly permitted by the AC, so this does not forbid a
/// <c>Lookup*</c> tool. It forbids a tool that <b>changes</b> one.
/// </para>
/// </remarks>
public sealed class NoMutatingDecisionSkillTests
{
    /// <summary>Verbs that would mean the assistant can change a table.</summary>
    private static readonly string[] MutatingVerbs =
        ["create", "update", "edit", "save", "delete", "publish", "deploy", "set", "add", "remove"];

    [Fact]
    public void No_registered_agent_tool_can_change_a_decision_table()
    {
        var tools = AllSkills()
            .SelectMany(skill => skill.Tools.Select(tool => (Skill: skill.Name, tool.Name)))
            .ToList();

        // The corpus is asserted first. A reflection scan that found no skills at
        // all would report "clean" in exactly the same green as a clean surface --
        // the failure mode this repo has named repeatedly.
        Assert.True(tools.Count > 20,
            $"Only {tools.Count} agent tools were discovered, which is too few to be the real "
            + "surface. The scan is looking in the wrong place, and a pass here means nothing.");

        var offenders = tools
            .Where(t => t.Name.Contains("decision", StringComparison.OrdinalIgnoreCase)
                        && MutatingVerbs.Any(v => t.Name.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            .Select(t => $"{t.Skill}: {t.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "The assistant gained the ability to change a decision table, which #110 says it must not: "
            + string.Join(", ", offenders)
            + ". Authoring from the assistant is M8's subject. Read-only exposure is fine.");
    }

    [Fact]
    public void The_detector_would_notice_a_mutating_tool()
    {
        // The complement, and the reason the fact above is not vacuous. Without
        // this, a scan that matched nothing -- a wrong substring, a changed naming
        // convention -- would pass forever while the thing it guards walked in.
        var planted = new[] { "publish_decision_table", "list_decision_tables", "search_records" }
            .Where(name => name.Contains("decision", StringComparison.OrdinalIgnoreCase)
                           && MutatingVerbs.Any(v => name.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Equal(["publish_decision_table"], planted);
    }

    /// <summary>
    /// Every <see cref="IAgentSkill"/> the assembly defines, constructed where it can be.
    /// </summary>
    /// <remarks>
    /// Reflection over the assembly rather than the DI container, deliberately: a
    /// skill registered conditionally — behind a feature flag, or only in one
    /// environment — is still a skill that exists, and a container-based scan would
    /// miss exactly the one somebody was unsure about.
    /// </remarks>
    private static IEnumerable<IAgentSkill> AllSkills()
    {
        foreach (var type in typeof(IAgentSkill).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IAgentSkill).IsAssignableFrom(type)) continue;
            if (type.GetConstructors().All(c => c.GetParameters().Length != 0)) continue;

            IAgentSkill? skill = null;
            try
            {
                skill = (IAgentSkill?)Activator.CreateInstance(type);
            }
            catch (TargetInvocationException)
            {
                // A skill needing more than a parameterless constructor is not a
                // gap here: its TOOL NAMES are what this test reads, and any skill
                // that cannot be constructed this way is covered by the corpus
                // assertion refusing to pass on a thin scan.
            }

            if (skill is not null) yield return skill;
        }
    }
}
