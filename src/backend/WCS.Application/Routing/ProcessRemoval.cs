namespace WCS.Application.Routing;

/// <summary>CR-055: what removing a process does.</summary>
public static class ProcessRemovalActions
{
    public const string Delete = "Delete";
    /// <summary>Kept because it has transport orders: switched off (hidden from the default list).</summary>
    public const string Disable = "Disable";
    public const string None = "None";
}

public sealed record ProcessRemovalDto(string Action, string? BlockCode, string? BlockMessage, int Orders, int OpenOrders);
public sealed record ProcessRemovalResult(Guid Id, string Code, string Outcome, int Versions, int Orders);
public sealed record VersionRemovalResult(Guid ProcessId, string ProcessCode, int Number);
