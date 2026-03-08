using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OWLProtect.Core;

public sealed record AuditEventDraft(
    string Id,
    string Actor,
    string Action,
    string TargetType,
    string TargetId,
    DateTimeOffset CreatedAtUtc,
    string Outcome,
    string Detail,
    string TenantId = SeedData.DefaultTenantId);

public static class AuditChain
{
    public static IReadOnlyList<AuditEvent> CreateSeedChain(IEnumerable<AuditEventDraft> drafts)
    {
        var events = new List<AuditEvent>();
        AuditEvent? previous = null;
        foreach (var draft in drafts.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            previous = CreateNext(previous, draft);
            events.Add(previous);
        }

        return events;
    }

    public static AuditEvent CreateNext(
        AuditEvent? previous,
        string id,
        string actor,
        string action,
        string targetType,
        string targetId,
        DateTimeOffset createdAtUtc,
        string outcome,
        string detail,
        string tenantId = SeedData.DefaultTenantId) =>
        CreateNext(previous, new AuditEventDraft(id, actor, action, targetType, targetId, createdAtUtc, outcome, detail, tenantId));

    public static AuditEvent CreateNext(
        long previousSequence,
        string? previousHash,
        string id,
        string actor,
        string action,
        string targetType,
        string targetId,
        DateTimeOffset createdAtUtc,
        string outcome,
        string detail,
        string tenantId = SeedData.DefaultTenantId)
    {
        var sequence = previousSequence + 1;
        var eventHash = ComputeHash(sequence, actor, action, targetType, targetId, createdAtUtc, outcome, detail, previousHash, tenantId);

        return new AuditEvent(
            id,
            sequence,
            actor,
            action,
            targetType,
            targetId,
            createdAtUtc,
            outcome,
            detail,
            previousHash,
            eventHash,
            tenantId);
    }

    public static AuditEvent CreateNext(AuditEvent? previous, AuditEventDraft draft) =>
        CreateNext(
            previous?.Sequence ?? 0,
            previous?.EventHash,
            draft.Id,
            draft.Actor,
            draft.Action,
            draft.TargetType,
            draft.TargetId,
            draft.CreatedAtUtc,
            draft.Outcome,
            draft.Detail,
            draft.TenantId);

    public static string ComputeHash(
        long sequence,
        string actor,
        string action,
        string targetType,
        string targetId,
        DateTimeOffset createdAtUtc,
        string outcome,
        string detail,
        string? previousHash,
        string tenantId = SeedData.DefaultTenantId)
    {
        var payload = string.Join('\n',
            sequence.ToString(CultureInfo.InvariantCulture),
            actor,
            action,
            targetType,
            targetId,
            createdAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            outcome,
            detail,
            tenantId,
            previousHash ?? string.Empty);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
