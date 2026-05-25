using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 2 entity-channel coverage. Two-axis:
//   - Subscribe-time gate: own/other user, view-grant present/absent,
//     superadmin short-circuit, malformed shapes.
//   - Per-message fan-out: GateTarget-bearing deliveries respect IAuthorizer
//     view checks (only allowed actors receive the frame).
[Trait("Category", "Integration")]
public sealed class BusSubscriptionChannelTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Route = "/ws/bus-watcher";

    // Full enforcement so AuthorizeAsync point decisions actually fire — the
    // read-only mode used elsewhere short-circuits to Allow, hiding the
    // subscribe-time / per-message gating.
    private static readonly IReadOnlyDictionary<string, string?> AuthFull = new Dictionary<string, string?>
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
    };

    // ---------- Record channel ----------

    [Fact]
    public async Task Record_Instance_NonSuperAdmin_NoGrant_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "r-1", new[] { $"record:{Guid.NewGuid()}" }, cts.Token);

        AssertOnlyRejected(ack, expectedCode: "forbidden");
    }

    [Fact]
    public async Task Record_Instance_SuperAdmin_Accepted_AndDeliversMatchingEvent()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        var recordId = Guid.NewGuid();
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "r-2", new[] { $"record:{recordId}" }, cts.Token);
        AssertSubscribed(ack, $"record:{recordId}");

        var payload = JsonSerializer.Serialize(new { recordId, assigneeIds = Array.Empty<Guid>() });
        await PublishBusMessageAsync(factory, DaprRecordEventPublisher.TopicName, payload);

        var evt = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("event", evt.GetProperty("type").GetString());
        Assert.Equal($"record:{recordId}", evt.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Records_AssignedTo_OwnUserId_Accepted_DoesNotDeliverOtherAssignees()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "r-3", new[] { $"records:assigned-to:{AdminUserId}" }, cts.Token);
        AssertSubscribed(ack, $"records:assigned-to:{AdminUserId}");

        // Event whose only assignee is someone else — must not reach us.
        var otherAssignee = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            recordId = Guid.NewGuid(),
            assigneeIds = new[] { otherAssignee },
        });
        await PublishBusMessageAsync(factory, DaprRecordEventPublisher.TopicName, payload);

        await AssertNoFrameAsync(ws);
    }

    [Fact]
    public async Task Records_AssignedTo_OtherUserId_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var otherUser = Guid.NewGuid();
        var ack = await SubscribeAsync(ws, "r-4", new[] { $"records:assigned-to:{otherUser}" }, cts.Token);

        AssertOnlyRejected(ack, expectedCode: "forbidden");
    }

    [Fact]
    public async Task Records_Visible_Accepted_AnyAuthenticatedActor()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "r-5", new[] { "records:visible" }, cts.Token);
        AssertSubscribed(ack, "records:visible");
    }

    [Fact]
    public async Task Records_Visible_NonSuperAdmin_NoGrant_DropsFanout()
    {
        // Admin is auto-logged-in but not promoted to SuperAdmin; they have no
        // record view grants. records:visible subscribe is accepted but per-
        // message GateTarget drops every event.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "r-6", new[] { "records:visible" }, cts.Token);
        AssertSubscribed(ack, "records:visible");

        var payload = JsonSerializer.Serialize(new
        {
            recordId = Guid.NewGuid(),
            assigneeIds = Array.Empty<Guid>(),
        });
        await PublishBusMessageAsync(factory, DaprRecordEventPublisher.TopicName, payload);

        await AssertNoFrameAsync(ws);
    }

    // ---------- Workflow channels ----------

    [Fact]
    public async Task WorkflowExecution_Instance_SuperAdmin_DeliversEvent()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        var executionId = "proc-1";
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "w-1", new[] { $"workflow-execution:{executionId}" }, cts.Token);
        AssertSubscribed(ack, $"workflow-execution:{executionId}");

        var payload = JsonSerializer.Serialize(new { eventType = "process.completed", processInstanceId = executionId });
        await PublishBusMessageAsync(factory, BusWatcherStreamService.TopicName, payload);

        var evt = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal($"workflow-execution:{executionId}", evt.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task WorkflowExecutions_Visible_AnyAuthenticatedActor()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "w-2", new[] { "workflow-executions:visible" }, cts.Token);
        AssertSubscribed(ack, "workflow-executions:visible");
    }

    [Fact]
    public async Task WorkflowTasks_AssignedTo_OwnUserId_DeliversTaskEvent()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var channel = $"workflow-tasks:assigned-to:{AdminUserId}";
        var ack = await SubscribeAsync(ws, "w-3", new[] { channel }, cts.Token);
        AssertSubscribed(ack, channel);

        var payload = JsonSerializer.Serialize(new
        {
            eventType = "task.assigned",
            taskId = "task-1",
            processInstanceId = "proc-1",
            assignee = AdminUserId.ToString(),
        });
        await PublishBusMessageAsync(factory, BusWatcherStreamService.TopicName, payload);

        var evt = await ReceiveFirstMatchingAsync(ws, channel, cts.Token);
        Assert.Equal(channel, evt.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Tasks_SuperviseesOfMe_NoSupervisorEdges_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        // Not promoted to SuperAdmin and no supervisor edges loaded.
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "w-4", new[] { "tasks:supervisees-of-me" }, cts.Token);
        AssertOnlyRejected(ack, expectedCode: "forbidden");
    }

    [Fact]
    public async Task Tasks_SuperviseesOfMe_WithEdge_DeliversForSupervisee()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        // Add a supervisor edge admin -> supervisee so the FastGate fires.
        var supervisee = Guid.NewGuid();
        await using (var db = factory.Database.CreateDbContext())
        {
            db.EntityEdges.Add(new EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = EdgeKinds.Supervisor,
                FromKind = EntityKinds.User,
                FromId = AdminUserId.ToString(),
                ToKind = EntityKinds.User,
                ToId = supervisee.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId,
            });
            await db.SaveChangesAsync();
        }

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "w-5", new[] { "tasks:supervisees-of-me" }, cts.Token);
        AssertSubscribed(ack, "tasks:supervisees-of-me");

        var payload = JsonSerializer.Serialize(new
        {
            eventType = "task.assigned",
            taskId = "task-2",
            processInstanceId = "proc-2",
            assignee = supervisee.ToString(),
        });
        await PublishBusMessageAsync(factory, BusWatcherStreamService.TopicName, payload);

        var evt = await ReceiveFirstMatchingAsync(ws, "tasks:supervisees-of-me", cts.Token);
        Assert.Equal("tasks:supervisees-of-me", evt.GetProperty("channel").GetString());
    }

    // ---------- Page channel ----------

    [Fact]
    public async Task Page_Instance_NonSuperAdmin_NoGrant_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "p-1", new[] { $"page:{Guid.NewGuid()}" }, cts.Token);

        AssertOnlyRejected(ack, expectedCode: "forbidden");
    }

    [Fact]
    public async Task Page_Instance_SuperAdmin_DeliversEvent()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);

        var pageId = Guid.NewGuid();
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "p-2", new[] { $"page:{pageId}" }, cts.Token);
        AssertSubscribed(ack, $"page:{pageId}");

        var payload = JsonSerializer.Serialize(new
        {
            eventType = "content.page.updated",
            resourceKind = "page",
            resource = new { id = pageId.ToString() },
        });
        await PublishBusMessageAsync(factory, ContentEventTopic.TopicName, payload);

        var evt = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal($"page:{pageId}", evt.GetProperty("channel").GetString());
    }

    // ---------- External connection ----------

    [Fact]
    public async Task ExternalConnection_Instance_NonSuperAdmin_NoGrant_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "e-1", new[] { $"external-connection:{Guid.NewGuid()}" }, cts.Token);

        AssertOnlyRejected(ack, expectedCode: "forbidden");
    }

    // ---------- Project / cabinet / notebook ancestor fan-out ----------

    [Fact]
    public async Task Cabinet_Project_Notebook_Rejected_For_NonSuperAdmin_Without_View()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        foreach (var channel in new[]
        {
            $"project:{Guid.NewGuid()}",
            $"cabinet:{Guid.NewGuid()}",
            $"notebook:{Guid.NewGuid()}",
        })
        {
            var ack = await SubscribeAsync(ws, channel, new[] { channel }, cts.Token);
            AssertOnlyRejected(ack, expectedCode: "forbidden");
        }
    }

    [Fact]
    public async Task NotebookCreated_Event_FansOutToCabinetAndProjectChannels()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthFull);
        await PromoteToSuperAdmin(factory);
        var (projectId, cabinetId, notebookId) = await SeedProjectCabinetNotebookAsync(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var channels = new[]
        {
            $"project:{projectId}",
            $"cabinet:{cabinetId}",
            $"notebook:{notebookId}",
        };
        var ack = await SubscribeAsync(ws, "ancestor-fanout", channels, cts.Token);
        foreach (var c in channels) AssertSubscribed(ack, c);

        // NotebookCreated event for the seeded notebook should fan out to:
        //   notebook:{notebookId} (the leaf itself)
        //   cabinet:{cabinetId}   (closure ancestor)
        //   project:{projectId}   (closure ancestor)
        var payload = JsonSerializer.Serialize(new
        {
            eventType = "content.notebook.created",
            resourceKind = "notebook",
            resource = new { id = notebookId.ToString(), cabinetId = cabinetId.ToString(), name = "Test Notebook" },
        });
        await PublishBusMessageAsync(factory, ContentEventTopic.TopicName, payload);

        var received = new HashSet<string>(StringComparer.Ordinal);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (received.Count < 3 && DateTimeOffset.UtcNow < deadline)
        {
            var frame = await ReceiveJsonAsync(ws, cts.Token);
            if (frame.GetProperty("type").GetString() == "event")
            {
                received.Add(frame.GetProperty("channel").GetString()!);
            }
        }
        Assert.Contains($"notebook:{notebookId}", received);
        Assert.Contains($"cabinet:{cabinetId}", received);
        Assert.Contains($"project:{projectId}", received);
    }

    private static async Task<(Guid ProjectId, Guid CabinetId, Guid NotebookId)>
        SeedProjectCabinetNotebookAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "FanoutProject",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = AdminUserId,
            UpdatedBy = AdminUserId,
            DeletionsLocked = false,
            IsArchived = false,
        };
        var cabinet = new Cabinet
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "FanoutCabinet",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = AdminUserId,
            UpdatedBy = AdminUserId,
            IsArchived = false,
        };
        var notebook = new Notebook
        {
            Id = Guid.NewGuid(),
            CabinetId = cabinet.Id,
            Name = "FanoutNotebook",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = AdminUserId,
            UpdatedBy = AdminUserId,
            IsArchived = false,
        };
        db.Projects.Add(project);
        db.Cabinets.Add(cabinet);
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, default);
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Cabinet, cabinet.Id, default);
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Notebook, notebook.Id, default);
        return (project.Id, cabinet.Id, notebook.Id);
    }

    // ---------- helpers ----------

    private static CancellationTokenSource TestTimeout() =>
        new(TimeSpan.FromSeconds(5));

    private static async Task<WebSocket> ConnectAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, Route);
        return await client.ConnectAsync(uri, CancellationToken.None);
    }

    private static async Task PromoteToSuperAdmin(AutoNateWebApplicationFactory factory)
    {
        var assignments = factory.Database.CreateRoleAssignmentStore();
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            SystemRoles.SuperAdminId, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
    }

    private static Task PublishBusMessageAsync(AutoNateWebApplicationFactory factory, string topic, string payload)
    {
        var bus = factory.Services.GetRequiredService<BusWatcherStreamService>();
        var message = new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            topic,
            "application/json",
            new Dictionary<string, string>(),
            payload);
        return bus.PublishAsync(message, CancellationToken.None);
    }

    private static async Task<JsonElement> SubscribeAsync(
        WebSocket ws, string id, IReadOnlyList<string> channels, CancellationToken ct)
    {
        await SendJsonAsync(ws, new { type = "subscribe", id, channels }, ct);
        var ack = await ReceiveJsonAsync(ws, ct);
        Assert.Equal("ack", ack.GetProperty("type").GetString());
        Assert.Equal(id, ack.GetProperty("id").GetString());
        return ack;
    }

    private static void AssertSubscribed(JsonElement ack, string channel)
    {
        var subscribed = ack.GetProperty("subscribed");
        Assert.Contains(channel, subscribed.EnumerateArray().Select(e => e.GetString()!));
        Assert.False(ack.TryGetProperty("rejected", out _));
    }

    private static void AssertOnlyRejected(JsonElement ack, string expectedCode)
    {
        Assert.False(ack.TryGetProperty("subscribed", out _));
        var rejected = ack.GetProperty("rejected");
        Assert.Equal(1, rejected.GetArrayLength());
        Assert.Equal(expectedCode, rejected[0].GetProperty("code").GetString());
    }

    private static async Task AssertNoFrameAsync(WebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var frame = await ReceiveJsonAsync(ws, cts.Token);
            Assert.Fail($"expected no frame but received: {frame.GetRawText()}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Per-message fan-out for a single bus event can produce multiple frames
    // (e.g. workflow events emit instance + list + assigned-to deliveries);
    // scan for the one matching the channel under test.
    private static async Task<JsonElement> ReceiveFirstMatchingAsync(WebSocket ws, string channel, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var frame = await ReceiveJsonAsync(ws, ct);
            if (frame.TryGetProperty("channel", out var c)
                && string.Equals(c.GetString(), channel, StringComparison.Ordinal))
            {
                return frame;
            }
        }
        throw new InvalidOperationException($"no frame for channel '{channel}' arrived in time");
    }

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket ws, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            await ms.WriteAsync(buffer.AsMemory(0, result.Count), ct);
        } while (!result.EndOfMessage);
        var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
        return doc.RootElement.Clone();
    }
}
