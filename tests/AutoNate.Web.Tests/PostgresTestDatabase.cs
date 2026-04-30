using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EntityTypes;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Configuration;
using AutoNate.Web.Hooks;
using AutoNate.Web.Persistence;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Tests;

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName)
    {
        _databaseName = databaseName;
    }

    public string ConnectionString =>
        $"Host=localhost;Port=5432;Database={_databaseName};Username=autonate;Password=Your_password123!";

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var database = new PostgresTestDatabase($"autonate_test_{Guid.NewGuid():N}");
        await database.InitializeAsync();
        return database;
    }

    public EfCoreLocalUserStore CreateLocalUserStore() => new(CreateDbContextFactory());

    public EfCoreWorkflowModelStore CreateWorkflowStore() =>
        CreateWorkflowStore(new RecordingWorkflowSignalRegistry(), new NoopStreamingSubscriber());

    public EfCoreWorkflowModelStore CreateWorkflowStore(
        IWorkflowSignalRegistry signalRegistry,
        IDaprStreamingSubscriber streamingSubscriber) =>
        new(CreateDbContextFactory(), signalRegistry, streamingSubscriber);

    // Minimal in-memory test double. Counts RefreshAsync invocations so tests
    // can assert that publish triggers a refresh; doesn't actually parse XML.
    internal sealed class RecordingWorkflowSignalRegistry : IWorkflowSignalRegistry
    {
        private static readonly IReadOnlySet<string> Empty =
            new HashSet<string>(StringComparer.Ordinal);

        public int RefreshCount { get; private set; }

        public IReadOnlyCollection<string> GetSubscribedTopics() => Array.Empty<string>();

        public IReadOnlySet<string> GetSignalNamesForTopic(string topic) => Empty;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    internal sealed class NoopStreamingSubscriber : IDaprStreamingSubscriber
    {
        public int SyncCount { get; private set; }

        public Task SyncAsync(CancellationToken cancellationToken)
        {
            SyncCount++;
            return Task.CompletedTask;
        }
    }

    public EfCoreRecordTypeStore CreateRecordTypeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordStore CreateRecordStore() =>
        CreateRecordStore(authorizationEnabled: false);

    public EfCoreRecordStore CreateRecordStore(
        bool authorizationEnabled,
        string enforcement = AuthorizationEnforcement.Off,
        IRecordEventPublisher? eventPublisher = null,
        INotificationStore? notificationStore = null)
    {
        var authorizer = CreateAuthorizer(authorizationEnabled, enforcement);
        var daprOptions = Options.Create(new DaprOptions { AppId = "autonate.web.tests" });
        return new EfCoreRecordStore(
            CreateDbContextFactory(),
            BuildDefaultFieldTypeRegistry(),
            new EntityEdgeWriter(),
            authorizer,
            eventPublisher ?? new NoopRecordEventPublisher(),
            notificationStore ?? new RecordingNotificationStore(),
            NullLogger<EfCoreRecordStore>.Instance,
            daprOptions);
    }

    // Captures notifications so tests can assert assignment-driven creation
    // without going through the EF store. The real INotificationStore writes
    // to its own table; tests typically don't care.
    public sealed class RecordingNotificationStore : INotificationStore
    {
        private readonly List<Notification> _notifications = new();

        public IReadOnlyList<Notification> Notifications => _notifications;

        public Task<Notification> CreateAsync(CreateNotificationInput input, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var n = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = input.UserId,
                Kind = input.Kind,
                Title = input.Title,
                Body = input.Body,
                RelatedEntityKind = input.RelatedEntityKind,
                RelatedEntityId = input.RelatedEntityId,
                LinkPath = input.LinkPath,
                IsRead = false,
                CreatedAtUtc = now
            };
            _notifications.Add(n);
            return Task.FromResult(n);
        }

        public Task<IReadOnlyList<Notification>> ListForUserAsync(Guid userId, int? limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(_notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(limit ?? int.MaxValue)
                .ToList());

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_notifications.Count(n => n.UserId == userId && !n.IsRead));

        public Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Notification?>(_notifications.FirstOrDefault(n => n.Id == notificationId && n.UserId == userId));

        public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    // Captures every published event so tests can assert publication shape.
    public sealed class RecordingRecordEventPublisher : IRecordEventPublisher
    {
        private readonly List<RecordEventEnvelope> _events = new();

        public IReadOnlyList<RecordEventEnvelope> Events => _events;

        public Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _events.Add(envelope);
            return Task.CompletedTask;
        }
    }

    public EntityEdgeReconciler CreateEdgeReconciler() => new(CreateDbContextFactory());

    public IEntityEdgeWriter CreateEdgeWriter() => new EntityEdgeWriter();

    private AuthCacheBumper CreateBumper() => new(CreateDbContextFactory());

    public EfCoreRoleStore CreateRoleStore() => CreateRoleStore(authorizationEnabled: false);

    public EfCoreRoleStore CreateRoleStore(bool authorizationEnabled, string enforcement = AuthorizationEnforcement.Off) =>
        new(CreateDbContextFactory(), CreateBumper(), CreateAuthorizer(authorizationEnabled, enforcement));

    public EfCoreGroupStore CreateGroupStore() => CreateGroupStore(authorizationEnabled: false);

    public EfCoreGroupStore CreateGroupStore(bool authorizationEnabled, string enforcement = AuthorizationEnforcement.Off) =>
        new(CreateDbContextFactory(), CreateBumper(), CreateAuthorizer(authorizationEnabled, enforcement));

    public EfCoreRoleAssignmentStore CreateRoleAssignmentStore() =>
        new(CreateDbContextFactory(), CreateBumper());

    public EfCorePermissionGrantStore CreatePermissionGrantStore() =>
        new(CreateDbContextFactory(), CreateBumper());

    public IAuthorizer CreateAuthorizer(
        bool enabled,
        string enforcement = AuthorizationEnforcement.ReadOnly,
        bool dryRun = false)
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var compilers = new SelectorCompilerRegistry(new ISelectorCompiler[]
        {
            new RecordSelectorCompiler(),
            new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Role>(EntityKinds.Role, x => x.Id),
            new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Group>(EntityKinds.Group, x => x.Id),
            new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.RecordType>(EntityKinds.RecordType, x => x.Id),
            new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.WorkflowModel>(EntityKinds.WorkflowModel, x => x.Id)
        });
        var dbFactory = CreateDbContextFactory();
        var instanceAuthorizers = new IInstanceAuthorizer[]
        {
            new RecordInstanceAuthorizer(dbFactory),
            new RoleInstanceAuthorizer(dbFactory),
            new GroupInstanceAuthorizer(dbFactory),
            new RecordTypeInstanceAuthorizer(dbFactory),
            new WorkflowModelInstanceAuthorizer(dbFactory)
        };
        var options = Options.Create(new AuthorizationOptions
        {
            Enabled = enabled,
            Enforcement = enforcement,
            DryRun = dryRun
        });
        return new Authorizer(
            CreateDbContextFactory(), options, registry, compilers, instanceAuthorizers,
            new HookRegistrar(NullLogger<ActionHub>.Instance).Filters,
            NullLogger<Authorizer>.Instance);
    }

    public EfCoreRecordHistoryStore CreateRecordHistoryStore() =>
        new(CreateDbContextFactory());

    public EfCoreRecordEdgeTypeStore CreateRecordEdgeTypeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordEdgeStore CreateRecordEdgeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordCommentStore CreateRecordCommentStore() =>
        new(CreateDbContextFactory());

    public AutoNate.Web.Services.Menus.EfCorePageTemplateStore CreatePageTemplateStore() =>
        new(CreateDbContextFactory());

    public AutoNate.Web.Services.Menus.EfCoreMenuStore CreateMenuStore(bool authorizationEnabled = false) =>
        new(CreateDbContextFactory(), CreateAuthorizer(authorizationEnabled));

    public static IFieldTypeRegistry BuildDefaultFieldTypeRegistry() =>
        new FieldTypeRegistry(new IFieldType[]
        {
            new TextFieldType(),
            new NumberFieldType(),
            new DateFieldType(),
            new PhoneFieldType(),
            new EmailFieldType(),
            new OptionFieldType(),
            new BooleanFieldType()
        });

    public AutoNateDbContext CreateDbContext() => CreateDbContextFactory().CreateDbContext();

    public async ValueTask DisposeAsync()
    {
        await using var adminConnection = new NpgsqlConnection(AdminConnectionString("postgres"));
        await adminConnection.OpenAsync();

        await using (var terminateCommand = adminConnection.CreateCommand())
        {
            terminateCommand.CommandText =
                """
                select pg_terminate_backend(pid)
                from pg_stat_activity
                where datname = @databaseName
                  and pid <> pg_backend_pid();
                """;
            terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"drop database if exists \"{_databaseName}\";";
        await dropCommand.ExecuteNonQueryAsync();
    }

    private async Task InitializeAsync()
    {
        await using (var adminConnection = new NpgsqlConnection(AdminConnectionString("postgres")))
        {
            await adminConnection.OpenAsync();
            await using var createDatabaseCommand = adminConnection.CreateCommand();
            createDatabaseCommand.CommandText = $"create database \"{_databaseName}\";";
            await createDatabaseCommand.ExecuteNonQueryAsync();
        }

        var bootstrapScripts = new[]
        {
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "02-create-autonate-app-schema.sql"))
        };

        await using var databaseConnection = new NpgsqlConnection(ConnectionString);
        await databaseConnection.OpenAsync();

        foreach (var bootstrapScript in bootstrapScripts)
        {
            await using var bootstrapCommand = databaseConnection.CreateCommand();
            bootstrapCommand.CommandText = bootstrapScript;
            await bootstrapCommand.ExecuteNonQueryAsync();
        }
    }

    public IDbContextFactory<AutoNateDbContext> CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AutoNateDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new SimpleDbContextFactory(options);
    }

    private static string AdminConnectionString(string databaseName) =>
        $"Host=localhost;Port=5432;Database={databaseName};Username=autonate;Password=Your_password123!";

    private sealed class SimpleDbContextFactory(DbContextOptions<AutoNateDbContext> options)
        : IDbContextFactory<AutoNateDbContext>
    {
        public AutoNateDbContext CreateDbContext() => new(options);

        public Task<AutoNateDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
