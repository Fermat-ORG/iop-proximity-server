using IopServerCore.Data;
using ProximityServer.Data.Models;

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository of planned actions in the neighborhood
  /// </summary>
  public class NeighborhoodActionRepository : GenericRepository<Context, NeighborhoodAction>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public NeighborhoodActionRepository(Context context)
      : base(context)
    {
    }
  }
}
