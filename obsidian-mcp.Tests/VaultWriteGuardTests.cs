using Microsoft.Extensions.Options;
using ObsidianMcp.Config;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tests;

/// <summary>
/// VaultWriteGuard 单测。
/// 核心场景：保护文件名永禁写、白名单允许、非白名单拒绝、空白名单全拒、前缀 vs 精确匹配。
/// </summary>
public class VaultWriteGuardTests : IDisposable
{
    private readonly string _tempRoot;

    public VaultWriteGuardTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "obsidian-mcp-guard-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    private VaultWriteGuard MakeGuard(string[] whitelist)
    {
        var opts = Options.Create(new VaultOptions
        {
            Root = _tempRoot,
            Blacklist = [],
            WriteWhitelist = whitelist,
        });
        var resolver = new VaultPathResolver(opts);
        return new VaultWriteGuard(resolver, opts);
    }

    private void EnsureDirFor(string relPath)
    {
        var abs = Path.Combine(_tempRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
    }

    // ─── 白名单：前缀匹配 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Notes/foo.md")]
    [InlineData("Notes/sub/bar.md")]
    public void EnsureWritable_PrefixWhitelist_AllowsMatchingPaths(string path)
    {
        var guard = MakeGuard(["Notes/"]);
        EnsureDirFor(path);

        var result = guard.EnsureWritable(path);
        Assert.True(result.StartsWith(_tempRoot, StringComparison.OrdinalIgnoreCase));
    }

    // ─── 白名单：精确匹配 ────────────────────────────────────────────────────

    [Fact]
    public void EnsureWritable_ExactWhitelist_AllowsOnlyExactPath()
    {
        var guard = MakeGuard(["todo.md"]);
        EnsureDirFor("todo.md");

        var result = guard.EnsureWritable("todo.md");
        Assert.True(result.StartsWith(_tempRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureWritable_ExactWhitelist_RejectsSiblingPath()
    {
        var guard = MakeGuard(["todo.md"]);
        EnsureDirFor("other.md");

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => guard.EnsureWritable("other.md"));
        Assert.Contains("白名单", ex.Message);
    }

    // ─── 保护文件名永禁（即便父目录被白名单允许） ──────────────────────────

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("Notes/AGENTS.md")]
    [InlineData("Notes/README.md")]
    [InlineData("Notes/CLAUDE.md")]
    public void EnsureWritable_ProtectedFileName_ThrowsUnauthorized(string path)
    {
        var guard = MakeGuard(["Notes/", "AGENTS.md", "README.md", "CLAUDE.md"]);
        EnsureDirFor(path);

        var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureWritable(path));
        Assert.Contains("禁止写入", ex.Message);
    }

    // ─── 空白名单：拒绝一切写入 ──────────────────────────────────────────────

    [Theory]
    [InlineData("Notes/foo.md")]
    [InlineData("foo.md")]
    public void EnsureWritable_EmptyWhitelist_RejectsEverything(string path)
    {
        var guard = MakeGuard([]);
        EnsureDirFor(path);

        var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureWritable(path));
        Assert.Contains("白名单", ex.Message);
    }

    // ─── 非白名单路径拒绝 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Other/note.md")]
    [InlineData("random.md")]
    public void EnsureWritable_NonWhitelistedPath_ThrowsUnauthorized(string path)
    {
        var guard = MakeGuard(["Notes/"]);
        EnsureDirFor(path);

        var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureWritable(path));
        Assert.Contains("白名单", ex.Message);
    }

    // ─── 反斜杠归一化 ────────────────────────────────────────────────────────

    [Fact]
    public void EnsureWritable_WhitelistAcceptsBackslashEntries()
    {
        var guard = MakeGuard(["Notes\\"]);
        EnsureDirFor("Notes/foo.md");

        var result = guard.EnsureWritable("Notes/foo.md");
        Assert.True(result.StartsWith(_tempRoot, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }
}
