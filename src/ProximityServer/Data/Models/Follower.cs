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
  public class Follower : RemoteServerBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.Follower");
  }
}
