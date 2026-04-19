using AutoNate.Web.Models;
using Npgsql;
using NpgsqlTypes;

namespace AutoNate.Web.Services.Auth;

public sealed class PostgresLocalUserStore(IConfiguration configuration) : ILocalUserStore
{
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is required to persist local users.");

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _isInitialized;

    public async Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key
            from local_users
            order by username asc;
            """,
            connection);

        var users = new List<LocalUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key
            from local_users
            where username = @username;
            """,
            connection);
        command.Parameters.AddWithValue("username", username.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                create table if not exists local_users (
                    id bigint generated always as identity primary key,
                    username text not null unique,
                    password_hash text not null,
                    password_salt text not null,
                    email text not null,
                    first_name text not null,
                    last_name text not null,
                    user_id uuid not null unique,
                    created_date timestamptz not null,
                    last_login_date timestamptz null,
                    idp_key text not null unique
                );

                create index if not exists ix_local_users_username
                    on local_users (username);
                """,
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            var (hash, salt) = PasswordHasher.HashPassword("admin");
            await using var seedCommand = new NpgsqlCommand(
                """
                insert into local_users (
                    username,
                    password_hash,
                    password_salt,
                    email,
                    first_name,
                    last_name,
                    user_id,
                    created_date,
                    last_login_date,
                    idp_key
                )
                values (
                    @username,
                    @password_hash,
                    @password_salt,
                    @email,
                    @first_name,
                    @last_name,
                    @user_id,
                    @created_date,
                    @last_login_date,
                    @idp_key
                )
                on conflict (username) do nothing;
                """,
                connection);
            seedCommand.Parameters.AddWithValue("username", "admin");
            seedCommand.Parameters.AddWithValue("password_hash", hash);
            seedCommand.Parameters.AddWithValue("password_salt", salt);
            seedCommand.Parameters.AddWithValue("email", "admin@localhost");
            seedCommand.Parameters.AddWithValue("first_name", "Admin");
            seedCommand.Parameters.AddWithValue("last_name", "User");
            seedCommand.Parameters.AddWithValue("user_id", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            seedCommand.Parameters.AddWithValue("created_date", DateTimeOffset.UtcNow);
            seedCommand.Parameters.Add(new NpgsqlParameter("last_login_date", NpgsqlDbType.TimestampTz)
            {
                Value = DBNull.Value
            });
            seedCommand.Parameters.AddWithValue("idp_key", "local-admin");
            await seedCommand.ExecuteNonQueryAsync(cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<LocalUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key
            from local_users
            where username = @username;
            """,
            connection);
        command.Parameters.AddWithValue("username", username.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var passwordHash = reader.GetString(2);
        var passwordSalt = reader.GetString(3);
        if (!PasswordHasher.VerifyPassword(password, passwordHash, passwordSalt))
        {
            return null;
        }

        var user = MapUser(reader);
        await reader.DisposeAsync();

        await using var updateCommand = new NpgsqlCommand(
            """
            update local_users
            set last_login_date = @last_login_date
            where id = @id;
            """,
            connection);
        updateCommand.Parameters.AddWithValue("last_login_date", DateTimeOffset.UtcNow);
        updateCommand.Parameters.AddWithValue("id", user.Id);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        return user with
        {
            LastLoginDate = DateTimeOffset.UtcNow
        };
    }

    public async Task<LocalUser> CreateAsync(
        string username,
        string firstName,
        string lastName,
        string password,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var normalizedUsername = NormalizeRequired(username, nameof(username));
        var normalizedFirstName = NormalizeRequired(firstName, nameof(firstName));
        var normalizedLastName = NormalizeRequired(lastName, nameof(lastName));
        var normalizedEmail = string.IsNullOrWhiteSpace(email)
            ? $"{normalizedUsername}@localhost"
            : NormalizeRequired(email, nameof(email));
        var (hash, salt) = PasswordHasher.HashPassword(NormalizeRequired(password, nameof(password)));
        var userId = Guid.NewGuid();
        var createdDate = DateTimeOffset.UtcNow;
        var idpKey = $"local-{userId:N}";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into local_users (
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key
            )
            values (
                @username,
                @password_hash,
                @password_salt,
                @email,
                @first_name,
                @last_name,
                @user_id,
                @created_date,
                @last_login_date,
                @idp_key
            )
            returning
                id,
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key;
            """,
            connection);
        command.Parameters.AddWithValue("username", normalizedUsername);
        command.Parameters.AddWithValue("password_hash", hash);
        command.Parameters.AddWithValue("password_salt", salt);
        command.Parameters.AddWithValue("email", normalizedEmail);
        command.Parameters.AddWithValue("first_name", normalizedFirstName);
        command.Parameters.AddWithValue("last_name", normalizedLastName);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("created_date", createdDate);
        command.Parameters.Add(new NpgsqlParameter("last_login_date", NpgsqlDbType.TimestampTz)
        {
            Value = DBNull.Value
        });
        command.Parameters.AddWithValue("idp_key", idpKey);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The user could not be created.");
            }

            return MapUser(reader);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("A user with that username already exists.", exception);
        }
    }

    public async Task<LocalUser?> UpdateAsync(
        long id,
        string username,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            update local_users
            set
                username = @username,
                first_name = @first_name,
                last_name = @last_name,
                email = @email
            where id = @id
            returning
                id,
                username,
                password_hash,
                password_salt,
                email,
                first_name,
                last_name,
                user_id,
                created_date,
                last_login_date,
                idp_key;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("username", NormalizeRequired(username, nameof(username)));
        command.Parameters.AddWithValue("first_name", NormalizeRequired(firstName, nameof(firstName)));
        command.Parameters.AddWithValue("last_name", NormalizeRequired(lastName, nameof(lastName)));
        command.Parameters.AddWithValue("email", NormalizeRequired(email, nameof(email)));

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("A user with that username already exists.", exception);
        }
    }

    public async Task<bool> ResetPasswordAsync(long id, string password, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var (hash, salt) = PasswordHasher.HashPassword(NormalizeRequired(password, nameof(password)));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            update local_users
            set
                password_hash = @password_hash,
                password_salt = @password_salt
            where id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("password_hash", hash);
        command.Parameters.AddWithValue("password_salt", salt);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        return updated > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            delete from local_users
            where id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        return deleted > 0;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static LocalUser MapUser(NpgsqlDataReader reader)
    {
        return new LocalUser
        {
            Id = reader.GetInt64(0),
            Username = reader.GetString(1),
            Email = reader.GetString(4),
            FirstName = reader.GetString(5),
            LastName = reader.GetString(6),
            UserId = reader.GetGuid(7),
            CreatedDate = reader.GetFieldValue<DateTimeOffset>(8),
            LastLoginDate = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            IdpKey = reader.GetString(10)
        };
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"'{paramName}' is required.");
        }

        return normalized;
    }
}
