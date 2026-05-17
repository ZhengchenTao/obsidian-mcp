using Microsoft.Extensions.Options;
using ObsidianMcp.Config;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tests;

/// <summary>
/// VaultPathResolver 路径安全单测。
/// 核心场景：路径穿越、黑名单、绝对路径拒绝。
/// </summary>
public class VaultPathResolverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly VaultPathResolver _resolver;

    public VaultPathResolverTests()
    {
        // 每个测试类实例创建独立临时目录作 vault root
        _tempRoot = Path.Combine(Path.GetTempPath(), "obsidian-mcp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);

        // 在 tempRoot 下建一些测试文件/目录
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Notes"));
        File.WriteAllText(Path.Combine(_tempRoot, "Notes", "test.md"), "hello");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Projects"));

        var opts = Options.Create(new VaultOptions
        {
            Root = _tempRoot,
            Blacklist = ["custom-black"],
            WriteWhitelist = [],
        });
        _resolver = new VaultPathResolver(opts);
    }

    // ─── 正常路径 ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ValidRelativePath_ReturnsAbsolutePath()
    {
        var result = _resolver.Resolve("Notes/test.md");
        Assert.Equal(Path.Combine(_tempRoot, "Notes", "test.md"), result);
    }

    [Fact]
    public void Resolve_NestedPath_ReturnsCorrectAbsolutePath()
    {
        var result = _resolver.Resolve("Projects");
        Assert.Equal(Path.Combine(_tempRoot, "Projects"), result);
    }

    // ─── 路径穿越（Path Traversal） ──────────────────────────────────────────

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../Windows/System32")]
    [InlineData("Notes/../../../etc/shadow")]
    [InlineData("Notes/../../outside")]
    public void Resolve_PathTraversal_ThrowsUnauthorized(string path)
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve(path));
        Assert.Contains("路径穿越", ex.Message);
    }

    // ─── 绝对路径拒绝 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    public void Resolve_AbsolutePath_ThrowsUnauthorized(string path)
    {
        // 绝对路径判断：Path.IsPathRooted 在 Windows 上对 / 开头也返回 true
        var ex = Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve(path));
        Assert.Contains("绝对路径", ex.Message);
    }

    // ─── Hardcode 黑名单（.obsidian / .trash / .git） ────────────────────────

    [Theory]
    [InlineData(".obsidian/config")]
    [InlineData(".trash/deleted.md")]
    [InlineData(".git/config")]
    public void Resolve_HardcodeBlacklist_ThrowsUnauthorized(string path)
    {
        // 先确保这些目录/文件在 vault root 下存在（避免路径规范化误报）
        var abs = Path.Combine(_tempRoot, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, "test");

        var ex = Assert.Throws<UnauthorizedAccessException>(() => _resolver.Resolve(path));
        Assert.Contains("黑名单", ex.Message);
    }

    // ─── Env 扩展黑名单 ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CustomBlacklist_ThrowsUnauthorized()
    {
        var customDir = Path.Combine(_tempRoot, "custom-black");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, "test.md"), "test");

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => _resolver.Resolve("custom-black/test.md"));
        Assert.Contains("黑名单", ex.Message);
    }

    // ─── 空路径 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyOrWhitespace_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(path));
    }

    public void Dispose()
    {
        // 清理临时目录
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }
}
