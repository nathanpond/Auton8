namespace AutoNate.Web.Services.Authorization;

// Topic + event-type names for the iam.events bus topic. Phase 3 of the
// audit-events plan introduces this domain — every administrative change to
// users, groups, roles, role assignments, and permission grants publishes one
// event here so an information-access audit can answer "who has what access
// today, who granted it, and when was it last changed?"
public static class IamEventTopic
{
    public const string TopicRoot = "iam";
    public const string TopicName = "iam.events";
}

public static class IamResourceKinds
{
    public const string User = "user";
    public const string Group = "group";
    public const string GroupMember = "group.member";
    public const string Role = "role";
    public const string RoleAssignment = "role.assignment";
    public const string PermissionGrant = "permission.grant";
    public const string Supervisor = "user.supervisor";
}

public static class IamEventTypes
{
    // User lifecycle
    public const string UserCreated = "iam.user.created";
    public const string UserUpdated = "iam.user.updated";
    public const string UserDeleted = "iam.user.deleted";
    public const string UserPasswordReset = "iam.user.password.reset";

    // Supervisor relation
    public const string SupervisorSet = "iam.user.supervisor.set";
    public const string SupervisorCleared = "iam.user.supervisor.cleared";

    // Group lifecycle
    public const string GroupCreated = "iam.group.created";
    public const string GroupUpdated = "iam.group.updated";
    public const string GroupArchived = "iam.group.archived";
    public const string GroupRestored = "iam.group.restored";
    public const string GroupDeleted = "iam.group.deleted";
    public const string GroupMemberAdded = "iam.group.member.added";
    public const string GroupMemberRemoved = "iam.group.member.removed";

    // Role lifecycle
    public const string RoleCreated = "iam.role.created";
    public const string RoleUpdated = "iam.role.updated";
    public const string RoleDeleted = "iam.role.deleted";

    // Role assignment lifecycle
    public const string RoleAssignmentGranted = "iam.role-assignment.granted";
    public const string RoleAssignmentRevoked = "iam.role-assignment.revoked";

    // Permission grant lifecycle
    public const string PermissionGrantCreated = "iam.permission-grant.created";
    public const string PermissionGrantDeleted = "iam.permission-grant.deleted";

    // View events (Phase 4)
    public const string UserListViewed = "iam.user.list.viewed";
    public const string UserViewed = "iam.user.viewed";
    public const string SupervisorsListViewed = "iam.user.supervisors.viewed";
    public const string SupervisorViewed = "iam.user.supervisor.viewed";
    public const string GroupListViewed = "iam.group.list.viewed";
    public const string GroupViewed = "iam.group.viewed";
    public const string GroupMembersViewed = "iam.group.members.viewed";
    public const string RoleListViewed = "iam.role.list.viewed";
    public const string RoleViewed = "iam.role.viewed";
    public const string RoleAssignmentsViewed = "iam.role.assignments.viewed";
    public const string RoleAssignmentsByPrincipalViewed = "iam.role-assignment.by-principal.viewed";
    public const string PermissionGrantListViewed = "iam.permission-grant.list.viewed";
    public const string AuthorizationExplained = "iam.authorization.explained";
    public const string RegistryViewed = "iam.registry.viewed";
}
