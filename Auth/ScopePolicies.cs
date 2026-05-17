using Microsoft.AspNetCore.Authorization;

namespace ObsidianMcp.Auth;

/// <summary>
/// 自定义 scope 校验 Policy：
///   RequireScope("read:obsidian")
///   RequireScope("write:obsidian")
///
/// JWT 的 scope claim 可能是单个字符串（空格分隔）或多个 claim，两种都处理。
/// </summary>
public static class ScopePolicies
{
    public const string ReadObsidian = "read:obsidian";
    public const string WriteObsidian = "write:obsidian";

    /// <summary>注册两条 scope policy 到 AuthorizationOptions。</summary>
    public static void AddScopePolicies(this AuthorizationOptions opts)
    {
        opts.AddPolicy(ReadObsidian, policy =>
            policy.RequireAuthenticatedUser()
                  .AddRequirements(new ScopeRequirement(ReadObsidian)));

        opts.AddPolicy(WriteObsidian, policy =>
            policy.RequireAuthenticatedUser()
                  .AddRequirements(new ScopeRequirement(WriteObsidian)));
    }
}

// ---------- Requirement ----------

public class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = scope;
}

// ---------- Handler ----------

public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        // scope claim 在 JWT 里可能是一整个空格分隔的字符串，也可能是多个 claim。
        // OAuth 2.0 (RFC 6749) 规定 scope 大小写敏感，按 Ordinal 比对。
        var scopes = context.User
            .FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        if (scopes.Contains(requirement.RequiredScope))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

// ---------- Per-tool scope guard helper ----------

/// <summary>
/// MCP Tool 内部 scope 校验：从当前 HttpContext.User 读 scope claim，
/// 不包含 requiredScope 时抛 UnauthorizedAccessException。
///
/// 用法：在每个读 / 写 Tool 的方法体首行调一下，给客户端可读的失败原因。
/// 端点级 RequireAuthorization 只确保 JWT 验签通过；scope 颗粒度门禁在这里。
/// OAuth 2.0 (RFC 6749) 规定 scope 大小写敏感。
/// </summary>
public static class ToolScopeGuard
{
    public static void EnsureScope(IHttpContextAccessor http, string requiredScope)
    {
        var ctx = http.HttpContext
            ?? throw new InvalidOperationException("无 HttpContext，无法校验 scope。");

        var scopes = ctx.User
            .FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        if (!scopes.Contains(requiredScope))
            throw new UnauthorizedAccessException(
                $"当前 Token 缺少所需 scope：{requiredScope}");
    }
}
