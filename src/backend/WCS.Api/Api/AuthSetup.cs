using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WCS.Application.Auth;
using WCS.Domain.Users;

namespace WCS.Api.Api;

/// <summary>
/// Cookie-based session authentication (HttpOnly, SameSite=Strict, Secure outside Development) and role policies.
/// Every request re-validates the account against the database, so disabling an account, changing a role or resetting a password
/// takes effect immediately, and the session also ends after an idle and an absolute timeout.
/// </summary>
public static class AuthSetup
{
    public const string Scheme = "WcsCookie";
    public const string AdminPolicy = "AdminOnly";
    public const string OperationsPolicy = "Operations";
    public const string ClientHeader = "X-WCS-Client";

    private const string ClaimStamp = "wcs:stamp";
    private const string ClaimMustChange = "wcs:must_change";
    private const string ClaimSignedInAt = "wcs:signed_in_at";
    private const string ClaimDisplayName = "wcs:display_name";

    public static IServiceCollection AddWcsAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var opts = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        services.AddAuthentication(Scheme).AddCookie(Scheme, o =>
        {
            o.Cookie.Name = "wcs.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(1, opts.IdleTimeoutMinutes));
            o.SlidingExpiration = true;
            o.Events = new CookieAuthenticationEvents
            {
                // An API: answer with status codes, never with a redirect to a login page.
                OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
                OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
                OnValidatePrincipal = ValidateAsync,
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(OperationsPolicy, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Operator)))
            // Anything not explicitly opened (login) needs a signed-in user.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var sp = ctx.HttpContext.RequestServices;
        var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
        var time = ctx.Options.TimeProvider ?? TimeProvider.System;

        var ok = Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                 && long.TryParse(ctx.Principal?.FindFirstValue(ClaimSignedInAt), out var signedAt)
                 && time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(signedAt) < TimeSpan.FromHours(Math.Max(1, auth.AbsoluteTimeoutHours));
        if (ok)
        {
            var user = await sp.GetRequiredService<IUserRepository>().GetAsync(id, ctx.HttpContext.RequestAborted);
            ok = user is { IsActive: true } && user.SecurityStamp.ToString() == ctx.Principal!.FindFirstValue(ClaimStamp);
        }
        if (!ok)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(Scheme);
        }
    }

    public static ClaimsPrincipal ToPrincipal(AuthenticatedUser u, TimeProvider? time = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Username),
            new(ClaimTypes.Role, u.Role.ToString()),
            new(ClaimDisplayName, u.DisplayName),
            new(ClaimStamp, u.SecurityStamp.ToString()),
            new(ClaimSignedInAt, (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds().ToString()),
        };
        if (u.MustChangePassword) claims.Add(new Claim(ClaimMustChange, "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static Task SignInAsync(this HttpContext c, AuthenticatedUser u)
    {
        var time = c.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).TimeProvider;
        return c.SignInAsync(Scheme, ToPrincipal(u, time), new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    public static ActorInfo Actor(this HttpContext c)
    {
        var p = c.User;
        var id = Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
        var role = Enum.TryParse<UserRole>(p.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.Operator;
        return new ActorInfo(id, p.Identity?.Name ?? "?", role, c.Connection.RemoteIpAddress?.ToString());
    }

    public static string DisplayName(this ClaimsPrincipal p) => p.FindFirstValue(ClaimDisplayName) ?? p.Identity?.Name ?? "?";
    public static bool MustChangePassword(this ClaimsPrincipal p) => p.HasClaim(ClaimMustChange, "1");

    /// <summary>
    /// Pipeline guards that run after authentication: (1) a session that must change its password may only use /api/auth,
    /// (2) state-changing API calls must carry the custom client header (cross-site forms/images cannot add it).
    /// </summary>
    public static IApplicationBuilder UseWcsRequestGuards(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        var isApi = path.StartsWithSegments("/api");
        if (isApi && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
            && !ctx.Request.Headers.ContainsKey(ClientHeader))
        {
            await Write(ctx, StatusCodes.Status403Forbidden, "CLIENT_HEADER_MISSING", $"Thiếu header {ClientHeader}.");
            return;
        }
        if ((isApi || path.StartsWithSegments("/hubs")) && !path.StartsWithSegments("/api/auth")
            && ctx.User.Identity?.IsAuthenticated == true && ctx.User.MustChangePassword())
        {
            await Write(ctx, StatusCodes.Status403Forbidden, "PASSWORD_CHANGE_REQUIRED", "Bạn cần đổi mật khẩu trước khi tiếp tục.");
            return;
        }
        await next();
    });

    private static Task Write(HttpContext c, int status, string code, string message)
    {
        c.Response.StatusCode = status;
        return c.Response.WriteAsJsonAsync(new ApiResponse<object>(false, null, new ApiError(code, message), c.TraceIdentifier));
    }
}
