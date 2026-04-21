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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
