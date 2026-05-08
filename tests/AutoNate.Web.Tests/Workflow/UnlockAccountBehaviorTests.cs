using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Workflow.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

public sealed class UnlockAccountBehaviorTests
{
    // The Flowable bridge dispatches on the `type` field as a string
    // (lowercased). If S.T.Json reverts to the integer default the JVM
    // throws "Unsupported variable update type '0'" and behavior runs
    // become invisible to the workflow.
    [Fact]
    public void BehaviorVariableType_SerializesAsLowercasableName_NotInteger()
    {
        var value = BehaviorVariableValue.String("unlocked");
        var json = JsonSerializer.Serialize(value);
        Assert.Contains("\"Type\":\"String\"", json);
        Assert.DoesNotContain("\"Type\":0", json);
    }

    [Fact]
    public async Task ExecuteAsync_UnlocksLockedUser_AndPublishesAuditEvent()
    {
        var (behavior, store, audit) = NewBehaviorWithLockedUser();

        var result = await behavior.ExecuteAsync(NewContext(userId: 42), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.NotNull(result.VariableUpdates);
        Assert.Equal(BehaviorVariableType.String,
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Type);
        Assert.Equal("unlocked",
            result.VariableUpdates[UnlockAccountBehavior.ResultVariableName].Value);

        Assert.Equal(1, store.UnlockCallCount);
        var published = Assert.Single(audit.Events);
        Assert.Equal(AuthEventTopic.TopicName, published.Topic);
        Assert.Equal(AuthEventTypes.AccountUnlocked, published.EventType);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotUnlockOrAudit_WhenAlreadyUnlocked()
    {
        var (behavior, store, audit) = NewBehavior(initialLocked: false, userId: 42);

        var result = await behavior.ExecuteAsync(NewContext(userId: 42), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal("alreadyUnlocked",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Equal(0, store.UnlockCallCount);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsUserNotFound_WhenUserMissing()
    {
        var (behavior, store, audit) = NewBehavior(initialLocked: true, userId: 42);

        var result = await behavior.ExecuteAsync(NewContext(userId: 99), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal("userNotFound",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Equal(0, store.UnlockCallCount);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWithMissingUserId_WhenVariableAbsent()
    {
        var (behavior, _, audit) = NewBehavior(initialLocked: true, userId: 42);

        var result = await behavior.ExecuteAsync(NewContext(userId: null), CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Equal("missingUserId", result.FailureCode);
        Assert.Equal("missingUserId",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWithInvalidUserId_WhenVariableNotNumeric()
    {
        var (behavior, _, audit) = NewBehavior(initialLocked: true, userId: 42);
        // userId is a json object — not a number, not a parseable string.
        var doc = JsonDocument.Parse("""{"name":"alice"}""");
        var context = new BehaviorContext(
            ProcessInstanceId: "p1",
            ExecutionId: "e1",
            ProcessDefinitionKey: "k",
            ProcessName: null,
            ActivityId: "ServiceTask_1",
            BusinessKey: null,
            CorrelationId: "c1",
            Variables: new Dictionary<string, JsonElement> { ["userId"] = doc.RootElement });

        var result = await behavior.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Equal("invalidUserId", result.FailureCode);
        Assert.Equal("invalidUserId",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsUserIdAsGuidString()
    {
        // Audit events publish resource.userId as the user's stable Guid;
        // a workflow author dropping that into a `userId` process variable
        // should still unlock the account.
        var (behavior, store, audit) = NewBehaviorWithLockedUser();
        var userGuid = store.UserGuid;
        using var doc = JsonDocument.Parse($"\"{userGuid}\"");
        var context = new BehaviorContext(
            ProcessInstanceId: "p1",
            ExecutionId: "e1",
            ProcessDefinitionKey: "k",
            ProcessName: null,
            ActivityId: "ServiceTask_1",
            BusinessKey: null,
            CorrelationId: "c1",
            Variables: new Dictionary<string, JsonElement> { ["userId"] = doc.RootElement.Clone() });

        var result = await behavior.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal("unlocked",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Equal(1, store.UnlockCallCount);
        var published = Assert.Single(audit.Events);
        Assert.Equal(AuthEventTypes.AccountUnlocked, published.EventType);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsUserIdAsNumericString()
    {
        var (behavior, store, _) = NewBehaviorWithLockedUser();
        var doc = JsonDocument.Parse("""{"userId":"42"}""");
        var context = new BehaviorContext(
            ProcessInstanceId: "p1",
            ExecutionId: "e1",
            ProcessDefinitionKey: "k",
            ProcessName: null,
            ActivityId: "ServiceTask_1",
            BusinessKey: null,
            CorrelationId: "c1",
            Variables: new Dictionary<string, JsonElement>
            {
                ["userId"] = doc.RootElement.GetProperty("userId")
            });

        var result = await behavior.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal("unlocked",
            result.VariableUpdates![UnlockAccountBehavior.ResultVariableName].Value);
        Assert.Equal(1, store.UnlockCallCount);
    }

    private static (UnlockAccountBehavior Behavior, FakeLocalUserStore Store, RecordingAuditEventPublisher Audit)
        NewBehaviorWithLockedUser() => NewBehavior(initialLocked: true, userId: 42);

    private static (UnlockAccountBehavior, FakeLocalUserStore, RecordingAuditEventPublisher)
        NewBehavior(bool initialLocked, long userId)
    {
        var store = new FakeLocalUserStore(new LocalUser
        {
            Id = userId,
            UserId = Guid.NewGuid(),
            Username = "alice",
            IsLocked = initialLocked,
            LockedAtUtc = initialLocked ? DateTimeOffset.UtcNow : null,
            FailedLoginAttempts = initialLocked ? 3 : 0
        });
        var audit = new RecordingAuditEventPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalUserStore>(store);
        services.AddSingleton<IAuditEventPublisher>(audit);
        var sp = services.BuildServiceProvider();
        var behavior = new UnlockAccountBehavior(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UnlockAccountBehavior>.Instance);
        return (behavior, store, audit);
    }

    private static BehaviorContext NewContext(long? userId)
    {
        var variables = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (userId.HasValue)
        {
            using var doc = JsonDocument.Parse(userId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            variables["userId"] = doc.RootElement.Clone();
        }
        return new BehaviorContext(
            ProcessInstanceId: "p1",
            ExecutionId: "e1",
            ProcessDefinitionKey: "unlock_test",
            ProcessName: "Unlock Test",
            ActivityId: "ServiceTask_1",
            BusinessKey: null,
            CorrelationId: "corr-1",
            Variables: variables);
    }
}

// In-memory single-user ILocalUserStore mirroring the production lockout
// policy so tests targeting any method on the contract can lean on a
// faithful fake instead of the postgres fixture. Single-slot intentionally:
// the behaviors we cover here only operate on one user at a time, and
// keeping the surface tiny keeps test setup readable.
internal sealed class FakeLocalUserStore : ILocalUserStore
{
    private LocalUser? _user;
    private string _password;

    public FakeLocalUserStore(LocalUser? user, string password = "password")
    {
        _user = user;
        _password = password;
    }

    public int UnlockCallCount { get; private set; }

    public Guid UserGuid => _user?.UserId ?? Guid.Empty;

    public Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalUser>>(
            _user is null ? Array.Empty<LocalUser>() : new[] { _user });

    public Task<LocalUserPage> ListPagedAsync(ListLocalUsersRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalUser> items = _user is null ? Array.Empty<LocalUser>() : new[] { _user };
        return Task.FromResult(new LocalUserPage(items, items.Count));
    }

    public Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(_user is not null
            && string.Equals(_user.Username, username, StringComparison.Ordinal)
            ? _user
            : null);

    public Task<LocalUser?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_user is not null && _user.Id == id ? _user : null);

    public Task<LocalUser?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_user is not null && _user.UserId == userId ? _user : null);

    public async Task<LocalUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var attempt = await AttemptLoginAsync(username, password, cancellationToken);
        return attempt.Outcome == LoginAttemptOutcome.Succeeded ? attempt.User : null;
    }

    // Mirrors the EfCore implementation: locked accounts reject every login,
    // wrong passwords increment the counter, hitting the threshold flips
    // IsLocked, success resets the counter.
    public Task<LoginAttemptResult> AttemptLoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (_user is null || !string.Equals(_user.Username, username, StringComparison.Ordinal))
        {
            return Task.FromResult(new LoginAttemptResult(
                LoginAttemptOutcome.InvalidCredentials, null, username, 0));
        }

        if (_user.IsLocked)
        {
            return Task.FromResult(new LoginAttemptResult(
                LoginAttemptOutcome.AccountLocked,
                null,
                _user.Username,
                _user.FailedLoginAttempts,
                _user.UserId));
        }

        if (!string.Equals(_password, password, StringComparison.Ordinal))
        {
            var failed = _user.FailedLoginAttempts + 1;
            var justLocked = failed >= EfCoreLocalUserStore.FailedLoginLockoutThreshold;
            _user = _user with
            {
                FailedLoginAttempts = failed,
                IsLocked = justLocked,
                LockedAtUtc = justLocked ? DateTimeOffset.UtcNow : _user.LockedAtUtc
            };
            return Task.FromResult(new LoginAttemptResult(
                justLocked ? LoginAttemptOutcome.JustLocked : LoginAttemptOutcome.InvalidCredentials,
                null,
                _user.Username,
                _user.FailedLoginAttempts,
                _user.UserId));
        }

        _user = _user with { LastLoginDate = DateTimeOffset.UtcNow, FailedLoginAttempts = 0 };
        return Task.FromResult(new LoginAttemptResult(
            LoginAttemptOutcome.Succeeded, _user, _user.Username, 0, _user.UserId));
    }

    public Task<LocalUser?> SetLockedAsync(long id, bool isLocked, CancellationToken cancellationToken = default)
    {
        if (_user is null || _user.Id != id) return Task.FromResult<LocalUser?>(null);
        if (!isLocked) UnlockCallCount++;
        _user = _user with
        {
            IsLocked = isLocked,
            LockedAtUtc = isLocked ? DateTimeOffset.UtcNow : null,
            FailedLoginAttempts = isLocked ? _user.FailedLoginAttempts : 0
        };
        return Task.FromResult<LocalUser?>(_user);
    }

    public Task<LocalUser> CreateAsync(
        string username,
        string firstName,
        string lastName,
        string password,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        // Single-slot fake: re-creating clobbers the seeded user. Tests
        // that need multi-user scenarios should use a different fixture.
        var userId = Guid.NewGuid();
        _user = new LocalUser
        {
            Id = (_user?.Id ?? 0) + 1,
            UserId = userId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            Email = string.IsNullOrWhiteSpace(email) ? $"{username}@localhost" : email,
            CreatedDate = DateTimeOffset.UtcNow,
            IdpKey = $"local-{userId:N}"
        };
        _password = password;
        return Task.FromResult(_user);
    }

    public Task<LocalUser?> UpdateAsync(
        long id,
        string username,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (_user is null || _user.Id != id) return Task.FromResult<LocalUser?>(null);
        _user = _user with
        {
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            Email = email
        };
        return Task.FromResult<LocalUser?>(_user);
    }

    public Task<bool> ResetPasswordAsync(long id, string password, CancellationToken cancellationToken = default)
    {
        if (_user is null || _user.Id != id) return Task.FromResult(false);
        _password = password;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_user is null || _user.Id != id) return Task.FromResult(false);
        _user = null;
        return Task.FromResult(true);
    }
}
