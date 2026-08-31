using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using NLog;
using Shoko.Abstractions.Metadata.Anidb.Enums;
using Shoko.Abstractions.Metadata.Anidb.Models;
using Shoko.Abstractions.Metadata.Anidb.Services;

namespace ShokoMyListSyncPlus;

/// <summary>Holds the current status, metrics, and log outputs for the sync operation.</summary>
public class SyncState
{
    /// <summary>True if a sync is currently running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>True if writes to Shoko/AniDB are bypassed.</summary>
    public bool DryRun { get; set; }

    /// <summary>The total number of entries completely missing from the AniDB MyList.</summary>
    public int MissingCount { get; set; }

    /// <summary>The total number of entries present on MyList but out-of-sync with Shoko's watched/storage state.</summary>
    public int OutOfSyncCount { get; set; }

    /// <summary>The total number of entries watched on AniDB but unwatched locally (informational).</summary>
    public int AniDbWatchedLocalUnwatchedCount { get; set; }

    /// <summary>The number of entries evaluated during the sync plan.</summary>
    public int ProcessedEpisodes { get; set; }

    /// <summary>The number of entries successfully synced or queued to queue.</summary>
    public int EpisodesSynced { get; set; }

    /// <summary>The number of errors encountered during the sync.</summary>
    public int Errors { get; set; }

    /// <summary>The URL path to the generated report log, if available.</summary>
    public string? LastReportUrl { get; set; }

    /// <summary>A thread-safe queue of chronological log messages for the UI.</summary>
    public ConcurrentQueue<string> Logs { get; } = new();
}

/// <summary>Background worker responsible for orchestrating the native MyList sync and generating reports.</summary>
public class MyListSyncWorker(IMylistService mylistService)
{
    #region Setup & State

    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>The live state of the sync worker.</summary>
    public SyncState State { get; } = new();

    #endregion

    #region Public API

    /// <summary>Triggers Shoko's native IMylistService sync and generates a detailed report based on the executed actions or generated plan.</summary>
    /// <param name="dryRun">Whether to perform a dry run (plan-only).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the background sync operation.</returns>
    public async Task StartSyncAsync(bool dryRun, CancellationToken ct)
    {
        if (State.IsRunning)
            return;

        State.IsRunning = true;
        State.DryRun = dryRun;
        State.MissingCount = 0;
        State.OutOfSyncCount = 0;
        State.AniDbWatchedLocalUnwatchedCount = 0;
        State.ProcessedEpisodes = 0;
        State.EpisodesSynced = 0;
        State.Errors = 0;
        State.LastReportUrl = null;
        State.Logs.Clear();

        var reportDetails = new List<string>();
        var infoDetails = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        s_logger.Info("MyListSync: Starting native sync task (DryRun: {0})", dryRun);

        try
        {
            Log("Fetching AniDB MyList and calculating sync plan...");

            var options = new MylistSyncOptions { PlanOnly = dryRun };
            var result = await mylistService.SyncAsync(options, ct).ConfigureAwait(false);

            if (result == null)
            {
                s_logger.Warn("MyListSync: Sync failed to start. Another sync may be currently running in Shoko.");
                Log("Fatal Error: Sync failed to start. Another MyList sync is likely already running in Shoko.");
                return;
            }

            State.ProcessedEpisodes = result.TotalEntries;
            Log($"Scan complete. Evaluated {result.TotalEntries} entries against local database.");

            foreach (var action in result.Plan.Actions)
            {
                if (action.Kind == MylistSyncActionKind.AlreadyInDesiredState)
                    continue;

                string prefix;
                if (action.Kind == MylistSyncActionKind.ExportEntryAddition)
                {
                    State.MissingCount++;
                    prefix = "[MISSING]";
                }
                else if (action.Kind is MylistSyncActionKind.ExportWatchedState or MylistSyncActionKind.ExportEntryRemoval)
                {
                    State.OutOfSyncCount++;
                    prefix = "[OUT OF SYNC]";
                }
                else if (action.Kind == MylistSyncActionKind.ImportWatchedState)
                {
                    State.AniDbWatchedLocalUnwatchedCount++;
                    prefix = "[IMPORT]";
                }
                else
                {
                    prefix = "[UPDATE]";
                }

                string title = action.ShokoEpisode?.Series?.PreferredTitle?.Value ?? action.Entry?.AnimeID.ToString() ?? "Unknown";
                string desc = action.Description;

                if (action.Kind == MylistSyncActionKind.ImportWatchedState)
                    infoDetails.Add($"[{title}] {desc}");
                else
                    reportDetails.Add($"{prefix} [{title}] {desc}");
            }

            if (!dryRun)
            {
                State.EpisodesSynced = result.ModifiedEntries + result.FilesQueuedForAdd + result.EpisodesQueuedForAdd + result.EntriesQueuedForRemoval + result.EpisodesQueuedForRemoval;
                Log(
                    $"Sync completed successfully. Modified: {result.ModifiedEntries}, Added: {result.FilesQueuedForAdd + result.EpisodesQueuedForAdd}, Removed: {result.EntriesQueuedForRemoval + result.EpisodesQueuedForRemoval}"
                );
                s_logger.Info("MyListSync: Task completed successfully -> Synced {0} items.", State.EpisodesSynced);
            }
            else
            {
                Log($"Dry Run completed. Would modify {State.OutOfSyncCount}, add {State.MissingCount}, and import {State.AniDbWatchedLocalUnwatchedCount}.");
                s_logger.Info("MyListSync: Task completed successfully (Dry Run).");
            }
        }
        catch (Exception ex)
        {
            s_logger.Error(ex, "MyListSync: Fatal error encountered during execution");
            Log($"Fatal Error: {ex.Message}");
            State.Errors++;
        }
        finally
        {
            sw.Stop();
            GenerateReport(sw.Elapsed, reportDetails, infoDetails);
            State.IsRunning = false;
        }
    }

    #endregion

    #region Internal Helpers & Logging

    /// <summary>Pushes a message to the real-time log queue.</summary>
    /// <param name="message">The text to log.</param>
    private void Log(string message) => State.Logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");

    /// <summary>Builds and saves a formatted text report of the sync operation to the logs directory.</summary>
    /// <param name="elapsed">Total time elapsed during the task.</param>
    /// <param name="details">List of descriptive strings for each missing or out-of-sync entry.</param>
    /// <param name="infoDetails">List of descriptive strings for items watched on AniDB but unwatched locally.</param>
    private void GenerateReport(TimeSpan elapsed, List<string> details, List<string> infoDetails)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Shoko MyList Sync+ Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
            sb.AppendLine($"  Elapsed Time             : {elapsed.TotalSeconds:F2}s");
            sb.AppendLine($"  Mode                     : {(State.DryRun ? "Dry Run" : "Live")}");
            sb.AppendLine($"  Missing Items Found      : {State.MissingCount}");
            sb.AppendLine($"  Out-of-Sync Items        : {State.OutOfSyncCount}");
            sb.AppendLine($"  AniDB Watched / Unwatched: {State.AniDbWatchedLocalUnwatchedCount}");
            sb.AppendLine($"  Items Synced             : {State.EpisodesSynced}");
            sb.AppendLine($"  Errors                   : {State.Errors}");

            if (details.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Out-of-Sync & Missing Items Details:");
                foreach (var d in details.OrderBy(x => x))
                    sb.AppendLine($"  {d}");
            }

            if (infoDetails.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Items Watched on AniDB but Unwatched in Shoko (Informational):");
                foreach (var d in infoDetails.OrderBy(x => x))
                    sb.AppendLine($"  {d}");
            }

            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string logsDir = Path.Combine(pluginDir, "logs");
            Directory.CreateDirectory(logsDir);

            string filename = "mylist-sync-report.log";
            File.WriteAllText(Path.Combine(logsDir, filename), sb.ToString(), Encoding.UTF8);
            State.LastReportUrl = $"{ShokoMyListSyncPlusConstants.BasePath}/logs/{filename}";
        }
        catch (Exception ex)
        {
            s_logger.Warn(ex, "MyListSync: Failed to generate report log.");
        }
    }

    #endregion
}
