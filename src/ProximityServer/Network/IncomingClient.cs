using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using IopProtocol;
using System.Net;
using System.Runtime.InteropServices;
using ProximityServer.Kernel;
using System.Net.Security;
using System.Threading;
using ProximityServer.Data.Models;
using Iop.Proximityserver;
using IopCrypto;
using System.Security.Authentication;
using ProximityServer.Data;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopAppCore.Data;
using IopServerCore.Network;

namespace ProximityServer.Network
{
  /// <summary>Different states of conversation between the client and the server.</summary>
  public enum ClientConversationStatus
  {
    /// <summary>Client has not initiated a conversation yet.</summary>
    NoConversation,

    /// <summary>There is an established conversation with the client, but no authentication has been done.</summary>
    ConversationStarted,

    /// <summary>There is an established conversation with the non-customer client and the verification process has already been completed.</summary>
    Verified,

    /// <summary>The conversation status of the client is ConversationStarted, Verified, or Authenticated.</summary>
    ConversationAny
  };

  /// <summary>
  /// Incoming client class represents any kind of TCP client that connects to one of the proximity server's TCP servers.
  /// </summary>
  public class IncomingClient : IncomingClientBase, IDisposable
  {
    /// <summary>
    /// Length of the activity search cache expiration period in seconds.
    /// Minimal value defined by the protocol is 60 seconds.
    /// </summary>
    public const int ActivitySearchResultCacheExpirationTimeSeconds = 180;

    
    /// <summary>Protocol message builder.</summary>
    private ProxMessageBuilder messageBuilder;
    /// <summary>Protocol message builder.</summary>
    public ProxMessageBuilder MessageBuilder { get { return messageBuilder; } }


    /// <summary>Current status of the conversation with the client.</summary>
    public ClientConversationStatus ConversationStatus;

    /// <summary>True if the client connection is from a follower server who initiated the neighborhood initialization process in this session.</summary>
    public bool NeighborhoodInitializationProcessInProgress;


    /// <summary>List of unprocessed requests that we expect to receive responses to mapped by Message.id.</summary>
    private Dictionary<uint, UnfinishedRequest> unfinishedRequests = new Dictionary<uint, UnfinishedRequest>();

    /// <summary>Lock for access to unfinishedRequests list.</summary>
    private object unfinishedRequestsLock = new object();


    /// <summary>
    /// Timer for activity search result cache expiration. When the timer's routine is called, 
    /// the cache is deleted and cached results are no longer available.
    /// </summary>
    private Timer activitySearchResultCacheExpirationTimer;

    /// <summary>Lock object to protect access to activity search result cache objects.</summary>
    private object activitySearchResultCacheLock = new object();

    /// <summary>Cache for activity search result queries.</summary>
    private List<ActivityQueryInformation> activitySearchResultCache;



    /// <summary>
    /// Creates the instance for a new TCP server client.
    /// </summary>
    /// <param name="Server">Role server that the client connected to.</param>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="Id">Unique identifier of the client's connection.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="KeepAliveIntervalMs">Number of seconds for the connection to this client to be without any message until the proximity server can close it for inactivity.</param>
    /// <param name="LogPrefix">Prefix for log entries created by the client.</param>
    public IncomingClient(TcpRoleServer<IncomingClient> Server, TcpClient TcpClient, ulong Id, bool UseTls, int KeepAliveIntervalMs, string LogPrefix) :
      base(TcpClient, new ProxMessageProcessor(Server, LogPrefix), Id, UseTls, KeepAliveIntervalMs, Server.IdBase, Server.ShutdownSignaling, LogPrefix)
    {
      this.Id = Id;
      log = new Logger("ProximityServer.Network.IncomingClient", LogPrefix);

      log.Trace("(UseTls:{0},KeepAliveIntervalMs:{1})", UseTls, KeepAliveIntervalMs);

      messageBuilder = new ProxMessageBuilder(Server.IdBase, new List<SemVer>() { SemVer.V100 }, Config.Configuration.Keys);
      this.KeepAliveIntervalMs = KeepAliveIntervalMs;
      NextKeepAliveTime = DateTime.UtcNow.AddMilliseconds(this.KeepAliveIntervalMs);

      ConversationStatus = ClientConversationStatus.NoConversation;

      log.Trace("(-)");
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public override IProtocolMessage CreateMessageFromRawData(byte[] Data)
    {
      return ProxMessageBuilder.CreateMessageFromRawData(Data);
    }


    /// <summary>
    /// Converts an IoP Proximity Server Network protocol message to a binary format.
    /// </summary>
    /// <param name="Data">IoP Proximity Server Network protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public override byte[] MessageToByteArray(IProtocolMessage Data)
    {
      return ProxMessageBuilder.MessageToByteArray(Data);
    }


    /// <summary>
    /// Handles client disconnection. Destroys objects that are connected to this client 
    /// and frees the resources.
    /// </summary>
    public override async Task HandleDisconnect()
    {
      log.Trace("()");

      await base.HandleDisconnect();

      if (NeighborhoodInitializationProcessInProgress)
      {
        // This client is a follower server and when it disconnected there was an unfinished neighborhood initialization process.
        // We need to delete its traces from the database.
        await DeleteUnfinishedFollowerInitialization();
      }

      log.Trace("(-)");
    }

    
    /// <summary>
    /// Saves search results to the client session cache.
    /// </summary>
    /// <param name="SearchResults">Search results to save.</param>
    public void SaveActivitySearchResults(List<ActivityQueryInformation> SearchResults)
    {
      log.Trace("(SearchResults.GetHashCode():{0})", SearchResults.GetHashCode());

      lock (activitySearchResultCacheLock)
      {
        if (activitySearchResultCacheExpirationTimer != null)
          activitySearchResultCacheExpirationTimer.Dispose();

        activitySearchResultCache = SearchResults;

        activitySearchResultCacheExpirationTimer = new Timer(ActivitySearchResultCacheTimerCallback, activitySearchResultCache, ActivitySearchResultCacheExpirationTimeSeconds * 1000, Timeout.Infinite);
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Gets information about the search result cache.
    /// </summary>
    /// <param name="Count">If the result cache is not empty, this is filled with the number of items in the cache.</param>
    /// <returns>true if the result cache is not empty, false otherwise.</returns>
    public bool GetActivitySearchResultsInfo(out int Count)
    {
      log.Trace("()");

      bool res = false;
      Count = 0;

      lock (activitySearchResultCacheLock)
      {
        if (activitySearchResultCache != null)
        {
          Count = activitySearchResultCache.Count;
          res = true;
        }
      }

      if (res) log.Trace("(-):{0},Count={1}", res, Count);
      else log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Loads search results from the client session cache.
    /// </summary>
    /// <param name="Index">Index of the first item to retrieve.</param>
    /// <param name="Count">Number of items to retrieve.</param>
    /// <returns>A copy of search results loaded from the cache or null if the required item range is not available.</returns>
    public List<ActivityQueryInformation> GetActivitySearchResults(int Index, int Count)
    {
      log.Trace("()");

      List<ActivityQueryInformation> res = null;
      lock (activitySearchResultCacheLock)
      {
        if ((activitySearchResultCache != null) && (Index + Count <= activitySearchResultCache.Count))
          res = new List<ActivityQueryInformation>(activitySearchResultCache.GetRange(Index, Count));
      }

      log.Trace("(-):*.Count={0}", res != null ? res.Count.ToString() : "N/A");
      return res;
    }


    /// <summary>
    /// Callback routine that is called once the activitySearchResultCacheExpirationTimer expires to delete cached search results.
    /// </summary>
    /// <param name="State">Search results object that was set during the timer initialization.
    /// this has to match the current search results, otherwise it means the results have been replaced already.</param>
    private void ActivitySearchResultCacheTimerCallback(object State)
    {
      List<ActivityQueryInformation> searchResults = (List<ActivityQueryInformation>)State;
      log.Trace("(State.GetHashCode():{0})", searchResults.GetHashCode());

      lock (activitySearchResultCacheLock)
      {
        if (activitySearchResultCache == searchResults)
        {
          if (activitySearchResultCacheExpirationTimer != null)
          {
            activitySearchResultCacheExpirationTimer.Dispose();
            activitySearchResultCacheExpirationTimer = null;
          }

          activitySearchResultCache = null;

          log.Debug("Search result cache has been cleaned.");
        }
        else log.Debug("Current search result cache is not the same as the cache this timer was about to delete.");
      }

      log.Trace("(-)");
    }
    

    /// <summary>
    /// Deletes a follower entry from the database when the follower disconnected before the neighborhood initialization process completed.
    /// </summary>
    /// <returns>true if the function succeeds, false othewise.</returns>
    private async Task<bool> DeleteUnfinishedFollowerInitialization()
    {
      log.Trace("()");

      bool res = false;
      byte[] followerId = IdentityId;
      bool success = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
        {
          try
          {
            bool saveDb = false;

            // Delete the follower itself.
            Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.NetworkId == followerId)).FirstOrDefault();
            if (existingFollower != null)
            {
              unitOfWork.FollowerRepository.Delete(existingFollower);
              log.Debug("Follower ID '{0}' will be removed from the database.", followerId.ToHex());
              saveDb = true;
            }
            else log.Error("Follower ID '{0}' not found.", followerId.ToHex());

            // Delete all its neighborhood actions.
            List<NeighborhoodAction> actions = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => a.ServerId == followerId)).ToList();
            foreach (NeighborhoodAction action in actions)
            {
              if (action.IsActivityAction())
              {
                log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, followerId.ToHex());
                unitOfWork.NeighborhoodActionRepository.Delete(action);
                saveDb = true;
              }
            }


            if (saveDb)
            {
              await unitOfWork.SaveThrowAsync();
              transaction.Commit();
            }

            success = true;
            res = true;
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
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected override void Dispose(bool Disposing)
    {
      bool disposedAlready = false;
      lock (disposingLock)
      {
        disposedAlready = disposed;
        disposed = true;
      }
      if (disposedAlready) return;

      if (Disposing)
      {
        base.Dispose(Disposing);
        
        lock (activitySearchResultCacheLock)
        {
          if (activitySearchResultCacheExpirationTimer != null)
            activitySearchResultCacheExpirationTimer.Dispose();

          activitySearchResultCacheExpirationTimer = null;
          activitySearchResultCache = null;
        }
      }
    }
  }
}
