using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Plc;

/// <summary>CR-044: the write decisions of the PLC gateway in <c>audit_log</c>. Unlike the general audit log it does NOT swallow a failure: the caller refuses the write then.</summary>
internal sealed class PlcWriteAudit(IServiceScopeFactory scopes, IClock clock) : IPlcWriteAudit
{
    public async Task RecordAsync(PlcWriteAuditRecord r, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        var result = r.Denied ? AuditResult.Denied : r.Success ? AuditResult.Success : AuditResult.Failed;
        var detail = $"{r.PlcId} {r.StationId}/{r.Signal} = {r.Value} at {r.Address ?? "(no address)"}, mode {r.Mode}{(r.Code is null ? "" : $", {r.Code}")}";
        db.AuditLog.Add(new AuditEntry(clock.UtcNow, r.Actor, null, r.Action, "StationIoPoint", r.PointId?.ToString() ?? $"{r.StationId}/{r.Signal}", result,
            detail.Length > 1000 ? detail[..1000] : detail, null, JsonSerializer.Serialize(new { r.PlcId, r.StationId, r.Signal, r.Address, r.Value, r.Mode, r.Code }), null));
        await db.SaveChangesAsync(ct);
    }
}
