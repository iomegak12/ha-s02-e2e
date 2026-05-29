using System.Security.Claims;
using Nexus.Web.Host.Auth;

namespace Nexus.Web.Host.Proxy;

/// <summary>
/// Intercepts /api/v1/* requests that are not handled by local controllers,
/// injects the server-side Bearer token, and streams the response back to the WASM client.
/// The browser never sends or receives a raw JWT.
/// </summary>
public sealed class ReverseProxyMiddleware
{
    // Paths handled by local controllers — skip proxying for these
    private static readonly string[] LocalPaths =
    [
        "/api/v1/session",
        "/api/v1/me",
    ];

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<ReverseProxyMiddleware> _logger;

    public ReverseProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        ILogger<ReverseProxyMiddleware> logger)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only intercept /api/v1/* paths that are NOT handled locally
        if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)
            || IsLocalPath(path))
        {
            await _next(context);
            return;
        }

        var clientName = ServiceRouter.Resolve(path);
        if (clientName is null)
        {
            await _next(context);
            return;
        }

        // Require authentication for all proxied routes
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var sessionId = context.User.FindFirstValue("nexus.session_id");
        var tokenEntry = sessionId is not null ? _tokenStore.Get(sessionId) : null;

        if (tokenEntry is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var client = _httpClientFactory.CreateClient(clientName);

        // Build the outbound request
        var queryString = context.Request.QueryString.Value ?? "";
        var targetUri = new Uri(client.BaseAddress!, path + queryString);

        using var outboundRequest = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            targetUri);

        // Forward body (for POST/PUT/PATCH)
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            outboundRequest.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                outboundRequest.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }

        // Forward safe headers
        foreach (var header in context.Request.Headers)
        {
            var name = header.Key;
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!outboundRequest.Headers.TryAddWithoutValidation(name, header.Value.ToArray()))
                outboundRequest.Content?.Headers.TryAddWithoutValidation(name, header.Value.ToArray());
        }

        // Inject server-side JWT — browser never sees this token
        outboundRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenEntry.AccessToken);

        // Execute and stream response back
        HttpResponseMessage downstreamResponse;
        try
        {
            downstreamResponse = await client.SendAsync(
                outboundRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Downstream service {ClientName} unreachable for {Path}", clientName, path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type    = "https://errors.nexusha.local/gateway-error",
                title   = "Service Unavailable",
                status  = 503,
                detail  = $"Downstream service is temporarily unavailable.",
                instance = path
            });
            return;
        }

        context.Response.StatusCode = (int)downstreamResponse.StatusCode;

        foreach (var header in downstreamResponse.Headers)
            context.Response.Headers.TryAdd(header.Key, string.Join(",", header.Value));

        foreach (var header in downstreamResponse.Content.Headers)
            context.Response.Headers.TryAdd(header.Key, string.Join(",", header.Value));

        // Remove transfer-encoding chunked — ASP.NET Core handles this
        context.Response.Headers.Remove("transfer-encoding");

        await downstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static bool IsLocalPath(string path) =>
        LocalPaths.Any(lp => path.StartsWith(lp, StringComparison.OrdinalIgnoreCase));
}
