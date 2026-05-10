using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;
using GroupEntity = AutoNate.Web.Persistence.Scaffolded.Group;
using GroupMemberEntity = AutoNate.Web.Persistence.Scaffolded.GroupMember;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;
using RoleEntity = AutoNate.Web.Persistence.Scaffolded.Role;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Tests.Authorization;

// Verifies the four selector compilers introduced when we replaced the
// PathOnlySelectorCompiler registrations: WorkflowModel, Role, Group,
// RecordType. Each test inserts entities directly via the scaffolded
// DbContext, authors a grant with a tag predicate, then runs
// IAuthorizer.FilterQueryAsync to confirm the predicate is actually compiled
// into the SQL filter (rather than silently skipped, as it was before).
[Trait("Category", "Integration")]
public sealed class SelectorCompilerTagPredicateTests
{
    private static ClaimsPrincipal Actor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task WorkflowModel_ProcessKey_FiltersByLiteral()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        var (leadId, dealId) = await SeedTwoWorkflowModelsAsync(db);

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*[processkey=lead]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<WorkflowModelEntity>(
            db, actorId, EntityKinds.WorkflowModel,
            ctx => ctx.WorkflowModels.AsNoTracking().AsQueryable(),
            m => m.Id);

        Assert.Contains(leadId, visibleIds);
        Assert.DoesNotContain(dealId, visibleIds);
    }

    [Fact]
    public async Task WorkflowModel_Draft_FiltersByBool()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        var (draftId, publishedId) = await SeedDraftAndPublishedAsync(db);

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*[draft=true]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<WorkflowModelEntity>(
            db, actorId, EntityKinds.WorkflowModel,
            ctx => ctx.WorkflowModels.AsNoTracking().AsQueryable(),
            m => m.Id);

        Assert.Contains(draftId, visibleIds);
        Assert.DoesNotContain(publishedId, visibleIds);
    }

    [Fact]
    public async Task WorkflowModel_Published_FiltersOnVersionNumber()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        var (draftId, publishedId) = await SeedDraftAndPublishedAsync(db);

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*[published=true]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<WorkflowModelEntity>(
            db, actorId, EntityKinds.WorkflowModel,
            ctx => ctx.WorkflowModels.AsNoTracking().AsQueryable(),
            m => m.Id);

        Assert.Contains(publishedId, visibleIds);
        Assert.DoesNotContain(draftId, visibleIds);
    }

    [Fact]
    public async Task Role_Name_FiltersByLiteral()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid editorsId, viewersId;
        await using (var ctx = db.CreateDbContext())
        {
            editorsId = Guid.NewGuid();
            viewersId = Guid.NewGuid();
            await ctx.Roles.AddRangeAsync(
                NewRole(editorsId, "Editors", actorId),
                NewRole(viewersId, "Viewers", actorId));
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/role/*[name=Editors]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RoleEntity>(
            db, actorId, EntityKinds.Role,
            ctx => ctx.Roles.AsNoTracking().AsQueryable(),
            r => r.Id);

        Assert.Contains(editorsId, visibleIds);
        Assert.DoesNotContain(viewersId, visibleIds);
    }

    [Fact]
    public async Task Group_Name_FiltersByLiteral()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid engineeringId, salesId;
        await using (var ctx = db.CreateDbContext())
        {
            engineeringId = Guid.NewGuid();
            salesId = Guid.NewGuid();
            await ctx.Groups.AddRangeAsync(
                NewGroup(engineeringId, "Engineering", actorId),
                NewGroup(salesId, "Sales", actorId));
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/group/*[name=Engineering]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<GroupEntity>(
            db, actorId, EntityKinds.Group,
            ctx => ctx.Groups.AsNoTracking().AsQueryable(),
            g => g.Id);

        Assert.Contains(engineeringId, visibleIds);
        Assert.DoesNotContain(salesId, visibleIds);
    }

    [Fact]
    public async Task Group_Member_FiltersByActorMembership()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid joinedId, otherId;
        await using (var ctx = db.CreateDbContext())
        {
            joinedId = Guid.NewGuid();
            otherId = Guid.NewGuid();
            await ctx.Groups.AddRangeAsync(
                NewGroup(joinedId, "Joined", actorId),
                NewGroup(otherId, "Other", actorId));
            await ctx.SaveChangesAsync();

            await ctx.GroupMembers.AddAsync(new GroupMemberEntity
            {
                GroupId = joinedId,
                UserId = actorId,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = actorId
            });
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/group/*[member=user]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<GroupEntity>(
            db, actorId, EntityKinds.Group,
            ctx => ctx.Groups.AsNoTracking().AsQueryable(),
            g => g.Id);

        Assert.Contains(joinedId, visibleIds);
        Assert.DoesNotContain(otherId, visibleIds);
    }

    [Fact]
    public async Task RecordType_ShortCode_FiltersByLiteral()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid leadId, dealId;
        await using (var ctx = db.CreateDbContext())
        {
            leadId = Guid.NewGuid();
            dealId = Guid.NewGuid();
            // Shortcodes are stored uppercased by RecordTypeShortCode.Normalize.
            await ctx.RecordTypes.AddRangeAsync(
                NewRecordType(leadId, "LEAD", "Lead", isArchived: false, actorId),
                NewRecordType(dealId, "DEAL", "Deal", isArchived: false, actorId));
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/recordtype/*[shortcode=lead]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RecordTypeEntity>(
            db, actorId, EntityKinds.RecordType,
            ctx => ctx.RecordTypes.AsNoTracking().AsQueryable(),
            t => t.Id);

        Assert.Contains(leadId, visibleIds);
        Assert.DoesNotContain(dealId, visibleIds);
    }

    [Fact]
    public async Task RecordType_Archived_FiltersByBool()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid liveId, archivedId;
        await using (var ctx = db.CreateDbContext())
        {
            liveId = Guid.NewGuid();
            archivedId = Guid.NewGuid();
            await ctx.RecordTypes.AddRangeAsync(
                NewRecordType(liveId, "LIVE", "Live", isArchived: false, actorId),
                NewRecordType(archivedId, "OLD", "Old", isArchived: true, actorId));
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/recordtype/*[archived=true]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RecordTypeEntity>(
            db, actorId, EntityKinds.RecordType,
            ctx => ctx.RecordTypes.AsNoTracking().AsQueryable(),
            t => t.Id);

        Assert.Contains(archivedId, visibleIds);
        Assert.DoesNotContain(liveId, visibleIds);
    }

    // ---- helpers ----

    private static async Task<HashSet<Guid>> FilterAsync<T>(
        PostgresTestDatabase db,
        Guid actorId,
        string kind,
        Func<Persistence.AutoNateDbContext, IQueryable<T>> source,
        Func<T, Guid> idAccessor) where T : class
    {
        var authorizer = db.CreateAuthorizer(enabled: true, AuthorizationEnforcement.Full);
        await using var ctx = db.CreateDbContext();
        var filtered = await authorizer.FilterQueryAsync(
            ctx, Actor(actorId), kind, Actions.View, source(ctx));
        var rows = await filtered.ToListAsync();
        return rows.Select(idAccessor).ToHashSet();
    }

    private static async Task<(Guid lead, Guid deal)> SeedTwoWorkflowModelsAsync(PostgresTestDatabase db)
    {
        var leadId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        await using var ctx = db.CreateDbContext();
        await ctx.WorkflowModels.AddRangeAsync(
            NewWorkflowModel(leadId, "Lead", "lead", isDraft: false, publishedVersion: 1),
            NewWorkflowModel(dealId, "Deal", "deal", isDraft: false, publishedVersion: 1));
        await ctx.SaveChangesAsync();
        return (leadId, dealId);
    }

    private static async Task<(Guid draft, Guid published)> SeedDraftAndPublishedAsync(PostgresTestDatabase db)
    {
        var draftId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        await using var ctx = db.CreateDbContext();
        await ctx.WorkflowModels.AddRangeAsync(
            NewWorkflowModel(draftId, "Draft", "draft-key", isDraft: true, publishedVersion: null),
            NewWorkflowModel(publishedId, "Published", "pub-key", isDraft: false, publishedVersion: 1));
        await ctx.SaveChangesAsync();
        return (draftId, publishedId);
    }

    private static WorkflowModelEntity NewWorkflowModel(
        Guid id, string name, string processKey, bool isDraft, int? publishedVersion) =>
        new()
        {
            Id = id,
            Name = name,
            ProcessKey = processKey,
            BpmnXml = "<definitions/>",
            IsDraft = isDraft,
            DraftVersionNumber = 1,
            PublishedVersionNumber = publishedVersion,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static RoleEntity NewRole(Guid id, string name, Guid actorId) => new()
    {
        Id = id,
        Name = name,
        IsSystem = false,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actorId,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = actorId
    };

    private static GroupEntity NewGroup(Guid id, string name, Guid actorId) => new()
    {
        Id = id,
        Name = name,
        IsArchived = false,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actorId,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = actorId
    };

    private static RecordTypeEntity NewRecordType(
        Guid id, string shortCode, string name, bool isArchived, Guid actorId) => new()
    {
        Id = id,
        ShortCode = shortCode,
        Name = name,
        IsSystem = false,
        IsArchived = isArchived,
        NextKeyNumber = 1,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actorId,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = actorId
    };
}
