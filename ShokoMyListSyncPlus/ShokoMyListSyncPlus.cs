using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Plugin;
using Shoko.Abstractions.Plugin.Models;

namespace ShokoMyListSyncPlus;

/// <summary>Plugin entry point and descriptor for Shoko Server.</summary>
public class Plugin : IPlugin
{
    /// <inheritdoc/>
    public Guid ID => new("f4d3c2b1-a9e8-7f6d-5c4b-3a2b1c0d9e8f");

    /// <inheritdoc/>
    public string Name => "Shoko MyList Sync Plus";

    /// <inheritdoc/>
    public string Description => "Syncs Shoko's database state to AniDB's MyList by verifying it against a xml-cdb MyList Export.";

    /// <inheritdoc/>
    public IReadOnlyList<PluginPage> GetPages() => [new PluginPage { Name = "Dashboard", Url = "/api/plugin/ShokoMyListSyncPlus/dashboard" }];
}

/// <summary>Registers plugin services into the DI container.</summary>
public class ServiceRegistration : IPluginServiceRegistration
{
    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection services, IApplicationPaths applicationPaths)
    {
        services.AddHttpClient();
        services.AddSingleton<MyListSyncWorker>();
    }
}
