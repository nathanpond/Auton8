using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Persistence;

// Projection-framework cache tables live in their own partial so the main
// scaffolded DbContext stays untouched by feature additions. Tables are
// created by DatabaseSchemaInitializer.WorkflowCacheSchemaSql.
public partial class AutoNateDbContext
{
    public virtual DbSet<WorkflowExecutionCache> WorkflowExecutionCache { get; set; } = null!;

    public virtual DbSet<WorkflowTaskCache> WorkflowTaskCache { get; set; } = null!;

    public virtual DbSet<WorkflowVariableCache> WorkflowVariableCache { get; set; } = null!;

    public virtual DbSet<WorkflowEventLogCache> WorkflowEventLogCache { get; set; } = null!;

    public virtual DbSet<ProcessRetentionConfig> ProcessRetentionConfigs { get; set; } = null!;

    public virtual DbSet<RecordActivityRollupCache> RecordActivityRollupCache { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowExecutionCache>(entity =>
        {
            entity.HasKey(e => e.FlowableInstanceId).HasName("workflow_execution_cache_pkey");
            entity.ToTable("workflow_execution_cache");

            entity.Property(e => e.FlowableInstanceId).HasColumnName("flowable_instance_id");
            entity.Property(e => e.ProcessDefinitionKey).HasColumnName("process_definition_key");
            entity.Property(e => e.ProcessDefinitionId).HasColumnName("process_definition_id");
            entity.Property(e => e.ProcessDefinitionVersion).HasColumnName("process_definition_version");
            entity.Property(e => e.BusinessKey).HasColumnName("business_key");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.StartTime).HasColumnName("start_time");
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.StartedBy).HasColumnName("started_by");
            entity.Property(e => e.CurrentActivityId).HasColumnName("current_activity_id");
            entity.Property(e => e.CurrentActivityName).HasColumnName("current_activity_name");
            entity.Property(e => e.RecordId).HasColumnName("record_id");
            entity.Property(e => e.AuthTagsJson).HasColumnName("auth_tags").HasColumnType("jsonb");
            entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
            entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
        });

        modelBuilder.Entity<WorkflowTaskCache>(entity =>
        {
            entity.HasKey(e => e.FlowableTaskId).HasName("workflow_task_cache_pkey");
            entity.ToTable("workflow_task_cache");

            entity.Property(e => e.FlowableTaskId).HasColumnName("flowable_task_id");
            entity.Property(e => e.FlowableInstanceId).HasColumnName("flowable_instance_id");
            entity.Property(e => e.ProcessDefinitionKey).HasColumnName("process_definition_key");
            entity.Property(e => e.TaskDefinitionKey).HasColumnName("task_definition_key");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Assignee).HasColumnName("assignee");
            entity.Property(e => e.Owner).HasColumnName("owner");
            entity.Property(e => e.CandidateUsers).HasColumnName("candidate_users").HasColumnType("text[]");
            entity.Property(e => e.CandidateGroups).HasColumnName("candidate_groups").HasColumnType("text[]");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.CreatedTime).HasColumnName("created_time");
            entity.Property(e => e.ClaimTime).HasColumnName("claim_time");
            entity.Property(e => e.CompletedTime).HasColumnName("completed_time");
            entity.Property(e => e.FormKey).HasColumnName("form_key");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.AuthTagsJson).HasColumnName("auth_tags").HasColumnType("jsonb");
            entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
            entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
        });

        modelBuilder.Entity<WorkflowVariableCache>(entity =>
        {
            entity.HasKey(e => new { e.FlowableInstanceId, e.Name })
                .HasName("workflow_variable_cache_pkey");
            entity.ToTable("workflow_variable_cache");

            entity.Property(e => e.FlowableInstanceId).HasColumnName("flowable_instance_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ValueText).HasColumnName("value_text");
            entity.Property(e => e.ValueLong).HasColumnName("value_long");
            entity.Property(e => e.ValueDouble).HasColumnName("value_double");
            entity.Property(e => e.ValueBool).HasColumnName("value_bool");
            entity.Property(e => e.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedTime).HasColumnName("updated_time");
            entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
            entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
        });

        modelBuilder.Entity<WorkflowEventLogCache>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("workflow_event_log_cache_pkey");
            entity.ToTable("workflow_event_log_cache");

            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.FlowableInstanceId).HasColumnName("flowable_instance_id");
            entity.Property(e => e.ProcessDefinitionKey).HasColumnName("process_definition_key");
            entity.Property(e => e.EventTime).HasColumnName("event_time");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.ActivityId).HasColumnName("activity_id");
            entity.Property(e => e.ActivityName).HasColumnName("activity_name");
            entity.Property(e => e.ActivityType).HasColumnName("activity_type");
            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.VariableName).HasColumnName("variable_name");
            entity.Property(e => e.Actor).HasColumnName("actor");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
            entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
        });

        modelBuilder.Entity<ProcessRetentionConfig>(entity =>
        {
            entity.HasKey(e => e.ProcessDefinitionKey).HasName("process_retention_config_pkey");
            entity.ToTable("process_retention_config");

            entity.Property(e => e.ProcessDefinitionKey).HasColumnName("process_definition_key");
            entity.Property(e => e.RetainDays).HasColumnName("retain_days");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<RecordActivityRollupCache>(entity =>
        {
            entity.HasKey(e => new { e.RecordTypeId, e.BucketDay })
                .HasName("record_activity_rollup_cache_pkey");
            entity.ToTable("record_activity_rollup_cache");

            entity.Property(e => e.RecordTypeId).HasColumnName("record_type_id");
            entity.Property(e => e.BucketDay).HasColumnName("bucket_day");
            entity.Property(e => e.RecordsCreated).HasColumnName("records_created");
            entity.Property(e => e.RecordsUpdated).HasColumnName("records_updated");
            entity.Property(e => e.RecordsArchived).HasColumnName("records_archived");
            entity.Property(e => e.ProjectionVersion).HasColumnName("projection_version");
            entity.Property(e => e.LastSyncAtUtc).HasColumnName("last_sync_at");
        });
    }
}
