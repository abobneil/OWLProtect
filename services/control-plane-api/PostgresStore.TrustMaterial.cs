using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore
{
    public IReadOnlyList<MachineTrustMaterial> ListTrustMaterials()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, kind, subject_id, subject_name, thumbprint, certificate_pem, issued_at_utc, not_before_utc, expires_at_utc, rotate_after_utc, revoked_at_utc, replaced_by_id
            FROM machine_trust_materials
            ORDER BY issued_at_utc DESC
            """,
            connection);
        using var reader = command.ExecuteReader();

        var items = new List<MachineTrustMaterial>();
        while (reader.Read())
        {
            items.Add(MapMachineTrustMaterial(reader));
        }

        return items;
    }

    public IReadOnlyList<MachineTrustMaterial> ListTrustMaterials(MachineTrustSubjectKind kind, string subjectId)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, kind, subject_id, subject_name, thumbprint, certificate_pem, issued_at_utc, not_before_utc, expires_at_utc, rotate_after_utc, revoked_at_utc, replaced_by_id
            FROM machine_trust_materials
            WHERE kind = @kind
              AND subject_id = @subject_id
            ORDER BY issued_at_utc DESC
            """,
            connection);
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("subject_id", subjectId);
        using var reader = command.ExecuteReader();

        var items = new List<MachineTrustMaterial>();
        while (reader.Read())
        {
            items.Add(MapMachineTrustMaterial(reader));
        }

        return items;
    }

    public MachineTrustMaterial? GetTrustMaterial(string trustMaterialId)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, kind, subject_id, subject_name, thumbprint, certificate_pem, issued_at_utc, not_before_utc, expires_at_utc, rotate_after_utc, revoked_at_utc, replaced_by_id
            FROM machine_trust_materials
            WHERE id = @id
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("id", trustMaterialId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMachineTrustMaterial(reader) : null;
    }

    public IssuedMachineTrustMaterial IssueTrustMaterial(MachineTrustSubjectKind kind, string subjectId, string subjectName, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (HasActiveTrustMaterial(connection, transaction, kind, subjectId))
        {
            throw new InvalidOperationException($"Active trust material already exists for {kind.ToString().ToLowerInvariant()} '{subjectId}'. Rotate it instead.");
        }

        var issued = MachineTrustFactory.Create(kind, subjectId, subjectName);
        InsertMachineTrustMaterial(connection, transaction, issued.Material);
        AddAudit(connection, transaction, actor, "issue-machine-trust", kind.ToString().ToLowerInvariant(), subjectId, "success", $"Issued trust material {issued.Material.Id}.");
        transaction.Commit();
        return issued;
    }

    public IssuedMachineTrustMaterial RotateTrustMaterial(MachineTrustSubjectKind kind, string subjectId, string subjectName, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var activeMaterialIds = ListActiveTrustMaterialIds(connection, transaction, kind, subjectId);
        if (activeMaterialIds.Count == 0)
        {
            throw new InvalidOperationException($"No active trust material exists for {kind.ToString().ToLowerInvariant()} '{subjectId}'.");
        }

        var issued = MachineTrustFactory.Create(kind, subjectId, subjectName);
        InsertMachineTrustMaterial(connection, transaction, issued.Material);

        using var revoke = new NpgsqlCommand(
            """
            UPDATE machine_trust_materials
            SET revoked_at_utc = NOW(),
                replaced_by_id = @replaced_by_id
            WHERE id = ANY(@ids)
            """,
            connection,
            transaction);
        revoke.Parameters.AddWithValue("replaced_by_id", issued.Material.Id);
        revoke.Parameters.AddWithValue("ids", activeMaterialIds.ToArray());
        revoke.ExecuteNonQuery();

        AddAudit(connection, transaction, actor, "rotate-machine-trust", kind.ToString().ToLowerInvariant(), subjectId, "success", $"Rotated trust material to {issued.Material.Id}.");
        transaction.Commit();
        return issued;
    }

    public bool RevokeTrustMaterial(string trustMaterialId, string actor, string reason)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            UPDATE machine_trust_materials
            SET revoked_at_utc = NOW()
            WHERE id = @id
              AND revoked_at_utc IS NULL
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", trustMaterialId);
        var revoked = command.ExecuteNonQuery() > 0;
        if (revoked)
        {
            AddAudit(connection, transaction, actor, "revoke-machine-trust", "machine-trust", trustMaterialId, "success", reason);
        }

        transaction.Commit();
        return revoked;
    }

    private static MachineTrustMaterial MapMachineTrustMaterial(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            Enum.Parse<MachineTrustSubjectKind>(reader.GetString(1), ignoreCase: true),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));

    private static void InsertMachineTrustMaterial(NpgsqlConnection connection, NpgsqlTransaction transaction, MachineTrustMaterial material)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO machine_trust_materials (
                id,
                kind,
                subject_id,
                subject_name,
                thumbprint,
                certificate_pem,
                issued_at_utc,
                not_before_utc,
                expires_at_utc,
                rotate_after_utc,
                revoked_at_utc,
                replaced_by_id)
            VALUES (
                @id,
                @kind,
                @subject_id,
                @subject_name,
                @thumbprint,
                @certificate_pem,
                @issued_at_utc,
                @not_before_utc,
                @expires_at_utc,
                @rotate_after_utc,
                @revoked_at_utc,
                @replaced_by_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", material.Id);
        command.Parameters.AddWithValue("kind", material.Kind.ToString());
        command.Parameters.AddWithValue("subject_id", material.SubjectId);
        command.Parameters.AddWithValue("subject_name", material.SubjectName);
        command.Parameters.AddWithValue("thumbprint", material.Thumbprint);
        command.Parameters.AddWithValue("certificate_pem", material.CertificatePem);
        command.Parameters.AddWithValue("issued_at_utc", material.IssuedAtUtc);
        command.Parameters.AddWithValue("not_before_utc", material.NotBeforeUtc);
        command.Parameters.AddWithValue("expires_at_utc", material.ExpiresAtUtc);
        command.Parameters.AddWithValue("rotate_after_utc", material.RotateAfterUtc);
        command.Parameters.AddWithValue("revoked_at_utc", (object?)material.RevokedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("replaced_by_id", (object?)material.ReplacedById ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static bool HasActiveTrustMaterial(NpgsqlConnection connection, NpgsqlTransaction transaction, MachineTrustSubjectKind kind, string subjectId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT COUNT(1)
            FROM machine_trust_materials
            WHERE kind = @kind
              AND subject_id = @subject_id
              AND revoked_at_utc IS NULL
              AND expires_at_utc > NOW()
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("subject_id", subjectId);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static List<string> ListActiveTrustMaterialIds(NpgsqlConnection connection, NpgsqlTransaction transaction, MachineTrustSubjectKind kind, string subjectId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM machine_trust_materials
            WHERE kind = @kind
              AND subject_id = @subject_id
              AND revoked_at_utc IS NULL
              AND expires_at_utc > NOW()
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("subject_id", subjectId);
        using var reader = command.ExecuteReader();

        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }
}
