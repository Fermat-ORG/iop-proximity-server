﻿using Google.Protobuf;
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

                      case ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization:
                        responseMessage = await ProcessMessageStartNeighborhoodInitializationRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                        responseMessage = ProcessMessageFinishNeighborhoodInitializationRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedActivityUpdate:
                        responseMessage = await ProcessMessageNeighborhoodSharedActivityUpdateRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.StopNeighborhoodUpdates:
                        responseMessage = await ProcessMessageStopNeighborhoodUpdatesRequestAsync(client, incomingMessage);
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
                          case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedActivityUpdate:
                            res = await ProcessMessageNeighborhoodSharedActivityUpdateResponseAsync(client, incomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                            res = await ProcessMessageFinishNeighborhoodInitializationResponseAsync(client, incomingMessage, unfinishedRequest);
                            break;

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
      if (ByteArrayComparer.Equals(challenge, Client.AuthenticationChallenge))
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
      ActivityInformation activityInformation = createActivityRequest.Activity;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      ProxProtocolMessage errorResponse;
      if (InputValidators.ValidateCreateActivityRequest(createActivityRequest, Client, RequestMessage, out errorResponse))
      {
        GpsLocation activityLocation = new GpsLocation(activityInformation.Latitude, activityInformation.Longitude);

        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          StrongBox<byte[]> nearestServerId = new StrongBox<byte[]>(null);
          List<byte[]> ignoreServerIds = new List<byte[]>(createActivityRequest.IgnoreServerIds.Select(i => i.ToByteArray()));
          if (await unitOfWork.NeighborRepository.IsServerNearestToLocationAsync(activityLocation, ignoreServerIds, nearestServerId))
          {
            PrimaryActivity activity = ActivityBase.FromActivityInformation<PrimaryActivity>(activityInformation); 

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
      if (InputValidators.ValidateUpdateActivityRequest(updateActivityRequest, Client, RequestMessage, out errorResponse))
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          StrongBox<byte[]> nearestServerId = new StrongBox<byte[]>(null);
          Status status = await unitOfWork.PrimaryActivityRepository.UpdateAndPropagateAsync(updateActivityRequest, Client.IdentityId, nearestServerId);
          switch (status)
          {
            case Status.Ok:
              // Activity was updated and the change will be propagated to the neighborhood.
              res = messageBuilder.CreateUpdateActivityResponse(RequestMessage);
              break;

            case Status.ErrorRejected:
              res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, nearestServerId.Value.ToHex());
              break;

            case Status.ErrorNotFound:
              // Activity of given ID not found among activities created by the client.
              res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              break;

            default:
              // Internal error
              break;
          }
        }
      }
      else res = errorResponse;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
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
      if (InputValidators.ValidateActivitySearchRequest(activitySearchRequest, messageBuilder, RequestMessage, out errorResponse))
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
              // If possible and needed we try to find more results among activities of this proximity server's neighbors.
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
                Activity = activity.ToActivityInformation()
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


    /// <summary>
    /// Processes StartNeighborhoodInitializationRequest message from client.
    /// <para>If the server is not overloaded it accepts the neighborhood initialization request, 
    /// adds the client to the list of server for which the proximity server acts as a neighbor,
    /// and starts sharing its activity database.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client, or null if no response is to be sent by the calling function.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageStartNeighborhoodInitializationRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Neighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = RequestMessage.Request.ConversationRequest.StartNeighborhoodInitialization;
      int primaryPort = (int)startNeighborhoodInitializationRequest.PrimaryPort;
      int neighborPort = (int)startNeighborhoodInitializationRequest.NeighborPort;
      byte[] ipAddressBytes = startNeighborhoodInitializationRequest.IpAddress.ToByteArray();
      byte[] followerId = Client.IdentityId;

      IPAddress followerIpAddress = IPAddressExtensions.IpFromBytes(ipAddressBytes);

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      bool success = false;

      NeighborhoodInitializationProcessContext nipContext = null;

      Config config = (Config)Base.ComponentDictionary[ConfigBase.ComponentName];
      bool primaryPortValid = (0 < primaryPort) && (primaryPort <= 65535);
      bool neighborPortValid = (0 < neighborPort) && (neighborPort <= 65535);

      bool ipAddressValid = (followerIpAddress != null) && (config.TestModeEnabled || !followerIpAddress.IsReservedOrLocal());

      if (primaryPortValid && neighborPortValid && ipAddressValid)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          int blockActionId = await unitOfWork.NeighborhoodActionRepository.InstallInitializationProcessInProgressAsync(followerId);
          if (blockActionId != -1)
          {
            DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.PrimaryActivityLock, UnitOfWork.FollowerLock };
            using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
            {
              try
              {
                int followerCount = await unitOfWork.FollowerRepository.CountAsync();
                if (followerCount < Config.Configuration.MaxFollowerServersCount)
                {
                  int neighborhoodInitializationsInProgress = await unitOfWork.FollowerRepository.CountAsync(f => f.LastRefreshTime == null);
                  if (neighborhoodInitializationsInProgress < Config.Configuration.NeighborhoodInitializationParallelism)
                  {
                    Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == followerId)).FirstOrDefault();
                    if (existingFollower == null)
                    {
                      // Take snapshot of all our identities.
                      byte[] invalidVersion = SemVer.Invalid.ToByteArray();
                      List<PrimaryActivity> allPrimaryActivities = (await unitOfWork.PrimaryActivityRepository.GetAsync(null, null, true)).ToList();

                      // Create new follower.
                      Follower follower = new Follower()
                      {
                        FollowerId = followerId,
                        IpAddress = followerIpAddress.ToString(),
                        PrimaryPort = primaryPort,
                        NeighborPort = neighborPort,
                        LastRefreshTime = null
                      };

                      await unitOfWork.FollowerRepository.InsertAsync(follower);
                      await unitOfWork.SaveThrowAsync();
                      transaction.Commit();
                      success = true;

                      // Set the client to be in the middle of neighbor initialization process.
                      Client.NeighborhoodInitializationProcessInProgress = true;
                      nipContext = new NeighborhoodInitializationProcessContext()
                      {
                        PrimaryActivities = allPrimaryActivities,
                        ActivitiesDone = 0
                      };
                    }
                    else
                    {
                      log.Warn("Follower ID '{0}' already exists in the database.", followerId.ToHex());
                      res = messageBuilder.CreateErrorAlreadyExistsResponse(RequestMessage);
                    }
                  }
                  else
                  {
                    log.Warn("Maximal number of neighborhood initialization processes {0} in progress has been reached.", Config.Configuration.NeighborhoodInitializationParallelism);
                    res = messageBuilder.CreateErrorBusyResponse(RequestMessage);
                  }
                }
                else
                {
                  log.Warn("Maximal number of follower servers {0} has been reached already. Will not accept another follower.", Config.Configuration.MaxFollowerServersCount);
                  res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
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

            if (!success)
            {
              // It may happen that due to power failure, this will not get executed but when the server runs next time, 
              // Data.Database.DeleteInvalidNeighborhoodActions will be executed during the startup and will delete the blocking action.
              if (!await unitOfWork.NeighborhoodActionRepository.UninstallInitializationProcessInProgressAsync(blockActionId))
                log.Error("Unable to uninstall blocking neighborhood action ID {0} for follower ID '{1}'.", blockActionId, followerId.ToHex());
            }
          }
          else log.Error("Unable to install blocking neighborhood action for follower ID '{0}'.", followerId.ToHex());
        }
      }
      else
      {
        if (!primaryPortValid) res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "primaryPort");
        else if (!neighborPortValid) res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "neighborPort");
        else res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "ipAddress");
      }

      if (success)
      {
        log.Info("New follower ID '{0}' added to the database.", followerId.ToHex());

        ProxProtocolMessage responseMessage = messageBuilder.CreateStartNeighborhoodInitializationResponse(RequestMessage);
        if (await Client.SendMessageAsync(responseMessage))
        {
          if (nipContext.PrimaryActivities.Count > 0)
          {
            log.Trace("Sending first batch of our {0} primary activities.", nipContext.PrimaryActivities.Count);
            ProxProtocolMessage updateMessage = BuildNeighborhoodSharedActivityUpdateRequest(Client, nipContext);
            if (!await Client.SendMessageAndSaveUnfinishedRequestAsync(updateMessage, nipContext))
            {
              log.Warn("Unable to send first update message to the client.");
              Client.ForceDisconnect = true;
            }
          }
          else
          {
            log.Trace("No hosted identities to be shared, finishing neighborhood initialization process.");

            // If the proximity server has no primary activities, simply finish initialization process.
            ProxProtocolMessage finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
            if (!await Client.SendMessageAndSaveUnfinishedRequestAsync(finishMessage, null))
            {
              log.Warn("Unable to send finish message to the client.");
              Client.ForceDisconnect = true;
            }
          }
        }
        else
        {
          log.Warn("Unable to send reponse message to the client.");
          Client.ForceDisconnect = true;
        }

        res = null;
      }

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Builds an update message for neighborhood initialization process.
    /// <para>An update message is built in a way that as many as possible consecutive activities from the list 
    /// are being put into the message.</para>
    /// </summary>
    /// <param name="Client">Client for which the message is to be prepared.</param>
    /// <param name="Context">Context describing the status of the initialization process.</param>
    /// <returns>Upadate request message that is ready to be sent to the client.</returns>
    public ProxProtocolMessage BuildNeighborhoodSharedActivityUpdateRequest(IncomingClient Client, NeighborhoodInitializationProcessContext Context)
    {
      log.Trace("()");

      ProxProtocolMessage res = Client.MessageBuilder.CreateNeighborhoodSharedActivityUpdateRequest();

      // We want to send as many items as possible in one message in order to minimize the number of messages 
      // there is some overhead when the message put into the final MessageWithHeader structure, 
      // so to be safe we just use 32 bytes less then the maximum.
      int messageSizeLimit = ProtocolHelper.MaxMessageSize - 32;

      int index = Context.ActivitiesDone;
      List<PrimaryActivity> activities = Context.PrimaryActivities;
      log.Trace("Starting with identity index {0}, total identities number is {1}.", index, activities.Count);
      while (index < activities.Count)
      {
        PrimaryActivity activity = activities[index];
        GpsLocation location = activity.GetLocation();
        SharedActivityUpdateItem updateItem = new SharedActivityUpdateItem()
        {
          Add = activity.ToActivityInformation()
        };

        res.Request.ConversationRequest.NeighborhoodSharedActivityUpdate.Items.Add(updateItem);
        int newSize = res.Message.CalculateSize();

        log.Trace("Index {0}, message size is {1} bytes, limit is {2} bytes.", index, newSize, messageSizeLimit);
        if (newSize > messageSizeLimit)
        {
          // We have reached the limit, remove the last item and send the message.
          res.Request.ConversationRequest.NeighborhoodSharedActivityUpdate.Items.RemoveAt(res.Request.ConversationRequest.NeighborhoodSharedActivityUpdate.Items.Count - 1);
          break;
        }

        index++;
      }

      Context.ActivitiesDone += res.Request.ConversationRequest.NeighborhoodSharedActivityUpdate.Items.Count;
      log.Debug("{0} update items inserted to the message. Already processed {1}/{2} activities.",
        res.Request.ConversationRequest.NeighborhoodSharedActivityUpdate.Items.Count, Context.ActivitiesDone, Context.PrimaryActivities.Count);

      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Processes NeighborhoodSharedActivityUpdateResponse message from client.
    /// <para>This response is received when the follower server accepted a batch of activities and is ready to receive next batch.</para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageNeighborhoodSharedActivityUpdateResponseAsync(IncomingClient Client, ProxProtocolMessage ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Response.Status == Status.Ok)
        {
          NeighborhoodInitializationProcessContext nipContext = (NeighborhoodInitializationProcessContext)Request.Context;
          if (nipContext.ActivitiesDone < nipContext.PrimaryActivities.Count)
          {
            ProxProtocolMessage updateMessage = BuildNeighborhoodSharedActivityUpdateRequest(Client, nipContext);
            if (await Client.SendMessageAndSaveUnfinishedRequestAsync(updateMessage, nipContext))
            {
              res = true;
            }
            else log.Warn("Unable to send update message to the client.");
          }
          else
          {
            // If all hosted identities were sent, finish initialization process.
            ProxProtocolMessage finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
            if (await Client.SendMessageAndSaveUnfinishedRequestAsync(finishMessage, null))
            {
              res = true;
            }
            else log.Warn("Unable to send finish message to the client.");
          }
        }
        else
        {
          // We are in the middle of the neighborhood initialization process, but the follower did not accepted our message with activities.
          // We should disconnect from the follower and delete it from our follower database.
          // If it wants to retry later, it can.
          // Follower will be deleted automatically as the connection terminates in IncomingClient.HandleDisconnect.
          log.Warn("Client ID '{0}' is follower in the middle of the neighborhood initialization process, but it did not accept our activities (error code {1}), so we will disconnect it and delete it from our database.", Client.IdentityId.ToHex(), ResponseMessage.Response.Status);
        }
      }
      else log.Error("Client ID '{0}' does not have neighborhood initialization process in progress, client will be disconnected.", Client.IdentityId.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes FinishNeighborhoodInitializationRequest message from client.
    /// <para>This message should never come here from a protocol conforming client,
    /// hence the only thing we can do is return an error.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public ProxProtocolMessage ProcessMessageFinishNeighborhoodInitializationRequest(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Neighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    } 



    /// <summary>
    /// Processes NeighborhoodSharedActivityUpdateRequest message from client.
    /// <para>Processes a shared activity update from a neighbor.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageNeighborhoodSharedActivityUpdateRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Neighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      NeighborhoodSharedActivityUpdateRequest neighborhoodSharedActivityUpdateRequest = RequestMessage.Request.ConversationRequest.NeighborhoodSharedActivityUpdate;

      bool error = false;
      byte[] neighborId = Client.IdentityId;

      int sharedActivitiesCount = 0;
      // First, we verify that the client is our neighbor and how many activities it shares with us.
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == neighborId)).FirstOrDefault();
        if (neighbor == null)
        {
          log.Warn("Share activity update request came from client ID '{0}', who is not our neighbor.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        }
        else if (neighbor.LastRefreshTime == null)
        {
          log.Warn("Share activity update request came from client ID '{0}', who is our neighbor, but we have not finished the initialization process with it yet.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        }
        else
        {
          sharedActivitiesCount = neighbor.SharedActivities;
          log.Trace("Neighbor ID '{0}' currently shares {1} activities with the proximity server.", neighborId.ToHex(), sharedActivitiesCount);
        }
      }

      if (error)
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      // Second, we do a validation of all items without touching a database.

      // itemIndex will hold the index of the first item that is invalid.
      // If it reaches the number of items, all items are valid.
      int itemIndex = 0;

      // List of string representation of full ID (owner network IDs + activity ID) of activities that were validated in this batch already.
      // It is used to find multiple updates within the batch that work with the same activity, which is not allowed.
      HashSet<string> usedActivitiesIdsInBatch = new HashSet<string>(StringComparer.Ordinal);

      while (itemIndex < neighborhoodSharedActivityUpdateRequest.Items.Count)
      {
        SharedActivityUpdateItem updateItem = neighborhoodSharedActivityUpdateRequest.Items[itemIndex];
        ProxProtocolMessage errorResponse;
        if (InputValidators.ValidateSharedActivityUpdateItem(updateItem, itemIndex, sharedActivitiesCount, usedActivitiesIdsInBatch, Client.MessageBuilder, RequestMessage, out errorResponse))
        {
          // Modify sharedActivitiesCount to reflect the item we just validated.
          // In case of delete operation, we have not checked the existence yet, 
          // but it will be checked prior any problem could be caused by that.
          if (updateItem.ActionTypeCase == SharedActivityUpdateItem.ActionTypeOneofCase.Add) sharedActivitiesCount++;
          else if (updateItem.ActionTypeCase == SharedActivityUpdateItem.ActionTypeOneofCase.Delete) sharedActivitiesCount--;
        }
        else
        {
          res = errorResponse;
          break;
        }

        itemIndex++;
      }

      log.Debug("{0}/{1} update items passed validation.", itemIndex, neighborhoodSharedActivityUpdateRequest.Items.Count);


      // Now we save all valid items up to the first invalid (or all if all are valid).
      // But if we detect duplicity of identity with Add operation, or we can not find identity 
      // with Change or Delete action, we end earlier.
      // We will process the data in batches of max 100 items, not to occupy the database locks for too long.
      log.Trace("Saving {0} valid profiles changes.", itemIndex);

      // Index of the update item currently being processed.
      int index = 0;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        // Batch number just for logging purposes.
        int batchNumber = 1;
        while (index < itemIndex)
        {
          log.Trace("Processing batch number {0}, which starts with item index {1}.", batchNumber, index);
          batchNumber++;

          DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborActivityLock, UnitOfWork.NeighborLock };
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
          {
            bool success = false;
            bool saveDb = false;
            try
            {
              Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == neighborId)).FirstOrDefault();
              if (neighbor != null)
              {
                int oldSharedActivitiesCount = neighbor.SharedActivities;
                for (int loopIndex = 0; loopIndex < 100; loopIndex++)
                {
                  SharedActivityUpdateItem updateItem = neighborhoodSharedActivityUpdateRequest.Items[index];

                  StoreSharedActivityUpdateResult storeResult = await StoreSharedActivityUpdateToDatabaseAsync(unitOfWork, updateItem, index, neighbor, messageBuilder, RequestMessage);
                  if (storeResult.SaveDb) saveDb = true;
                  if (storeResult.Error) error = true;
                  if (storeResult.ErrorResponse != null) res = storeResult.ErrorResponse;

                  // Error here means that we want to save all already processed items to the database
                  // and quit the loop right after that, the response is filled with error response already.
                  if (error) break;

                  index++;
                  if (index >= itemIndex) break;
                }

                if (oldSharedActivitiesCount != neighbor.SharedActivities)
                {
                  unitOfWork.NeighborRepository.Update(neighbor);
                  saveDb = true;
                }
              }
              else
              {
                log.Error("Unable to find neighbor ID '{0}', sending ERROR_REJECTED response.", neighborId.ToHex());
                res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
              }

              if (saveDb)
              {
                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
              }
              success = true;
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

          if (error) break;
        }
      }


      if (res == null) res = messageBuilder.CreateNeighborhoodSharedActivityUpdateResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }





    /// <summary>
    /// Description of result of StoreSharedActivityUpdateToDatabase function.
    /// </summary>
    private class StoreSharedActivityUpdateResult
    {
      /// <summary>True if there was a change to the database that should be saved.</summary>
      public bool SaveDb;

      /// <summary>True if there was an error and no more update items should be processed.</summary>
      public bool Error;

      /// <summary>If there was an error, this is the error response message to be delivered to the neighbor.</summary>
      public ProxProtocolMessage ErrorResponse;
    }


    /// <summary>
    /// Updates a database according to the update item that is already partially validated.
    /// </summary>
    /// <param name="UnitOfWork">Instance of unit of work.</param>
    /// <param name="UpdateItem">Update item that is to be processed.</param>
    /// <param name="UpdateItemIndex">Index of the item within the request.</param>
    /// <param name="Neighbor">Identifier of the neighbor that sent the request.</param>
    /// <param name="MessageBuilder">Neighbor client's message builder.</param>
    /// <param name="RequestMessage">Original request message sent by the neighbor.</param>
    /// <returns>Result described by StoreSharedActivityUpdateResult class.</returns>
    /// <remarks>The caller of this function is responsible to call this function within a database transaction with acquired NeighborIdentityLock.</remarks>
    private async Task<StoreSharedActivityUpdateResult> StoreSharedActivityUpdateToDatabaseAsync(UnitOfWork UnitOfWork, SharedActivityUpdateItem UpdateItem, int UpdateItemIndex, Neighbor Neighbor, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage)
    {
      log.Trace("(UpdateItemIndex:{0},Neighbor.SharedActivities:{1})", UpdateItemIndex, Neighbor.SharedActivities);

      StoreSharedActivityUpdateResult res = new StoreSharedActivityUpdateResult()
      {
        SaveDb = false,
        Error = false,
        ErrorResponse = null,
      };

      switch (UpdateItem.ActionTypeCase)
      {
        case SharedActivityUpdateItem.ActionTypeOneofCase.Add:
          {
            if (Neighbor.SharedActivities >= ActivityBase.MaxPrimaryActivities)
            {
              log.Warn("Neighbor ID '{0}' already shares the maximum number of profiles.", Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add");
              res.Error = true;
              break;
            }

            ActivityInformation addItem = UpdateItem.Add;
            uint activityId = addItem.Id;
            byte[] ownerPubKey = addItem.OwnerPublicKey.ToByteArray();
            byte[] ownerIdentityId = Crypto.Sha256(ownerPubKey);

            // Activity already exists if there exists a NeighborActivity with same activity ID and owner identity ID and the same primary server ID.
            // It may seem enough to just use activity ID and owner identit ID and not ID of its primary proximity server.
            // However, the activity can migrate to another proximity server and we can not guarantee the order of the messages in the network,
            // which means the delete update from old proximity server can arrive later than this add item.
            NeighborActivity existingActivity = (await UnitOfWork.NeighborActivityRepository.GetAsync(a => (a.ActivityId == activityId) && (a.OwnerIdentityId == ownerIdentityId) && (a.PrimaryServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingActivity == null)
            {
              GpsLocation location = new GpsLocation(addItem.Latitude, addItem.Longitude);
              NeighborActivity newActivity = ActivityBase.FromActivityInformation<NeighborActivity>(addItem, Neighbor.NeighborId);

              await UnitOfWork.NeighborActivityRepository.InsertAsync(newActivity);
              Neighbor.SharedActivities++;
              res.SaveDb = true;
            }
            else
            {
              log.Warn("Activity ID {0}, owner identity ID '{1}' already exists with primary proximity server ID '{2}'.", activityId, ownerIdentityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add.id");
              res.Error = true;
            }

            break;
          }

        case SharedActivityUpdateItem.ActionTypeOneofCase.Change:
          {
            ActivityInformation changeItem = UpdateItem.Change;
            uint activityId = changeItem.Id;
            byte[] ownerPubKey = changeItem.OwnerPublicKey.ToByteArray();
            byte[] ownerIdentityId = Crypto.Sha256(ownerPubKey);

            // Activity already exists if there exists a NeighborActivity with same activity ID and owner identity ID and the same primary server ID.
            NeighborActivity existingActivity = (await UnitOfWork.NeighborActivityRepository.GetAsync(a => (a.ActivityId == activityId) && (a.OwnerIdentityId == ownerIdentityId) && (a.PrimaryServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingActivity != null)
            {
              existingActivity.CopyFromActivityInformation(changeItem);

              UnitOfWork.NeighborActivityRepository.Update(existingActivity);
              res.SaveDb = true;
            }
            else
            {
              log.Warn("Activity ID {0}, owner identity ID '{1}' does exists with primary proximity server ID '{2}'.", activityId, ownerIdentityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".change.id");
              res.Error = true;
            }

            break;
          }

        case SharedActivityUpdateItem.ActionTypeOneofCase.Delete:
          {
            ActivityFullId deleteItem = UpdateItem.Delete;
            uint activityId = deleteItem.Id;
            byte[] ownerIdentityId = deleteItem.OwnerNetworkId.ToByteArray();

            // Activity already exists if there exists a NeighborActivity with same activity ID and owner identity ID and the same primary server ID.
            NeighborActivity existingActivity = (await UnitOfWork.NeighborActivityRepository.GetAsync(a => (a.ActivityId == activityId) && (a.OwnerIdentityId == ownerIdentityId) && (a.PrimaryServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingActivity != null)
            {
              UnitOfWork.NeighborActivityRepository.Delete(existingActivity);
              Neighbor.SharedActivities--;
              res.SaveDb = true;
            }
            else
            {
              log.Warn("Activity ID {0}, owner identity ID '{1}' does exists with primary proximity server ID '{2}'.", activityId, ownerIdentityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".delete.id");
              res.Error = true;
            }
            break;
          }
      }

      log.Trace("(-):*.Error={0},*.SaveDb={1}", res.Error, res.SaveDb);
      return res;
    }


    /// <summary>
    /// Processes StopNeighborhoodUpdatesRequest message from client.
    /// <para>Removes follower server from the database and also removes all pending actions to the follower.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<ProxProtocolMessage> ProcessMessageStopNeighborhoodUpdatesRequestAsync(IncomingClient Client, ProxProtocolMessage RequestMessage)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Neighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      StopNeighborhoodUpdatesRequest stopNeighborhoodUpdatesRequest = RequestMessage.Request.ConversationRequest.StopNeighborhoodUpdates;


      byte[] followerId = Client.IdentityId;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Status status = await unitOfWork.FollowerRepository.DeleteFollowerAsync(followerId);

        if (status == Status.Ok) res = messageBuilder.CreateStopNeighborhoodUpdatesResponse(RequestMessage);
        else if (status == Status.ErrorNotFound) res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
        else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      }


      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes FinishNeighborhoodInitializationResponse message from client.
    /// <para>
    /// This response is received when the neighborhood initialization process is finished and the follower server confirms that.
    /// The proximity server marks the follower as fully synchronized in the database. It also unblocks the neighborhood action queue 
    /// for this follower.
    /// </para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageFinishNeighborhoodInitializationResponseAsync(IncomingClient Client, ProxProtocolMessage ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Response.Status == Status.Ok)
        {
          using (UnitOfWork unitOfWork = new UnitOfWork())
          {
            byte[] followerId = Client.IdentityId;

            bool success = false;
            bool signalNeighborhoodAction = false;
            DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
            using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
            {
              try
              {
                bool saveDb = false;

                // Update the follower, so it is considered as fully initialized.
                Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == followerId)).FirstOrDefault();
                if (follower != null)
                {
                  follower.LastRefreshTime = DateTime.UtcNow;
                  unitOfWork.FollowerRepository.Update(follower);
                  saveDb = true;
                }
                else log.Error("Follower ID '{0}' not found.", followerId.ToHex());

                // Update the blocking neighbhorhood action, so that new updates are sent to the follower.
                NeighborhoodAction action = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => (a.ServerId == followerId) && (a.Type == NeighborhoodActionType.InitializationProcessInProgress))).FirstOrDefault();
                if (action != null)
                {
                  action.ExecuteAfter = DateTime.UtcNow;
                  unitOfWork.NeighborhoodActionRepository.Update(action);
                  signalNeighborhoodAction = true;
                  saveDb = true;
                }
                else log.Error("Initialization process in progress neighborhood action for follower ID '{0}' not found.", followerId.ToHex());


                if (saveDb)
                {
                  await unitOfWork.SaveThrowAsync();
                  transaction.Commit();
                }

                Client.NeighborhoodInitializationProcessInProgress = false;
                success = true;
                res = true;
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
              }

              if (success)
              {
                if (signalNeighborhoodAction)
                {
                  NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];
                  neighborhoodActionProcessor.Signal();
                }
              }
              else
              {
                log.Warn("Rolling back transaction.");
                unitOfWork.SafeTransactionRollback(transaction);
              }
            }
            unitOfWork.ReleaseLock(lockObjects);
          }
        }
        else
        {
          // Client is a follower in the middle of the initialization process and failed to accept the finish request.
          // Follower will be deleted automatically as the connection terminates in IncomingClient.HandleDisconnect.
          log.Error("Client ID '{0}' is a follower and failed to accept finish request to neighborhood initialization process (error code {1}), it will be disconnected and deleted from our database.", Client.IdentityId.ToHex(), ResponseMessage.Response.Status);
        }
      }
      else log.Error("Client ID '{0}' does not have neighborhood initialization process in progress.", Client.IdentityId.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


#warning zjisti jestli potrebujeme neighbor action refreshe 
  }
}
