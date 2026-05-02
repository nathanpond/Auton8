using System;
using System.Collections.Generic;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class LocalUser
{
    public long Id { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string PasswordSalt { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public string IdpKey { get; set; } = null!;

    public int FailedLoginAttempts { get; set; }

    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }
}
