using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IopCommon;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Database representation of a proximity server neighbor. A neighbor is another proximity server within the proximity server's 
  /// neighborhood, which was announced to the proximity server by its LOC server. There are two directions of a neighborhood relationship,
  /// this one represents only the servers that are neighbors to this proximity server, but not necessarily vice versa. The proximity server 
  /// asks its neighbors to share their activity databases with it. This allows the proximity server to include activities managed 
  /// by the neighbors in its own search queries.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Follower"/> class.
  /// </para>
  /// </summary>
  public class Neighbor
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.Neighbor");

    /// <summary>Minimal time for which a proximity server considers a neighbor server as alive without hearing from it.</summary>
    public const int MinNeighborhoodExpirationTimeSeconds = 86400;

    /// <summary>Minimal allowed value for the limit of the size of the proximity server's neighborhood.</summary>
    public const int MinMaxNeighborhoodSize = 105;

    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Network identifier of the proximity server is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] NeighborId { get; set; }

    /// <summary>IP address of the proximity server.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public string IpAddress { get; set; }

    /// <summary>TCP port of the proximity server's primary interface.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(1, 65535)]
    public int PrimaryPort { get; set; }

    /// <summary>TCP port of the proximity server's neighbors interface.</summary>
    [Range(1, 65535)]
    public int? NeighborPort { get; set; }

    /// <summary>Proximity server's GPS location latitude.</summary>
    /// <remarks>For precision definition see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-90, 90)]
    public decimal LocationLatitude { get; set; }

    /// <summary>Proximity server's GPS location longitude.</summary>
    /// <remarks>For precision definition see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-180, 180)]
    public decimal LocationLongitude { get; set; }

    /// <summary>
    /// Time of the last refresh message received from the neighbor.
    /// <para>
    /// A null value means that the proximity server did not finish the initialization process with the neighbor.
    /// Once the initialization process is completed this field is initialized.
    /// </para>
    /// </summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? LastRefreshTime { get; set; }

    /// <summary>Number of shared activities that the proximity server received from this neighbor.</summary>
    public int SharedActivities { get; set; }
  }
}
