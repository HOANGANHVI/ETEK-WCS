namespace WCS.Application.Routing;

/// <summary>CR-055 / CR-056: what removing a process does.</summary>
public static class ProcessRemovalActions
{
    public const string Delete = "Delete";
    public const string None = "None";
}

/// <summary><see cref="Orders"/> = finished orders that stay after a removal; <see cref="HeldPorts"/> = ports still held by an order of the process.</summary>
public sealed record ProcessRemovalDto(string Action, string? BlockCode, string? BlockMessage, int Orders, int OpenOrders, IReadOnlyList<string> HeldPorts);
public sealed record ProcessRemovalResult(Guid Id, string Code, string Outcome, int Versions, int Orders);
public sealed record VersionRemovalResult(Guid ProcessId, string ProcessCode, int Number);
