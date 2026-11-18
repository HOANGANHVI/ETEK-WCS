using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

public interface IPlcConfigRepository
{
    Task<IReadOnlyList<PlcConnection>> ListConnectionsAsync(CancellationToken ct = default);
    Task<PlcConnection?> GetConnectionAsync(string plcId, CancellationToken ct = default);
    void AddConnection(PlcConnection c);
    Task<IReadOnlyList<PlcStation>> ListStationsAsync(CancellationToken ct = default);
    Task<PlcStation?> GetStationAsync(string stationId, CancellationToken ct = default);
    void AddStation(PlcStation s);
    Task<IReadOnlyList<StationIoPoint>> ListPointsAsync(string? plcId, string? stationId, CancellationToken ct = default);
    Task<StationIoPoint?> GetPointAsync(Guid id, CancellationToken ct = default);
    Task<StationIoPoint?> FindPointAsync(string plcId, string stationId, string signal, CancellationToken ct = default);
    void AddPoint(StationIoPoint p);
    void AddLog(PlcCommLogEntry e);
    Task<IReadOnlyList<PlcCommLogEntry>> QueryLogsAsync(string? plcId, string? stationId, PlcLogLevel? minLevel, int skip, int take, CancellationToken ct = default);
    Task<int> PurgeLogsAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}

// ---- DTOs (Admin sees configuration; Operator sees monitoring data without addresses or network parameters) -------------------------
public sealed record PlcConnectionView(string PlcId, string Name, string IpAddress, int? Port, string? Protocol, int? NetworkNumber, int? PcNumber,
    int? ModuleIoNumber, int? PlcStationNumber, int? MonitoringTimer, int? TimeoutMs, int? PollingIntervalMs, bool Enabled, string ConfigStatus,
    IReadOnlyList<string> MissingParameters, IReadOnlyList<string> Stations, DateTimeOffset UpdatedAt, string? UpdatedBy,
    int? ConnectTimeoutMs = null, int? ReadTimeoutMs = null, int? WriteTimeoutMs = null, int? XyAddressBase = null, int? WAddressBase = null);

public sealed record UpdatePlcConnectionCommand(string? Name, string IpAddress, int? Port, string? Protocol, int? NetworkNumber, int? PcNumber,
    int? ModuleIoNumber, int? PlcStationNumber, int? MonitoringTimer, int? TimeoutMs, int? PollingIntervalMs, bool Enabled,
    int? ConnectTimeoutMs = null, int? ReadTimeoutMs = null, int? WriteTimeoutMs = null, int? XyAddressBase = null, int? WAddressBase = null);

public sealed record PlcStationView(string StationId, string PlcId, int Order, string? LocationCode, bool Enabled, string Role,
    int PointCount, int ConfiguredPoints, int ApprovedPoints);

public sealed record IoPointView(Guid Id, string PlcId, string StationId, int Seq, string Group, string SignalName, string Direction, string DataType,
    string Description, string ConfigStatus, string? TemplateAddressReference, string? ActualAddress, int? WordLength, string? Encoding,
    bool? WriteAllowed, string? ApprovedBy, DateTimeOffset? ApprovedAt, bool? Pollable = null,
    string? WriteApproval = null, string? WriteDecidedBy = null, DateTimeOffset? WriteDecidedAt = null, string? WriteDecisionNote = null, bool? WriteSupported = null);

public sealed record SetPointAddressCommand(string Address, int? WordLength, string? Encoding);

public static class PlcLabels
{
    /// <summary>CR-014 write approval label: NOT_APPROVED (awaiting a decision) / APPROVED / REJECTED.</summary>
    public static string WriteStatus(WriteApprovalStatus s) => s switch { WriteApprovalStatus.Approved => "APPROVED", WriteApprovalStatus.Rejected => "REJECTED", _ => "NOT_APPROVED" };
    public static string Status(IoConfigStatus s) => s switch { IoConfigStatus.Approved => "APPROVED", IoConfigStatus.Configured => "CONFIGURED", _ => "NEEDS_CONFIGURATION" };
}

/// <summary>
/// Configuration of the 4 PLC connections, 16 Stations and their IO points. It never connects to a device and never invents a value:
/// addresses exist only when an Admin enters them (and the PLC engineer's approval is recorded separately).
/// </summary>
public sealed class PlcConfigService(IPlcConfigRepository repo, IUnitOfWork uow, IClock clock)
{
    public async Task<IReadOnlyList<PlcConnectionView>> ListConnectionsAsync(CancellationToken ct = default)
    {
        var stations = await repo.ListStationsAsync(ct);
        return (await repo.ListConnectionsAsync(ct)).Select(c => ToView(c, stations.Where(s => s.PlcId == c.Id).OrderBy(s => s.Order).Select(s => s.Id).ToList())).ToList();
    }

    public static PlcConnectionView ToView(PlcConnection c, IReadOnlyList<string> stations)
    {
        var missing = c.MissingParameters();
        return new(c.Id, c.Name, c.IpAddress, c.Port, c.Protocol, c.NetworkNumber, c.PcNumber, c.ModuleIoNumber, c.PlcStationNumber, c.MonitoringTimer,
            c.TimeoutMs, c.PollingIntervalMs, c.Enabled, missing.Count == 0 ? "CONFIGURED" : "NEEDS_CONFIGURATION", missing, stations, c.UpdatedAt, c.UpdatedBy,
            c.ConnectTimeoutMs, c.ReadTimeoutMs, c.WriteTimeoutMs, c.XyAddressBase, c.WAddressBase);
    }

    public async Task<PlcConnectionView> UpdateConnectionAsync(string plcId, UpdatePlcConnectionCommand cmd, PlcRuntimeOptions options, string? by, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(cmd.IpAddress) || !ConnectionValidator.IsStrictIp(cmd.IpAddress.Trim())) errors.Add(new("ipAddress", "Địa chỉ IP không hợp lệ.", "IP_INVALID"));
        Range(errors, "port", cmd.Port, 1, 65535, "Cổng TCP");
        Range(errors, "networkNumber", cmd.NetworkNumber, 0, 255, "Network number");
        Range(errors, "pcNumber", cmd.PcNumber, 0, 255, "PC number");
        Range(errors, "moduleIoNumber", cmd.ModuleIoNumber, 0, 65535, "Request destination module I/O number");
        Range(errors, "plcStationNumber", cmd.PlcStationNumber, 0, 255, "PLC station number");
        Range(errors, "monitoringTimer", cmd.MonitoringTimer, 0, 65535, "Monitoring timer");
        Range(errors, "timeoutMs", cmd.TimeoutMs, 100, 60_000, "Timeout (ms)");
        Range(errors, "pollingIntervalMs", cmd.PollingIntervalMs, 50, 60_000, "Chu kỳ đọc (ms)");
        Range(errors, "connectTimeoutMs", cmd.ConnectTimeoutMs, 100, 60_000, "Timeout kết nối (ms)");
        Range(errors, "readTimeoutMs", cmd.ReadTimeoutMs, 100, 60_000, "Timeout đọc (ms)");
        Range(errors, "writeTimeoutMs", cmd.WriteTimeoutMs, 100, 60_000, "Timeout ghi (ms)");
        if (cmd.XyAddressBase is { } xb && !AddressBases.IsValidXy(xb)) errors.Add(new("xyAddressBase", "Cơ số địa chỉ X/Y chỉ có thể là 8 (bát phân) hoặc 16 (thập lục phân).", "XY_BASE_INVALID"));
        if (cmd.WAddressBase is { } wb && !AddressBases.IsValidW(wb)) errors.Add(new("wAddressBase", "Cơ số địa chỉ W chỉ có thể là 10 (thập phân) hoặc 16 (thập lục phân).", "W_BASE_INVALID"));
        if (!string.IsNullOrWhiteSpace(cmd.Protocol) && !ConnectionOptions.PlcProtocols.Contains(cmd.Protocol.Trim()))
            errors.Add(new("protocol", "Giao thức không được hỗ trợ.", "PROTOCOL_UNSUPPORTED"));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        return await uow.ExecuteAsync(async t =>
        {
            var c = await repo.GetConnectionAsync(plcId, t) ?? throw AppException.NotFound("PLC_NOT_FOUND", $"Không tìm thấy {plcId}.");
            var name = string.IsNullOrWhiteSpace(cmd.Name) ? c.Name : cmd.Name.Trim();
            c.Update(name, cmd.IpAddress.Trim(), cmd.Port, ConnectionValidator.Clean(cmd.Protocol), cmd.NetworkNumber, cmd.PcNumber, cmd.ModuleIoNumber,
                cmd.PlcStationNumber, cmd.MonitoringTimer, cmd.TimeoutMs, cmd.PollingIntervalMs, cmd.Enabled, by, clock.UtcNow,
                cmd.ConnectTimeoutMs, cmd.ReadTimeoutMs, cmd.WriteTimeoutMs, cmd.XyAddressBase, cmd.WAddressBase);
            // Switching a REAL connection on needs the complete, confirmed parameter set. The simulator never needs them.
            if (cmd.Enabled && options.Mode != PlcRuntimeMode.Simulator && c.MissingParameters() is { Count: > 0 } missing)
                throw new ValidationFailedException(missing.Select(m => new FieldError(m, "Cần được kỹ sư PLC xác nhận trước khi bật kết nối thật (NEEDS_CONFIGURATION).", "NEEDS_ENGINEER_CONFIRMATION")).ToList());
            var stations = (await repo.ListStationsAsync(t)).Where(s => s.PlcId == c.Id).OrderBy(s => s.Order).Select(s => s.Id).ToList();
            return ToView(c, stations);
        }, ct);
    }

    private static void Range(List<FieldError> e, string field, int? v, int min, int max, string label)
    {
        if (v is { } x && (x < min || x > max)) e.Add(new(field, $"{label} phải nằm trong khoảng {min}–{max}.", "RANGE", FieldArgs.Of(("min", min), ("max", max))));
    }

    public async Task<IReadOnlyList<PlcStationView>> ListStationsAsync(CancellationToken ct = default)
    {
        var points = await repo.ListPointsAsync(null, null, ct);
        return (await repo.ListStationsAsync(ct)).OrderBy(s => s.PlcId).ThenBy(s => s.Order).Select(s =>
        {
            var mine = points.Where(p => p.StationId == s.Id).ToList();
            return new PlcStationView(s.Id, s.PlcId, s.Order, s.LocationCode, s.Enabled, s.Role, mine.Count,
                mine.Count(p => p.Status != IoConfigStatus.NeedsConfiguration), mine.Count(p => p.Status == IoConfigStatus.Approved));
        }).ToList();
    }

    public async Task<IReadOnlyList<IoPointView>> ListPointsAsync(string? plcId, string? stationId, bool includeConfiguration, CancellationToken ct = default)
    {
        var points = await repo.ListPointsAsync(plcId, stationId, ct);
        // CR-012: Pollable = has an address that is valid and does not overlap another point of the same PLC (the other points of that PLC are needed for the overlap check)
        var pollable = new HashSet<Guid>();
        foreach (var group in points.GroupBy(p => p.PlcId))
        {
            var all = stationId is null ? group.ToList() : (await repo.ListPointsAsync(group.Key, null, ct)).ToList();
            pollable.UnionWith(PlcPollablePolicy.Pollable(all, await BasesOfAsync(group.Key, ct)));
        }
        return points.OrderBy(p => p.PlcId).ThenBy(p => p.StationId).ThenBy(p => p.Seq).Select(p => ToView(p, includeConfiguration, pollable.Contains(p.Id))).ToList();
    }

    public static IoPointView ToView(StationIoPoint p, bool includeConfiguration, bool? pollable = null) => new(p.Id, p.PlcId, p.StationId, p.Seq, p.Group, p.SignalName,
        p.Direction.ToString(), p.DataType.ToString(), p.Description, PlcLabels.Status(p.Status),
        includeConfiguration ? p.TemplateAddressReference : null, includeConfiguration ? p.ActualAddress : null, includeConfiguration ? p.WordLength : null,
        includeConfiguration ? p.Encoding : null, includeConfiguration ? p.WriteAllowed : null, includeConfiguration ? p.ApprovedBy : null, includeConfiguration ? p.ApprovedAt : null, includeConfiguration ? pollable ?? p.IsPollable : null,
        PlcLabels.WriteStatus(p.WriteApproval), includeConfiguration ? p.WriteDecidedBy : null, includeConfiguration ? p.WriteDecidedAt : null, includeConfiguration ? p.WriteDecisionNote : null,
        WriteValueValidator.IsWriteSupported(p.DataType, p.Encoding));

    /// <summary>Records an address confirmed by the PLC engineer. Conflicts with other points of the SAME PLC are rejected; other PLCs are independent.</summary>
    public Task<IoPointView> SetAddressAsync(Guid pointId, SetPointAddressCommand cmd, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            var p = await repo.GetPointAsync(pointId, t) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            // CR-004: X/Y are OUT_OF_SCOPE for IO mapping (radix NEEDS_CONFIRMATION). Only D, W, M may be entered. The driver is unchanged.
            if (!string.IsNullOrWhiteSpace(cmd.Address) && char.ToUpperInvariant(cmd.Address.Trim()[0]) is 'X' or 'Y')
                throw new ValidationFailedException([new("address", "Thiết bị X/Y chưa được hỗ trợ trong cấu hình địa chỉ IO (OUT_OF_SCOPE, NEEDS_CONFIRMATION: cần xác nhận cơ số với kỹ sư PLC). Chỉ dùng M, D hoặc W.", "ADDRESS_XY_UNSUPPORTED")]);
            var bases = await BasesOfAsync(p.PlcId, t);
            var address = string.IsNullOrWhiteSpace(cmd.Address) ? cmd.Address?.Trim() : DeviceAddress.Normalize(cmd.Address, bases);
            var spec = new PointSpec(p.Id, p.StationId, p.SignalName, p.DataType, address, cmd.WordLength, p.Direction);
            if (AddressValidator.ValidateOne(spec, bases) is { } issue) throw new ValidationFailedException([new("address", issue.Message, issue.Code, issue.Args)]);
            if (p.DataType == PlcDataType.String && string.IsNullOrWhiteSpace(cmd.Encoding))
                throw new ValidationFailedException([new("encoding", "STRING cần encoding được xác nhận.", "ENCODING_REQUIRED")]);

            var others = (await repo.ListPointsAsync(p.PlcId, null, t)).Where(x => x.Id != p.Id)
                .Select(x => new PointSpec(x.Id, x.StationId, x.SignalName, x.DataType, x.ActualAddress, x.WordLength, x.Direction));
            var conflict = AddressValidator.Validate(others.Append(spec), bases).FirstOrDefault(i => i.PointId == p.Id || i.ConflictWith == p.Id);
            if (conflict is not null) throw new ValidationFailedException([new("address", conflict.Message, conflict.Code, conflict.Args)]);

            p.ConfigureAddress(address!, p.DataType == PlcDataType.String ? cmd.WordLength : null, p.DataType == PlcDataType.String ? cmd.Encoding!.Trim() : null, clock.UtcNow);
            return ToView(p, true);
        }, ct);

    public Task<IoPointView> ClearAddressAsync(Guid pointId, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            var p = await repo.GetPointAsync(pointId, t) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            p.ClearAddress(clock.UtcNow);
            return ToView(p, true);
        }, ct);

    /// <summary>Records that the PLC engineer confirmed the address. The approver is the authenticated Admin; the note names the engineer / document.</summary>
    public Task<IoPointView> ApproveAsync(Guid pointId, string approvedBy, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            var p = await repo.GetPointAsync(pointId, t) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            if (p.Status == IoConfigStatus.NeedsConfiguration) throw AppException.Rejected("IO_NO_ADDRESS", "Cần nhập địa chỉ trước khi xác nhận.");
            p.Approve(approvedBy, clock.UtcNow);
            return ToView(p, true);
        }, ct);

    // ---------------------------------------------------------------------------------------------- bulk READ approval (commissioning of one PLC)
    /// <summary>Classifies every IO point of a PLC for a bulk read-approval without changing anything.</summary>
    private static IReadOnlyList<BulkApprovalItem> Classify(IReadOnlyList<StationIoPoint> points, AddressBases bases)
    {
        var specs = points.Select(x => new PointSpec(x.Id, x.StationId, x.SignalName, x.DataType, x.ActualAddress, x.WordLength, x.Direction)).ToList();
        var issues = AddressValidator.Validate(specs, bases);
        AddressIssue? IssueOf(Guid id) => issues.FirstOrDefault(i => i.PointId == id) ?? issues.FirstOrDefault(i => i.ConflictWith == id);
        return points.Select(p =>
        {
            BulkApprovalItem Item(string outcome, string? code = null, string? reason = null) =>
                new(p.Id, p.StationId, p.SignalName, p.DataType.ToString(), p.ActualAddress, outcome, code, reason);
            if (p.Status == IoConfigStatus.Approved) return Item(BulkOutcome.AlreadyApproved);
            if (p.Status == IoConfigStatus.NeedsConfiguration || string.IsNullOrWhiteSpace(p.ActualAddress))
                return Item(BulkOutcome.NotEligible, "NO_ADDRESS", "No address entered.");
            if (IssueOf(p.Id) is { } issue) return Item(BulkOutcome.NotEligible, issue.Code, issue.Message);
            if (p.DataType == PlcDataType.String && string.IsNullOrWhiteSpace(p.Encoding))
                return Item(BulkOutcome.NotEligible, "ENCODING_REQUIRED", "STRING needs a confirmed encoding.");
            return Item(BulkOutcome.Eligible);
        }).ToList();
    }

    private static BulkApprovalPreview ToPreview(string plcId, IReadOnlyList<BulkApprovalItem> items) =>
        new(plcId, items.Count, items.Count(i => !string.IsNullOrWhiteSpace(i.Address)), items.Count(i => i.Outcome == BulkOutcome.Eligible),
            items.Count(i => i.Outcome == BulkOutcome.NotEligible), items.Count(i => i.Outcome == BulkOutcome.AlreadyApproved), items);

    private async Task RequireBulkAllowedAsync(string plcId, IReadOnlyList<string> allowed, CancellationToken ct)
    {
        _ = await repo.GetConnectionAsync(plcId, ct) ?? throw AppException.NotFound("PLC_NOT_FOUND", $"Không tìm thấy {plcId}.");
        if (!BulkApprovalPolicy.IsAllowed(allowed, plcId))
            throw AppException.Rejected("BULK_APPROVAL_NOT_ALLOWED", $"Duyệt đọc hàng loạt không được bật cho {plcId}.");
    }

    /// <summary>What a bulk read-approval of <paramref name="plcId"/> would do. Read-only.</summary>
    public async Task<BulkApprovalPreview> PreviewBulkApprovalAsync(string plcId, IReadOnlyList<string> allowedPlcIds, CancellationToken ct = default)
    {
        await RequireBulkAllowedAsync(plcId, allowedPlcIds, ct);
        return ToPreview(plcId, Classify(await repo.ListPointsAsync(plcId, null, ct), await BasesOfAsync(plcId, ct)));
    }

    /// <summary>
    /// Grants READ approval (the existing <see cref="StationIoPoint.Approve"/>) to every eligible IO point of one allowed PLC: status CONFIGURED, address present and valid
    /// (format, type/device, no overlap on the PLC), STRING with encoding. Addresses, data types, write permission, handshake and every other PLC are untouched.
    /// <paramref name="expectedEligible"/> (the count the Admin confirmed in the preview) guards against a changed state between preview and confirmation.
    /// A point whose approval fails is reported and does not stop the others.
    /// </summary>
    public Task<BulkApprovalResult> BulkApproveAsync(string plcId, string approvedBy, IReadOnlyList<string> allowedPlcIds, int? expectedEligible, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            await RequireBulkAllowedAsync(plcId, allowedPlcIds, t);
            var points = await repo.ListPointsAsync(plcId, null, t);
            var items = Classify(points, await BasesOfAsync(plcId, t)).ToList();
            var eligible = items.Count(i => i.Outcome == BulkOutcome.Eligible);
            if (expectedEligible is { } exp && exp != eligible)
                throw AppException.Rejected("BULK_APPROVAL_PREVIEW_STALE", $"Số điểm đủ điều kiện đã thay đổi ({exp} → {eligible}). Hãy xem lại bản xem trước rồi xác nhận lại.");
            var now = clock.UtcNow;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Outcome != BulkOutcome.Eligible) continue;
                var p = points.First(x => x.Id == items[i].PointId);
                try { p.Approve(approvedBy, now); items[i] = items[i] with { Outcome = BulkOutcome.Approved }; }
                catch (WCS.Domain.Common.DomainException ex) { items[i] = items[i] with { Outcome = BulkOutcome.Failed, Code = ex.Code, Reason = ex.Message }; }
            }
            return Summarize(plcId, items, verified: 0);
        }, ct);

    private static BulkApprovalResult Summarize(string plcId, IReadOnlyList<BulkApprovalItem> items, int verified) =>
        new(plcId, items.Count, items.Count(i => i.Outcome == BulkOutcome.Approved), items.Count(i => i.Outcome == BulkOutcome.AlreadyApproved),
            items.Count(i => i.Outcome == BulkOutcome.NotEligible), items.Count(i => i.Outcome == BulkOutcome.Failed), verified, items);

    /// <summary>
    /// Re-reads the PLC's points (call it on a NEW scope so the database is read, not the tracker) and keeps "approved" only for points that really are
    /// APPROVED and pollable now; anything else is reported as failed. Success is never taken from the number of requests sent.
    /// </summary>
    public async Task<BulkApprovalResult> VerifyBulkApprovalAsync(BulkApprovalResult result, CancellationToken ct = default)
    {
        var current = (await repo.ListPointsAsync(result.PlcId, null, ct)).ToDictionary(p => p.Id);
        bool Pollable(Guid id) => current.TryGetValue(id, out var p) && p.Status == IoConfigStatus.Approved && p.IsPollable;
        var items = result.Items.Select(i => i.Outcome == BulkOutcome.Approved && !Pollable(i.PointId)
            ? i with { Outcome = BulkOutcome.Failed, Code = "VERIFY_FAILED", Reason = "After saving, the point is not Approved/Pollable." }
            : i).ToList();
        return Summarize(result.PlcId, items, items.Count(i => (i.Outcome is BulkOutcome.Approved or BulkOutcome.AlreadyApproved) && Pollable(i.PointId)));
    }

    /// <summary>Compatibility entry (PUT write-allowed): the same decision as <see cref="DecideWriteAsync"/>.</summary>
    public Task<IoPointView> SetWriteAllowedAsync(Guid pointId, bool allowed, string by, CancellationToken ct = default) => DecideWriteAsync(pointId, allowed, by, null, ct);

    /// <summary>
    /// CR-014: the Admin approves or rejects WRITING one point. Independent of reading (Pollable / read approval) and of the handshake. Refused for the safety/interlock/permit signals
    /// and for a data type or encoding the driver cannot write. Whether a write is then really executed is decided at write time by the runtime mode and WritePolicy.
    /// </summary>
    public Task<IoPointView> DecideWriteAsync(Guid pointId, bool approve, string by, string? note, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            var p = await repo.GetPointAsync(pointId, t) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            if (approve)
            {
                if (WritePolicy.IsBlockedSignal(p.SignalName, p.Group))
                    throw AppException.Rejected("WRITE_BLOCKED_SAFETY", "Tín hiệu Safety/Interlock và Transfer/Departure Permit không được cho phép ghi từ chức năng thử.");
                if (!WriteValueValidator.IsWriteSupported(p.DataType, p.Encoding))
                    throw AppException.Rejected("WRITE_TYPE_UNSUPPORTED", "Kiểu dữ liệu hoặc encoding của tín hiệu chưa hỗ trợ ghi.");
                try { p.ApproveWrite(by, clock.UtcNow); }
                catch (WCS.Domain.Common.DomainException ex) { throw AppException.Rejected(ex.Code, ex.Message); }
            }
            else p.RejectWrite(by, note, clock.UtcNow);
            return ToView(p, true);
        }, ct);

    /// <summary>Address conflicts currently present on a PLC (empty list = consistent).</summary>
    public async Task<IReadOnlyList<AddressIssue>> CheckPlcAsync(string plcId, CancellationToken ct = default) =>
        AddressValidator.Validate((await repo.ListPointsAsync(plcId, null, ct)).Select(x => new PointSpec(x.Id, x.StationId, x.SignalName, x.DataType, x.ActualAddress, x.WordLength, x.Direction)),
            await BasesOfAsync(plcId, ct));

    private async Task<AddressBases> BasesOfAsync(string plcId, CancellationToken ct) =>
        await repo.GetConnectionAsync(plcId, ct) is { } c ? new AddressBases(c.XyAddressBase, c.WAddressBase) : AddressBases.None;

    public Task<IReadOnlyList<PlcCommLogEntry>> LogsAsync(string? plcId, string? stationId, PlcLogLevel? minLevel, int skip, int take, CancellationToken ct = default) =>
        repo.QueryLogsAsync(plcId, stationId, minLevel, skip, Math.Clamp(take, 1, 500), ct);
}

/// <summary>Rules that keep control signals out of reach of test writes (CR-002 safety rules).</summary>
public static class WritePolicy
{
    /// <summary>Safety / interlock and motion permits are never writable through the test facility.</summary>
    public static readonly IReadOnlySet<string> BlockedSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Safety_Ready", "Transfer_Permit", "AGV_Departure_Permit",
    };

    public static bool IsBlockedSignal(string signalName, string group) =>
        BlockedSignals.Contains(signalName) || group.Contains("Safety", StringComparison.OrdinalIgnoreCase) || group.Contains("Interlock", StringComparison.OrdinalIgnoreCase);

    public sealed record Decision(bool Allowed, string? Code = null, string? Reason = null);

    /// <summary>The mode that is really in force: READ_WRITE degrades to READ_ONLY unless every write key is present.</summary>
    public static PlcRuntimeMode EffectiveMode(PlcRuntimeOptions o) =>
        o.Mode == PlcRuntimeMode.RealReadWrite && !WriteKeysComplete(o) ? PlcRuntimeMode.RealReadOnly : o.Mode;

    /// <summary>
    /// CR-017: the deployment settings (by their environment-variable names) that are not in effect for a real write - exactly the conditions of <see cref="EffectiveMode"/>. The approval reference is
    /// only checked for being present, never returned. An empty list in a real mode means the runtime is in REAL_READ_WRITE; the simulator needs none.
    /// </summary>
    public static IReadOnlyList<string> MissingWriteKeys(PlcRuntimeOptions o)
    {
        if (o.Mode == PlcRuntimeMode.Simulator) return [];
        var missing = new List<string>();
        if (o.Mode != PlcRuntimeMode.RealReadWrite) missing.Add("PLC_MODE");
        if (!o.AllowLiveConnection) missing.Add("PLC_ALLOW_LIVE_CONNECTION");
        if (!o.Write.Enabled) missing.Add("PLC_WRITE_ENABLED");
        if (string.IsNullOrWhiteSpace(o.Write.EngineerApprovalReference)) missing.Add("PLC_WRITE_APPROVAL_REFERENCE");
        if (!o.Write.HandshakeApproved) missing.Add("PLC_WRITE_HANDSHAKE_APPROVED");
        return missing;
    }

    public static bool WriteKeysComplete(PlcRuntimeOptions o) =>
        o.AllowLiveConnection && o.Write.Enabled && !string.IsNullOrWhiteSpace(o.Write.EngineerApprovalReference) && o.Write.HandshakeApproved;

    /// <param name="forHandshake">The write comes from an APPROVED handshake rule (not from a manual test). Only then may the simulator drive motion permits;
    /// real modes keep them blocked: they cannot be allow-listed until a separate approved change exists.</param>
    public static Decision Evaluate(PlcRuntimeOptions o, IoPointRef p, bool forHandshake = false)
    {
        if (p.Direction != PlcSignalDirection.SoftwareToPlc) return new(false, "WRITE_DIRECTION", "Chỉ ghi được tín hiệu chiều Phần mềm → PLC.");
        if (IsBlockedSignal(p.SignalName, p.Group) && !(forHandshake && EffectiveMode(o) == PlcRuntimeMode.Simulator && !p.SignalName.Contains("Safety", StringComparison.OrdinalIgnoreCase) && !p.Group.Contains("Safety", StringComparison.OrdinalIgnoreCase)))
            return new(false, "WRITE_BLOCKED_SAFETY", "Tín hiệu Safety/Interlock hoặc Transfer/Departure Permit bị chặn ghi thử.");
        var mode = EffectiveMode(o);
        switch (mode)
        {
            case PlcRuntimeMode.Simulator: return new(true);
            case PlcRuntimeMode.RealReadOnly: return new(false, "READ_ONLY", "Hệ thống đang ở chế độ chỉ đọc (REAL_READ_ONLY).");
            default:
                if (!p.WriteAllowed) return new(false, "WRITE_NOT_ALLOWED", "Tín hiệu chưa nằm trong danh sách được phép ghi.");
                if (string.IsNullOrWhiteSpace(p.Address)) return new(false, "NO_ADDRESS", "Tín hiệu chưa có địa chỉ được xác nhận.");
                if (!p.Pollable) return new(false, "ADDRESS_NOT_APPROVED", "Địa chỉ của tín hiệu chưa được kỹ sư PLC xác nhận (duyệt).");
                return new(true);
        }
    }
}
