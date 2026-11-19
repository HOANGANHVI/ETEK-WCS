using Microsoft.EntityFrameworkCore;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Maintenance;

internal sealed class MaintenanceStore(WcsDbContext db) : IMaintenanceStore
{
    public async Task<SystemMaintenance> GetAsync(CancellationToken ct = default) =>
        await db.SystemMaintenance.AsNoTracking().SingleAsync(x => x.Id == SystemMaintenance.SingletonId, ct);

    public async Task<SystemMaintenance> GetForUpdateAsync(CancellationToken ct = default)
    {
        RequireTransaction(db);
        // exclusive row lock first: it waits for every transaction that holds the share lock (units about to start new work), then the tracked change is saved
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM system_maintenance WHERE \"Id\" = 1 FOR UPDATE", ct);
        return await db.SystemMaintenance.SingleAsync(x => x.Id == SystemMaintenance.SingletonId, ct);
    }

    internal static void RequireTransaction(WcsDbContext db)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("The Maintenance row lock must be taken inside a unit of work.");
    }
}

internal sealed class MaintenanceGate(WcsDbContext db) : IMaintenanceGate
{
    public Task<bool> IsEnabledAsync(CancellationToken ct = default) =>
        db.SystemMaintenance.AsNoTracking().Where(x => x.Id == SystemMaintenance.SingletonId).Select(x => x.Enabled).SingleOrDefaultAsync(ct);

    public async Task EnsureOpenAsync(CancellationToken ct = default)
    {
        if (await HoldNewWorkAsync(ct)) throw AppException.Rejected(MaintenanceCodes.Code, MaintenanceCodes.Message);
    }

    public async Task<bool> HoldNewWorkAsync(CancellationToken ct = default)
    {
        MaintenanceStore.RequireTransaction(db);
        // already on (committed): holding is correct whatever happens next, and no lock is needed (the scheduler asks every tick while Maintenance lasts)
        if (await IsEnabledAsync(ct)) return true;
        // SELECT ... FOR SHARE: many units may share it; switching the flag (FOR UPDATE) waits for them, and a unit that starts later reads the committed new value
        var enabled = await db.Database.SqlQueryRaw<bool>("SELECT \"Enabled\" AS \"Value\" FROM system_maintenance WHERE \"Id\" = 1 FOR SHARE").ToListAsync(ct);
        return enabled.Count == 1 && enabled[0];
    }
}
