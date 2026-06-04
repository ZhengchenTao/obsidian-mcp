using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class WriteFileTool(
    VaultPathResolver resolver,
    AuditLogger audit,
    IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Overwrite a vault file with new content (requires write:obsidian scope). " +
        "Completely replaces the existing content. " +
        "Any vault-relative path is writable except paths whose segment is in Vault__Blacklist. " +
        "Use append_file to add content without overwriting.")]
    public async Task<WriteResult> WriteFile(
        [Description("Vault-relative path, e.g. 'Projects/logs/2026-05.md'")] string path,
        [Description("Full file content to write (UTF-8). Replaces existing content entirely.")] string content)
    {
        // scope 校验
        EnsureScope(ScopePolicies.WriteObsidian);

        var user = GetUser();
        var clientId = GetClientId();
        string? absPath = null;

        try
        {
            absPath = resolver.Resolve(path);

            // 确保父目录存在
            var dir = Path.GetDirectoryName(absPath)!;
            Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(absPath, content, System.Text.Encoding.UTF8);
            var written = System.Text.Encoding.UTF8.GetByteCount(content);

            audit.LogWrite(user, clientId, "write_file", path, written, ok: true);
            return new WriteResult { Ok = true, WrittenBytes = written };
        }
        catch (Exception ex)
        {
            audit.LogWrite(user, clientId, "write_file", path, 0, ok: false, error: ex.Message);
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
