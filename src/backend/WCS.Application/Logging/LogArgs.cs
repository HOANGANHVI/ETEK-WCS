namespace WCS.Application.Logging;

/// <summary>Builds the parameter dictionary of a log event (PLC events: stored as <c>args</c> in the details JSON).</summary>
public static class LogArgs
{
    public static IReadOnlyDictionary<string, object?> Of(params (string Key, object? Value)[] items) => items.ToDictionary(x => x.Key, x => x.Value);
}
