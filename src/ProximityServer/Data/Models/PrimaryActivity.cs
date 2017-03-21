using System.Threading.Tasks;
using IopCommon;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Database representation of proximity network activity profile for that is managed by the proximity server.
  /// </summary>
  public class PrimaryActivity : ActivityBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.PrimaryActivity");
  }
}
