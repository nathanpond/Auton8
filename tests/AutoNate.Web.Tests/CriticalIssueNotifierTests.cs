using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.SystemIssues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class CriticalIssueNotifierTests
{
    [Fact]
    public async Task Low_severity_does_not_fan_out_notifications()
    {
        await using var harness = await NotifierHarness.CreateAsync();
        await harness.AssignSuperAdminAsync(harness.AdminUserId);

        await harness.Notifier.NotifyOpenedAsync(
            issueId: Guid.NewGuid(),
            severity: SystemIssueSeverities.Warning,
            title: "shouldn't fan out",
            summary: null,
            CancellationToken.None);

        Assert.Empty(harness.Notifications.Created);
    }

    [Fact]
    public async Task Error_severity_creates_notification_for_each_super_admin_user()
    {
        await using var harness = await NotifierHarness.CreateAsync();
        var alice = harness.AdminUserId; // pre-existing seeded admin
        await harness.AssignSuperAdminAsync(alice);

        await harness.Notifier.NotifyOpenedAsync(
            issueId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            severity: SystemIssueSeverities.Error,
            title: "Postgres unreachable",
            summary: "Connection refused for 90s",
            CancellationToken.None);

        var notification = Assert.Single(harness.Notifications.Created);
        Assert.Equal(alice, notification.UserId);
        Assert.Equal(NotificationKinds.SystemIssueOpened, notification.Kind);
        Assert.Equal("Postgres unreachable", notification.Title);
        Assert.Equal("Connection refused for 90s", notification.Body);
        Assert.Equal(NotificationEntityKinds.SystemIssue, notification.RelatedEntityKind);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", notification.RelatedEntityId);
        Assert.Equal("/admin/config/system-issues", notification.LinkPath);
    }

    [Fact]
    public async Task Critical_severity_with_no_super_admins_is_a_no_op()
    {
        // Seeded admin is NOT auto-assigned to SuperAdmin in this harness
        // (we explicitly skip the backfill). The notifier should log and
        // succeed without creating any notifications.
        await using var harness = await NotifierHarness.CreateAsync();

        await harness.Notifier.NotifyOpenedAsync(
            issueId: Guid.NewGuid(),
            severity: SystemIssueSeverities.Critical,
            title: "no admins to tell",
            summary: null,
            CancellationToken.None);

        Assert.Empty(harness.Notifications.Created);
    }

    // Wire just enough DI to exercise the notifier against a real
    // IRoleAssignmentStore + a recording INotificationStore.
    private sealed class NotifierHarness : IAsyncDisposable
    {
        public required PostgresTestDatabase Database { get; init; }
        public required IServiceProvider Services { get; init; }
        public required CriticalIssueNotifier Notifier { get; init; }
        public required RecordingNotificationStore Notifications { get; init; }
        public required Guid AdminUserId { get; init; }

        public static async Task<NotifierHarness> CreateAsync()
        {
            var db = await PostgresTestDatabase.CreateAsync();
            var recording = new RecordingNotificationStore();
            var provider = new ServiceCollection()
                .AddLogging()
                .AddDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>(opts =>
                    opts.UseNpgsql(db.ConnectionString))
                .AddScoped<AuthCacheBumper>()
                .AddScoped<IRoleAssignmentStore, EfCoreRoleAssignmentStore>()
                .AddSingleton(recording)
                .AddScoped<INotificationStore>(sp => sp.GetRequiredService<RecordingNotificationStore>())
                .BuildServiceProvider();

            var notifier = new CriticalIssueNotifier(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CriticalIssueNotifier>.Instance);

            await using var ctx = db.CreateDbContext();
            var admin = await ctx.LocalUsers.FirstAsync(u => u.Username == "admin");

            return new NotifierHarness
            {
                Database = db,
                Services = provider,
                Notifier = notifier,
                Notifications = recording,
                AdminUserId = admin.UserId
            };
        }

        public async Task AssignSuperAdminAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
            await store.AssignAsync(new CreateRoleAssignmentInput(
                RoleId: SystemRoles.SuperAdminId,
                PrincipalKind: "user",
                PrincipalId: userId.ToString(),
                ScopeString: null), actorId: userId);
        }

        public async ValueTask DisposeAsync()
        {
            if (Services is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (Services is IDisposable d) d.Dispose();
            await Database.DisposeAsync();
        }
    }

    private sealed class RecordingNotificationStore : INotificationStore
    {
        private readonly List<Notification> _created = new();
        public IReadOnlyList<Notification> Created
        {
            get { lock (_created) return _created.ToArray(); }
        }

        public Task<Notification> CreateAsync(CreateNotificationInput input, CancellationToken cancellationToken = default)
        {
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
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            lock (_created) _created.Add(n);
            return Task.FromResult(n);
        }

        public Task<IReadOnlyList<Notification>> ListForUserAsync(Guid userId, int? limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());

        public Task<NotificationPage> ListPagedForUserAsync(Guid userId, ListNotificationsRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new NotificationPage(Array.Empty<Notification>(), 0, 0));

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<Notification?>(null);

        public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<Notification>> DeleteByRelatedEntityAsync(
            Guid? userId, string relatedEntityKind, string relatedEntityId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());

        public Task<IReadOnlyList<Notification>> DeleteByParentEntityAsync(
            string parentEntityKind, string parentEntityId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());
    }
}
