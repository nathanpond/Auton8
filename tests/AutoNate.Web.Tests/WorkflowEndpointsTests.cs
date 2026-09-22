using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class WorkflowEndpointsTests
{
    private const string SimpleBpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="simple_flow" name="Simple Flow" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task ListWorkflows_OnEmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var models = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");

        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task GetLatestWorkflow_OnEmptyDatabase_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workflows/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflow_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflowVersions_OnUnknownId_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var versions = await client.GetFromJsonAsync<WorkflowModelVersion[]>(
            $"/api/workflows/{Guid.NewGuid()}/versions");

        Assert.NotNull(versions);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task SaveWorkflow_RoundTrips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var model = new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "My Flow",
            ProcessKey = "my_flow",
            BpmnXml = SimpleBpmn
        };

        var response = await client.PostAsJsonAsync("/api/workflows/", model);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(saved);
        Assert.Equal(model.Id, saved.Id);
        Assert.Equal("My Flow", saved.Name);

        var listed = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");
        Assert.NotNull(listed);
        Assert.Single(listed);
    }

    [Fact]
    public async Task PrepareWorkflow_NormalizesNameAndProcessKey()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "  Spaced Name  ",
                ProcessKey = string.Empty,
                BpmnXml = SimpleBpmn
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.Equal("Spaced Name", result.Model.Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Model.ProcessKey));
    }

    // #107: refusing to deploy must not make an old diagram unopenable.
    //
    // This is the regression a naive validation change causes, and it is easy to
    // miss because nobody tests with a diagram they can no longer publish. The two
    // are different paths — validation runs on prepare, loading does not — so the
    // test has to show the diagram survives the round trip, not merely that publish
    // rejects it.
    [Fact]
    public async Task PrepareWorkflow_RefusesAnUnrunnableElement_ButStillReturnsTheDiagram()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="legacy_flow" name="Legacy Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1" />
                <bpmn:transaction id="Task_1" name="Two of three" />
                <bpmn:endEvent id="EndEvent_1" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Legacy Flow",
                ProcessKey = "legacy_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);

        // Publishing is refused, and the author is told which element and why.
        // #218 rebased this fixture off the complex gateway; #111 rebases it again
        // off the business rule task, for the same reason both times -- the
        // element started publishing. Transaction is `withdrawn` as well as
        // `cannot-execute`, so no story is queued to make it run.
        // Matched on the unsupported-element phrasing as well as the name, for the
        // same reason `ValidateProcess_RefusesAnElementTheEngineCannotRun` is:
        // "any error mentioning Transaction" is satisfied by whatever else an
        // empty transaction trips, and a predicate that loose is how a rebased
        // fixture stays green while proving nothing.
        Assert.Contains(result.Errors, e => e.Contains("cannot be deployed", StringComparison.Ordinal)
                                            && e.Contains("Transaction", StringComparison.Ordinal)
                                            && e.Contains("Two of three", StringComparison.Ordinal));

        // And the diagram comes back intact, so the studio still renders it. If
        // validation ever stripped or rejected the payload, this is what would fail.
        Assert.Contains("transaction", result.Model.BpmnXml, StringComparison.Ordinal);
        Assert.Contains("Two of three", result.Model.BpmnXml, StringComparison.Ordinal);
    }

    // #160: the story's demo, at the endpoint that actually gates the SPA.
    //
    // Written against /prepare because that is the surface the SPA uses.
    //
    // It used to carry a second reason — that /publish went straight to
    // DeployProcessAsync and validated nothing, so the same test pointed there
    // would pass with a successful deploy. #225 fixed that: publish now runs the
    // full set, and PublishWorkflow_RefusesADiagramPrepareWouldReject below is
    // the test that says so.
    [Fact]
    public async Task PrepareWorkflow_RefusesHandAuthoredLinkEvents_AndOffersTheAlternative()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="link_flow" name="Link Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1" />
                <bpmn:intermediateThrowEvent id="Throw_1" name="Skip ahead">
                  <bpmn:linkEventDefinition id="Link_1" name="Ahead" />
                </bpmn:intermediateThrowEvent>
                <bpmn:intermediateCatchEvent id="Catch_1" name="Ahead">
                  <bpmn:linkEventDefinition id="Link_2" name="Ahead" />
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="EndEvent_1" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Link Flow",
                ProcessKey = "link_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.Contains(result.Errors, e => e.Contains("Intermediate Throw (Link)", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Intermediate Catch (Link)", StringComparison.Ordinal));
        Assert.All(
            result.Errors.Where(e => e.Contains("(Link)", StringComparison.Ordinal)),
            e => Assert.Contains("sequence flow", e, StringComparison.OrdinalIgnoreCase));

        // The diagram still comes back, so an author who already had one can open
        // it and replace the pair rather than losing the work.
        Assert.Contains("linkEventDefinition", result.Model.BpmnXml, StringComparison.Ordinal);
    }

    // The complement: an element the studio has not wired yet but Flowable runs is
    // NOT refused. The old deny-lists refused 25 such elements, and a fix that
    // simply turned those warnings into errors would have made this fail.
    [Fact]
    public async Task PrepareWorkflow_AcceptsAnElementFlowableRuns_EvenWhileTheStudioCallsItComingSoon()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="boundary_flow" name="Boundary Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1" />
                <bpmn:userTask id="Task_1" name="Approve" />
                <bpmn:boundaryEvent id="Boundary_1" name="Escalate" attachedToRef="Task_1">
                  <bpmn:escalationEventDefinition id="Escalation_1" />
                </bpmn:boundaryEvent>
                <bpmn:endEvent id="EndEvent_1" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Boundary Flow",
                ProcessKey = "boundary_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Errors, e => e.Contains("cannot be deployed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareWorkflow_ReturnsWarning_WhenSignalFilterReferencesUnknownShortCode()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Seed a known record type so we can verify the warning is selective.
        await SeedRecordTypeAsync(factory, "asset");

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Signal_record_created" name="record.created" flowable:topic="record.events" />
              <bpmn:process id="filter_flow" name="Filter Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1">
                  <bpmn:signalEventDefinition signalRef="Signal_record_created" flowable:recordTypeShortCodes="asset,unknownType" />
                </bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Filter Flow",
                ProcessKey = "filter_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        // Publish proceeds — no errors from this rule.
        Assert.DoesNotContain(result.Errors,
            e => e.Contains("recordTypeShortCodes", StringComparison.OrdinalIgnoreCase));
        // The unknown shortcode is named in the warning.
        Assert.Contains(result.Warnings,
            w => w.Contains("unknownType", StringComparison.OrdinalIgnoreCase));
        // The known shortcode is NOT named — only unknowns.
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("asset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareWorkflow_NoRecordTypeWarning_WhenAllShortCodesKnown()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        await SeedRecordTypeAsync(factory, "asset");

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Signal_record_created" name="record.created" flowable:topic="record.events" />
              <bpmn:process id="known_flow" name="Known Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1">
                  <bpmn:signalEventDefinition signalRef="Signal_record_created" flowable:recordTypeShortCodes="asset" />
                </bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Known Flow",
                ProcessKey = "known_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("not found in this environment", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SeedRecordTypeAsync(AutoNateWebApplicationFactory factory, string shortCode)
    {
        var dbFactory = factory.Database.CreateDbContextFactory();
        await using var dbContext = await dbFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        dbContext.RecordTypes.Add(new RecordTypeEntity
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            Name = shortCode,
            Description = null,
            Icon = null,
            Color = null,
            IsSystem = false,
            IsArchived = false,
            NextKeyNumber = 1,
            CreatedAtUtc = now,
            CreatedBy = Guid.Empty,
            UpdatedAtUtc = now,
            UpdatedBy = Guid.Empty
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task PublishWorkflow_DelegatesToFlowableStub()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Publish Me",
            ProcessKey = "publish_me",
            BpmnXml = SimpleBpmn
        };
        // Save first so the store has a row to publish.
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/publish",
            model);
        response.EnsureSuccessStatusCode();

        Assert.Contains("Deploy:publish_me", factory.FlowableStub.Calls);
    }

    /// <summary>
    /// The publish ROUTE, not the pure function, on an engine refusal (#344).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every test of this path called <c>DescribeEngineRefusal</c> directly, so
    /// the route's own behaviour was unguarded: reverting the whole of #334 —
    /// deleting the catch and letting the raw exception escape as a 500 — left
    /// the suite green at 44/44. These two rows are what makes that impossible.
    /// </para>
    /// <para>
    /// The status matters and was wrong. Measured: Flowable answers a
    /// <b>validation</b> refusal — a diagram the author drew badly — with HTTP
    /// 500. The old code branched on <c>IsCallerError</c>, so the author got 502
    /// Bad Gateway for their own mistake and the 400 branch never ran. What
    /// actually distinguishes the two is whether the engine named a problem with
    /// the diagram.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PublishWorkflow_WhenTheEngineNamesADiagramProblem_Returns400WithOurWords()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Flowable answers validation refusals with 500, which is the case the
        // old IsCallerError split got backwards.
        factory.FlowableStub.DeployThrows = new FlowableRequestException(
            // #371 made the operation load-bearing -- the deployment table is
            // consulted only for the real deploy operation -- so a fixture using
            // an approximation silently stopped exercising this path.
            HttpStatusCode.InternalServerError, EngineRefusal.DeployOperation,
            $"Flowable could not {EngineRefusal.DeployOperation}. HTTP 500 Internal Server Error. "
            + "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task does not have an "
            + "implementation defined - [Extra info : processDefinitionId = secretProc:3:9f2c ] "
            + "at org.flowable.bpmn.Foo.bar(Foo.java:198) /Users/npond/secret.bpmn20.xml");

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id, Name = "Refused", ProcessKey = "refused_flow", BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Our sentence, in the shape the studio's prepare path already renders.
        Assert.Contains("errors", body, StringComparison.Ordinal);
        Assert.Contains("no behaviour chosen", body, StringComparison.Ordinal);
        Assert.Contains("flowable-servicetask-missing-implementation", body, StringComparison.Ordinal);

        // And nothing the engine wrote.
        Assert.DoesNotContain("Extra info", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secretProc", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".java:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishWorkflow_WhenTheEngineItselfFailed_Returns502AndLeaksNothing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // No problem code: we genuinely do not know the caller was at fault.
        factory.FlowableStub.DeployThrows = new FlowableRequestException(
            // #371 made the operation load-bearing -- the deployment table is
            // consulted only for the real deploy operation -- so a fixture using
            // an approximation silently stopped exercising this path.
            HttpStatusCode.InternalServerError, EngineRefusal.DeployOperation,
            $"Flowable could not {EngineRefusal.DeployOperation}. HTTP 500 Internal Server Error. "
            + "Could not acquire a connection: jdbc:postgresql://flowable-db.internal:5432/db"
            + "?user=flowable&password=Hunter2!");

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id, Name = "Engine Down", ProcessKey = "engine_down", BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("Hunter2", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jdbc", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flowable-db.internal", body, StringComparison.Ordinal);
    }

    // #225. Publish used to run only a promoted handful of rules, so every other
    // rule in the set was advisory: the studio calls prepare first, a direct API
    // caller need not, and their diagram reached the engine unchecked.
    [Fact]
    public async Task PublishWorkflow_RefusesADiagramPrepareWouldReject()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        // A transaction: cannot-execute AND withdrawn, so no diagram can fix it
        // and no story is queued to make it run. Rebased here by #111, which made
        // the business rule task this used to carry publishable.
        var xml = SimpleBpmn.Replace(
            "</bpmn:process>",
            "<bpmn:transaction id=\"tx\" name=\"Take the payment\" /></bpmn:process>",
            StringComparison.Ordinal);

        var model = new WorkflowModel
        {
            Id = id, Name = "Refuse Me", ProcessKey = "refuse_me", BpmnXml = xml
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Transaction", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // And it never reached the engine. Asserting the 400 alone would pass for
        // an implementation that deployed first and then complained.
        Assert.DoesNotContain("Deploy:refuse_me", factory.FlowableStub.Calls);
    }

    // The complement, and the one that matters most: a gate that refused
    // everything would satisfy the test above and break the product.
    // PublishWorkflow_DelegatesToFlowableStub already covers the happy path, so
    // this pins the specific risk — that the full set rejects diagrams the
    // promoted subset accepted.
    [Fact]
    public async Task PublishWorkflow_StillAcceptsAValidDiagram()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id, Name = "Fine", ProcessKey = "still_fine", BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);

        response.EnsureSuccessStatusCode();
        Assert.Contains("Deploy:still_fine", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task PublishWorkflow_MismatchedId_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{Guid.NewGuid()}/publish",
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "x",
                ProcessKey = "x",
                BpmnXml = SimpleBpmn
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartProcessInstance_AutoNamesUsingModelNameAndCount_WhenRequestNameIsMissing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Seed a workflow model so the auto-name lookup finds a label.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowModelStore>();
            await store.SaveAsync(new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Lead Qualification",
                ProcessKey = "my_flow",
                BpmnXml = string.Empty
            });
        }

        // Three existing runs → next auto-name should be "(4)".
        factory.FlowableStub.InstanceCountsByDefinitionKey["my_flow"] = 3;

        var response = await client.PostAsJsonAsync(
            "/api/workflows/my_flow/start",
            new WorkflowEndpoints.StartInstanceRequest(null, null));
        response.EnsureSuccessStatusCode();

        Assert.Contains("Start:my_flow:Lead Qualification (4)", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task StartProcessInstance_PassesExplicitName_VerbatimToFlowable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/workflows/my_flow/start",
            new WorkflowEndpoints.StartInstanceRequest("Custom Run Label", null));
        response.EnsureSuccessStatusCode();

        Assert.Contains("Start:my_flow:Custom Run Label", factory.FlowableStub.Calls);
        // No count lookup when caller supplied a name.
        Assert.DoesNotContain(factory.FlowableStub.Calls, c => c.StartsWith("CountByDefinitionKey:"));
    }

    [Fact]
    public async Task PauseWorkflow_OnUnpublished_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/workflows/", new WorkflowModel
        {
            Id = id,
            Name = "Unpublished",
            ProcessKey = "unpublished",
            BpmnXml = SimpleBpmn
        })).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/workflows/{id}/pause", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.FlowableStub.Calls, c => c.StartsWith("SuspendDefinition:"));
    }

    [Fact]
    public async Task PauseWorkflow_OnMissing_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsync($"/api/workflows/{Guid.NewGuid()}/pause", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PauseAndResumeWorkflow_ToggleFlowableSuspendedState()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Pausable",
            ProcessKey = "pausable",
            BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        // Seed the stub so GetLatestProcessDefinitionAsync (used by pause/resume
        // and the IsSuspended augmentation) returns a real definition.
        factory.FlowableStub.ProcessDefinitionsByKey["pausable"] = new Models.FlowableProcessDefinitionSummary
        {
            Id = "pd-pausable",
            Key = "pausable",
            Version = 1,
            DeploymentId = "dep-pausable",
            Suspended = false
        };

        var pauseResponse = await client.PostAsync($"/api/workflows/{id}/pause", null);
        pauseResponse.EnsureSuccessStatusCode();
        var paused = await pauseResponse.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(paused);
        Assert.True(paused.IsSuspended);
        Assert.Contains("SuspendDefinition:pausable", factory.FlowableStub.Calls);

        var resumeResponse = await client.PostAsync($"/api/workflows/{id}/resume", null);
        resumeResponse.EnsureSuccessStatusCode();
        var resumed = await resumeResponse.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(resumed);
        Assert.False(resumed.IsSuspended);
        Assert.Contains("ActivateDefinition:pausable", factory.FlowableStub.Calls);
    }

    /// <summary>
    /// Two executable pools, deployed as a set. Both keys have a start event, so
    /// the stub's deployment produces two definitions, and <c>/pause</c> must
    /// suspend BOTH: a message flow into an unpaused counterparty would still
    /// start instances (#169 AC9). The set branch of
    /// <c>PublishedDefinitionKeysAsync</c> never executed under test before #646.
    /// </summary>
    private const string TwoPoolBpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_b" name="Buyer" processRef="buyer" />
            <bpmn:participant id="P_s" name="Seller" processRef="seller" />
          </bpmn:collaboration>
          <bpmn:process id="buyer" name="Buyer" isExecutable="true">
            <bpmn:startEvent id="bs" /><bpmn:sequenceFlow id="bf" sourceRef="bs" targetRef="be" /><bpmn:endEvent id="be" />
          </bpmn:process>
          <bpmn:process id="seller" name="Seller" isExecutable="true">
            <bpmn:startEvent id="ss" /><bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" /><bpmn:endEvent id="se" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task PauseAndResume_ApplyToEveryDefinitionInThePublishedSet()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel { Id = id, Name = "Two pools", ProcessKey = "buyer", BpmnXml = TwoPoolBpmn };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        // The stub read the set back by deployment id: two definitions.
        var set = Assert.Single(factory.FlowableStub.DeployedSets).Value;
        Assert.Equal(new[] { "buyer", "seller" }, set.Select(d => d.Key).Order().ToArray());
        foreach (var key in new[] { "buyer", "seller" })
        {
            factory.FlowableStub.ProcessDefinitionsByKey[key] = new Models.FlowableProcessDefinitionSummary
            {
                Id = $"pd-{key}", Key = key, Version = 1, DeploymentId = set[0].DeploymentId, Suspended = false
            };
        }

        (await client.PostAsync($"/api/workflows/{id}/pause", null)).EnsureSuccessStatusCode();
        Assert.Contains("SuspendDefinition:buyer", factory.FlowableStub.Calls);
        // THE COUNTERPARTY TOO. A primary-only pause passes the fact above and
        // leaves Seller startable.
        Assert.Contains("SuspendDefinition:seller", factory.FlowableStub.Calls);

        (await client.PostAsync($"/api/workflows/{id}/resume", null)).EnsureSuccessStatusCode();
        Assert.Contains("ActivateDefinition:buyer", factory.FlowableStub.Calls);
        Assert.Contains("ActivateDefinition:seller", factory.FlowableStub.Calls);
    }

    /// <summary>
    /// The window #169 built <c>WorkflowPublishCompensation</c> for, asserted
    /// at the endpoint rather than on the class alone (#646): the engine
    /// accepted the deployment, the record failed, and the deployment is
    /// withdrawn -- by the id the engine produced.
    /// </summary>
    [Fact]
    public async Task Publish_WithdrawsTheDeployment_WhenRecordingItFails()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            configureServices: services => services.AddScoped<IWorkflowModelStore>(sp =>
                new FailingPublishStore(ActivatorUtilities.CreateInstance<EfCoreWorkflowModelStore>(sp))));
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel { Id = id, Name = "Unrecordable", ProcessKey = "unrecordable", BpmnXml = SimpleBpmn };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var deployed = Assert.Single(factory.FlowableStub.DeployedSets).Key;
        Assert.Equal(new[] { deployed }, factory.FlowableStub.DeletedDeployments);
    }

    /// <summary>
    /// #653. A direct API caller whose body carries a process id other than the
    /// model's key: the deployable is PREPARED, so the engine is asked to deploy
    /// the key the record will be looked up by. Before this the raw id deployed,
    /// the readback by key found nothing, and the deployment stayed in the engine
    /// with nothing recording it.
    /// </summary>
    [Fact]
    public async Task Publish_DeploysThePreparedCopy_WhenTheBodysProcessIdIsNotTheKey()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var body = SimpleBpmn.Replace("<bpmn:process id=\"", "<bpmn:process id=\"somethingelse_", StringComparison.Ordinal);
        Assert.NotEqual(SimpleBpmn, body);
        var model = new WorkflowModel { Id = id, Name = "Renamed", ProcessKey = "renamed_key", BpmnXml = body };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model);

        response.EnsureSuccessStatusCode();
        var deployed = Assert.Single(factory.FlowableStub.DeployedModels);
        Assert.Contains("<bpmn:process id=\"renamed_key\"", deployed.BpmnXml, StringComparison.Ordinal);
        Assert.DoesNotContain("somethingelse_", deployed.BpmnXml, StringComparison.Ordinal);
        Assert.Empty(factory.FlowableStub.DeletedDeployments);
    }

    /// <summary>Every read delegates; only the record of a publish fails.</summary>
    private sealed class FailingPublishStore(IWorkflowModelStore inner) : IWorkflowModelStore
    {
        public Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyList<WorkflowModel>> ListPublishedAsync(CancellationToken cancellationToken = default) => inner.ListPublishedAsync(cancellationToken);
        public Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default) => inner.GetAsync(workflowModelId, cancellationToken);
        public Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default) => inner.GetMostRecentAsync(cancellationToken);
        public Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) => inner.GetByProcessKeyAsync(processKey, cancellationToken);
        public Task<WorkflowModel?> GetPublishedByDefinitionKeyAsync(string processDefinitionKey, CancellationToken cancellationToken = default) => inner.GetPublishedByDefinitionKeyAsync(processDefinitionKey, cancellationToken);
        public Task<WorkflowModel?> GetPublishedByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) => inner.GetPublishedByProcessKeyAsync(processKey, cancellationToken);
        public Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default) => inner.SaveAsync(model, cancellationToken);
        public Task<WorkflowModel> PublishAsync(WorkflowModel model, WorkflowDeploymentInfo deployment, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the record failed after the engine accepted the deployment");
        public Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(Guid workflowModelId, CancellationToken cancellationToken = default) => inner.ListVersionsAsync(workflowModelId, cancellationToken);
        public Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowModelId, cancellationToken);
    }

    [Fact]
    public async Task ListWorkflows_PopulatesIsSuspendedFromFlowable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Listed",
            ProcessKey = "listed",
            BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        factory.FlowableStub.ProcessDefinitionsByKey["listed"] = new Models.FlowableProcessDefinitionSummary
        {
            Id = "pd-listed",
            Key = "listed",
            Version = 1,
            DeploymentId = "dep-listed",
            Suspended = true
        };

        var listed = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");
        Assert.NotNull(listed);
        var found = Assert.Single(listed);
        Assert.True(found.IsSuspended);
    }

    [Fact]
    public async Task DeleteWorkflow_OnUnknownId_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.DeleteAsync($"/api/workflows/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWorkflow_RemovesUnpublishedRow()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/workflows/", new WorkflowModel
        {
            Id = id,
            Name = "Disposable",
            ProcessKey = "disposable",
            BpmnXml = SimpleBpmn
        })).EnsureSuccessStatusCode();

        var deleteResponse = await client.DeleteAsync($"/api/workflows/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listed = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");
        Assert.NotNull(listed);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task DeleteWorkflow_CascadesVersionsForPublishedRow()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Published",
            ProcessKey = "published_to_delete",
            BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        // Sanity: version row exists pre-delete.
        var versionsBefore = await client.GetFromJsonAsync<WorkflowModelVersion[]>(
            $"/api/workflows/{id}/versions");
        Assert.NotNull(versionsBefore);
        Assert.NotEmpty(versionsBefore);

        var deleteResponse = await client.DeleteAsync($"/api/workflows/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Versions cascade-deleted by the FK.
        var versionsAfter = await client.GetFromJsonAsync<WorkflowModelVersion[]>(
            $"/api/workflows/{id}/versions");
        Assert.NotNull(versionsAfter);
        Assert.Empty(versionsAfter);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();
    }
}
