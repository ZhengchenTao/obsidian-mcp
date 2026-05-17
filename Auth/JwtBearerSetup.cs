using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ObsidianMcp.Config;
using System.Text;

namespace ObsidianMcp.Auth;

public static class JwtBearerSetup
{
    /// <summary>
    /// 配置 HS256 JWT Bearer 认证。
    /// 支持 Current + Previous 双密钥，方便密钥轮换过渡期。
    /// </summary>
    public static IServiceCollection AddObsidianJwtBearer(
        this IServiceCollection services,
        JwtOptions opts)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // 关闭默认的入站 claim type 映射，否则 "sub"/"scope" 会被改写成
                // ClaimTypes.NameIdentifier 之类的长 URI，下游 FindFirst("sub") 取不到。
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,

                    ValidateAudience = true,
                    ValidAudience = opts.Audience,

                    ValidateIssuerSigningKey = true,
                    // Current 必须有值；Previous 可选（密钥轮换过渡期）。
                    // ToList 物化一次，避免每次验签都重建 SymmetricSecurityKey。
                    IssuerSigningKeys = BuildSigningKeys(opts).ToList(),

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),

                    // scope claim 的 claim type 直接保持原样，User.FindAll("scope") 能取到。
                    NameClaimType = "sub",
                };
            });

        return services;
    }

    private static IEnumerable<SecurityKey> BuildSigningKeys(JwtOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.SigningKey.Current))
            throw new InvalidOperationException("Jwt:SigningKey:Current 未配置，服务无法启动。");

        yield return new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(opts.SigningKey.Current));

        if (!string.IsNullOrWhiteSpace(opts.SigningKey.Previous))
            yield return new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(opts.SigningKey.Previous));
    }
}
