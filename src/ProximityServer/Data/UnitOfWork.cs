using System;
using IopCommon;
using IopAppCore.Data;
using ProximityServer.Data.Repositories;
using ProximityServer.Data.Models;

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
          settingsRepository = new SettingsRepository(Context, this);

        return settingsRepository;
      }
    }

    /// <summary>Activity repository for the proximity server's primary activities.</summary>
    private PrimaryActivityRepository primaryActivityRepository;
    /// <summary>Activity repository for the proximity server's primary activities.</summary>
    public PrimaryActivityRepository PrimaryActivityRepository
    {
      get
      {
        if (primaryActivityRepository == null)
          primaryActivityRepository = new PrimaryActivityRepository(Context, this);

        return primaryActivityRepository;
      }
    }

    /// <summary>Activity repository for activities managed by the proximity server's neighbors.</summary>
    private NeighborActivityRepository neighborActivityRepository;
    /// <summary>Activity repository for activities managed by the proximity server's neighbors.</summary>
    public NeighborActivityRepository NeighborActivityRepository
    {
      get
      {
        if (neighborActivityRepository == null)
          neighborActivityRepository = new NeighborActivityRepository(Context, this);

        return neighborActivityRepository;
      }
    }


    /// <summary>Repository of profile server neighbors.</summary>
    private NeighborRepository neighborRepository;
    /// <summary>Repository of profile server neighbors.</summary>
    public NeighborRepository NeighborRepository
    {
      get
      {
        if (neighborRepository == null)
          neighborRepository = new NeighborRepository(Context, this);

        return neighborRepository;
      }
    }


    /// <summary>Repository of planned actions in the neighborhood.</summary>
    private NeighborhoodActionRepository neighborhoodActionRepository;
    /// <summary>Repository of planned actions in the neighborhood.</summary>
    public NeighborhoodActionRepository NeighborhoodActionRepository
    {
      get
      {
        if (neighborhoodActionRepository == null)
          neighborhoodActionRepository = new NeighborhoodActionRepository(Context, this);

        return neighborhoodActionRepository;
      }
    }

    /// <summary>Repository of profile server followers.</summary>
    private FollowerRepository followerRepository;
    /// <summary>Repository of profile server followers.</summary>
    public FollowerRepository FollowerRepository
    {
      get
      {
        if (followerRepository == null)
          followerRepository = new FollowerRepository(Context, this);

        return followerRepository;
      }
    }

  }
}
