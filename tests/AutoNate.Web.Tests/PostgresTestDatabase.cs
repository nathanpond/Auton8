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
using AutoNate.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Tests;

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    // Falls back to the docker-compose default so tests work out of the box;
    // overridable via env var so a developer rotating the local dev secret
    // doesn't have to grep through the test project to update it.
    private static readonly string Password =
        Environment.GetEnvironmentVariable("AUTONATE_POSTGRES_PASSWORD") ?? "Your_password123!";

    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName)
    {
        _databaseName = databaseName;
    }

    // Bounded pool per test database.
    //
    // Every test class builds its own database, so it also gets its own
    // connection-string and therefore its own Npgsql pool — and the default
    // maximum is 100 *per pool*. With xunit running classes in parallel that
    // multiplies past Postgres's own max_connections (100 by default), and the
    // suite fails with "53300: sorry, too many clients already" on whichever
    // test happens to ask for connections at the wrong moment. It surfaced on
    // CreateAsync_KeysAreSequentialUnderConcurrency, which opens twenty at
    // once, but the cause was suite-wide rather than anything about that test.
    //
    // Ten is comfortably above what any single class needs concurrently; the
    // twenty-way test simply queues for a free connection instead of opening a
    // twenty-first. The idle settings return connections to the server quickly
    // so a finished class stops holding any.
    private const string PoolTuning =
        "Maximum Pool Size=10;Connection Idle Lifetime=15;Connection Pruning Interval=5";

    public string ConnectionString =>
        $"Host=localhost;Port=5432;Database={_databaseName};Username=autonate;Password={Password};{PoolTuning}";

    // The `admin` account most suites expect to find.
    //
    // It used to arrive from the init script replayed in InitializeAsync,
    // which shipped the row with its password_hash and password_salt committed
    // to the repository. That seed is gone; the application now creates its
    // first administrator at startup from configuration. Tests that boot the
    // host get theirs that way — see AutoNateWebApplicationFactory — but the
    // many suites that talk to the database directly never start a host, so
    // the row has to come from somewhere. Here, in test code, is the right
    // somewhere: the credential is a test fixture, not a product default.
    //
    // The id is pinned to the value the old seed used because roughly twenty
    // suites assert against it.
    public static readonly Guid SeededAdminUserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string SeededAdminUsername = "admin";
    public const string SeededAdminPassword = "admin";

    public static async Task<PostgresTestDatabase> CreateAsync(bool seedLocalAdmin = true)
    {
        var database = new PostgresTestDatabase($"autonate_test_{Guid.NewGuid():N}");
        await database.InitializeAsync();
        if (seedLocalAdmin)
        {
            await database.SeedLocalAdminAsync();
        }
        return database;
    }

    // Hashed rather than pasted: a stored hash in the tree is what the removed
    // seed got wrong, and there is no reason to reintroduce one even in test
    // code. Suites that sign in as admin/admin still work, because this uses
    // the same hasher the login path verifies with.
    private async Task SeedLocalAdminAsync()
    {
        var (hash, salt) = PasswordHasher.HashPassword(SeededAdminPassword);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO local_users (
                username, password_hash, password_salt, email,
                first_name, last_name, user_id, created_date, last_login_date, idp_key)
            VALUES (
                @username, @hash, @salt, 'admin@localhost',
                'Admin', 'User', @userId, NOW(), NULL, 'local-admin')
            ON CONFLICT (username) DO NOTHING;
            """;
        command.Parameters.AddWithValue("username", SeededAdminUsername);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("salt", salt);
        command.Parameters.AddWithValue("userId", SeededAdminUserId);
        await command.ExecuteNonQueryAsync();
    }

    public EfCoreLocalUserStore CreateLocalUserStore()
    {
        // A cache of its own per store, so a test that writes users cannot be
        // served another test's snapshot (#9).
        var factory = CreateDbContextFactory();
        return new EfCoreLocalUserStore(
            factory,
            new UserDirectorySnapshotCache(
                factory,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UserDirectorySnapshotCache>.Instance));
    }

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

        private static readonly IReadOnlyList<WorkflowSignalRegistration> EmptyRegistrations =
            Array.Empty<WorkflowSignalRegistration>();

        public int RefreshCount { get; private set; }

        public IReadOnlyCollection<string> GetSubscribedTopics() => Array.Empty<string>();

        public IReadOnlySet<string> GetSignalNamesForTopic(string topic) => Empty;

        public IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic) =>
            EmptyRegistrations;

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
                ParentEntityKind = input.ParentEntityKind,
                ParentEntityId = input.ParentEntityId,
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

        public Task<NotificationPage> ListPagedForUserAsync(Guid userId, ListNotificationsRequest request, CancellationToken cancellationToken = default)
        {
            var all = _notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAtUtc)
                .ToList();
            var unreadCount = all.Count(n => !n.IsRead);
            var filtered = request.UnreadOnly ? all.Where(n => !n.IsRead).ToList() : all;
            var page = filtered
                .Skip(request.Page * request.PageSize)
                .Take(request.PageSize)
                .ToList();
            return Task.FromResult(new NotificationPage(page, filtered.Count, unreadCount));
        }

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_notifications.Count(n => n.UserId == userId && !n.IsRead));

        public Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Notification?>(_notifications.FirstOrDefault(n => n.Id == notificationId && n.UserId == userId));

        public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<Notification>> DeleteByRelatedEntityAsync(
            Guid? userId,
            string relatedEntityKind,
            string relatedEntityId,
            CancellationToken cancellationToken = default)
        {
            var matched = _notifications
                .Where(n => n.RelatedEntityKind == relatedEntityKind
                            && n.RelatedEntityId == relatedEntityId
                            && (!userId.HasValue || n.UserId == userId.Value))
                .ToList();
            foreach (var n in matched)
            {
                _notifications.Remove(n);
            }
            return Task.FromResult<IReadOnlyList<Notification>>(matched);
        }

        public Task<IReadOnlyList<Notification>> DeleteByParentEntityAsync(
            string parentEntityKind,
            string parentEntityId,
            CancellationToken cancellationToken = default)
        {
            var matched = _notifications
                .Where(n => n.ParentEntityKind == parentEntityKind
                            && n.ParentEntityId == parentEntityId)
                .ToList();
            foreach (var n in matched)
            {
                _notifications.Remove(n);
            }
            return Task.FromResult<IReadOnlyList<Notification>>(matched);
        }
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

    public static IEntityEdgeWriter CreateEdgeWriter() => new EntityEdgeWriter();


    public EfCoreRoleStore CreateRoleStore() => CreateRoleStore(authorizationEnabled: false);

    public EfCoreRoleStore CreateRoleStore(bool authorizationEnabled, string enforcement = AuthorizationEnforcement.Off) =>
        new(CreateDbContextFactory(), CreateAuthorizer(authorizationEnabled, enforcement));

    public EfCoreGroupStore CreateGroupStore() => CreateGroupStore(authorizationEnabled: false);

    public EfCoreGroupStore CreateGroupStore(bool authorizationEnabled, string enforcement = AuthorizationEnforcement.Off) =>
        new(CreateDbContextFactory(), CreateAuthorizer(authorizationEnabled, enforcement));

    public EfCoreRoleAssignmentStore CreateRoleAssignmentStore() =>
        new(CreateDbContextFactory());

    public EfCorePermissionGrantStore CreatePermissionGrantStore() =>
        new(CreateDbContextFactory());

    public IAuthorizer CreateAuthorizer(
        bool enabled,
        string enforcement = AuthorizationEnforcement.ReadOnly,
        bool dryRun = false)
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var compilers = new SelectorCompilerRegistry(new ISelectorCompiler[]
        {
            new RecordSelectorCompiler(),
            new RoleSelectorCompiler(),
            new GroupSelectorCompiler(),
            new RecordTypeSelectorCompiler(),
            new WorkflowModelSelectorCompiler()
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
            EmptyRecordTypeShortCodeResolver.Instance,
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
        new(CreateDbContextFactory(), CreateTestDataPaths(out var dataOptions), Options.Create(dataOptions));

    private static IDataPaths CreateTestDataPaths(out DataOptions dataOptions)
    {
        // Per-call temp root keeps tests isolated; the directory is fine to
        // leak — xUnit's TempPath is swept periodically by the OS.
        var contentRoot = Path.Combine(Path.GetTempPath(), "autonate-pgtests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        dataOptions = new DataOptions { Root = "data", PublicUrlPrefix = "/files" };
        return new DataPaths(Options.Create(dataOptions), new TestHostEnvironment(contentRoot));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
        }

        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "AutoNate.Web.Tests";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }

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
        // Retry the create on 23505.
        //
        // The name is a fresh GUID, so this is never an actual name clash:
        // PostgreSQL inserts into pg_database without holding a lock that
        // serialises concurrent creates, and two CREATE DATABASE statements
        // running at the same moment can both fail the unique index on
        // datname. Every test class here builds its own database, so a run
        // with enough parallelism hits it — rare on a developer machine,
        // reproducible on CI, which is where it first appeared.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var adminConnection =
                    new NpgsqlConnection(AdminConnectionString("postgres"));
                await adminConnection.OpenAsync();
                await using var createDatabaseCommand = adminConnection.CreateCommand();
                createDatabaseCommand.CommandText = $"create database \"{_databaseName}\";";
                await createDatabaseCommand.ExecuteNonQueryAsync();
                break;
            }
            catch (PostgresException ex) when (ex.SqlState == "23505" && attempt < 5)
            {
                // Back off a little so the colliding creates don't line up
                // again on the retry.
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }

        var bootstrapScripts = new[]
        {
            // The base schema, read from the same embedded resource the
            // application applies. Not a build-copied file: a duplicate in
            // bin/ can drift, and this fixture setting up different bytes than
            // EnsureAsync applies is exactly the divergence the single-source
            // move exists to prevent.
            DatabaseSchemaInitializer.ReadBaseSchemaSql()
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

    // Pooling off: these are short administrative connections to the
    // maintenance database, one per create/drop. Pooling them would hold
    // connections open against the same server budget the tuning above exists
    // to protect.
    private static string AdminConnectionString(string databaseName) =>
        $"Host=localhost;Port=5432;Database={databaseName};Username=autonate;Password={Password};Pooling=false";

    // Same string, for tests that need a server-level connection of their own
    // (RoleCreationRaceTests races CREATE ROLE, which is cluster-wide and so
    // belongs to no single test database).
    internal static string AdminConnectionStringFor(string databaseName) =>
        AdminConnectionString(databaseName);

    private sealed class SimpleDbContextFactory(DbContextOptions<AutoNateDbContext> options)
        : IDbContextFactory<AutoNateDbContext>
    {
        public AutoNateDbContext CreateDbContext() => new(options);

        public Task<AutoNateDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
