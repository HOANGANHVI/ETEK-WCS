using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Domain.Common;

namespace WCS.Application.Config;

/// <summary>
/// Reads/saves PLC and RCS connection settings and runs the safe connectivity check. It owns configuration only: it never drives a
/// device and never sends business commands. Secrets are encrypted at rest, never returned and never logged.
/// </summary>
public sealed class ConnectionConfigService(
    IUnitOfWork uow,
    IConnectionSettingRepository repo,
    ISecretProtector protector,
    IConnectionProbe probe,
    IClock clock,
    ILogger<ConnectionConfigService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Secrets(string? Password, string? Token);

    // ------------------------------------------------------------------ PLC
    public async Task<PlcConfigView> GetPlcAsync(CancellationToken ct = default)
    {
        var s = await uow.ExecuteAsync(t => repo.GetAsync(ConnectionKind.Plc, t), ct);
        if (s is null) return new PlcConfigView(false, false, new PlcSettings(null, null, null, null, null, null, null, null, null), null, null, null);
        return new PlcConfigView(true, s.Enabled, Read<PlcSettings>(s) ?? Empty.Plc, LastTest(s), s.UpdatedAt, s.UpdatedBy);
    }

    public async Task<PlcConfigView> UpdatePlcAsync(UpdatePlcCommand cmd, CancellationToken ct = default)
    {
        var n = Normalize(cmd.Settings);
        var errors = ConnectionValidator.ValidatePlc(n, cmd.Enabled);
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await uow.ExecuteAsync(async t =>
        {
            var s = await repo.GetAsync(ConnectionKind.Plc, t);
            if (s is null) { s = new ConnectionSetting(ConnectionKind.Plc, clock.UtcNow); repo.Add(s); }
            s.Update(cmd.Enabled, JsonSerializer.Serialize(n, Json), s.ProtectedSecrets, ConnectionValidator.Clean(cmd.UpdatedBy), clock.UtcNow);
            return true;
        }, ct);
        logger.LogInformation("PLC connection settings updated (enabled={Enabled})", cmd.Enabled); // never log addresses or secrets
        return await GetPlcAsync(ct);
    }

    // ------------------------------------------------------------------ RCS
    public async Task<RcsConfigView> GetRcsAsync(CancellationToken ct = default)
    {
        var s = await uow.ExecuteAsync(t => repo.GetAsync(ConnectionKind.Rcs, t), ct);
        if (s is null) return new RcsConfigView(false, false, Empty.Rcs, new SecretState(false, false), null, null, null);
        var sec = ReadSecrets(s);
        return new RcsConfigView(true, s.Enabled, Read<RcsSettings>(s) ?? Empty.Rcs,
            new SecretState(!string.IsNullOrEmpty(sec.Password), !string.IsNullOrEmpty(sec.Token)), LastTest(s), s.UpdatedAt, s.UpdatedBy);
    }

    public async Task<RcsConfigView> UpdateRcsAsync(UpdateRcsCommand cmd, CancellationToken ct = default)
    {
        var n = Normalize(cmd.Settings);
        // Validation needs to know the secrets that will exist AFTER the update, so it runs inside the unit of work (read-only until it passes).
        await uow.ExecuteAsync(async t =>
        {
            var s = await repo.GetAsync(ConnectionKind.Rcs, t);
            var current = s is null ? new Secrets(null, null) : ReadSecrets(s);
            var password = cmd.ClearPassword ? null : ConnectionValidator.Clean(cmd.Password) ?? current.Password;
            var token = cmd.ClearToken ? null : ConnectionValidator.Clean(cmd.Token) ?? current.Token;

            var errors = ConnectionValidator.ValidateRcs(n, cmd.Enabled, !string.IsNullOrEmpty(password), !string.IsNullOrEmpty(token));
            if (errors.Count > 0) throw new ValidationFailedException(errors);

            // Credentials that the chosen auth mode does not use are not kept.
            if (n.AuthMode != ConnectionOptions.AuthUserPassword) password = null;
            if (n.AuthMode != ConnectionOptions.AuthToken) token = null;
            var blob = password is null && token is null ? null : protector.Protect(JsonSerializer.Serialize(new Secrets(password, token), Json));

            if (s is null) { s = new ConnectionSetting(ConnectionKind.Rcs, clock.UtcNow); repo.Add(s); }
            s.Update(cmd.Enabled, JsonSerializer.Serialize(n, Json), blob, ConnectionValidator.Clean(cmd.UpdatedBy), clock.UtcNow);
            return true;
        }, ct);
        logger.LogInformation("RCS connection settings updated (enabled={Enabled}, auth={Auth})", cmd.Enabled, n.AuthMode);
        return await GetRcsAsync(ct);
    }

    // ------------------------------------------------------------------ safe connectivity check
    /// <summary>Tests the SAVED configuration only (so a half-edited form can never trigger a check).</summary>
    public async Task<ConnectionTestResult> TestAsync(ConnectionKind kind, CancellationToken ct = default)
    {
        var s = await uow.ExecuteAsync(t => repo.GetAsync(kind, t), ct);
        ConnectionTestResult Done(ConnectionTestOutcome o, string mode, string msg, string? err = null, long? ms = null) =>
            new(o, mode, msg, err, ms, clock.UtcNow);

        ConnectionTestResult result;
        if (s is null) result = Done(ConnectionTestOutcome.NotExecuted, "None", "Chưa có cấu hình đã lưu. Hãy nhập và lưu cấu hình trước khi kiểm tra.");
        else if (!s.Enabled) result = Done(ConnectionTestOutcome.NotExecuted, "None", "Kết nối đang ở trạng thái tắt nên không thực hiện kiểm tra.");
        else
        {
            string? host; int? port; int? timeout; IReadOnlyList<FieldError> errors;
            if (kind == ConnectionKind.Plc)
            {
                var p = Read<PlcSettings>(s) ?? Empty.Plc;
                (host, port, timeout) = (p.IpAddress, p.Port, p.TimeoutMs);
                errors = ConnectionValidator.ValidatePlc(p, true);
            }
            else
            {
                var r = Read<RcsSettings>(s) ?? Empty.Rcs;
                var sec = ReadSecrets(s);
                (host, port, timeout) = (r.Host, r.Port, r.TimeoutMs);
                errors = ConnectionValidator.ValidateRcs(r, true, !string.IsNullOrEmpty(sec.Password), !string.IsNullOrEmpty(sec.Token));
            }

            if (errors.Count > 0 || host is null || port is null || timeout is null)
                result = Done(ConnectionTestOutcome.NotExecuted, "None", "Cấu hình chưa đầy đủ hoặc không hợp lệ nên không thực hiện kiểm tra.",
                    string.Join("; ", errors.Select(x => x.Message)));
            else
            {
                var p = await probe.ProbeAsync(kind, host, port.Value, TimeSpan.FromMilliseconds(timeout.Value), ct);
                result = Done(p.Outcome, p.Mode, p.Message, p.Error, p.LatencyMs);
            }
        }

        if (s is not null)
            await uow.ExecuteAsync(async t =>
            {
                var fresh = await repo.GetAsync(kind, t);
                fresh?.RecordTest(result.Outcome, result.Mode, result.Message, result.Error, result.LatencyMs, result.TestedAt);
                return true;
            }, ct);
        logger.LogInformation("Connection check {Kind}: {Outcome} ({Mode})", kind, result.Outcome, result.Mode);
        return result;
    }

    // ------------------------------------------------------------------ helpers
    private static class Empty
    {
        public static readonly PlcSettings Plc = new(null, null, null, null, null, null, null, null, null);
        public static readonly RcsSettings Rcs = new(null, null, null, null, ConnectionOptions.AuthNone, null, null);
    }

    private static PlcSettings Normalize(PlcSettings s) => s with
    {
        DeviceName = ConnectionValidator.Clean(s.DeviceName),
        IpAddress = Canonical(ConnectionValidator.Clean(s.IpAddress)),
        Protocol = ConnectionValidator.Clean(s.Protocol),
    };

    private static RcsSettings Normalize(RcsSettings s) => s with
    {
        SystemName = ConnectionValidator.Clean(s.SystemName),
        Host = ConnectionValidator.Clean(s.Host),
        BaseUrl = ConnectionValidator.Clean(s.BaseUrl),
        AuthMode = ConnectionValidator.Clean(s.AuthMode) ?? ConnectionOptions.AuthNone,
        Username = ConnectionValidator.Clean(s.Username),
    };

    /// <summary>Keeps invalid text as typed (the validator reports it); canonicalises valid IPs.</summary>
    private static string? Canonical(string? ip) => ip is not null && ConnectionValidator.IsStrictIp(ip) && IPAddress.TryParse(ip, out var a) ? a.ToString() : ip;

    private static T? Read<T>(ConnectionSetting s) where T : class => JsonSerializer.Deserialize<T>(s.SettingsJson, Json);

    private Secrets ReadSecrets(ConnectionSetting s)
    {
        if (string.IsNullOrEmpty(s.ProtectedSecrets)) return new Secrets(null, null);
        try { return JsonSerializer.Deserialize<Secrets>(protector.Unprotect(s.ProtectedSecrets), Json) ?? new Secrets(null, null); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Key ring lost / blob corrupted: treat as "no secret stored" so the operator can enter it again. Nothing sensitive is logged.
            logger.LogWarning("Stored connection secrets could not be decrypted (kind={Kind}); they must be entered again", s.Kind);
            return new Secrets(null, null);
        }
    }

    private static LastTestView? LastTest(ConnectionSetting s) => s.LastTestAt is null || s.LastTestOutcome is null
        ? null
        : new LastTestView(s.LastTestOutcome.Value, s.LastTestMode ?? "", s.LastTestMessage ?? "", s.LastError, s.LastLatencyMs, s.LastTestAt.Value);
}
