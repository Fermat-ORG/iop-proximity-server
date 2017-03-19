﻿using System;
using ProximityServer.Kernel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IopProtocol;
using Google.Protobuf;
using ProximityServer.Data;
using ProximityServer.Data.Models;
using Iop.Can;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network.CAN;
using Iop.Proximityserver;

namespace ProximityServer.Network
{
  /// <summary>
  /// Content address network (CAN) is a part of IoP that the proximity server relies on.
  /// This component is responsible for submitting proximity server's contact information to CAN 
  /// and managing and refreshing its IPNS record.
  /// </summary>
  public class ContentAddressNetwork : Component
  {
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Network.ContentAddressNetwork";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer." + ComponentName);

    /// <summary>Validity of proximity server's IPNS record in seconds. </summary>
    private const int IpnsRecordExpirationTimeSeconds = 24 * 60 * 60;

    /// <summary>Proximity server's IPNS record.</summary>
    private CanIpnsEntry canIpnsRecord;


    /// <summary>Proximity server's contact information object in CAN.</summary>
    private CanProximityServerContact canContactInformation;

    /// <summary>CAN hash of CanContactInformation object.</summary>
    private byte[] canContactInformationHash;


    /// <summary>Event that is set when initThread is not running.</summary>
    private ManualResetEvent initThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that initializes CAN objects during the proximity server's startup.</summary>
    private Thread initThread;

    /// <summary>Access to CAN API.</summary>
    private CanApi api;
    /// <summary>Access to CAN API.</summary>
    public CanApi Api { get { return api; } }

    /// <summary>Last sequence number used for IPNS record.</summary>
    private UInt64 canIpnsLastSequenceNumber;



    /// <summary>
    /// Initializes the component.
    /// </summary>
    public ContentAddressNetwork() :
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        canIpnsLastSequenceNumber = Config.Configuration.CanIpnsLastSequenceNumber;
        api = new CanApi(Config.Configuration.CanEndPoint, ShutdownSignaling);

        // Construct proximity server's contact information CAN object.
        canContactInformation = new CanProximityServerContact()
        {
          PublicKey = ProtocolHelper.ByteArrayToByteString(Config.Configuration.Keys.PublicKey),
          IpAddress = ProtocolHelper.ByteArrayToByteString(Config.Configuration.ExternalServerAddress.GetAddressBytes()),
          PrimaryPort = (uint)Config.Configuration.ServerRoles.GetRolePort((uint)ServerRole.Primary)
        };


        initThread = new Thread(new ThreadStart(InitThread));
        initThread.Start();

        RegisterCronJobs();

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

        if ((initThread != null) && !initThreadFinished.WaitOne(25000))
          log.Error("Init thread did not terminated in 25 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if ((initThread != null) && !initThreadFinished.WaitOne(25000))
        log.Error("Init thread did not terminate in 25 seconds.");

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
        // Refreshes proximity server's IPNS record.
        { new CronJob() { Name = "ipnsRecordRefresh", StartDelay = 2 * 60 * 60 * 1000, Interval = 7 * 60 * 60 * 1000, HandlerAsync = CronJobHandlerIpnsRecordRefreshAsync  } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }



    /// <summary>
    /// Handler for "ipnsRecordRefresh" cron job.
    /// </summary>
    public async void CronJobHandlerIpnsRecordRefreshAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await IpnsRecordRefreshAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Refreshes proximity server's IPNS record.
    /// </summary>
    public async Task IpnsRecordRefreshAsync()
    {
      log.Trace("()");

      if (canContactInformationHash == null)
      {
        log.Debug("No CAN contact information hash, can't refresh IPNS record, will try later.");
        log.Trace("(-)");
        return;
      }

      canIpnsLastSequenceNumber++;
      canIpnsRecord = CanApi.CreateIpnsRecord(canContactInformationHash, canIpnsLastSequenceNumber, IpnsRecordExpirationTimeSeconds);
      CanRefreshIpnsResult cres = await api.RefreshIpnsRecord(canIpnsRecord, Config.Configuration.Keys.PublicKey);
      if (cres.Success)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          await unitOfWork.AcquireLockAsync(UnitOfWork.SettingsLock);

          try
          {
            Setting setting = new Setting("CanIpnsLastSequenceNumber", canIpnsLastSequenceNumber.ToString());
            await unitOfWork.SettingsRepository.AddOrUpdate(setting);
            await unitOfWork.SaveThrowAsync();
            log.Debug("CanIpnsLastSequenceNumber updated in database to new value {0}.", setting.Value);
          }
          catch (Exception e)
          {
            log.Error("Unable to update CanIpnsLastSequenceNumber in the database to new value {0}, exception: {1}", canIpnsLastSequenceNumber, e.ToString());
          }

          unitOfWork.ReleaseLock(UnitOfWork.SettingsLock);
        }
      }
      else if (cres.Message != "Shutdown") log.Error("Failed to refresh proximity server's IPNS record.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread that is initializes CAN objects during the proximity server startup.
    /// </summary>
    private async void InitThread()
    {
      log.Info("()");

      initThreadFinished.Reset();

      if (Config.Configuration.CanProximityServerContactInformationHash != null) log.Debug("Old CAN object hash is '{0}', object {1} change.", Config.Configuration.CanProximityServerContactInformationHash.ToBase58(), Config.Configuration.CanProximityServerContactInformationChanged ? "DID" : "did NOT");
      else log.Debug("No CAN object found.");

      bool deleteOldObject = Config.Configuration.CanProximityServerContactInformationChanged && (Config.Configuration.CanProximityServerContactInformationHash != null);
      byte[] canObject = canContactInformation.ToByteArray();
      log.Trace("CAN object: {0}", canObject.ToHex());

      while (!ShutdownSignaling.IsShutdown)
      {
        // First delete old CAN object if there is any.
        bool error = false;
        if (deleteOldObject)
        {
          string objectPath = CanApi.CreateIpfsPathFromHash(Config.Configuration.CanProximityServerContactInformationHash);
          CanDeleteResult cres = await api.CanDeleteObject(objectPath);
          if (cres.Success)
          {
            log.Info("Old CAN object hash '{0}' deleted.", Config.Configuration.CanProximityServerContactInformationHash.ToBase58());
          }
          else
          {
            log.Warn("Failed to delete old CAN object hash '{0}', error message '{1}', will retry.", Config.Configuration.CanProximityServerContactInformationHash.ToBase58(), cres.Message);
            error = true;
          }
        }
        else log.Trace("No old object to delete.");

        if (ShutdownSignaling.IsShutdown) break;
        if (!error)
        {
          if (Config.Configuration.CanProximityServerContactInformationChanged)
          {
            // Now upload the new object.
            CanUploadResult cres = await api.CanUploadObject(canObject);
            if (cres.Success)
            {
              canContactInformationHash = cres.Hash;
              log.Info("New CAN object hash '{0}' added.", canContactInformationHash.ToBase58());
              break;
            }

            log.Warn("Unable to add new object to CAN, error message: '{0}'", cres.Message);
          }
          else
          {
            canContactInformationHash = Config.Configuration.CanProximityServerContactInformationHash;
            log.Info("CAN object unchanged since last time, hash is '{0}'.", canContactInformationHash.ToBase58());
            break;
          }
        }

        // Retry in 10 seconds.
        try
        {
          await Task.Delay(10000, ShutdownSignaling.ShutdownCancellationTokenSource.Token);
        }
        catch
        {
          // Catch cancellation exception.
        }
      }


      if (canContactInformationHash != null)
      {
        if (Config.Configuration.CanProximityServerContactInformationChanged)
        {
          // Save the new data to the database.
          if (!await SaveProximityServerContactInformation())
            log.Error("Failed to save new proximity server contact information values to database.");
        }

        // Finally, start IPNS record refreshing timer.
        await IpnsRecordRefreshAsync();
      }


      initThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Saves values related to the proximity server contact information to the database.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveProximityServerContactInformation()
    {
      log.Trace("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.SettingsLock;
        await unitOfWork.AcquireLockAsync(lockObject);

        try
        {
          string addr = Config.Configuration.ExternalServerAddress.ToString();
          string port = Config.Configuration.ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString();
          string hash = canContactInformationHash.ToBase58();
          log.Debug("Saving contact information values to database: {0}:{1}, '{2}'", addr, port, hash);

          Setting primaryPort = new Setting("PrimaryPort", port);
          Setting externalServerAddress = new Setting("ExternalServerAddress", addr);
          Setting canProximityServerContactInformationHash = new Setting("CanProximityServerContactInformationHash", hash);

          await unitOfWork.SettingsRepository.AddOrUpdate(externalServerAddress);
          await unitOfWork.SettingsRepository.AddOrUpdate(primaryPort);
          await unitOfWork.SettingsRepository.AddOrUpdate(canProximityServerContactInformationHash);

          await unitOfWork.SaveThrowAsync();
          res = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
