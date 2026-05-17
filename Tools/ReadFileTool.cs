using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class ReadFileTool(VaultPathResolver resolver, IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Read the full content of a vault file (UTF-8). " +
        "This is the primary tool for reading notes, design docs, logs, and config files. " +
        "Use get_metadata first if you want to check the file size before reading. " +
        "For very large files (>100 KB), use the offset and limit parameters (in bytes) " +
        "to read specific byte ranges and avoid context window overflow. " +
        "Returns the raw Markdown text including frontmatter.")]
    public async Task<string> ReadFile(
        [Description("Vault-relative path to the file. " +
                     "e.g. 'Notes/index.md', 'README.md', 'Projects/logs/2026-05.md'")] string path,
        [Description("Byte offset to start reading from (0-based). Omit to read from the beginning.")] long? offset = null,
        [Description("Number of bytes to read. Omit to read the entire file.")] long? limit = null)
    {
        ToolScopeGuard.EnsureScope(http, ScopePolicies.ReadObsidian);

        var absPath = resolver.Resolve(path);

        if (!File.Exists(absPath))
            throw new FileNotFoundException($"文件不存在：{path}");

        // 无分页时直接读全文
        if (offset is null && limit is null)
            return await File.ReadAllTextAsync(absPath);

        // 有分页参数时按字节切片
        await using var fs = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        long startByte = offset ?? 0;
        if (startByte < 0) startByte = 0;
        if (startByte >= fs.Length)
            return string.Empty;

        fs.Seek(startByte, SeekOrigin.Begin);

        long bytesToRead = limit.HasValue
            ? Math.Min(limit.Value, fs.Length - startByte)
            : fs.Length - startByte;

        var buffer = new byte[bytesToRead];
        var read = await fs.ReadAsync(buffer.AsMemory(0, (int)bytesToRead));

        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }
}
