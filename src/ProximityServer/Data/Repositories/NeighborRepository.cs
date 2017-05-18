using ProximityServer.Data.Models;
using Iop.Proximityserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using ProximityServer.Network;
using ProximityServer.Kernel;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using IopServerCore.Kernel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Net;

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository of proximity server neighbors.
  /// </summary>
  public class NeighborRepository : GenericRepository<Neighbor>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Repositories.NeighborRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public NeighborRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }


    /// <summary>
    /// Deletes neighbor server, all its activities and all neighborhood actions for it from the database.
    /// </summary>
    /// <param name="NeighborId">Identifier of the neighbor server to delete.</param>
    /// <param name="ActionId">If there is a neighborhood action that should NOT be deleted, this is its ID, otherwise it is -1.</param>
    /// <param name="HoldingLocks">true if the caller is holding NeighborLock and NeighborhoodActionLock.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> DeleteNeighborAsync(byte[] NeighborId, int ActionId = -1, bool HoldingLocks = false)
    {
      log.Trace("(NeighborId:'{0}',ActionId:{1},HoldingLocks:{2})", NeighborId.ToHex(), ActionId, HoldingLocks);

      bool res = false;
      bool success = false;


      // Delete neighbor from the list of neighbors.
      DatabaseLock lockObject = UnitOfWork.NeighborLock;
      if (!HoldingLocks) await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        Neighbor neighbor = (await GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
        if (neighbor != null)
        {
          Delete(neighbor);
          await unitOfWork.SaveThrowAsync();
          log.Debug("Neighbor ID '{0}' deleted from database.", NeighborId.ToHex());
        }
        else
        {
          // If the neighbor does not exist, we set success to true as the result of the operation is as we want it 
          // and we gain nothing by trying to repeat the action later.
          log.Warn("Neighbor ID '{0}' not found.", NeighborId.ToHex());
        }

        success = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      if (!HoldingLocks) unitOfWork.ReleaseLock(lockObject);

      // Delete neighbor's activities from the database.
      if (success)
      {
        success = false;

        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        lockObject = UnitOfWork.NeighborActivityLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          List<NeighborActivity> activities = (await unitOfWork.NeighborActivityRepository.GetAsync(i => i.PrimaryServerId == NeighborId)).ToList();
          if (activities.Count > 0)
          {
            log.Debug("There are {0} activities of removed neighbor ID '{1}'.", activities.Count, NeighborId.ToHex());
            foreach (NeighborActivity activity in activities)
              unitOfWork.NeighborActivityRepository.Delete(activity);

            await unitOfWork.SaveThrowAsync();
            log.Debug("{0} identities hosted on neighbor ID '{1}' deleted from database.", activities.Count, NeighborId.ToHex());
          }
          else log.Trace("No profiles hosted on neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);

        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = true;
      }

      if (success)
      {
        success = false;
        lockObject = UnitOfWork.NeighborhoodActionLock;
        if (!HoldingLocks) await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          // Do not delete the current action, it will be deleted just after this method finishes.
          List<NeighborhoodAction> actions = unitOfWork.NeighborhoodActionRepository.Get(a => (a.ServerId == NeighborId) && (a.Id != ActionId)).ToList();
          if (actions.Count > 0)
          {
            foreach (NeighborhoodAction action in actions)
            {
              log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, NeighborId.ToHex());
              unitOfWork.NeighborhoodActionRepository.Delete(action);
            }

            await unitOfWork.SaveThrowAsync();
          }
          else log.Debug("No neighborhood actions for neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!HoldingLocks) unitOfWork.ReleaseLock(lockObject);
      }

      res = success;


      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sets NeighborPort of a neighbor to null.
    /// </summary>
    /// <param name="NeighborId">Identifier of the neighbor server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ResetNeighborPortAsync(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      bool res = false;
      bool dbSuccess = false;
      DatabaseLock lockObject = UnitOfWork.FollowerLock;
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          Neighbor neighbor = (await GetAsync(f => f.NeighborId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            neighbor.NeighborPort = null;
            Update(neighbor);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = true;
          }
          else log.Error("Unable to find follower ID '{0}'.", NeighborId.ToHex());

          dbSuccess = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!dbSuccess)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the server is the nearest server to a given location.
    /// </summary>
    /// <param name="TargetLocation">Target GPS location.</param>
    /// <param name="IgnoreServerIds">List of network IDs that should be ignored.</param>
    /// <param name="NearestServerId">If the result is false, this is filled with network identifier of a neighbor server that is nearest to the target location.</param>
    /// <param name="Threshold">Optionally, threshold value which allows the function to return true even if there exists a neighbor server that is actually closer 
    /// to the target location, but it is only slightly closer than the proximity server.</param>
    /// <returns>true if the server is the nearest proximity server to the target location, false if the server knows at least one other server that is closer
    /// or if the function fails.</returns>
    public async Task<bool> IsServerNearestToLocationAsync(GpsLocation TargetLocation, List<byte[]> IgnoreServerIds, StrongBox<byte[]> NearestServerId, double? Threshold = null)
    {
      log.Trace("(TargetLocation:[{0}],IgnoreServerIds:'{1}',Threshold:{2})", TargetLocation, string.Join(",", IgnoreServerIds), Threshold != null ? Threshold.Value.ToString(CultureInfo.InvariantCulture) : "null");

      bool res = true;

      LocationBasedNetwork loc = (LocationBasedNetwork)Base.ComponentDictionary[LocationBasedNetwork.ComponentName];
      GpsLocation myLocation = loc.Location;
      double myDistance = TargetLocation.DistanceTo(myLocation);
      log.Trace("Server's distance to the activity location is {0} metres.", myDistance.ToString(CultureInfo.InvariantCulture));

      double thresholdCoef = 1;
      if (Threshold != null) thresholdCoef += Threshold.Value;

      try
      {
        List<Neighbor> allNeighbors = (await GetAsync(null, null, true)).ToList();
        foreach (Neighbor neighbor in allNeighbors)
        {
          GpsLocation neighborLocation = new GpsLocation(neighbor.LocationLatitude, neighbor.LocationLongitude);
          double neighborDistance = neighborLocation.DistanceTo(myLocation);
          double thresholdNeighborDistance = neighborDistance * thresholdCoef;
          bool serverNearestWithThreshold = myDistance <= thresholdNeighborDistance;
          if (!serverNearestWithThreshold)
          {
            NearestServerId.Value = neighbor.NeighborId;
            log.Debug("Server network ID '{0}', GPS location [{1}] is closer (distance {2} m, {3} m with threshold) to the target location [{4}] than the current server location [{5}] (distance {6} m).", 
              neighbor.NeighborId.ToHex(), neighborLocation, neighborDistance.ToString(CultureInfo.InvariantCulture), thresholdNeighborDistance.ToString(CultureInfo.InvariantCulture), TargetLocation, 
              myLocation, myDistance.ToString(CultureInfo.InvariantCulture));
            res = false;
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Updates LastRefreshTime of a neighbor server.
    /// </summary>
    /// <param name="NeighborId">Identifier of the neighbor server to update.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> UpdateNeighborLastRefreshTimeAsync(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      bool res = false;

      DatabaseLock lockObject = UnitOfWork.NeighborLock;
      await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        Neighbor neighbor = (await GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
        if (neighbor != null)
        {
          neighbor.LastRefreshTime = DateTime.UtcNow;
          Update(neighbor);
          await unitOfWork.SaveThrowAsync();
        }
        else
        {
          // Between the check couple of lines above and here, the requesting server stop being our neighbor
          // we can ignore it now and proceed as this does no harm and the requesting server will be informed later.
          log.Error("Client ID '{0}' is no longer our neighbor.", NeighborId.ToHex());
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to update LastRefreshTime of neighbor ID '{0}': {1}", NeighborId.ToHex(), e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Saves activities of a neighbor from the memory to the database. This is done when the neighborhood initialization process is finished.
    /// </summary>
    /// <param name="ActivityDatabase">List of activities received from the neighbor mapped by their full ID.</param>
    /// <param name="NeighborId">Network ID of the neighbor.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveNeighborhoodInitializationActivitiesAsync(Dictionary<string, NeighborActivity> ActivityDatabase, byte[] NeighborId)
    {
      log.Trace("(ActivityDatabase.Count:{0},NeighborId:'{1}')", ActivityDatabase.Count, NeighborId.ToHex());

      bool error = false;
      bool success = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborActivityLock, UnitOfWork.NeighborLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          Neighbor neighbor = (await GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            // The neighbor is now initialized and is allowed to send us updates.
            neighbor.LastRefreshTime = DateTime.UtcNow;
            neighbor.SharedActivities = ActivityDatabase.Count;
            Update(neighbor);

            // Insert all its activities.
            foreach (NeighborActivity activity in ActivityDatabase.Values)
              await unitOfWork.NeighborActivityRepository.InsertAsync(activity);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else log.Error("Unable to find neighbor ID '{0}'.", NeighborId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
          error = true;
        }

        unitOfWork.ReleaseLock(lockObjects);
      }


      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Checks whether the server is the nearest server to a given location.
    /// </summary>
    /// <param name="TargetLocation">Target GPS location.</param>
    /// <param name="IgnoreServerIds">List of network IDs that should be ignored.</param>
    /// <param name="NearestServerId">If the result is false, this is filled with network identifier of a neighbor server that is nearest to the target location.</param>
    /// <param name="Threshold">Optionally, threshold value which allows the function to return true even if there exists a neighbor server that is actually closer 
    /// to the target location, but it is only slightly closer than the proximity server.</param>
    /// <returns>true if the server is the nearest proximity server to the target location, false if the server knows at least one other server that is closer
    /// or if the function fails.</returns>
    public async Task<ServerContactInfo> GetServerContactInfoAsync(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId);

      Neighbor neighbor = (await GetAsync(n => n.NeighborId == NeighborId, null, true)).FirstOrDefault();
      ServerContactInfo res = neighbor.GetServerContactInfo();

      log.Trace("(-):{0}", res != null ? "ServerContactInfo" : "null");
      return res;
    }


  }
}
