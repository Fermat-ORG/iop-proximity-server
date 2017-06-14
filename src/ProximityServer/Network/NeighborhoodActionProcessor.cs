﻿using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;
using ProximityServer.Data;
using ProximityServer.Data.Models;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network;
using Iop.Proximityserver;
using ProximityServer.Kernel;
using Iop.Shared;
using System.Runtime.CompilerServices;

namespace ProximityServer.Network
{
  /// <summary>
  /// This component executes planned actions from NeighborhoodAction database table, which forms a queue of actions 
  /// related to neighbors and followers. Actions that go in the same directions using the same target server 
  /// must be executed in serial manner. Actions that work with different target servers or that goes in opposite 
  /// directions can be executed in parallel.
  /// <para>
  /// A timer is installed that causes the action queue to be checked every so often (see CheckActionListTimerInterval).
  /// In combination to the timer, other components do call Signal method to enforce an immediate check of the queue.
  /// </para>
  /// </summary>
  public class NeighborhoodActionProcessor : Component
  {
#warning Regtest needed - neighborhood initialization process takes too long -> server retries again later
#warning Regtest needed - shared activity refresh and expiration
#warning Regtest needed - update failed soft -> server retries again
#warning Regtest needed - update failed hard -> follower is deleted
#warning Regtest needed - update failed hard -> follower is deleted; server won't send updates to the follower -> neighbor expires on the follower -> LOC refreshes -> neighbor relationship established again
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Network.NeighborhoodActionProcessor";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer." + ComponentName);



    /// <summary>Proximity server's primary interface port.</summary>
    private uint primaryPort;

    /// <summary>Proximity server neighbors interface port.</summary>
    private uint neighborPort;


    /// <summary>Lock to protect access to actionExecutorCounter.</summary>
    private object actionExecutorLock = new object();

    /// <summary>Number of action executions in progress.</summary>
    private int actionExecutorCounter = 0;


    /// <summary>Event that is set when DelayedStartupThread is not running.</summary>
    private ManualResetEvent delayedStartupThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for delated initialization of this component.</summary>
    private Thread delayedStartupThread;



    /// <summary>
    /// Initializes the component.
    /// </summary>
    public NeighborhoodActionProcessor() :
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        delayedStartupThread = new Thread(new ThreadStart(DelayedStartupThread));
        delayedStartupThread.Start();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      bool done = false;
      int counter = 0;
      log.Trace("Waiting for action executors to finish.");
      while (!done)
      {
        int actionCounter = 0;
        lock (actionExecutorLock)
        {
          actionCounter = actionExecutorCounter;
        }

        done = actionCounter == 0;
        if (!done)
        {
          log.Debug("Still {0} actions in progress.", actionCounter);

          counter++;
          if (counter >= 65)
          {
            log.Error("Waiting for action executors took too long, terminating.");
            break;
          }

          Thread.Sleep(1000);
        }
      }

      log.Info("(-)");
    }


    /// <summary>
    /// Thread that is responsible for late initialization of neighborhood action processor.
    /// This is needed because in order for the proximity server to run, it must know its location.
    /// The server component thus waits until the location is received from LOC server, 
    /// but there is no guarantee LOC server is running when the proximity server starts.
    /// Neighborhood action processor needs server component to be ready before its own initialization 
    /// can be completed.
    /// </summary>
    private async void DelayedStartupThread()
    {
      LogDiagnosticContext.Start();

      log.Info("()");

      delayedStartupThreadFinished.Reset();

      try
      {
        log.Trace("Waiting for server component initialization ...");
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        bool serverInit = false;
        try
        {
          serverInit = await serverComponent.DelayedStartupCompletedEvent.Task;
        }
        catch
        {
          // Catch cancellation exception.
          log.Trace("Shutdown detected.");
        }

        if (serverInit)
        {
          log.Debug("Server initialization completed.");
          List<TcpRoleServer<IncomingClient>> roleServers = serverComponent.GetRoleServers();
          foreach (TcpRoleServer<IncomingClient> roleServer in roleServers)
          {
            if ((roleServer.Roles & (uint)ServerRole.Primary) != 0)
              primaryPort = (uint)roleServer.EndPoint.Port;

            if ((roleServer.Roles & (uint)ServerRole.Neighbor) != 0)
              neighborPort = (uint)roleServer.EndPoint.Port;
          }

          RegisterCronJobs();
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
        await Task.Delay(5000);
        throw e;
      }

      delayedStartupThreadFinished.Set();

      log.Info("(-)");

      LogDiagnosticContext.Stop();
    }



    /// <summary>
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Checks if there are any neighborhood actions to process.
        { new CronJob() { Name = "checkNeighborhoodActionList", StartDelay = 20 * 1000, Interval = 20 * 1000, HandlerAsync = CronJobHandlerCheckNeighborhoodActionListAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "checkNeighborhoodActionList" cron job.
    /// </summary>
    public async void CronJobHandlerCheckNeighborhoodActionListAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await CheckActionListAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Signals check action list event.
    /// </summary>
    public void Signal()
    {
      log.Trace("()");

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.SignalEvent("checkNeighborhoodActionList");

      log.Trace("(-)");
    }



    /// <summary>
    /// Loads list of actions from the database and executes actions that can be executed.
    /// </summary>
    public async Task CheckActionListAsync()
    {
      log.Trace("()");

      try
      {
        while (!ShutdownSignaling.IsShutdown)
        {
          NeighborhoodAction action = await LoadNextActionAsync();
          if (action != null)
          {
            ProcessActionAsync(action);
          }
          else
          {
            log.Trace("No neighborhood action to process.");
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
        await Task.Delay(5000);
        throw e;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Processes a neighborhood action and either removes it from the database or schedules its retry.
    /// </summary>
    /// <param name="Action">Action to process.</param>
    private async void ProcessActionAsync(NeighborhoodAction Action)
    {
      LogDiagnosticContext.Start();

      log.Trace("(Action.Id:{0})", Action.Id);

      bool shutdown = false;
      lock (actionExecutorLock)
      {
        shutdown = ShutdownSignaling.IsShutdown;
        if (!shutdown)
          actionExecutorCounter++;
      }

      if (!shutdown)
      {
        bool removeAction = await ExecuteActionAsync(Action);
        if (removeAction)
        {
          // If the action was processed successfully, we remove it from the database.
          // Otherwise its ExecuteAfter flag is set to the future and the action will 
          // attempt to be processed again later.
          if (await RemoveActionAsync(Action.Id))
            log.Info("Action ID {0} removed from the database.", Action.Id);

          // We signal the event to have the action queue checked, because this action 
          // that just finished might have blocked another action in the queue.
          Signal();
        }
        else log.Info("Processing of action ID {0} failed.", Action.Id);


        lock (actionExecutorLock)
        {
          actionExecutorCounter--;
        }
      }
      else log.Trace("Shutdown detected.");

      log.Trace("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Loads next neighborhood action to process from the database.
    /// <para>
    /// Next action to process is an action that is not blocked by ExecuteAfter condition and has the lowest ID.
    /// An action is blocked by ExecuteAfter condition if its ExecuteAfter is greater than current time, 
    /// or if any action with lower ID and the same neighbor/follower ID has ExecuteAfter greater than current time.
    /// </para>
    /// </summary>
    /// <returns>Next action to process or null if there is no action to process now.</returns>
    private async Task<NeighborhoodAction> LoadNextActionAsync()
    {
      log.Trace("()");

      NeighborhoodAction res = null;
      
      int actionsInProgress = 0;
      lock (actionExecutorLock)
      {
        actionsInProgress = actionExecutorCounter;
      }

      // Never execute more than 5 actions at once.
      if (actionsInProgress >= 5)
      {
        log.Trace("(-)[TOO_MANY_ACTIONS]:null");
        return res;
      }


      // Network IDs of servers which activity actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> activityActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);

      // Network IDs of servers which server actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> serverActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DateTime now = DateTime.UtcNow;
        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          NeighborhoodAction actionToProcess = null;
          List<NeighborhoodAction> actions = (await unitOfWork.NeighborhoodActionRepository.GetAsync(null, q => q.OrderBy(a => a.Id))).ToList();
          foreach (NeighborhoodAction action in actions)
          {
            bool isActivityAction = action.IsActivityAction();

            bool isLocked = (isActivityAction && activityActionsLockedIds.Contains(action.ServerId))
              || (!isActivityAction && serverActionsLockedIds.Contains(action.ServerId));

            log.Trace("Action ID {0}, action type is {1}, isActivityAction is {2}, isLocked is {3}, execute after time is {4}.", action.Id, action.Type, isActivityAction, isLocked, action.ExecuteAfter != null ? action.ExecuteAfter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null");

            if (!isLocked)
            {
              if ((action.ExecuteAfter == null) || (action.ExecuteAfter <= now))
              {
                actionToProcess = action;
                break;
              }
              else
              {
                log.Trace("Action ID {0} can't be executed because its ExecuteAfter ({1}) is in the future.", action.Id, action.ExecuteAfter.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                if (isActivityAction) activityActionsLockedIds.Add(action.ServerId);
                else serverActionsLockedIds.Add(action.ServerId);
              }
            }
            else log.Trace("Action ID {0} can't be executed because it is locked by other action on the server ID '{1}'.", action.Id, action.ServerId.ToHex());
          }

          if (actionToProcess != null)
          {
            // If there is action to process, we set its ExecuteAfter value to the future, so that it is not selected next time 
            // this function is executed again before the action is processed.
            //
            // We MUST make sure, the action is processed before ExecuteAfter, otherwise it will be picked up again for processing!
            actionToProcess.ExecuteAfter = now.AddSeconds(600);
            unitOfWork.NeighborhoodActionRepository.Update(actionToProcess);
            await unitOfWork.SaveThrowAsync();

            res = actionToProcess;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }
      
      if (res != null) log.Trace("(-):*.Id={0}", res.Id);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Removes an action from the database.
    /// </summary>
    /// <param name="ActionId">Identifier of the action to remove.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> RemoveActionAsync(int ActionId)
    {
      log.Trace("()");

      bool res = false;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        res = await unitOfWork.NeighborhoodActionRepository.DeleteAsync(ActionId);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Executes a neighborhood action.
    /// </summary>
    /// <param name="Action">Action to execute.</param>
    /// <returns>true if the action should be removed, false otherwise.</returns>
    private async Task<bool> ExecuteActionAsync(NeighborhoodAction Action)
    {
      log.Trace("(Action.Id:{0},Action.Type:{1})", Action.Id, Action.Type);

      bool res = false;
      
      switch (Action.Type)
      {
        case NeighborhoodActionType.AddNeighbor:
          res = await NeighborhoodInitializationProcessAsync(Action.ServerId, Action.ExecuteAfter.Value, Action.Id);
          break;

        case NeighborhoodActionType.RemoveNeighbor:
          res = await NeighborhoodRemoveNeighborAsync(Action.ServerId, Action.Id);
          break;

        case NeighborhoodActionType.StopNeighborhoodUpdates:
          res = await NeighborhoodRequestStopUpdatesAsync(Action.ServerId, Action.AdditionalData);
          break;

        case NeighborhoodActionType.AddActivity:
        case NeighborhoodActionType.ChangeActivity:
        case NeighborhoodActionType.RemoveActivity:
        case NeighborhoodActionType.RefreshNeighborStatus:
          res = await NeighborhoodActivityUpdateAsync(Action.ServerId, Action.TargetActivityId, Action.TargetActivityOwnerId, Action.Type, Action.AdditionalData, Action.Id);
          break;

        case NeighborhoodActionType.InitializationProcessInProgress:
          // If InitializationProcessInProgress action can be executed, it means the follower finished the initialization process.
          // It is now safe to remove the action and proceed with the queue.
          res = true;
          break;

        default:
          log.Error("Invalid action type {0}.", Action.Type);
          break;
      }

      if (res) log.Debug("Action ID {0} processed successfully.", Action.Id);
      
      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to a neighbor proximity server and performs a neighborhood initialization process,
    /// which means that it asks the neighbor to share its proximity server database.
    /// </summary>
    /// <param name="NeighborId">Network identifer of the neighbor server.</param>
    /// <param name="MustFinishBefore">Time before which the initialization process must be completed.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodInitializationProcessAsync(byte[] NeighborId, DateTime MustFinishBefore, int CurrentActionId)
    {
      log.Trace("(NeighborId:'{0}',MustFinishBefore:{1},CurrentActionId:{2})", NeighborId.ToHex(), MustFinishBefore.ToString("yyyy-MM-dd HH:mm:ss"), CurrentActionId);

      bool res = false;

      // We MUST finish before MustFinishBefore time, so to be sure, we will terminate the process 
      // if we find ourselves running 90 seconds before that time. We have 60 seconds read timeout 
      // on the stream, so in the worst case, we should have 30 seconds reserve.
      DateTime processStart = DateTime.UtcNow;
      DateTime safeDeadline = MustFinishBefore.AddSeconds(-90);
      log.Trace("Setting up safe deadline to {0}.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));

      bool deleteNeighbor = false;
      bool resetNeighborPort = false;

      StrongBox<bool> notFound = new StrongBox<bool>(false);
      IPEndPoint endPoint = await GetNeighborServerContactAsync(NeighborId, notFound);
      if (endPoint != null)
      {
        using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
        {
          Dictionary<string, NeighborActivity> activityDatabase = new Dictionary<string, NeighborActivity>(StringComparer.Ordinal);
          client.Context = activityDatabase;

          bool connected = await client.ConnectAndVerifyIdentityAsync();
          if (!connected)
          {
            log.Debug("Failed to connect to neighbor ID '{0}' on address '{1}'. Trying to get new value of its neighbor port from its primaryPort.", NeighborId.ToHex(), endPoint);
            IPEndPoint newEndPoint = await GetNeighborServerContactAsync(NeighborId, notFound, true);
            if (newEndPoint != null)
            {
              endPoint = newEndPoint;
              client.SetRemoteEndPoint(endPoint);
              connected = await client.ConnectAndVerifyIdentityAsync();
            }
            else log.Debug("Failed to get neighbor port from primaryPort of neighbor ID '{0}'.", NeighborId.ToHex());
          }

          if (connected)
          {
            if (client.MatchServerId(NeighborId))
            {
              if (await client.StartNeighborhoodInitializationAsync(primaryPort, neighborPort))
              {
                bool error = false;
                bool done = false;
                while (!done && !error)
                {
                  if (DateTime.UtcNow > safeDeadline)
                  {
                    log.Warn("Intialization process took too long, the safe deadline {0} has been reached, will retry later.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));
                    error = true;
                    break;
                  }

                  ProxProtocolMessage requestMessage = await client.ReceiveMessageAsync();
                  if (requestMessage != null)
                  {
                    ProxProtocolMessage responseMessage = client.MessageBuilder.CreateErrorProtocolViolationResponse(requestMessage);
                    try
                    {

                      if (requestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request)
                      {
                        Request request = requestMessage.Request;
                        if (request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
                        {
                          ConversationRequest conversationRequest = request.ConversationRequest;
                          log.Trace("Conversation request type '{0}' received.", conversationRequest.RequestTypeCase);
                          switch (conversationRequest.RequestTypeCase)
                          {
                            case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedActivityUpdate:
                              responseMessage = ProcessMessageNeighborhoodSharedActivityUpdateRequest(client, requestMessage);
                              break;

                            case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                              responseMessage = await ProcessMessageFinishNeighborhoodInitializationRequestAsync(client, requestMessage);
                              done = true;
                              break;

                            default:
                              log.Error("Invalid conversation request type '{0}' received, will retry later.", conversationRequest.RequestTypeCase);
                              error = true;
                              break;
                          }
                        }
                        else
                        {
                          log.Warn("Invalid conversation type '{0}' received, will retry later.", request.ConversationTypeCase);
                          error = true;
                        }
                      }
                      else
                      {
                        log.Warn("Invalid message type '{0}' received, expected Request, will retry later.", requestMessage.MessageTypeCase);
                        error = true;
                      }
                    }
                    catch (Exception e)
                    {
                      log.Error("Exception occurred: {0}", e.ToString());
                      error = true;
                    }


                    // Send response to neighbor.
                    if (!await client.SendMessageAsync(responseMessage))
                    {
                      log.Warn("Unable to send response to neighbor, will retry later.");
                      error = true;
                      break;
                    }

                    if (client.ForceDisconnect)
                    {
                      error = true;
                      break;
                    }
                  }
                  else
                  {
                    log.Warn("Connection has been terminated, initialization process has not been completed, will retry later.");
                    error = true;
                  }
                }

                res = !error;
              }
              else
              {
                if (client.LastResponseStatus == Status.ErrorBusy)
                {
                  // Target server is busy at the moment and does not want to talk to us about neighborhood initialization.
                  log.Debug("Neighbor ID '{0}' is busy now, let's try later.", NeighborId.ToHex());
                }
                else if (client.LastResponseStatus == Status.ErrorBadRole)
                {
                  log.Info("Neighbor ID '{0}' rejected start of initialization process to bad port usage (port {1} was used), reseting neighbor port for this follower, will retry later.", NeighborId.ToHex(), endPoint.Port);
                  resetNeighborPort = true;
                }
                else log.Warn("Starting the intialization process failed with neighbor ID '{0}', will retry later.", NeighborId.ToHex());
              }
            }
            else
            {
              log.Warn("Server identity differs from expected ID '{0}', deleting this neighbor.", NeighborId.ToHex());
              deleteNeighbor = true;
            }
          }
          else log.Warn("Unable to initiate conversation with neighbor ID '{0}' on address {1}, will retry later.", NeighborId.ToHex(), endPoint);
        }
      }
      else
      {
        if (notFound.Value == true)
        {
          // This means that the neighbor does not exists, hence we return true to delete this action from the queue.
          res = true;
        }
        else
        {
          log.Warn("Unable to find neighbor ID '{0}' IP and port information, will retry later.", NeighborId.ToHex());
          //
          // If we are here it means that neighborPort of this neighbor was reset before because we received ERROR_BAD_ROLE from the old neighborPort port.
          // Alternatively, we could have receive update from LOC server changing the primary port of the neighbor, in which case we expect neighbor port to change as well.
          // This could happen if the neighbor server changed its ports and the port we know is not used as neighborPort anymore.
          // Then we attempted to connect to its primaryPort to get new value for its neighborPort and this failed as well,
          // which means that the server was offline.
          //
          // The solution here is to wait. All actions to this neighbor will be blocked and this particular action will be retried later again.
          // If the neighbor gets online and we succeed, actions for it will be unblocked again. Otherwise it will be eventually deleted 
          // when it is unresponsive for long, either we will get notified from LOC server, or we find out on ourselves in Database.CheckExpiredNeighborsAsync().
          //
        }
      }

      if (deleteNeighbor)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          if (await unitOfWork.NeighborRepository.DeleteNeighborAsync(NeighborId, CurrentActionId))
          {
            // Neighbor was deleted from the database, all its actions should be deleted 
            // except for this one that is currently being executed.
            // Nothing more to do here.
            log.Info("Neighbor ID '{0}' has been deleted.", NeighborId.ToHex());
            res = true;
          }
          else log.Error("Unable to delete neighbor ID '{0}' from database.", NeighborId.ToHex());
        }
      }

      if (resetNeighborPort)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          if (await unitOfWork.NeighborRepository.ResetNeighborPortAsync(NeighborId)) log.Info("Neighbor port of neighbor ID '{0}' has been reset.", NeighborId.ToHex());
          else log.Error("Unable to reset neighbor port of neighbor ID '{0}'.", NeighborId.ToHex());
        }
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains IP address and neighbor port from neighbor server's network identifier.
    /// </summary>
    /// <param name="NeighborId">Network identifer of the neighbor server.</param>
    /// <param name="NotFound">If the function fails, this is set to true if the reason for failure was that the neighbor was not found.</param>
    /// <param name="IgnoreDbPortValue">If set to true, the function will ignore NeighborPort value of the neighbor even if it is set in the database 
    /// and will contact the neighbor on its primary port and then update NeighborPort in the database, if it successfully gets its value.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetNeighborServerContactAsync(byte[] NeighborId, StrongBox<bool> NotFound, bool IgnoreDbPortValue = false)
    {
      log.Trace("(NeighborId:'{0}',IgnoreDbPortValue:{1})", NeighborId.ToHex(), IgnoreDbPortValue);

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        res = await unitOfWork.NeighborRepository.GetServerContactAsync(NeighborId, NotFound, IgnoreDbPortValue);
      }

      log.Trace("(-):{0}", res != null ? res.ToString() : "null");
      return res;
    }


    /// <summary>
    /// Obtains IP address and neighbor port from follower server's network identifier.
    /// </summary>
    /// <param name="FollowerId">Network identifer of the follower server.</param>
    /// <param name="NotFound">If the function fails, this is set to true if the reason for failure was that the follower was not found.</param>
    /// <param name="IgnoreDbPortValue">If set to true, the function will ignore NeighborPort value of the follower even if it is set in the database 
    /// and will contact the follower on its primary port and then update NeighborPort in the database, if it successfully gets its value.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetFollowerServerContactAsync(byte[] FollowerId, StrongBox<bool> NotFound, bool IgnoreDbPortValue = false)
    {
      log.Trace("(FollowerId:'{0}',IgnoreDbPortValue:{1})", FollowerId.ToHex(), IgnoreDbPortValue);

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        res = await unitOfWork.FollowerRepository.GetServerContactAsync(FollowerId, NotFound, IgnoreDbPortValue);
      }

      log.Trace("(-):{0}", res != null ? res.ToString() : "null");
      return res;
    }


    /// <summary>
    /// Connects to a proximity server's primary port and attempts to get information on which port is a certain role served. 
    /// </summary>
    /// <param name="IpAddress">IP address of the proximity server.</param>
    /// <param name="PrimaryPort">Primary interface port of the proximity server.</param>
    /// <param name="Role">Role which port is being searched.</param>
    /// <returns>Port on which the given role is being served, or 0 if the port is not known.</returns>
    public async Task<int> GetServerRolePortFromPrimaryPort(IPAddress IpAddress, int PrimaryPort, ServerRoleType Role)
    {
      log.Trace("(IpAddress:{0},PrimaryPort:{1},Role:{2})", IpAddress, PrimaryPort, Role);

      int res = 0;
      using (OutgoingClient client = new OutgoingClient(new IPEndPoint(IpAddress, PrimaryPort), false, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
      {
        if (await client.ConnectAsync())
        {
          ListRolesResponse listRoles = await client.SendListRolesRequest();
          if (listRoles != null)
          {
            foreach (Iop.Proximityserver.ServerRole serverRole in listRoles.Roles)
            {
              if (serverRole.Role == Role)
              {
                res = (int)serverRole.Port;
                break;
              }
            }
          }
          else log.Debug("Unable to get list of server roles from server {0}.", client.RemoteEndPoint);
        }
        else log.Debug("Unable to connect to {0}.", client.RemoteEndPoint);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// A neighbor expired and needs to be removed from a neighborhood. We delete it and all its shared activities from our database.
    /// </summary>
    /// <param name="NeighborId">Network identifier of the former neighbor server.</param>
    /// <param name="CurrentActionId">Identifier of the action being executed.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodRemoveNeighborAsync(byte[] NeighborId, int CurrentActionId)
    {
      log.Trace("(NeighborId:'{0}',CurrentActionId:{1})", NeighborId.ToHex(), CurrentActionId);

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        bool unlock = false;
        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        try
        {
          Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NetworkId == NeighborId, null, true)).FirstOrDefault();
          if (neighbor != null)
          {
            string neighborInfo = JsonConvert.SerializeObject(neighbor);

            // Delete neighbor completely.
            res = await unitOfWork.NeighborRepository.DeleteNeighborAsync(NeighborId, CurrentActionId);

            // Add action that will contact the neighbor and ask it to stop sending updates.
            // Note that the neighbor information will be deleted by the time this action 
            // is executed and this is why we have to fill in AdditionalData.
            NeighborhoodAction stopUpdatesAction = new NeighborhoodAction()
            {
              ServerId = NeighborId,
              Type = NeighborhoodActionType.StopNeighborhoodUpdates,
              TargetActivityId = 0,
              TargetActivityOwnerId = null,  
              ExecuteAfter = DateTime.UtcNow,
              Timestamp = DateTime.UtcNow,
              AdditionalData = neighborInfo
            };

            await unitOfWork.AcquireLockAsync(lockObject);
            unlock = true;

            await unitOfWork.NeighborhoodActionRepository.InsertAsync(stopUpdatesAction);
            await unitOfWork.SaveThrowAsync();
          }
          else
          {
            log.Warn("Neighbor ID '{0}' not found, probably it was deleted already.", NeighborId.ToHex());
            res = true;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (unlock) unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Proximity server removed neighbor server from its neighborhood and needs to contact it and ask it to stop sending updates.
    /// </summary>
    /// <param name="NeighborId">Network identifier of the former neighbor server.</param>
    /// <param name="NeighborInfo">Serialized neighbor information.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodRequestStopUpdatesAsync(byte[] NeighborId, string NeighborInfo)
    {
      log.Trace("(NeighborId:'{0}',NeighborInfo:'{1}')", NeighborId.ToHex(), NeighborInfo);

      bool res = false;

      Neighbor neighbor = null;
      IPAddress ipAddress = IPAddress.Any;
      try
      {
        neighbor = JsonConvert.DeserializeObject<Neighbor>(NeighborInfo);
        ipAddress = new IPAddress(neighbor.IpAddress);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (neighbor == null)
      {
        // Delete this action, it is corrupted.
        log.Trace("(-)[ERROR]:true");
        return true;
      }

      bool newNeighborPortObtained = false;
      int neighborPort = neighbor.NeighborPort != null ? neighbor.NeighborPort.Value : 0;

      if (neighborPort == 0)
      {
        neighborPort = await GetServerRolePortFromPrimaryPort(ipAddress, neighbor.PrimaryPort, ServerRoleType.Neighbor);
        newNeighborPortObtained = true;
      }

      if (neighborPort != 0)
      {
        IPEndPoint endPoint = new IPEndPoint(ipAddress, neighborPort);
        using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
        {
          bool connected = await client.ConnectAndVerifyIdentityAsync();
          if (!connected && !newNeighborPortObtained)
          {
            log.Debug("Failed to connect to neighbor ID '{0}' on address '{1}'. Trying to get new value of its srNeighborPort from its primaryPort.", NeighborId.ToHex(), endPoint);
            int newSrNeighborPort = await GetServerRolePortFromPrimaryPort(ipAddress, neighbor.PrimaryPort, ServerRoleType.Neighbor);
            if (newSrNeighborPort != 0)
            {
              client.SetRemoteEndPoint(new IPEndPoint(ipAddress, newSrNeighborPort));
              connected = await client.ConnectAndVerifyIdentityAsync();
            }
            else log.Debug("Failed to get neighborPort from primaryPort of neighbor ID '{0}'.", NeighborId.ToHex());
          }

          if (connected)
          {
            if (client.MatchServerId(NeighborId))
            {
              ProxProtocolMessage requestMessage = client.MessageBuilder.CreateStopNeighborhoodUpdatesRequest();
              if (!await client.SendStopNeighborhoodUpdatesAsync(requestMessage))
              {
                if (client.LastResponseStatus == Status.ErrorNotFound) log.Info("Neighbor ID '{0}' does not register us as followers.", NeighborId.ToHex());
                else log.Warn("Sending update to neighbor ID '{0}' failed, error status {1}.", NeighborId.ToHex(), client.LastResponseStatus);
              }
            }
            else log.Warn("Server identity differs from expected ID '{0}'.", NeighborId.ToHex());
          }
          else log.Info("Unable to initiate conversation with neighbor ID '{0}' on address '{1}'.", NeighborId.ToHex(), endPoint);
        }
      }
      else log.Warn("Unable to find neighbor ID '{0}' IP and port information.", NeighborId.ToHex());

      // In case of failure, we will not try again. The neighbor is deleted from our database 
      // and hopefully will not send us any more updates. If it does, we will reject them.
      res = true;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to a follower proximity server and propagates an update regarding changes in an activity.
    /// </summary>
    /// <param name="FollowerId">Network identifer of the follower server.</param>
    /// <param name="ActivityId">Identifier of the activity that has changed.</param>
    /// <param name="OwnerIdentityId">Identifier of the owner's identity of the activity.</param>
    /// <param name="ActionType">Type of activity change action.</param>
    /// <param name="AdditionalData">Additional action data or null if action has no additional data.</param>
    /// <param name="ActionId">Identifier of the neighborhood action currently being executed.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodActivityUpdateAsync(byte[] FollowerId, uint ActivityId, byte[] OwnerIdentityId, NeighborhoodActionType ActionType, string AdditionalData, int ActionId)
    {
      log.Trace("(FollowerId:'{0}',ActivityId:{1},OwnerIdentityId:'{2}',ActionType:{3},AdditionalData:'{4}',ActionId:{5})", FollowerId.ToHex(), ActivityId, OwnerIdentityId.ToHex(), ActionType, AdditionalData, ActionId);

      bool res = false;

      // First, we find the target server, to which we want to send the update.
      StrongBox<bool> notFound = new StrongBox<bool>(false);
      IPEndPoint endPoint = await GetFollowerServerContactAsync(FollowerId, notFound);
      if (endPoint != null)
      {
        // Then we construct the update message.
        SharedActivityUpdateItem updateItem = null;
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          try
          {
            switch (ActionType)
            {
              case NeighborhoodActionType.AddActivity:
                {
                  PrimaryActivity activity = (await unitOfWork.PrimaryActivityRepository.GetAsync(i => (i.ActivityId == ActivityId) && (i.OwnerIdentityId == OwnerIdentityId))).FirstOrDefault();
                  if (activity != null)
                  {
                    updateItem = new SharedActivityUpdateItem()
                    {
                      Add = new SharedActivityAddItem()
                      {
                        SignedActivity = activity.ToSignedActivityInformation()
                      }
                    };
                  }
                  else
                  {
                    log.Warn("Unable to find primary activity ID {0}, owner identity ID '{1}'.", ActivityId, OwnerIdentityId.ToHex());
                    //
                    // This happens when the activity was deleted before we managed to send the update to the follower. 
                    // 
                    // We have to remove this action from the queue because otherwise it would block all actions 
                    // to this follower forever. However, the problem is that the follower is not aware of this 
                    // activity and we may have change or remove updates in the action queue. Thus if we just 
                    // returned true here, the subsequent actions could fail because of this inconsistency.
                    //
                    // We solve this problem using a hack, which is creating an artifical activity for this 
                    // (no longer existing) activity. We propagate that artifical activity to the follower 
                    // and so the subsequent activity removal update action can happen flawlessly.
                    //
                    // We also skip all change activity update actions if there are any.
                    //
                    // Alternatively, we could process the action queue, either here or during removal of the activity. 
                    // However, such a solution is complex and error prone. This seems to be by far the easiest way 
                    // to deal with this somewhat rare case.
                    //
                    byte[] identityPublicKey = AdditionalData.FromHex();
                    SharedActivityAddItem item = new SharedActivityAddItem()
                    {
                      SignedActivity = new SignedActivityInformation()
                      {
                        Activity = new ActivityInformation()
                        {
                          Version = SemVer.V100.ToByteString(),
                          Id = ActivityId,
                          OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(identityPublicKey),
                          ProfileServerContact = new ServerContactInfo()
                          {
                            IpAddress = ProtocolHelper.ByteArrayToByteString(Config.Configuration.ExternalServerAddress.GetAddressBytes()),
                            NetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(Config.Configuration.Keys.PublicKey)),
                            PrimaryPort = 1
                          },
                          Type = ActivityBase.InternalInvalidActivityType,
                          Latitude = 0,
                          Longitude = 0,
                          Precision = 0,
                          StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(DateTime.UtcNow),
                          ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(DateTime.UtcNow.AddMinutes(1)),
                          ExtraData = ""
                        },
                        Signature = ProtocolHelper.ByteArrayToByteString(new byte[0])
                      }
                    };

                    updateItem = new SharedActivityUpdateItem();
                    updateItem.Add = item;
                  }


                  break;
                }

              case NeighborhoodActionType.ChangeActivity:
                {
                  PrimaryActivity activity = (await unitOfWork.PrimaryActivityRepository.GetAsync(i => (i.ActivityId == ActivityId) && (i.OwnerIdentityId == OwnerIdentityId))).FirstOrDefault();
                  if (activity != null)
                  {
                    updateItem = new SharedActivityUpdateItem()
                    {
                      Change = new SharedActivityChangeItem()
                      {
                        SignedActivity = activity.ToSignedActivityInformation()
                      }
                    };
                  }
                  else
                  {
                    log.Warn("Unable to find primary activity ID {0}, owner identity ID '{1}'.", ActivityId, OwnerIdentityId.ToHex());
                    //
                    // This happens when the activity has been deleted before we managed to send the update to the follower. 
                    // See the analysis in AddActivity case for more information.
                    //
                    // We simply return true here to erase this action from the queue.
                    //
                    res = true;
                  }
                  
                  break;
                }

              case NeighborhoodActionType.RemoveActivity:
                {
                  // Because of using the artifical activity trick, we can be sure here that the follower is aware of 
                  // this activity even if it was deleted from our database before add activity update action 
                  // was taken from the queue. See the analysis in AddActivity case for more information.
                  updateItem = new SharedActivityUpdateItem()
                  {
                    Delete = new SharedActivityDeleteItem()
                    {
                      Id = ActivityId,
                      OwnerNetworkId = ProtocolHelper.ByteArrayToByteString(OwnerIdentityId)
                    }
                  };
                  break;
                }


              case NeighborhoodActionType.RefreshNeighborStatus:
                {
                  updateItem = new SharedActivityUpdateItem()
                  {
                    Refresh = new SharedActivityRefreshItem()
                  };
                  break;
                }


              default:
                log.Error("Invalid action type '{0}'.", ActionType);

                // Should never happen. Same resolution as in case of exception below.
                break;
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
            //
            // We should never be here, there probably is a bug and this action will probably cause 
            // this bug again if we don't remove it. However, removing the action is even worse because 
            // it would cause silent inconsistency of the follower's database, which may even be occasionally 
            // fixed by future updates, but we would rather want to know about bugs of this severity,
            // so we leave the action in the queue and hopefully someone will notice sooner or later.
            //
          }
        }

        if (updateItem != null)
        {
          bool deleteFollower = false;
          bool resetNeighborPort = false;

          using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
          {
            // If we successfully constructed the update for the follower server, we connect to it and send it.
            bool connected = await client.ConnectAndVerifyIdentityAsync();
            if (!connected)
            {
              log.Debug("Failed to connect to follower ID '{0}' on address '{1}'. Trying to get new value of its neighbor port from its primaryPort.", FollowerId.ToHex(), endPoint);
              IPEndPoint newEndPoint = await GetFollowerServerContactAsync(FollowerId, notFound, true);
              if (newEndPoint != null)
              {
                endPoint = newEndPoint;
                client.SetRemoteEndPoint(endPoint);
                connected = await client.ConnectAndVerifyIdentityAsync();
              }
              else log.Debug("Failed to get neighbor port from primaryPort of follower ID '{0}'.", FollowerId.ToHex());
            }

            if (connected)
            {
              if (client.MatchServerId(FollowerId))
              {
                List<SharedActivityUpdateItem> updateList = new List<SharedActivityUpdateItem>() { updateItem };
                ProxProtocolMessage updateMessage = client.MessageBuilder.CreateNeighborhoodSharedActivityUpdateRequest(updateList);
                if (await client.SendNeighborhoodSharedProfileUpdateAsync(updateMessage))
                {
                  if (updateItem.ActionTypeCase == SharedActivityUpdateItem.ActionTypeOneofCase.Refresh)
                  {
                    // If database update fails, the follower server will just be refreshed again later.
                    using (UnitOfWork unitOfWork = new UnitOfWork())
                    {
                      await unitOfWork.FollowerRepository.UpdateLastRefreshTimeAsync(FollowerId);
                    }
                  }

                  res = true;
                }
                else
                {
                  if (client.LastResponseStatus == Status.ErrorRejected)
                  {
                    log.Info("Follower ID '{0}' rejected an update, deleting this follower.", FollowerId.ToHex());
                    deleteFollower = true;
                  }
                  else if (client.LastResponseStatus == Status.ErrorBadRole)
                  {
                    log.Info("Follower ID '{0}' rejected an update due to bad port usage (port {1} was used), reseting neighbor port for this follower, will retry later.", FollowerId.ToHex(), endPoint.Port);
                    resetNeighborPort = true;
                  }
                  else if (client.LastResponseStatus == Status.ErrorInvalidValue)
                  {
                    //
                    // There are two possibilities here. First case is that we are in trouble because the database of the follower is not synchronized with ours
                    // in which case retrying would not make it any better, and our best move is to delete the follower. Our shared activities with it will expire 
                    // in time and the follower will contact us again to reestablish the relationship.
                    // 
                    // The second case is a very rare case that can happen if the server sent NeighborhoodSharedActivityUpdateRequest to its follower 
                    // in the past and the follower did accept and did process the update and saved it into its database, but it failed to deliver 
                    // NeighborhoodSharedActivityUpdateResponse to this server (e.g. due to a power failure on either side, or a network failure).
                    // We could handle this special case differently but it is not easy to detect it. This is why we delete the follower as well in this case 
                    // although it might not be necessary, but it is definitely the easiest thing to do.
                    //
                    log.Warn("Sending update to follower ID '{0}' failed, error status {1}, deleting follower!", FollowerId.ToHex(), client.LastResponseStatus);
                    deleteFollower = true;
                  }
                  else if (client.LastResponseStatus != Status.Ok)
                  {
                    // In this case we have received unexpected error from the follower, we do not know how to recover from this, 
                    // so we just delete it and hope it will get better next time.
                    log.Warn("Sending update to follower ID '{0}' failed, unexpected error status {1}, deleting follower!", FollowerId.ToHex(), client.LastResponseStatus);
                    deleteFollower = true;
                  }
                  else
                  {
                    // This means that there was a problem with connection, we can try later and should be able to recover.
                    log.Warn("Sending update to follower ID '{0}' failed, will retry later.", FollowerId.ToHex());
                  }
                }
              }
              else
              {
                log.Warn("Server identity differs from expected ID '{0}', deleting this follower.", FollowerId.ToHex());
                deleteFollower = true;
              }
            }
            else
            {
              log.Info("Unable to initiate conversation with follower ID '{0}' on address {1}, will retry later.", FollowerId.ToHex(), endPoint);

              // We have failed to connect to the follower's neighborPort as well as to get the current neighborPort from its primaryPort.
              // The follower is probably offline and we will try to process this action later.
            }
          }

          if (deleteFollower)
          {
            using (UnitOfWork unitOfWork = new UnitOfWork())
            {
              Status status = await unitOfWork.FollowerRepository.DeleteFollowerAsync(FollowerId, ActionId);
              if ((status == Status.Ok) || (status == Status.ErrorNotFound))
              {
                // Follower was deleted from the database, all its actions should be deleted by now 
                // except for this one that is currently being executed.
                // Nothing more to do here.
                log.Info("Follower ID '{0}' has been deleted.", FollowerId.ToHex());
                res = true;
              }
              else log.Error("Unable to delete follower ID '{0}' from database.", FollowerId.ToHex());
            }
          }

          if (resetNeighborPort)
          {
            using (UnitOfWork unitOfWork = new UnitOfWork())
            {
              if (await unitOfWork.FollowerRepository.ResetNeighborPortAsync(FollowerId)) log.Info("Neighbor port of follower ID '{0}' has been reset.", FollowerId.ToHex());
              else log.Error("Unable to reset neighbor port of follower ID '{0}'.", FollowerId.ToHex());
            }
          }
        }
        else
        {
          // Only change activity update action can cause this when the activity was deleted from our database already.
          // In that case result is set to true, so the action will be removed from the queue.
          if (!res) log.Error("No update item was created but the action remains in the queue.");
        }
      }
      else
      {
        if (notFound.Value == true)
        {
          // This means that the follower does not exists, hence we return true to delete this action from the queue.
          res = true;
        }
        else
        {
          //
          // If we are here it means that neighborPort of this follower was reset before because we received ERROR_BAD_ROLE from the old neighborPort port.
          // This could happen if the follower server changed its ports and the port we know is not used as neighborPort anymore.
          // Then we attempted to connect to its primaryPort to get new value for its neighborPort and this failed as well,
          // which means that the server was offline.
          //
          // The solution here is to wait. All actions to this follower will be blocked and this particular action will be retried later again.
          // If the follower gets online and we succeed, actions for it will be unblocked again. Otherwise it will be eventually deleted 
          // when it is unresponsive for long time in Database.CheckFollowersRefreshAsync().
          //
          log.Warn("Unable to find follower ID '{0}' IP and port information, will retry later.", FollowerId.ToHex());
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes NeighborhoodSharedActivityUpdateRequest message from client sent during neighborhood initialization process.
    /// <para>Saves incoming activities information to the temporary memory location.</para>
    /// </summary>
    /// <param name="Client">Client that received the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the remote peer.</returns>
    private ProxProtocolMessage ProcessMessageNeighborhoodSharedActivityUpdateRequest(OutgoingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      ProxProtocolMessage res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      bool error = false;
      NeighborhoodSharedActivityUpdateRequest neighborhoodSharedActivityUpdateRequest = RequestMessage.Request.ConversationRequest.NeighborhoodSharedActivityUpdate;
      log.Debug("Received {0} update items.", neighborhoodSharedActivityUpdateRequest.Items.Count);

      Dictionary<string, NeighborActivity> activityDatabase = (Dictionary<string, NeighborActivity>)Client.Context;
      int itemIndex = 0;
      foreach (SharedActivityUpdateItem updateItem in neighborhoodSharedActivityUpdateRequest.Items)
      {
        if (updateItem.ActionTypeCase == SharedActivityUpdateItem.ActionTypeOneofCase.Add)
        {
          SharedActivityAddItem addItem = updateItem.Add;
          ProxProtocolMessage errorResponse;
          if (InputValidators.ValidateInMemorySharedActivityActivityInformation(addItem, itemIndex, activityDatabase, Client.MessageBuilder, RequestMessage, out errorResponse))
          {
            NeighborActivity activity = ActivityBase.FromSignedActivityInformation<NeighborActivity>(addItem.SignedActivity, Client.ServerId);
            activityDatabase.Add(activity.GetFullId(), activity);
          }
          else
          {
            res = errorResponse;
            error = true;
            break;
          }
        }
        else
        {
          log.Warn("Invalid activity update item action type '{0}' received during the neighborhood initialization process.", updateItem.ActionTypeCase);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, itemIndex.ToString() + ".actionType");
          error = true;
          break;
        }

        itemIndex++;
      }

      if (!error) res = messageBuilder.CreateNeighborhoodSharedActivityUpdateResponse(RequestMessage);
      else Client.ForceDisconnect = true;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    
    /// <summary>
    /// Processes FinishNeighborhoodInitializationRequest message from neighbor server sent during neighborhood initialization process.
    /// <para>Saves the temporary in-memory database of activities to the database.</para>
    /// </summary>
    /// <param name="Client">Client that received the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the remote peer.</returns>
    private async Task<ProxProtocolMessage> ProcessMessageFinishNeighborhoodInitializationRequestAsync(OutgoingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      ProxProtocolMessage res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      FinishNeighborhoodInitializationRequest finishNeighborhoodInitializationRequest = RequestMessage.Request.ConversationRequest.FinishNeighborhoodInitialization;

      bool success = false;
      Dictionary<string, NeighborActivity> activityDatabase = (Dictionary<string, NeighborActivity>)Client.Context;

      // Save new activities to the database.
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        success = await unitOfWork.NeighborRepository.SaveNeighborhoodInitializationActivitiesAsync(activityDatabase, Client.ServerId);
      }


      if (success) res = messageBuilder.CreateFinishNeighborhoodInitializationResponse(RequestMessage);
      else Client.ForceDisconnect = true;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }
  }
}
