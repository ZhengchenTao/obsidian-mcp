using ObsidianMcp.Config;

namespace ObsidianMcp.Endpoints;

public static class DiscoveryEndpoints
{
    /// <summary>
    /// 注册两个 well-known 端点：
    /// 1. /.well-known/oauth-authorization-server (RFC 8414)：指向配置的 AS
    /// 2. /.well-known/oauth-protected-resource (RFC 9728)：告诉客户端这个资源
    ///    的 identifier 是什么 + 用哪个 AS。客户端读到 PRM 后会在
    ///    /authorize 请求里带 resource=<identifier>，满足 AS 的 RFC 8707 校验。
    /// </summary>
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/oauth-authorization-server", (McpDiscoveryOptions opts) =>
        {
            return Results.Ok(new
            {
                issuer = opts.Issuer,
                authorization_endpoint = opts.AuthorizationEndpoint,
                token_endpoint = opts.TokenEndpoint,
                registration_endpoint = opts.RegistrationEndpoint,
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                scopes_supported = new[] { "read:obsidian", "write:obsidian" },
            });
        })
        .AllowAnonymous()
        .WithName("OAuthDiscovery");

        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx, McpDiscoveryOptions opts) =>
        {
            var resourceUrl = string.IsNullOrWhiteSpace(opts.ResourceUrl)
                ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                : opts.ResourceUrl;
            return Results.Ok(new
            {
                resource = resourceUrl,
                authorization_servers = new[] { opts.Issuer },
                scopes_supported = new[] { "read:obsidian", "write:obsidian" },
                bearer_methods_supported = new[] { "header" },
            });
        })
        .AllowAnonymous()
        .WithName("ProtectedResourceMetadata");
    }
}
