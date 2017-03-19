using System;
using IopCommon;
using IopServerCore.Data;
using ProximityServer.Data.Repositories;

namespace ProximityServer.Data
{
  /// <summary>
  /// Coordinates the work of multiple repositories by creating a single database context class shared by all of them.
  /// </summary>
  public class UnitOfWork : UnitOfWorkBase<Context>, IDisposable
  {
    /// <summary>Lock for SettingsRepository.</summary>
    public static DatabaseLock SettingsLock = new DatabaseLock("SETTINGS");

    /// <summary>Lock for PrimaryActivityRepository.</summary>
    public static DatabaseLock PrimaryActivityLock = new DatabaseLock("PRIMARY_ACTIVITY");

    /// <summary>Lock for NeighborActivityRepository.</summary>
    public static DatabaseLock NeighborActivityLock = new DatabaseLock("NEIGHBORHOOD_ACTIVITY");

    /// <summary>Lock for NeighborRepository.</summary>
    public static DatabaseLock NeighborLock = new DatabaseLock("NEIGHBOR");

    /// <summary>Lock for NeighborhoodActionRepository.</summary>
    public static DatabaseLock NeighborhoodActionLock = new DatabaseLock("NEIGHBORHOOD_ACTION");

    /// <summary>Lock for FollowerRepository.</summary>
    public static DatabaseLock FollowerLock = new DatabaseLock("FOLLOWER");


    /// <summary>Settings repository.</summary>
    private SettingsRepository settingsRepository;
    /// <summary>Settings repository.</summary>
    public SettingsRepository SettingsRepository
    {
      get
      {
        if (settingsRepository == null)
          settingsRepository = new SettingsRepository(Context);

        return settingsRepository;
      }
    }
  }
}
