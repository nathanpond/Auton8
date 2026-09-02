using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// The scaffolded EF entities and the API models share these two names, and
// both namespaces are needed here (entities to seed, models to read back).
using PermissionGrantModel = AutoNate.Web.Models.Authorization.PermissionGrant;
using RoleAssignmentModel = AutoNate.Web.Models.Authorization.RoleAssignment;

namespace AutoNate.Web.Tests.Authorization;

// archived-91: the endpoints that hand out privilege had no endpoint tests at all —
// POST /api/admin/roles/{id}/assignments (RoleEndpoints) + DELETE and
// GET /by-principal on /api/admin/role-assignments (RoleAssignmentEndpoints),
// plus the self-service content overrides at
// /api/content/{documents|folders}/{id}/permissions. A regression in any of
// those gates is a privilege-escalation bug, so every test here reads the
// store back after the call: a 403 that still wrote the row is exactly the
// failure a status-code-only test would miss.
//
// Three of the tests below pin behaviour that is sharp rather than desirable.
// They assert what the code actually does, and say so where they're defined:
//   • role:assign is transitively super-admin,
//   • revoke is gated kind-level, so an assign grant scoped to one role
//     revokes assignments of every other role,
//   • neither store checks that the principal it is granting to exists.
[Trait("Category", "Integration")]
public sealed class PrivilegeMutationEndpointTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    // ---------------------------------------------------------------------
    // Role assignments — POST /api/admin/roles/{roleId}/assignments
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AssignRole_WithoutAssignGrant_IsForbiddenAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        var client = await SignedInClientAsync(factory);

        var resp = await PostAssignmentAsync(client, roleId, EntityKinds.User, AdminUserId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, roleId));
    }

    [Fact]
    public async Task AssignRole_WithAssignGrant_WritesTheAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);
        var target = Guid.NewGuid();

        var resp = await PostAssignmentAsync(client, roleId, EntityKinds.User, target.ToString());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var stored = await ListAssignmentsForRoleAsync(factory, roleId);
        Assert.Single(stored);
        Assert.Equal(target.ToString(), stored[0].PrincipalId);
    }

    // The POST gate is instance-level (RequirePermission on the {id} route
    // slot), so a grant naming one role must not reach another. Contrast with
    // RevokeAssignment_WithGrantScopedToADifferentRole_* below, where the
    // kind-level gate does not make this distinction.
    [Fact]
    public async Task AssignRole_WithGrantScopedToADifferentRole_IsForbiddenAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var grantedRole = await CreateRoleAsync(factory, "Granted");
        var targetRole = await CreateRoleAsync(factory, "Target");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/{grantedRole}");
        var client = await SignedInClientAsync(factory);

        var resp = await PostAssignmentAsync(client, targetRole, EntityKinds.User, AdminUserId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, targetRole));
        // Nothing may leak into the role the caller *can* assign, either.
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, grantedRole));
    }

    // role:assign used to be transitively equivalent to super-admin: nothing
    // restricted which role could be handed out or to whom, and the authorizer
    // re-reads role assignments per request, so the escalation was live on the
    // next call. The pre/post probes hit an endpoint gated by a *different*
    // permission (role:view), which is what proves privilege was or was not
    // actually gained rather than merely that a row landed.
    [Fact]
    public async Task AssignRole_WithAssignGrant_CannotMakeTheCallerSuperAdmin()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var before = await client.GetAsync($"/api/admin/roles/{SystemRoles.SuperAdminId}");
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        var resp = await PostAssignmentAsync(
            client, SystemRoles.SuperAdminId, EntityKinds.User, AdminUserId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        Assert.Empty(await ListAssignmentsForRoleAsync(factory, SystemRoles.SuperAdminId));

        // Still exactly as privileged as before the attempt.
        var after = await client.GetAsync($"/api/admin/roles/{SystemRoles.SuperAdminId}");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    // The second half of the guard: SuperAdmin is not a special case, it is
    // the worst case. Handing yourself *any* role is how a narrow grant turns
    // into a broad one, so self-assignment is refused outright.
    [Fact]
    public async Task AssignRole_ToSelf_IsForbiddenAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var role = await CreateRoleAsync(factory, "Delegated");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await PostAssignmentAsync(
            client, role, EntityKinds.User, AdminUserId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, role));
    }

    // Delegation to other people is untouched — the guard is aimed at
    // self-escalation, not at making role management unusable.
    [Fact]
    public async Task AssignRole_ToAnotherUser_StillSucceeds()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var role = await CreateRoleAsync(factory, "Delegated");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var someoneElse = Guid.NewGuid();
        var resp = await PostAssignmentAsync(
            client, role, EntityKinds.User, someoneElse.ToString());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Contains(
            await ListAssignmentsForRoleAsync(factory, role),
            a => a.PrincipalId == someoneElse.ToString());
    }

    // The store's "Role '{id}' was not found" validation is unreachable from
    // the route: the instance gate resolves the role first and denies when it
    // doesn't exist, so an unknown role id is a 403, never a 400. Pinned so a
    // future refactor can't turn the gate into an existence oracle.
    [Fact]
    public async Task AssignRole_ToAnUnknownRole_IsForbiddenAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await PostAssignmentAsync(
            client, Guid.NewGuid(), EntityKinds.User, AdminUserId.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(0, await CountAllAssignmentsAsync(factory));
    }

    [Fact]
    public async Task AssignRole_WithUnknownPrincipalKind_IsBadRequestAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await PostAssignmentAsync(client, roleId, "robot", "hal-9000");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, roleId));
    }

    [Fact]
    public async Task AssignRole_WithMalformedScope_IsBadRequestAndWritesNoAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        // Targets another principal on purpose: self-assignment is refused
        // before the store ever validates the scope, so aiming this at the
        // caller would assert the escalation guard rather than the parser.
        var resp = await PostAssignmentAsync(
            client, roleId, EntityKinds.User, Guid.NewGuid().ToString(),
            scopeString: "garbage scope without leading slash");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, roleId));
    }

    // Real behaviour, not the desired one: EfCoreRoleAssignmentStore checks
    // the principal *kind* and the role's existence but never looks the
    // principal up, so a role can be granted to a user id that isn't in the
    // users table. Harmless today (a phantom principal authorizes nobody) but
    // it lets an assign-capable caller pre-seed privilege for a user id that
    // is created later. Asserting the current behaviour so a change to it is
    // a deliberate one.
    [Fact]
    public async Task AssignRole_ToAPrincipalThatDoesNotExist_StillWritesTheAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);
        var ghost = Guid.NewGuid();

        var resp = await PostAssignmentAsync(client, roleId, EntityKinds.User, ghost.ToString());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Contains(
            await ListAssignmentsForRoleAsync(factory, roleId),
            a => a.PrincipalId == ghost.ToString());
    }

    // ---------------------------------------------------------------------
    // Role assignments — DELETE /api/admin/role-assignments/{id}
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RevokeAssignment_WithoutAssignGrant_IsForbiddenAndLeavesTheAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        var victim = Guid.NewGuid();
        var assignmentId = await AssignAsync(factory, roleId, EntityKinds.User, victim.ToString());
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync($"/api/admin/role-assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var stored = await ListAssignmentsForRoleAsync(factory, roleId);
        Assert.Single(stored);
        Assert.Equal(assignmentId, stored[0].Id);
    }

    [Fact]
    public async Task RevokeAssignment_WithAssignGrant_RemovesTheAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        var assignmentId = await AssignAsync(
            factory, roleId, EntityKinds.User, Guid.NewGuid().ToString());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync($"/api/admin/role-assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, roleId));
    }

    // archived-182: the revoke gate has to be about the role the assignment names.
    // It used to be RequireKindPermission(Role, Assign) — "does any allow
    // grant for role:assign exist?" — which never resolved the assignment, so
    // a grant naming one throwaway role could strip anyone's SuperAdmin. The
    // assign side was already instance-gated, so the two halves of one
    // privilege disagreed.
    [Fact]
    public async Task RevokeAssignment_WithGrantScopedToADifferentRole_IsForbiddenAndLeavesTheAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var harmlessRole = await CreateRoleAsync(factory, "Harmless");
        var otherAdmin = Guid.NewGuid();
        var superAdminAssignment = await AssignAsync(
            factory, SystemRoles.SuperAdminId, EntityKinds.User, otherAdmin.ToString());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/{harmlessRole}");
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync($"/api/admin/role-assignments/{superAdminAssignment}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        // Still assigned: a 403 that revoked anyway would be worse than none.
        Assert.Contains(
            await ListAssignmentsForRoleAsync(factory, SystemRoles.SuperAdminId),
            a => a.PrincipalId == otherAdmin.ToString());
    }

    // The other half: a grant that does name the assignment's role still
    // works, so the fix narrowed the gate rather than breaking revoke.
    [Fact]
    public async Task RevokeAssignment_WithGrantScopedToThatRole_RemovesIt()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var role = await CreateRoleAsync(factory, "Revocable");
        var assignment = await AssignAsync(
            factory, role, EntityKinds.User, Guid.NewGuid().ToString());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/{role}");
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync($"/api/admin/role-assignments/{assignment}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(await ListAssignmentsForRoleAsync(factory, role));
    }

    // ---------------------------------------------------------------------
    // Role assignments — GET /api/admin/role-assignments/by-principal
    // ---------------------------------------------------------------------

    // role:assign must not confer read on the assignment graph: knowing who
    // holds which role is itself a targeting aid.
    [Fact]
    public async Task ListAssignmentsByPrincipal_WithoutRoleViewGrant_IsForbiddenAndLeaksNothing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        var victim = Guid.NewGuid();
        await AssignAsync(factory, roleId, EntityKinds.User, victim.ToString());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync(
            $"/api/admin/role-assignments/by-principal?principalKind={EntityKinds.User}&principalId={victim}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.DoesNotContain(
            roleId.ToString(),
            await resp.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    // Positive control for the gate above. Also pins the read's scope: the
    // filter is kind-level, so role:view over any role reads *any* principal's
    // assignments — the endpoint has no notion of "my own roles".
    [Fact]
    public async Task ListAssignmentsByPrincipal_WithRoleViewGrant_ReturnsAnotherPrincipalsAssignments()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var roleId = await CreateRoleAsync(factory, "Editors");
        var victim = Guid.NewGuid();
        await AssignAsync(factory, roleId, EntityKinds.User, victim.ToString());
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.View, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync(
            $"/api/admin/role-assignments/by-principal?principalKind={EntityKinds.User}&principalId={victim}");

        resp.EnsureSuccessStatusCode();
        Assert.Contains(
            roleId.ToString(),
            await resp.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Content permission overrides —
    // /api/content/{documents|folders}/{id}/permissions
    // ---------------------------------------------------------------------

    // The whole group is gated on Edit of the resource in the route, so a
    // caller with no access to the folder must be unable to read, widen, or
    // tear down its access list. All three verbs in one test because the
    // shared assertion is the interesting one: the grant table is untouched.
    [Fact]
    public async Task OverrideEndpoints_WithoutEditOnTheFolder_ForbidEveryVerbAndChangeNothing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (_, folderId) = await SeedFolderAsync(factory);
        var existingBeneficiary = Guid.NewGuid();
        var existingGrant = await GrantAsync(factory, EntityKinds.User,
            existingBeneficiary.ToString(), Actions.View, FolderSelector(folderId));
        var client = await SignedInClientAsync(factory);

        var list = await client.GetAsync($"/api/content/folders/{folderId}/permissions");
        var create = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, Guid.NewGuid().ToString(), Actions.View);
        var delete = await client.DeleteAsync(
            $"/api/content/folders/{folderId}/permissions/{existingGrant}");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.DoesNotContain(
            existingBeneficiary.ToString(),
            await list.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        var stored = await ListGrantsForSelectorAsync(factory, FolderSelector(folderId));
        Assert.Single(stored);
        Assert.Equal(existingGrant, stored[0].Id);
    }

    [Fact]
    public async Task CreateOverride_AsFolderEditor_WritesAnAllowGrantScopedToThatFolder()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);
        var beneficiary = Guid.NewGuid();

        var resp = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, beneficiary.ToString(), Actions.View);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var stored = Assert.Single(await ListGrantsForSelectorAsync(factory, FolderSelector(folderId)));
        Assert.Equal(beneficiary.ToString(), stored.PrincipalId);
        Assert.Equal(Actions.View, stored.Action);
        Assert.Equal("allow", stored.Effect);
    }

    // The escalation guard in the POST handler: passing the group's Edit gate
    // is not enough, the caller must also hold the specific action being
    // handed out. The actor here holds a folder-scoped Edit override and no
    // project membership at all, so Edit passes and View does not — which is
    // precisely the gap the guard exists to close.
    [Fact]
    public async Task CreateOverride_ForAnActionTheCallerLacks_IsForbiddenAndWritesNoGrant()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (_, folderId) = await SeedFolderAsync(factory);
        await GrantAsync(factory, EntityKinds.User, AdminUserId.ToString(),
            Actions.Edit, FolderSelector(folderId));
        var client = await SignedInClientAsync(factory);
        var beneficiary = Guid.NewGuid();

        var resp = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, beneficiary.ToString(), Actions.View);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var stored = await ListGrantsForSelectorAsync(factory, FolderSelector(folderId));
        Assert.DoesNotContain(stored, g => g.PrincipalId == beneficiary.ToString());
    }

    // Delete and archive are excluded from the self-service allowlist even
    // though a Contributor can perform them — sharing must not be able to
    // hand out destructive actions.
    [Fact]
    public async Task CreateOverride_WithANonGrantableAction_IsBadRequestAndWritesNoGrant()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);

        var resp = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, Guid.NewGuid().ToString(), Actions.Delete);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ListGrantsForSelectorAsync(factory, FolderSelector(folderId)));
    }

    // Kind isolation of the allowlist: `create` is grantable on a folder and
    // not on a document, and a Contributor holds `create` on both — so only
    // the per-kind array can tell these two calls apart. If MapForKind ever
    // stops threading `grantableActions` per kind, the document call starts
    // returning 201 and this fails.
    [Fact]
    public async Task CreateOverride_OnADocument_RejectsCreateEvenThoughFoldersAllowIt()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        var documentId = await SeedDocumentAsync(factory, projectId);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);
        var beneficiary = Guid.NewGuid();

        var onDocument = await PostOverrideAsync(
            client, "documents", documentId, EntityKinds.User, beneficiary.ToString(), Actions.Create);
        var onFolder = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, beneficiary.ToString(), Actions.Create);

        Assert.Equal(HttpStatusCode.BadRequest, onDocument.StatusCode);
        Assert.Equal(HttpStatusCode.Created, onFolder.StatusCode);
        Assert.Empty(await ListGrantsForSelectorAsync(factory, DocumentSelector(documentId)));
        Assert.Single(await ListGrantsForSelectorAsync(factory, FolderSelector(folderId)));
    }

    // The selector and effect are forced from the route, not the body — that
    // clamp is what stops a folder editor from writing a site-wide grant or a
    // deny that locks an owner out. Extra JSON members are ignored by the
    // binder today; this asserts the stored row regardless of how the request
    // record evolves.
    [Fact]
    public async Task CreateOverride_IgnoresCallerSuppliedSelectorAndEffect()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);
        var beneficiary = Guid.NewGuid();

        var resp = await client.PostAsJsonAsync(
            $"/api/content/folders/{folderId}/permissions",
            new
            {
                PrincipalKind = EntityKinds.User,
                PrincipalId = beneficiary.ToString(),
                Action = Actions.View,
                SelectorString = "/*",
                Effect = "deny",
                Priority = 9999
            });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var all = await ListAllGrantsAsync(factory);
        var stored = Assert.Single(
            all.Where(g => g.PrincipalId == beneficiary.ToString()).ToList());
        Assert.Equal(FolderSelector(folderId), stored.SelectorString);
        Assert.Equal("allow", stored.Effect);
        Assert.Equal(0, stored.Priority);
    }

    // archived-186 item 1: self-service sharing is for people and groups of people.
    // The grant store's allowlist also contains `role` — correct for an admin
    // using /api/admin/grants — but nothing narrowed it here, so anyone with
    // Edit on a folder could attach a resource grant to a role, SuperAdmin
    // included, through an endpoint meant for user/group sharing.
    [Fact]
    public async Task CreateOverride_ForARolePrincipal_IsRejectedAndWritesNoGrant()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);

        var before = (await ListAllGrantsAsync(factory)).Count;

        var resp = await client.PostAsJsonAsync(
            $"/api/content/folders/{folderId}/permissions",
            new { principalKind = EntityKinds.Role, principalId = SystemRoles.SuperAdminId.ToString(), action = "view" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(before, (await ListAllGrantsAsync(factory)).Count);
    }

    // Deliberate, not an oversight (archived-186 item 2, assessed and left alone).
    // EfCorePermissionGrantStore validates the principal kind but not that the
    // principal exists, so privilege can be written against an id before it is
    // created. That is how pre-provisioning works with an external IdP, where
    // the local user row appears on first sign-in — adding an existence check
    // would break granting ahead of someone's first login. Pinned so the
    // behaviour is a choice on the record rather than an accident.
    [Fact]
    public async Task CreateOverride_ForAPrincipalThatDoesNotExist_StillWritesTheGrant()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var client = await SignedInClientAsync(factory);
        var ghost = Guid.NewGuid();

        var resp = await PostOverrideAsync(
            client, "folders", folderId, EntityKinds.User, ghost.ToString(), Actions.View);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Contains(
            await ListGrantsForSelectorAsync(factory, FolderSelector(folderId)),
            g => g.PrincipalId == ghost.ToString());
    }

    // The DELETE handler re-reads the grant and checks its selector before
    // removing it, so an editor can't launder an arbitrary grant id — here a
    // site-admin role grant on a different kind entirely — through a folder
    // they legitimately edit.
    [Fact]
    public async Task DeleteOverride_ForAGrantThatDoesNotTargetTheFolder_IsNotFoundAndLeavesItIntact()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var foreignGrant = await GrantAsync(factory, EntityKinds.User, Guid.NewGuid().ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync(
            $"/api/content/folders/{folderId}/permissions/{foreignGrant}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Contains(await ListAllGrantsAsync(factory), g => g.Id == foreignGrant);
    }

    [Fact]
    public async Task DeleteOverride_ForAGrantOnTheFolder_RemovesIt()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var grantId = await GrantAsync(factory, EntityKinds.User, Guid.NewGuid().ToString(),
            Actions.View, FolderSelector(folderId));
        var client = await SignedInClientAsync(factory);

        var resp = await client.DeleteAsync(
            $"/api/content/folders/{folderId}/permissions/{grantId}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(await ListGrantsForSelectorAsync(factory, FolderSelector(folderId)));
    }

    // The list is filtered to the route's selector rather than returning the
    // whole grant table — a folder editor must not be able to read the site's
    // access map through a resource they happen to own.
    [Fact]
    public async Task ListOverrides_AsFolderEditor_ReturnsOnlyGrantsTargetingThatFolder()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var (projectId, folderId) = await SeedFolderAsync(factory);
        await AddProjectMemberAsync(factory, projectId, AdminUserId, ProjectRoleNames.Contributor);
        var mine = await GrantAsync(factory, EntityKinds.User, Guid.NewGuid().ToString(),
            Actions.View, FolderSelector(folderId));
        var elsewhere = await GrantAsync(factory, EntityKinds.User, Guid.NewGuid().ToString(),
            Actions.Assign, $"/{EntityKinds.Role}/*");
        var client = await SignedInClientAsync(factory);

        var resp = await client.GetAsync($"/api/content/folders/{folderId}/permissions");

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(mine.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(elsewhere.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        // Dev auto-login skips POSTs, so land the cookie with a GET first.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }

    private static string FolderSelector(Guid id) => $"/{ContentKinds.Folder}/{id}";

    private static string DocumentSelector(Guid id) => $"/{ContentKinds.Document}/{id}";

    private static Task<HttpResponseMessage> PostAssignmentAsync(
        HttpClient client,
        Guid roleId,
        string principalKind,
        string principalId,
        string? scopeString = null) =>
        client.PostAsJsonAsync($"/api/admin/roles/{roleId}/assignments", new
        {
            PrincipalKind = principalKind,
            PrincipalId = principalId,
            ScopeString = scopeString
        });

    private static Task<HttpResponseMessage> PostOverrideAsync(
        HttpClient client,
        string segment,
        Guid resourceId,
        string principalKind,
        string principalId,
        string action) =>
        client.PostAsJsonAsync($"/api/content/{segment}/{resourceId}/permissions", new
        {
            PrincipalKind = principalKind,
            PrincipalId = principalId,
            Action = action
        });

    private static async Task<Guid> CreateRoleAsync(
        AutoNateWebApplicationFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        var role = await roles.CreateAsync(new CreateRoleInput(name, null), AdminUserId);
        return role.Id;
    }

    private static async Task<Guid> AssignAsync(
        AutoNateWebApplicationFactory factory,
        Guid roleId,
        string principalKind,
        string principalId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
        var assignment = await assignments.AssignAsync(
            new CreateRoleAssignmentInput(roleId, principalKind, principalId, null), AdminUserId);
        return assignment.Id;
    }

    private static async Task<Guid> GrantAsync(
        AutoNateWebApplicationFactory factory,
        string principalKind,
        string principalId,
        string action,
        string selector,
        string effect = "allow")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var grant = await grants.CreateAsync(new CreatePermissionGrantInput(
            principalKind, principalId, action, selector, effect, 0), AdminUserId);
        return grant.Id;
    }

    private static async Task<IReadOnlyList<RoleAssignmentModel>>
        ListAssignmentsForRoleAsync(AutoNateWebApplicationFactory factory, Guid roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
        return await assignments.ListByRoleAsync(roleId);
    }

    // Some denials must leave the table completely empty (e.g. a POST to a
    // role id that never existed) — ListByRoleAsync can't express that.
    private static async Task<int> CountAllAssignmentsAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RoleAssignments.AsNoTracking().CountAsync();
    }

    private static async Task<IReadOnlyList<PermissionGrantModel>>
        ListAllGrantsAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        return await grants.ListAsync();
    }

    private static async Task<IReadOnlyList<PermissionGrantModel>>
        ListGrantsForSelectorAsync(AutoNateWebApplicationFactory factory, string selector)
    {
        var all = await ListAllGrantsAsync(factory);
        return all
            .Where(g => string.Equals(g.SelectorString, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task<(Guid ProjectId, Guid FolderId)> SeedFolderAsync(
        AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var tree = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "priv-" + Guid.NewGuid().ToString("N")[..8],
            DeletionsLocked = false,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = AdminUserId, UpdatedBy = AdminUserId
        };
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ParentFolderId = null,
            Name = "root-folder",
            SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = AdminUserId, UpdatedBy = AdminUserId
        };

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(project);
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
        }

        // ContentAuthorizer fails closed without a project ancestor row, so
        // the closure has to exist before any gate can pass.
        foreach (var (kind, id) in new[]
        {
            (ContentKinds.Project, project.Id),
            (ContentKinds.Folder, folder.Id)
        })
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }

        return (project.Id, folder.Id);
    }

    private static async Task<Guid> SeedDocumentAsync(
        AutoNateWebApplicationFactory factory, Guid projectId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var tree = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

        var now = DateTime.UtcNow;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            FolderId = null,
            Kind = ContentKinds.Document,
            Title = "doc",
            BodyJsonb = "{}",
            CurrentVersionNumber = 1,
            SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = AdminUserId, UpdatedBy = AdminUserId
        };

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Documents.Add(document);
            await db.SaveChangesAsync();
        }

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await tree.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, document.Id, default);
        }

        return document.Id;
    }

    private static async Task AddProjectMemberAsync(
        AutoNateWebApplicationFactory factory, Guid projectId, Guid userId, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            AddedAtUtc = now, AddedBy = userId,
            UpdatedAtUtc = now, UpdatedBy = userId
        });
        await db.SaveChangesAsync();
    }
}
