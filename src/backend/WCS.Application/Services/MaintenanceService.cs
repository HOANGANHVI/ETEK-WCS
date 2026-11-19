using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Domain.Common;
using WCS.Domain.Users;

namespace WCS.Application.Services;

/// <summary>CR-026: reads and switches the Maintenance flag (Admin only at the API). Every switch is audited in the SAME transaction as the change.</summary>
public sealed class MaintenanceService(IMaintenanceStore store, IUnitOfWork uow, IAuditLog audit, IWcsNotifier notifier, IClock clock)
{
    public async Task<MaintenanceView> GetAsync(CancellationToken ct = default) => View(await store.GetAsync(ct));

    public async Task<MaintenanceView> SetAsync(bool enabled, string? reason, ActorInfo actor, CancellationToken ct = default)
    {
        var view = await uow.ExecuteAsync(async t =>
        {
            var row = await store.GetForUpdateAsync(t);   // waits for every unit that is about to start new work; later units see the new value
            var was = row.Enabled;
            row.Set(enabled, reason, actor.Username, clock.UtcNow);
            audit.Enlist(new AuditRecord(enabled ? "SYSTEM_MAINTENANCE_ON" : "SYSTEM_MAINTENANCE_OFF", actor.Username, actor.Role.ToString(), "System", "maintenance",
                AuditResult.Success, row.Reason is null ? (was == enabled ? "unchanged" : null) : $"reason: {row.Reason}", new { Enabled = was }, new { Enabled = enabled }, null));
            return View(row);
        }, ct);
        await notifier.PublishAsync([new WcsEvent("MaintenanceChanged", view)], ct);
        return view;
    }

    private static MaintenanceView View(SystemMaintenance m) => new(m.Enabled, m.Reason, m.ChangedBy, m.Enabled || m.ChangedBy is not null ? m.UpdatedAt : null);
}
