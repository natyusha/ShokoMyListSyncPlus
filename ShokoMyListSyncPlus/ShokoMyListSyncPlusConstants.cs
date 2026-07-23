namespace ShokoMyListSyncPlus;

/// <summary>Centralized constants for plugin identity, task identifiers, logging, and system filenames.</summary>
public static class ShokoMyListSyncPlusConstants
{
    #region Plugin Identity

    /// <summary>Display name of the plugin.</summary>
    public const string Name = "Shoko MyList Sync+";

    /// <summary>Description of the plugin.</summary>
    public const string Description = "Syncs Shoko's database state to AniDB's MyList by verifying it against a xml-cdb MyList Export.";

    /// <summary>Current version string.</summary>
    public const string Version = "1.0.2";

    /// <summary>Internal API version.</summary>
    public const string ApiVersion = "1";

    /// <summary>Unique plugin ID used for configuration storage.</summary>
    public const string PluginId = "f4d3c2b1-a9e8-7f6d-5c4b-3a2b1c0d9e8f";

    /// <summary>Base HTTP path for plugin endpoints.</summary>
    public const string BasePath = "/api/plugin/ShokoMyListSyncPlus";

    #endregion
}
