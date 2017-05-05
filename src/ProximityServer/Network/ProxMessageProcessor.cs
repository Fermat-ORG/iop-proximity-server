using Google.Protobuf;
using ProximityServer.Data;
using ProximityServer.Data.Models;
using ProximityServer.Data.Repositories;
using ProximityServer.Kernel;
using IopCrypto;
using IopProtocol;
using Iop.Proximityserver;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network;
using IopServerCore.Network.CAN;
using System.Net;
using Iop.Shared;
using System.Runtime.CompilerServices;

namespace ProximityServer.Network
{
  /// <summary>
  /// Implements the logic behind processing incoming messages to the proximity server.
  /// </summary>
  public class ProxMessageProcessor : IMessageProcessor
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Prefix used</summary>
    private string logPrefix;

    /// <summary>Parent role server.</summary>
    private TcpRoleServer<IncomingClient> roleServer;

    /// <summary>Pointer to the Network.Server component.</summary>
    private Server serverComponent;

    /// <summary>List of server's network peers and clients owned by Network.Server component.</summary>
    public IncomingClientList clientList;

    /// <summary>
    /// Creates a new instance connected to the parent role server.
    /// </summary>
    /// <param name="RoleServer">Parent role server.</param>
    /// <param name="LogPrefix">Log prefix of the parent role server.</param>
    public ProxMessageProcessor(TcpRoleServer<IncomingClient> RoleServer, string LogPrefix = "")
    {
      roleServer = RoleServer;
      logPrefix = LogPrefix;
      log = new Logger("ProximityServer.Network.ProxMessageProcessor", logPrefix);
      serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
      clientList = serverComponent.GetClientList();
    }



    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(ClientBase Client, IProtocolMessage IncomingMessage)
    {
      IncomingClient client = (IncomingClient)Client;
      ProxProtocolMessage incomingMessage = (ProxProtocolMessage)IncomingMessage;
      ProxMessageBuilder messageBuilder = client.MessageBuilder;

      bool res = false;
      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        client.NextKeepAliveTime = DateTime.UtcNow.AddMilliseconds(client.KeepAliveIntervalMs);
        log.Trace("Client ID {0} NextKeepAliveTime updated to {1}.", client.Id.ToHex(), client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

        log.Trace("Received message type is {0}, message ID is {1}.", incomingMessage.MessageTypeCase, incomingMessage.Id);
        switch (incomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              ProxProtocolMessage responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(incomingMessage);
              Request request = incomingMessage.Request;
              log.Trace("Request conversation type is {0}.", request.ConversationTypeCase);
              switch (request.ConversationTypeCase)
              {
                case Request.ConversationTypeOneofCase.SingleRequest:
                  {
                    SingleRequest singleRequest = request.SingleRequest;
                    SemVer version = new SemVer(singleRequest.Version);
                    log.Trace("Single request type is {0}, version is {1}.", singleRequest.RequestTypeCase, version);

                    if (!version.IsValid())
                    {
                      responseMessage.Response.Details = "version";
                      break;
                    }

                    switch (singleRequest.RequestTypeCase)
                    {
                      case SingleRequest.RequestTypeOneofCase.Ping:
                        responseMessage = ProcessMessagePingRequest(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ListRoles:
                        responseMessage = ProcessMessageListRolesRequest(client, incomingMessage);
                        break;

                      default:
                        log.Warn("Invalid request type '{0}'.", singleRequest.RequestTypeCase);
                        break;
                    }

                    break;
                  }

                case Request.ConversationTypeOneofCase.ConversationRequest:
                  {
                    ConversationRequest conversationRequest = request.ConversationRequest;
                    log.Trace("Conversation request type is {0}.", conversationRequest.RequestTypeCase);
                    if (conversationRequest.Signature.Length > 0) log.Trace("Conversation signature is '{0}'.", conversationRequest.Signature.ToByteArray().ToHex());
                    else log.Trace("No signature provided.");

                    switch (conversationRequest.RequestTypeCase)
                    {
                      case ConversationRequest.RequestTypeOneofCase.Start:
                        responseMessage = ProcessMessageStartConversationRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.VerifyIdentity:
                        responseMessage = ProcessMessageVerifyIdentityRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CreateActivity:
                        responseMessage = await ProcessMessageCreateActivityRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.UpdateActivity:
                        responseMessage = await ProcessMessageUpdateActivityRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.DeleteActivity:
                        responseMessage = await ProcessMessageDeleteActivityRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ActivitySearch:
                        responseMessage = await ProcessMessageActivitySearchRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ActivitySearchPart:
                        responseMessage = ProcessMessageActivitySearchPartRequest(client, incomingMessage);
                        break;

                      default:
                        log.Warn("Invalid request type '{0}'.", conversationRequest.RequestTypeCase);
                        // Connection will be closed in ReceiveMessageLoop.
                        break;
                    }

                    break;
                  }

                default:
                  log.Error("Unknown conversation type '{0}'.", request.ConversationTypeCase);
                  // Connection will be closed in ReceiveMessageLoop.
                  break;
              }

              if (responseMessage != null)
              {
                // Send response to client.
                res = await client.SendMessageAsync(responseMessage);

                if (res)
                {
                  // If the message was sent successfully to the target, we close the connection in case it was a protocol violation error response.
                  if (responseMessage.MessageTypeCase == Message.MessageTypeOneofCase.Response)
                    res = responseMessage.Response.Status != Status.ErrorProtocolViolation;
                }
              }
              else
              {
                // If there is no response to send immediately to the client,
                // we want to keep the connection open.
                res = true;
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            {
              Response response = incomingMessage.Response;
              log.Trace("Response status is {0}, details are '{1}', conversation type is {2}.", response.Status, response.Details, response.ConversationTypeCase);

              // Find associated request. If it does not exist, disconnect the client as it 
              // send a response without receiving a request. This is protocol violation, 
              // but as this is a reponse, we have no how to inform the client about it, 
              // so we just disconnect it.
              UnfinishedRequest unfinishedRequest = client.GetAndRemoveUnfinishedRequest(incomingMessage.Id);
              if ((unfinishedRequest != null) && (unfinishedRequest.RequestMessage != null))
              {
                ProxProtocolMessage requestMessage = (ProxProtocolMessage)unfinishedRequest.RequestMessage;
                Request request = requestMessage.Request;
                // We now check whether the response message type corresponds with the request type.
                // This is only valid if the status is Ok. If the message types do not match, we disconnect 
                // for the protocol violation again.
                bool typeMatch = false;
                bool isErrorResponse = response.Status != Status.Ok;
                if (!isErrorResponse)
                {
                  if (response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
                  {
                    typeMatch = (request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
                      && ((int)response.SingleResponse.ResponseTypeCase == (int)request.SingleRequest.RequestTypeCase);
                  }
                  else
                  {
                    typeMatch = (request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
                      && ((int)response.ConversationResponse.ResponseTypeCase == (int)request.ConversationRequest.RequestTypeCase);
                  }
                }
                else typeMatch = true;

                if (typeMatch)
                {
                  // Now we know the types match, so we can rely on request type even if response is just an error.
                  switch (request.ConversationTypeCase)
                  {
                    case Request.ConversationTypeOneofCase.SingleRequest:
                      {
                        SingleRequest singleRequest = request.SingleRequest;
                        switch (singleRequest.RequestTypeCase)
                        {
                          default:
                            log.Warn("Invalid conversation type '{0}' of the corresponding request.", request.ConversationTypeCase);
                            // Connection will be closed in ReceiveMessageLoop.
                            break;
                        }

                        break;
                      }

                    case Request.ConversationTypeOneofCase.ConversationRequest:
                      {
                        ConversationRequest conversationRequest = request.ConversationRequest;
                        switch (conversationRequest.RequestTypeCase)
                        {
                          default:
                            log.Warn("Invalid type '{0}' of the corresponding request.", conversationRequest.RequestTypeCase);
                            // Connection will be closed in ReceiveMessageLoop.
                            break;
                        }
                        break;
                      }

                    default:
                      log.Error("Unknown conversation type '{0}' of the corresponding request.", request.ConversationTypeCase);
                      // Connection will be closed in ReceiveMessageLoop.
                      break;
                  }
                }
                else
                {
                  log.Warn("Message type of the response ID {0} does not match the message type of the request ID {1}, the connection will be closed.", incomingMessage.Id, unfinishedRequest.RequestMessage.Id);
                  // Connection will be closed in ReceiveMessageLoop.
                }
              }
              else
              {
                log.Warn("No unfinished request found for incoming response ID {0}, the connection will be closed.", incomingMessage.Id);
                // Connection will be closed in ReceiveMessageLoop.
              }

              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", incomingMessage.MessageTypeCase);
            await SendProtocolViolation(client);
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation(client);
        // Connection will be closed in ReceiveMessageLoop.
      }

      if (res && client.ForceDisconnect)
      {
        log.Debug("Connection to the client will be forcefully closed.");
        res = false;
      }

      log.Debug("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(ClientBase Client)
    {
      ProxMessageBuilder mb = new ProxMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, null);
      ProxProtocolMessage response = mb.CreateErrorProtocolViolationResponse(new ProxProtocolMessage(new Message() { Id = 0x0BADC0DE }));

      await Client.SendMessageAsync(response);
    }


    /// <summary>
    /// Verifies that client's request was not sent against the protocol rules - i.e. that the role server
    /// that received the message is serving the role the message was designed for and that the conversation 
    /// status with the clients matches the required status for the particular message.
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <param name="RequiredRole">Server role required for the message, or null if all roles servers can handle this message.</param>
    /// <param name="RequiredConversationStatus">Required conversation status for the message, or null for single messages.</param>
    /// <param name="ResponseMessage">If the verification fails, this is filled with error response to be sent to the client.</param>
    /// <returns>true if the function succeeds (i.e. required conditions are met and the message can be processed), false otherwise.</returns>
    public bool CheckSessionConditions(IncomingClient Client, ProxProtocolMessage RequestMessage, ServerRole? RequiredRole, ClientConversationStatus? RequiredConversationStatus, out ProxProtocolMessage ResponseMessage)
    {
      log.Trace("(RequiredRole:{0},RequiredConversationStatus:{1})", RequiredRole != null ? RequiredRole.ToString() : "null", RequiredConversationStatus != null ? RequiredConversationStatus.Value.ToString() : "null");

      bool res = false;
      ResponseMessage = null;

      string requestName = RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest ? "single request " + RequestMessage.Request.SingleRequest.RequestTypeCase.ToString() : "conversation request " + RequestMessage.Request.ConversationRequest.RequestTypeCase.ToString();

      // RequiredRole contains one or more roles and the current server has to have at least one of them.
      if ((RequiredRole == null) || ((roleServer.Roles & (uint)RequiredRole.Value) != 0))
      {
        if (RequiredConversationStatus == null)
        {
          res = true;
        }
        else
        {
          switch (RequiredConversationStatus.Value)
          {
            case ClientConversationStatus.NoConversation:
            case ClientConversationStatus.ConversationStarted:
              res = Client.ConversationStatus == RequiredConversationStatus.Value;
              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorBadConversationStatusResponse(RequestMessage);
              }
              break;

            case ClientConversationStatus.Verified:
              res = Client.ConversationStatus == RequiredConversationStatus.Value;
              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorUnauthorizedResponse(RequestMessage);
              }
              break;


            case ClientConversationStatus.ConversationAny:
              res = (Client.ConversationStatus == ClientConversationStatus.ConversationStarted)
                || (Client.ConversationStatus == ClientConversationStatus.Verified);

              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorBadConversationStatusResponse(RequestMessage);
              }
              break;

            default:
              log.Error("Unknown conversation status '{0}'.", Client.ConversationStatus);
              ResponseMessage = Client.MessageBuilder.CreateErrorInternalResponse(RequestMessage);
              break;
          }
        }
      }
      else
      {
        log.Warn("Received {0} on server without {1} role(s) (server roles are {2}).", requestName, RequiredRole.Value, roleServer.Roles);
        ResponseMessage = Client.MessageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Selects a common protocol version that both server and client support.
    /// </summary>
    /// <param name="ClientVersions">List of versions that the client supports. The list is ordered by client's preference.</param>
    /// <param name="SelectedCommonVersion">If the function succeeds, this is set to the selected version that both client and server support.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool GetCommonSupportedVersion(IEnumerable<ByteString> ClientVersions, out SemVer SelectedCommonVersion)
    {
      log.Trace("()");
      SelectedCommonVersion = SemVer.Invalid;

      SemVer selectedVersion = SemVer.Invalid;
      bool res = false;
      foreach (ByteString clVersion in ClientVersions)
      {
        SemVer version = new SemVer(clVersion);
        if (version.Equals(SemVer.V100))
        {
          SelectedCommonVersion = version;
          selectedVersion = version;
          res = true;
          break;
        }
      }

      if (res) log.Trace("(-):{0},SelectedCommonVersion='{1}'", res, selectedVersion);
      else log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Processes PingRequest message from client.
    /// <para>Simply copies the payload to a new ping response message.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessagePingRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      PingRequest pingRequest = RequestMessage.Request.SingleRequest.Ping;

      ProxProtocolMessage res = messageBuilder.CreatePingResponse(RequestMessage, pingRequest.Payload.ToByteArray(), ProtocolHelper.GetUnixTimestampMs());

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes ListRolesRequest message from client.
    /// <para>Obtains a list of role servers and returns it in the response.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessageListRolesRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Primary, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      ListRolesRequest listRolesRequest = RequestMessage.Request.SingleRequest.ListRoles;

      List<Iop.Proximityserver.ServerRole> roles = GetRolesFromServerComponent();
      res = messageBuilder.CreateListRolesResponse(RequestMessage, roles);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Creates a list of role servers to be sent to the requesting client.
    /// The information about role servers can be obtained from Network.Server component.
    /// </summary>
    /// <returns>List of role server descriptions.</returns>
    private List<Iop.Proximityserver.ServerRole> GetRolesFromServerComponent()
    {
      log.Trace("()");

      List<Iop.Proximityserver.ServerRole> res = new List<Iop.Proximityserver.ServerRole>();

      foreach (TcpRoleServer<IncomingClient> roleServer in serverComponent.GetRoleServers())
      {
        foreach (ServerRole role in Enum.GetValues(typeof(ServerRole)))
        {
          if ((roleServer.Roles & (uint)role) != 0)
          {
            bool skip = false;
            ServerRoleType srt = ServerRoleType.Primary;
            switch (role)
            {
              case ServerRole.Primary: srt = ServerRoleType.Primary; break;
              case ServerRole.Neighbor: srt = ServerRoleType.Neighbor; break;
              case ServerRole.Client: srt = ServerRoleType.Client; break;
              default:
                skip = true;
                break;
            }

            if (!skip)
            {
              Iop.Proximityserver.ServerRole item = new Iop.Proximityserver.ServerRole()
              {
                Role = srt,
                Port = (uint)roleServer.EndPoint.Port,
                IsTcp = true,
                IsTls = roleServer.UseTls
              };
              res.Add(item);
            }
          }
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }



    /// <summary>
    /// Processes StartConversationRequest message from client.
    /// <para>Initiates a conversation with the client provided that there is a common version of the protocol supported by both sides.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessageStartConversationRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, null, ClientConversationStatus.NoConversation, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      StartConversationRequest startConversationRequest = RequestMessage.Request.ConversationRequest.Start;
      byte[] clientChallenge = startConversationRequest.ClientChallenge.ToByteArray();
      byte[] pubKey = startConversationRequest.PublicKey.ToByteArray();

      if (clientChallenge.Length == ProxMessageBuilder.ChallengeDataSize)
      {
        if ((0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes))
        {
          SemVer version;
          if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
          {
            Client.PublicKey = pubKey;
            Client.IdentityId = Crypto.Sha256(Client.PublicKey);

            if (clientList.AddNetworkPeerWithIdentity(Client))
            {
              Client.MessageBuilder.SetProtocolVersion(version);

              byte[] challenge = new byte[ProxMessageBuilder.ChallengeDataSize];
              Crypto.Rng.GetBytes(challenge);
              Client.AuthenticationChallenge = challenge;
              Client.ConversationStatus = ClientConversationStatus.ConversationStarted;

              log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
                Client.RemoteEndPoint, Client.ConversationStatus, version, Client.PublicKey.ToHex(), Client.IdentityId.ToHex(), Client.AuthenticationChallenge.ToHex());

              res = messageBuilder.CreateStartConversationResponse(RequestMessage, version, Config.Configuration.Keys.PublicKey, Client.AuthenticationChallenge, clientChallenge);
            }
            else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
          }
          else
          {
            log.Warn("Client and server are incompatible in protocol versions.");
            res = messageBuilder.CreateErrorUnsupportedResponse(RequestMessage);
          }
        }
        else
        {
          log.Warn("Client send public key of invalid length of {0} bytes.", pubKey.Length);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "publicKey");
        }
      }
      else
      {
        log.Warn("Client send clientChallenge, which is {0} bytes long, but it should be {1} bytes long.", clientChallenge.Length, ProxMessageBuilder.ChallengeDataSize);
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "clientChallenge");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes VerifyIdentityRequest message from client.
    /// <para>It verifies the identity's public key against the signature of the challenge provided during the start of the conversation. 
    /// If everything is OK, the status of the conversation is upgraded to Verified.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessageVerifyIdentityRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client | ServerRole.Neighbor, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      VerifyIdentityRequest verifyIdentityRequest = RequestMessage.Request.ConversationRequest.VerifyIdentity;

      byte[] challenge = verifyIdentityRequest.Challenge.ToByteArray();
      if (StructuralEqualityComparer<byte[]>.Default.Equals(challenge, Client.AuthenticationChallenge))
      {
        if (messageBuilder.VerifySignedConversationRequestBody(RequestMessage, verifyIdentityRequest, Client.PublicKey))
        {
          log.Debug("Identity '{0}' successfully verified its public key.", Client.IdentityId.ToHex());
          Client.ConversationStatus = ClientConversationStatus.Verified;
          res = messageBuilder.CreateVerifyIdentityResponse(RequestMessage);
        }
        else
        {
          log.Warn("Client's challenge signature is invalid.");
          res = messageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        }
      }
      else
      {
        log.Warn("Challenge provided in the request does not match the challenge created by the proximity server.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "challenge");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes CreateActivityRequest message from client.
    /// <para>It creates new activity on behalf of the client and initiates its propagation into the neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageCreateActivityRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      CreateActivityRequest createActivityRequest = RequestMessage.Request.ConversationRequest.CreateActivity;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      ProxProtocolMessage errorResponse;
      if (ValidateCreateActivityRequest(createActivityRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        GpsLocation activityLocation = new GpsLocation(createActivityRequest.Latitude, createActivityRequest.Longitude);

        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          StrongBox<byte[]> nearestServerId = new StrongBox<byte[]>(null);
          List<byte[]> ignoreServerIds = new List<byte[]>(createActivityRequest.IgnoreServerIds.Select(i => i.ToByteArray()));
          if (await unitOfWork.NeighborRepository.IsServerNearestToLocationAsync(activityLocation, ignoreServerIds, nearestServerId))
          {
            PrimaryActivity activity = new PrimaryActivity()
            {
              Version = new SemVer(createActivityRequest.Version).ToByteArray(),
              ActivityId = createActivityRequest.Id,
              OwnerIdentityId = Client.IdentityId,
              OwnerPublicKey = Client.PublicKey,
              OwnerProfileServerId = createActivityRequest.ProfileServerContact.NetworkId.ToByteArray(),
              OwnerProfileServerIpAddress = createActivityRequest.ProfileServerContact.IpAddress.ToByteArray(),
              OwnerProfileServerPrimaryPort = (ushort)createActivityRequest.ProfileServerContact.PrimaryPort,
              Type = createActivityRequest.Type,
              LocationLatitude = activityLocation.Latitude,
              LocationLongitude = activityLocation.Longitude,
              PrecisionRadius = createActivityRequest.Precision,
              StartTime = ProtocolHelper.UnixTimestampMsToDateTime(createActivityRequest.StartTime).Value,
              ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(createActivityRequest.ExpirationTime).Value,
              ExtraData = createActivityRequest.ExtraData
            };

            StrongBox<int> existingActivityId = new StrongBox<int>(0);
            if (await unitOfWork.PrimaryActivityRepository.CreateAndPropagateAsync(activity, existingActivityId))
            {
              res = messageBuilder.CreateCreateActivityResponse(RequestMessage);
            }
            else if (existingActivityId.Value != 0)
            {
              log.Debug("Activity with the activity ID {0} and owner identity ID '{1}' already exists with database ID {2}.", activity.ActivityId, activity.OwnerIdentityId.ToHex(), existingActivityId.Value);
              res = messageBuilder.CreateErrorAlreadyExistsResponse(RequestMessage);
            }
          }
          else
          {
            if (nearestServerId.Value != null)
            {
              log.Debug("Creation of activity rejected because the server is not the nearest proximity server.");
              res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, nearestServerId.Value.ToHex());
            }
            // else Internal error
          }
        }
      }
      else res = errorResponse;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Checks whether the create activity request is valid.
    /// <para>This function does not verify the uniqueness of the activity identifier.
    /// It also does not verify whether a closer proximity server exists.</para>
    /// </summary>
    /// <param name="CreateActivityRequest">Create activity request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the create activity request can be applied, false otherwise.</returns>
    private bool ValidateCreateActivityRequest(CreateActivityRequest CreateActivityRequest, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;

      string details = null;

      SemVer version = new SemVer(CreateActivityRequest.Version);

      // Currently only supported version is 1.0.0.
      if (!version.Equals(SemVer.V100))
      {
        log.Debug("Unsupported version '{0}'.", version);
        details = "version";
      }


      if (details == null)
      {
        uint activityId = CreateActivityRequest.Id;

        // 0 is not a valid activity identifier.
        if (activityId == 0)
        {
          log.Debug("Invalid activity ID '{0}'.", activityId);
          details = "id";
        }
      }

      if (details == null)
      {
        ServerContactInfo sci = CreateActivityRequest.ProfileServerContact;
        bool networkIdValid = sci.NetworkId.Length == ProtocolHelper.NetworkIdentifierLength;
        IPAddress ipAddress = IPAddressExtensions.IpFromBytes(sci.IpAddress.ToByteArray());
        bool ipAddressValid = (ipAddress != null) && (Config.Configuration.TestModeEnabled || !ipAddress.IsReservedOrLocal());
        bool portValid = (1 <= sci.PrimaryPort) && (sci.PrimaryPort <= 65535);

        if (!networkIdValid || !ipAddressValid || !portValid)
        {
          log.Debug("Profile server contact's network ID is {0}, IP address is {1}, port is {2}.", networkIdValid ? "valid" : "invalid", ipAddressValid ? "valid" : "invalid", portValid ? "valid" : "invalid");

          if (!networkIdValid) details = "profileServerContact.networkId";
          else if (!ipAddressValid) details = "profileServerContact.ipAddress";
          else if (!portValid) details = "profileServerContact.primaryPort";
        }
      }

      if (details == null)
      {
        string activityType = CreateActivityRequest.Type;
        if (activityType == null) activityType = "";

        int byteLen = Encoding.UTF8.GetByteCount(activityType);
        if (byteLen > ActivityBase.MaxActivityTypeLengthBytes)
        {
          log.Debug("Activity type too large ({0} bytes, limit is {1}).", byteLen, ActivityBase.MaxActivityTypeLengthBytes);
          details = "type";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(CreateActivityRequest.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, CreateActivityRequest.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", CreateActivityRequest.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", CreateActivityRequest.Longitude);
          details = "longitude";
        }
      }

      if (details == null)
      {
        uint precision = CreateActivityRequest.Precision;
        bool precisionValid = (0 <= precision) && (precision <= ActivityBase.MaxLocationPrecision);
        if (!precisionValid)
        {
          log.Debug("Precision '{0}' is not an integer between 0 and {1}.", precision, ActivityBase.MaxLocationPrecision);
          details = "precision";
        }
      }


      if (details == null)
      {
        DateTime? startTime = ProtocolHelper.UnixTimestampMsToDateTime(CreateActivityRequest.StartTime);
        DateTime? expirationTime = ProtocolHelper.UnixTimestampMsToDateTime(CreateActivityRequest.ExpirationTime);

        if (startTime == null)
        {
          log.Debug("Invalid activity start time timestamp '{0}'.", CreateActivityRequest.StartTime);
          details = "startTime";
        }
        else if (expirationTime == null)
        {
          log.Debug("Invalid activity expiration time timestamp '{0}'.", CreateActivityRequest.ExpirationTime);
          details = "expirationTime";
        }
        else
        {
          if (startTime > expirationTime)
          {
            log.Debug("Activity expiration time has to be greater than or equal to its start time.");
            details = "expirationTime";
          }
          else if (expirationTime.Value > DateTime.UtcNow.AddHours(ActivityBase.MaxActivityLifeTimeHours))
          {
            log.Debug("Activity expiration time {0} is more than {1} hours in the future.", expirationTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), ActivityBase.MaxActivityLifeTimeHours);
            details = "expirationTime";
          }
        }
      }

      if (details == null)
      {
        string extraData = CreateActivityRequest.ExtraData;
        if (extraData == null) extraData = "";


        // Extra data is semicolon separated 'key=value' list, max ActivityBase.MaxActivityExtraDataLengthBytes bytes long.
        int byteLen = Encoding.UTF8.GetByteCount(extraData);
        if (byteLen > ActivityBase.MaxActivityExtraDataLengthBytes)
        {
          log.Debug("Extra data too large ({0} bytes, limit is {1}).", byteLen, ActivityBase.MaxActivityExtraDataLengthBytes);
          details = "extraData";
        }
      }

      if (details == null)
      {
        int index = 0;
        foreach (ByteString serverId in CreateActivityRequest.IgnoreServerIds)
        {
          if (serverId.Length != ProtocolHelper.NetworkIdentifierLength)
          {
            log.Debug("Ignored server ID #{0} is not a valid network ID as its length is {1} bytes.", index, serverId.Length);
            details = "ignoreServerIds";
            break;
          }

          index++;
        }
      }

      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes UpdateActivityRequest message from client.
    /// <para>Updates existing activity of the client and initiates propagation of the change into the neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageUpdateActivityRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      UpdateActivityRequest updateActivityRequest = RequestMessage.Request.ConversationRequest.UpdateActivity;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      ProxProtocolMessage errorResponse;
      if (ValidateUpdateActivityRequest(updateActivityRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          bool error = false;
          bool migrateActivity = false;
          StrongBox<byte[]> nearestServerId = new StrongBox<byte[]>(null);
          if (updateActivityRequest.SetLocation)
          {
            GpsLocation activityLocation = new GpsLocation(updateActivityRequest.Latitude, updateActivityRequest.Longitude);
            List<byte[]> ignoreServerIds = new List<byte[]>(updateActivityRequest.IgnoreServerIds.Select(i => i.ToByteArray()));
            if (!await unitOfWork.NeighborRepository.IsServerNearestToLocationAsync(activityLocation, ignoreServerIds, nearestServerId, ProxMessageBuilder.ActivityMigrationDistanceTolerance))
            {
              if (nearestServerId.Value != null)
              {
                migrateActivity = true;
                log.Debug("Activity's new location is outside the reach of this proximity server, the activity will be deleted from the database.");
                res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, nearestServerId.Value.ToHex());
              }
              else error = true;
            }
          }

          if (!error)
          {
            if (!migrateActivity)
            {
              Status status = await unitOfWork.PrimaryActivityRepository.UpdateAndPropagateAsync(updateActivityRequest, Client.IdentityId);
              switch (status)
              {
                case Status.Ok:
                  // Activity was updated and the change will be propagated to the neighborhood.
                  res = messageBuilder.CreateUpdateActivityResponse(RequestMessage);
                  break;

                case Status.ErrorNotFound:
                  // Activity of given ID not found among activities created by the client.
                  res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                  break;

                case Status.ErrorInvalidValue:
                  // Activity's new start time is greater than its new expiration time.
                  res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "expirationTime");
                  break;

                default:
                  // Internal error
                  break;
              }
            }
            else
            {
              // The update is rejected, there is a closer server to the activity's new location.
              StrongBox<bool> notFound = new StrongBox<bool>(false);
              if (await unitOfWork.PrimaryActivityRepository.DeleteAndPropagateAsync(updateActivityRequest.Id, Client.IdentityId, notFound))
              {
                if (!notFound.Value)
                {
                  // The activity was deleted from the database and this change will be propagated to the neighborhood.
                  log.Debug("Update rejected, activity ID {0}, owner ID '{1}' deleted.", updateActivityRequest.Id, Client.IdentityId);
                  res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, nearestServerId.Value.ToHex());
                }
                else
                {
                  // Activity of given ID not found among activities created by the client.
                  res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                }
              }
            }
          }
          // else Internal error
        }
      }
      else res = errorResponse;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Checks whether the update activity request is valid.
    /// <para>This function does not verify whether the activity exists.
    /// It also does not verify whether a closer proximity server exists to which the activity should be migrated.
    /// It also does not verify whether the changed activity's start time will be before expiration time.</para>
    /// </summary>
    /// <param name="UpdateActivityRequest">Update activity request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the update activity request can be applied, false otherwise.</returns>
    private bool ValidateUpdateActivityRequest(UpdateActivityRequest UpdateActivityRequest, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;

      string details = null;

      if (!UpdateActivityRequest.SetLocation
        && !UpdateActivityRequest.SetPrecision
        && !UpdateActivityRequest.SetStartTime
        && !UpdateActivityRequest.SetExpirationTime
        && !UpdateActivityRequest.SetExtraData)
      {
        log.Debug("Update request does not want to change anything.");
        details = "set*";
      }

      if (details == null)
      {
        uint activityId = UpdateActivityRequest.Id;

        // 0 is not a valid activity identifier.
        if (activityId == 0)
        {
          log.Debug("Invalid activity ID '{0}'.", activityId);
          details = "id";
        }
      }

      if ((details == null) && UpdateActivityRequest.SetLocation)
      {
        GpsLocation locLat = new GpsLocation(UpdateActivityRequest.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, UpdateActivityRequest.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", UpdateActivityRequest.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", UpdateActivityRequest.Longitude);
          details = "longitude";
        }
      }

      if ((details == null) && UpdateActivityRequest.SetPrecision)
      {
        uint precision = UpdateActivityRequest.Precision;
        bool precisionValid = (0 <= precision) && (precision <= ActivityBase.MaxLocationPrecision);
        if (!precisionValid)
        {
          log.Debug("Precision '{0}' is not an integer between 0 and {1}.", precision, ActivityBase.MaxLocationPrecision);
          details = "precision";
        }
      }


      if ((details == null) && UpdateActivityRequest.SetStartTime)
      {
        DateTime? startTime = ProtocolHelper.UnixTimestampMsToDateTime(UpdateActivityRequest.StartTime);
        if (startTime == null)
        {
          log.Debug("Invalid activity start time timestamp '{0}'.", UpdateActivityRequest.StartTime);
          details = "startTime";
        }
      }

      if ((details == null) && UpdateActivityRequest.SetExpirationTime)
      {
        DateTime? expirationTime = ProtocolHelper.UnixTimestampMsToDateTime(UpdateActivityRequest.ExpirationTime);
        if (expirationTime != null)
        {
          if (expirationTime.Value > DateTime.UtcNow.AddHours(ActivityBase.MaxActivityLifeTimeHours))
          {
            log.Debug("Activity expiration time {0} is more than {1} hours in the future.", expirationTime.Value.ToString("yyyy-MM-dd HH:mm:ss"), ActivityBase.MaxActivityLifeTimeHours);
            details = "expirationTime";
          }
        }
        else
        {
          log.Debug("Invalid activity expiration time timestamp '{0}'.", UpdateActivityRequest.ExpirationTime);
          details = "expirationTime";
        }
      }

      if ((details == null) && UpdateActivityRequest.SetExtraData)
      {
        string extraData = UpdateActivityRequest.ExtraData;
        if (extraData == null) extraData = "";


        // Extra data is semicolon separated 'key=value' list, max ActivityBase.MaxActivityExtraDataLengthBytes bytes long.
        int byteLen = Encoding.UTF8.GetByteCount(extraData);
        if (byteLen > ActivityBase.MaxActivityExtraDataLengthBytes)
        {
          log.Debug("Extra data too large ({0} bytes, limit is {1}).", byteLen, ActivityBase.MaxActivityExtraDataLengthBytes);
          details = "extraData";
        }
      }

      if (details == null)
      {
        int index = 0;
        foreach (ByteString serverId in UpdateActivityRequest.IgnoreServerIds)
        {
          if (serverId.Length != ProtocolHelper.NetworkIdentifierLength)
          {
            log.Debug("Ignored server ID #{0} is not a valid network ID as its length is {1} bytes.", index, serverId.Length);
            details = "ignoreServerIds";
            break;
          }

          index++;
        }
      }

      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);


      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Processes DeleteActivityRequest message from client.
    /// <para>Deletes existing activity of the client and initiates propagation of the change into the neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageDeleteActivityRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      DeleteActivityRequest deleteActivityRequest = RequestMessage.Request.ConversationRequest.DeleteActivity;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        StrongBox<bool> notFound = new StrongBox<bool>(false);
        if (await unitOfWork.PrimaryActivityRepository.DeleteAndPropagateAsync(deleteActivityRequest.Id, Client.IdentityId, notFound))
        {
          if (!notFound.Value)
          {
            // The activity was deleted from the database and this change will be propagated to the neighborhood.
            log.Debug("Activity ID {0}, owner ID '{1}' deleted.", deleteActivityRequest.Id, Client.IdentityId);
            res = messageBuilder.CreateDeleteActivityResponse(RequestMessage);
          }
          else
          {
            // Activity of given ID not found among activities created by the client.
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Minimal number of results we ask for from database. This limit prevents doing small database queries 
    /// when there are many activities to be explored but very few of them actually match the client's criteria. 
    /// Since not all filtering is done in the SQL query, we need to prevent ourselves from putting to much 
    /// pressure on the database in those edge cases.
    /// </summary>
    public const int ActivitySearchBatchSizeMin = 1000;

    /// <summary>
    /// Multiplication factor to count maximal size of the batch from the number of required results.
    /// Not all filtering is done in the SQL query. This means that in order to get a certain amount of results 
    /// that the client asks for, it is likely that we need to load more records from the database.
    /// This value provides information about how many times more do we load.
    /// </summary>
    public const decimal ActivitySearchBatchSizeMaxFactor = 10.0m;

    /// <summary>Maximum amount of time in milliseconds that a single search query can take.</summary>
    public const int ActivitySearchMaxTimeMs = 10000;

    /// <summary>Maximum amount of time in milliseconds that we want to spend on regular expression matching of extraData.</summary>
    public const int ActivitySearchMaxExtraDataMatchingTimeTotalMs = 1000;

    /// <summary>Maximum amount of time in milliseconds that we want to spend on regular expression matching of extraData of a single activity.</summary>
    public const int ActivitySearchMaxExtraDataMatchingTimeSingleMs = 50;

    /// <summary>Maximum number of results the proximity server can send in the response.</summary>
    public const int ActivitySearchMaxResponseRecords = 1000;

    /// <summary>Maximum number of results the proximity server can store in total for a single client if images are included.</summary>
    public const int ActivitySearchMaxTotalRecords = 10000;


    /// <summary>
    /// Processes ActivitySearchRequest message from client.
    /// <para>Performs a search operation to find all matching activities that this proximity server manages, 
    /// possibly including activities managed in the server's neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageActivitySearchRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      ActivitySearchRequest activitySearchRequest = RequestMessage.Request.ConversationRequest.ActivitySearch;

      ProxProtocolMessage errorResponse;
      if (ValidateActivitySearchRequest(activitySearchRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          Stopwatch watch = new Stopwatch();
          try
          {
            uint maxResults = activitySearchRequest.MaxTotalRecordCount;
            uint maxResponseResults = activitySearchRequest.MaxResponseRecordCount;
            uint activityIdFilter = activitySearchRequest.Id;
            byte[] ownerIdFilter = (activitySearchRequest.OwnerNetworkId != null) && (activitySearchRequest.OwnerNetworkId.Length > 0) ? activitySearchRequest.OwnerNetworkId.ToByteArray() : null;
            string typeFilter = activitySearchRequest.Type;
            DateTime? startNotAfterFilter = activitySearchRequest.StartNotAfter != 0 ? ProtocolHelper.UnixTimestampMsToDateTime(activitySearchRequest.StartNotAfter) : null;
            DateTime? expirationNotBeforeFilter = activitySearchRequest.ExpirationNotBefore != 0 ? ProtocolHelper.UnixTimestampMsToDateTime(activitySearchRequest.ExpirationNotBefore) : null;
            GpsLocation locationFilter = activitySearchRequest.Latitude != GpsLocation.NoLocationLocationType ? new GpsLocation(activitySearchRequest.Latitude, activitySearchRequest.Longitude) : null;
            uint radiusFilter = activitySearchRequest.Radius;
            string extraDataFilter = activitySearchRequest.ExtraData;

            watch.Start();

            // First, we try to find enough results among primary activities of this proximity server.
            List<ActivityNetworkInformation> searchResultsNeighborhood = new List<ActivityNetworkInformation>();
            List<ActivityNetworkInformation> searchResultsLocal = await ActivitySearchAsync(unitOfWork, unitOfWork.PrimaryActivityRepository, maxResults, activityIdFilter, ownerIdFilter, typeFilter,
              startNotAfterFilter, expirationNotBeforeFilter, locationFilter, radiusFilter, extraDataFilter, watch);
            if (searchResultsLocal != null)
            {
              bool localServerOnly = true;
              bool error = false;
              // If possible and needed we try to find more results among identities hosted in this profile server's neighborhood.
              if (!activitySearchRequest.IncludePrimaryOnly && (searchResultsLocal.Count < maxResults))
              {
                localServerOnly = false;
                maxResults -= (uint)searchResultsLocal.Count;
                searchResultsNeighborhood = await ActivitySearchAsync(unitOfWork, unitOfWork.NeighborActivityRepository, maxResults, activityIdFilter, ownerIdFilter, typeFilter,
                  startNotAfterFilter, expirationNotBeforeFilter, locationFilter, radiusFilter, extraDataFilter, watch);
                if (searchResultsNeighborhood == null)
                {
                  log.Error("Activity search among neighborhood activities failed.");
                  error = true;
                }
              }

              if (!error)
              {
                // Now we have all required results in searchResultsLocal and searchResultsNeighborhood.
                // If the number of results is small enough to fit into a single response, we send them all.
                // Otherwise, we save them to the session context and only send first part of them.
                List<ActivityNetworkInformation> allResults = searchResultsLocal;
                allResults.AddRange(searchResultsNeighborhood);
                List<ActivityNetworkInformation> responseResults = allResults;
                log.Debug("Total number of matching activities is {0}, from which {1} are local, {2} are from neighbors.", allResults.Count, searchResultsLocal.Count, searchResultsNeighborhood.Count);
                if (maxResponseResults < allResults.Count)
                {
                  log.Trace("All results can not fit into a single response (max {0} results).", maxResponseResults);
                  // We can not send all results, save them to session.
                  Client.SaveActivitySearchResults(allResults);

                  // And send the maximum we can in the response.
                  responseResults = new List<ActivityNetworkInformation>();
                  responseResults.AddRange(allResults.GetRange(0, (int)maxResponseResults));
                }

                List<byte[]> coveredServers = await ActivitySearchGetCoveredServersAsync(unitOfWork, localServerOnly);
                res = messageBuilder.CreateActivitySearchResponse(RequestMessage, (uint)allResults.Count, maxResponseResults, coveredServers, responseResults);
              }
            }
            else log.Error("Activity search among primary activities failed.");
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          watch.Stop();
        }
      }
      else res = errorResponse;

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Obtains list of covered proximity servers for a activity search query.
    /// <para>If the search used the local activity database only, the result is simply ID of the local proximity server. 
    /// Otherwise, it is its ID and a list of all its neighbors' IDs.</para>
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="LocalServerOnly">true if the search query only used the local proximity server, false otherwise.</param>
    /// <returns>List of network IDs of proximity servers whose database could be used to create the result.</returns>
    /// <remarks>Note that the covered proximity server list is not guaranteed to be accurate. The search query processing is not atomic 
    /// and during the process it may happen that a neighbor server can be added or removed from the list of neighbors.</remarks>
    private async Task<List<byte[]>> ActivitySearchGetCoveredServersAsync(UnitOfWork UnitOfWork, bool LocalServerOnly)
    {
      log.Trace("()");

      List<byte[]> res = new List<byte[]>();
      res.Add(serverComponent.ServerId);
      if (!LocalServerOnly)
      {
        List<byte[]> neighborIds = (await UnitOfWork.NeighborRepository.GetAsync(null, null, true)).Select(n => n.NeighborId).ToList();
        res.AddRange(neighborIds);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }

    /// <summary>
    /// Checks whether the activity search request is valid.
    /// </summary>
    /// <param name="ActivitySearchRequest">Activity search request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the search request is valid, false otherwise.</returns>
    private bool ValidateActivitySearchRequest(ActivitySearchRequest ActivitySearchRequest, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      int responseResultLimit = ActivitySearchMaxResponseRecords;
      int totalResultLimit = ActivitySearchMaxTotalRecords;

      bool maxResponseRecordCountValid = (1 <= ActivitySearchRequest.MaxResponseRecordCount)
        && (ActivitySearchRequest.MaxResponseRecordCount <= responseResultLimit)
        && (ActivitySearchRequest.MaxResponseRecordCount <= ActivitySearchRequest.MaxTotalRecordCount);
      if (!maxResponseRecordCountValid)
      {
        log.Debug("Invalid maxResponseRecordCount value '{0}'.", ActivitySearchRequest.MaxResponseRecordCount);
        details = "maxResponseRecordCount";
      }

      if (details == null)
      {
        bool maxTotalRecordCountValid = (1 <= ActivitySearchRequest.MaxTotalRecordCount) && (ActivitySearchRequest.MaxTotalRecordCount <= totalResultLimit);
        if (!maxTotalRecordCountValid)
        {
          log.Debug("Invalid maxTotalRecordCount value '{0}'.", ActivitySearchRequest.MaxTotalRecordCount);
          details = "maxTotalRecordCount";
        }
      }

      if ((details == null) && ((ActivitySearchRequest.OwnerNetworkId == null) || (ActivitySearchRequest.OwnerNetworkId.Length == 0)))
      {
        // If ActivitySearchRequest.Id is not zero, ActivitySearchRequest.OwnerNetworkId must not be empty.
        bool ownerNetworkIdValid = ActivitySearchRequest.Id == 0;
        if (!ownerNetworkIdValid)
        {
          log.Debug("Owner network ID is not used but activity ID is non-zero.");
          details = "ownerNetworkId";
        }
      }

      if ((details == null) && (ActivitySearchRequest.OwnerNetworkId != null) && (ActivitySearchRequest.OwnerNetworkId.Length > 0))
      {
        bool ownerNetworkIdValid = ActivitySearchRequest.OwnerNetworkId.Length == ProtocolHelper.NetworkIdentifierLength;
        if (!ownerNetworkIdValid)
        {
          log.Debug("Invalid owner network ID length '{0}'.", ActivitySearchRequest.OwnerNetworkId.Length);
          details = "ownerNetworkId";
        }
      }

      if ((details == null) && (ActivitySearchRequest.Type != null))
      {
        bool typeValid = Encoding.UTF8.GetByteCount(ActivitySearchRequest.Type) <= ProxMessageBuilder.MaxActivitySearchTypeLengthBytes;
        if (!typeValid)
        {
          log.Debug("Invalid type value length '{0}'.", ActivitySearchRequest.Type.Length);
          details = "type";
        }
      }

      if ((details == null) && ((ActivitySearchRequest.StartNotAfter != 0) || (ActivitySearchRequest.ExpirationNotBefore != 0)))
      {
        DateTime? startNotAfter = null;
        DateTime? expirationNotBefore = null;

        if (ActivitySearchRequest.StartNotAfter != 0)
        {
          startNotAfter = ProtocolHelper.UnixTimestampMsToDateTime(ActivitySearchRequest.StartNotAfter);
          bool startNotAfterValid = startNotAfter != null;
          if (!startNotAfterValid)
          {
            log.Debug("Start not after {0} is not a valid timestamp value.", ActivitySearchRequest.StartNotAfter);
            details = "startNotAfter";
          }
        }

        if ((details == null) && (ActivitySearchRequest.ExpirationNotBefore != 0))
        {
          expirationNotBefore = ProtocolHelper.UnixTimestampMsToDateTime(ActivitySearchRequest.ExpirationNotBefore);
          bool expirationNotBeforeValid = expirationNotBefore != null;
          if (!expirationNotBeforeValid)
          {
            log.Debug("Expiration not before {0} is not a valid timestamp value.", ActivitySearchRequest.ExpirationNotBefore);
            details = "expirationNotBefore";
          }
          else if (ActivitySearchRequest.StartNotAfter != 0)
          {
            expirationNotBeforeValid = ActivitySearchRequest.StartNotAfter <= ActivitySearchRequest.ExpirationNotBefore;
            if (!expirationNotBeforeValid)
            {
              log.Debug("Expiration not before {0} is smaller than start not after {1}.", ActivitySearchRequest.StartNotAfter, ActivitySearchRequest.ExpirationNotBefore);
              details = "expirationNotBefore";
            }
          }
        }
      }

      if ((details == null) && (ActivitySearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        GpsLocation locLat = new GpsLocation(ActivitySearchRequest.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, ActivitySearchRequest.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", ActivitySearchRequest.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", ActivitySearchRequest.Longitude);
          details = "longitude";
        }
      }

      if ((details == null) && (ActivitySearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        bool radiusValid = ActivitySearchRequest.Radius > 0;
        if (!radiusValid)
        {
          log.Debug("Invalid radius value '{0}'.", ActivitySearchRequest.Radius);
          details = "radius";
        }
      }

      if ((details == null) && (ActivitySearchRequest.ExtraData != null))
      {
        bool validLength = (Encoding.UTF8.GetByteCount(ActivitySearchRequest.ExtraData) <= ProxMessageBuilder.MaxActivitySearchExtraDataLengthBytes);
        bool extraDataValid = RegexTypeValidator.ValidateRegex(ActivitySearchRequest.ExtraData);
        if (!validLength || !extraDataValid)
        {
          log.Debug("Invalid extraData regular expression filter.");
          details = "extraData";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Performs a search request on a repository to retrieve the list of activities that match specific criteria.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="Repository">Primary or neighborhood activity repository, which is queried.</param>
    /// <param name="MaxResults">Maximum number of results to retrieve.</param>
    /// <param name="ActivityIdFilter">Activity ID or 0 if activity ID is not known. If non-zero, <paramref name="OwnerIdFilter"/> must not be null.</param>
    /// <param name="OwnerIdFilter">Network identifier of the identity that created matching activities, or null if filtering by the owner is not required.</param>
    /// <param name="TypeFilter">Wildcard filter for activity type, or empty string if activity type filtering is not required.</param>
    /// <param name="StartNotAfterFilter">Maximal start time of activity, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBeforeFilter">Minimal expiration time of activity, or null if expiration time filtering is not required.</param>
    /// <param name="LocationFilter">If not null, this value together with <paramref name="RadiusFilter"/> provide specification of target area, in which the identities has to have their location set. If null, GPS location filtering is not required.</param>
    /// <param name="RadiusFilter">If <paramref name="LocationFilter"/> is not null, this is the target area radius with the centre in <paramref name="LocationFilter"/>.</param>
    /// <param name="ExtraDataFilter">Regular expression filter for identity's extraData information, or empty string if extraData filtering is not required.</param>
    /// <param name="TimeoutWatch">Stopwatch instance that is used to terminate the search query in case the execution takes too long. The stopwatch has to be started by the caller before calling this method.</param>
    /// <returns>List of activity network informations of activities that match the specific criteria.</returns>
    /// <remarks>In order to prevent DoS attacks, we require the search to complete within small period of time. 
    /// One the allowed time is up, the search is terminated even if we do not have enough results yet and there 
    /// is still a possibility to get more.</remarks>
    private async Task<List<ActivityNetworkInformation>> ActivitySearchAsync<T>(UnitOfWork UnitOfWork, ActivityRepository<T> Repository, uint MaxResults, uint ActivityIdFilter, byte[] OwnerIdFilter, string TypeFilter, DateTime? StartNotAfterFilter, DateTime? ExpirationNotBeforeFilter, GpsLocation LocationFilter, uint RadiusFilter, string ExtraDataFilter, Stopwatch TimeoutWatch) where T : ActivityBase
    {
      log.Trace("(Repository:{0},MaxResults:{1},ActivityIdFilter:{2},OwnerIdFilter:'{3}',TypeFilter:'{4}',StartNotAfterFilter:{5},ExpirationNotBeforeFilter:{6},LocationFilter:[{7}],RadiusFilter:{8},ExtraDataFilter:'{9}')",
        Repository, MaxResults, ActivityIdFilter, OwnerIdFilter != null ? OwnerIdFilter.ToHex() : "", TypeFilter, StartNotAfterFilter != null ? StartNotAfterFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null",
        ExpirationNotBeforeFilter != null ? ExpirationNotBeforeFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", LocationFilter, RadiusFilter, ExtraDataFilter);

      List<ActivityNetworkInformation> res = new List<ActivityNetworkInformation>();

      uint batchSize = Math.Max(ActivitySearchBatchSizeMin, (uint)(MaxResults * ActivitySearchBatchSizeMaxFactor));
      uint offset = 0;

      RegexEval extraDataEval = !string.IsNullOrEmpty(ExtraDataFilter) ? new RegexEval(ExtraDataFilter, ActivitySearchMaxExtraDataMatchingTimeSingleMs, ActivitySearchMaxExtraDataMatchingTimeTotalMs) : null;

      long totalTimeMs = ActivitySearchMaxTimeMs;

      bool isPrimary = Repository is PrimaryActivityRepository;

      // List of network contact information to neighbors mapped by their network ID.
      Dictionary<byte[], ServerContactInfo> neighborServerContactInfo = null;
      if (!isPrimary)
      {
        neighborServerContactInfo = new Dictionary<byte[], ServerContactInfo>(StructuralEqualityComparer<byte[]>.Default);
        List<Neighbor> neighbors = (await UnitOfWork.NeighborRepository.GetAsync(null, null, true)).ToList();
        foreach (Neighbor neighbor in neighbors)
        {
          ServerContactInfo sci = new ServerContactInfo();
          sci.NetworkId = ProtocolHelper.ByteArrayToByteString(neighbor.NeighborId);
          sci.PrimaryPort = (uint)neighbor.PrimaryPort;
          IPAddress ipAddr = null;
          if (IPAddress.TryParse(neighbor.IpAddress, out ipAddr))
          {
            sci.IpAddress = ProtocolHelper.ByteArrayToByteString(ipAddr.GetAddressBytes());
            neighborServerContactInfo.Add(neighbor.NeighborId, sci);
          }
          else log.Error("Neighbor ID '{0}' has invalid IP address '{1}'.", neighbor.NeighborId.ToHex(), neighbor.IpAddress);
        }
      }


      bool done = false;
      while (!done)
      {
        // Load result candidates from the database.
        bool noMoreResults = false;
        List<T> activities = null;
        try
        {
          activities = await Repository.ActivitySearchAsync(offset, batchSize, ActivityIdFilter, OwnerIdFilter, TypeFilter, StartNotAfterFilter, ExpirationNotBeforeFilter, LocationFilter, RadiusFilter);
          noMoreResults = (activities == null) || (activities.Count < batchSize);
          if (noMoreResults)
            log.Debug("Received {0}/{1} results from repository, no more results available.", activities != null ? activities.Count : 0, batchSize);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          done = true;
        }

        if (!done)
        {
          if (activities != null)
          {
            int accepted = 0;
            int filteredOutLocation = 0;
            int filteredOutExtraData = 0;
            foreach (T activity in activities)
            {
              // Terminate search if the time is up.
              if (totalTimeMs - TimeoutWatch.ElapsedMilliseconds < 0)
              {
                log.Debug("Time for search query ({0} ms) is up, terminating query.", totalTimeMs);
                done = true;
                break;
              }

              // Filter out activities that do not match exact location filter and extraData filter, mind the precision information.
              GpsLocation activityLocation = new GpsLocation(activity.LocationLatitude, activity.LocationLongitude);
              if (LocationFilter != null)
              {                
                double distance = GpsLocation.DistanceBetween(LocationFilter, activityLocation) - (double)activity.PrecisionRadius;
                bool withinArea = distance <= (double)RadiusFilter;
                if (!withinArea)
                {
                  filteredOutLocation++;
                  continue;
                }
              }

              if (!string.IsNullOrEmpty(ExtraDataFilter))
              {
                bool match = extraDataEval.Matches(activity.ExtraData);
                if (!match)
                {
                  filteredOutExtraData++;
                  continue;
                }
              }

              // Convert activity to search result format.
              ActivityNetworkInformation ani = new ActivityNetworkInformation()
              {
                IsPrimary = isPrimary,
                Version = ProtocolHelper.ByteArrayToByteString(activity.Version),
                Id = activity.ActivityId,
                OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(activity.OwnerPublicKey),
                ProfileServerContact = new ServerContactInfo()
                {
                  IpAddress = ProtocolHelper.ByteArrayToByteString(activity.OwnerProfileServerIpAddress),
                  NetworkId = ProtocolHelper.ByteArrayToByteString(activity.OwnerProfileServerId),
                  PrimaryPort = activity.OwnerProfileServerPrimaryPort,
                },
                Type = activity.Type != null ? activity.Type : "",
                Latitude = activityLocation.GetLocationTypeLatitude(),
                Longitude = activityLocation.GetLocationTypeLongitude(),
                Precision = activity.PrecisionRadius,
                StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(activity.StartTime),
                ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(activity.ExpirationTime),
                ExtraData = activity.ExtraData != null ? activity.ExtraData : ""
              };

              if (isPrimary)
              {
                byte[] primaryServerId = (activity as NeighborActivity).PrimaryServerId;
                ServerContactInfo serverContactInfo = null;
                if (neighborServerContactInfo.TryGetValue(primaryServerId, out serverContactInfo))
                {
                  ani.PrimaryServer = serverContactInfo;
                }
                else
                {
                  // This can actually happen because the search operation is not atomic.
                  // It means that neighbor was deleted before we finished the search operation.
                  // In that case we simply do not include this activity in the result.
                  continue;
                }
              }

              accepted++;

              res.Add(ani);
              if (res.Count >= MaxResults)
              {
                log.Debug("Target number of results {0} has been reached.", MaxResults);
                break;
              }
            }

            log.Info("Total number of examined records is {0}, {1} of them have been accepted, {2} filtered out by location, {3} filtered out by extra data filter.",
              accepted + filteredOutLocation + filteredOutExtraData, accepted, filteredOutLocation, filteredOutExtraData);
          }

          bool timedOut = totalTimeMs - TimeoutWatch.ElapsedMilliseconds < 0;
          if (timedOut) log.Debug("Time for search query ({0} ms) is up, terminating query.", totalTimeMs);
          done = noMoreResults || (res.Count >= MaxResults) || timedOut;
          offset += batchSize;
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Processes ActivitySearchPartRequest message from client.
    /// <para>Loads cached search results and sends them to the client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessageActivitySearchPartRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Client, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      ActivitySearchPartRequest activitySearchPartRequest = RequestMessage.Request.ConversationRequest.ActivitySearchPart;

      int cacheResultsCount;
      if (Client.GetActivitySearchResultsInfo(out cacheResultsCount))
      {
        int maxRecordCount = ActivitySearchMaxResponseRecords;
        bool recordIndexValid = (0 <= activitySearchPartRequest.RecordIndex) && (activitySearchPartRequest.RecordIndex < cacheResultsCount);
        bool recordCountValid = (0 <= activitySearchPartRequest.RecordCount) && (activitySearchPartRequest.RecordCount <= maxRecordCount)
          && (activitySearchPartRequest.RecordIndex + activitySearchPartRequest.RecordCount <= cacheResultsCount);

        if (recordIndexValid && recordCountValid)
        {
          List<ActivityNetworkInformation> cachedResults = Client.GetActivitySearchResults((int)activitySearchPartRequest.RecordIndex, (int)activitySearchPartRequest.RecordCount);
          if (cachedResults != null)
          {
            res = messageBuilder.CreateActivitySearchPartResponse(RequestMessage, activitySearchPartRequest.RecordIndex, activitySearchPartRequest.RecordCount, cachedResults);
          }
          else
          {
            log.Trace("Cached results are no longer available for client ID {0}.", Client.Id.ToHex());
            res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
          }
        }
        else
        {
          log.Trace("Required record index is {0}, required record count is {1}.", recordIndexValid ? "valid" : "invalid", recordCountValid ? "valid" : "invalid");
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, !recordIndexValid ? "recordIndex" : "recordCount");
        }
      }
      else
      {
        log.Trace("No cached results are available for client ID {0}.", Client.Id.ToHex());
        res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

  }
}
