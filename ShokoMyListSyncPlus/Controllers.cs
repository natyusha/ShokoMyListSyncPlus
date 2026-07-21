using System.Reflection;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace ShokoMyListSyncPlus;

/// <summary>Serves the single-page dashboard UI and its static assets.</summary>
[ApiController]
[ApiVersion("1")]
[Route("/api/plugin/ShokoMyListSyncPlus")]
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

/// <summary>Provides the API endpoints to manage the background sync process and retrieve logs.</summary>
[ApiController]
[ApiVersion("1")]
[Route("/api/plugin/ShokoMyListSyncPlus")]
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
    /// <returns>The log content as text/plain.</returns>
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

        return PhysicalFile(path, "text/plain");
    }

    /// <summary>Accepts an XML or TGZ payload, buffers it, and begins the background synchronization task.</summary>
    /// <param name="exportFile">The uploaded export file.</param>
    /// <param name="dryRun">Whether to run in Dry Run mode.</param>
    /// <param name="apiKey">The Shoko v3 API Key used to authenticate the file addition calls.</param>
    /// <returns>A status acknowledgment.</returns>
    [HttpPost("sync")]
    public async Task<IActionResult> StartSync([FromForm] IFormFile exportFile, [FromForm] bool dryRun, [FromForm] string apiKey)
    {
        if (worker.State.IsRunning)
        {
            s_logger.Warn("MyListSync: Rejected sync request -> A sync task is already running.");
            return BadRequest("Sync is already running.");
        }
        if (exportFile == null || exportFile.Length == 0)
        {
            s_logger.Warn("MyListSync: Rejected sync request -> No file provided.");
            return BadRequest("Export file is required.");
        }

        if (!exportFile.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !exportFile.FileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            s_logger.Warn("MyListSync: Rejected sync request -> Invalid file format '{0}'.", exportFile.FileName);
            return BadRequest("Export file must be an XML document or TGZ archive.");
        }

        if (!dryRun && string.IsNullOrWhiteSpace(apiKey))
        {
            s_logger.Warn("MyListSync: Rejected sync request -> API Key is required for live sync.");
            return BadRequest("API Key is required for live sync.");
        }

        s_logger.Info("MyListSync: Accepted export file '{0}' ({1} bytes) -> Triggering background worker...", exportFile.FileName, exportFile.Length);

        // Buffer the stream so it isn't disposed when the HTTP request ends
        var ms = new MemoryStream();
        await exportFile.CopyToAsync(ms).ConfigureAwait(false);
        ms.Position = 0;

        // Derive the server's local Base URL dynamically
        string baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var filename = exportFile.FileName;

        _ = Task.Run(() => worker.StartSyncAsync(ms, filename, dryRun, apiKey ?? "", baseUrl, default));

        return Ok("Sync started.");
    }
}
