using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Services.Workflow.Behaviors;

// First built-in workflow behavior. Reads `userId` from the running process
// variables, unlocks the matching local user if it's currently locked, and
// returns a status branch (`unlocked` / `alreadyUnlocked` / `userNotFound`)
// as a `unlockResult` process variable so the workflow author can branch on
// it via an exclusive gateway.
//
// Idempotent by construction: a second invocation with the same userId
// short-circuits to `alreadyUnlocked` without publishing a duplicate audit
// event. The activity transaction can roll back after we've committed; on
// the retry we just see the user already unlocked.
//
// Mirrors POST /api/users/{id}/unlock (UserEndpoints.cs ~line 105) for the
// audit event so consumers see the same shape regardless of who triggered
// it. Actor attribution falls out of IRequestContext: workflow-driven calls
// land without an authenticated principal (anonymous + shared-secret
// filter), so they show as system in the audit log — intentionally
// distinguishable from admin-driven unlocks.
public sealed class UnlockAccountBehavior : IWorkflowBehavior
{
    public const string BehaviorKey = "autonate.unlock-account";
    public const string ResultVariableName = "unlockResult";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnlockAccountBehavior> _log;

    public UnlockAccountBehavior(
        IServiceScopeFactory scopeFactory,
        ILogger<UnlockAccountBehavior> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public string Key => BehaviorKey;

    public string DisplayName => "Unlock Account";

    public string? Description =>
        "Unlocks the local user account identified by the `userId` process variable. " +
        "Sets a `unlockResult` variable to `unlocked`, `alreadyUnlocked`, or `userNotFound`.";

    public async Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken)
    {
        if (!context.Variables.TryGetValue("userId", out var rawUserId))
        {
            // Surface the result variable even on failure so the workflow's
            // history makes the behavior's outcome legible (otherwise "node
            // hit, nothing changed" looks like it didn't run at all).
            return BehaviorResult.Fail(
                "missingUserId",
                "Process variable 'userId' is required.",
                VariableUpdate("missingUserId"));
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILocalUserStore>();
        var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        // Workflow authors may have either id form on hand:
        //   * the bigint primary key (LocalUser.Id) — what SetLockedAsync needs
        //   * the stable Guid identifier (LocalUser.UserId) — what audit
        //     events publish under `resource.userId`
        // Accept either; resolve to a model first, then operate on its
        // numeric Id.
        var existing = await ResolveUserAsync(store, rawUserId, cancellationToken);
        if (existing is null && !LooksLikeKnownIdShape(rawUserId))
        {
            return BehaviorResult.Fail(
                "invalidUserId",
                $"Process variable 'userId' must be a numeric id or Guid; got '{rawUserId}'.",
                VariableUpdate("invalidUserId"));
        }

        if (existing is null)
        {
            _log.LogInformation(
                "UnlockAccountBehavior: user '{RawUserId}' not found (process {ProcessInstanceId}).",
                rawUserId, context.ProcessInstanceId);
            return BehaviorResult.Ok(VariableUpdate("userNotFound"));
        }

        if (!existing.IsLocked)
        {
            return BehaviorResult.Ok(VariableUpdate("alreadyUnlocked"));
        }

        var updated = await store.SetLockedAsync(existing.Id, isLocked: false, cancellationToken);
        if (updated is null)
        {
            // Race: locked when we read, deleted before we wrote. Treat as
            // not-found so the workflow can branch consistently.
            return BehaviorResult.Ok(VariableUpdate("userNotFound"));
        }

        await auditPublisher.PublishAsync(
            AuthEventTopic.TopicName,
            AuthEventTypes.AccountUnlocked,
            AuthEventTopic.ResourceKind,
            resource: new { id = updated.Id, userId = updated.UserId, username = updated.Username },
            details: new
            {
                source = "workflow-behavior",
                behaviorKey = BehaviorKey,
                processInstanceId = context.ProcessInstanceId,
                processDefinitionKey = context.ProcessDefinitionKey,
                correlationId = context.CorrelationId,
            },
            cancellationToken);

        _log.LogInformation(
            "UnlockAccountBehavior: unlocked user {UserId} (id={Id}) for process {ProcessInstanceId}.",
            updated.UserId, updated.Id, context.ProcessInstanceId);

        return BehaviorResult.Ok(VariableUpdate("unlocked"));
    }

    private static IReadOnlyDictionary<string, BehaviorVariableValue> VariableUpdate(string status) =>
        new Dictionary<string, BehaviorVariableValue>(StringComparer.Ordinal)
        {
            [ResultVariableName] = BehaviorVariableValue.String(status),
        };

    private static async Task<LocalUser?> ResolveUserAsync(
        ILocalUserStore store,
        JsonElement rawUserId,
        CancellationToken cancellationToken)
    {
        if (TryReadLong(rawUserId, out var numericId))
        {
            return await store.GetByIdAsync(numericId, cancellationToken);
        }
        if (TryReadGuid(rawUserId, out var guidId))
        {
            return await store.GetByUserIdAsync(guidId, cancellationToken);
        }
        return null;
    }

    private static bool LooksLikeKnownIdShape(JsonElement element)
    {
        return TryReadLong(element, out _) || TryReadGuid(element, out _);
    }

    private static bool TryReadLong(JsonElement element, out long value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt64(out value);
            case JsonValueKind.String:
                return long.TryParse(element.GetString(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryReadGuid(JsonElement element, out Guid value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(element.GetString(), out value);
        }
        value = Guid.Empty;
        return false;
    }
}
