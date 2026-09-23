using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoNate.Web.Configuration;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The engine topics subscribe through a component of their own, and that
/// component is the thing that makes replicas compete (#660).
/// </summary>
/// <remarks>
/// <para>
/// Configuration, not code, so nothing in the suite would have noticed either
/// half going missing: a map entry deleted sends the topic back through the
/// shared component (every replica takes a copy again), and a component file
/// losing `durableName` does the same thing more quietly, because the
/// subscription still works.
/// </para>
/// <para>
/// The last fact here is the complement, and it is the one that cost a day:
/// putting those two lines on the SHARED component gives all ~18 topics one
/// consumer, the first topic's filter wins, and delivery stops entirely.
/// </para>
/// </remarks>
public sealed class DaprEngineTopicComponentTests
{
    private static string ComponentsDirectory => Path.Combine(RepoRoot.Path, "infra", "dapr", "components");

    private static Dictionary<string, string> TopicPubSubNames()
    {
        var settings = JsonNode.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "appsettings.json")),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })!;

        var map = settings["Dapr"]?["TopicPubSubNames"]?.AsObject();
        Assert.True(map is not null, "appsettings.json no longer maps any topic to its own pub/sub component (#660).");
        return map!.ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
    }

    /// <summary>`- name: x` / `value: y` pairs from a component's spec metadata.</summary>
    private static Dictionary<string, string> ComponentMetadata(string yaml) =>
        Regex.Matches(yaml, @"(?m)^\s*-\s+name:\s*(?<key>\S+)\s*\n\s*value:\s*(?<value>.*?)\s*$")
            .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value, StringComparer.Ordinal);

    private static string ComponentName(string yaml) =>
        Regex.Match(yaml, @"(?m)^metadata:\s*\n\s+name:\s*(?<name>\S+)\s*$").Groups["name"].Value;

    [Fact]
    public void Every_engine_topic_subscribes_through_a_durable_queue_grouped_component()
    {
        var map = TopicPubSubNames();
        Assert.True(map.Count >= 2, $"only {map.Count} topic(s) mapped; this guard is looking at nothing");

        foreach (var (topic, component) in map)
        {
            var path = Path.Combine(ComponentsDirectory, $"{component}.yaml");
            Assert.True(File.Exists(path),
                $"'{topic}' subscribes through '{component}', and {path} does not exist. "
                + "Dapr would fail the subscription and the topic would go silent.");

            var yaml = File.ReadAllText(path);
            Assert.Equal(component, ComponentName(yaml));

            var metadata = ComponentMetadata(yaml);
            Assert.True(metadata.Count >= 3, $"{component}.yaml parsed as {metadata.Count} metadata entries; the reader is looking at nothing");
            Assert.Equal("workflow-execution", metadata.GetValueOrDefault("streamName"));
            foreach (var required in new[] { "durableName", "queueGroupName" })
            {
                Assert.True(!string.IsNullOrWhiteSpace(metadata.GetValueOrDefault(required)),
                    $"{component}.yaml has no '{required}'. Without it Dapr creates an EPHEMERAL consumer per process, "
                    + "so every replica receives every message and a message-start workflow starts once per replica (#660).");
            }
        }
    }

    [Fact]
    public void The_two_topics_the_engine_uses_by_default_are_mapped()
    {
        var map = TopicPubSubNames();

        Assert.Contains(WorkflowBpmnXml.DefaultMessageTopic, map.Keys);
        Assert.Contains(WorkflowBpmnXml.DefaultSignalTopic, map.Keys);
    }

    /// <summary>
    /// The complement, measured: those two lines on the shared component give
    /// every topic it serves one consumer, and delivery stops.
    /// </summary>
    [Fact]
    public void The_shared_component_is_not_queue_grouped()
    {
        var metadata = ComponentMetadata(File.ReadAllText(Path.Combine(ComponentsDirectory, "pubsub.yaml")));

        Assert.True(metadata.Count >= 3, "pubsub.yaml parsed as nothing");
        Assert.False(metadata.ContainsKey("durableName"));
        Assert.False(metadata.ContainsKey("queueGroupName"));
    }

    [Theory]
    [InlineData("workflow.messages", "pubsub-workflow-messages")]
    [InlineData("workflow.signals", "pubsub-workflow-signals")]
    public void A_mapped_topic_subscribes_through_its_own_component(string topic, string expected)
    {
        var options = new DaprOptions
        {
            PubSubName = "pubsub",
            TopicPubSubNames = TopicPubSubNames()
        };

        Assert.Equal(expected, DaprStreamingSubscriber.PubSubNameFor(options, topic));
    }

    [Theory]
    [InlineData("record.events")]
    [InlineData("system.issues")]
    public void An_unmapped_topic_still_uses_the_shared_component(string topic)
    {
        var options = new DaprOptions { PubSubName = "pubsub", TopicPubSubNames = TopicPubSubNames() };

        Assert.Equal("pubsub", DaprStreamingSubscriber.PubSubNameFor(options, topic));
    }

    /// <summary>A blank mapping is a missing one, not a component named "".</summary>
    [Fact]
    public void A_blank_mapping_falls_back_to_the_shared_component()
    {
        var options = new DaprOptions
        {
            PubSubName = "pubsub",
            TopicPubSubNames = new Dictionary<string, string>(StringComparer.Ordinal) { ["workflow.messages"] = "  " }
        };

        Assert.Equal("pubsub", DaprStreamingSubscriber.PubSubNameFor(options, "workflow.messages"));
    }
}
