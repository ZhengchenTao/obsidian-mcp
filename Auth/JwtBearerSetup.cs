using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ObsidianMcp.Config;
using System.Text;

namespace ObsidianMcp.Auth;

public static class JwtBearerSetup
{
    /// <summary>
    /// 配置 JWT Bearer 认证。根据 JwtOptions.Algorithm 选择：
    /// <list type="bullet">
    ///   <item><c>HS256</c>：与 AS 共享对称密钥（Current + Previous 双密钥用于轮换）。</item>
    ///   <item><c>RS256</c>：委托 ASP.NET Core 通过 OIDC discovery 拉 JWKS，
    ///     自动刷新公钥（默认 24h），AS 端密钥轮换无需重启本服务。</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddObsidianJwtBearer(
        this IServiceCollection services,
        JwtOptions opts)
    {
        var algorithm = (opts.Algorithm ?? "HS256").Trim().ToUpperInvariant();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // 关闭默认的入站 claim type 映射，否则 "sub"/"scope" 会被改写成
                // ClaimTypes.NameIdentifier 之类的长 URI，下游 FindFirst("sub") 取不到。
                options.MapInboundClaims = false;

                var tvp = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,

                    ValidateAudience = true,
                    ValidAudience = opts.Audience,

                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),

                    NameClaimType = "sub",
                };

                if (algorithm == "RS256")
                {
                    if (string.IsNullOrWhiteSpace(opts.Issuer))
                        throw new InvalidOperationException(
                            "Jwt:Issuer 未配置，RS256 模式无法启动。");

                    // Authority = Issuer 时，JwtBearer 会自动从
                    // <Issuer>/.well-known/openid-configuration 拉 OIDC 元数据，
                    // 再从其中的 jwks_uri 拉公钥并周期性刷新。IssuerSigningKeys
                    // 由 ConfigurationManager 在验签时动态注入，无需在此设置。
                    options.Authority = opts.Issuer;
                    options.RequireHttpsMetadata = !IsLocalhostUrl(opts.Issuer);
                }
                else if (algorithm == "HS256")
                {
                    tvp.IssuerSigningKeys = BuildHs256Keys(opts).ToList();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Jwt:Algorithm '{opts.Algorithm}' 不支持。可选值：HS256, RS256");
                }

                options.TokenValidationParameters = tvp;
            });

        return services;
    }

    private static IEnumerable<SecurityKey> BuildHs256Keys(JwtOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.SigningKey.Current))
            throw new InvalidOperationException(
                "Jwt:SigningKey:Current 未配置，HS256 模式无法启动。");

        yield return new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(opts.SigningKey.Current));

        if (!string.IsNullOrWhiteSpace(opts.SigningKey.Previous))
            yield return new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(opts.SigningKey.Previous));
    }

    private static bool IsLocalhostUrl(string url) =>
        url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
