using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Auth;

public enum LoginAttemptOutcome
{
    Succeeded,
    InvalidCredentials,
    AccountLocked,
    JustLocked
}

public sealed record LoginAttemptResult(
    LoginAttemptOutcome Outcome,
    LocalUser? User,
    string? Username,
    int FailedAttempts,
    Guid? UserId = null);
