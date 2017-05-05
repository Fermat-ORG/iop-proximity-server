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
    /// Updates an existing activity in the database. Then a new neighborhood action is created to propagate the changes to the neighborhood.
    /// </summary>
    /// <param name="UpdateRequest">Update request received from the client.</param>
    /// <param name="OwnerIdentityId">Network ID of the client who requested the update.</param>
    /// <returns>Status.Ok if the function succeeds, 
    /// Status.ErrorNotFound if the activity to update does not exist,
    /// Status.ErrorInvalidValue if the new activity's start time is greater than its new expiration time,
    /// Status.ErrorInternal otherwise.</returns>
    public async Task<Iop.Shared.Status> UpdateAndPropagateAsync(UpdateActivityRequest UpdateRequest, byte[] OwnerIdentityId)
    {
      log.Trace("(UpdateRequest.Id:{0},OwnerIdentityId:'{1}')", UpdateRequest.Id, OwnerIdentityId.ToHex());

      Iop.Shared.Status res = Iop.Shared.Status.ErrorInternal;

      bool success = false;
      bool signalNeighborhoodAction = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.PrimaryActivityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          PrimaryActivity existingActivity = (await GetAsync(a => (a.ActivityId == UpdateRequest.Id) && (a.OwnerIdentityId == OwnerIdentityId))).FirstOrDefault();
          if (existingActivity != null)
          {
            if (UpdateRequest.SetStartTime)
              existingActivity.StartTime = ProtocolHelper.UnixTimestampMsToDateTime(UpdateRequest.StartTime).Value;

            if (UpdateRequest.SetExpirationTime)
              existingActivity.ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(UpdateRequest.ExpirationTime).Value;


            bool expirationTimeValid = existingActivity.StartTime <= existingActivity.ExpirationTime;
            if (expirationTimeValid)
            {
              if (UpdateRequest.SetLocation)
              {
                GpsLocation activityLocation = new GpsLocation(UpdateRequest.Latitude, UpdateRequest.Longitude);
                existingActivity.LocationLatitude = activityLocation.Latitude;
                existingActivity.LocationLongitude = activityLocation.Longitude;
              }

              if (UpdateRequest.SetPrecision)
                existingActivity.PrecisionRadius = UpdateRequest.Precision;

              if (UpdateRequest.SetExtraData)
                existingActivity.ExtraData = UpdateRequest.ExtraData;

              Update(existingActivity);


              // The activity has to be propagated to all our followers we create database actions that will be processed by dedicated thread.
              signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddActivityFollowerActionsAsync(NeighborhoodActionType.ChangeActivity, existingActivity.ActivityId, existingActivity.OwnerIdentityId, UpdateRequest.ToString());

              await unitOfWork.SaveThrowAsync();
              transaction.Commit();
              success = true;
            }
            else 
            {
              log.Debug("Activity new start time {0} is greater than its new expiration time {1}.", existingActivity.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), existingActivity.ExpirationTime.ToString("yyyy-MM-dd HH:mm:ss"));
              res = Iop.Shared.Status.ErrorInvalidValue;
            }
          }
          else
          {
            log.Debug("Activity ID {0}, owner identity ID '{1}' does not exist.", UpdateRequest.Id, OwnerIdentityId);
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
