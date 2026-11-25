namespace WCS.Api.Api;

public sealed record RackCreateRequest([property: System.ComponentModel.DataAnnotations.Required] string LocationCode, WCS.Domain.Common.RackKind RackKind, string? ExternalRackId, Guid? ProductModelId, [property: System.ComponentModel.DataAnnotations.Required] string Reason);
public sealed record RackUpdateRequest(string? LocationCode, WCS.Domain.Common.RackKind? RackKind, string? ExternalRackId, [property: System.ComponentModel.DataAnnotations.Required] string Reason);
