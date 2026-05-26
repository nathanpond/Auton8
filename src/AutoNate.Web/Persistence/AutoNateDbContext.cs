using System;
using System.Collections.Generic;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Persistence;

public partial class AutoNateDbContext : DbContext
{
    public AutoNateDbContext(DbContextOptions<AutoNateDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<LocalUser> LocalUsers { get; set; }

    public virtual DbSet<WorkflowModel> WorkflowModels { get; set; }

    public virtual DbSet<WorkflowModelVersion> WorkflowModelVersions { get; set; }

    public virtual DbSet<WorkflowExecutionError> WorkflowExecutionErrors { get; set; }

    public virtual DbSet<WorkflowTaskCompletion> WorkflowTaskCompletions { get; set; }

    public virtual DbSet<RecordType> RecordTypes { get; set; }

    public virtual DbSet<RecordTypeField> RecordTypeFields { get; set; }

    public virtual DbSet<RecordTypeAuditEntry> RecordTypeAuditLog { get; set; }

    public virtual DbSet<Record> Records { get; set; }

    public virtual DbSet<RecordFieldChange> RecordFieldChanges { get; set; }

    public virtual DbSet<RecordEdgeType> RecordEdgeTypes { get; set; }

    public virtual DbSet<RecordEdgeTypeField> RecordEdgeTypeFields { get; set; }

    public virtual DbSet<RecordEdge> RecordEdges { get; set; }

    public virtual DbSet<RecordComment> RecordComments { get; set; }

    public virtual DbSet<RecordCommentRevision> RecordCommentRevisions { get; set; }

    public virtual DbSet<RecordWatch> RecordWatches { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RoleAssignment> RoleAssignments { get; set; }

    public virtual DbSet<EntityEdge> EntityEdges { get; set; }

    public virtual DbSet<Group> Groups { get; set; }

    public virtual DbSet<GroupMember> GroupMembers { get; set; }

    public virtual DbSet<PermissionGrant> PermissionGrants { get; set; }

    public virtual DbSet<Menu> Menus { get; set; }

    public virtual DbSet<MenuItem> MenuItems { get; set; }

    public virtual DbSet<SiteAppearanceSettings> SiteAppearanceSettings { get; set; }

    public virtual DbSet<StatusAppearanceEntry> StatusAppearanceEntries { get; set; }

    public virtual DbSet<Plugin> Plugins { get; set; }

    public virtual DbSet<PageTemplate> PageTemplates { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<SiteSetting> SiteSettings { get; set; }

    public virtual DbSet<AuditOutboxEntry> AuditOutbox { get; set; }

    public virtual DbSet<SystemIssue> SystemIssues { get; set; }

    public virtual DbSet<Form> Forms { get; set; }

    public virtual DbSet<FormVersion> FormVersions { get; set; }

    public virtual DbSet<ExternalConnection> ExternalConnections { get; set; }

    public virtual DbSet<AgentConversation> AgentConversations { get; set; }

    public virtual DbSet<AgentMessage> AgentMessages { get; set; }

    public virtual DbSet<AgentToolCall> AgentToolCalls { get; set; }

    public virtual DbSet<AgentModel> AgentModels { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectMember> ProjectMembers { get; set; }

    public virtual DbSet<Dashboard> Dashboards { get; set; }

    public virtual DbSet<DashboardWidget> DashboardWidgets { get; set; }

    public virtual DbSet<DashboardShare> DashboardShares { get; set; }

    public virtual DbSet<SavedQuery> SavedQueries { get; set; }

    public virtual DbSet<Cabinet> Cabinets { get; set; }

    public virtual DbSet<Notebook> Notebooks { get; set; }

    public virtual DbSet<Page> Pages { get; set; }

    public virtual DbSet<PageVersion> PageVersions { get; set; }

    public virtual DbSet<PageAttachment> PageAttachments { get; set; }

    public virtual DbSet<Note> Notes { get; set; }

    public virtual DbSet<NoteVersion> NoteVersions { get; set; }

    public virtual DbSet<Folder> Folders { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentVersion> DocumentVersions { get; set; }

    public virtual DbSet<DocumentComment> DocumentComments { get; set; }

    public virtual DbSet<ContentAncestor> ContentAncestors { get; set; }

    public virtual DbSet<PageFavorite> PageFavorites { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalUser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("local_users_pkey");

            entity.ToTable("local_users");

            entity.HasIndex(e => e.Username, "ix_local_users_username");

            entity.HasIndex(e => e.IdpKey, "local_users_idp_key_key").IsUnique();

            entity.HasIndex(e => e.UserId, "local_users_user_id_key").IsUnique();

            entity.HasIndex(e => e.Username, "local_users_username_key").IsUnique();

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.CreatedDate).HasColumnName("created_date");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.IdpKey).HasColumnName("idp_key");
            entity.Property(e => e.LastLoginDate).HasColumnName("last_login_date");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.PasswordSalt).HasColumnName("password_salt");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.FailedLoginAttempts).HasColumnName("failed_login_attempts").HasDefaultValue(0);
            entity.Property(e => e.IsLocked).HasColumnName("is_locked").HasDefaultValue(false);
            entity.Property(e => e.LockedAtUtc).HasColumnName("locked_at_utc");
        });

        modelBuilder.Entity<WorkflowModel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_models_pkey");

            entity.ToTable("workflow_models");

            entity.HasIndex(e => e.UpdatedAtUtc, "ix_workflow_models_updated_at_utc").IsDescending();

            entity.HasIndex(e => e.ProcessKey, "workflow_models_process_key_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ActiveProcessInstanceId).HasColumnName("active_process_instance_id");
            entity.Property(e => e.BpmnXml).HasColumnName("bpmn_xml");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.DraftVersionNumber).HasColumnName("draft_version_number");
            entity.Property(e => e.IsDraft).HasColumnName("is_draft");
            entity.Property(e => e.LastDeployedAtUtc).HasColumnName("last_deployed_at_utc");
            entity.Property(e => e.LastDeploymentId).HasColumnName("last_deployment_id");
            entity.Property(e => e.LastProcessDefinitionId).HasColumnName("last_process_definition_id");
            entity.Property(e => e.LastProcessDefinitionKey).HasColumnName("last_process_definition_key");
            entity.Property(e => e.LastProcessDefinitionVersion).HasColumnName("last_process_definition_version");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ProcessKey).HasColumnName("process_key");
            entity.Property(e => e.PublishedVersionNumber).HasColumnName("published_version_number");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.DefaultVariables)
                .HasColumnName("default_variables")
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<WorkflowModelVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_model_versions_pkey");

            entity.ToTable("workflow_model_versions");

            entity.HasIndex(e => new { e.WorkflowModelId, e.VersionNumber }, "workflow_model_versions_workflow_model_id_version_number_key")
                .IsUnique();

            entity.HasIndex(e => e.WorkflowModelId, "ix_workflow_model_versions_workflow_model_id");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.BpmnXml).HasColumnName("bpmn_xml");
            entity.Property(e => e.DeploymentId).HasColumnName("deployment_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ProcessDefinitionId).HasColumnName("process_definition_id");
            entity.Property(e => e.ProcessDefinitionKey).HasColumnName("process_definition_key");
            entity.Property(e => e.ProcessDefinitionVersion).HasColumnName("process_definition_version");
            entity.Property(e => e.ProcessKey).HasColumnName("process_key");
            entity.Property(e => e.PublishedAtUtc).HasColumnName("published_at_utc");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.WorkflowModelId).HasColumnName("workflow_model_id");
        });

        modelBuilder.Entity<WorkflowExecutionError>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_execution_errors_pkey");

            entity.ToTable("workflow_execution_errors");

            entity.HasIndex(e => e.ProcessInstanceId, "ix_workflow_execution_errors_process_instance_id");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ProcessInstanceId).HasColumnName("process_instance_id");
            entity.Property(e => e.ActivityId).HasColumnName("activity_id");
            entity.Property(e => e.ActivityName).HasColumnName("activity_name");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ErrorStackTrace).HasColumnName("error_stack_trace");
            entity.Property(e => e.RawFlowableEventType).HasColumnName("raw_flowable_event_type");
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");
        });

        modelBuilder.Entity<WorkflowTaskCompletion>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("workflow_task_completions_pkey");

            entity.ToTable("workflow_task_completions");

            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.CompletedByUserId).HasColumnName("completed_by_user_id");
            entity.Property(e => e.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(e => e.WasOverride).HasColumnName("was_override");
        });

        modelBuilder.Entity<RecordType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_types_pkey");

            entity.ToTable("record_types");

            entity.HasIndex(e => e.ShortCode, "record_types_short_code_key").IsUnique();

            entity.HasIndex(e => e.UpdatedAtUtc, "ix_record_types_updated_at_utc").IsDescending();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ShortCode).HasColumnName("short_code");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.IsSystem).HasColumnName("is_system");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.NextKeyNumber).HasColumnName("next_key_number");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<RecordTypeField>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_type_fields_pkey");

            entity.ToTable("record_type_fields");

            entity.HasIndex(e => new { e.RecordTypeId, e.FieldKey },
                "record_type_fields_record_type_id_field_key_key").IsUnique();

            entity.HasIndex(e => new { e.RecordTypeId, e.SortOrder },
                "ix_record_type_fields_record_type_id");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.RecordTypeId).HasColumnName("record_type_id");
            entity.Property(e => e.FieldKey).HasColumnName("field_key");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.DataType).HasColumnName("data_type");
            entity.Property(e => e.Config)
                .HasColumnName("config")
                .HasColumnType("jsonb");
            entity.Property(e => e.IsRequired).HasColumnName("is_required");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<RecordTypeAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_type_audit_log_pkey");

            entity.ToTable("record_type_audit_log");

            entity.HasIndex(e => new { e.RecordTypeId, e.ChangedAtUtc },
                "ix_record_type_audit_log_record_type_id").IsDescending(false, true);

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.RecordTypeId).HasColumnName("record_type_id");
            entity.Property(e => e.ChangeKind).HasColumnName("change_kind");
            entity.Property(e => e.Before)
                .HasColumnName("before")
                .HasColumnType("jsonb");
            entity.Property(e => e.After)
                .HasColumnName("after")
                .HasColumnType("jsonb");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc");
        });

        modelBuilder.Entity<Record>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("records_pkey");

            entity.ToTable("records");

            entity.HasIndex(e => e.Key, "records_key_key").IsUnique();
            entity.HasIndex(e => e.RecordTypeId, "ix_records_record_type_id");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.RecordTypeId).HasColumnName("record_type_id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.KeyNumber).HasColumnName("key_number");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.AssigneeIds)
                .HasColumnName("assignee_ids")
                .HasColumnType("uuid[]");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.DueDate)
                .HasColumnName("due_date")
                .HasColumnType("date");
            entity.Property(e => e.Values)
                .HasColumnName("values")
                .HasColumnType("jsonb");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<RecordFieldChange>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_field_changes_pkey");

            entity.ToTable("record_field_changes");

            entity.HasIndex(e => new { e.RecordId, e.ChangedAtUtc },
                "ix_record_field_changes_record").IsDescending(false, true);
            entity.HasIndex(e => new { e.RecordId, e.FieldKey, e.ChangedAtUtc },
                "ix_record_field_changes_record_field").IsDescending(false, false, true);

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.RecordId).HasColumnName("record_id");
            entity.Property(e => e.ChangeSetId).HasColumnName("change_set_id");
            entity.Property(e => e.ChangeKind).HasColumnName("change_kind");
            entity.Property(e => e.FieldKey).HasColumnName("field_key");
            entity.Property(e => e.OldValue)
                .HasColumnName("old_value")
                .HasColumnType("jsonb");
            entity.Property(e => e.NewValue)
                .HasColumnName("new_value")
                .HasColumnType("jsonb");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.ChangedAtUtc).HasColumnName("changed_at_utc");
            entity.HasIndex(e => e.ChangeSetId, "ix_record_field_changes_change_set");
        });

        modelBuilder.Entity<RecordEdgeType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_edge_types_pkey");

            entity.ToTable("record_edge_types");

            entity.HasIndex(e => e.ShortCode, "record_edge_types_short_code_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ShortCode).HasColumnName("short_code");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.InverseName).HasColumnName("inverse_name");
            entity.Property(e => e.IsDirected).HasColumnName("is_directed");
            entity.Property(e => e.AllowSelfReference).HasColumnName("allow_self_reference");
            entity.Property(e => e.Cardinality).HasColumnName("cardinality");
            entity.Property(e => e.FromRecordTypeIds)
                .HasColumnName("from_record_type_ids")
                .HasColumnType("uuid[]");
            entity.Property(e => e.ToRecordTypeIds)
                .HasColumnName("to_record_type_ids")
                .HasColumnType("uuid[]");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<RecordEdgeTypeField>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_edge_type_fields_pkey");

            entity.ToTable("record_edge_type_fields");

            entity.HasIndex(e => new { e.EdgeTypeId, e.FieldKey },
                "record_edge_type_fields_edge_type_id_field_key_key").IsUnique();
            entity.HasIndex(e => new { e.EdgeTypeId, e.SortOrder },
                "ix_record_edge_type_fields_edge_type");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.EdgeTypeId).HasColumnName("edge_type_id");
            entity.Property(e => e.FieldKey).HasColumnName("field_key");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.DataType).HasColumnName("data_type");
            entity.Property(e => e.Config)
                .HasColumnName("config")
                .HasColumnType("jsonb");
            entity.Property(e => e.IsRequired).HasColumnName("is_required");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        });

        modelBuilder.Entity<RecordEdge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_edges_pkey");

            entity.ToTable("record_edges");

            entity.HasIndex(e => new { e.FromRecordId, e.EdgeTypeId }, "ix_record_edges_from");
            entity.HasIndex(e => new { e.ToRecordId, e.EdgeTypeId }, "ix_record_edges_to");
            entity.HasIndex(e => e.EdgeTypeId, "ix_record_edges_type");
            entity.HasIndex(e => new { e.EdgeTypeId, e.FromRecordId, e.ToRecordId },
                "uq_record_edges_triple").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.EdgeTypeId).HasColumnName("edge_type_id");
            entity.Property(e => e.FromRecordId).HasColumnName("from_record_id");
            entity.Property(e => e.ToRecordId).HasColumnName("to_record_id");
            entity.Property(e => e.Data)
                .HasColumnName("data")
                .HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<RecordComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_comments_pkey");

            entity.ToTable("record_comments");

            entity.HasIndex(e => new { e.RecordId, e.CreatedAtUtc }, "ix_record_comments_record_all")
                .IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.RecordId).HasColumnName("record_id");
            entity.Property(e => e.AuthorId).HasColumnName("author_id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.BodyUpdatedAtUtc).HasColumnName("body_updated_at_utc");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
            entity.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");
            entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");
        });

        modelBuilder.Entity<RecordCommentRevision>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("record_comment_revisions_pkey");

            entity.ToTable("record_comment_revisions");

            entity.HasIndex(e => new { e.CommentId, e.ReplacedAtUtc }, "ix_record_comment_revisions_comment")
                .IsDescending(false, true);

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.CommentId).HasColumnName("comment_id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.ReplacedAtUtc).HasColumnName("replaced_at_utc");
            entity.Property(e => e.ReplacedBy).HasColumnName("replaced_by");
        });

        modelBuilder.Entity<RecordWatch>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RecordId }).HasName("record_watches_pkey");

            entity.ToTable("record_watches");

            entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc }, "ix_record_watches_user")
                .IsDescending(false, true);
            entity.HasIndex(e => e.RecordId, "ix_record_watches_record");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RecordId).HasColumnName("record_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Name, "roles_name_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsSystem).HasColumnName("is_system");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<RoleAssignment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("role_assignments_pkey");

            entity.ToTable("role_assignments");

            entity.HasIndex(e => new { e.PrincipalKind, e.PrincipalId }, "ix_role_assignments_principal");
            entity.HasIndex(e => e.RoleId, "ix_role_assignments_role");
            entity.HasIndex(e => new { e.RoleId, e.PrincipalKind, e.PrincipalId }, "uq_role_assignments_triple")
                .IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.PrincipalKind).HasColumnName("principal_kind");
            entity.Property(e => e.PrincipalId).HasColumnName("principal_id");
            entity.Property(e => e.ScopeString).HasColumnName("scope_string");
            entity.Property(e => e.ScopeAst)
                .HasColumnName("scope_ast")
                .HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<EntityEdge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("entity_edges_pkey");

            entity.ToTable("entity_edges");

            entity.HasIndex(e => new { e.ToKind, e.ToId, e.EdgeKind }, "ix_entity_edges_to");
            entity.HasIndex(e => new { e.FromKind, e.FromId, e.EdgeKind }, "ix_entity_edges_from");
            entity.HasIndex(e => new { e.EdgeKind, e.FromKind, e.FromId, e.ToKind, e.ToId },
                "uq_entity_edges_triple").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.EdgeKind).HasColumnName("edge_kind");
            entity.Property(e => e.FromKind).HasColumnName("from_kind");
            entity.Property(e => e.FromId).HasColumnName("from_id");
            entity.Property(e => e.ToKind).HasColumnName("to_kind");
            entity.Property(e => e.ToId).HasColumnName("to_id");
            entity.Property(e => e.Data)
                .HasColumnName("data")
                .HasColumnType("jsonb");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("groups_pkey");

            entity.ToTable("groups");

            entity.HasIndex(e => e.Name, "groups_name_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => new { e.GroupId, e.UserId }).HasName("group_members_pkey");

            entity.ToTable("group_members");

            entity.HasIndex(e => e.UserId, "ix_group_members_user");

            entity.Property(e => e.GroupId).HasColumnName("group_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AddedAtUtc).HasColumnName("added_at_utc");
            entity.Property(e => e.AddedBy).HasColumnName("added_by");
        });

        modelBuilder.Entity<PermissionGrant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("permission_grants_pkey");

            entity.ToTable("permission_grants");

            entity.HasIndex(e => new { e.PrincipalKind, e.PrincipalId }, "ix_permission_grants_principal");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.PrincipalKind).HasColumnName("principal_kind");
            entity.Property(e => e.PrincipalId).HasColumnName("principal_id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.SelectorString).HasColumnName("selector_string");
            entity.Property(e => e.SelectorAst)
                .HasColumnName("selector_ast")
                .HasColumnType("jsonb");
            entity.Property(e => e.Effect).HasColumnName("effect");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menus_pkey");

            entity.ToTable("menus");

            entity.HasIndex(e => e.Key, "menus_key_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsSystem).HasColumnName("is_system");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menu_items_pkey");

            entity.ToTable("menu_items");

            entity.HasIndex(e => new { e.MenuId, e.ParentId, e.SortOrder },
                "ix_menu_items_menu_parent_sort");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.MenuId).HasColumnName("menu_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.ItemType).HasColumnName("item_type");
            entity.Property(e => e.Config)
                .HasColumnName("config")
                .HasColumnType("jsonb");
            entity.Property(e => e.PermissionRequired).HasColumnName("permission_required");
            entity.Property(e => e.IsVisible).HasColumnName("is_visible");
            entity.Property(e => e.IsSystem).HasColumnName("is_system");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedByPluginId).HasColumnName("created_by_plugin_id");
        });

        modelBuilder.Entity<SiteAppearanceSettings>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("site_appearance_settings_pkey");

            entity.ToTable("site_appearance_settings");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.SiteName).HasColumnName("site_name");
            entity.Property(e => e.LogoMode).HasColumnName("logo_mode");
            entity.Property(e => e.LogoImageUrl).HasColumnName("logo_image_url");
            entity.Property(e => e.LogoIcon).HasColumnName("logo_icon");
            entity.Property(e => e.LogoText).HasColumnName("logo_text");
            entity.Property(e => e.LoginTagline).HasColumnName("login_tagline");
            entity.Property(e => e.LoginCoverImageUrl).HasColumnName("login_cover_image_url");
            entity.Property(e => e.PrimaryAccentColor).HasColumnName("primary_accent_color");
            entity.Property(e => e.HeaderBg).HasColumnName("header_bg");
            entity.Property(e => e.HeaderColor).HasColumnName("header_color");
            entity.Property(e => e.TopMenuBg).HasColumnName("top_menu_bg");
            entity.Property(e => e.TopMenuLinkColor).HasColumnName("top_menu_link_color");
            entity.Property(e => e.TopMenuLinkHoverBg).HasColumnName("top_menu_link_hover_bg");
            entity.Property(e => e.TopMenuLinkHoverColor).HasColumnName("top_menu_link_hover_color");
            entity.Property(e => e.TopMenuLinkActiveBg).HasColumnName("top_menu_link_active_bg");
            entity.Property(e => e.TopMenuLinkActiveColor).HasColumnName("top_menu_link_active_color");
            entity.Property(e => e.SidebarBg).HasColumnName("sidebar_bg");
            entity.Property(e => e.SidebarLinkColor).HasColumnName("sidebar_link_color");
            entity.Property(e => e.SidebarLinkHoverColor).HasColumnName("sidebar_link_hover_color");
            entity.Property(e => e.SidebarActiveBg).HasColumnName("sidebar_active_bg");
            entity.Property(e => e.SidebarActiveColor).HasColumnName("sidebar_active_color");
            entity.Property(e => e.SidebarIconColor).HasColumnName("sidebar_icon_color");
            entity.Property(e => e.SidebarSubmenuBg).HasColumnName("sidebar_submenu_bg");
            entity.Property(e => e.SidebarSectionColor).HasColumnName("sidebar_section_color");
            entity.Property(e => e.SurfaceBg).HasColumnName("surface_bg");
            entity.Property(e => e.SurfaceSecondaryBg).HasColumnName("surface_secondary_bg");
            entity.Property(e => e.SurfaceTextColor).HasColumnName("surface_text_color");
            entity.Property(e => e.SurfaceDimmedColor).HasColumnName("surface_dimmed_color");
            entity.Property(e => e.BorderColor).HasColumnName("border_color");
            entity.Property(e => e.DropdownBg).HasColumnName("dropdown_bg");
            entity.Property(e => e.ModalBg).HasColumnName("modal_bg");
            entity.Property(e => e.SecondaryButtonBg).HasColumnName("secondary_button_bg");
            entity.Property(e => e.SecondaryButtonTextColor).HasColumnName("secondary_button_text_color");
            entity.Property(e => e.SecondaryButtonBorderColor).HasColumnName("secondary_button_border_color");
            entity.Property(e => e.SecondaryButtonHoverBg).HasColumnName("secondary_button_hover_bg");
            entity.Property(e => e.SecondaryButtonHoverTextColor).HasColumnName("secondary_button_hover_text_color");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<StatusAppearanceEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("status_appearance_entries_pkey");

            entity.ToTable("status_appearance_entries");

            entity.HasIndex(e => e.Status, "status_appearance_entries_status_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Plugin>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("plugins_pkey");

            entity.ToTable("plugins");

            entity.HasIndex(e => e.Status, "ix_plugins_status");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.EntryAssembly).HasColumnName("entry_assembly");
            entity.Property(e => e.EntryType).HasColumnName("entry_type");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.LastEnabledAt).HasColumnName("last_enabled_at");
            entity.Property(e => e.LastDisabledAt).HasColumnName("last_disabled_at");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.RolePasswordEncrypted).HasColumnName("role_password_encrypted");
        });

        modelBuilder.Entity<PageTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("page_templates_pkey");

            entity.ToTable("page_templates");

            entity.HasIndex(e => e.Key, "page_templates_key_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ThumbnailUrl).HasColumnName("thumbnail_url");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedByPluginId).HasColumnName("created_by_plugin_id");
            entity.Property(e => e.ContentType).HasColumnName("content_type").HasDefaultValue("builtin");
            entity.Property(e => e.Content).HasColumnName("content");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc }, "ix_notifications_user_created")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.UserId, e.IsRead }, "ix_notifications_user_unread");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.RelatedEntityKind).HasColumnName("related_entity_kind");
            entity.Property(e => e.RelatedEntityId).HasColumnName("related_entity_id");
            entity.Property(e => e.ParentEntityKind).HasColumnName("parent_entity_kind");
            entity.Property(e => e.ParentEntityId).HasColumnName("parent_entity_id");
            entity.Property(e => e.LinkPath).HasColumnName("link_path");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.ReadAtUtc).HasColumnName("read_at_utc");
        });

        modelBuilder.Entity<SiteSetting>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("site_settings_pkey");

            entity.ToTable("site_settings");

            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.ValueJson)
                .HasColumnName("value_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<AuditOutboxEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_outbox_pkey");
            entity.ToTable("audit_outbox");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Topic).HasColumnName("topic");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.DispatchedAtUtc).HasColumnName("dispatched_at_utc");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.NextAttemptAfterUtc).HasColumnName("next_attempt_after_utc");

            // Filtered index for the dispatcher's hot path: only undispatched
            // rows whose backoff has expired.
            entity.HasIndex(e => new { e.NextAttemptAfterUtc }, "ix_audit_outbox_pending")
                .HasFilter("dispatched_at_utc IS NULL");
        });

        modelBuilder.Entity<SystemIssue>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("system_issues_pkey");
            entity.ToTable("system_issues");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DetectorId).HasColumnName("detector_id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Severity).HasColumnName("severity");
            entity.Property(e => e.Fingerprint).HasColumnName("fingerprint");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.RelatedEntityKind).HasColumnName("related_entity_kind");
            entity.Property(e => e.RelatedEntityId).HasColumnName("related_entity_id");
            entity.Property(e => e.FactsJson)
                .HasColumnName("facts_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
            entity.Property(e => e.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
            entity.Property(e => e.OccurrenceCount).HasColumnName("occurrence_count");
            entity.Property(e => e.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
            entity.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by");
            entity.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc");
            entity.Property(e => e.ResolutionKind).HasColumnName("resolution_kind");
            entity.Property(e => e.ResolutionNotes).HasColumnName("resolution_notes");
            entity.Property(e => e.AutoRemediationAttemptCount).HasColumnName("auto_remediation_attempt_count");
            entity.Property(e => e.AutoRemediationLastError).HasColumnName("auto_remediation_last_error");
            entity.Property(e => e.NextRemediationAfterUtc).HasColumnName("next_remediation_after_utc");

            // Mirror the partial unique index used for upsert dedup. The
            // schema initializer creates these — declaring them here lets EF
            // know about them without trying to recreate them.
            entity.HasIndex(e => e.Fingerprint, "ux_system_issues_open_fingerprint")
                .IsUnique()
                .HasFilter("state IN ('open', 'acknowledged')");
        });

        modelBuilder.Entity<Form>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("forms_pkey");

            entity.ToTable("forms");

            entity.HasIndex(e => e.ShortCode, "forms_short_code_key").IsUnique();

            entity.HasIndex(e => e.UpdatedAtUtc, "ix_forms_updated_at_utc").IsDescending();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ShortCode).HasColumnName("short_code");
            entity.Property(e => e.FormCode).HasColumnName("form_code");
            entity.Property(e => e.SiteAvailable).HasColumnName("site_available");
            entity.Property(e => e.IsDraft).HasColumnName("is_draft");
            entity.Property(e => e.DraftVersionNumber).HasColumnName("draft_version_number");
            entity.Property(e => e.PublishedVersionNumber).HasColumnName("published_version_number");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<FormVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("form_versions_pkey");

            entity.ToTable("form_versions");

            entity.HasIndex(e => new { e.FormId, e.VersionNumber },
                "form_versions_form_id_version_number_key").IsUnique();

            entity.HasIndex(e => e.FormId, "ix_form_versions_form_id");

            // Declared so EF orders the parent insert before the version
            // insert when both are added in the same SaveChanges (Create
            // path). The schema initializer creates the FK with ON DELETE
            // CASCADE — match it here so EF's delete tracking agrees.
            entity.HasOne<Form>()
                .WithMany()
                .HasForeignKey(e => e.FormId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.FormId).HasColumnName("form_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ShortCode).HasColumnName("short_code");
            entity.Property(e => e.FormCode).HasColumnName("form_code");
            entity.Property(e => e.SiteAvailable).HasColumnName("site_available");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<ExternalConnection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("external_connection_pkey");

            entity.ToTable("external_connection");

            entity.HasIndex(e => new { e.Kind, e.IsEnabled }, "ix_external_connection_kind_enabled");
            entity.HasIndex(e => e.Kind, "ux_external_connection_default_per_kind")
                .IsUnique()
                .HasFilter("is_default");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.SecretCiphertext).HasColumnName("secret_ciphertext");
            entity.Property(e => e.SecretFingerprint).HasColumnName("secret_fingerprint");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<AgentConversation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("agent_conversation_pkey");

            entity.ToTable("agent_conversation");

            entity.HasIndex(e => new { e.UserId, e.PageKey, e.LastMessageAtUtc }, "ix_agent_conversation_user_page")
                .IsDescending(false, false, true);
            entity.HasIndex(e => new { e.UserId, e.LastMessageAtUtc }, "ix_agent_conversation_user")
                .IsDescending(false, true);

            entity.HasOne<ExternalConnection>()
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PageKey).HasColumnName("page_key");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.ProviderKind).HasColumnName("provider_kind");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.ConnectionId).HasColumnName("connection_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.LastMessageAtUtc).HasColumnName("last_message_at_utc");
        });

        modelBuilder.Entity<AgentMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("agent_message_pkey");

            entity.ToTable("agent_message");

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAtUtc }, "ix_agent_message_conversation");

            entity.HasOne<AgentConversation>()
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.ParentMessageId).HasColumnName("parent_message_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.ContentJson)
                .HasColumnName("content_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.ProviderKind).HasColumnName("provider_kind");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entity.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entity.Property(e => e.CacheReadTokens).HasColumnName("cache_read_tokens");
            entity.Property(e => e.CacheWriteTokens).HasColumnName("cache_write_tokens");
            entity.Property(e => e.StopReason).HasColumnName("stop_reason");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.Kind).HasColumnName("kind").HasDefaultValue("chat");
            entity.Property(e => e.ReplacesThroughMessageId).HasColumnName("replaces_through_message_id");
        });

        modelBuilder.Entity<AgentToolCall>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("agent_tool_call_pkey");

            entity.ToTable("agent_tool_call");

            entity.HasIndex(e => e.MessageId, "ix_agent_tool_call_message");

            entity.HasOne<AgentMessage>()
                .WithMany()
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.ToolUseId).HasColumnName("tool_use_id");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.ArgsJson)
                .HasColumnName("args_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.ResultJson)
                .HasColumnName("result_json")
                .HasColumnType("jsonb");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.ErrorText).HasColumnName("error_text");
            entity.Property(e => e.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(e => e.FinishedAtUtc).HasColumnName("finished_at_utc");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
        });

        modelBuilder.Entity<AgentModel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("agent_model_pkey");
            entity.ToTable("agent_model");
            entity.HasIndex(e => e.ModelId, "agent_model_model_id_key").IsUnique();

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.ContextWindowTokens).HasColumnName("context_window_tokens");
            entity.Property(e => e.InputCostPerMillionTokens).HasColumnName("input_cost_per_million_tokens").HasColumnType("numeric(10,4)");
            entity.Property(e => e.OutputCostPerMillionTokens).HasColumnName("output_cost_per_million_tokens").HasColumnType("numeric(10,4)");
            entity.Property(e => e.CostCurrency).HasColumnName("cost_currency").HasDefaultValue("USD");
            entity.Property(e => e.CostPublishedAtUtc).HasColumnName("cost_published_at_utc");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
            entity.Property(e => e.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            entity.Property(e => e.IsAvailable).HasColumnName("is_available").HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("projects_pkey");
            entity.ToTable("projects");
            entity.HasIndex(e => e.UpdatedAtUtc, "ix_projects_updated_at_utc").IsDescending();
            entity.HasIndex(e => e.Locator, "projects_locator_key").IsUnique();

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DeletionsLocked).HasColumnName("deletions_locked");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.UserId }).HasName("project_members_pkey");
            entity.ToTable("project_members");
            entity.HasIndex(e => e.UserId, "ix_project_members_user_id");

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.AddedAtUtc).HasColumnName("added_at_utc");
            entity.Property(e => e.AddedBy).HasColumnName("added_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Dashboard>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("dashboards_pkey");
            entity.ToTable("dashboards");
            entity.HasIndex(e => new { e.OwnerUserId, e.UpdatedAtUtc }, "ix_dashboards_owner_user_id_updated_at_utc")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.Visibility, e.Scope }, "ix_dashboards_visibility_scope");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Visibility).HasColumnName("visibility").HasDefaultValue("private");
            entity.Property(e => e.Scope).HasColumnName("scope").HasDefaultValue("user");
            entity.Property(e => e.Source).HasColumnName("source").HasDefaultValue("user");
            entity.Property(e => e.TemplateKey).HasColumnName("template_key");
            entity.Property(e => e.SettingsJsonb)
                .HasColumnName("settings_jsonb")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<DashboardWidget>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("dashboard_widgets_pkey");
            entity.ToTable("dashboard_widgets");
            entity.HasIndex(e => e.DashboardId, "ix_dashboard_widgets_dashboard_id");

            entity.HasOne<Dashboard>()
                .WithMany()
                .HasForeignKey(e => e.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DashboardId).HasColumnName("dashboard_id");
            entity.Property(e => e.WidgetType).HasColumnName("widget_type");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.ConfigJsonb)
                .HasColumnName("config_jsonb")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");
            entity.Property(e => e.GridX).HasColumnName("grid_x");
            entity.Property(e => e.GridY).HasColumnName("grid_y");
            entity.Property(e => e.GridW).HasColumnName("grid_w");
            entity.Property(e => e.GridH).HasColumnName("grid_h");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<DashboardShare>(entity =>
        {
            entity.HasKey(e => new { e.DashboardId, e.PrincipalType, e.PrincipalId })
                .HasName("dashboard_shares_pkey");
            entity.ToTable("dashboard_shares");
            entity.HasIndex(e => new { e.PrincipalType, e.PrincipalId }, "ix_dashboard_shares_principal");

            entity.HasOne<Dashboard>()
                .WithMany()
                .HasForeignKey(e => e.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.DashboardId).HasColumnName("dashboard_id");
            entity.Property(e => e.PrincipalType).HasColumnName("principal_type");
            entity.Property(e => e.PrincipalId).HasColumnName("principal_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.GrantedAtUtc).HasColumnName("granted_at_utc");
            entity.Property(e => e.GrantedBy).HasColumnName("granted_by");
        });

        modelBuilder.Entity<SavedQuery>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("saved_queries_pkey");
            entity.ToTable("saved_queries");
            entity.HasIndex(e => e.OwnerUserId, "ix_saved_queries_owner_user_id");
            entity.HasIndex(e => e.IsShared, "ix_saved_queries_is_shared")
                .HasFilter("is_shared = TRUE");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.QueryText).HasColumnName("query_text");
            entity.Property(e => e.IsShared).HasColumnName("is_shared").HasDefaultValue(false);
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Cabinet>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cabinets_pkey");
            entity.ToTable("cabinets");
            entity.HasIndex(e => e.ProjectId, "ix_cabinets_project_id");
            entity.HasIndex(e => e.Locator, "cabinets_locator_key").IsUnique();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Notebook>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notebooks_pkey");
            entity.ToTable("notebooks");
            entity.HasIndex(e => e.CabinetId, "ix_notebooks_cabinet_id");
            entity.HasIndex(e => e.Locator, "notebooks_locator_key").IsUnique();

            entity.HasOne<Cabinet>()
                .WithMany()
                .HasForeignKey(e => e.CabinetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.CabinetId).HasColumnName("cabinet_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Page>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pages_pkey");
            entity.ToTable("pages");
            entity.HasIndex(e => e.NotebookId, "ix_pages_notebook_id");
            entity.HasIndex(e => e.ParentPageId, "ix_pages_parent_page_id");
            entity.HasIndex(e => e.Locator, "pages_locator_key").IsUnique();

            entity.HasOne<Notebook>()
                .WithMany()
                .HasForeignKey(e => e.NotebookId)
                .OnDelete(DeleteBehavior.Cascade);

            // Self-referential FK for parent_page_id. OnDelete cascade matches
            // the SQL schema (deleting a page deletes its descendant pages).
            entity.HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.ParentPageId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.NotebookId).HasColumnName("notebook_id");
            entity.Property(e => e.ParentPageId).HasColumnName("parent_page_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.BodyJsonb).HasColumnName("body_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.CurrentVersionNumber).HasColumnName("current_version_number");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("folders_pkey");
            entity.ToTable("folders");
            entity.HasIndex(e => e.ProjectId, "ix_folders_project_id");
            entity.HasIndex(e => e.ParentFolderId, "ix_folders_parent_folder_id");
            entity.HasIndex(e => e.Locator, "folders_locator_key").IsUnique();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Self-referential parent FK. Matches the SQL schema's ON DELETE
            // CASCADE — deleting a folder removes its descendants.
            entity.HasOne<Folder>()
                .WithMany()
                .HasForeignKey(e => e.ParentFolderId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.ParentFolderId).HasColumnName("parent_folder_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("documents_pkey");
            entity.ToTable("documents");
            entity.HasIndex(e => e.ProjectId, "ix_documents_project_id");
            entity.HasIndex(e => e.FolderId, "ix_documents_folder_id");
            entity.HasIndex(e => e.TemplateId, "ix_documents_template_id");
            entity.HasIndex(e => e.Locator, "documents_locator_key").IsUnique();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Folder>()
                .WithMany()
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            // Self-reference: a document created from a template carries the
            // template's id; we keep the link soft (SET NULL) so deleting the
            // template doesn't cascade through real documents that were
            // already cloned off it.
            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.FolderId).HasColumnName("folder_id");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.TemplateId).HasColumnName("template_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.BodyJsonb).HasColumnName("body_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.CurrentVersionNumber).HasColumnName("current_version_number");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("document_versions_pkey");
            entity.ToTable("document_versions");
            entity.HasIndex(e => new { e.DocumentId, e.VersionNumber },
                "document_versions_document_id_version_number_key").IsUnique();
            entity.HasIndex(e => e.DocumentId, "ix_document_versions_document_id");

            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.BodyJsonb).HasColumnName("body_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<DocumentComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("document_comments_pkey");
            entity.ToTable("document_comments");
            entity.HasIndex(e => new { e.DocumentId, e.Number },
                "document_comments_document_id_number_key").IsUnique();
            entity.HasIndex(e => new { e.DocumentId, e.ThreadId },
                "ix_document_comments_document_id_thread_id");

            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Self-reference for reply chains. ON DELETE CASCADE keeps the
            // table consistent if a thread root is deleted; the resolve UI
            // marks-resolved rather than deletes, so this rarely fires in
            // practice.
            entity.HasOne<DocumentComment>()
                .WithMany()
                .HasForeignKey(e => e.ParentCommentId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.Number).HasColumnName("number");
            entity.Property(e => e.ParentCommentId).HasColumnName("parent_comment_id");
            entity.Property(e => e.ThreadId).HasColumnName("thread_id");
            entity.Property(e => e.AuthorId).HasColumnName("author_id");
            entity.Property(e => e.BodyText).HasColumnName("body_text");
            entity.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc");
            entity.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<PageVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("page_versions_pkey");
            entity.ToTable("page_versions");
            entity.HasIndex(e => new { e.PageId, e.VersionNumber },
                "page_versions_page_id_version_number_key").IsUnique();
            entity.HasIndex(e => e.PageId, "ix_page_versions_page_id");

            entity.HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.PageId).HasColumnName("page_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.BodyJsonb).HasColumnName("body_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<PageAttachment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("page_attachments_pkey");
            entity.ToTable("page_attachments");
            entity.HasIndex(e => e.PageId, "ix_page_attachments_page_id");
            entity.HasIndex(e => e.Sha256Hex, "ix_page_attachments_sha256");

            entity.HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.PageId).HasColumnName("page_id");
            entity.Property(e => e.FileName).HasColumnName("file_name");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.ByteSize).HasColumnName("byte_size");
            entity.Property(e => e.Sha256Hex).HasColumnName("sha256_hex");
            entity.Property(e => e.StorageKey).HasColumnName("storage_key");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notes_pkey");
            entity.ToTable("notes");
            entity.HasIndex(e => e.PageId, "ix_notes_page_id");
            entity.HasIndex(e => e.Locator, "notes_locator_key").IsUnique();
            entity.HasIndex(e => new { e.PageId, e.PageNoteIndex },
                "notes_page_id_page_note_index_key").IsUnique();

            entity.HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Locator)
                .HasColumnName("locator")
                .HasDefaultValueSql("nextval('content_locator_seq')")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.PageId).HasColumnName("page_id");
            entity.Property(e => e.PageNoteIndex).HasColumnName("page_note_index");
            entity.Property(e => e.NoteKind).HasColumnName("note_kind");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.ContentJsonb).HasColumnName("content_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.PreviewSvg).HasColumnName("preview_svg");
            entity.Property(e => e.CurrentVersionNumber).HasColumnName("current_version_number");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<NoteVersion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("note_versions_pkey");
            entity.ToTable("note_versions");
            entity.HasIndex(e => new { e.NoteId, e.VersionNumber },
                "note_versions_note_id_version_number_key").IsUnique();
            entity.HasIndex(e => e.NoteId, "ix_note_versions_note_id");

            entity.HasOne<Note>()
                .WithMany()
                .HasForeignKey(e => e.NoteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.NoteId).HasColumnName("note_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.NoteKind).HasColumnName("note_kind");
            entity.Property(e => e.ContentJsonb).HasColumnName("content_jsonb").HasColumnType("jsonb");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<ContentAncestor>(entity =>
        {
            entity.HasKey(e => new { e.DescendantKind, e.DescendantId, e.AncestorKind, e.AncestorId })
                .HasName("content_ancestors_pkey");
            entity.ToTable("content_ancestors");
            entity.HasIndex(e => new { e.DescendantKind, e.DescendantId }, "ix_content_ancestors_desc");
            entity.HasIndex(e => new { e.AncestorKind, e.AncestorId }, "ix_content_ancestors_anc");

            entity.Property(e => e.DescendantKind).HasColumnName("descendant_kind");
            entity.Property(e => e.DescendantId).HasColumnName("descendant_id");
            entity.Property(e => e.AncestorKind).HasColumnName("ancestor_kind");
            entity.Property(e => e.AncestorId).HasColumnName("ancestor_id");
            entity.Property(e => e.Depth).HasColumnName("depth");
        });

        modelBuilder.Entity<PageFavorite>(entity =>
        {
            entity.HasKey(e => new { e.PageId, e.UserId }).HasName("page_favorites_pkey");
            entity.ToTable("page_favorites");
            entity.HasIndex(e => e.UserId, "ix_page_favorites_user_id");

            entity.HasOne<Page>()
                .WithMany()
                .HasForeignKey(e => e.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.PageId).HasColumnName("page_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FavoritedAtUtc).HasColumnName("favorited_at_utc");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
