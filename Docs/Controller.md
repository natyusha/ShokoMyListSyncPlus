# Controller

- All endpoints below are available under the plugin base path: `http(s)://{ShokoHost}:{ShokoPort}/api/plugin/ShokoMyListSyncPlus`
- They can be interacted with easily using **/swagger/** at: `http(s)://{ShokoHost}:{ShokoPort}/swagger/index.html?urls.primaryName=Shoko+MyList+Sync+Plus+V1`

## Dashboard & Assets

```text
GET  /dashboard                                                -> GetDashboard
GET  /dashboard/{*path}                                        -> GetAssetFile
```

- `GetDashboard` Serves the plugin's frontend UI from `dashboard/dashboard.cshtml`.
- `GetAssetFile` Serves static assets (JS, CSS, fonts, favicon) mapping their respective MIME types.

## Sync Operations

```text
GET  /status                                                   -> GetStatus
GET  /logs/{fileName}                                          -> GetLog
POST /sync                                                     -> StartSync
```

- `GetStatus` Retrieves the live sync statistics (counts, errors) and pops any pending log messages from the background queue.
- `GetLog` Serves report files generated under the plugin's `logs` directory.
- `StartSync` Accepts a `multipart/form-data` payload to begin the synchronization background task.
  - `exportFile` (Required): The uploaded `xml-cdb` .xml or .tgz file.
  - `dryRun` (Optional bool): If `true`, the plugin will scan for missing/out-of-sync episodes and evaluate their state, but will not trigger any changes.
  - `apiKey` (Required if dryRun is false): The Shoko v3 API Key used to authorize the `AddToMyList` file commands.
