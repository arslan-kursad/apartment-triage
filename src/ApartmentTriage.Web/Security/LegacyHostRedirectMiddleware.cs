namespace ApartmentTriage.Web.Security;

/// <summary>
/// Permanently redirects legacy public hostnames (e.g. hanwas-ai.fly.dev) to the canonical domain.
/// </summary>
public sealed class LegacyHostRedirectMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        var legacyHosts = configuration.GetSection("Hosting:LegacyHosts").Get<string[]>() ?? [];
        var canonicalHost = configuration["Hosting:CanonicalHost"]?.Trim();

        var path = context.Request.Path;

        // Never redirect infrastructure / machine-to-machine paths — these callers do not follow
        // redirects: the Fly health probe (/health), and the Meta WhatsApp webhook (/api/webhook),
        // which silently drops delivery on a 3xx. (/hangfire kept local too.)
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/webhook", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/hangfire", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(canonicalHost)
            || legacyHosts.Length == 0
            || !legacyHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var pathStr = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value! : string.Empty;
        var target = $"https://{canonicalHost}{pathStr}{query}";

        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = target;
    }
}
