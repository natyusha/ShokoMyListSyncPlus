<!-- prettier-ignore-start -->

![Shoko MyList Sync+ Logo](https://raw.githubusercontent.com/natyusha/ShokoMyListSyncPlus/master/ShokoMyListSyncPlus/Assets/shoko-mylist-sync-plus-logo-small.png "Shoko MyList Sync+")  
[![Discord](https://img.shields.io/discord/96234011612958720?logo=discord&logoColor=fff&label=Discord&color=5865F2 "Shoko Discord")](https://discord.gg/shokoanime)
[![Shoko Docs](https://img.shields.io/badge/VitePress-Shoko_Docs-4E7CF5?logo=vitepress&logoColor=fff)](https://docs.shokoanime.com/)
[![GitHub Latest](https://img.shields.io/github/v/tag/natyusha/ShokoMyListSyncPlus?label=Latest&logo=github&logoColor=fff)](https://github.com/natyusha/ShokoMyListSyncPlus/releases/latest)
-

<!-- prettier-ignore-end -->

This is a companion plugin for Shoko Server that syncs Shoko's database state to AniDB's MyList by verifying it against your AniDB MyList.

For long-time Shoko Server users, it is quite common for the AniDB MyList to become somewhat desynced from Shoko. This plugin identifies any files or episodes indexed by Shoko that are not currently synced to your AniDB account, or whose watched states differ, and automatically syncs them via Shoko's internal MyList service.

## Installation

Installation can be completed via Shoko's WebUI (Recommended) or Manually. Both Methods will be detailed below:

- **WebUI** (Recommended)
  - Open Shoko's WebUI and navigate to: `Settings > Plugin Management > Repositories`
  - Click `Add Repository` and configure the following:
    - Name: `NN Plugins`
    - Manifest URL: `https://raw.githubusercontent.com/natyusha/ShokoPluginManifest/master/manifest.json`
  - Go to `Settings > Plugin Management > Browse` and find "Shoko MyList Sync+"
  - Click `Install`
- **Manual**
  - Navigate to Shoko Server's `plugins` directory and create a new subfolder called `ShokoMyListSyncPlus`
  - Extract [the latest release](https://github.com/natyusha/ShokoMyListSyncPlus/releases) into the `plugins/ShokoMyListSyncPlus` directory
  - It may be necessary to create the `plugins` (all lowercase) folder in Shoko's root first
- Restart Shoko Server after finishing either of the above installation methods

## Usage

1. Navigate to the plugin's dashboard at `Settings > Plugins > Shoko MyList Sync+ > Dashboard`
2. Choose your sync direction using the toggle: `AniDB <- Shoko` (Export to AniDB) or `AniDB -> Shoko` (Import to Shoko)
3. Uncheck **Dry Run** if you don't just want to see a preview of the potential changes to your MyList/Shoko
4. Click **Start MyList Sync** to begin. The status log will populate automatically as the sync plan is processed
