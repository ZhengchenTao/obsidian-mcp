using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class GetMetadataTool(VaultPathResolver resolver, IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Get metadata for a vault file without reading its content. " +
        "Returns size in bytes, last modified timestamp (UTC ISO-8601), " +
        "and whether the file starts with YAML frontmatter (--- ... ---). " +
        "Useful for deciding whether to read a file (staleness check, size guard before read_file).")]
    public FileMetadata GetMetadata(
        [Description("Vault-relative path to the file. " +
                     "e.g. 'Notes/index.md'")] string path)
    {
        ToolScopeGuard.EnsureScope(http, ScopePolicies.ReadObsidian);

        var absPath = resolver.Resolve(path);

        if (!File.Exists(absPath))
            throw new FileNotFoundException($"文件不存在：{path}");

        var fi = new FileInfo(absPath);

        // 简单判断是否有 frontmatter：读前 4 字节检查是否以 --- 开头
        bool hasFrontmatter = false;
        try
        {
            using var fs = File.OpenRead(absPath);
            var header = new byte[4];
            if (fs.Read(header, 0, 4) == 4)
                hasFrontmatter = header[0] == '-' && header[1] == '-' && header[2] == '-';
        }
        catch
        {
            // 读取失败不影响其他字段
        }

        return new FileMetadata
        {
            Path = path,
            SizeBytes = fi.Length,
            ModifiedAt = fi.LastWriteTimeUtc.ToString("O"),
            HasFrontmatter = hasFrontmatter,
        };
    }
}

public class FileMetadata
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ModifiedAt { get; set; } = "";
    public bool HasFrontmatter { get; set; }
}
