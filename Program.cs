using Microsoft.AspNetCore.Authorization;
using ObsidianMcp.Auth;
using ObsidianMcp.Config;
using ObsidianMcp.Endpoints;
using ObsidianMcp.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── 配置绑定 ───────────────────────────────────────────────────────────────

var jwtOpts = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
    ?? new JwtOptions();

// ─── 配置对象注册到 DI ───────────────────────────────────────────────────────

builder.Services.Configure<VaultOptions>(
    builder.Configuration.GetSection(VaultOptions.Section));
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.Section));

// McpDiscoveryOptions 直接注册为单例（供 DiscoveryEndpoints 依赖注入）
var discoveryOpts = builder.Configuration.GetSection(McpDiscoveryOptions.Section).Get<McpDiscoveryOptions>()
    ?? new McpDiscoveryOptions();
builder.Services.AddSingleton(discoveryOpts);

// ─── 认证与授权 ──────────────────────────────────────────────────────────────

builder.Services.AddObsidianJwtBearer(jwtOpts);

builder.Services.AddAuthorization(opts =>
{
    opts.AddScopePolicies();
    // 默认 policy：只要求已认证（JWT 验签通过即可），不要求特定 scope
    opts.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// scope 自定义 handler
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

// IHttpContextAccessor（Tool 里取 User / scope 用）
builder.Services.AddHttpContextAccessor();

// ─── MCP SDK ─────────────────────────────────────────────────────────────────

builder.Services.AddMcpServer()
    .WithHttpTransport()             // Streamable HTTP（单端点 POST /mcp）
    .WithToolsFromAssembly();        // 自动扫描 [McpServerToolType]

// ─── 业务服务 ────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<VaultPathResolver>();
builder.Services.AddSingleton<VaultWriteGuard>();
builder.Services.AddSingleton<VaultSearchService>();
builder.Services.AddSingleton<AuditLogger>();

// ─── Build ───────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Middleware 顺序（顺序不能乱）───────────────────────────────────────────

app.UseAuthentication();
app.UseAuthorization();

// ─── 路由 ────────────────────────────────────────────────────────────────────

// /.well-known/oauth-authorization-server（不需要认证）
app.MapDiscoveryEndpoints();

// 健康检查（方便 docker compose 和 NPM 探活）
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
    .AllowAnonymous();

// MCP 端点（必须认证，Bearer JWT）。
// 端点级只校验"已认证"，scope 校验放在每个 Tool 里：
//   - 读 tool 校验 read:obsidian
//   - 写 tool 校验 write:obsidian
// 这样客户端拿单一 scope（仅读 / 仅写）的 Token 都能正常用对应工具。
app.MapMcp("/mcp").RequireAuthorization();

app.Run();
