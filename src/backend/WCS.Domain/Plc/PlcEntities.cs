using WCS.Domain.Common;

namespace WCS.Domain.Plc;

public enum PlcSignalDirection { PlcToSoftware = 1, SoftwareToPlc = 2 }

public enum PlcDataType { Bool = 1, Int = 2, String = 3 }

/// <summary>NeedsConfiguration: no confirmed address. Configured: address entered by an Admin. Approved: confirmed by the PLC engineer.</summary>
public enum IoConfigStatus { NeedsConfiguration = 0, Configured = 1, Approved = 2 }

/// <summary>CR-014: the Admin decision on WRITING a point. Independent of reading (<see cref="IoConfigStatus"/>) and of the handshake. NotApproved = awaiting a decision.</summary>
public enum WriteApprovalStatus { NotApproved = 0, Approved = 1, Rejected = 2 }

/// <summary>Runtime mode of the PLC layer (CR-002). The default is <see cref="Simulator"/>; real modes are opt-in and guarded.</summary>
public enum PlcRuntimeMode { Simulator = 0, RealReadOnly = 1, RealReadWrite = 2 }

public enum PlcLogLevel { Info = 1, Warning = 2, Error = 3 }

/// <summary>
/// One physical PLC and its single WCS connection (CR-002: 4 PLCs, 4 connections). Network parameters are optional on purpose:
/// they stay null (NEEDS_CONFIGURATION) until the PLC engineer confirms them. Nothing is defaulted.
/// </summary>
public class PlcConnection
{
    private PlcConnection() { }

    public PlcConnection(string id, string name, string ipAddress, DateTimeOffset now)
    {
        Id = id; Name = name; IpAddress = ipAddress; UpdatedAt = now;
    }

    /// <summary>PLC_ID, e.g. PLC-01.</summary>
    public string Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string IpAddress { get; private set; } = default!;
    public int? Port { get; private set; }
    /// <summary>Protocol/frame identifier, e.g. McProtocol3EBinaryTcp. Null until confirmed.</summary>
    public string? Protocol { get; private set; }
    public int? NetworkNumber { get; private set; }
    /// <summary>MC "PC number" (request destination PLC number).</summary>
    public int? PcNumber { get; private set; }
    /// <summary>MC request destination module I/O number (e.g. own station = 0x03FF in some setups). Never assumed.</summary>
    public int? ModuleIoNumber { get; private set; }
    /// <summary>MC request destination module station number (workbook: PLC_Station_No).</summary>
    public int? PlcStationNumber { get; private set; }
    /// <summary>MC monitoring timer in 250 ms units (PLC-side wait). Never assumed.</summary>
    public int? MonitoringTimer { get; private set; }
    public int? TimeoutMs { get; private set; }
    public int? PollingIntervalMs { get; private set; }
    /// <summary>Optional per-operation timeouts (CR-003). Null = use <see cref="TimeoutMs"/>. Never defaulted to a made-up value.</summary>
    public int? ConnectTimeoutMs { get; private set; }
    public int? ReadTimeoutMs { get; private set; }
    public int? WriteTimeoutMs { get; private set; }
    /// <summary>Numbering base of X/Y device numbers: 8 (octal) or 16 (hexadecimal). Null = NEEDS_CONFIRMATION; X/Y addresses are refused until set.</summary>
    public int? XyAddressBase { get; private set; }
    /// <summary>Numbering base of W device numbers: 10 or 16. Null = NEEDS_CONFIRMATION; W addresses are refused until set.</summary>
    public int? WAddressBase { get; private set; }
    /// <summary>The Admin switch "connect this PLC". Real connections additionally need the deployment guard (see PlcRuntimeOptions).</summary>
    public bool Enabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    /// <summary>Names of the parameters that are still missing for a REAL connection (empty = complete).</summary>
    public IReadOnlyList<string> MissingParameters()
    {
        var m = new List<string>();
        if (Port is null) m.Add("port");
        if (string.IsNullOrWhiteSpace(Protocol)) m.Add("protocol");
        if (NetworkNumber is null) m.Add("networkNumber");
        if (PcNumber is null) m.Add("pcNumber");
        if (ModuleIoNumber is null) m.Add("moduleIoNumber");
        if (PlcStationNumber is null) m.Add("plcStationNumber");
        if (MonitoringTimer is null) m.Add("monitoringTimer");
        if (TimeoutMs is null) m.Add("timeoutMs");
        if (PollingIntervalMs is null) m.Add("pollingIntervalMs");
        return m;
    }

    public void Update(string name, string ipAddress, int? port, string? protocol, int? networkNumber, int? pcNumber, int? moduleIoNumber,
        int? plcStationNumber, int? monitoringTimer, int? timeoutMs, int? pollingIntervalMs, bool enabled, string? by, DateTimeOffset now,
        int? connectTimeoutMs = null, int? readTimeoutMs = null, int? writeTimeoutMs = null, int? xyAddressBase = null, int? wAddressBase = null)
    {
        Name = name; IpAddress = ipAddress; Port = port; Protocol = protocol; NetworkNumber = networkNumber; PcNumber = pcNumber;
        ModuleIoNumber = moduleIoNumber; PlcStationNumber = plcStationNumber; MonitoringTimer = monitoringTimer;
        TimeoutMs = timeoutMs; PollingIntervalMs = pollingIntervalMs; Enabled = enabled; UpdatedBy = by; UpdatedAt = now;
        ConnectTimeoutMs = connectTimeoutMs; ReadTimeoutMs = readTimeoutMs; WriteTimeoutMs = writeTimeoutMs;
        XyAddressBase = xyAddressBase; WAddressBase = wAddressBase;
    }
}

/// <summary>
/// A Station served by one PLC. Stations are NOT locations: most of them (A3/A4, D1/D2, X*, E*, G3, G7) have no WCS location or approved flow,
/// so they are monitor-only. <see cref="LocationCode"/> is an optional, explicit link set by an Admin.
/// </summary>
public class PlcStation
{
    private PlcStation() { }

    public PlcStation(string id, string plcId, int order, DateTimeOffset now)
    {
        Id = id; PlcId = plcId; Order = order; UpdatedAt = now;
    }

    public string Id { get; private set; } = default!;
    public string PlcId { get; private set; } = default!;
    public int Order { get; private set; }
    public string? LocationCode { get; private set; }
    public bool Enabled { get; private set; } = true;
    /// <summary>Technical role. Only "MonitorOnly" exists: no station may create tasks until a flow mapping is approved.</summary>
    public string Role { get; private set; } = "MonitorOnly";
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>CR-048: set when the station is no longer in the IO map in use. The row is kept for the records that refer to it; it is not a port any more.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>CR-048: the station left the IO map: its location link is released.</summary>
    public void Retire(DateTimeOffset now) { RetiredAt = now; LocationCode = null; UpdatedAt = now; }
    public void Restore(DateTimeOffset now) { RetiredAt = null; UpdatedAt = now; }

    public void Update(string? locationCode, bool enabled, DateTimeOffset now) { LocationCode = locationCode; Enabled = enabled; UpdatedAt = now; }
}

/// <summary>CR-048. None: never set. WorkbookDefault: taken from the IO map. Custom: entered or removed by an Admin.</summary>
public enum IoAddressOrigin { None, WorkbookDefault, Custom }

/// <summary>CR-048: a row of a signal that left the IO map, kept for the record. Nothing reads it for operation.</summary>
public class RetiredIoPoint
{
    private RetiredIoPoint() { }

    public RetiredIoPoint(Guid id, DateTimeOffset at, string catalogSha256, string plcId, string stationId, string signalName, string? actualAddress, string snapshotJson)
    {
        Id = id; ArchivedAt = at; CatalogSha256 = catalogSha256; PlcId = plcId; StationId = stationId; SignalName = signalName; ActualAddress = actualAddress; SnapshotJson = snapshotJson;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset ArchivedAt { get; private set; }
    public string CatalogSha256 { get; private set; } = default!;
    public string PlcId { get; private set; } = default!;
    public string StationId { get; private set; } = default!;
    public string SignalName { get; private set; } = default!;
    public string? ActualAddress { get; private set; }
    public string SnapshotJson { get; private set; } = default!;
}

/// <summary>One IO point of one Station of one PLC. Identity: PLC_ID + Station_ID + Signal_Name.</summary>
public class StationIoPoint
{
    private StationIoPoint() { }

    public StationIoPoint(string plcId, string stationId, int seq, string group, string signalName, PlcSignalDirection direction,
        PlcDataType dataType, string templateAddressReference, string description, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); PlcId = plcId; StationId = stationId; Seq = seq; Group = group; SignalName = signalName;
        Direction = direction; DataType = dataType; TemplateAddressReference = templateAddressReference; Description = description;
        Status = IoConfigStatus.NeedsConfiguration; UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string PlcId { get; private set; } = default!;
    public string StationId { get; private set; } = default!;
    public int Seq { get; private set; }
    public string Group { get; private set; } = default!;
    public string SignalName { get; private set; } = default!;
    public PlcSignalDirection Direction { get; private set; }
    public PlcDataType DataType { get; private set; }
    /// <summary>Reference address copied from the workbook template. NOT an address of this station: it is identical for every station.</summary>
    public string TemplateAddressReference { get; private set; } = default!;
    public string Description { get; private set; } = "";

    /// <summary>The device address in use (e.g. D1500): the address written in the IO map (CR-048) or the one an Admin entered. Never calculated. Using it for reading says nothing about confirmation or writing.</summary>
    public string? ActualAddress { get; private set; }
    /// <summary>CR-048: where <see cref="ActualAddress"/> comes from. A <see cref="IoAddressOrigin.Custom"/> value (an empty one too) is never overwritten by the catalog.</summary>
    public IoAddressOrigin AddressOrigin { get; private set; }
    /// <summary>CR-048: signal names of the same station that the IO map puts in the same register (comma separated); they may hold the same address.</summary>
    public string? SharedWith { get; private set; }
    /// <summary>Length in words (STRING) - required for STRING, ignored otherwise.</summary>
    public int? WordLength { get; private set; }
    /// <summary>STRING encoding (e.g. ASCII) or scaling note. Required for STRING.</summary>
    public string? Encoding { get; private set; }
    public IoConfigStatus Status { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    /// <summary>Per-signal write allow-list. Default false. Honoured only in REAL_READ_WRITE (and the simulator).</summary>
    public bool WriteAllowed { get; private set; }
    /// <summary>CR-014: write approval decision (NotApproved / Approved / Rejected). <see cref="WriteAllowed"/> is true exactly when this is Approved (kept for the runtime and older clients).</summary>
    public WriteApprovalStatus WriteApproval { get; private set; }
    public string? WriteDecidedBy { get; private set; }
    public DateTimeOffset? WriteDecidedAt { get; private set; }
    public string? WriteDecisionNote { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool HasConfirmedAddress => Status != IoConfigStatus.NeedsConfiguration && !string.IsNullOrWhiteSpace(ActualAddress);

    /// <summary>
    /// CR-012: an entered address makes the point readable (polled) - approval is no longer needed for READING. Only an address that passed the entry validation can be stored,
    /// and the runtime additionally re-checks it with <c>AddressValidator</c> (see <c>PlcPollablePolicy</c>). Approval (<see cref="IoConfigStatus.Approved"/>) now only governs WRITE permission
    /// (<see cref="ApproveWrite"/>, CR-014) and the approval of a port mapping for Business Process publishing; it never grants a write by itself.
    /// </summary>
    public bool IsPollable => HasConfirmedAddress;

    /// <summary>An address an Admin entered.</summary>
    public void ConfigureAddress(string address, int? wordLength, string? encoding, DateTimeOffset now)
    {
        AddressOrigin = IoAddressOrigin.Custom;
        ActualAddress = address; WordLength = wordLength; Encoding = encoding;
        Status = IoConfigStatus.Configured; ApprovedBy = null; ApprovedAt = null; ResetWriteApproval(); UpdatedAt = now;
    }

    /// <summary>CR-048: the address written in the IO map for this station and signal. Like any new address it is not approved and not writable.</summary>
    public void ApplyDefaultAddress(string address, DateTimeOffset now)
    {
        ConfigureAddress(address, null, null, now);
        AddressOrigin = IoAddressOrigin.WorkbookDefault;
    }

    public void ShareRegisterWith(string? signals) => SharedWith = string.IsNullOrWhiteSpace(signals) ? null : signals;

    /// <summary>An Admin removes the address: the point stays empty until an address is entered or the default is restored.</summary>
    public void ClearAddress(DateTimeOffset now)
    {
        AddressOrigin = IoAddressOrigin.Custom;
        ActualAddress = null; WordLength = null; Encoding = null;
        Status = IoConfigStatus.NeedsConfiguration; ApprovedBy = null; ApprovedAt = null; ResetWriteApproval(); UpdatedAt = now;
    }

    public void Approve(string by, DateTimeOffset now)
    {
        if (Status == IoConfigStatus.NeedsConfiguration) throw new DomainException("IO_NO_ADDRESS", "Cannot approve a point without an address.");
        Status = IoConfigStatus.Approved; ApprovedBy = by; ApprovedAt = now; UpdatedAt = now;
    }

    /// <summary>A new or cleared address withdraws any write decision: the Admin decided on a different address.</summary>
    private void ResetWriteApproval() { WriteAllowed = false; WriteApproval = WriteApprovalStatus.NotApproved; WriteDecidedBy = null; WriteDecidedAt = null; WriteDecisionNote = null; }

    /// <summary>
    /// CR-014: approves WRITING this point. Independent of the read approval (<see cref="Approve"/>) and of pollability: it needs an entered address and a Software -> PLC direction,
    /// nothing else. Whether a write really happens is still decided by the runtime mode and WritePolicy.
    /// </summary>
    public void ApproveWrite(string by, DateTimeOffset now)
    {
        if (Status == IoConfigStatus.NeedsConfiguration || string.IsNullOrWhiteSpace(ActualAddress)) throw new DomainException("IO_NO_ADDRESS", "Cannot approve writing a point without an address.");
        if (Direction != PlcSignalDirection.SoftwareToPlc) throw new DomainException("IO_NOT_WRITABLE", "Only Software -> PLC points can be written.");
        WriteAllowed = true; WriteApproval = WriteApprovalStatus.Approved; WriteDecidedBy = by; WriteDecidedAt = now; WriteDecisionNote = null; UpdatedAt = now;
    }

    /// <summary>Rejects (or withdraws) the write approval. Always allowed.</summary>
    public void RejectWrite(string by, string? note, DateTimeOffset now)
    {
        WriteAllowed = false; WriteApproval = WriteApprovalStatus.Rejected; WriteDecidedBy = by; WriteDecidedAt = now;
        WriteDecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 200)]; UpdatedAt = now;
    }
}

/// <summary>Diagnostic/communication log (no business data, no secrets).</summary>
public class PlcCommLogEntry
{
    private PlcCommLogEntry() { }

    public PlcCommLogEntry(DateTimeOffset at, string? plcId, string? stationId, PlcLogLevel level, string eventName, string message)
    {
        Id = Guid.NewGuid(); OccurredAt = at; PlcId = plcId; StationId = stationId; Level = level; Event = eventName;
        Message = message.Length <= 1000 ? message : message[..1000];
    }

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? PlcId { get; private set; }
    public string? StationId { get; private set; }
    public PlcLogLevel Level { get; private set; }
    public string Event { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    /// <summary>CR-007: structured facts (ip, port, previousState, newState, kind, endCode, reason) as JSON; null for events without such context.</summary>
    public string? DetailsJson { get; private set; }

    /// <summary>Diagnostics: standard code (PlcDiagCodes) and stage (PlcDiagStages) as searchable columns. Null for events that carry neither.</summary>
    public string? DiagCode { get; private set; }
    public string? Stage { get; private set; }

    public PlcCommLogEntry WithDiag(string? code, string? stage) { DiagCode = code; Stage = stage; return this; }

    public PlcCommLogEntry WithDetails(string? json) { DetailsJson = json is { Length: > 4000 } j ? j[..4000] : json; return this; }
}

/// <summary>
/// One MC Protocol request/response pair of a PLC connection, kept in PostgreSQL (at most the latest 300 per PLC; older rows are pruned).
/// Protocol data only: hex is capped, no secrets. ADMIN ONLY.
/// </summary>
public class PlcExchangeLogEntry
{
    private PlcExchangeLogEntry() { }

    public PlcExchangeLogEntry(DateTimeOffset at, string plcId, string operation, string command, string device, int startAddress, int points, long durationMs,
        int? endCode, string outcome, string? error, string? requestHex, string? responseHex, bool ambiguous)
    {
        Id = Guid.NewGuid(); OccurredAt = at; PlcId = plcId; Operation = operation; Command = command; Device = device; StartAddress = startAddress; Points = points;
        DurationMs = durationMs; EndCode = endCode; Outcome = outcome; Error = Cut(error, 300); RequestHex = Cut(requestHex, MaxHex); ResponseHex = Cut(responseHex, MaxHex); IsAmbiguous = ambiguous;
    }

    public const int MaxHex = 400;
    private static string? Cut(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string PlcId { get; private set; } = default!;
    public string Operation { get; private set; } = default!;
    public string Command { get; private set; } = default!;
    public string Device { get; private set; } = default!;
    public int StartAddress { get; private set; }
    public int Points { get; private set; }
    public long DurationMs { get; private set; }
    public int? EndCode { get; private set; }
    public string Outcome { get; private set; } = default!;
    public string? Error { get; private set; }
    public string? RequestHex { get; private set; }
    public string? ResponseHex { get; private set; }
    public bool IsAmbiguous { get; private set; }
}
