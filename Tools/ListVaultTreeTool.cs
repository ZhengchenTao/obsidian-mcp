using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class ListVaultTreeTool(VaultPathResolver resolver, IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Return a depth-limited directory tree of the entire Obsidian vault as JSON. " +
        "Use this first when you need an overview of the vault structure. " +
        "Each node has { name, type (file|directory), children? }. " +
        "Hidden directories (.obsidian, .trash, .git) and any path in Vault__Blacklist are excluded. " +
        "Prefer this over multiple list_files calls when you need the big picture.")]
    public object ListVaultTree(
        [Description("Maximum depth to traverse (default 3). Root is depth 0.")] int depth = 3)
    {
        ToolScopeGuard.EnsureScope(http, ScopePolicies.ReadObsidian);

        if (depth < 0) depth = 0;
        if (depth > 10) depth = 10; // 防止超大 vault 超时

        var root = resolver.VaultRoot;
        return BuildNode(root, root, depth);
    }

    private static object BuildNode(string path, string root, int remainingDepth)
    {
        var name = path == root ? "/" : Path.GetFileName(path);

        if (File.Exists(path))
        {
            return new { name, type = "file" };
        }

        if (!Directory.Exists(path))
            return new { name, type = "unknown" };

        if (remainingDepth == 0)
            return new { name, type = "directory" };

        // 枚举子项，排序：目录先、文件后，各自按名字排序
        List<object> children = [];

        try
        {
            var entries = Directory.GetFileSystemEntries(path)
                .OrderBy(e => File.Exists(e) ? 1 : 0)
                .ThenBy(e => Path.GetFileName(e), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var segName = Path.GetFileName(entry);
                // 跳过隐藏文件/目录（以 . 开头）；其他黑名单目录在尝试访问时由 VaultPathResolver 拦截
                if (segName.StartsWith('.')) continue;

                children.Add(BuildNode(entry, root, remainingDepth - 1));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 无权限目录跳过
        }

        return new { name, type = "directory", children };
    }
}
