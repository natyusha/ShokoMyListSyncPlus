using System.Reflection;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace ShokoMyListSyncPlus;

#region Dashboard

/// <summary>Serves the single-page dashboard UI and its static assets.</summary>
[ApiController]
[ApiVersion(ShokoMyListSyncPlusConstants.ApiVersion)]
[Route(ShokoMyListSyncPlusConstants.BasePath)]
public class DashboardController : ControllerBase
{
    /// <summary>Returns the physical CSHTML dashboard.</summary>
    /// <returns>HTML content.</returns>
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        string path = Path.Combine(pluginDir, "dashboard", "dashboard.cshtml");

        return !System.IO.File.Exists(path) ? NotFound() : PhysicalFile(path, "text/html");
    }

    /// <summary>Serves static assets (JS, CSS, SVG, ICO) from the dashboard folder.</summary>
    /// <param name="path">The relative asset path.</param>
    /// <returns>Physical file content with correct MIME type.</returns>
    [HttpGet("dashboard/{*path}")]
    public IActionResult GetAssetFile([FromRoute] string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        string dashboardDir = Path.Combine(pluginDir, "dashboard");
        string safePath = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string requested = Path.GetFullPath(Path.Combine(dashboardDir, safePath));

        if (!requested.StartsWith(Path.GetFullPath(dashboardDir), StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(requested) || requested.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        string ext = Path.GetExtension(requested).ToLowerInvariant();
        string contentType = ext switch
        {
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };

        return PhysicalFile(requested, contentType);
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

    /// <summary>Retrieves the current status, consumes any pending logs, and returns the report URL if complete.</summary>
    /// <returns>A JSON object containing current sync metrics and logs.</returns>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var logs = worker.State.Logs.ToList();
        worker.State.Logs.Clear();
        return Ok(
            new
            {
                worker.State.IsRunning,
                worker.State.DryRun,
                worker.State.MissingCount,
                worker.State.OutOfSyncCount,
                worker.State.AniDbWatchedLocalUnwatchedCount,
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
        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        var path = Path.Combine(pluginDir, "logs", fileName);

        if (!System.IO.File.Exists(path))
            return NotFound("Log not found");

        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return PhysicalFile(path, "text/plain; charset=utf-8");
    }

    /// <summary>Begins the background synchronization task natively through Shoko.</summary>
    /// <param name="dryRun">Whether to run in Dry Run (Plan-Only) mode.</param>
    /// <param name="import">Whether to pull watched states from AniDB (import) or push local states (export).</param>
    /// <returns>A status acknowledgment.</returns>
    [HttpPost("sync")]
    public IActionResult StartSync([FromQuery] bool dryRun = true, [FromQuery] bool import = false)
    {
        if (worker.State.IsRunning)
        {
            s_logger.Warn("MyListSync: Rejected sync request -> A sync task is already running.");
            return BadRequest("Sync is already running.");
        }

        s_logger.Info("MyListSync: Accepted sync request (DryRun: {0}, Import: {1}) -> Triggering background worker...", dryRun, import);
        _ = Task.Run(() => worker.StartSyncAsync(dryRun, import, default));

        return Ok("Sync started.");
    }
}

#endregion
