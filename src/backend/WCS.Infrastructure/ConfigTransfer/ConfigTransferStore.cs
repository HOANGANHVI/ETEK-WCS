using Microsoft.EntityFrameworkCore;
using WCS.Application.ConfigTransfer;
using WCS.Domain.Common;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.ConfigTransfer;

internal sealed class ConfigTransferStore(WcsDbContext db) : IConfigTransferStore
{
    public Task<ConfigTransferSession?> GetAsync(Guid id, CancellationToken ct = default) => db.ConfigTransferSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(ConfigTransferSession session) => db.ConfigTransferSessions.Add(session);
    public async Task<string> LatestMigrationAsync(CancellationToken ct = default) => (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "none";
}
