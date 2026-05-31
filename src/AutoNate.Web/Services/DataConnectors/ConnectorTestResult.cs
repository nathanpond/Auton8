namespace AutoNate.Web.Services.DataConnectors;

// Returned by IDataConnectorHandler.TestAsync — the "connect" admin action
// gates this. Success carries a short summary the UI surfaces verbatim
// (e.g. "Reached host, received 200 with 12 rows in sample"); Failure
// carries a single human-readable reason without leaking secrets back.
public sealed record class ConnectorTestResult(
    bool Success,
    string Message,
    TimeSpan Elapsed)
{
    public static ConnectorTestResult Ok(string message, TimeSpan elapsed)
        => new(true, message, elapsed);

    public static ConnectorTestResult Fail(string message, TimeSpan elapsed)
        => new(false, message, elapsed);
}
