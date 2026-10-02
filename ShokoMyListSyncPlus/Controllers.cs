using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NLog;
using IOFile = System.IO.File;

namespace ShokoMyListSyncPlus;

#region Dashboard

/// <summary>Serves the single-page dashboard UI and its static assets.</summary>
[ApiController]
[ApiVersion(ShokoMyListSyncPlusConstants.ApiVersion)]
[Route(ShokoMyListSyncPlusConstants.BasePath)]
public class DashboardController : ControllerBase
{
    private static readonly string s_dashboardDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "dashboard");

    /// <summary>Serves the settings dashboard page.</summary>
    /// <returns>The dashboard HTML content.</returns>
    [HttpGet("dashboard")]
    public IActionResult GetDashboard() => ServePage("dashboard.cshtml");

    /// <summary>Serves a razor template page from the dashboard directory, processing constants and injecting the base path.</summary>
    /// <param name="fileName">The filename of the razor template to serve.</param>
    /// <returns>An HTML content result or NotFound if the template does not exist.</returns>
    private IActionResult ServePage(string fileName)
    {
        string requested = Path.GetFullPath(Path.Combine(s_dashboardDir, fileName));

        if (!requested.StartsWith(s_dashboardDir, StringComparison.OrdinalIgnoreCase) || !IOFile.Exists(requested))
            return NotFound();

        var html = IOFile.ReadAllText(requested);
        var fields = typeof(ShokoMyListSyncPlusConstants).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Where(f => f.IsLiteral && !f.IsInitOnly);

        foreach (var field in fields)
            html = Regex.Replace(html, $@"\{{\{{\s?{field.Name}\s?\}}}}", field.GetValue(null)?.ToString() ?? "");

        if (html.IndexOf("<base", StringComparison.OrdinalIgnoreCase) < 0)
            html = html.Replace("<head>", $"<head>\n    <base href=\"{WebUtility.HtmlEncode($"{Request.PathBase}{ShokoMyListSyncPlusConstants.BasePath}/dashboard/")}\">", StringComparison.OrdinalIgnoreCase);

        return Content(html, "text/html");
    }

    /// <summary>Serves static assets (JS, CSS, SVG, ICO) from the dashboard folder.</summary>
    /// <param name="path">The relative asset path.</param>
    /// <returns>Physical file content with correct MIME type.</returns>
    [HttpGet("dashboard/{*path}")]
    public IActionResult GetAssetFile([FromRoute] string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        string requested = Path.GetFullPath(Path.Combine(s_dashboardDir, path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));

        return !requested.StartsWith(s_dashboardDir, StringComparison.OrdinalIgnoreCase) || !IOFile.Exists(requested) || requested.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
            ? NotFound()
            : PhysicalFile(
                requested,
                Path.GetExtension(requested).ToLowerInvariant() switch
                {
                    ".css" => "text/css",
                    ".js" => "application/javascript",
                    ".svg" => "image/svg+xml",
                    ".ico" => "image/x-icon",
                    _ => "application/octet-stream",
                }
            );
    }
}

#endregion

#region Sync Operations

/// <summary>Provides the API endpoints to manage the background sync process and retrieve logs.</summary>
[ApiController]
[ApiVersion(ShokoMyListSyncPlusConstants.ApiVersion)]
[Route(ShokoMyListSyncPlusConstants.BasePath)]
public class MyListSyncController(MyListSyncWorker worker) : ControllerBase
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();
    private static readonly string s_logsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "logs");

    /// <summary>Retrieves the current status, consumes any pending logs, and returns the report URL if complete.</summary>
    /// <returns>A JSON object containing current sync metrics and logs.</returns>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var logs = new List<string>();
        while (worker.State.Logs.TryDequeue(out var log))
            logs.Add(log);

        return Ok(
            new
            {
                worker.State.IsRunning,
                worker.State.DryRun,
                worker.State.MissingCount,
                worker.State.OutOfSyncCount,
                worker.State.ProcessedEpisodes,
                worker.State.EpisodesSynced,
                worker.State.Errors,
                worker.State.LastReportUrl,
                Logs = logs,
            }
        );
    }

    /// <summary>Serves report files from the plugin's logs directory.</summary>
    /// <param name="fileName">The log filename.</param>
    /// <returns>The log content as text/plain; charset=utf-8.</returns>
    [HttpGet("logs/{fileName}")]
    public IActionResult GetLog(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest("fileName is required");

        var path = Path.Combine(s_logsDir, fileName);

        if (!IOFile.Exists(path))
            return NotFound("Log not found");

        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return PhysicalFile(path, "text/plain; charset=utf-8");
    }

    /// <summary>Begins the background synchronization task natively through Shoko.</summary>
    /// <param name="dryRun">Whether to run in Dry Run (Plan-Only) mode.</param>
    /// <param name="import">Whether to pull watched states from AniDB (import) or push local states (export).</param>
    /// <param name="updateStates">Whether to update existing MyList entries with the configured storage state.</param>
    /// <returns>A status acknowledgment.</returns>
    [HttpPost("sync")]
    public IActionResult StartSync([FromQuery] bool dryRun = true, [FromQuery] bool import = false, [FromQuery] bool updateStates = true)
    {
        if (worker.State.IsRunning)
        {
            s_logger.Warn("MyListSync: Rejected sync request -> A sync task is already running.");
            return BadRequest("Sync is already running.");
        }

        s_logger.Info("MyListSync: Accepted sync request (DryRun: {0}, Import: {1}, UpdateStates: {2}) -> Triggering background worker...", dryRun, import, updateStates);
        worker.StartSync(dryRun, import, updateStates);

        return Ok("Sync started.");
    }
}

#endregion
