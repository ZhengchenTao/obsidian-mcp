using System.Text.Json;

namespace ObsidianMcp.Services;

/// <summary>
/// 写操作审计日志（JSON lines 格式，按天 rotate）。
/// 输出到 /app/logs/audit-YYYY-MM-DD.log。
/// 注册为 Singleton，内部用 lock 保证多线程写入安全。
/// </summary>
public class AuditLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public AuditLogger(IConfiguration config)
    {
        // 允许通过配置覆盖日志目录，默认 /app/logs
        _logDir = config["AuditLog:Directory"] ?? "/app/logs";
        Directory.CreateDirectory(_logDir);
    }

    /// <summary>
    /// 记录一次写操作审计条目。
    /// </summary>
    public void LogWrite(
        string user,
        string clientId,
        string tool,
        string path,
        long bytes,
        bool ok,
        string? error = null)
    {
        var entry = new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            user,
            tool,
            path,
            bytes,
            client_id = clientId,
            ok,
            error,
        };

        var line = JsonSerializer.Serialize(entry);
        var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.log";
        var filePath = Path.Combine(_logDir, fileName);

        lock (_lock)
        {
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }
}
