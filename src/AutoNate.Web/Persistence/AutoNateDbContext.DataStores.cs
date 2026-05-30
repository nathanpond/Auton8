using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Persistence;

// Data Stores feature mappings (docs/plans/2026-05-30-data-stores-implementation.md).
// Tables are created by DatabaseSchemaInitializer.DataStoresSchemaSql.
public partial class AutoNateDbContext
{
    public virtual DbSet<DataStore> DataStores { get; set; } = null!;

    public virtual DbSet<DataConnector> DataConnectors { get; set; } = null!;

    public virtual DbSet<DataStoreFile> DataStoreFiles { get; set; } = null!;

    public virtual DbSet<DataStoreTable> DataStoreTables { get; set; } = null!;

    public virtual DbSet<ConnectorRun> ConnectorRuns { get; set; } = null!;

    public virtual DbSet<Dataset> Datasets { get; set; } = null!;

#pragma warning disable CA1822
    partial void OnDataStoresModelCreating(ModelBuilder modelBuilder)
#pragma warning restore CA1822
    {
        modelBuilder.Entity<DataStore>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("datastores_pkey");
            entity.ToTable("datastores");
            entity.HasIndex(e => e.OwnerUserId, "ix_datastores_owner_user_id");
            entity.HasIndex(e => e.Kind, "ix_datastores_kind");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<DataConnector>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("dataconnectors_pkey");
            entity.ToTable("dataconnectors");
            entity.HasIndex(e => e.OwnerUserId, "ix_dataconnectors_owner_user_id");
            entity.HasIndex(e => e.Kind, "ix_dataconnectors_kind");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.ConfigJson)
                .HasColumnName("config")
                .HasColumnType("jsonb");
            entity.Property(e => e.LastFetchedAtUtc).HasColumnName("last_fetched_at_utc");
            entity.Property(e => e.Cursor).HasColumnName("cursor");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<DataStoreFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("datastore_files_pkey");
            entity.ToTable("datastore_files");
            entity.HasIndex(e => e.DataStoreId, "ix_datastore_files_datastore_id");
            entity.HasIndex(e => new { e.DataStoreId, e.FolderPath }, "ix_datastore_files_datastore_folder");

            entity.HasOne<DataStore>()
                .WithMany()
                .HasForeignKey(e => e.DataStoreId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DataStoreId).HasColumnName("datastore_id");
            entity.Property(e => e.FolderPath).HasColumnName("folder_path");
            entity.Property(e => e.Filename).HasColumnName("filename");
            entity.Property(e => e.StorageKey).HasColumnName("storage_key");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.UploadedAtUtc).HasColumnName("uploaded_at_utc");
        });

        modelBuilder.Entity<DataStoreTable>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("datastore_tables_pkey");
            entity.ToTable("datastore_tables");
            entity.HasIndex(e => e.DataStoreId, "ix_datastore_tables_datastore_id");
            entity.HasIndex(
                e => new { e.DataStoreId, e.SchemaName, e.TableName },
                "uq_datastore_tables_datastore_schema_table").IsUnique();

            entity.HasOne<DataStore>()
                .WithMany()
                .HasForeignKey(e => e.DataStoreId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DataStoreId).HasColumnName("datastore_id");
            entity.Property(e => e.SchemaName).HasColumnName("schema_name");
            entity.Property(e => e.TableName).HasColumnName("table_name");
            entity.Property(e => e.ColumnSchemaJson)
                .HasColumnName("column_schema")
                .HasColumnType("jsonb");
            entity.Property(e => e.RowCount).HasColumnName("row_count");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<Dataset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("datasets_pkey");
            entity.ToTable("datasets");
            entity.HasIndex(e => e.OwnerUserId, "ix_datasets_owner_user_id");
            entity.HasIndex(e => new { e.SourceKind, e.SourceId }, "ix_datasets_source");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.ColumnSchemaJson)
                .HasColumnName("column_schema")
                .HasColumnType("jsonb");
            entity.Property(e => e.RefreshCron).HasColumnName("refresh_cron");
            entity.Property(e => e.LastRefreshedAtUtc).HasColumnName("last_refreshed_at_utc");
            entity.Property(e => e.SourceKind).HasColumnName("source_kind");
            entity.Property(e => e.SourceId).HasColumnName("source_id");
            entity.Property(e => e.SourceTableName).HasColumnName("source_table_name");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<ConnectorRun>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("connector_runs_pkey");
            entity.ToTable("connector_runs");
            entity.HasIndex(e => e.DataConnectorId, "ix_connector_runs_dataconnector_id");
            entity.HasIndex(e => e.StartedAtUtc, "ix_connector_runs_started_at_utc")
                .IsDescending();

            entity.HasOne<DataConnector>()
                .WithMany()
                .HasForeignKey(e => e.DataConnectorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.DataConnectorId).HasColumnName("dataconnector_id");
            entity.Property(e => e.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(e => e.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.RowsFetched).HasColumnName("rows_fetched");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.CursorBefore).HasColumnName("cursor_before");
            entity.Property(e => e.CursorAfter).HasColumnName("cursor_after");
            entity.Property(e => e.TriggeredBy).HasColumnName("triggered_by");
        });
    }
}
