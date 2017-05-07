using ProximityServer.Data.Models;
using Iop.Proximityserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using System.Runtime.CompilerServices;
using IopServerCore.Kernel;

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository for primary activities of the proximity server.
  /// </summary>
  public class PrimaryActivityRepository : ActivityRepository<PrimaryActivity>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Repositories.PrimaryActivityRepository");

    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public PrimaryActivityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }


    /// <summary>
    /// Obtains activity information by its ID and owner user ID.
    /// </summary>
    /// <param name="ActivityId">User defined activity ID.</param>
    /// <param name="OwnerId">Network identifier of the identity that owns the activity.</param>
    /// <returns>Activity information or null if the function fails.</returns>
    public async Task<PrimaryActivity> GetPrimaryActivityByIdAsync(uint ActivityId, byte[] OwnerId)
    {
      log.Trace("(ActivityId:{0},OwnerId:'{1}')", ActivityId, OwnerId.ToHex());
      PrimaryActivity res = null;

      try
      {
        res = (await GetAsync(i => (i.ActivityId == ActivityId) && (i.OwnerIdentityId == OwnerId))).FirstOrDefault();
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res != null ? "PrimaryActivity" : "null");
      return res;
    }


    /// <summary>
    /// Inserts a new activity to the database provided that it does not exists yet. Then a new neighborhood action is created to propagate the new activity to the neighborhood.
    /// </summary>
    /// <param name="Activity">New activity to insert to database.</param>
    /// <param name="ExistingActivityDbId">If the function fails because the activity of the same activity ID and owner network ID exists, this is filled with database ID of the existing activity.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CreateAndPropagateAsync(PrimaryActivity Activity, StrongBox<int> ExistingActivityDbId)
    {
      log.Trace("()");

      bool res = false;

      bool success = false;
      bool signalNeighborhoodAction = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.PrimaryActivityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          PrimaryActivity existingActivity = (await GetAsync(a => (a.ActivityId == Activity.ActivityId) && (a.OwnerIdentityId == Activity.OwnerIdentityId))).FirstOrDefault();
          if (existingActivity == null)
          {
            await InsertAsync(existingActivity);

            // The activity has to be propagated to all our followers we create database actions that will be processed by dedicated thread.
            signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddActivityFollowerActionsAsync(NeighborhoodActionType.AddActivity, Activity.ActivityId, Activity.OwnerIdentityId, Activity.OwnerPublicKey.ToHex());

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else
          {
            log.Debug("Activity already exists.");
            ExistingActivityDbId.Value = existingActivity.DbId;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      if (success)
      {
        // Send signal to neighborhood action processor to process the new series of actions.
        if (signalNeighborhoodAction)
        {
          Network.NeighborhoodActionProcessor neighborhoodActionProcessor = (Network.NeighborhoodActionProcessor)Base.ComponentDictionary[Network.NeighborhoodActionProcessor.ComponentName];
          neighborhoodActionProcessor.Signal();
        }

        res = true;
      }


      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Updates an existing activity in the database. Then a new neighborhood action is created to propagate the changes to the neighborhood 
    /// if this is required. If the update is rejected, the action is deleted from the database and this is propagated to the neighborhood.
    /// <para>Note that the change in activity is not propagated if the client sets no propagation flag in the request, or if only the activity 
    /// expiration date or its location is changed.</para>
    /// </summary>
    /// <param name="UpdateRequest">Update request received from the client.</param>
    /// <param name="OwnerIdentityId">Network ID of the client who requested the update.</param>
    /// <param name="NearestServerId">If the result is Status.ErrorRejected, this is filled with network identifier of a neighbor server that is nearest to the target location.</param>
    /// <returns>Status.Ok if the function succeeds, 
    /// Status.ErrorNotFound if the activity to update does not exist,
    /// Status.ErrorRejected if the update was rejected and the client should migrate the activity to closest proximity server,
    /// Status.ErrorInternal otherwise.</returns>
    public async Task<Iop.Shared.Status> UpdateAndPropagateAsync(UpdateActivityRequest UpdateRequest, byte[] OwnerIdentityId, StrongBox<byte[]> NearestServerId)
    {
      log.Trace("(UpdateRequest.Activity.Id:{0},OwnerIdentityId:'{1}')", UpdateRequest.Activity.Id, OwnerIdentityId.ToHex());

      Iop.Shared.Status res = Iop.Shared.Status.ErrorInternal;

      bool success = false;
      bool signalNeighborhoodAction = false;
      bool migrateActivity = false;
      ActivityInformation activityInformation = UpdateRequest.Activity;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.PrimaryActivityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          PrimaryActivity existingActivity = (await GetAsync(a => (a.ActivityId == activityInformation.Id) && (a.OwnerIdentityId == OwnerIdentityId))).FirstOrDefault();
          if (existingActivity != null)
          {
            // First, we check whether the activity should be migrated to closer proximity server.
            GpsLocation oldLocation = existingActivity.GetLocation();
            GpsLocation newLocation = new GpsLocation(activityInformation.Latitude, activityInformation.Longitude);
            bool locationChanged = !oldLocation.Equals(newLocation);

            bool error = false;
            if (locationChanged)
            {
              StrongBox<byte[]> nearestServerId = new StrongBox<byte[]>(null);
              List<byte[]> ignoreServerIds = new List<byte[]>(UpdateRequest.IgnoreServerIds.Select(i => i.ToByteArray()));
              if (!await unitOfWork.NeighborRepository.IsServerNearestToLocationAsync(newLocation, ignoreServerIds, nearestServerId, ProxMessageBuilder.ActivityMigrationDistanceTolerance))
              {
                if (nearestServerId.Value != null)
                {
                  migrateActivity = true;
                  log.Debug("Activity's new location is outside the reach of this proximity server, the activity will be deleted from the database.");
                }
                else error = true;
              }
              // else No migration needed
            }

            if (!error)
            {
              // If it should not be migrated, we update the activity in our database.
              if (!migrateActivity)
              {
                bool propagateChange = false;
                if (!UpdateRequest.NoPropagation)
                {
                  PrimaryActivity updatedActivity = ActivityBase.FromActivityInformation<PrimaryActivity>(activityInformation);
                  ActivityChange changes = existingActivity.CompareChangeTo(updatedActivity);
                  
                  // If only changes in the activity are related to location or expiration time, the activity update is not propagated to the neighborhood.
                  propagateChange = (changes & ~(ActivityChange.LocationLatitude | ActivityChange.LocationLongitude | ActivityChange.ExpirationTime)) != 0;
                }

                existingActivity.CopyFromActivityInformation(activityInformation);

                Update(existingActivity);

                if (propagateChange)
                {
                  // The activity has to be propagated to all our followers we create database actions that will be processed by dedicated thread.
                  signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddActivityFollowerActionsAsync(NeighborhoodActionType.ChangeActivity, existingActivity.ActivityId, existingActivity.OwnerIdentityId);
                }
                else log.Trace("Change of activity ID {0}, owner identity ID '{1}' won't be propagated to neighborhood.", existingActivity.ActivityId, existingActivity.OwnerIdentityId);

                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
                success = true;
              }
              // else this is handled below separately, out of locked section
            }
            // else Internal error
          }
          else
          {
            log.Debug("Activity ID {0}, owner identity ID '{1}' does not exist.", activityInformation.Id, OwnerIdentityId);
            res = Iop.Shared.Status.ErrorNotFound;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);
      }


      if (migrateActivity)
      {
        // If activity should be migrated, we delete it from our database and inform our neighbors.
        StrongBox<bool> notFound = new StrongBox<bool>(false);
        if (await DeleteAndPropagateAsync(activityInformation.Id, OwnerIdentityId, notFound))
        {
          if (!notFound.Value)
          {
            // The activity was deleted from the database and this change will be propagated to the neighborhood.
            log.Debug("Update rejected, activity ID {0}, owner identity ID '{1}' deleted.", activityInformation.Id, OwnerIdentityId);
            res = Iop.Shared.Status.ErrorRejected;
          }
          else
          {
            // Activity of given ID not found among activities created by the client.
            res = Iop.Shared.Status.ErrorNotFound;
          }
        }
      }

      if (success)
      {
        // Send signal to neighborhood action processor to process the new series of actions.
        if (signalNeighborhoodAction)
        {
          Network.NeighborhoodActionProcessor neighborhoodActionProcessor = (Network.NeighborhoodActionProcessor)Base.ComponentDictionary[Network.NeighborhoodActionProcessor.ComponentName];
          neighborhoodActionProcessor.Signal();
        }

        res = Iop.Shared.Status.Ok;
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Deletes an existing activity from the database. Then a new neighborhood action is created to propagate the change to the neighborhood.
    /// </summary>
    /// <param name="ActivityId">Identifier of the activity to delete.</param>
    /// <param name="OwnerIdentityId">Network ID of the client who owns the activity.</param>
    /// <param name="NotFound">If the function fails, this is set to true if the reason of the failure is that the activity was not found.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> DeleteAndPropagateAsync(uint ActivityId, byte[] OwnerIdentityId, StrongBox<bool> NotFound)
    {
      log.Trace("(ActivityId:{0},OwnerIdentityId:'{1}')", ActivityId, OwnerIdentityId.ToHex());

      bool res = false;

      bool success = false;
      bool signalNeighborhoodAction = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.PrimaryActivityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          PrimaryActivity existingActivity = (await GetAsync(a => (a.ActivityId == ActivityId) && (a.OwnerIdentityId == OwnerIdentityId))).FirstOrDefault();
          if (existingActivity != null)
          {
            Delete(existingActivity);

            // The activity has to be propagated to all our followers we create database actions that will be processed by dedicated thread.
            signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddActivityFollowerActionsAsync(NeighborhoodActionType.RemoveActivity, existingActivity.ActivityId, existingActivity.OwnerIdentityId);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else
          {
            log.Debug("Activity ID {0}, owner identity ID '{1}' does not exist.", ActivityId, OwnerIdentityId);
            NotFound.Value = true;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      if (success)
      {
        // Send signal to neighborhood action processor to process the new series of actions.
        if (signalNeighborhoodAction)
        {
          Network.NeighborhoodActionProcessor neighborhoodActionProcessor = (Network.NeighborhoodActionProcessor)Base.ComponentDictionary[Network.NeighborhoodActionProcessor.ComponentName];
          neighborhoodActionProcessor.Signal();
        }

        res = true;
      }


      log.Trace("(-):{0}", res);
      return res;
    }

  }
}
