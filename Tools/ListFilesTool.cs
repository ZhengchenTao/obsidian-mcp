using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class ListFilesTool(VaultPathResolver resolver, IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "List files and immediate subdirectories in a vault directory. " +
        "Returns a flat list of names (not full paths). " +
        "Use list_vault_tree for a recursive overview, or this tool to drill into one directory. " +
        "Hidden directories (.obsidian, .git, .trash) and any path configured in Vault__Blacklist are excluded.")]
    public List<string> ListFiles(
        [Description("Vault-relative path to list. Defaults to root if omitted. " +
                     "e.g. 'Notes', 'Projects/logs'")] string? path = null)
    {
        ToolScopeGuard.EnsureScope(http, ScopePolicies.ReadObsidian);

        string absDir;
        if (string.IsNullOrWhiteSpace(path))
        {
            absDir = resolver.VaultRoot;
        }
        else
        {
            absDir = resolver.Resolve(path);
        }

        if (!Directory.Exists(absDir))
            throw new DirectoryNotFoundException($"目录不存在：{path}");

        var result = new List<string>();

        // 子目录（排除隐藏目录；黑名单目录在尝试访问时由 VaultPathResolver 拦截）
        foreach (var dir in Directory.GetDirectories(absDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var seg = Path.GetFileName(dir)!;
            if (seg.StartsWith('.')) continue;
            result.Add(seg + "/");
        }

        // 文件
        foreach (var file in Directory.GetFiles(absDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(Path.GetFileName(file));
        }

        return result;
    }
}
