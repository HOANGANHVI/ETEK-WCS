using System.Net.Http.Json;
using System.Text.Json;
using WCS.Application.Auth;
using WCS.Domain.Users;
using WCS.Infrastructure.Auth;

namespace WCS.Tests.Support;

public sealed class InMemoryUserRepository : IUserRepository
{
    public List<AppUser> Users { get; } = [];
    public Task<AppUser?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    public Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.Username == username));
    public Task<IReadOnlyList<AppUser>> ListAsync(string? search, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppUser>>(Users.Where(u => string.IsNullOrWhiteSpace(search) || u.Username.Contains(search.ToLowerInvariant())).ToList());
    public Task<IReadOnlyList<AppUser>> ListActiveAdminsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppUser>>(Users.Where(u => u is { Role: UserRole.Admin, IsActive: true }).ToList());
    public Task<bool> AnyAdminAsync(CancellationToken ct = default) => Task.FromResult(Users.Any(u => u is { Role: UserRole.Admin, IsActive: true }));
    public void Add(AppUser user) => Users.Add(user);
    public void Remove(AppUser user) => Users.Remove(user);
}

public sealed class RecordingAuditLog : IAuditLog
{
    public List<AuditRecord> Records { get; } = [];
    public Task WriteAsync(AuditRecord record, CancellationToken ct = default) { lock (Records) Records.Add(record); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    public Task<int> CountByActorAsync(string username, CancellationToken ct = default) { lock (Records) return Task.FromResult(Records.Count(r => r.Actor == username)); }
}

/// <summary>Controllable clock for the cookie handler (idle/absolute session timeout tests).</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public static class TestUsers
{
    public const string Password = "Correct-Horse-42";
    private static readonly PasswordService Hasher = new();

    public static AppUser Make(string username, UserRole role, string? password = null, bool mustChange = false, bool active = true)
    {
        var u = AppUser.Create(username, username.ToUpperInvariant(), role, Hasher.Hash(password ?? Password), mustChange, DateTimeOffset.UtcNow);
        if (!active) u.SetActive(false, DateTimeOffset.UtcNow);
        return u;
    }
}

public static class HttpTestExtensions
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static HttpClient WithClientHeader(this HttpClient c)
    {
        c.DefaultRequestHeaders.Remove("X-WCS-Client");
        c.DefaultRequestHeaders.Add("X-WCS-Client", "tests");
        return c;
    }

    public static async Task<(int Status, JsonElement Root, string Raw)> SendJsonAsync(this HttpClient c, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Web);
        var res = await c.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var root = string.IsNullOrWhiteSpace(raw) ? default : JsonDocument.Parse(raw).RootElement;
        return ((int)res.StatusCode, root, raw);
    }

    public static async Task<int> LoginAsync(this HttpClient c, string username, string password = TestUsers.Password) =>
        (await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username, password })).Status;

    public static string ErrorCode(this JsonElement root) => root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
        ? e.GetProperty("code").GetString() ?? "" : "";
}
