using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Auth;

internal sealed class UserRepository(WcsDbContext db) : IUserRepository
{
    public Task<AppUser?> GetAsync(Guid id, CancellationToken ct) => db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct) => db.Users.FirstOrDefaultAsync(x => x.Username == username, ct);

    public async Task<IReadOnlyList<AppUser>> ListAsync(string? search, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(x => x.Username.Contains(s) || x.DisplayName.ToLower().Contains(s));
        }
        return await q.OrderBy(x => x.Username).Take(500).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AppUser>> ListActiveAdminsAsync(CancellationToken ct) =>
        await db.Users.Where(x => x.Role == UserRole.Admin && x.IsActive).OrderBy(x => x.Username).ToListAsync(ct);

    public Task<bool> AnyAdminAsync(CancellationToken ct) => db.Users.AnyAsync(x => x.Role == UserRole.Admin && x.IsActive, ct);
    public void Add(AppUser user) => db.Users.Add(user);
}

/// <summary>ASP.NET Core Identity password hasher (PBKDF2, per-password random salt, versioned format).</summary>
internal sealed class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly AppUser Anonymous = AppUser.Create("hasher", "hasher", UserRole.Operator, "x", false, DateTimeOffset.UnixEpoch);

    public string Hash(string password) => Hasher.HashPassword(Anonymous, password);

    public bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Anonymous, hash, password) != PasswordVerificationResult.Failed;
}

/// <summary>Audit trail writer. Uses its own short-lived DbContext so it neither joins nor is rolled back with a business transaction.</summary>
internal sealed class AuditLog(DbContextOptions<WcsDbContext> options, IClock clock, ILogger<AuditLog> logger) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly string[] SecretWords = ["password", "token", "secret", "hash", "apikey"];

    public async Task WriteAsync(AuditRecord r, CancellationToken ct = default)
    {
        try
        {
            await using var db = new WcsDbContext(options);
            db.AuditLog.Add(new AuditEntry(clock.UtcNow, Cut(r.Actor, 100), Cut(r.ActorRole, 40), Cut(r.Action, 100)!, Cut(r.TargetType, 50), Cut(r.TargetId, 100),
                r.Result, Cut(r.Detail, 1000), Serialize(r.Before), Serialize(r.After), Cut(r.IpAddress, 64)));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never break the user's operation because the audit table is unavailable; surface it in the application log instead.
            logger.LogError(ex, "Audit write failed for action {Action}", r.Action);
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery q, CancellationToken ct = default)
    {
        await using var db = new WcsDbContext(options);
        IQueryable<AuditEntry> query = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Actor)) query = query.Where(x => x.Actor == q.Actor.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(x => x.Action == q.Action.Trim());
        if (q.Result is { } res) query = query.Where(x => x.Result == res);
        if (q.From is { } from) query = query.Where(x => x.OccurredAt >= from);
        if (q.To is { } to) query = query.Where(x => x.OccurredAt <= to);
        return await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip(Math.Max(0, q.Skip)).Take(Math.Clamp(q.Take, 1, 500)).ToListAsync(ct);
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>Serializes a snapshot and blanks any string value whose key looks like a secret (defence in depth).</summary>
    internal static string? Serialize(object? snapshot)
    {
        if (snapshot is null) return null;
        var node = JsonSerializer.SerializeToNode(snapshot, Json);
        Redact(node);
        return node?.ToJsonString(Json);
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (o[key] is JsonValue v && v.TryGetValue<string>(out _) && SecretWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        o[key] = "***";
                    else Redact(o[key]);
                }
                break;
            case JsonArray a:
                foreach (var item in a) Redact(item);
                break;
        }
    }
}

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Bootstrap:Admin";
    public string? Username { get; set; }
    /// <summary>Initial password. Supply via user secrets / environment variable (Bootstrap__Admin__Password), never in source control.</summary>
    public string? Password { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>
/// Creates the FIRST administrator, only when no active administrator exists and the deployment supplied credentials.
/// The account must change the password at first login. There is no built-in default account.
/// </summary>
public sealed class AdminBootstrapper(IUserRepository users, IPasswordService passwords, IUnitOfWork uow, IAuditLog audit, IClock clock,
    IConfiguration configuration, ILogger<AdminBootstrapper> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var o = configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>() ?? new();
        var created = await uow.ExecuteAsync(async t =>
        {
            if (await users.AnyAdminAsync(t)) return (string?)null;
            if (string.IsNullOrWhiteSpace(o.Username) || string.IsNullOrEmpty(o.Password))
            {
                logger.LogWarning("No active administrator exists and no bootstrap credentials are configured (Bootstrap:Admin:Username / Bootstrap:Admin:Password). Nobody can sign in until one is provided.");
                return null;
            }
            var name = AppUser.NormalizeUsername(o.Username);
            var errors = PasswordPolicy.Check(o.Password, name);
            if (!AppUser.IsValidUsername(name) || errors.Count > 0)
            {
                logger.LogError("Bootstrap administrator was NOT created: username or password does not satisfy the policy ({Problems}).",
                    string.Join("; ", errors.Select(e => e.Message)));
                return null;
            }
            if (await users.GetByUsernameAsync(name, t) is not null)
            {
                logger.LogError("Bootstrap administrator was NOT created: the username '{Username}' already belongs to a non-admin or disabled account.", name);
                return null;
            }
            users.Add(AppUser.Create(name, string.IsNullOrWhiteSpace(o.DisplayName) ? name : o.DisplayName!, UserRole.Admin,
                passwords.Hash(o.Password), mustChangePassword: true, clock.UtcNow));
            return name;
        }, ct);

        if (created is null) return;
        logger.LogInformation("Bootstrap administrator '{Username}' created; the password must be changed at first sign-in.", created);
        await audit.WriteAsync(new AuditRecord("USER_BOOTSTRAP_ADMIN", "system", null, "User", created, AuditResult.Success, "first administrator created from deployment configuration"), ct);
    }
}
