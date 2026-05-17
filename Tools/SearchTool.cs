using System.ComponentModel;
using ModelContextProtocol.Server;
using ObsidianMcp.Auth;
using ObsidianMcp.Services;

namespace ObsidianMcp.Tools;

[McpServerToolType]
public class SearchTool(VaultSearchService searchSvc, IHttpContextAccessor http)
{
    [McpServerTool]
    [Description(
        "Full-text search the Obsidian vault using case-insensitive literal substring match. " +
        "Does NOT support regex or wildcards in the query string. " +
        "Returns up to 50 hits by default, each with file path, line number, and a preview snippet. " +
        "Use the glob parameter to narrow the search to specific directories or file patterns. " +
        "For exact file reading use read_file instead. " +
        "Examples: search for 'docker compose' across all files, or 'TODO' within 'Projects/**/*.md'.")]
    public async Task<List<SearchHit>> Search(
        [Description("Literal substring to search for (case-insensitive). " +
                     "e.g. 'docker compose', 'TODO', 'API_KEY'")] string query,
        [Description("Optional glob pattern to narrow files. " +
                     "e.g. 'Notes/**/*.md', 'Projects/**', '*.md'. " +
                     "Omit to search all .md files.")] string? glob = null,
        [Description("Maximum number of results to return (default 50, max 200).")] int limit = 50)
    {
        ToolScopeGuard.EnsureScope(http, ScopePolicies.ReadObsidian);

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query 不能为空。");

        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        return await searchSvc.SearchAsync(query, glob, limit);
    }
}
