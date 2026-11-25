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

    /// <summary>
    /// CR-026: runs <paramref name="work"/> in ONE database transaction in which every <see cref="ExecuteAsync{T}"/> call made by the work JOINS the open
    /// transaction (it is flushed to the transaction but not committed). Any exception rolls the whole batch back; a concurrency conflict re-runs the whole batch
    /// (so the work must be repeatable). Outside a batch, nested units of work are still refused. Realtime events published inside a batch are held until
    /// the batch has committed and are dropped on rollback (<see cref="BatchEffects"/>).
    /// Default implementation (stand-ins in tests): the work simply runs once.
    /// </summary>
    Task<T> ExecuteBatchAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => ExecuteAsync(work, ct);

    /// <summary>CR-026: a batch that reads ONE consistent snapshot (REPEATABLE READ), used by Export so every group comes from the same moment. Default: the work runs once.</summary>
    Task<T> ExecuteSnapshotAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => ExecuteAsync(work, ct);
}

/// <summary>CR-008B-2: durable store of detected station requests (see <see cref="WCS.Domain.Intake.StationRequestRecord"/>).</summary>
public interface IStationRequestRepository
{
    Task<WCS.Domain.Intake.StationRequestRecord?> GetByKeyAsync(string requestKey, CancellationToken ct = default);
    Task<IReadOnlyList<WCS.Domain.Intake.StationRequestRecord>> ListPendingAsync(int take, CancellationToken ct = default);
    void Add(WCS.Domain.Intake.StationRequestRecord record);
}

public interface IAgvRepository
{
    Task<Agv?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Agv?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Agv?> GetByRoleAsync(AgvRole role, CancellationToken ct = default);
    Task<IReadOnlyList<Agv>> ListAsync(CancellationToken ct = default);
    /// <summary>CR-008 closure: reads the CURRENT committed hold flag with a fresh query (never the tracked, possibly stale entity). Dispatch decisions call it AFTER the active-task check.</summary>
    Task<bool> IsOnDispatchHoldAsync(Guid agvId, CancellationToken ct = default);
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
    /// <summary>CR-040: the rack that carries this external id.</summary>
    Task<Rack?> FindByExternalIdAsync(string externalRackId, CancellationToken ct = default);
    Task<IReadOnlyList<Rack>> ListAsync(Guid? locationId, RackStatus? status, int take, CancellationToken ct = default);
    Task<int> CountAtLocationAsync(Guid locationId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> CountByLocationAsync(CancellationToken ct = default);
    /// <summary>First rack at the location with the given status that has no ACTIVE reservation (deterministic order).</summary>
    Task<Rack?> FindUnreservedAsync(Guid locationId, RackStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Rack>> ListUnreservedAsync(Guid locationId, RackStatus status, int take, CancellationToken ct = default);
    /// <summary>
    /// CR-036: the same query with a rack filter (product model / manual handling). A NEW overload on purpose: the overload above (used by the legacy flows) is unchanged.
    /// </summary>
    Task<Rack?> FindUnreservedAsync(Guid locationId, RackStatus status, RackSelection selection, CancellationToken ct = default);
}

/// <summary>
/// CR-036: filter of an automatic source search. <see cref="ProductModelId"/> set = only racks of exactly that model (a rack without a model is NOT selected);
/// <see cref="RequireNoModel"/> = only racks without a model (a process without a model never takes a modelled rack);
/// <see cref="AutoOnly"/> = racks in manual handling are never selected.
/// </summary>
public sealed record RackSelection(Guid? ProductModelId, bool RequireNoModel, bool AutoOnly);

/// <summary>CR-036: product models.</summary>
public interface IProductModelRepository
{
    Task<ProductModel?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductModel?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ProductModel>> ListAsync(CancellationToken ct = default);
    void Add(ProductModel model);
}

public interface IReservationRepository
{
    void Add(Reservation reservation);
    /// <summary>Removes every reservation row (active or released) of a task that is being hard-deleted.</summary>
    Task RemoveAllForTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<Reservation>> ListActiveByTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<int> CountActiveDestinationAsync(Guid locationId, CancellationToken ct = default);
    /// <summary>CR-036: true when an ACTIVE reservation holds the rack.</summary>
    Task<bool> HasActiveForRackAsync(Guid rackId, CancellationToken ct = default);
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
    void Remove(TransportTask task);
    Task<TransportTask?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TransportTask?> GetByRequestKeyAsync(string requestKey, CancellationToken ct = default);
    Task<TransportTask?> GetByRcsTaskCodeAsync(string rcsTaskCode, CancellationToken ct = default);
    Task<TransportTask?> GetActiveForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<bool> AnyQueuedForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<TransportTask?> NextQueuedForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<IReadOnlyList<TransportTask>> ListByStatesAsync(IReadOnlyCollection<TaskState> states, int take, CancellationToken ct = default);
    Task<(IReadOnlyList<TransportTask> Items, int Total)> QueryAsync(TaskQuery query, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Guid>> ActiveTaskIdByAgvAsync(CancellationToken ct = default);
    /// <summary>CR-028: number of tasks in the given states (fleet-level limit of tasks in flight).</summary>
    Task<int> CountByStatesAsync(IReadOnlyCollection<TaskState> states, CancellationToken ct = default);
    /// <summary>CR-028: tasks of the RcsManaged dispatch that are not finished (oldest first). Tracked, so the caller may change them inside its unit of work.</summary>
    Task<IReadOnlyList<TransportTask>> ListActiveRcsManagedAsync(int take, CancellationToken ct = default);
    /// <summary>CR-028: tasks that are not finished and belong to the given dispatch mode (RcsManaged = RcsDispatchState is set; Legacy = it is not).</summary>
    Task<int> CountActiveByModeAsync(bool rcsManaged, CancellationToken ct = default);
    /// <summary>CR-008B-3: tasks whose physical outcome still has to be reconciled by an Admin (newest first).</summary>
    Task<IReadOnlyList<TransportTask>> ListRequiringReconciliationAsync(int take, CancellationToken ct = default);
}

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(string source, string eventKey, CancellationToken ct = default);
    void Add(ProcessedEvent processed);
    Task RemoveForTaskAsync(Guid taskId, CancellationToken ct = default);
}
