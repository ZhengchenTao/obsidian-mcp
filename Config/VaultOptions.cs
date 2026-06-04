namespace ObsidianMcp.Config;

/// <summary>
/// Vault 根目录与路径安全配置。
/// 环境变量前缀 Vault__，例如 Vault__Root=/vault
/// </summary>
public class VaultOptions
{
    public const string Section = "Vault";

    /// <summary>Vault 根目录的绝对路径，容器内默认 /vault</summary>
    public string Root { get; set; } = "/vault";

    /// <summary>额外黑名单路径段（与 hardcode 合并），env: Vault__Blacklist__0, __1...</summary>
    public string[] Blacklist { get; set; } = [];
}
