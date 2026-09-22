using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;
using GroupEntity = AutoNate.Web.Persistence.Scaffolded.Group;
using GroupMemberEntity = AutoNate.Web.Persistence.Scaffolded.GroupMember;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;
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

    // ── #631: the two paths agree on case, and "insensitive" is not "fuzzy" ──
    //
    // The agreement property covers this across generated selectors, and its
    // generator pool had to gain case variants before it could -- every value in
    // it was already lowercase, which is why five divergences were found there
    // and this sixth had to be found by reading. Measured: with the variants
    // added and the fix reverted, the property reports 1567 lockouts and 0 leaks.
    //
    // These are the direct, named cases the property still cannot reach.
    //
    // `[shortcode=lead]` against a stored `LEAD` is NOT here: the test above
    // already asserts it, because RecordTypeSelectorCompiler has normalised short
    // codes since it was written. That one compiler getting it right is precisely
    // what made the other eight look deliberate.

    /// <summary>
    /// #651. The one site #631's nine-site table missed: `RecordSelectorCompiler`
    /// compiled `[status=…]` with `==`, so a grant that matched in the records
    /// list (whose SQL path lowers) missed in edge traversal and the grant
    /// debugger. Record statuses are free text -- `Open`, `In-Progress` in the
    /// dev database -- so the case an author types is the case that fails.
    /// </summary>
    [Fact]
    public async Task Record_Status_MatchesRegardlessOfCase()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();

        Guid openId, closedId;
        await using (var ctx = db.CreateDbContext())
        {
            var typeId = Guid.NewGuid();
            await ctx.RecordTypes.AddAsync(NewRecordType(typeId, "CASE", "Case", isArchived: false, actorId));
            openId = Guid.NewGuid();
            closedId = Guid.NewGuid();
            await ctx.Records.AddRangeAsync(
                NewRecord(openId, typeId, 1, "Open", actorId),
                NewRecord(closedId, typeId, 2, "Closed", actorId));
            await ctx.SaveChangesAsync();
        }

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/record/*[status=OPEN]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RecordEntity>(
            db, actorId, EntityKinds.Record,
            ctx => ctx.Records.AsNoTracking().AsQueryable(),
            r => r.Id);

        Assert.Contains(openId, visibleIds);
        // The complement: `Closed` is a different value, not a different spelling.
        Assert.DoesNotContain(closedId, visibleIds);
    }

    private static RecordEntity NewRecord(Guid id, Guid typeId, long number, string status, Guid actorId) => new()
    {
        Id = id,
        RecordTypeId = typeId,
        Key = $"CASE-{number}",
        KeyNumber = number,
        Name = $"Case {number}",
        Status = status,
        Values = "{}",
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = actorId,
        UpdatedAtUtc = DateTime.UtcNow,
        UpdatedBy = actorId
    };

    [Fact]
    public async Task WorkflowModel_ProcessKey_MatchesRegardlessOfCase()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        var (leadId, dealId) = await SeedTwoWorkflowModelsAsync(db);

        // Stored `lead`, authored `LEAD`. Before #631 this matched in memory and
        // returned nothing here -- a grant that works on a single-instance check
        // and is silently empty in a list.
        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*[processkey=LEAD]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<WorkflowModelEntity>(
            db, actorId, EntityKinds.WorkflowModel,
            ctx => ctx.WorkflowModels.AsNoTracking().AsQueryable(),
            m => m.Id);

        Assert.Contains(leadId, visibleIds);

        // The complement, and the point of it: case-insensitive must not have
        // become value-insensitive. `deal` is a different value, not a different
        // spelling of the same one.
        Assert.DoesNotContain(dealId, visibleIds);
    }

    [Fact]
    public async Task Role_Name_MatchesRegardlessOfCase()
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
            Actions.View, "/role/*[name=eDiToRs]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RoleEntity>(
            db, actorId, EntityKinds.Role,
            ctx => ctx.Roles.AsNoTracking().AsQueryable(),
            r => r.Id);

        Assert.Contains(editorsId, visibleIds);
        Assert.DoesNotContain(viewersId, visibleIds);
    }

    /// <summary>
    /// `%` and `_` are literal characters in a tag value, not wildcards (#631).
    /// </summary>
    /// <remarks>
    /// <para>THE TEST THAT REJECTS THE OBVIOUS IMPLEMENTATION. Making the SQL side
    /// case-insensitive is a one-word change if you reach for
    /// <c>EF.Functions.ILike</c> — and <c>ILIKE</c> reads <c>_</c> as "any single
    /// character" and <c>%</c> as "any characters". A grant authored
    /// <c>[name=E_itors]</c> would then match the stored <c>Editors</c>, and
    /// <c>[name=%]</c> would match every role in the table: a widening hidden
    /// inside the widening this change already is.</para>
    /// <para>The agreement property cannot catch it — no value in its pools
    /// contains either character — so it is asserted here by name rather than
    /// left for someone to find later.</para>
    /// </remarks>
    [Fact]
    public async Task Role_Name_TreatsWildcardCharactersAsLiterals()
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
            // Under ILIKE this reads as `E` + any single character + `itors`,
            // which matches the stored `Editors`. Under `lower() =` it is a role
            // literally named "E_itors", and there is no such role.
            Actions.View, "/role/*[name=E_itors]", "allow", 0), actorId);

        var visibleIds = await FilterAsync<RoleEntity>(
            db, actorId, EntityKinds.Role,
            ctx => ctx.Roles.AsNoTracking().AsQueryable(),
            r => r.Id);

        Assert.DoesNotContain(editorsId, visibleIds);
        Assert.DoesNotContain(viewersId, visibleIds);
    }

    // ---- helpers ----

    private static async Task<HashSet<Guid>> FilterAsync<T>(
        PostgresTestDatabase db,
        Guid actorId,
        string kind,
        // Fully qualified: a bare `Persistence.` here resolves against
        // AutoNate.Web.Tests.Persistence once any test lives in that namespace.
        Func<AutoNate.Web.Persistence.AutoNateDbContext, IQueryable<T>> source,
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
