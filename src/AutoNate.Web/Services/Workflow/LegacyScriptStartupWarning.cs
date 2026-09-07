using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Services.Workflow;

// Tells an operator at startup that saved workflows will fail, rather than
// letting them find out when one does (#194).
//
// #147 replaced the `execution` binding with `variables`, and #151 catches the
// old shape at publish. Neither helps a diagram that was published before the
// upgrade: it is already deployed and fails on its next run. Before this,
// nothing anywhere said so.
//
// Deliberately a warning and not a refusal to start. The problem is in
// authored content, not in the deployment, and an operator who cannot start
// the application is worse off than one who cannot run four workflows — they
// could not even open the studio to fix them.
public sealed class LegacyScriptStartupWarning(
    IServiceProvider services,
    ILogger<LegacyScriptStartupWarning> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowModelStore>();
            var models = await store.ListAsync(cancellationToken);

            var affected = models
                .Select(m => (m.Name, m.ProcessKey, Published: !m.IsDraft,
                              Findings: LegacyScriptInventory.Scan(m.BpmnXml)))
                .Where(r => r.Findings.Count > 0)
                .ToArray();

            if (affected.Length == 0) return;

            var published = affected.Count(a => a.Published);
            logger.LogWarning(
                "{Affected} saved workflow model(s) contain script tasks written against the " +
                "removed `execution` API ({Published} already published, so they fail on their " +
                "next run). Affected: {Names}. Each must be edited to use `variables.get` / " +
                "`variables.set`; GET /api/workflows/legacy-scripts lists them with the specific " +
                "replacement. See docs/DEPLOYMENT.md and #194.",
                affected.Length,
                published,
                string.Join(", ", affected.Select(a => a.ProcessKey)));
        }
        catch (Exception ex)
        {
            // A diagnostic must never be the reason a deployment fails to come
            // up. If the scan cannot run, the application is still fine.
            logger.LogError(ex, "The legacy script-task scan could not run.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
