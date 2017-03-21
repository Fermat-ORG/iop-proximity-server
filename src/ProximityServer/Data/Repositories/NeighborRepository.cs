﻿using ProximityServer.Data.Models;
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

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository of proximity server neighbors.
  /// </summary>
  public class NeighborRepository : GenericRepository<Context, Neighbor>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Repositories.NeighborRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public NeighborRepository(Context context)
      : base(context)
    {
    }


    /// <summary>
    /// Deletes neighbor server, all its activities and all neighborhood actions for it from the database.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="NeighborId">Identifier of the neighbor server to delete.</param>
    /// <param name="ActionId">If there is a neighborhood action that should NOT be deleted, this is its ID, otherwise it is -1.</param>
    /// <param name="HoldingLocks">true if the caller is holding NeighborLock and NeighborhoodActionLock.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> DeleteNeighbor(UnitOfWork UnitOfWork, byte[] NeighborId, int ActionId = -1, bool HoldingLocks = false)
    {
      log.Trace("(NeighborId:'{0}',ActionId:{1},HoldingLocks:{2})", NeighborId.ToHex(), ActionId, HoldingLocks);

      bool res = false;
      bool success = false;


      // Delete neighbor from the list of neighbors.
      DatabaseLock lockObject = UnitOfWork.NeighborLock;
      if (!HoldingLocks) await UnitOfWork.AcquireLockAsync(lockObject);
      try
      {
        Neighbor neighbor = (await GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
        if (neighbor != null)
        {
          Delete(neighbor);
          await UnitOfWork.SaveThrowAsync();
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
      if (!HoldingLocks) UnitOfWork.ReleaseLock(lockObject);

      // Delete neighbor's activities from the database.
      if (success)
      {
        success = false;

        // Disable change tracking for faster multiple deletes.
        UnitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        lockObject = UnitOfWork.NeighborActivityLock;
        await UnitOfWork.AcquireLockAsync(lockObject);
        try
        {
          List<NeighborActivity> activities = (await UnitOfWork.NeighborActivityRepository.GetAsync(i => i.PrimaryServerId == NeighborId)).ToList();
          if (activities.Count > 0)
          {
            log.Debug("There are {0} activities of removed neighbor ID '{1}'.", activities.Count, NeighborId.ToHex());
            foreach (NeighborActivity activity in activities)
              UnitOfWork.NeighborActivityRepository.Delete(activity);

            await UnitOfWork.SaveThrowAsync();
            log.Debug("{0} identities hosted on neighbor ID '{1}' deleted from database.", activities.Count, NeighborId.ToHex());
          }
          else log.Trace("No profiles hosted on neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        UnitOfWork.ReleaseLock(lockObject);

        UnitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = true;
      }

      if (success)
      {
        success = false;
        lockObject = UnitOfWork.NeighborhoodActionLock;
        if (!HoldingLocks) await UnitOfWork.AcquireLockAsync(lockObject);
        try
        {
          // Do not delete the current action, it will be deleted just after this method finishes.
          List<NeighborhoodAction> actions = UnitOfWork.NeighborhoodActionRepository.Get(a => (a.ServerId == NeighborId) && (a.Id != ActionId)).ToList();
          if (actions.Count > 0)
          {
            foreach (NeighborhoodAction action in actions)
            {
              log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, NeighborId.ToHex());
              UnitOfWork.NeighborhoodActionRepository.Delete(action);
            }

            await UnitOfWork.SaveThrowAsync();
          }
          else log.Debug("No neighborhood actions for neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!HoldingLocks) UnitOfWork.ReleaseLock(lockObject);
      }

      res = success;


      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sets NeighborPort of a neighbor to null.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="NeighborId">Identifier of the neighbor server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ResetNeighborPort(UnitOfWork UnitOfWork, byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      bool res = false;
      bool dbSuccess = false;
      DatabaseLock lockObject = UnitOfWork.FollowerLock;
      using (IDbContextTransaction transaction = await UnitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          Neighbor neighbor = (await GetAsync(f => f.NeighborId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            neighbor.NeighborPort = null;
            Update(neighbor);

            await UnitOfWork.SaveThrowAsync();
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
          UnitOfWork.SafeTransactionRollback(transaction);
        }

        UnitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
