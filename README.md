# Shoko MyList Sync Plus

<!-- prettier-ignore-start -->

[![Discord](https://img.shields.io/discord/96234011612958720?logo=discord&logoColor=fff&label=Discord&color=5865F2 "Shoko Discord")](https://discord.gg/shokoanime)
[![Shoko Docs](https://img.shields.io/badge/VitePress-Shoko_Docs-4E7CF5?logo=vitepress&logoColor=fff)](https://docs.shokoanime.com/)
[![GitHub Latest](https://img.shields.io/github/v/tag/natyusha/ShokoMyListSyncPlus?label=Latest&logo=github&logoColor=fff)](https://github.com/natyusha/ShokoMyListSyncPlus/releases/latest)
-

<!-- prettier-ignore-end -->

This is a companion plugin for Shoko Server that syncs Shoko's database state to AniDB's MyList by verifying it against a `xml-cdb` MyList Export.

For long-time Shoko Server users it is quite common for the AniDB MyList to become desynced from Shoko. To compound this issue, Shoko's built-in sync commands have no knowledge of the MyList state and will produce API calls for every single file. By taking an export of your current MyList, this plugin identifies any files indexed by Shoko that are not currently synced to your AniDB account. It then uses Shoko's internal queue to push the missing watched states, ratings, and file metadata directly to AniDB without any redundant API calls.

## Installation

Installation can be completed via Shoko's WebUI (Recommended) or Manually. Both Methods will be detailed below:

- **WebUI** (Recommended)
  - Open Shoko's WebUI and navigate to: `Settings > Plugin Management > Repositories`
  - Click `Add Repository` and configure the following:
    - Name: `Shoko MyList Sync Plus`
    - Manifest URL: `https://raw.githubusercontent.com/natyusha/ShokoMyListSyncPlus/master/manifest.json`
  - Go to `Settings > Plugin Management > Browse` and find "Shoko MyList Sync Plus"
  - Click `Install`
- **Manual**
  - Navigate to Shoko Server's `plugins` directory and create a new subfolder called `ShokoMyListSyncPlus`
  - Extract [the latest release](https://github.com/natyusha/ShokoMyListSyncPlus/releases) into the `plugins/ShokoMyListSyncPlus` directory
  - It may be necessary to create the `plugins` (all lowercase) folder in Shoko's root first
- Restart Shoko Server after finishing either of the above installation methods

## Usage

1. Navigate to `User Data -> Export` in AniDB's navigation menu or go directly to: https://anidb.net/user/export
2. Select **xml-cdb** as the template and click **Request Export**; save the `.tgz` file to your computer once the export is complete
3. Navigate to the plugin dashboard and enter a Shoko API Key from `/webui/settings/api-keys`
4. Open the Shoko MyList Sync Plus dashboard: `http(s)://{ShokoHost}:{ShokoPort}/api/plugin/ShokoMyListSyncPlus/dashboard`
5. Drag and drop the downloaded export file into the dashboard zone
6. Click **Start Sync** to begin. The logs will populate automatically as episodes are processed

## TODO

- Replace v3 API usage (`/api/v3/File/{file.ID}/AddToMyList`) with a Shoko.Abstractions implementation
