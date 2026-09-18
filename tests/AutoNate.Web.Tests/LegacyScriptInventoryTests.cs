using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

// #194: find saved workflow models written against the API #147 removed.
//
// #151 protects everything authored from now on. This is for the diagrams that
// were already published, which fail at runtime with nothing having warned
// anyone — the gap /n8-verify found on a real database, where 4 of 5 workflows
// with script tasks were affected.
public sealed class LegacyScriptInventoryTests
{
    private static string Model(params string[] scripts)
    {
        var tasks = string.Join("", scripts.Select((s, i) =>
            $"""
             <bpmn:scriptTask id="t{i}" name="Task{i}" scriptFormat="javascript">
               <bpmn:script>{System.Security.SecurityElement.Escape(s)}</bpmn:script>
             </bpmn:scriptTask>
             """));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="D" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="p" isExecutable="true">{tasks}</bpmn:process>
            </bpmn:definitions>
            """;
    }

    [Fact]
    public void ItFindsTheRemovedApiAndNamesTheTaskAndTheReplacement()
    {
        var findings = LegacyScriptInventory.Scan(Model("""execution.setVariable("x", 1);"""));

        var finding = Assert.Single(findings);
        Assert.Equal("Task0", finding.ScriptTask);
        // Naming the task alone would leave an operator opening every script
        // to find out what to change.
        Assert.Contains("variables.set", finding.Problem);
    }

    [Fact]
    public void ItReportsEachAffectedTaskSeparately()
    {
        // A model with several bad scripts is several pieces of work, and an
        // operator planning the edit needs the count, not a boolean.
        var findings = LegacyScriptInventory.Scan(Model(
            """execution.setVariable("x", 1);""",
            """variables.set("ok", true);""",
            """var v = execution.getVariable("y");"""));

        Assert.Equal(2, findings.Count);
        Assert.Equal(new List<string> { "Task0", "Task2" }, findings.Select(f => f.ScriptTask).ToList());
    }

    [Fact]
    public void AModelUsingTheCurrentApiIsNotReported()
    {
        Assert.Empty(LegacyScriptInventory.Scan(Model("""variables.set("x", 1);""")));
    }

    [Fact]
    public void AMentionInACommentIsNotReported()
    {
        // Same false-positive guard as the publish validator, because it is
        // the same detection. A report that cries wolf gets ignored.
        Assert.Empty(LegacyScriptInventory.Scan(Model("""
            // replaces execution.setVariable
            variables.set("x", 1);
            """)));
    }

    [Fact]
    public void UnparseableXmlYieldsNothingRatherThanThrowing()
    {
        // This runs over whatever is already stored, including rows written by
        // older builds. A report that dies on one malformed model tells an
        // operator less than one that skips it.
        Assert.Empty(LegacyScriptInventory.Scan("<not-xml"));
        Assert.Empty(LegacyScriptInventory.Scan(null));
        Assert.Empty(LegacyScriptInventory.Scan(""));
    }

    [Fact]
    public void DetectionIsTheSameSourceAsThePublishValidator()
    {
        // The point of reusing ScriptSurfaceRules: a fourth consumer with its
        // own copy of the rules is how they start disagreeing. If the two ever
        // diverge, this fails.
        const string script = """execution.removeVariable("x");""";
        var fromInventory = LegacyScriptInventory.Scan(Model(script)).Select(f => f.Problem);
        var fromRules = ScriptSurfaceRules.FindRejected(script);

        Assert.Equal(fromRules, fromInventory);
    }
    /// <summary>
    /// The scan reads the PUBLISHED xml, not the draft (#558).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both callers read <c>ListAsync</c>, which returns each row's working
    /// copy. So the scan whose whole purpose is warning about DEPLOYED diagrams
    /// — the ones #151 cannot help with, because they were published before the
    /// rule existed — reported on whatever the author last typed into the draft.
    /// A workflow published with a removed-API script and since draft-edited
    /// read clean while its deployed definition still failed on its next run.
    /// </para>
    /// <para>
    /// Both directions are asserted: the published finding survives a clean
    /// draft edit, and a clean published version is not condemned by a dirty
    /// draft. The second is the correct division of labour — #151 refuses that
    /// at publish — and asserting only the first would pass against a scan that
    /// read both.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ScanStoreAsync_ReadsThePublishedXmlNotTheDraft()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var legacy = Model("""execution.setVariable("x", 1);""");
        var clean = Model("""variables.set("x", 1);""");

        // Published DIRTY, then draft-edited clean. Still deployed, still fails.
        var dirty = await store.SaveAsync(new WorkflowModel
        {
            Name = "Dirty Published", ProcessKey = "dirty_published", BpmnXml = legacy
        });
        var publishedDirty = await store.PublishAsync(dirty, Deployment("dirty_published"));
        await store.SaveAsync(publishedDirty with { BpmnXml = clean });

        // Published CLEAN, then draft-edited dirty. #151 refuses that at publish.
        var ok = await store.SaveAsync(new WorkflowModel
        {
            Name = "Clean Published", ProcessKey = "clean_published", BpmnXml = clean
        });
        var publishedClean = await store.PublishAsync(ok, Deployment("clean_published"));
        await store.SaveAsync(publishedClean with { BpmnXml = legacy });

        var affected = await LegacyScriptInventory.ScanStoreAsync(store);

        var row = Assert.Single(affected);
        Assert.Equal("dirty_published", row.ProcessKey);

        // AND IT IS REPORTED AS PUBLISHED. `!IsDraft` answered false here,
        // because any definition change sets IsDraft -- so the row silently left
        // the "already published, so they fail on their next run" tally exactly
        // when the operator most needed to see it.
        Assert.True(row.IsPublished);
    }

    /// <summary>A never-published draft is reported, and as unpublished (#558).</summary>
    /// <remarks>
    /// The other complement. Without it, "read the published xml" could be
    /// implemented as "read only published models", which would hide a legacy
    /// script an author is about to try to publish.
    /// </remarks>
    [Fact]
    public async Task ScanStoreAsync_StillReportsANeverPublishedDraft()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        await store.SaveAsync(new WorkflowModel
        {
            Name = "Draft Only",
            ProcessKey = "draft_only",
            BpmnXml = Model("""execution.setVariable("x", 1);""")
        });

        var row = Assert.Single(await LegacyScriptInventory.ScanStoreAsync(store));

        Assert.Equal("draft_only", row.ProcessKey);
        Assert.False(row.IsPublished);
    }

    private static WorkflowDeploymentInfo Deployment(string processKey) => new()
    {
        DeploymentId = $"deployment-{processKey}",
        ProcessDefinitionId = $"definition-{processKey}",
        ProcessDefinitionKey = processKey,
        ProcessDefinitionVersion = 1,
        DeployedAtUtc = DateTimeOffset.UtcNow
    };
}
