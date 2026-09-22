using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The fixture writes its own Dapr components so a suite run never joins the
/// queue group a developer's app or the compose container is in (#660).
/// </summary>
/// <remarks>
/// Same app-id means the same queue group means competing consumers, which is
/// the point in production and a race in a test: a bus message would land in
/// whichever process won, and the spec asserting a workflow started would fail
/// for a reason that has nothing to do with the code under test.
/// </remarks>
public sealed class DaprComponentScopingTests
{
    private const string EngineComponent = """
        apiVersion: dapr.io/v1alpha1
        kind: Component
        metadata:
          name: pubsub-workflow-messages
        spec:
          metadata:
            - name: streamName
              value: workflow-execution
            - name: durableName
              value: autonate-web-workflow-messages
            - name: queueGroupName
              value: autonate-web-workflow-messages
        """;

    [Fact]
    public void Both_consumer_names_are_scoped_to_the_run_and_collected_for_teardown()
    {
        var collected = new List<string>();

        var scoped = AutoNateE2EFixture.ScopeConsumerNamesToRun(EngineComponent, "abc123", collected);

        Assert.Contains("value: e2e-abc123-messages", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("autonate-web-workflow-messages", scoped, StringComparison.Ordinal);
        // Durable AND queue group, or the run shares one of the two with everyone else.
        Assert.Equal(2, collected.Count);
        Assert.All(collected, name => Assert.Equal("e2e-abc123-messages", name));
        // The rest of the file is untouched.
        Assert.Contains("value: workflow-execution", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_that_names_no_engine_consumer_passes_through_unchanged()
    {
        var collected = new List<string>();
        const string shared = """
            spec:
              metadata:
                - name: natsURL
                  value: nats://localhost:4222
            """;

        var scoped = AutoNateE2EFixture.ScopeConsumerNamesToRun(shared, "abc123", collected);

        Assert.Equal(shared, scoped);
        Assert.Empty(collected);
    }
}
