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
  }
}
