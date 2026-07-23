using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Plugin;
using Shoko.Abstractions.Plugin.Models;

namespace ShokoMyListSyncPlus;

#region Plugin Descriptor

/// <summary>Plugin entry point and descriptor for Shoko Server.</summary>
public class Plugin : IPlugin
{
    /// <inheritdoc/>
    public Guid ID => new(ShokoMyListSyncPlusConstants.PluginId);

    /// <inheritdoc/>
    public string Name => ShokoMyListSyncPlusConstants.Name;

    /// <inheritdoc/>
    public string Description => ShokoMyListSyncPlusConstants.Description;

    /// <inheritdoc/>
    public string? EmbeddedThumbnailResourceName => "ShokoMyListSyncPlus.Assets.shoko-mylist-sync-plus-logo.png";

    /// <inheritdoc/>
    public IReadOnlyList<PluginPage> GetPages() => [new PluginPage { Name = "Dashboard", Url = $"{ShokoMyListSyncPlusConstants.BasePath}/dashboard" }];
}

#endregion

#region Service Registration

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

#endregion
