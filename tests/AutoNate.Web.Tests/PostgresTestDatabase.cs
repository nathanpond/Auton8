using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Tests;

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName)
    {
        _databaseName = databaseName;
    }

    public string ConnectionString =>
        $"Host=localhost;Port=5432;Database={_databaseName};Username=autonate;Password=Your_password123!";

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var database = new PostgresTestDatabase($"autonate_test_{Guid.NewGuid():N}");
        await database.InitializeAsync();
        return database;
    }

    public EfCoreLocalUserStore CreateLocalUserStore() => new(CreateDbContextFactory());

    public EfCoreWorkflowModelStore CreateWorkflowStore() => new(CreateDbContextFactory());

    public EfCoreRecordTypeStore CreateRecordTypeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordStore CreateRecordStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordHistoryStore CreateRecordHistoryStore() =>
        new(CreateDbContextFactory());

    public EfCoreRecordEdgeTypeStore CreateRecordEdgeTypeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordEdgeStore CreateRecordEdgeStore() =>
        new(CreateDbContextFactory(), BuildDefaultFieldTypeRegistry());

    public EfCoreRecordCommentStore CreateRecordCommentStore() =>
        new(CreateDbContextFactory());

    public static IFieldTypeRegistry BuildDefaultFieldTypeRegistry() =>
        new FieldTypeRegistry(new IFieldType[]
        {
            new TextFieldType(),
            new NumberFieldType(),
            new DateFieldType(),
            new PhoneFieldType(),
            new EmailFieldType(),
            new OptionFieldType(),
            new BooleanFieldType()
        });

    public AutoNateDbContext CreateDbContext() => CreateDbContextFactory().CreateDbContext();

    public async ValueTask DisposeAsync()
    {
        await using var adminConnection = new NpgsqlConnection(AdminConnectionString("postgres"));
        await adminConnection.OpenAsync();

        await using (var terminateCommand = adminConnection.CreateCommand())
        {
            terminateCommand.CommandText =
                """
                select pg_terminate_backend(pid)
                from pg_stat_activity
                where datname = @databaseName
                  and pid <> pg_backend_pid();
                """;
            terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"drop database if exists \"{_databaseName}\";";
        await dropCommand.ExecuteNonQueryAsync();
    }

    private async Task InitializeAsync()
    {
        await using (var adminConnection = new NpgsqlConnection(AdminConnectionString("postgres")))
        {
            await adminConnection.OpenAsync();
            await using var createDatabaseCommand = adminConnection.CreateCommand();
            createDatabaseCommand.CommandText = $"create database \"{_databaseName}\";";
            await createDatabaseCommand.ExecuteNonQueryAsync();
        }

        var bootstrapScripts = new[]
        {
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "02-create-autonate-app-schema.sql"))
        };

        await using var databaseConnection = new NpgsqlConnection(ConnectionString);
        await databaseConnection.OpenAsync();

        foreach (var bootstrapScript in bootstrapScripts)
        {
            await using var bootstrapCommand = databaseConnection.CreateCommand();
            bootstrapCommand.CommandText = bootstrapScript;
            await bootstrapCommand.ExecuteNonQueryAsync();
        }
    }

    private IDbContextFactory<AutoNateDbContext> CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AutoNateDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new SimpleDbContextFactory(options);
    }

    private static string AdminConnectionString(string databaseName) =>
        $"Host=localhost;Port=5432;Database={databaseName};Username=autonate;Password=Your_password123!";

    private sealed class SimpleDbContextFactory(DbContextOptions<AutoNateDbContext> options)
        : IDbContextFactory<AutoNateDbContext>
    {
        public AutoNateDbContext CreateDbContext() => new(options);

        public Task<AutoNateDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
