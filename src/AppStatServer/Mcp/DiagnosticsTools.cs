using System.ComponentModel;
using AppStatServer.Data;
using AppStatServer.Sentry;
using ModelContextProtocol.Server;

namespace AppStatServer.Mcp;

// MCP tools exposed over the bearer-protected /mcp endpoint. They read straight from the
// same IEventStorage the dashboard uses, so an agent (e.g. Claude Code working in the
// AppStatServer repo) can pull the live crashes/errors and fix them, then mark them resolved.
[McpServerToolType]
public class DiagnosticsTools(IEventStorage storage)
{
    [McpServerTool(Name = "list_diagnostics")]
    [Description(
        "List crash and handled-error signatures collected from the app, ranked by how many " +
        "times they occurred. Each item is one collapsed signature with a stable 'key' that " +
        "get_issue and resolve_issue accept. Use this to see what is currently broken.")]
    public async Task<object> ListDiagnostics(
        [Description("Rolling window in days to look back over (1-90). Default 14.")]
        int days = 14,
        [Description("Only signatures from this app version (release). Null = all versions.")]
        string? release = null,
        [Description("Filter by kind: 'crash' (unhandled, app terminated) or 'error' (handled). Null = both.")]
        string? kind = null,
        [Description("Include already-resolved signatures too. Default false (only open issues).")]
        bool includeResolved = false,
        [Description("Max signatures to return. Default 50.")]
        int limit = 50)
    {
        days = Math.Clamp(days, 1, 90);
        limit = Math.Clamp(limit, 1, 300);
        var report = await storage.GetDiagnosticsAsync(days, NullIfBlank(release));

        var groups = report.Groups.AsEnumerable();
        if (!includeResolved)
            groups = groups.Where(g => !g.Resolved);
        if (!string.IsNullOrWhiteSpace(kind))
            groups = groups.Where(g => string.Equals(g.Kind, kind, StringComparison.OrdinalIgnoreCase));

        var issues = groups
            .OrderByDescending(g => g.Count)
            .Take(limit)
            .Select(Summarize)
            .ToList();

        return new
        {
            window = new { days = report.Days, release = report.Release },
            totals = new
            {
                crashes = report.TotalCrashes,
                errors = report.TotalErrors,
                affectedUsers = report.AffectedUsers,
                openGroups = report.OpenGroups,
            },
            returned = issues.Count,
            issues,
        };
    }

    [McpServerTool(Name = "get_issue")]
    [Description(
        "Get the full detail of a single crash/error signature by its 'key', including the " +
        "stack trace of the most recent occurrence plus OS, device and release. Use this to " +
        "locate and fix the offending code.")]
    public async Task<object?> GetIssue(
        [Description("The signature key from list_diagnostics (e.g. 'crash|NullReferenceException: ...').")]
        string key,
        [Description("Rolling window in days to search for the signature (1-90). Default 90.")]
        int days = 90,
        [Description("Narrow the search to a single app version (release). Null = all versions.")]
        string? release = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new { error = "key is required" };

        days = Math.Clamp(days, 1, 90);
        var report = await storage.GetDiagnosticsAsync(days, NullIfBlank(release));
        var group = report.Groups.FirstOrDefault(g => g.Key == key);
        if (group is null)
            return new { error = $"No signature '{key}' seen in the last {days} day(s)." };

        var s = group.Sample;
        // App-attached context (exception_chain, app_context, last_command, …). On trimmed/AOT
        // builds the exception carries no frames, so these extras — plus the stack_trace_text
        // now folded into StackTrace — are often the only way to locate the offending code.
        var context = EnvelopeParser.ExtractExtras(s.EventEntry);
        return new
        {
            key = group.Key,
            kind = group.Kind,
            title = group.Title,
            level = group.Level,
            count = group.Count,
            users = group.Users,
            release = group.Release,
            firstSeen = group.FirstSeen,
            lastSeen = group.LastSeen,
            resolved = group.Resolved,
            resolvedAt = group.ResolvedAt,
            latestOccurrence = new
            {
                timestamp = s.Timestamp,
                message = s.Message,
                level = s.Level,
                os = s.Os,
                device = s.DeviceModel,
                release = s.Release,
                userId = s.UserId,
                traceId = s.TraceId,
                spanId = s.SpanId,
                stackTrace = s.StackTrace,
                context = context.Count > 0 ? context : null,
            },
        };
    }

    [McpServerTool(Name = "resolve_issue")]
    [Description(
        "Mark a crash/error signature resolved (or reopen it) by its 'key'. A resolved issue " +
        "automatically reopens if the same signature occurs again after this point. Call this " +
        "after you have shipped a fix.")]
    public async Task<object> ResolveIssue(
        [Description("The signature key from list_diagnostics.")]
        string key,
        [Description("True to mark resolved, false to reopen. Default true.")]
        bool resolved = true)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new { error = "key is required" };

        var newState = await storage.SetResolutionAsync(key, resolved);
        return new { key, resolved = newState };
    }

    private static object Summarize(DiagnosticGroup g) => new
    {
        key = g.Key,
        kind = g.Kind,
        title = g.Title,
        level = g.Level,
        count = g.Count,
        users = g.Users,
        release = g.Release,
        firstSeen = g.FirstSeen,
        lastSeen = g.LastSeen,
        resolved = g.Resolved,
    };

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
