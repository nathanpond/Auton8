using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A declared data object becomes a typed process variable (#166).
/// </summary>
/// <remarks>
/// The declaration is stored as <c>autonate:dataType</c> and rewritten at publish
/// into <c>itemSubjectRef</c>, because neither BPMN spelling does both jobs:
/// the bare QName types the variable but bpmn-js drops it, and an
/// <c>itemDefinition</c> indirection survives the modeller but leaves the engine
/// producing <c>string</c> for everything. This asserts the end of that chain —
/// what the engine actually stored.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class DataObjectExecutionTests : E2ETestBase
{
    public DataObjectExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_declared_data_object_becomes_a_variable_of_its_declared_type()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"dat{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key));
        var instance = await StartAsync(api, key);

        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());

        var variables = document.RootElement.GetProperty("variables").EnumerateArray()
            .ToDictionary(
                v => v.GetProperty("name").GetString()!,
                v => (Type: v.GetProperty("type").GetString(), Value: v.GetProperty("value").GetString()));

        // The declaration seeded a real variable...
        Assert.True(variables.ContainsKey("amount"),
            $"the declaration produced no variable; got: {string.Join(", ", variables.Keys)}");
        Assert.Equal("42.5", variables["amount"].Value);

        // ...with the declared TYPE, which is the half that fails silently. An
        // itemDefinition indirection deploys happily and yields `string` here,
        // and nothing else in this suite would notice.
        Assert.Equal("double", variables["amount"].Type);
    }

    private static string Diagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Amounts" isExecutable="true">
            <bpmn:dataObject id="amountObj" name="amount" autonate:dataType="xsd:double">
              <bpmn:extensionElements><flowable:value>42.5</flowable:value></bpmn:extensionElements>
            </bpmn:dataObject>
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
            <bpmn:userTask id="t1" name="Check the amount" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{{key}}">
              <bpmndi:BPMNShape id="Shape_s" bpmnElement="s">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_t1" bpmnElement="t1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task PublishAsync(IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }
}
