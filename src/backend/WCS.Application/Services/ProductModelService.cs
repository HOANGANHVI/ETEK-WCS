using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Inventory;

namespace WCS.Application.Services;

public sealed record ProductModelDto(Guid Id, string Code, string Name, bool Enabled);

public sealed record RackModelAssignmentDto(Guid RackId, Guid? ProductModelId, string? ProductModelCode, bool Changed);

/// <summary>
/// CR-036 (Batch 1). Product model administration and the audited assignment of a model to an EXISTING rack (option A of P2-OQ-04: the model is declared by a person; it is never
/// inferred from the location). A rack that already has a model never gets a different one here. Callers must be Admin-authorized and audit the call.
/// </summary>
public sealed class ProductModelService(IUnitOfWork uow, IProductModelRepository models, IRackRepository racks, ILocationRepository locations, IReservationRepository reservations,
    IInventoryTransactionRepository transactions, IWcsNotifier notifier, IClock clock)
{
    private static ProductModelDto ToDto(ProductModel m) => new(m.Id, m.Code, m.Name, m.Enabled);

    public Task<IReadOnlyList<ProductModelDto>> ListAsync(CancellationToken ct = default) =>
        uow.ExecuteAsync<IReadOnlyList<ProductModelDto>>(async t => (await models.ListAsync(t)).Select(ToDto).ToList(), ct);

    public Task<ProductModelDto> CreateAsync(string code, string name, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        ProductModel m;
        try { m = new ProductModel(code, name, clock.UtcNow); } catch (DomainException e) { throw AppException.Validation(e.Code, e.Message); }
        if (await models.GetByCodeAsync(m.Code, t) is not null) throw AppException.Conflict("MODEL_CODE_EXISTS", $"Product model {m.Code} already exists.");
        models.Add(m);
        return ToDto(m);
    }, ct);

    public Task<ProductModelDto> UpdateAsync(Guid id, string name, bool enabled, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var m = await models.GetAsync(id, t) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
        try { m.Update(name, enabled, clock.UtcNow); } catch (DomainException e) { throw AppException.Validation(e.Code, e.Message); }
        return ToDto(m);
    }, ct);

    /// <summary>
    /// Gives a model to a rack that has none. Refused while the rack is in transit or reserved (a rack must not change meaning under a running job), when it already has another model,
    /// and when the rack stands at a location of a different model. Writes the audit record (<see cref="InventoryEventType.ModelAssigned"/>) in the same transaction.
    /// </summary>
    public async Task<RackModelAssignmentDto> AssignRackModelAsync(Guid rackId, Guid modelId, string? user, string? reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required to assign a product model to a rack.");
        var res = await uow.ExecuteAsync(async t =>
        {
            var rack = await racks.GetAsync(rackId, t) ?? throw AppException.NotFound("RACK_NOT_FOUND", "Rack not found.");
            var model = await models.GetAsync(modelId, t) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
            if (!model.Enabled) throw AppException.Rejected("MODEL_DISABLED", $"Product model {model.Code} is disabled.");
            if (rack.ProductModelId == modelId) return new RackModelAssignmentDto(rack.Id, modelId, model.Code, false);   // idempotent
            if (rack.ProductModelId is not null) throw AppException.Conflict("RACK_MODEL_ALREADY_SET", "The rack already has a different product model; changing it needs a separate approved procedure.");
            if (rack.Status == RackStatus.InTransit) throw AppException.Conflict("RACK_IN_TRANSIT", "The rack is in transit; assign the model when it stands at a location.");
            if (await reservations.HasActiveForRackAsync(rack.Id, t)) throw AppException.Conflict("RACK_RESERVED", "The rack is reserved by an active job.");
            if (rack.CurrentLocationId is { } lid && await locations.GetAsync(lid, t) is { } loc && loc.ProductModelId is { } lm && lm != modelId)
                throw AppException.Conflict("RACK_MODEL_LOCATION_MISMATCH", $"The rack stands at {loc.Code}, which belongs to another product model.");
            rack.AssignModel(modelId);
            transactions.Add(new InventoryTransaction(InventoryEventType.ModelAssigned, rack.Id, null, rack.CurrentLocationId, null, rack.Status, rack.Status,
                ConfirmationSource.Operator, user, BindOutcome.NotRequested, $"Product model {model.Code} assigned: {reason.Trim()}", clock.UtcNow));
            return new RackModelAssignmentDto(rack.Id, modelId, model.Code, true);
        }, ct);
        if (res.Changed) await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { rack = rackId })], ct);
        return res;
    }
}
