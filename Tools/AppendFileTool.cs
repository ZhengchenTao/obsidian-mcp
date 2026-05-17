using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class AppendFileTool(
    VaultWriteGuard guard,
    AuditLogger audit,
    IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Append text to the end of a vault file (requires write:obsidian scope). " +
        "Automatically prepends a newline if the file is non-empty and does not end with one. " +
        "Ideal for adding entries to a running log or todo file without touching existing content. " +
        "Same whitelist restrictions as write_file apply (path must be in Vault__WriteWhitelist).")]
    public async Task<WriteResult> AppendFile(
        [Description("Vault-relative path (must be in writable whitelist). " +
                     "e.g. 'Notes/todo.md', 'Projects/logs/2026-05.md'")] string path,
        [Description("Text to append (UTF-8). A newline is automatically inserted before this text " +
                     "if the file does not already end with one.")] string content)
    {
        // scope 校验
        EnsureScope(ScopePolicies.WriteObsidian);

        var user = GetUser();
        var clientId = GetClientId();

        try
        {
            var absPath = guard.EnsureWritable(path);

            // 确保父目录存在
            var dir = Path.GetDirectoryName(absPath)!;
            Directory.CreateDirectory(dir);

            // 如果文件已存在且末尾没有换行，先补一个
            string prefix = string.Empty;
            if (File.Exists(absPath))
            {
                var existing = await File.ReadAllTextAsync(absPath);
                if (existing.Length > 0 && !existing.EndsWith('\n'))
                    prefix = Environment.NewLine;
            }

            await File.AppendAllTextAsync(absPath, prefix + content, System.Text.Encoding.UTF8);
            var written = System.Text.Encoding.UTF8.GetByteCount(prefix + content);

            audit.LogWrite(user, clientId, "append_file", path, written, ok: true);
            return new WriteResult { Ok = true, WrittenBytes = written };
        }
        catch (Exception ex)
        {
            audit.LogWrite(user, clientId, "append_file", path, 0, ok: false, error: ex.Message);
            throw;
        }
    }

    private void EnsureScope(string requiredScope)
    {
        // OAuth 2.0 (RFC 6749) 规定 scope 大小写敏感，按 Ordinal 比对。
        ToolScopeGuard.EnsureScope(http, requiredScope);
    }

    private string GetUser() =>
        http.HttpContext?.User?.FindFirst("sub")?.Value ?? "unknown";

    private string GetClientId() =>
        http.HttpContext?.User?.FindFirst("client_id")?.Value ?? "unknown";
}

/// <summary>写入操作的返回值。</summary>
public class WriteResult
{
    public bool Ok { get; set; }
    public int WrittenBytes { get; set; }
}
