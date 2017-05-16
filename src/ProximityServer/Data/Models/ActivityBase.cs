using Iop.Proximityserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace ProximityServer.Data.Models
{
  /// <summary>Information about which fields are different in two instances of activity.</summary>
  [Flags]
  public enum ActivityChange
  {
    None = 0,
    Version = 1 << 0,
    ActivityId = 1 << 1,
    OwnerIdentityId = 1 << 2,
    OwnerPublicKey = 1 << 3,
    OwnerProfileServerId = 1 << 4,
    OwnerProfileServerIpAddress = 1 << 5,
    OwnerProfileServerPrimaryPort = 1 << 6,
    Type = 1 << 7,
    LocationLatitude = 1 << 8,
    LocationLongitude = 1 << 9,
    PrecisionRadius = 1 << 10,
    StartTime = 1 << 11,
    ExpirationTime = 1 << 12,
    ExtraData = 1 << 13,

    // NeighborActivity only
    PrimaryServerId = 1 << 14
  }

  /// <summary>
  /// Database representation of proximity network activity. This is base class for PrimaryActivity and NeighborActivity classes
  /// and must not be used on its own.
  /// </summary>
  public abstract class ActivityBase
  {
    private static Logger log = new Logger("ProximityServer.Data.Models.ActivityBase");

    /// <summary>Maximum number of primary activities that a proximity server can serve.</summary>
    public const int MaxPrimaryActivities = 50000;

    /// <summary>Maximum number of bytes that activity type can occupy.</summary>
    public const int MaxActivityTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that profile extra data can occupy.</summary>
    public const int MaxActivityExtraDataLengthBytes = 2048;

    /// <summary>Maximum value of activity location precision.</summary>
    public const int MaxLocationPrecision = 1000;

    /// <summary>Maximum life time of the activity in hours. Activity's expiration time can't be set more than this many hours in the future.</summary>
    public const int MaxActivityLifeTimeHours = 24;

    /// <summary>Special type of activity that is used internally and should not be displayed to users.</summary>
    public const string InternalInvalidActivityType = "<INVALID_INTERNAL>";

    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>
    /// Activity structure version according to http://semver.org/. First byte is MAJOR, second byte is MINOR, third byte is PATCH.
    /// </summary>
    [Required]
    [MaxLength(3)]
    public byte[] Version { get; set; }

    /// <summary>User defined activity identifier that together with OwnerIdentityId must form a unique identifier of the activity within the network.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public uint ActivityId { get; set; }

    /// <summary>Network identifier of the identity that created the activity.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] OwnerIdentityId { get; set; }

    /// <summary>Public key the identity that created the activity.</summary>
    [Required]
    [MaxLength(ProtocolHelper.MaxPublicKeyLengthBytes)]
    public byte[] OwnerPublicKey { get; set; }

    /// <summary>Network identifier of the profile server where the owner of the activity has its profile.</summary>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] OwnerProfileServerId { get; set; }

    /// <summary>IP address of the profile server where the owner of the activity has its profile.</summary>
    [Required]
    [MaxLength(ProtocolHelper.IpAddressMaxLengthBytes)]
    public byte[] OwnerProfileServerIpAddress { get; set; }

    /// <summary>TCP port of primary interface of the profile server where the owner of the activity has its profile.</summary>
    [Required]
    public ushort OwnerProfileServerPrimaryPort { get; set; }

    /// <summary>Profile type.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(MaxActivityTypeLengthBytes)]
    public string Type { get; set; }

    /// <summary>
    /// Activity's GPS location latitude. 
    /// <para>For PrimaryActivity, this is the current location latitude.</para>
    /// <para>For NeighborActivity, this is the last known location latitude.</para>
    /// </summary>
    /// <remarks>For precision definition see ProximityServer.Data.Context.OnModelCreating.</remarks>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-90, 90)]
    public decimal LocationLatitude { get; set; }

    /// <summary>
    /// User's initial GPS location longitude.
    /// <para>For PrimaryActivity, this is the current location longitude.</para>
    /// <para>For NeighborActivity, this is the last known location longitude.</para>
    /// </summary>
    /// <remarks>For precision definition see ProximityServer.Data.Context.OnModelCreating.</remarks>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-180, 180)]
    public decimal LocationLongitude { get; set; }

    /// <summary>
    /// Precision of the activity's location in metres.
    /// </summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public uint PrecisionRadius { get; set; }

    /// <summary>
    /// Time when the activity starts. 
    /// This can be in the past for already running or past activities as well as in the future for future activities. 
    /// </summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Time when the activity expires and can be deleted. 
    /// This can be no further in the future than 24 hours from the last update of the activity.
    /// </summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime ExpirationTime { get; set; }


    /// <summary>User defined extra data that serve for satisfying search queries in proximity server network.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxActivityExtraDataLengthBytes)]
    public string ExtraData { get; set; }


    /// <summary>
    /// Creates GPS location from identity's latitude and longitude.
    /// </summary>
    /// <returns>GPS location information.</returns>
    public GpsLocation GetLocation()
    {
      return new GpsLocation(LocationLatitude, LocationLongitude);
    }

    /// <summary>
    /// Sets identity's GPS location information.
    /// </summary>
    /// <param name="Location">Location information.</param>
    public void SetLocation(GpsLocation Location)
    {
      LocationLatitude = Location.Latitude;
      LocationLongitude = Location.Longitude;
    }


    /// <summary>
    /// Constructs a string representation of the activity's full ID, which is network ID of its owner identity 
    /// concatenated with its activity ID. This should be unique in the entire network.
    /// </summary>
    /// <returns>String representation of full activity ID.</returns>
    public string GetFullId()
    {
      return string.Format("{0}-{1}", OwnerIdentityId.ToHex(), ActivityId);
    }

    /// <summary>
    /// Compares this activity to other activity and returns list of changed properties.
    /// </summary>
    /// <param name="Other">Other activity to compare to.</param>
    /// <returns>Bit mask information about which properties are different.</returns>
    public ActivityChange CompareChangeTo(ActivityBase Other)
    {
      ActivityChange res = ActivityChange.None;

      SemVer thisVersion = new SemVer(this.Version);
      SemVer otherVersion = new SemVer(Other.Version);
      if (!thisVersion.Equals(otherVersion))
        res |= ActivityChange.Version;

      if (this.ActivityId != Other.ActivityId)
        res |= ActivityChange.ActivityId;

      if (!ByteArrayComparer.Equals(this.OwnerIdentityId, Other.OwnerIdentityId))
        res |= ActivityChange.OwnerIdentityId;

      if (!ByteArrayComparer.Equals(this.OwnerPublicKey, Other.OwnerPublicKey))
        res |= ActivityChange.OwnerPublicKey;

      if (!ByteArrayComparer.Equals(this.OwnerProfileServerId, Other.OwnerProfileServerId))
        res |= ActivityChange.OwnerProfileServerId;

      if (!ByteArrayComparer.Equals(this.OwnerProfileServerIpAddress, Other.OwnerProfileServerIpAddress))
        res |= ActivityChange.OwnerProfileServerIpAddress;

      if (this.OwnerProfileServerPrimaryPort != Other.OwnerProfileServerPrimaryPort)
        res |= ActivityChange.OwnerProfileServerPrimaryPort;

      if (this.Type != Other.Type)
        res |= ActivityChange.Type;

      if (this.LocationLatitude != Other.LocationLatitude)
        res |= ActivityChange.LocationLatitude;

      if (this.LocationLongitude != Other.LocationLongitude)
        res |= ActivityChange.LocationLongitude;

      if (this.PrecisionRadius != Other.PrecisionRadius)
        res |= ActivityChange.PrecisionRadius;

      if (this.StartTime != Other.StartTime)
        res |= ActivityChange.StartTime;

      if (this.ExpirationTime != Other.ExpirationTime)
        res |= ActivityChange.ExpirationTime;

      if (this.ExtraData != Other.ExtraData)
        res |= ActivityChange.ExtraData;

      return res;
    }


    /// <summary>
    /// Creates a new instance of activity information from the activity.
    /// </summary>
    /// <returns>New instance of the ActivityInformation structure.</returns>
    public ActivityInformation ToActivityInformation()
    {
      GpsLocation activityLocation = this.GetLocation();
      ActivityInformation res = new ActivityInformation()
      {
        Version = new SemVer(this.Version).ToByteString(),
        Id = this.ActivityId,
        OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(this.OwnerPublicKey),
        ProfileServerContact = new ServerContactInfo()
        {
          IpAddress = ProtocolHelper.ByteArrayToByteString(this.OwnerProfileServerIpAddress),
          NetworkId = ProtocolHelper.ByteArrayToByteString(this.OwnerProfileServerId),
          PrimaryPort = this.OwnerProfileServerPrimaryPort,
        },
        Type = this.Type != null ? this.Type : "",
        Latitude = activityLocation.GetLocationTypeLatitude(),
        Longitude = activityLocation.GetLocationTypeLongitude(),
        Precision = this.PrecisionRadius,
        StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.StartTime),
        ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.ExpirationTime),
        ExtraData = this.ExtraData != null ? this.ExtraData : ""
      };
      return res;
    }


    /// <summary>
    /// Creates a new instance of activity from ActivityInformation structure.
    /// </summary>
    /// <param name="Activity">Description of the activity.</param>
    /// <param name="PrimaryServerId">In case of NeighborActivity, this is identifier of the primary proximity server of the activity.</param>
    /// <returns>New instance of the activity.</returns>
    public static T FromActivityInformation<T>(ActivityInformation Activity, byte[] PrimaryServerId = null) where T:ActivityBase, new()
    {
      T res = new T();
      res.CopyFromActivityInformation(Activity);

      if (res is NeighborActivity) (res as NeighborActivity).PrimaryServerId = PrimaryServerId;

      return res;
    }


    /// <summary>
    /// Copies values from activity information to properties of this instance of the activity.
    /// </summary>
    /// <param name="Activity">Description of the activity.</param>
    public void CopyFromActivityInformation(ActivityInformation Activity)
    {
      GpsLocation activityLocation = new GpsLocation(Activity.Latitude, Activity.Longitude);
      byte[] pubKey = Activity.OwnerPublicKey.ToByteArray();
      byte[] identityId = Crypto.Sha256(pubKey);

      this.Version = new SemVer(Activity.Version).ToByteArray();
      this.ActivityId = Activity.Id;
      this.OwnerIdentityId = identityId;
      this.OwnerPublicKey = pubKey;
      this.OwnerProfileServerId = Activity.ProfileServerContact.NetworkId.ToByteArray();
      this.OwnerProfileServerIpAddress = Activity.ProfileServerContact.IpAddress.ToByteArray();
      this.OwnerProfileServerPrimaryPort = (ushort)Activity.ProfileServerContact.PrimaryPort;
      this.Type = Activity.Type;
      this.LocationLatitude = activityLocation.Latitude;
      this.LocationLongitude = activityLocation.Longitude;
      this.PrecisionRadius = Activity.Precision;
      this.StartTime = ProtocolHelper.UnixTimestampMsToDateTime(Activity.StartTime).Value;
      this.ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(Activity.ExpirationTime).Value;
      this.ExtraData = Activity.ExtraData;
    }

  }

}
