using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml;
using NLog;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Abstractions.User.Enums;
using Shoko.Abstractions.User.Services;

namespace ShokoMyListSyncPlus;

/// <summary>Holds the current status, metrics, and log outputs for the sync operation.</summary>
public class SyncState
{
    /// <summary>True if a sync is currently running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>True if writes to Shoko/AniDB are bypassed.</summary>
    public bool DryRun { get; set; }

    /// <summary>The total number of episodes completely missing from the AniDB MyList.</summary>
    public int MissingCount { get; set; }

    /// <summary>The total number of episodes present on MyList but out-of-sync with Shoko's watched state.</summary>
    public int OutOfSyncCount { get; set; }

    /// <summary>The number of episodes evaluated so far.</summary>
    public int ProcessedEpisodes { get; set; }

    /// <summary>The number of episodes successfully queued for MyList sync.</summary>
    public int EpisodesSynced { get; set; }

    /// <summary>The number of errors encountered during the sync.</summary>
    public int Errors { get; set; }

    /// <summary>The URL path to the generated report log, if available.</summary>
    public string? LastReportUrl { get; set; }

    /// <summary>A thread-safe queue of chronological log messages for the UI.</summary>
    public ConcurrentQueue<string> Logs { get; set; } = new();
}

/// <summary>Background worker responsible for parsing the export, orchestrating the sync, and generating reports.</summary>
public class MyListSyncWorker(IMetadataService metadataService, IUserDataService userDataService, IUserService userService, IHttpClientFactory httpClientFactory)
{
    #region Setup & State

    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>The live state of the sync worker.</summary>
    public SyncState State { get; } = new();

    #endregion

    #region Public API

    /// <summary>Parses the export, queues missing local episodes for MyList sync via Shoko Abstractions, and generates a report.</summary>
    /// <param name="fileStream">The buffered memory stream containing the uploaded export.</param>
    /// <param name="filename">The original filename of the uploaded export.</param>
    /// <param name="dryRun">Whether to perform a dry run.</param>
    /// <param name="apiKey">The Shoko v3 API Key used to authenticate the file addition calls.</param>
    /// <param name="baseUrl">The automatically derived local base URL of the Shoko Server.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the background sync operation.</returns>
    public async Task StartSyncAsync(Stream fileStream, string filename, bool dryRun, string apiKey, string baseUrl, CancellationToken ct)
    {
        if (State.IsRunning)
            return;
        State.IsRunning = true;
        State.DryRun = dryRun;
        State.MissingCount = 0;
        State.OutOfSyncCount = 0;
        State.ProcessedEpisodes = 0;
        State.EpisodesSynced = 0;
        State.LastReportUrl = null;
        State.Logs.Clear();

        var reportDetails = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        s_logger.Info("MyListSync: Starting task (DryRun: {0}, File: {1})", dryRun, filename);

        try
        {
            Log($"Parsing AniDB Export ({filename})...");
            var mylistEpisodes = filename.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ? ParseTgzForEpisodes(fileStream) : ParseXmlForEpisodes(fileStream);

            Log($"Extracted {mylistEpisodes.Count} unique episode IDs from export.");

            Log("Scanning local database for missing or out-of-sync episodes...");
            var defaultUser = userService.GetUsers().FirstOrDefault();
            if (defaultUser == null)
            {
                s_logger.Error("MyListSync: Fatal error -> Could not find a default Shoko user to perform the sync.");
                Log("Fatal Error: Could not find a default Shoko user to perform the sync.");
                return;
            }

            var missingEpisodes = new List<IShokoEpisode>();
            var allSeries = metadataService.GetAllShokoSeries() ?? [];
            int missingCount = 0;
            int outOfSyncCount = 0;

            foreach (var series in allSeries)
            {
                foreach (var ep in series.Episodes)
                {
                    if (ep.AnidbEpisodeID <= 0 || ep.VideoList?.Count == 0)
                        continue;

                    var ud = userDataService.GetEpisodeUserData(ep, defaultUser);
                    bool localWatched = ud?.IsWatched ?? false;

                    if (!mylistEpisodes.TryGetValue(ep.AnidbEpisodeID, out bool aniDbWatched))
                    {
                        // Case 1: Completely missing from AniDB MyList
                        missingEpisodes.Add(ep);
                        missingCount++;
                    }
                    else if (localWatched && !aniDbWatched)
                    {
                        // Case 2: Present on MyList, but marked unwatched on AniDB and watched in Shoko
                        missingEpisodes.Add(ep);
                        outOfSyncCount++;
                    }
                }
            }

            State.MissingCount = missingCount;
            State.OutOfSyncCount = outOfSyncCount;
            int total = missingCount + outOfSyncCount;
            s_logger.Info("MyListSync: Scan complete -> Found {0} missing and {1} out-of-sync episodes requiring alignment.", missingCount, outOfSyncCount);
            Log($"Found {missingCount} missing and {outOfSyncCount} out-of-sync episodes requiring alignment with AniDB MyList.");

            if (total == 0)
            {
                Log("Sync complete. Nothing to do.");
                return;
            }

            HttpClient? client = null;
            if (!dryRun)
            {
                client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("apikey", apiKey);
            }

            // Group missing/out-of-sync episodes by SeriesID to optimize statistics re-aggregation
            var groupedEpisodes = missingEpisodes.GroupBy(ep => ep.SeriesID).ToList();

            foreach (var group in groupedEpisodes)
            {
                var epsInSeries = group.ToList();
                for (int i = 0; i < epsInSeries.Count; i++)
                {
                    if (ct.IsCancellationRequested)
                        break;
                    State.ProcessedEpisodes++;

                    var ep = epsInSeries[i];
                    bool isLastInSeries = i == epsInSeries.Count - 1;

                    var udOriginal = userDataService.GetEpisodeUserData(ep, defaultUser);
                    string stateInfo =
                        udOriginal != null ? $"Watched: {udOriginal.IsWatched}, Rating: {(udOriginal.HasUserRating ? udOriginal.UserRating.Value.ToString() : "None")}" : "Watched: False, Rating: None";

                    bool onMyList = mylistEpisodes.ContainsKey(ep.AnidbEpisodeID);
                    string typePrefix = onMyList ? "[OUT OF SYNC]" : "[MISSING]";
                    string epDisplay = $"{typePrefix} [{ep.Series?.PreferredTitle?.Value}] S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} (AniDB: {ep.AnidbEpisodeID}) -> {stateInfo}";

                    reportDetails.Add(epDisplay);

                    if (dryRun)
                    {
                        Log($"[DRYRUN] Would sync {epDisplay}");
                        continue;
                    }

                    try
                    {
                        s_logger.Trace("MyListSync: Syncing episode {0} (Series: {1})", ep.AnidbEpisodeID, ep.Series?.PreferredTitle?.Value);
                        Log($"Syncing '{ep.Series?.PreferredTitle?.Value}' Ep {ep.EpisodeNumber} (AniDB: {ep.AnidbEpisodeID})...");

                        // Step 1: Force add physical files to MyList using Shoko's v3 HTTP API (only if the episode is completely missing from MyList)
                        if (!onMyList)
                        {
                            foreach (var file in ep.VideoList ?? [])
                            {
                                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/v3/File/{file.ID}/AddToMyList");
                                using var resp = await client!.SendAsync(req, ct).ConfigureAwait(false);
                                if (!resp.IsSuccessStatusCode)
                                    s_logger.Warn("MyListSync: AddToMyList HTTP API returned {0} for file {1}", resp.StatusCode, file.ID);
                            }
                        }

                        // Step 2: Toggle watched status if watched, to force database write events to fire, triggering Shoko to push the watched state to AniDB
                        bool originalWatched = udOriginal?.IsWatched ?? false;
                        DateTime? originalDate = udOriginal?.LastPlayedAt;

                        if (originalWatched)
                        {
                            // Pass false to updateStatsNow on the temporary toggle to prevent Shoko from doing redundant statistics calculations
                            await userDataService.SetEpisodeWatchedStatus(ep, defaultUser, false, originalDate, VideoUserDataSaveReason.UserInteraction, false, false).ConfigureAwait(false);
                            await userDataService.SetEpisodeWatchedStatus(ep, defaultUser, true, originalDate, VideoUserDataSaveReason.UserInteraction, false, isLastInSeries).ConfigureAwait(false);
                        }

                        if (udOriginal?.HasUserRating == true)
                        {
                            // Toggle rating to force database write events to fire, forcing Shoko to dispatch the vote to AniDB
                            await userDataService.RateEpisode(ep, defaultUser, 0).ConfigureAwait(false);
                            await userDataService.RateEpisode(ep, defaultUser, udOriginal.UserRating.Value).ConfigureAwait(false);
                        }

                        State.EpisodesSynced++;
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        s_logger.Warn(ex, "MyListSync: Failed to sync episode {0}", ep.AnidbEpisodeID);
                        Log($"Error syncing episode {ep.AnidbEpisodeID}: {ex.Message}");
                        State.Errors++;
                    }
                }
            }
            s_logger.Info("MyListSync: Task completed successfully -> Synced {0} episodes with {1} errors.", State.EpisodesSynced, State.Errors);
            Log("Sync completed successfully.");
        }
        catch (Exception ex)
        {
            s_logger.Error(ex, "MyListSync: Fatal error encountered during execution");
            Log($"Fatal Error: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            GenerateReport(sw.Elapsed, reportDetails);
            fileStream?.Dispose();
            State.IsRunning = false;
        }
    }

    #endregion

    #region Internal Parsers & Logging

    private Dictionary<int, bool> ParseTgzForEpisodes(Stream tgzStream)
    {
        using var gzip = new GZipStream(tgzStream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType == TarEntryType.RegularFile && entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                using var entryStream = entry.DataStream;
                if (entryStream != null)
                    return ParseXmlForEpisodes(entryStream);
            }
        }
        return [];
    }

    private Dictionary<int, bool> ParseXmlForEpisodes(Stream stream)
    {
        var episodes = new Dictionary<int, bool>();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        int currentEpId = 0;
        string? currentViewDate = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Name.Equals("ep_id", StringComparison.OrdinalIgnoreCase))
                    currentEpId = int.TryParse(reader.ReadElementContentAsString().Trim(), out int id) ? id : 0;
                else if (reader.Name.Equals("viewdate", StringComparison.OrdinalIgnoreCase))
                    currentViewDate = reader.ReadElementContentAsString().Trim();
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Name.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                if (currentEpId > 0)
                {
                    bool isWatched = !string.IsNullOrEmpty(currentViewDate) && currentViewDate != "-";
                    episodes[currentEpId] = episodes.TryGetValue(currentEpId, out bool existingWatched) ? existingWatched || isWatched : isWatched;
                }
                currentEpId = 0;
                currentViewDate = null;
            }
        }
        return episodes;
    }

    private void Log(string message) => State.Logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");

    private void GenerateReport(TimeSpan elapsed, List<string> details)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Shoko MyList Sync Plus Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
            sb.AppendLine($"  Elapsed Time             : {elapsed.TotalSeconds:F2}s");
            sb.AppendLine($"  Mode                     : {(State.DryRun ? "Dry Run" : "Live")}");
            sb.AppendLine($"  Missing Episodes Found   : {State.MissingCount}");
            sb.AppendLine($"  Out-of-Sync Episodes     : {State.OutOfSyncCount}");
            sb.AppendLine($"  Episodes Synced          : {State.EpisodesSynced}");
            sb.AppendLine($"  Errors                   : {State.Errors}");

            if (details.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Out-of-Sync & Missing Episodes Details:");
                foreach (var d in details.OrderBy(x => x))
                    sb.AppendLine($"  {d}");
            }

            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string logsDir = Path.Combine(pluginDir, "logs");
            Directory.CreateDirectory(logsDir);

            string filename = "mylist-sync-report.log";
            File.WriteAllText(Path.Combine(logsDir, filename), sb.ToString());
            State.LastReportUrl = $"/api/plugin/ShokoMyListSyncPlus/logs/{filename}";
        }
        catch (Exception ex)
        {
            s_logger.Warn(ex, "MyListSync: Failed to generate report log.");
        }
    }

    #endregion
}
