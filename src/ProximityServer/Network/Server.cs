using System;
using System.Collections.Generic;
using IopCommon;
using IopServerCore.Kernel;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityServer.Network
{
  /// <summary>
  /// Types of server roles that can each be served on different port.
  /// Some of the roles are served unencrypted, others are encrypted.
  /// </summary>
  /// <remarks>If more than 8 different values are needed, consider changing IdBase initialization in TcpRoleServer constructor.</remarks>
  [Flags]
  public enum ServerRole
  {
    /// <summary>Primary Interface server role.</summary>
    Primary = 1,

    /// <summary>Neighbors Interface server role.</summary>
    Neighbor = 4,

    /// <summary>Customer Clients Interface server role.</summary>
    Client = 16,
  }


  /// <summary>
  /// Network server component is responsible managing the role TCP servers.
  /// </summary>
  public class Server : IopServerCore.Network.ServerBase<IncomingClient>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer." + ComponentName);

    /// <summary>
    /// Time in milliseconds for which a remote client is allowed not to send any request to the proximity server in the open connection.
    /// If the proximity server detects an open connection to the client without any request for more than this value,
    /// the server will close the connection.
    /// </summary>
    public const int ClientKeepAliveIntervalMs = 60 * 1000;

    /// <summary>
    /// Time in milliseconds for which a remote server is allowed not to send any request to the proximity server in the open connection.
    /// If the proximity server detects an open connection to other proximity server without any request for more than this value,
    /// the server will close the connection.
    /// </summary>
    public const int ServerKeepAliveIntervalMs = 300 * 1000;



    /// <summary>Event that is set when DelayedStartupThread is not running.</summary>
    private ManualResetEvent delayedStartupThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for delated initialization of this component.</summary>
    private Thread delayedStartupThread;

    /// <summary>Event that signals when delayed startup is finished.</summary>
    public TaskCompletionSource<bool> DelayedStartupCompletedEvent = new TaskCompletionSource<bool>();



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

        if ((delayedStartupThread != null) && !delayedStartupThreadFinished.WaitOne(10000))
          log.Error("Delayed startup thread did not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      DelayedStartupCompletedEvent.TrySetCanceled();

      base.Shutdown();

      if ((delayedStartupThread != null) && !delayedStartupThreadFinished.WaitOne(10000))
        log.Error("Delayed startup thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }


    /// <summary>
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Checks if any of the opened TCP connections are inactive and if so, it closes them.
        { new CronJob() { Name = "checkInactiveClientConnections", StartDelay = 2 * 60 * 1000, Interval = 2 * 60 * 1000, HandlerAsync = CronJobHandlerCheckInactiveClientConnectionsAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }



    /// <summary>
    /// Handler for "checkInactiveClientConnections" cron job.
    /// </summary>
    public async void CronJobHandlerCheckInactiveClientConnectionsAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await CheckInactiveClientConnectionsAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread that is responsible for late initialization of network servers.
    /// This is needed because in order for the proximity server to run, it must know its location.
    /// The location is received from LOC server, but there is no guarantee LOC server is running 
    /// when the proximity server starts.
    /// </summary>
    private async void DelayedStartupThread()
    {
      LogDiagnosticContext.Start();

      log.Info("()");

      delayedStartupThreadFinished.Reset();

      try
      {
        Console.WriteLine("Waiting for location initialization ...");
        LocationBasedNetwork loc = (LocationBasedNetwork)Base.ComponentDictionary[LocationBasedNetwork.ComponentName];
        bool locInit = false;
        try
        {
          locInit = await loc.LocLocationInitializedEvent.Task;
        }
        catch
        {
          // Catch cancellation exception.
          log.Trace("Shutdown detected.");
        }

        if (locInit)
        {
          log.Debug("LOC location is initialized, we can continue with Network server component initialization.");
          if (base.Init())
          {
            RegisterCronJobs();
            DelayedStartupCompletedEvent.TrySetResult(true);
          }
          else
          {
            log.Error("Delayed initialization failed, initiating shutdown.");
            Base.ComponentManager.GlobalShutdown.SignalShutdown();
          }
          Console.WriteLine("Location initialization completed.");
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
  }
}
