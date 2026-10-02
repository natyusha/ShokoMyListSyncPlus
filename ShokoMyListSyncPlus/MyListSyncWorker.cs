using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using NLog;
using Shoko.Abstractions.Metadata.Anidb.Enums;
using Shoko.Abstractions.Metadata.Anidb.Models;
using Shoko.Abstractions.Metadata.Anidb.Services;
using Shoko.Abstractions.Metadata.Services;
using IOFile = System.IO.File;

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
public class MyListSyncWorker(IMylistService mylistService, IMetadataService metadataService)
{
    #region Setup & State

    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();
    private static readonly string s_logsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "logs");

    /// <summary>The live state of the sync worker.</summary>
    public SyncState State { get; } = new();

    #endregion

    #region Public API

    /// <summary>Starts the background sync task.</summary>
    /// <param name="dryRun">Whether to perform a dry run (plan-only).</param>
    /// <param name="import">Whether to pull watched states from AniDB to Shoko (true), or push from Shoko to AniDB (false).</param>
    /// <param name="updateStates">Whether to update the storage state of existing entries during export.</param>
    public void StartSync(bool dryRun, bool import, bool updateStates)
    {
        if (State.IsRunning)
            return;

        State.IsRunning = true;
        _ = Task.Run(() => RunSyncAsync(dryRun, import, updateStates, default));
    }

    /// <summary>Triggers Shoko's native IMylistService sync and generates a detailed report based on the executed actions or generated plan.</summary>
    private async Task RunSyncAsync(bool dryRun, bool import, bool updateStates, CancellationToken ct)
    {
        var reportDetails = new List<string>();
        var sw = Stopwatch.StartNew();

        try
        {
            State.DryRun = dryRun;
            State.MissingCount = 0;
            State.OutOfSyncCount = 0;
            State.ProcessedEpisodes = 0;
            State.EpisodesSynced = 0;
            State.Errors = 0;
            State.LastReportUrl = null;
            State.Logs.Clear();

            s_logger.Info("MyListSync: Starting native sync task (DryRun: {0}, Import: {1}, UpdateStates: {2})", dryRun, import, updateStates);
            Log("Fetching AniDB MyList and calculating sync plan...");

            var options = new MylistSyncOptions
            {
                PlanOnly = true,
                ReadWatched = import,
                ReadUnwatched = import,
                SetWatched = !import,
                SetUnwatched = !import,
                UpdateStates = !import && updateStates,
                Targets = MylistSyncTargets.Videos,
            };

            var result = await mylistService.SyncAsync(options, ct).ConfigureAwait(false);
            if (result == null)
            {
                s_logger.Warn("MyListSync: Sync failed to start. Another sync may be currently running in Shoko.");
                Log("Fatal Error: Sync failed to start. Another MyList sync is likely already running in Shoko.");
                return;
            }

            State.ProcessedEpisodes = result.TotalEntries;
            Log($"Scan complete. Evaluated {result.TotalEntries} entries against local database.");

            var targetDirection = import ? MylistSyncDirection.Import : MylistSyncDirection.Export;
            var relevantActions = result.Plan.Actions.Where(a => a.Direction == targetDirection).ToList();

            foreach (var action in relevantActions)
            {
                string prefix = action.Kind switch
                {
                    MylistSyncActionKind.ExportEntryAddition => "[ADD]",
                    MylistSyncActionKind.ExportWatchedState => "[EXPORT]",
                    MylistSyncActionKind.ImportWatchedState => "[IMPORT]",
                    MylistSyncActionKind.ExportEntryRemoval => "[REMOVE]",
                    _ => "[UPDATE]",
                };

                if (action.Kind == MylistSyncActionKind.ExportEntryAddition)
                    State.MissingCount++;
                else
                    State.OutOfSyncCount++;

                var ep =
                    action.ShokoEpisode
                    ?? action.Video?.CrossReferences?.FirstOrDefault(cr => cr.ShokoEpisode != null)?.ShokoEpisode
                    ?? (action.Entry?.EpisodeID > 0 ? metadataService.GetShokoEpisodeByAnidbID(action.Entry.EpisodeID) : null);
                var series = ep?.Series ?? action.Video?.Series?.FirstOrDefault() ?? (action.Entry?.AnimeID > 0 ? metadataService.GetShokoSeriesByAnidbID(action.Entry.AnimeID) : null);
                string? fileName = action.Video?.Files?.FirstOrDefault()?.Path is { Length: > 0 } filePath ? Path.GetFileName(filePath) : action.Video?.EarliestKnownName;
                string seriesTitle = series?.PreferredTitle?.Value ?? (!string.IsNullOrWhiteSpace(fileName) ? fileName : (action.Entry?.AnimeID > 0 ? $"AniDB: {action.Entry.AnimeID}" : "Unknown Series"));
                string epCoords = ep != null ? $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} " : (action.Entry?.EpisodeID > 0 ? $"EpID {action.Entry.EpisodeID} " : "");
                string fileInfo =
                    action.Entry?.FileID > 0 ? $"(File: {action.Entry.FileID})"
                    : action.Entry?.MylistID > 0 ? $"(MyList ID: {action.Entry.MylistID})"
                    : "";

                string detail = action.Kind switch
                {
                    MylistSyncActionKind.ExportEntryAddition =>
                        $"Add to MyList{(action.WatchedAt != null ? $" (Watched: {action.WatchedAt:yyyy-MM-dd})" : " (Unwatched)")}{(action.State != null ? $", State: {action.State}" : "")}",
                    MylistSyncActionKind.ExportWatchedState =>
                        $"Export watched state -> {(action.WatchedAt != null ? $"Watched ({action.WatchedAt:yyyy-MM-dd})" : "Unwatched")}{(action.State != null ? $", State: {action.State}" : "")}",
                    MylistSyncActionKind.ImportWatchedState => $"Import watched state -> {(action.WatchedAt != null ? $"Watched ({action.WatchedAt:yyyy-MM-dd})" : "Unwatched")}",
                    MylistSyncActionKind.ExportEntryRemoval => $"Remove from MyList (File not in library){(action.DeleteType != null ? $" [{action.DeleteType}]" : "")}",
                    _ => action.Description,
                };

                reportDetails.Add($"{prefix} [{seriesTitle}] {epCoords}{fileInfo} -> {detail}".Replace("  ", " "));
            }

            if (!dryRun && relevantActions.Count > 0)
            {
                var appliedResult = await mylistService.ApplySyncPlanAsync(new MylistSyncPlan { CreatedAt = result.Plan.CreatedAt, Actions = relevantActions }, ct).ConfigureAwait(false);
                if (appliedResult == null)
                {
                    s_logger.Warn("MyListSync: Failed to apply sync plan. Another sync may be currently running.");
                    Log("Fatal Error: Failed to apply sync plan. Another MyList sync is likely already running in Shoko.");
                    return;
                }

                State.EpisodesSynced =
                    appliedResult.ModifiedEntries + appliedResult.FilesQueuedForAdd + appliedResult.EpisodesQueuedForAdd + appliedResult.EntriesQueuedForRemoval + appliedResult.EpisodesQueuedForRemoval;
            }

            Log($"{(dryRun ? "Dry Run completed. Would process" : "Sync completed successfully. Processed")} {relevantActions.Count} actions (Direction: {(import ? "AniDB -> Shoko" : "AniDB <- Shoko")}).");
            s_logger.Info(dryRun ? "MyListSync: Task completed successfully (Dry Run)." : "MyListSync: Task completed successfully -> Synced {0} items.", State.EpisodesSynced);
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
            GenerateReport(sw.Elapsed, import, updateStates, reportDetails);
            State.IsRunning = false;
        }
    }

    #endregion

    #region Helpers & Logging

    /// <summary>Pushes a message to the real-time log queue.</summary>
    /// <param name="message">The text to log.</param>
    private void Log(string message) => State.Logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");

    /// <summary>Builds and saves a formatted text report of the sync operation to the logs directory.</summary>
    /// <param name="elapsed">Total time elapsed during the task.</param>
    /// <param name="import">Whether the direction of the sync was pulling from AniDB to Shoko.</param>
    /// <param name="updateStates">Whether storage state updates were enabled for the sync run.</param>
    /// <param name="details">List of descriptive strings for each missing or out-of-sync entry.</param>
    private void GenerateReport(TimeSpan elapsed, bool import, bool updateStates, List<string> details)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"Shoko MyList Sync+ Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{new string('-', 60)}\n\n  Elapsed Time             : {elapsed.TotalSeconds:F2}s\n  Mode                     : {(State.DryRun ? "Dry Run" : "Live")}\n  Direction                : {(import ? "AniDB -> Shoko" : "AniDB <- Shoko")}\n  Update Storage States    : {(import ? "N/A (Import Mode)" : (updateStates ? "Enabled" : "Disabled"))}\n  Missing Items Found      : {State.MissingCount}\n  Out-of-Sync Items        : {State.OutOfSyncCount}\n  Items Synced             : {State.EpisodesSynced}\n  Errors                   : {State.Errors}"
            );

            if (details.Count > 0)
            {
                sb.AppendLine("\nOut-of-Sync & Missing Items Details:");
                foreach (var d in details.OrderBy(x => x))
                    sb.AppendLine($"  {d}");
            }

            Directory.CreateDirectory(s_logsDir);
            IOFile.WriteAllText(Path.Combine(s_logsDir, "mylist-sync-report.log"), sb.ToString(), Encoding.UTF8);
            State.LastReportUrl = $"{ShokoMyListSyncPlusConstants.BasePath}/logs/mylist-sync-report.log";
        }
        catch (Exception ex)
        {
            s_logger.Warn(ex, "MyListSync: Failed to generate report log.");
        }
    }

    #endregion
}
