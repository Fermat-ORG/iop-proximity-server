using Google.Protobuf;
using Iop.Proximityserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using ProximityServer.Data.Models;
using ProximityServer.Kernel;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ProximityServer.Network
{
  /// <summary>
  /// Implements functions to validate user inputs.
  /// </summary>
  public static class InputValidators
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Network.InputValidators");


    /// <summary>
    /// Validates ActivityInformation structure by itself. This function does not touch database and does not validate 
    /// anything that depends on the context. 
    /// </summary>
    /// <param name="Activity">Description of the activity to validate.</param>
    /// <param name="OwnerIdentityPublicKey">Public key of the activity's owner identity.</param>
    /// <param name="MessageBuilder">Network message builder of the client who sent this activity description.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorPrefix">Prefix to add to the validation error details.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the activity information is valid, false otherwise.</returns>
    public static bool ValidateActivityInformation(ActivityInformation Activity, byte[] OwnerIdentityPublicKey, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, string ErrorPrefix, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      if (Activity == null) Activity = new ActivityInformation();
      if (Activity.ProfileServerContact == null) Activity.ProfileServerContact = new ServerContactInfo();

      SemVer version = new SemVer(Activity.Version);

      // Currently only supported version is 1.0.0.
      if (!version.Equals(SemVer.V100))
      {
        log.Debug("Unsupported version '{0}'.", version);
        details = "version";
      }


      if (details == null)
      {
        uint activityId = Activity.Id;

        // 0 is not a valid activity identifier.
        if (activityId == 0)
        {
          log.Debug("Invalid activity ID '{0}'.", activityId);
          details = "id";
        }
      }

      if (details == null)
      {
        byte[] pubKey = Activity.OwnerPublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes) && ByteArrayComparer.Equals(OwnerIdentityPublicKey, pubKey);
        if (!pubKeyValid)
        {
          log.Debug("Invalid public key '{0}' does not match identity public key '{1}'.", pubKey.ToHex(), OwnerIdentityPublicKey.ToHex());
          details = "ownerPublicKey";
        }
      }


      if (details == null)
      {
        ServerContactInfo sci = Activity.ProfileServerContact;
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
        string activityType = Activity.Type;
        if (activityType == null) activityType = "";

        int byteLen = Encoding.UTF8.GetByteCount(activityType);
        bool activityTypeValid = (0 < byteLen) && (byteLen <= ActivityBase.MaxActivityTypeLengthBytes);
        if (!activityTypeValid)
        {
          log.Debug("Activity type too long or zero length ({0} bytes, limit is {1}).", byteLen, ActivityBase.MaxActivityTypeLengthBytes);
          details = "type";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(Activity.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, Activity.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", Activity.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", Activity.Longitude);
          details = "longitude";
        }
      }

      if (details == null)
      {
        uint precision = Activity.Precision;
        bool precisionValid = (0 <= precision) && (precision <= ActivityBase.MaxLocationPrecision);
        if (!precisionValid)
        {
          log.Debug("Precision '{0}' is not an integer between 0 and {1}.", precision, ActivityBase.MaxLocationPrecision);
          details = "precision";
        }
      }


      if (details == null)
      {
        DateTime? startTime = ProtocolHelper.UnixTimestampMsToDateTime(Activity.StartTime);
        DateTime? expirationTime = ProtocolHelper.UnixTimestampMsToDateTime(Activity.ExpirationTime);

        if (startTime == null)
        {
          log.Debug("Invalid activity start time timestamp '{0}'.", Activity.StartTime);
          details = "startTime";
        }
        else if (expirationTime == null)
        {
          log.Debug("Invalid activity expiration time timestamp '{0}'.", Activity.ExpirationTime);
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
        string extraData = Activity.ExtraData;
        if (extraData == null) extraData = "";


        // Extra data is semicolon separated 'key=value' list, max ActivityBase.MaxActivityExtraDataLengthBytes bytes long.
        int byteLen = Encoding.UTF8.GetByteCount(extraData);
        if (byteLen > ActivityBase.MaxActivityExtraDataLengthBytes)
        {
          log.Debug("Extra data too large ({0} bytes, limit is {1}).", byteLen, ActivityBase.MaxActivityExtraDataLengthBytes);
          details = "extraData";
        }
      }

      if (details == null) res = true;
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, ErrorPrefix + details);

      log.Trace("(-):{0}");
      return res;
    }


    /// <summary>
    /// Checks whether a signed activity information is valid.
    /// </summary>
    /// <param name="SignedActivity">Signed activity information to check.</param>
    /// <param name="OwnerIdentityPublicKey">Public key of the activity's owner identity.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorPrefix">Prefix to add to the validation error details.</param>
    /// <param name="InvalidSignatureToDetails">If set to true, invalid signature error will be reported as invalid value in signature field.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the signed activity information is valid and signed correctly by the given identity, false otherwise.</returns>
    public static bool ValidateSignedActivityInformation(SignedActivityInformation SignedActivity, byte[] OwnerIdentityPublicKey, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, string ErrorPrefix, bool InvalidSignatureToDetails, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");
      ErrorResponse = null;

      if (SignedActivity == null) SignedActivity = new SignedActivityInformation();
      if (SignedActivity.Activity == null) SignedActivity.Activity = new ActivityInformation();

      bool res = false;
      if (ValidateActivityInformation(SignedActivity.Activity, OwnerIdentityPublicKey, MessageBuilder, RequestMessage, ErrorPrefix + "activity.", out ErrorResponse))
      {
        // ActivityBase.InternalInvalidActivityType is a special internal type of activity that we use to prevent problems in the network.
        // This is not an elegant solution.
        // See NeighborhoodActionProcessor.NeighborhoodActivityUpdateAsync case NeighborhoodActionType.AddActivity for more information.
        if (SignedActivity.Activity.Type != ActivityBase.InternalInvalidActivityType)
        {
          byte[] signature = SignedActivity.Signature.ToByteArray();
          byte[] data = SignedActivity.Activity.ToByteArray();

          if (Ed25519.Verify(signature, data, OwnerIdentityPublicKey)) res = true;
          else ErrorResponse = InvalidSignatureToDetails ? MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, ErrorPrefix + "signature") : MessageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }





    /// <summary>
    /// Checks whether the create activity request is valid.
    /// <para>This function does not verify the uniqueness of the activity identifier.
    /// It also does not verify whether a closer proximity server exists.</para>
    /// </summary>
    /// <param name="CreateActivityRequest">Create activity request part of the client's request message.</param>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the create activity request can be applied, false otherwise.</returns>
    public static bool ValidateCreateActivityRequest(CreateActivityRequest CreateActivityRequest, IncomingClient Client, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      if (CreateActivityRequest == null) CreateActivityRequest = new CreateActivityRequest();
      if (CreateActivityRequest.Activity == null) CreateActivityRequest.Activity = new ActivityInformation();
      if (CreateActivityRequest.Activity.ProfileServerContact == null) CreateActivityRequest.Activity.ProfileServerContact = new ServerContactInfo();

      SignedActivityInformation signedActivity = new SignedActivityInformation()
      {
        Activity = CreateActivityRequest.Activity,
        Signature = RequestMessage.Request.ConversationRequest.Signature
      };


      if (ValidateSignedActivityInformation(signedActivity, Client.PublicKey, Client.MessageBuilder, RequestMessage, "", false, out ErrorResponse))
      {
        byte[] ownerPubKey = CreateActivityRequest.Activity.OwnerPublicKey.ToByteArray();

        if (!ByteArrayComparer.Equals(Client.PublicKey, ownerPubKey))
        {
          log.Debug("Client's public key '{0}' does not match activity owner's public key '{1}'.", Client.PublicKey.ToHex(), ownerPubKey.ToHex());
          details = "activity.ownerPublicKey";
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

        if (details == null) res = true;
        else ErrorResponse = Client.MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Checks whether the update activity request is valid.
    /// <para>This function does not verify whether the activity exists.
    /// It also does not verify whether a closer proximity server exists to which the activity should be migrated.
    /// It also does not verify whether the changed activity's start time will be before expiration time.</para>
    /// </summary>
    /// <param name="UpdateActivityRequest">Update activity request part of the client's request message.</param>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the update activity request can be applied, false otherwise.</returns>
    public static bool ValidateUpdateActivityRequest(UpdateActivityRequest UpdateActivityRequest, IncomingClient Client, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      if (UpdateActivityRequest == null) UpdateActivityRequest = new UpdateActivityRequest();
      if (UpdateActivityRequest.Activity == null) UpdateActivityRequest.Activity = new ActivityInformation();

      SignedActivityInformation signedActivity = new SignedActivityInformation()
      {
        Activity = UpdateActivityRequest.Activity,
        Signature = RequestMessage.Request.ConversationRequest.Signature
      };

      ProxMessageBuilder messageBuilder = Client.MessageBuilder;
      if (ValidateSignedActivityInformation(signedActivity, Client.PublicKey, messageBuilder, RequestMessage, "", false, out ErrorResponse))
      {
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

        if (details == null) res = true;
        else ErrorResponse = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
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
    public static bool ValidateActivitySearchRequest(ActivitySearchRequest ActivitySearchRequest, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      int responseResultLimit = ProxMessageProcessor.ActivitySearchMaxResponseRecords;
      int totalResultLimit = ProxMessageProcessor.ActivitySearchMaxTotalRecords;

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

      if ((details == null) && (ActivitySearchRequest.OwnerNetworkId.Length > 0))
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
    /// Validates incoming SharedActivityUpdateItem update item.
    /// </summary>
    /// <param name="UpdateItem">Update item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedActivitiesCount">Number of activities the neighbor already shares with the activity server.</param>
    /// <param name="UsedFullActivityIdsInBatch">List of activity full IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedActivityUpdateItem(SharedActivityUpdateItem UpdateItem, int Index, int SharedActivitiesCount, HashSet<string> UsedFullActivityIdsInBatch, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0},SharedActivitiesCount:{1})", Index, SharedActivitiesCount);

      bool res = false;
      ErrorResponse = null;

      switch (UpdateItem.ActionTypeCase)
      {
        case SharedActivityUpdateItem.ActionTypeOneofCase.Add:
          res = ValidateSharedActivityAddItem(UpdateItem.Add, Index, SharedActivitiesCount, UsedFullActivityIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedActivityUpdateItem.ActionTypeOneofCase.Change:
          res = ValidateSharedActivityChangeItem(UpdateItem.Change, Index, UsedFullActivityIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedActivityUpdateItem.ActionTypeOneofCase.Delete:
          res = ValidateSharedActivityDeleteItem(UpdateItem.Delete, Index, UsedFullActivityIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedActivityUpdateItem.ActionTypeOneofCase.Refresh:
          res = true;
          break;

        default:
          ErrorResponse = MessageBuilder.CreateErrorProtocolViolationResponse(RequestMessage);
          res = false;
          break;
      }

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Validates incoming SharedActivityAddItem update item.
    /// <para>This function does not verify whether the activity is not duplicate.</para>
    /// </summary>
    /// <param name="AddItem">Add item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedActivitiesCount">Number of activities the neighbor already shares with the activity server.</param>
    /// <param name="UsedFullActivityIdsInBatch">List of activity full IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedActivityAddItem(SharedActivityAddItem AddItem, int Index, int SharedActivitiesCount, HashSet<string> UsedFullActivityIdsInBatch, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0},SharedActivitiesCount:{1})", Index, SharedActivitiesCount);

      bool res = false;
      ErrorResponse = null;

      if (AddItem == null) AddItem = new SharedActivityAddItem();
      if (AddItem.SignedActivity == null) AddItem.SignedActivity = new SignedActivityInformation();
      if (AddItem.SignedActivity.Activity == null) AddItem.SignedActivity.Activity = new ActivityInformation();


      byte[] ownerIdentityPubKey = AddItem.SignedActivity.Activity.OwnerPublicKey.ToByteArray();
      if (ValidateSignedActivityInformation(AddItem.SignedActivity, ownerIdentityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".add.signedActivity.", true, out ErrorResponse))
      {
        string details = null;

        if (SharedActivitiesCount >= ActivityBase.MaxPrimaryActivities)
        {
          log.Debug("Target server already sent too many activities.");
          details = "add";
        }

        if (details == null)
        {
          byte[] identityId = Crypto.Sha256(ownerIdentityPubKey);
          NeighborActivity na = new NeighborActivity()
          {
            ActivityId = AddItem.SignedActivity.Activity.Id,
            OwnerIdentityId = identityId
          };

          string activityFullId = na.GetFullId();
          if (!UsedFullActivityIdsInBatch.Contains(activityFullId))
          {
            UsedFullActivityIdsInBatch.Add(activityFullId);
          }
          else
          {
            log.Debug("Activity full ID '{0}' (public key '{1}') already processed in this request.", activityFullId, ownerIdentityPubKey.ToHex());
            details = "add.signedActivity.activity.id";
          }
        }

        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedActivityChangeItem update item.
    /// <para>This function does not verify whether the activity exists.
    /// It also does not verify whether the new activity start time and expiration time are correct if they are not both changed.</para>
    /// </summary>
    /// <param name="ChangeItem">Change item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedFullActivityIdsInBatch">List of activity full IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedActivityChangeItem(SharedActivityChangeItem ChangeItem, int Index, HashSet<string> UsedFullActivityIdsInBatch, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      if (ChangeItem == null) ChangeItem = new SharedActivityChangeItem();
      if (ChangeItem.SignedActivity == null) ChangeItem.SignedActivity = new SignedActivityInformation();
      if (ChangeItem.SignedActivity.Activity == null) ChangeItem.SignedActivity.Activity = new ActivityInformation();


      byte[] ownerIdentityPubKey = ChangeItem.SignedActivity.Activity.OwnerPublicKey.ToByteArray();
      if (ValidateSignedActivityInformation(ChangeItem.SignedActivity, ownerIdentityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".change.signedActivity.", true, out ErrorResponse))
      {
        string details = null;

        byte[] identityId = Crypto.Sha256(ownerIdentityPubKey);

        NeighborActivity na = new NeighborActivity()
        {
          ActivityId = ChangeItem.SignedActivity.Activity.Id,
          OwnerIdentityId = identityId
        };

        string activityFullId = na.GetFullId();
        if (!UsedFullActivityIdsInBatch.Contains(activityFullId))
        {
          UsedFullActivityIdsInBatch.Add(activityFullId);
        }
        else
        {
          log.Debug("Activity full ID '{0}' already processed in this request.", activityFullId);
          details = "change.signedActivity.activity.id";
        }

        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }


      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Validates incoming SharedActivityDeleteItem update item.
    /// </summary>
    /// <para>This function does not verify whether the activity exists.</para>
    /// <param name="DeleteItem">Delete item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedFullActivityIdsInBatch">List of activity full IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedActivityDeleteItem(SharedActivityDeleteItem DeleteItem, int Index, HashSet<string> UsedFullActivityIdsInBatch, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      if (DeleteItem == null) DeleteItem = new SharedActivityDeleteItem();

      string details = null;

      uint activityId = DeleteItem.Id;

      // 0 is not a valid activity identifier.
      if (activityId == 0)
      {
        log.Debug("Invalid activity ID '{0}'.", activityId);
        details = "delete.id";
      }

      if (details == null)
      {
        byte[] identityId = DeleteItem.OwnerNetworkId.ToByteArray();
        bool identityIdValid = identityId.Length == ProtocolHelper.NetworkIdentifierLength;
        if (identityIdValid)
        {
          NeighborActivity na = new NeighborActivity()
          {
            ActivityId = DeleteItem.Id,
            OwnerIdentityId = identityId
          };

          string activityFullId = na.GetFullId();
          if (!UsedFullActivityIdsInBatch.Contains(activityFullId))
          {
            UsedFullActivityIdsInBatch.Add(activityFullId);
          }
          else
          {
            log.Debug("Activity full ID '{0}' already processed in this request.", activityFullId);
            details = "delete.id";
          }
        }
        else
        {
          log.Debug("Invalid owner network ID length '{0}'.", identityId.Length);
          details = "delete.ownerNetworkId";
        }
      }


      if (details == null) res = true;
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming update item during the neighborhood initialization process with in-memory database of activities from the neighbor.
    /// </summary>
    /// <param name="AddItem">Update item to validate.</param>
    /// <param name="Index">Index of the update item in the message.</param>
    /// <param name="ActivityDatabase">In-memory temporary database of activities from the neighbor server that were already received and processed.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateInMemorySharedActivityActivityInformation(SharedActivityAddItem AddItem, int Index, Dictionary<string, NeighborActivity> ActivityDatabase, ProxMessageBuilder MessageBuilder, ProxProtocolMessage RequestMessage, out ProxProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      if (AddItem == null) AddItem = new SharedActivityAddItem();
      if (AddItem.SignedActivity == null) AddItem.SignedActivity = new SignedActivityInformation();
      if (AddItem.SignedActivity.Activity == null) AddItem.SignedActivity.Activity = new ActivityInformation();

      byte[] ownerIdentityPubKey = AddItem.SignedActivity.Activity.OwnerPublicKey.ToByteArray();
      if (ValidateSignedActivityInformation(AddItem.SignedActivity, ownerIdentityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".add.signedActivity.", true, out ErrorResponse))
      {
        string details = null;
        if (ActivityDatabase.Count >= ActivityBase.MaxPrimaryActivities)
        {
          log.Debug("Target server already sent too many activities.");
          details = "add";
        }

        if (details == null)
        {
          byte[] identityId = Crypto.Sha256(ownerIdentityPubKey);
          NeighborActivity na = new NeighborActivity()
          {
            ActivityId = AddItem.SignedActivity.Activity.Id,
            OwnerIdentityId = identityId
          };

          string activityFullId = na.GetFullId();
          if (ActivityDatabase.ContainsKey(activityFullId))
          {
            log.Debug("Activity full ID '{0}' (public key '{1}') already exists.", activityFullId, ownerIdentityPubKey.ToHex());
            details = "add.signedActivity.activity.id";
          }
        }

        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
