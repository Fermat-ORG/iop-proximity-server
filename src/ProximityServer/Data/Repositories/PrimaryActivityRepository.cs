using ProximityServer.Data.Models;
using Iop.Proximityserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Repository for primary activities of the proximity server.
  /// </summary>
  public class PrimaryActivityRepository : ActivityRepository<PrimaryActivity>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Repositories.PrimaryActivityRepository");

    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public PrimaryActivityRepository(Context context)
      : base(context)
    {
    }


    /// <summary>
    /// Obtains activity information by its ID and owner user ID.
    /// </summary>
    /// <param name="ActivityId">User defined activity ID.</param>
    /// <param name="OwnerId">Network identifier of the identity that owns the activity.</param>
    /// <returns>Activity information or null if the function fails.</returns>
    public async Task<PrimaryActivity> GetPrimarydentityByIdAsync(uint ActivityId, byte[] OwnerId)
    {
      log.Trace("(ActivityId:{0},OwnerId:'{1}')", ActivityId, OwnerId.ToHex());
      PrimaryActivity res = null;

      try
      {
        res = (await GetAsync(i => (i.ActivityId == ActivityId) && (i.OwnerIdentityId == OwnerId))).FirstOrDefault();
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res != null ? "PrimaryActivity" : "null");
      return res;
    }
  }
}
