using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace ObsidianMcp.Services;

/// <summary>
/// Vault 全文搜索服务。
/// 纯 C# 实现，大小写不敏感子串匹配（不支持 regex）。
/// V3 可替换为 ripgrep 调用。
/// </summary>
public class VaultSearchService
{
    private readonly VaultPathResolver _resolver;

    public VaultSearchService(VaultPathResolver resolver)
    {
        _resolver = resolver;
    }

    /// <param name="query">大小写不敏感的子串</param>
    /// <param name="glob">glob 过滤，例如 "Notes/**/*.md"，为 null 时搜全 vault</param>
    /// <param name="limit">最多返回条数</param>
    /// <param name="ct">CancellationToken</param>
    public async Task<List<SearchHit>> SearchAsync(
        string query,
        string? glob,
        int limit,
        CancellationToken ct = default)
    {
        var root = _resolver.VaultRoot;
        var files = GetFilesToSearch(root, glob);

        var hits = new List<SearchHit>();
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            if (hits.Count >= limit) break;

            await SearchFileAsync(file, root, query, limit, hits, ct);
        }

        return hits;
    }

    private static IEnumerable<string> GetFilesToSearch(string root, string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            // 全 vault 搜索，只搜 .md 文件（.json/.yaml 通常不需要全文检索）
            return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories);
        }

        // 用 Microsoft.Extensions.FileSystemGlobbing 做 glob 过滤
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(glob);
        var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(root));
        var result = matcher.Execute(dirInfo);
        return result.Files.Select(f => Path.Combine(root, f.Path));
    }

    private static async Task SearchFileAsync(
        string filePath,
        string root,
        string query,
        int limit,
        List<SearchHit> hits,
        CancellationToken ct)
    {
        // 跳过过大的文件（>5MB），避免 OOM
        var fi = new FileInfo(filePath);
        if (!fi.Exists || fi.Length > 5 * 1024 * 1024) return;

        try
        {
            int lineNumber = 0;
            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                lineNumber++;
                if (hits.Count >= limit) break;

                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SearchHit
                    {
                        File = Path.GetRelativePath(root, filePath).Replace('\\', '/'),
                        Line = lineNumber,
                        Preview = line.Length > 200 ? line[..200] + "..." : line,
                    });
                }
            }
        }
        catch (IOException)
        {
            // 文件读取失败（权限、锁定等），跳过不影响其他结果
        }
    }
}

public class SearchHit
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Preview { get; set; } = "";
}
