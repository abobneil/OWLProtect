using Microsoft.Extensions.Options;
using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore :
    IBootstrapService,
    IDashboardQueryService,
    IAdminRepository,
    IUserRepository,
    IDeviceRepository,
    IGatewayRepository,
    IGatewayPoolRepository,
    IPolicyRepository,
    ISessionRepository,
    IHealthSampleRepository,
    IAlertRepository,
    IAuthProviderConfigRepository,
    IAuditRepository,
    IAuditWriter,
    IDisposable
{
    private readonly ILogger<PostgresStore> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PersistenceOptions _options;
    private readonly IBootstrapAdminCredentialsProvider _bootstrapAdminCredentialsProvider;

    public PostgresStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresStore> logger,
        IBootstrapAdminCredentialsProvider bootstrapAdminCredentialsProvider)
    {
        _options = options.Value;
        _logger = logger;
        _bootstrapAdminCredentialsProvider = bootstrapAdminCredentialsProvider;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Persistence:ConnectionString is required when using the postgres provider.");
        }

        _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "sql", "001_initial_schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath, cancellationToken);

        await using (var command = _dataSource.CreateCommand(schemaSql))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (_options.SeedOnStartup)
        {
            await SeedIfEmptyAsync(cancellationToken);
        }
    }

    public void Dispose() => _dataSource.Dispose();

    public void WriteAudit(string actor, string action, string targetType, string targetId, string outcome, string detail)
    {
        using var connection = _dataSource.OpenConnection();
        AddAudit(connection, actor, action, targetType, targetId, outcome, detail);
    }

    private string BootstrapAdminUsername => _bootstrapAdminCredentialsProvider.GetBootstrapAdminCredentials().Username;

    private AdminAccount GetBootstrapAdmin(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT id, username, password_hash, role, must_change_password, mfa_enrolled
            FROM admins
            WHERE username = @username
            LIMIT 1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("username", BootstrapAdminUsername);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Bootstrap admin account was not found.");
        }

        return MapAdmin(reader);
    }

    private void AddAlert(NpgsqlConnection connection, NpgsqlTransaction? transaction, HealthSeverity severity, string title, string description, string targetType, string targetId)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO alerts (id, severity, title, description, target_type, target_id, created_at_utc)
            VALUES (@id, @severity, @title, @description, @target_type, @target_id, @created_at_utc)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("n"));
        command.Parameters.AddWithValue("severity", severity.ToString());
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("target_type", targetType);
        command.Parameters.AddWithValue("target_id", targetId);
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        command.ExecuteNonQuery();
    }

    private void AddAudit(NpgsqlConnection connection, string actor, string action, string targetType, string targetId, string outcome, string detail) =>
        AddAudit(connection, transaction: null, actor, action, targetType, targetId, outcome, detail);

    private void AddAudit(NpgsqlConnection connection, NpgsqlTransaction? transaction, string actor, string action, string targetType, string targetId, string outcome, string detail)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events (id, actor, action, target_type, target_id, created_at_utc, outcome, detail)
            VALUES (@id, @actor, @action, @target_type, @target_id, @created_at_utc, @outcome, @detail)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("n"));
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target_type", targetType);
        command.Parameters.AddWithValue("target_id", targetId);
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("detail", detail);
        command.ExecuteNonQuery();
    }

    private static AdminAccount MapAdmin(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<AdminRole>(reader.GetString(3), ignoreCase: true),
            reader.GetBoolean(4),
            reader.GetBoolean(5));

    private static User MapUser(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetString(5),
            ReadStringArray(reader, 6),
            ReadStringArray(reader, 7));

    private static Device MapDevice(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.GetInt32(8),
            ParseConnectionState(reader.GetString(9)),
            reader.GetFieldValue<DateTimeOffset>(10));

    private static Gateway MapGateway(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseHealthSeverity(reader.GetString(3)),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8));

    private static ConnectionState ParseConnectionState(string value) =>
        Enum.Parse<ConnectionState>(value, ignoreCase: true);

    private static HealthSeverity ParseHealthSeverity(string value) =>
        Enum.Parse<HealthSeverity>(value, ignoreCase: true);

    private static string[] ReadStringArray(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);

    private static int[] ReadIntArray(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<int[]>(ordinal);

    private static void BindGateway(NpgsqlCommand command, Gateway gateway)
    {
        command.Parameters.AddWithValue("id", gateway.Id);
        command.Parameters.AddWithValue("name", gateway.Name);
        command.Parameters.AddWithValue("region", gateway.Region);
        command.Parameters.AddWithValue("health", gateway.Health.ToString());
        command.Parameters.AddWithValue("load_percent", gateway.LoadPercent);
        command.Parameters.AddWithValue("peer_count", gateway.PeerCount);
        command.Parameters.AddWithValue("cpu_percent", gateway.CpuPercent);
        command.Parameters.AddWithValue("memory_percent", gateway.MemoryPercent);
        command.Parameters.AddWithValue("latency_ms", gateway.LatencyMs);
    }
}
