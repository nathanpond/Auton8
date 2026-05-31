using System.Diagnostics;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataConnectors.Builtin;

// SMB / Samba network share connector. v1 ships the registry + config
// surface so the SPA can offer "smb" as a kind in the connector-create
// dropdown, but the wire protocol is NOT implemented in this commit —
// the SMBLibrary integration needs a real samba target to validate
// against and lands in a follow-up commit dedicated to it.
// TestAsync and FetchAsync return failure with a clean explanation so
// operators see the right error rather than a silent no-op.
public sealed class SmbDataConnectorHandler : IDataConnectorHandler
{
    public string Kind => DataConnectorKinds.Smb;

    public Task<ConnectorTestResult> TestAsync(
        DataConnector connector, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        sw.Stop();
        return Task.FromResult(ConnectorTestResult.Fail(
            "SMB connector wire integration ships in a follow-up commit. " +
            "Register the kind and config now; fetch lands when SMBLibrary " +
            "is wired in against a samba target.",
            sw.Elapsed));
    }

    public Task<ConnectorRefreshState> FetchAsync(
        DataConnector connector,
        ConnectorRefreshState state,
        IConnectorFetchSink sink,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "SMB connector fetch is not yet implemented. The handler is registered " +
            "so the kind appears in admin UI and AQL `Dataset(...)` validation; the " +
            "SMBLibrary wire integration is tracked as a Phase-1 follow-up.");
    }
}
