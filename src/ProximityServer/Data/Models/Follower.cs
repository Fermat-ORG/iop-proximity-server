using IopCommon;
using IopProtocol;
using System;
using System.ComponentModel.DataAnnotations;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Database representation of so called Follower server, which is a remote proximity server, for which the proximity server acts as a neighbor. 
  /// Follower is a server that asked the proximity server to share its activity database with it and the proximity server is sending 
  /// updates of the database to the follower.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Neighbor"/> class.
  /// </para>
  /// </summary>
  public class Follower
  {
    private static Logger log = new Logger("ProximityServer.Data.Models.Follower");

    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Network identifier of the proximity server is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] FollowerId { get; set; }

    /// <summary>IP address of the proximity server.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public string IpAddress { get; set; }

    /// <summary>TCP port of the proximity server's primary interface.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(1, 65535)]
    public int PrimaryPort { get; set; }

    /// <summary>TCP port of the proximityserver's neighbors interface.</summary>
    [Range(1, 65535)]
    public int? NeighborPort { get; set; }

    /// <summary>
    /// Time of the last refresh message sent to the follower server.
    /// <para>
    /// A null value means that the follower server is in the middle of the initialization process 
    /// and the full synchronization has not been done yet. Once the initialization process is completed 
    /// this field is initialized.
    /// </para>
    /// </summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? LastRefreshTime { get; set; }
  }
}
