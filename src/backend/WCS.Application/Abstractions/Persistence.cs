using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Application.Abstractions;

public interface IClock { DateTimeOffset UtcNow { get; } }

public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction, saves, and commits. If a concurrency/unique conflict
    /// occurs the tracker is cleared and the work is re-run (it must therefore re-read everything it needs).
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}

public interface IAgvRepository
{
    Task<Agv?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Agv?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Agv?> GetByRoleAsync(AgvRole role, CancellationToken ct = default);
    Task<IReadOnlyList<Agv>> ListAsync(CancellationToken ct = default);
    void Add(Agv agv);
}

public interface ILocationRepository
{
    Task<Location?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Location?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default);
    void Add(Location location);
}

public interface IRackRepository
{
    Task<Rack?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(Rack rack);
    void Remove(Rack rack);
    Task<IReadOnlyList<Rack>> ListAsync(Guid? locationId, RackStatus? status, int take, CancellationToken ct = default);
    Task<int> CountAtLocationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> CountByLocationAsync(CancellationToken ct = default);
    /// <summary>First rack at the location with the given status that has no ACTIVE reservation (deterministic order).</summary>
    Task<Rack?> FindUnreservedAsync(Guid locationId, RackStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Rack>> ListUnreservedAsync(Guid locationId, RackStatus status, int take, CancellationToken ct = default);
}

public interface IReservationRepository
{
    void Add(Reservation reservation);
    Task<IReadOnlyList<Reservation>> ListActiveByTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<int> CountActiveDestinationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> CountActiveDestinationByLocationAsync(CancellationToken ct = default);
}

public interface IInventoryTransactionRepository
{
    void Add(InventoryTransaction tx);
    Task<IReadOnlyList<InventoryTransaction>> ListAsync(Guid? locationId, Guid? taskId, int take, CancellationToken ct = default);
}

public sealed record TaskQuery(TaskState? State, Guid? AgvId, int Skip, int Take);

public interface ITransportTaskRepository
{
    void Add(TransportTask task);
    Task<TransportTask?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TransportTask?> GetByRequestKeyAsync(string requestKey, CancellationToken ct = default);
    Task<TransportTask?> GetByRcsTaskCodeAsync(string rcsTaskCode, CancellationToken ct = default);
    Task<TransportTask?> GetActiveForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<bool> AnyQueuedForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<TransportTask?> NextQueuedForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<IReadOnlyList<TransportTask>> ListByStatesAsync(IReadOnlyCollection<TaskState> states, int take, CancellationToken ct = default);
    Task<(IReadOnlyList<TransportTask> Items, int Total)> QueryAsync(TaskQuery query, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Guid>> ActiveTaskIdByAgvAsync(CancellationToken ct = default);
}

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(string source, string eventKey, CancellationToken ct = default);
    void Add(ProcessedEvent processed);
}
