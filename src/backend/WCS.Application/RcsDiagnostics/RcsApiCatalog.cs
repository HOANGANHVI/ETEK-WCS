namespace WCS.Application.RcsDiagnostics;

/// <summary>One field of an RCS-2000 request, exactly as the API Developer Guide V3.3 (UD35865B, 2023-12-04) defines it. Nothing here is guessed: what the document does not state is left open and listed in <see cref="RcsApiDef.Gaps"/>.</summary>
public sealed record RcsField(string Name, string Type, bool Required, int? MaxLength, string Description,
    IReadOnlyList<string>? Enum = null, IReadOnlyList<RcsField>? Children = null, int? MaxItems = null, int? Min = null, int? Max = null,
    IReadOnlyList<string>? Suggestions = null);
// Enum = the document lists the ONLY valid values (the form is a drop-down and the validator rejects anything else). Suggestions = values the document names as built in, offered as hints: any other value is accepted.

/// <summary>A cross-field rule from the document: AtLeastOne / ExactlyOne of <c>Fields</c>, or <c>Field</c> required when <c>WhenField</c> = <c>WhenValue</c>.</summary>
public sealed record RcsRule(string Kind, IReadOnlyList<string> Fields, string Message, string? WhenField = null, string? WhenValue = null);

/// <summary>
/// One outbound RCS-2000 API (WCS calls the RCS). <c>Effect</c>: ReadOnly (the document shows it only answers a question), Unverified (the document does not say whether it changes RCS state: NOT treated as safe), Mutating (writes RCS data / configuration), Critical (creates, changes, stops or releases AMR work, or destroys RCS data).
/// </summary>
public sealed record RcsApiDef(string Name, string Group, string Section, string Path, string Function, string Effect, IReadOnlyList<RcsField> Fields,
    IReadOnlyList<RcsRule> Rules, IReadOnlyList<RcsField> Response, string Sample, IReadOnlyList<string> Gaps, bool Executable = true)
{
    public string Method => "POST";
}

/// <summary>The five calls the RCS makes TO a third-party platform (5.3). They are NOT outbound APIs: WCS cannot "call" them, it can only RECEIVE them.</summary>
public sealed record RcsCallbackDef(string Name, string Section, string Function, string Provided);

/// <summary>
/// CR-032: the static catalog of the 23 outbound APIs of RCS-2000 V3.3 (chapter 5.2, sections 5.2.1 - 5.2.23). It is the ONLY source of what the diagnostics module may send: the URL path comes from here, never from the
/// browser. Field tables were taken from the PDF; where the PDF text is ambiguous or inconsistent the gap is written down (see <see cref="RcsApiDef.Gaps"/>) and the strict reading is NOT applied.
/// </summary>
public static class RcsApiCatalog
{
    public const string RpcBase = "/rcms/services/rest/hikRpcService/";
    public const string TpsBase = "/rcms/services/rest/hikTpsService/";
    public const string DpsBase = "/rcms-dps/rest/";

    public static readonly string[] Groups = ["Query", "Task", "AmrControl", "LocationMapArea", "BindingMaterialRack", "Ctu"];

    // ---- field helpers
    private static RcsField Str(string n, bool req, int? max, string d, string[]? en = null, int? min = null, int? maxV = null, string[]? hints = null) => new(n, "string", req, max, d, en, null, null, min, maxV, hints);
    private static RcsField Num(string n, bool req, string d, string[]? en = null) => new(n, "number", req, null, d, en);
    private static RcsField StrArr(string n, bool req, int? itemMax, string d) => new(n, "string[]", req, itemMax, d);
    private static RcsField Objs(string n, bool req, string d, RcsField[] children, int? maxItems = null) => new(n, "object[]", req, null, d, null, children, maxItems);
    private static RcsField Obj(string n, bool req, string d, RcsField[] children) => new(n, "object", req, null, d, null, children);

    private static readonly string[] Bind = ["0", "1"];

    /// <summary>The four fields every request has (doc: reqCode Req 32; reqTime Opt 20; clientCode Opt 16; tokenCode Opt 64).</summary>
    public static readonly IReadOnlyList<RcsField> CommonFields =
    [
        Str("reqCode", true, 32, "Request ID. If a request is repeatedly submitted, the request ID must be the same."),
        Str("reqTime", false, 20, "Request timestamp, format: YYYY-MM-DD hh:mm:ss"),
        Str("clientCode", false, 16, "Third-party platform ID, e.g., PDA, HCWMS."),
        Str("tokenCode", false, 64, "Token ID, which is provided by RCS-2000 for the third-party platform. Optional; the monitoring site decision is that no token is used."),
    ];

    private static readonly RcsField[] StdResponse =
    [
        Str("code", true, 6, "Status code, see Appendix A (0 succeeded, 1 failed, 6 no need to resend, 99 unknown error)."),
        Str("message", true, 64, "Returned status description, e.g. \"successful\"."),
        Str("reqCode", true, 64, "Request ID, the same as in the request."),
        Str("data", false, 2000, "Custom content to be returned, if the API defines one."),
    ];

    private static RcsApiDef D(string name, string group, string section, string path, string function, string effect, RcsField[] fields, RcsRule[]? rules, string sample, string[]? gaps = null, RcsField[]? response = null) =>
        new(name, group, section, path + name, function, effect, fields, rules ?? [], response ?? StdResponse, sample, gaps ?? []);

    public const string ReadOnly = "ReadOnly", Unverified = "Unverified", Mutating = "Mutating", Critical = "Critical";

    /// <summary>The BUILT-IN task types the document names for taskTyp (5.2.1). taskTyp is "the same as the major task type configured in RCS-2000" (String, max 16, required): the list is a hint, not an enumeration.</summary>
    private static readonly string[] TaskTypes = ["F01", "F02", "F03", "F04", "F05", "F06", "F11", "F12", "F13", "F14", "F15", "F16", "F17", "F18", "F20"];

    public static readonly IReadOnlyList<RcsApiDef> All =
    [
        // ============================================================================ Query
        D("queryAgvStatus", "Query", "5.2.14", DpsBase, "Search for AMR status, including the AMR battery.", ReadOnly,
            [Str("mapCode", false, 32, "Map ID where the AMR locates (the MAP ID of the installation, default TT). Project decision: for this API the map is sent as mapCode; mapShortName is not a field of this request.")], null, "{ }",
            ["The path is /rcms-dps/rest/queryAgvStatus (not hikRpcService) and the document's table fixes the port to :8182 while the text says the default port is 8182; WCS uses the saved RCS port.",
             "The document's sample request uses \"mapCode\" but its parameter table names the field \"mapShortName\" (the table lists mapShortName only; the response has mapCode = \"ID of map\"). A real RCS answered mapShortName = TT with code 1 \"the map number is mandatory when querying all robots\": which field it reads is to be confirmed by Hikrobot. Project decision: this API sends mapCode (pre-filled with the MAP ID) and does not offer mapShortName; the field the RCS really reads remains to be confirmed by Hikrobot.",
             "Document: call at most every 5 s (< 100 AMRs). Manual calls here are not part of the 5 s monitoring poll.",
             "No tokenCode is sent by project decision (CR-031): the RCS provides no authentication for this API."],
            [Str("code", true, 6, "Status code."), Str("message", true, 64, "Status description."), Str("reqCode", true, 64, "Request ID."),
             Objs("data", false, "AMR list (robotCode, robotDir, robotIp, battery, posX, posY, mapCode, speed, status, ...).", [])]),

        D("queryTaskStatus", "Query", "5.2.15", RpcBase, "Search for the task executing status according to the task ID or AMR ID, or search for executing statuses of multiple tasks in batch.", ReadOnly,
            [StrArr("taskCodes", false, 64, "Array of task IDs. At least one of taskCodes and agvCode should be configured."), Str("agvCode", false, 5, "AMR ID. At least one of taskCodes and agvCode should be configured.")],
            [new("AtLeastOne", ["taskCodes", "agvCode"], "At least one of taskCodes and agvCode must be configured.")],
            """{ "taskCodes": ["123", "234"] }""", null,
            [Str("code", true, 6, "Status code."), Str("message", true, 64, "Status description."), Str("reqCode", true, 64, "Request ID."), Objs("data", false, "taskCode, taskTyp, taskStatus (0 sending exception, 1 created, 2 executing, 3 sending, 4 canceling, 5 canceled, 6 resending, 9 completed, 10 interrupted), agvCode.", [])]),

        D("queryPodBerthAndMat", "Query", "5.2.16", RpcBase, "Search the relations among rack, position and material batch.", ReadOnly,
            [Str("podCode", false, 16, "Rack ID."), Str("materialLot", false, 32, "Material batch ID."), Str("positionCode", false, 16, "Position ID, predefined on the map."), Str("areaCode", false, 16, "Area ID."), Str("mapShortName", false, 16, "Map alias.")],
            [new("AtLeastOne", ["podCode", "materialLot", "positionCode", "mapShortName"], "At least one of podCode, materialLot, positionCode and mapShortName must be configured.")],
            """{ "podCode": "100001" }""",
            ["The document's remark lists podCode, materialLot, positionCode and mapShortName as the \"at least one\" fields but areaCode is also a request field and appears in the sample; whether areaCode alone is enough is not stated."],
            [Str("code", true, 6, "Status code."), Str("message", true, 64, "Status description."), Str("reqCode", true, 64, "Request ID."), Objs("data", false, "areaCode, materialLot, podCode, mapDataCode, positionCode.", [])]),

        // ============================================================================ Task
        D("genAgvSchedulingTask", "Task", "5.2.1", RpcBase, "The third-party platform creates a task and RCS-2000 applies the task to an AMR.", Critical,
            [Str("taskTyp", true, 16, "Task type, the same as the major task type configured in RCS-2000 (free text, for example a custom type such as QTESTmove). The built-in types of the document are offered as suggestions.", hints: TaskTypes),
             Str("ctnrTyp", false, 16, "Container type (FMR / CTU tasks; required for FMR tasks)."), Str("ctnrCode", false, 32, "Container ID (FMR / CTU tasks)."), Str("ctnrNum", false, 2, "Number of containers (FMR tasks)."),
             Str("taskMode", false, 1, "Task mode: 0 normal move, 1 outbound move, 2 inbound move, 3 transfer move.", ["0", "1", "2", "3"]),
             Str("wbCode", false, 32, "Workstation ID (letters and digits, as configured in RCS-2000). One of wbCode and positionCodePath must be configured."),
             Objs("positionCodePath", false, "Rack moving pattern: multiple locations (up to 50).",
                 [Str("positionCode", false, null, "Location ID, predefined on the map; depends on type."),
                  Str("type", false, null, "Location type: 00 actual location, 01 material batch, 02 area selection strategy, 03 rack No., 04 available location in an area, 05 bin ID, 06 roadway ID, 07 container ID, 08 roadway strategy, 09 roadway area, 10 roadway bin, 11 conveyor No., 12 CTU workstation No., 13 position of roadway rack.",
                      ["00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13"])], 50),
             Str("podCode", false, 16, "Rack ID, can be empty if no rack specified."), Str("podDir", false, 4, "Rack direction: 180 leftward, 0 rightward, 90 upward, -90 downward.", ["180", "0", "90", "-90"]),
             Str("podTyp", false, 16, "Rack types: 1 empty rack of all types (default), 2 type linked with the workstation, other = specific type."),
             Str("materialLot", false, 32, "Material lot ID (see the document for the strategy search values 0 / 1)."), Str("materialType", false, 32, "Material type (CMR protocols only)."),
             Str("priority", false, 3, "Task priority [1,127]; the larger the higher. Not set = the task template priority.", null, 1, 127),
             Str("agvCode", false, 5, "AMR ID. Not set = RCS-2000 selects an AMR."), Str("taskCode", false, 64, "Task ID. Not set = the system generates one."),
             Str("groupId", false, 16, "Group ID (CTU outbound by groups)."), Str("agvTyp", false, 4, "Device type."),
             Str("positionSelStrategy", false, 2, "In/out rule for searching racks: 1 FIFO by arrival, 2 FILO by arrival, 9 FIFO by binding time, 10 FILO by binding time.", ["1", "2", "9", "10"]),
             Str("data", false, 2000, "Custom content in JSON format.")],
            [new("AtLeastOne", ["wbCode", "positionCodePath"], "One of wbCode and positionCodePath must be configured.")],
            """{ "taskTyp": "F01", "positionCodePath": [ { "positionCode": "p01", "type": "00" }, { "positionCode": "x02", "type": "02" } ], "podCode": "100001", "podDir": "0", "priority": "1" }""",
            ["taskTyp is a string of up to 16 characters naming a task type CONFIGURED in RCS-2000. The document lists the built-in types (F01-F06, F11-F18, F20) but a site can define others (a real RCS accepted a custom type); the list is only a suggestion and any value up to 16 characters is sent as typed.",
             "The document's sample request writes the key \"positionSelStrategy \" with a trailing space.",
             "The maximum length and the Required/Optional flag of positionCode and type inside positionCodePath are not legible in the PDF text; both are treated as optional with no length limit.",
             "The \"podDir\" description (180/0/90/-90) differs from bindPodAndBerth's podDir (0 horizontal / 1 vertical).",
             "Effect: creates a REAL task; an AMR may move."]),

        D("continueTask", "Task", "5.2.2", RpcBase, "Continue executing the next sub task.", Critical,
            [Str("wbCode", false, 32, "Workstation ID."), Str("podCode", false, 16, "Rack ID."), Str("agvCode", false, 5, "AMR ID."), Str("taskCode", false, 64, "Task ID (UUID). Recommended."),
             Str("taskSeq", false, 32, "Sub task No. to continue; not set = the next sub task."),
             Obj("nextPositionCode", false, "Location of the sub task (required when the task type is externally configured).",
                 [Str("positionCode", false, null, "Location ID."), Str("type", false, null, "Location type: 00 actual location on map, 02 strategy.", ["00", "02"])])],
            [new("ExactlyOne", ["wbCode", "agvCode", "taskCode", "podCode"], "Only one of wbCode, agvCode, taskCode and podCode should be configured at one time.")],
            """{ "taskCode": "123456" }""",
            ["\"Only one of wbCode, agvCode, taskCode and podCode\" is applied as exactly one; the document does not say what RCS-2000 answers when none (or several) is sent.",
             "nextPositionCode lengths are not legible (the field is shown as 40 characters)."]),

        D("cancelTask", "Task", "5.2.3", RpcBase, "Cancel the task that is executing or is generated but waiting for being executed.", Critical,
            [Str("forceCancel", false, 16, "Task cancel mode: 0 (default) AMR puts down the rack and turns idle; 1 AMR carries the rack back (LMR and CTU only).", Bind),
             Str("matterArea", false, 16, "Inbound area No., valid only when forceCancel is 1."), Str("agvCode", false, 5, "AMR ID whose executing task will be canceled."), Str("taskCode", false, 64, "ID (UUID) of the task to be canceled.")],
            [new("AtLeastOne", ["taskCode", "agvCode"], "One of taskCode and agvCode must be configured. If both are configured only agvCode takes effect.")],
            """{ "forceCancel": "0", "taskCode": "123456" }""",
            ["Effect: cancels a REAL task; the AMR may put the rack down where it is."]),

        D("setTaskPriority", "Task", "5.2.4", RpcBase, "Set the task priority from 1 to 127 (the larger the value, the higher the priority).", Mutating,
            [Objs("priorities", true, "Task list.", [Str("taskCode", true, 64, "Task ID (UUID)."), Str("priority", true, 3, "Task priority, range [1,127].", null, 1, 127)])],
            null, """{ "priorities": [ { "priority": "1", "taskCode": "1232" } ] }""",
            ["The maximum lengths of taskCode and priority inside the list are only partly legible in the PDF text.", "Only tasks not yet assigned to an AMR can be changed (document remark)."]),

        D("genPreScheduleTask", "Task", "5.2.7", RpcBase, "Apply a pre-schedule task: notify RCS of the actual task in advance so a free AMR can wait at the target position.", Critical,
            [Str("positionCode", true, 32, "Task position."), Str("nextTask", true, 32, "Pre-schedule time period: seconds before the actual task is generated; \"-1\" clears the pre-schedule tasks of a position."),
             Str("agvTyp", true, 256, "AMR type used for the actual task."), Str("waitTime", false, 32, "Waiting time."),
             Str("priority", false, 3, "Task priority [1,127].", null, 1, 127),
             Str("useableLayers", false, null, "Number of available empty bins (CTU only)."), Str("cacheCount", false, null, "Number of cache containers (loader/unloader and conveyor line)."),
             Num("update", false, "Whether to update the pre-schedule tasks: 0 add a new one, 1 refresh from cacheCount / useableLayers.", ["0", "1"]), Num("preTaskQty", false, "Number of pre-schedule tasks (used when update = 1).")],
            null, """{ "positionCode": "pos1", "nextTask": "300", "agvTyp": "1", "useableLayers": "1", "cacheCount": "1", "update": 0 }""",
            ["Maximum length of useableLayers / cacheCount and the exact data type of update / preTaskQty are not stated (PDF: \"String /\", \"Number\").", "The sample shows \"update\" as a quoted string while the table says Number; WCS accepts a number or a numeric string and sends it as given."]),

        D("genCtuGroupTaskBatch", "Task", "5.2.21", RpcBase, "CTU outbound tasks applied and performed in order (groups).", Critical,
            [Str("taskTyp", true, 16, "Task type (default: B10)."), Num("seqTyp", true, "Outbound order type: 1 ordered between and within groups, 2 ordered between groups / unordered within, 3 unordered between groups / ordered within.", ["1", "2", "3"]),
             Objs("taskGroups", true, "Task information list.",
                 [Str("taskCode", true, 64, "Task ID."), Str("ctnrCode", true, 32, "Container ID."), Str("ctnrTyp", true, 16, "Container type."), Str("wbCode", true, 32, "Workstation ID."),
                  Num("wbTyp", true, "Workstation type: 0 normal, 1 conveyor, 2 loader/unloader.", ["0", "1", "2"]), Num("groupId", true, "Task group ID or order between groups."), Num("sequence", true, "Order within a group.")])],
            null, """{ "taskTyp": "B10", "seqTyp": 1, "taskGroups": [ { "taskCode": "T1", "ctnrCode": "C1", "ctnrTyp": "C1", "wbCode": "WB1", "wbTyp": 0, "groupId": 1, "sequence": 1 } ] }""",
            ["The response is documented with code, message and reqCode only (no data).", "Max length of the number fields is shown as 3 / 2 in the PDF text for seqTyp / wbTyp; not enforced."]),

        // ============================================================================ AMR control
        D("stopRobot", "AmrControl", "5.2.10", RpcBase, "Stop the specified AMR or all AMRs.", Critical,
            [Str("robotCount", false, 64, "The number of AMRs to be stopped: \"-1\" = all AMRs."), Str("mapShortName", false, 32, "Alias of the map where the AMR locates. Required when robotCount is \"-1\"."), StrArr("robots", false, 16, "List of AMR IDs.")],
            [new("AtLeastOne", ["robotCount", "robots"], "One of robotCount and robots is required."), new("RequiredWhen", ["mapShortName"], "mapShortName is required when robotCount is \"-1\".", "robotCount", "-1")],
            """{ "robotCount": "2", "robots": ["1001", "1002"] }""",
            ["Effect: STOPS real AMRs (robotCount -1 stops all AMRs on a map).", "The document does not define what robotCount other than \"-1\" means when robots is also given."]),

        D("resumeRobot", "AmrControl", "5.2.13", RpcBase, "Resume the AMR; the resumed AMR continues executing the uncompleted task.", Critical,
            [Str("robotCount", false, 64, "The number of AMRs to be resumed: \"-1\" = all AMRs."), Str("mapShortName", false, 32, "Alias of the map where the AMR locates. Required when robotCount is \"-1\"."), StrArr("robots", false, 16, "List of AMR IDs.")],
            [new("AtLeastOne", ["robotCount", "robots"], "One of robotCount and robots is required."), new("RequiredWhen", ["mapShortName"], "mapShortName is required when robotCount is \"-1\".", "robotCount", "-1")],
            """{ "robotCount": "2", "robots": ["1001", "1002"] }""",
            ["Effect: RESUMES real AMRs."]),

        // ============================================================================ Location / map / area
        D("lockPosition", "LocationMapArea", "5.2.9", RpcBase, "Enable or disable the position (a disabled location cannot be found in the area).", Mutating,
            [Str("positionCode", true, 32, "Position ID, predefined on the map."), Str("indBind", true, 1, "Enable or disable: 1 enable, 0 disable.", Bind)],
            null, """{ "positionCode": "p02", "indBind": "1" }""", ["The field is called indBind although it means enable/disable (as in the document)."]),

        D("syncMapDatas", "LocationMapArea", "5.2.11", RpcBase, "Synchronize map information with that in RCS-2000.", Unverified,
            [Str("mapDataCode", false, 32, "Unique location code on the map."), Str("mapShortName", true, 32, "Alias of the map that needs to be synchronized."), Str("dataTyp", false, 6, "Map element type; not set = all location codes of the map.")],
            null, """{ "mapShortName": "TT" }""",
            ["UNVERIFIED: the document does not say whether the call only reads or also changes RCS-2000 (\"Synchronize map information with that in RCS-2000\"); it is NOT classed as read-only until Hikrobot confirms.", "The dataTyp values in the REQUEST are not listed (the response lists 1, 10, 11, 20, 55)."],
            [Str("code", true, 6, "Status code."), Str("message", true, 64, "Status description."), Str("reqCode", true, 64, "Request ID."), Objs("data", false, "cooX, cooY, dataTyp, direction, mapCode, mapDataCode, positionCode, berthType.", [])]),

        D("blockArea", "LocationMapArea", "5.2.12", RpcBase, "Empty or unblock the specified area (emptying removes all AMRs from the area and blocks it).", Critical,
            [Str("matterArea", true, 16, "ID of the area to be emptied or unblocked."), Str("indBind", true, 1, "Empty or unblock: 1 empty, 0 unblock.", Bind),
             Str("pause", true, 1, "Whether to stop all AMRs on the map when the area is already emptied: 0 no (default), 1 yes.", Bind),
             Str("controlMod", true, 2, "Scheduling mode: 0 move out (default), 1 interim park area, 2 specified area, -1 blocking mode.", ["0", "1", "2", "-1"]),
             Str("targetArea", false, 16, "The specified area ID. Required when controlMod is 2."), Str("noticeThird", true, 2, "Whether to notify the third-party platform when the area is emptied: 0 no, 1 yes.", Bind)],
            [new("RequiredWhen", ["targetArea"], "targetArea is required when controlMod is \"2\".", "controlMod", "2")],
            """{ "matterArea": "2", "indBind": "1", "pause": "0", "controlMod": "2", "targetArea": "HF002", "noticeThird": "0" }""",
            ["The sample request omits noticeThird although the table marks it Required.", "The description of \"pause\" is inconsistent in the PDF (\"pause emptying\" vs \"stop all AMRs on the map\").", "Effect: AMRs in the area stop or move away."]),

        D("clearRoadWay", "LocationMapArea", "5.2.8", RpcBase, "Clear all the data of roadways, containers or racks and notify RCS of updating the information.", Critical,
            [Str("roadWayCode", true, 16, "Roadway ID.")], null, """{ "roadWayCode": "RW1" }""",
            ["Effect: DELETES RCS roadway / container / rack data (the document: it cannot be cleared when the outbound lock capacity is not 0). The sample request shows an empty roadWayCode."]),

        D("blockStgBin", "LocationMapArea", "5.2.18", RpcBase, "Batch enable/disable bins (the batch succeeds or fails as a whole).", Mutating,
            [Objs("data", true, "Item list.", [Str("stgBinCode", true, 32, "Bin ID."), Str("action", true, 2, "Operation type: 0 enable bin, 1 disable bin.", Bind)])],
            null, """{ "data": [ { "stgBinCode": "BIN1", "action": "1" } ] }""",
            ["The document's sample spells the key \"stgBunCode\" (typo) while the table says \"stgBinCode\"; WCS uses stgBinCode.", "reqTime is described as an \"Expiry date\" here (yyyy-MM-dd HH:mm:ss) but as a request timestamp in other APIs."]),

        // ============================================================================ Binding / material / rack
        D("bindPodAndBerth", "BindingMaterialRack", "5.2.5", RpcBase, "Bind or unbind the rack and the location.", Mutating,
            [Str("podCode", true, 16, "Rack ID."), Str("positionCode", true, 32, "Position ID, predefined on the map."), Str("podDir", false, 6, "Rack direction: 0 horizontal (default), 1 vertical.", Bind),
             Str("characterValue", false, 64, "Trait value (racks on a roadway only): material type or batch."), Str("indBind", true, 1, "Bind or unbind: 1 bind, 0 unbind.", Bind)],
            null, """{ "podCode": "100001", "positionCode": "p05", "podDir": "0", "indBind": "1" }""",
            ["When unbinding, both podCode and positionCode are required for verification (document remark)."]),

        D("bindCtnrAndBin", "BindingMaterialRack", "5.2.6", RpcBase, "Bind or unbind the container and the bin.", Mutating,
            [Str("ctnrCode", true, 30, "Container ID."), Str("ctnrTyp", true, 16, "Container type."), Str("stgBinCode", false, 32, "Bin ID."), Str("binName", false, 32, "Custom bin ID, verified by RCS-2000."),
             Str("characterValue", false, 64, "Trait value (FMR roadway only)."), Str("positionCode", false, 32, "Position ID (for virtual racks' bins)."), Str("indBind", true, 1, "Bind or unbind: 1 bind, 0 unbind.", Bind)],
            [new("AtLeastOne", ["stgBinCode", "positionCode"], "One of stgBinCode and positionCode is required.")],
            """{ "ctnrCode": "C100", "ctnrTyp": "C1", "stgBinCode": "p05", "indBind": "1" }""",
            ["The sample request uses the key \"ctnrTyp\" for the container type, as in the table."]),

        D("bindPodAndMat", "BindingMaterialRack", "5.2.17", RpcBase, "Bind or unbind the material batch and the rack.", Mutating,
            [Str("podCode", true, 16, "Rack ID."), Str("materialLot", true, 32, "Material batch ID."), Str("indBind", true, 1, "Bind or unbind: 1 bind, 0 unbind.", Bind)],
            null, """{ "podCode": "100001", "materialLot": "123", "indBind": "1" }""", ["When unbinding both podCode and materialLot are required (document remark)."]),

        D("bindMaterialTypCodeAndBerth", "BindingMaterialRack", "5.2.23", RpcBase, "Bind or unbind the material type and the location (CMR).", Mutating,
            [Str("materialType", true, 32, "Material type No. (required for binding)."), Str("positionCode", true, 32, "Position ID, predefined on the map."), Str("ctnrCode", false, 6, "Container ID (required for binding the container)."),
             Str("indBind", true, 1, "Bind or unbind: 1 bind, 0 unbind.", Bind)],
            null, """{ "materialType": "1", "positionCode": "p05", "ctnrCode": "111101", "indBind": "1" }""",
            ["The maximum length of ctnrCode is shown as 6 in the PDF although the sample value \"111101\" has 6 characters and other APIs allow 30 / 32 for a container ID."]),

        // ============================================================================ CTU
        D("getOutPod", "Ctu", "5.2.19", TpsBase, "Container outbound, used for the buffer rack picking station (CTU / TPS).", Critical,
            [Str("taskTyp", true, 2, "Task type: 0 TPS tasks, 1 CTU tasks.", Bind),
             Objs("data", true, "Up to 1000 items.", [Str("taskCode", true, 64, "Task ID."), Str("ctnrCode", true, 32, "Container ID."), Str("binCode", false, 32, "Bin ID."), Str("wbCode", true, 32, "Workstation ID."),
                 Str("agvTyp", false, 16, "Device type."), Str("priority", false, 3, "Task priority [1,127].", null, 1, 127)], 1000)],
            null, """{ "taskTyp": "1", "data": [ { "taskCode": "T1", "ctnrCode": "C1", "wbCode": "WB1" } ] }""",
            ["The path is hikTpsService, not hikRpcService (as printed in the document).", "Document: clientCode / tokenCode are \"provided to the third-party platform by TPS\"."]),

        D("returnPod", "Ctu", "5.2.20", TpsBase, "Container inbound, the returning route of the outbound task (CTU / TPS).", Critical,
            [Str("taskCode", true, 64, "Task ID of the current workstation."), Str("agvTyp", false, 16, "Device type."), Str("returnPodStrategy", false, 32, "Inbound strategy ID (big area@small area)."),
             Str("taskTyp", true, 2, "Task type: 5 (container inbound).", ["5"]), Str("ctnrCode", true, 32, "Container ID."), Str("binCode", true, 32, "ID of the target bin the container is carried back to."),
             Str("srcBinCode", true, 32, "ID of the bin the container is carried from."), Str("wbCode", false, 32, "Workstation ID (required for initializing inbound).")],
            null, """{ "taskTyp": "5", "taskCode": "T1", "ctnrCode": "C1", "binCode": "B1", "srcBinCode": "B2" }""",
            ["The Required/Optional flags of binCode, srcBinCode and wbCode were read from the PDF's column order (the table is not legible as a grid).", "The path is hikTpsService."]),

        D("boxApplyPass", "Ctu", "5.2.22", RpcBase, "Container picking-up / placing callback: notify CMS that a picking / placing request is approved.", Critical,
            [Str("taskCode", true, 64, "Task ID."), Str("type", true, 2, "Task type: 1 picking request approved, 2 placing request approved.", ["1", "2"])],
            null, """{ "taskCode": "T1", "type": "1" }""",
            ["The function is the reply to an RCS application sent to the platform (callback applyReturnForValid / applyReturnForBin); calling it by hand answers a request the RCS may not have made."]),
    ];

    private static readonly Dictionary<string, RcsApiDef> ByName = All.ToDictionary(a => a.Name, StringComparer.Ordinal);

    public static RcsApiDef? Find(string? name) => name is not null && ByName.TryGetValue(name, out var d) ? d : null;

    /// <summary>Request fields = the four common fields + the API's own.</summary>
    public static IReadOnlyList<RcsField> RequestFields(RcsApiDef d) => [.. CommonFields, .. d.Fields];

    public static readonly IReadOnlyList<RcsCallbackDef> Callbacks =
    [
        new("agvCallback", "5.3.1", "Task executing status sent by RCS-2000 to the third-party platform.", "NotProvided"),
        new("warnCallback", "5.3.2", "Alarm sent by RCS-2000 to the third-party platform.", "NotProvided"),
        new("bindNotify", "5.3.3", "Notification of a binding / unbinding operation sent to the third-party platform.", "NotProvided"),
        new("applyReturnForValid", "5.3.4", "Check with the third-party platform whether the container inbound at the loader/unloader is allowed (CTU).", "NotProvided"),
        new("applyReturnForBin", "5.3.5", "Apply to the third-party platform for the inbound position of the container (CTU).", "NotProvided"),
    ];
}
