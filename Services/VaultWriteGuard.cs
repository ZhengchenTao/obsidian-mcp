using ObsidianMcp.Config;
using Microsoft.Extensions.Options;

namespace ObsidianMcp.Services;

/// <summary>
/// 写入门禁——在路径安全（VaultPathResolver）之上再加写入白名单控制。
///
/// 规则优先级（从高到低）：
///   1. 永禁写入：AGENTS.md / README.md / CLAUDE.md（任何路径下的同名文件）
///   2. 必须命中写入白名单之一才允许（由 Vault__WriteWhitelist__N 配置）
///
/// 白名单格式：
///   - 以 / 或 \ 结尾 → 前缀匹配（例如 "Notes/" 允许 Notes 目录及其子树）
///   - 不以斜杠结尾 → 精确路径匹配（例如 "todo.md"）
///
/// 默认白名单为空：未配置 Vault__WriteWhitelist__N 时所有写入都会被拒绝。
/// </summary>
public class VaultWriteGuard
{
    // 永禁写入的文件名（任意目录下的同名文件都禁写）。
    // 这几个是 agent / 仓库根常见的元信息文件，写坏会导致工具自身或下游 agent 行为异常。
    private static readonly HashSet<string> ForbiddenFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AGENTS.md",
            "README.md",
            "CLAUDE.md",
        };

    private readonly VaultPathResolver _resolver;
    private readonly string[] _writeWhitelist;

    public VaultWriteGuard(VaultPathResolver resolver, IOptions<VaultOptions> opts)
    {
        _resolver = resolver;
        _writeWhitelist = opts.Value.WriteWhitelist ?? [];
    }

    /// <summary>
    /// 校验相对路径是否允许写入。
    /// 通过则返回规范化后的绝对路径；不通过则抛 UnauthorizedAccessException。
    /// </summary>
    public string EnsureWritable(string relativePath)
    {
        // 先过路径安全守卫（防穿越 + 黑名单）
        var absPath = _resolver.Resolve(relativePath);

        // 规范化相对路径（用于白名单匹配），统一用 /
        var normalized = NormalizeRelative(relativePath);

        // 1. 永禁文件名
        var fileName = Path.GetFileName(absPath);
        if (ForbiddenFileNames.Contains(fileName))
            throw new UnauthorizedAccessException(
                $"禁止写入保护文件：{relativePath}");

        // 2. 写入白名单
        if (!IsInWhitelist(normalized))
            throw new UnauthorizedAccessException(
                $"路径不在写入白名单内：{relativePath}");

        return absPath;
    }

    private bool IsInWhitelist(string normalized)
    {
        foreach (var entry in _writeWhitelist)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var normalizedEntry = NormalizeRelative(entry);
            if (normalizedEntry.EndsWith('/'))
            {
                // 前缀匹配
                if (normalized.StartsWith(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // 精确匹配
                if (string.Equals(normalized, normalizedEntry, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>统一用 / 作分隔符，用于白名单匹配。</summary>
    private static string NormalizeRelative(string path) =>
        path.Replace('\\', '/');
}
