using System.Text.Json;
using Microsoft.Playwright;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Thin wrapper around a signed-in <see cref="IAPIRequestContext"/> for
/// creating prerequisite data fast (much faster than driving the UI). Use it
/// when a test's goal is to verify a UI behavior on top of seeded data, not to
/// verify the create flow itself. Each helper returns a strongly-typed result
/// parsed from the endpoint response.
///
/// Methods are added incrementally as test phases need them — the per-phase
/// build-out lives in the comprehensive plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>).
/// </summary>
public sealed class ApiSeeder
{
    private readonly IAPIRequestContext _request;

    public ApiSeeder(IAPIRequestContext request) => _request = request;

    /// <summary>
    /// POST /api/record-types/ — creates a record type. Required by every
    /// records-related test as the precondition for creating records.
    /// </summary>
    public async Task<RecordTypeDto> CreateRecordTypeAsync(
        string shortCode,
        string name,
        string? description = null,
        string? icon = null,
        string? color = null)
    {
        var response = await _request.PostAsync("/api/record-types/", new APIRequestContextOptions
        {
            DataObject = new
            {
                shortCode,
                name,
                description,
                icon,
                color
            }
        });

        await EnsureSuccessAsync(response, "create record type");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/record-types/.");

        return new RecordTypeDto(
            Id: json.GetProperty("id").GetGuid(),
            ShortCode: json.GetProperty("shortCode").GetString()!,
            Name: json.GetProperty("name").GetString()!);
    }

    /// <summary>
    /// POST /api/records/ — creates a record of the given type. <c>Values</c>
    /// is sent as an empty object since the record type has no custom fields
    /// in most foundation tests; pass <paramref name="valuesJson"/> to send a
    /// raw JSON object string when custom fields are needed.
    /// </summary>
    public async Task<RecordDto> CreateRecordAsync(
        Guid recordTypeId,
        string name,
        string? status = null,
        string? valuesJson = null)
    {
        // CreateRecordRequest.Values is a JsonElement, so an empty object
        // ({}) is the safest default — record types with no custom fields
        // accept it directly.
        using var doc = System.Text.Json.JsonDocument.Parse(valuesJson ?? "{}");
        var response = await _request.PostAsync("/api/records/", new APIRequestContextOptions
        {
            DataObject = new
            {
                recordTypeId,
                name,
                status,
                values = doc.RootElement
            }
        });

        await EnsureSuccessAsync(response, "create record");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/records/.");

        return new RecordDto(
            Id: json.GetProperty("id").GetGuid(),
            Key: json.GetProperty("key").GetString()!,
            Name: json.GetProperty("name").GetString()!);
    }

    /// <summary>
    /// Saves a minimal user-task workflow via <c>POST /api/workflows/</c>, then
    /// publishes it via <c>POST /api/workflows/{id}/publish</c> so it's deployed
    /// to Flowable and ready to receive instances. The BPMN is hand-rolled to be
    /// the smallest shape Flowable will accept: <c>start → userTask → end</c>.
    /// The user task keeps the instance in the RUNNING state long enough for UI
    /// tests to observe / cancel / delete it.
    ///
    /// Flowable lives outside the ephemeral test DB (its own Postgres schema),
    /// so each call must use a unique <paramref name="processKey"/> to avoid
    /// deployment collisions with prior runs or with the dev developer's
    /// workflows. <see cref="TestNames"/>'s slugs already supply that.
    /// </summary>
    public async Task<WorkflowDto> CreateAndPublishWorkflowAsync(
        string processKey,
        string name,
        string? assignee = null)
    {
        var modelId = Guid.NewGuid();
        var bpmnXml = MinimalUserTaskBpmn(processKey, name, assignee);
        var now = DateTimeOffset.UtcNow;

        var saveResponse = await _request.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = modelId,
                name,
                processKey,
                bpmnXml,
                isDraft = true,
                draftVersionNumber = 1,
                createdAtUtc = now,
                updatedAtUtc = now
            }
        });
        await EnsureSuccessAsync(saveResponse, "save workflow");
        var saved = await saveResponse.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from POST /api/workflows/.");

        // Publish requires the full WorkflowModel body — relay what the server
        // just gave us (which is the canonical, normalized shape).
        var publishResponse = await _request.PostAsync(
            $"/api/workflows/{modelId}/publish",
            new APIRequestContextOptions { DataObject = saved });
        await EnsureSuccessAsync(publishResponse, "publish workflow");

        return new WorkflowDto(modelId, name, processKey);
    }

    /// <summary>
    /// Starts a workflow instance via <c>POST /api/workflows/{processKey}/start</c>
    /// with the given display name. The Flowable response is relayed through
    /// the endpoint; we read the new instance id off it so cancel/delete tests
    /// can target the row by name + id pair.
    /// </summary>
    public async Task<ExecutionDto> StartExecutionAsync(string processKey, string instanceName)
    {
        var response = await _request.PostAsync(
            $"/api/workflows/{processKey}/start",
            new APIRequestContextOptions { DataObject = new { name = instanceName } });
        await EnsureSuccessAsync(response, "start execution");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /start.");

        return new ExecutionDto(
            Id: json.GetProperty("id").GetString()!,
            Name: instanceName);
    }

    /// <summary>
    /// Creates a local user via <c>POST /api/users/</c>. Critical for Phase 10
    /// permission gating: because <c>superadmin_backfill_v1</c> runs once at
    /// app startup, users created <em>after</em> boot do <em>not</em> get
    /// SuperAdmin — they have no grants until something is explicitly created
    /// for them. The endpoint requires the caller to have <c>user:create</c>,
    /// so this should be invoked from the seeded admin's request context.
    /// </summary>
    public async Task<UserDto> CreateUserAsync(
        string username, string password,
        string firstName = "E2E", string lastName = "User",
        string? email = null)
    {
        var response = await _request.PostAsync("/api/users/", new APIRequestContextOptions
        {
            DataObject = new
            {
                username,
                firstName,
                lastName,
                password,
                email = email ?? $"{username}@e2e.local"
            }
        });
        await EnsureSuccessAsync(response, "create user");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/users/.");
        return new UserDto(
            UserId: json.GetProperty("userId").GetGuid(),
            Username: json.GetProperty("username").GetString()!);
    }

    /// <summary>
    /// Creates a permission grant via <c>POST /api/admin/grants</c>. The
    /// endpoint requires <c>siteconfig:edit</c>, so this also needs the
    /// admin's request context. <paramref name="selectorString"/> follows the
    /// shape <c>/recordtype/*</c> (kind-level) or <c>/record/{id}</c>
    /// (instance), with optional sub-paths for tagged predicates; see
    /// <c>AutoNate.Web.Tests/Authorization/SelectorParserTests.cs</c> for the
    /// canonical grammar.
    /// </summary>
    public async Task GrantAsync(
        string principalKind, Guid principalId,
        string action, string selectorString,
        string effect = "allow", int priority = 0)
    {
        var response = await _request.PostAsync("/api/admin/grants/", new APIRequestContextOptions
        {
            DataObject = new
            {
                principalKind,
                principalId = principalId.ToString(),
                action,
                selectorString,
                effect,
                priority
            }
        });
        await EnsureSuccessAsync(response, "create permission grant");
    }

    /// <summary>
    /// Adds a typed schema field to a record type via
    /// <c>POST /api/record-types/{id}/fields</c>.
    ///
    /// The record seeder deliberately creates schema-less types, which is all
    /// most specs need — but it meant no journey could exercise typed values,
    /// typed filters or the column picker, and E2E-061/062 sat BLOCKED on
    /// exactly this gap. <paramref name="config"/> is raw JSON so option
    /// fields can carry their choice list without this helper growing a
    /// per-type overload.
    /// </summary>
    public async Task<Guid> AddRecordTypeFieldAsync(
        Guid recordTypeId,
        string fieldKey,
        string displayName,
        string dataType,
        string configJson = "{}",
        bool isRequired = false,
        int sortOrder = 0)
    {
        using var config = System.Text.Json.JsonDocument.Parse(configJson);
        var response = await _request.PostAsync(
            $"/api/record-types/{recordTypeId}/fields",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    fieldKey,
                    displayName,
                    dataType,
                    config = config.RootElement,
                    isRequired,
                    sortOrder
                }
            });
        await EnsureSuccessAsync(response, $"create '{dataType}' field '{fieldKey}'");
        var json = await response.JsonAsync();
        return json!.Value.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Creates a content project via <c>POST /api/content/projects</c>. Any
    /// signed-in user can create one; the caller becomes the Owner in the
    /// same transaction so the project is immediately scoped + writeable.
    /// </summary>
    public async Task<ProjectDto> CreateProjectAsync(string name, string? description = null)
    {
        var response = await _request.PostAsync("/api/content/projects", new APIRequestContextOptions
        {
            DataObject = new { name, description }
        });
        await EnsureSuccessAsync(response, "create project");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/content/projects.");
        return new ProjectDto(
            Id: json.GetProperty("id").GetGuid(),
            Name: json.GetProperty("name").GetString()!);
    }

    /// <summary>
    /// Creates a document at the project root via
    /// <c>POST /api/content/documents</c>. <c>Kind</c> defaults server-side to
    /// <c>DocumentKinds.Document</c> when omitted, which is what we want for
    /// the smoke tests.
    /// </summary>
    public async Task<DocumentDto> CreateDocumentAsync(Guid projectId, string title)
    {
        var response = await _request.PostAsync("/api/content/documents", new APIRequestContextOptions
        {
            DataObject = new { projectId, title }
        });
        await EnsureSuccessAsync(response, "create document");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/content/documents.");
        return new DocumentDto(
            Id: json.GetProperty("id").GetGuid(),
            Title: json.GetProperty("title").GetString()!);
    }

    /// <summary>
    /// Creates a form via <c>POST /api/forms/</c> and returns its id + short
    /// code. The server populates <c>FormCode</c> from <c>DefaultFormCode</c>
    /// when the request omits it, so the freshly-created form already renders
    /// a "New form" heading via <c>JsxFormHost</c> at <c>/form/{shortCode}</c>
    /// once published.
    /// </summary>
    public async Task<FormDto> CreateFormAsync(string name, string shortCode, bool siteAvailable)
    {
        var response = await _request.PostAsync("/api/forms/", new APIRequestContextOptions
        {
            DataObject = new
            {
                name,
                shortCode,
                siteAvailable
            }
        });
        await EnsureSuccessAsync(response, "create form");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/forms/.");

        return new FormDto(
            Id: json.GetProperty("id").GetGuid(),
            ShortCode: json.GetProperty("shortCode").GetString()!,
            Name: json.GetProperty("name").GetString()!);
    }

    /// <summary>
    /// Publishes a form via <c>POST /api/forms/{id}/publish</c>. The publish
    /// endpoint takes no body — it deploys the current draft as the published
    /// snapshot the public view loads.
    /// </summary>
    public async Task PublishFormAsync(Guid formId)
    {
        var response = await _request.PostAsync($"/api/forms/{formId}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        await EnsureSuccessAsync(response, "publish form");
    }

    /// <summary>
    /// Mounts the built-in dashboard template at a unique path. Fresh installs
    /// register the template but deliberately do not place it on a menu, so UI
    /// tests create their own mount before exercising dashboard behavior.
    /// </summary>
    public async Task CreateDashboardMountAsync(string path)
    {
        var response = await _request.PostAsync("/api/admin/menus/standalone/items",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    parentId = (Guid?)null,
                    sortOrder = 0,
                    displayName = TestNames.Prefixed("dashboard-mount"),
                    icon = (string?)null,
                    itemType = "template",
                    config = JsonSerializer.SerializeToElement(new
                    {
                        templateKey = "dashboard",
                        path,
                        isUserConfigurable = true
                    }),
                    permissionRequired = (string?)null,
                    isVisible = true
                }
            });
        await EnsureSuccessAsync(response, "create dashboard mount");
    }

    private static string MinimalUserTaskBpmn(string processKey, string name, string? assignee)
    {
        var assigneeAttribute = string.IsNullOrWhiteSpace(assignee)
            ? string.Empty
            : $" flowable:assignee=\"{System.Security.SecurityElement.Escape(assignee)}\"";
        return $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="{{name}}" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:userTask id="UserTask_1" name="Review"{{assigneeAttribute}}>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="EndEvent_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;
    }

    private static async Task EnsureSuccessAsync(IAPIResponse response, string action)
    {
        if (response.Ok) return;

        var body = await SafeReadBodyAsync(response);
        throw new InvalidOperationException(
            $"E2E API seeder failed to {action}: HTTP {response.Status} {response.StatusText}. " +
            $"Body: {body}");
    }

    private static async Task<string> SafeReadBodyAsync(IAPIResponse response)
    {
        try { return await response.TextAsync(); }
        catch { return "<unreadable>"; }
    }
}

/// <summary>
/// Minimal projection of <c>RecordTypeDto</c> from the SPA's perspective —
/// just the fields tests actually use. The full DTO has more shape (icon,
/// color, isSystem, …); add fields here when a test needs them.
/// </summary>
public sealed record RecordTypeDto(Guid Id, string ShortCode, string Name);

/// <summary>
/// Minimal projection of <c>RecordDto</c>. <c>Key</c> is the human-readable
/// composite ("E3F8C1-1") that drives /record/{key} routing in the SPA.
/// </summary>
public sealed record RecordDto(Guid Id, string Key, string Name);

/// <summary>
/// Minimal handle for a saved+published workflow model.
/// </summary>
public sealed record WorkflowDto(Guid Id, string Name, string ProcessKey);

/// <summary>
/// Minimal handle for a started workflow execution (process instance). The
/// <c>Id</c> is the Flowable process-instance id (a string).
/// </summary>
public sealed record ExecutionDto(string Id, string Name);

/// <summary>
/// Minimal handle for a created form. <c>ShortCode</c> drives the
/// <c>/form/{shortCode}</c> and <c>/formdev/{shortCode}</c> routes.
/// </summary>
public sealed record FormDto(Guid Id, string ShortCode, string Name);

/// <summary>
/// Minimal handle for a created local user — <c>UserId</c> is the principal
/// id used in permission grants.
/// </summary>
public sealed record UserDto(Guid UserId, string Username);

/// <summary>
/// Minimal handle for a created content project. Drives
/// <c>/documents/p/{id}</c> and the notes-side cabinets / notebooks tree.
/// </summary>
public sealed record ProjectDto(Guid Id, string Name);

/// <summary>
/// Minimal handle for a created document. Drives <c>/documents/edit/{id}</c>.
/// </summary>
public sealed record DocumentDto(Guid Id, string Title);
