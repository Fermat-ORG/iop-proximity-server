using System;
using System.Collections.Generic;
using System.Text;

namespace ProximityServer.Network
{
  /// <summary>
  /// Context information related to the neighborhood initialization process.
  /// </summary>
  public class NeighborhoodInitializationProcessContext
  {
    /// <summary>Snapshot of all primary activities at the moment the client sent request to start the neighborhood initialization process.</summary>
    public List<PrimaryActivity> PrimaryActivities;

    /// <summary>Number of items from PrimaryActivities that has been processed already.</summary>
    public int ActivitiesDone;
  }
}
