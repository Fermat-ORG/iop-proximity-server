using IopCommon;
using IopProtocol;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace ProximityServer.Data.Models
{
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
    public int ActivityId { get; set; }

    /// <summary>Network identifier of the identity that created the activity.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] OwnerIdentityId { get; set; }

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
  }
}
