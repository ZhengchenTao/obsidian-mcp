using ObsidianMcp.Config;
using Microsoft.Extensions.Options;

namespace ObsidianMcp.Services;

/// <summary>
/// Vault 路径安全守卫（chroot 语义）。
///
/// 职责：
///   - 把相对路径拼接到 VaultRoot，防止路径穿越（../）
///   - 拒绝绝对路径输入
///   - 拒绝命中黑名单的路径段
///
/// 线程安全，注册为 Singleton。
/// </summary>
public class VaultPathResolver
{
    // hardcode 黑名单路径段（任意路径段命中即拒）。
    // 这几个是 Obsidian / Git 的内部目录，访问它们既无意义也容易踩坑（例如读取 .obsidian
    // 配置可能泄露插件 secret）。用户可通过 Vault__Blacklist__N 追加自己的敏感目录。
    private static readonly HashSet<string> HardcodeBlacklist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".obsidian",
            ".trash",
            ".git",
        };

    private readonly string _root;
    private readonly HashSet<string> _blacklist;

    public VaultPathResolver(IOptions<VaultOptions> opts)
    {
        var o = opts.Value;
        _root = Path.GetFullPath(o.Root);

        // 合并 hardcode + env 配置的黑名单，去重
        _blacklist = new HashSet<string>(HardcodeBlacklist, StringComparer.OrdinalIgnoreCase);
        foreach (var b in o.Blacklist)
            if (!string.IsNullOrWhiteSpace(b))
                _blacklist.Add(b.Trim());
    }

    /// <summary>返回 vault 根目录的绝对路径（规范化后）。</summary>
    public string VaultRoot => _root;

    /// <summary>
    /// 将相对路径解析为 vault 内的绝对路径。
    ///
    /// 可能抛出：
    ///   UnauthorizedAccessException — 路径穿越、绝对路径、命中黑名单、目标是 symlink
    ///   ArgumentException — relativePath 为空
    /// </summary>
    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("路径不能为空。", nameof(relativePath));

        // 拒绝绝对路径输入（防止容器外访问；包括 Linux /etc/... 与 Windows C:\... / UNC \\server）
        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException(
                $"拒绝绝对路径输入：{relativePath}");

        // 把 Windows 反斜杠归一化成 Unix 分隔符，避免 Linux 容器上把 "..\\.." 当成单段不消解。
        // 注意：仅对相对路径输入做归一化；root 路径已经由 Path.GetFullPath 处理过。
        var normalizedRel = relativePath.Replace('\\', '/');

        // 拼接并规范化（自动消解 .. 和 .）
        var target = Path.GetFullPath(Path.Combine(_root, normalizedRel));

        // 确认解析后的路径仍在 vault root 内
        if (!IsUnderRoot(target))
            throw new UnauthorizedAccessException(
                $"路径穿越 vault 根目录：{relativePath}");

        // 逐段检查黑名单
        CheckBlacklist(target, relativePath);

        // 拒绝 symlink（无论指向 vault 内外，统一禁；vault 真实内容应是普通文件 / 目录）。
        // 这是兜底防线：万一 WebDAV / 操作失误把 symlink 落到 vault 里，避免 Tool 跟随到容器外。
        RejectSymlink(target, relativePath);

        return target;
    }

    /// <summary>
    /// 检查路径自身（以及任一父级路径段）是否是 symlink。是 → 拒绝。
    /// 防御链外文件 leak（例如有人在 vault 里建一个指向 /etc/passwd 的软链）。
    /// </summary>
    private void RejectSymlink(string absPath, string original)
    {
        // 从 absPath 一直向上检查到 _root（不含 root 本体；root 是已知信任的挂载点）
        var current = absPath;
        while (current != null && current.Length > _root.Length)
        {
            try
            {
                var info = new FileInfo(current);
                if (info.Exists && info.LinkTarget != null)
                    throw new UnauthorizedAccessException($"拒绝 symlink 路径：{original}");
                if (!info.Exists)
                {
                    var di = new DirectoryInfo(current);
                    if (di.Exists && di.LinkTarget != null)
                        throw new UnauthorizedAccessException($"拒绝 symlink 路径：{original}");
                }
            }
            catch (UnauthorizedAccessException) { throw; }
            catch
            {
                // I/O 异常不在这里阻断；后续真正读文件时会自然抛
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current) break;
            current = parent;
        }
    }

    /// <summary>检查绝对路径是否在 vault root 下（含等于 root）。</summary>
    private bool IsUnderRoot(string absPath)
    {
        return absPath == _root
            || absPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>逐个路径段检查黑名单。</summary>
    private void CheckBlacklist(string absPath, string original)
    {
        // 把 absPath 中 root 之后的部分按分隔符拆分，逐段比对
        var relative = absPath[_root.Length..].TrimStart(Path.DirectorySeparatorChar);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var seg in segments)
        {
            if (_blacklist.Contains(seg))
                throw new UnauthorizedAccessException(
                    $"路径命中黑名单段 '{seg}'：{original}");
        }
    }
}
