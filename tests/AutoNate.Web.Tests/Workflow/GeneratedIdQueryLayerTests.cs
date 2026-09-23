using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Where a generated id is mapped back to the id the author drew, and where it
/// deliberately is not (#327).
/// </summary>
/// <remarks>
/// <para>
/// The owner's decision of 2026-09-21, in their words: <i>leave the query layer
/// raw, map everything else</i>. A query result is data an author writes a
/// filter against and a dataset stores; rewriting ids there changes what a
/// saved query matches, which is a different thing from what a person is shown.
/// </para>
/// <para>
/// Both halves are guarded because both can rot: a read surface that loses its
/// mapping goes back to showing `cg__autonateRoute`, and a query entity that
/// gains one silently changes what stored queries return.
/// </para>
/// </remarks>
public sealed class GeneratedIdQueryLayerTests
{
    private static readonly string[] MappingApis =
    [
        "GetExpansionSourceMapAsync",
        "GetExpansionSourceMapByDefinitionAsync",
        "BuildExpansionSourceMap",
        "ExpansionSourceIds"
    ];

    /// <summary>The four query entities that expose an activity id.</summary>
    private static readonly string[] QueryEntities =
    [
        "Services/Query/Entities/WorkflowHistoryQueryEntity.cs",
        "Services/Query/Entities/WorkflowAnalyticsQueryEntity.cs",
        "Services/Query/Entities/WorkflowExecutionsQueryEntity.cs",
        "Services/Query/Entities/FlowsQueryEntity.cs"
    ];

    /// <summary>The surfaces a person or the assistant reads.</summary>
    private static readonly string[] MappedReadSurfaces =
    [
        "Endpoints/ExecutionEndpoints.cs",
        "Services/Agent/Skills/LookupWorkflowExecutionsSkill.cs",
        "Services/SystemIssues/Detectors/WorkflowExecutionErrorOpenDetector.cs",
        "Services/Flowable/FlowableClient.cs"
    ];

    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", relativePath);
        Assert.True(File.Exists(path), $"{relativePath} has moved; this guard is looking at nothing.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_query_layer_still_returns_the_engines_own_ids()
    {
        foreach (var entity in QueryEntities)
        {
            var source = Read(entity);
            // Vacuity: it must still be an entity that HAS an activity id, or
            // the absence below means nothing.
            Assert.Contains("ActivityId", source, StringComparison.Ordinal);

            foreach (var api in MappingApis)
            {
                Assert.False(source.Contains(api, StringComparison.Ordinal),
                    $"{entity} now maps generated ids ({api}). The owner's decision on #327 was to leave the query "
                    + "layer raw: a saved query filters on what the engine stores, and mapping here changes what "
                    + "stored queries match. If the decision has changed, change it here and say so.");
            }
        }
    }

    [Fact]
    public void Every_read_surface_still_maps_them()
    {
        foreach (var surface in MappedReadSurfaces)
        {
            var source = Read(surface);
            Assert.True(MappingApis.Any(api => source.Contains(api, StringComparison.Ordinal)),
                $"{surface} no longer maps a generated id back to the authored one (#327). A reader would be shown "
                + "`cg__autonateRoute`, which exists in no diagram they have seen.");
        }
    }
}
