using IopProtocol;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using IopCommon;
using Iop.Proximityserver;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Base class for representation of a proximity server's neighbors and followers. 
  /// </summary>
  public abstract class RemoteServerBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.RemoteServerBase");

    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Network identifier of the server is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] NetworkId { get; set; }

    /// <summary>IP address of the server.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.IpAddressMaxLengthBytes)]
    public byte[] IpAddress { get; set; }

    /// <summary>TCP port of the server's primary interface.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(1, 65535)]
    public int PrimaryPort { get; set; }

    /// <summary>TCP port of the server's neighbors interface.</summary>
    [Range(1, 65535)]
    public int? NeighborPort { get; set; }

    /// <summary>true if the server finished neighborhood initialization process, false otherwise.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public bool Initialized { get; set; }

    /// <summary>Time of the last refresh message sent to or received from the server.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime LastRefreshTime { get; set; }


    /// <summary>
    /// Constructs ServerContactInfo representation of the server's contact information.
    /// </summary>
    /// <returns>ServerContactInfo structure or null if the function fails.</returns>
    public ServerContactInfo GetServerContactInfo()
    {
      log.Trace("()");

      ServerContactInfo res = new ServerContactInfo();
      res.NetworkId = ProtocolHelper.ByteArrayToByteString(this.NetworkId);
      res.PrimaryPort = (uint)this.PrimaryPort;
      res.IpAddress = ProtocolHelper.ByteArrayToByteString(this.IpAddress);

      log.Trace("(-)");
      return res;
    }

  }
}
