using Microsoft.Extensions.Options;
using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore :
    IBootstrapService,
    IDashboardQueryService,
    ITenantRepository,
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
    IAuditRetentionRepository,
    IMachineTrustRepository,
    IDisposable
{
    private readonly ILogger<PostgresStore> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PersistenceOptions _options;
    private readonly IBootstrapAdminCredentialsProvider _bootstrapAdminCredentialsProvider;
    private readonly IPlatformBootstrapSettingsProvider _bootstrapSettingsProvider;
    private readonly IControlPlaneEventPublisher _eventPublisher;

    public PostgresStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresStore> logger,
        IBootstrapAdminCredentialsProvider bootstrapAdminCredentialsProvider,
        IPlatformBootstrapSettingsProvider bootstrapSettingsProvider,
        IControlPlaneEventPublisher eventPublisher)
    {
        _options = options.Value;
        _logger = logger;
        _bootstrapAdminCredentialsProvider = bootstrapAdminCredentialsProvider;
        _bootstrapSettingsProvider = bootstrapSettingsProvider;
        _eventPublisher = eventPublisher;

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

        await BackfillAuditChainAsync(cancellationToken);
    }

    public void Dispose() => _dataSource.Dispose();

    public void WriteAudit(string actor, string action, string targetType, string targetId, string outcome, string detail, string? tenantId = null)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        AddAudit(connection, transaction, actor, action, targetType, targetId, outcome, detail, tenantId);
        transaction.Commit();
    }

    public AuditRetentionCheckpoint ApplyRetention(AuditRetentionOperation operation)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        LockAuditChain(connection, transaction);
        EnableAuditMaintenance(connection, transaction);

        var checkpoint = new AuditRetentionCheckpoint(
            Guid.NewGuid().ToString("n"),
            operation.CutoffUtc,
            operation.ExportedAtUtc,
            operation.ExportPath,
            operation.RemovedThroughSequence,
            operation.RemovedThroughCreatedAtUtc,
            operation.RemovedThroughEventHash,
            operation.ExportedEventCount);

        using (var insert = new NpgsqlCommand(
                   """
                   INSERT INTO audit_retention_checkpoints (
                       id,
                       cutoff_utc,
                       exported_at_utc,
                       export_path,
                       removed_through_sequence,
                       removed_through_created_at_utc,
                       removed_through_event_hash,
                       exported_event_count)
                   VALUES (
                       @id,
                       @cutoff_utc,
                       @exported_at_utc,
                       @export_path,
                       @removed_through_sequence,
                       @removed_through_created_at_utc,
                       @removed_through_event_hash,
                       @exported_event_count)
                   """,
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("id", checkpoint.Id);
            insert.Parameters.AddWithValue("cutoff_utc", checkpoint.CutoffUtc);
            insert.Parameters.AddWithValue("exported_at_utc", checkpoint.ExportedAtUtc);
            insert.Parameters.AddWithValue("export_path", checkpoint.ExportPath);
            insert.Parameters.AddWithValue("removed_through_sequence", checkpoint.RemovedThroughSequence);
            insert.Parameters.AddWithValue("removed_through_created_at_utc", checkpoint.RemovedThroughCreatedAtUtc);
            insert.Parameters.AddWithValue("removed_through_event_hash", checkpoint.RemovedThroughEventHash);
            insert.Parameters.AddWithValue("exported_event_count", checkpoint.ExportedEventCount);
            insert.ExecuteNonQuery();
        }

        using (var delete = new NpgsqlCommand(
                   """
                   DELETE FROM audit_events
                   WHERE sequence_number <= @removed_through_sequence
                   """,
                   connection,
                   transaction))
        {
            delete.Parameters.AddWithValue("removed_through_sequence", checkpoint.RemovedThroughSequence);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return checkpoint;
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

    private Alert AddAlert(NpgsqlConnection connection, NpgsqlTransaction? transaction, HealthSeverity severity, string title, string description, string targetType, string targetId, string? tenantId = null)
    {
        var alert = new Alert(
            Guid.NewGuid().ToString("n"),
            severity,
            title,
            description,
            targetType,
            targetId,
            DateTimeOffset.UtcNow,
            tenantId ?? _bootstrapSettingsProvider.GetSettings().DefaultTenantId);

        using var command = new NpgsqlCommand(
            """
            INSERT INTO alerts (id, severity, title, description, target_type, target_id, created_at_utc, tenant_id)
            VALUES (@id, @severity, @title, @description, @target_type, @target_id, @created_at_utc, @tenant_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", alert.Id);
        command.Parameters.AddWithValue("severity", alert.Severity.ToString());
        command.Parameters.AddWithValue("title", alert.Title);
        command.Parameters.AddWithValue("description", alert.Description);
        command.Parameters.AddWithValue("target_type", alert.TargetType);
        command.Parameters.AddWithValue("target_id", alert.TargetId);
        command.Parameters.AddWithValue("created_at_utc", alert.CreatedAtUtc);
        command.Parameters.AddWithValue("tenant_id", alert.TenantId);
        command.ExecuteNonQuery();
        return alert;
    }

    private void AddAudit(NpgsqlConnection connection, string actor, string action, string targetType, string targetId, string outcome, string detail, string? tenantId = null)
    {
        using var transaction = connection.BeginTransaction();
        AddAudit(connection, transaction, actor, action, targetType, targetId, outcome, detail, tenantId);
        transaction.Commit();
    }

    private void AddAudit(NpgsqlConnection connection, NpgsqlTransaction? transaction, string actor, string action, string targetType, string targetId, string outcome, string detail, string? tenantId = null)
    {
        var auditEvent = CreateNextAuditEvent(connection, transaction, actor, action, targetType, targetId, outcome, detail, tenantId);
        InsertAuditEvent(connection, transaction, auditEvent);
    }

    private AuditEvent CreateNextAuditEvent(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string actor,
        string action,
        string targetType,
        string targetId,
        string outcome,
        string detail,
        string? tenantId = null)
    {
        LockAuditChain(connection, transaction);

        using var latestCommand = new NpgsqlCommand(
            """
            SELECT sequence_number, event_hash
            FROM audit_events
            WHERE sequence_number IS NOT NULL
            ORDER BY sequence_number DESC
            LIMIT 1
            """,
            connection,
            transaction);
        using var reader = latestCommand.ExecuteReader();

        long previousSequence = 0;
        string? previousHash = null;
        if (reader.Read())
        {
            previousSequence = reader.GetInt64(0);
            previousHash = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return AuditChain.CreateNext(
            previousSequence,
            previousHash,
            Guid.NewGuid().ToString("n"),
            actor,
            action,
            targetType,
            targetId,
            DateTimeOffset.UtcNow,
            outcome,
            detail,
            tenantId ?? _bootstrapSettingsProvider.GetSettings().DefaultTenantId);
    }

    private static void InsertAuditEvent(NpgsqlConnection connection, NpgsqlTransaction? transaction, AuditEvent auditEvent)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events (id, sequence_number, actor, action, target_type, target_id, created_at_utc, outcome, detail, previous_event_hash, event_hash, tenant_id)
            VALUES (@id, @sequence_number, @actor, @action, @target_type, @target_id, @created_at_utc, @outcome, @detail, @previous_event_hash, @event_hash, @tenant_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", auditEvent.Id);
        command.Parameters.AddWithValue("sequence_number", auditEvent.Sequence);
        command.Parameters.AddWithValue("actor", auditEvent.Actor);
        command.Parameters.AddWithValue("action", auditEvent.Action);
        command.Parameters.AddWithValue("target_type", auditEvent.TargetType);
        command.Parameters.AddWithValue("target_id", auditEvent.TargetId);
        command.Parameters.AddWithValue("created_at_utc", auditEvent.CreatedAtUtc);
        command.Parameters.AddWithValue("outcome", auditEvent.Outcome);
        command.Parameters.AddWithValue("detail", auditEvent.Detail);
        command.Parameters.AddWithValue("previous_event_hash", (object?)auditEvent.PreviousEventHash ?? DBNull.Value);
        command.Parameters.AddWithValue("event_hash", auditEvent.EventHash);
        command.Parameters.AddWithValue("tenant_id", auditEvent.TenantId);
        command.ExecuteNonQuery();
    }

    private static void LockAuditChain(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = new NpgsqlCommand("LOCK TABLE audit_events IN EXCLUSIVE MODE", connection, transaction);
        command.ExecuteNonQuery();
    }

    private static void EnableAuditMaintenance(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = new NpgsqlCommand("SET LOCAL owlprotect.allow_audit_maintenance = 'on'", connection, transaction);
        command.ExecuteNonQuery();
    }

    private async Task BackfillAuditChainAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var check = new NpgsqlCommand(
            """
            SELECT COUNT(1)
            FROM audit_events
            WHERE sequence_number IS NULL
               OR event_hash IS NULL
            """,
            connection);
        var missingAuditChain = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken));
        if (missingAuditChain == 0)
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        EnableAuditMaintenance(connection, transaction);

        var rebuiltEvents = new List<AuditEvent>();
        AuditEvent? previous = null;
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT id, actor, action, target_type, target_id, created_at_utc, outcome, detail, tenant_id
                         FROM audit_events
                         ORDER BY created_at_utc, id
                         """,
                         connection,
                         transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                previous = AuditChain.CreateNext(
                    previous,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? _bootstrapSettingsProvider.GetSettings().DefaultTenantId : reader.GetString(8));
                rebuiltEvents.Add(previous);
            }
        }

        foreach (var auditEvent in rebuiltEvents)
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE audit_events
                SET sequence_number = @sequence_number,
                    previous_event_hash = @previous_event_hash,
                    event_hash = @event_hash
                WHERE id = @id
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue("id", auditEvent.Id);
            update.Parameters.AddWithValue("sequence_number", auditEvent.Sequence);
            update.Parameters.AddWithValue("previous_event_hash", (object?)auditEvent.PreviousEventHash ?? DBNull.Value);
            update.Parameters.AddWithValue("event_hash", auditEvent.EventHash);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Backfilled audit chain metadata for {AuditEventCount} audit events.", rebuiltEvents.Count);
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
            ReadStringArray(reader, 7),
            reader.GetString(8));

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
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetString(11),
            Enum.Parse<DeviceRegistrationState>(reader.GetString(12), ignoreCase: true),
            Enum.Parse<DeviceEnrollmentKind>(reader.GetString(13), ignoreCase: true),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
            reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
            ReadStringArray(reader, 20));

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
            reader.GetInt32(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));

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
        command.Parameters.AddWithValue("tenant_id", gateway.TenantId);
    }
}
