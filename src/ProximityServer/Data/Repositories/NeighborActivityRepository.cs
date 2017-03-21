using ProximityServer.Data.Models;


namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository for activities managed on this proximity server's neighbors.
  /// </summary>
  public class NeighborActivityRepository : ActivityRepository<NeighborActivity>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public NeighborActivityRepository(Context context)
      : base(context)
    {
    }
  }
}
