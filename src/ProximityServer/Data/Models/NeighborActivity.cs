﻿using Iop.Proximityserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using System.ComponentModel.DataAnnotations;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Database representation of proximity network activity profile for that is managed by a server in the proximity server's neighborhood.
  /// </summary>
  public class NeighborActivity : ActivityBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.NeighborActivity");

    /// <summary>Identifier of the server that acts as the primary proximity server for the activity.</summary>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] PrimaryServerId { get; set; }


    /// <summary>
    /// Compares this activity to other activity and returns list of changed properties.
    /// </summary>
    /// <param name="Other">Other activity to compare to.</param>
    /// <returns>Bit mask information about which properties are different.</returns>
    public ActivityChange CompareChangeTo(NeighborActivity Other)
    {
      ActivityChange res = base.CompareChangeTo(Other);

      if (!ByteArrayComparer.Equals(this.PrimaryServerId, Other.PrimaryServerId))
        res |= ActivityChange.PrimaryServerId;

      return res;
    }
  }
}
