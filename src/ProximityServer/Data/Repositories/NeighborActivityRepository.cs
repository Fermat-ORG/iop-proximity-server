using ProximityServer.Data.Models;


namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository for activities managed on this proximity server's neighbors.
  /// </summary>
  public class NeighborActivityRepository : ActivityRepositoryBase<NeighborActivity>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public NeighborActivityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }
  }
}
