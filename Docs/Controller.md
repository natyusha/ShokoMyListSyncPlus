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
POST /sync?dryRun={true|false}&import={true|false}             -> StartSync
```

- `GetStatus` Retrieves the live sync statistics (counts, errors) and pops any pending log messages from the background queue.
- `GetLog` Serves report files generated under the plugin's `logs` directory.
- `StartSync` Accepts a form payload to begin the synchronization background task.
  - `dryRun` (default true) If true, the plugin will scan the MyList and evaluate its state generating a plan without triggering any actual changes on AniDB or locally.
  - `import` (default false) If true, pulls watched states from AniDB into Shoko. If false, exports local database states to AniDB's MyList.
