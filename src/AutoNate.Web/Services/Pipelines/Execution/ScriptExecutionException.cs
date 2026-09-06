namespace AutoNate.Web.Services.Pipelines.Execution;

// The sandbox ran the author's code and it failed: a syntax error, a thrown
// exception, a timeout inside the isolate.
//
// Distinct from a transport failure on purpose (#147). Both make the activity
// fail, but only one of them is worth retrying — re-running a script that
// threw will throw again, whereas an executor that was briefly unavailable
// will not be. Collapsing them into one exception, which is what the pipeline
// path did, makes the workflow error surface unable to tell an author's
// mistake from an infrastructure blip.
//
// Derives from InvalidOperationException so the existing pipeline callers,
// which catch that, are unaffected.
public sealed class ScriptExecutionException(string message) : InvalidOperationException(message);
