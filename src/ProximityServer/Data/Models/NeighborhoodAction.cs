using IopProtocol;
using System;
using System.ComponentModel.DataAnnotations;
using IopCommon;

namespace ProximityServer.Data.Models
{
  /// <summary>
  /// Types of actions between neighbor proximity servers.
  /// </summary>
  public enum NeighborhoodActionType
  {
    /// <summary>
    /// LOC server informed the proximity server about a new server in its neighborhood.
    /// The proximity server contacts the neighbor and ask it to share its proximity database.
    /// </summary>
    AddNeighbor = 1,

    /// <summary>
    /// The Cron component found out that a neighbor expired.
    /// The profile server removes the profiles hosted on the neighbor server from its database.
    /// Then it creates StopNeighborhoodUpdates action.
    /// </summary>
    RemoveNeighbor = 2,

    /// <summary>
    /// Proximity server removed a neighbor and wants to ask the neighbor proximity server 
    /// to stop sending activity updates.
    /// </summary>
    StopNeighborhoodUpdates = 3,

    /// <summary>
    /// New activity has been created on the proximity server.
    /// The proximity server has to inform its followers about the change.
    /// </summary>
    AddActivity = 10,

    /// <summary>
    /// Existing activity changed on the proximity server.
    /// The proximity server has to inform its followers about the change.
    /// </summary>
    ChangeActivity = 11,

    /// <summary>
    /// Existing activity has been deletedon the proximity server.
    /// The proximity server has to inform its followers about the change.
    /// </summary>
    RemoveActivity = 12,

    /// <summary>
    /// Purpose of this action is to prevent other activity actions to be sent as updates to followers 
    /// before the neighborhood initialization process is finished.
    /// </summary>
    InitializationProcessInProgress = 14
  }


  /// <summary>
  /// Neighborhood actions are actions within the proximity server's neighborhood that the proximity server is intended to do as soon as possible.
  /// For example, if a primary activity is changed, the change should be propagated to the proximity server followers. When this happens, the proximity 
  /// server creates a neighborhood action for each follower that will take care of this change propagation.
  /// <para>
  /// Each change has its target server. All changes to a single target has to be processed in the correct order, otherwise the integrity
  /// of information might be corrupted.
  /// </para>
  /// </summary>
  public class NeighborhoodAction
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Models.NeighborhoodAction");

    /// <summary>Unique identifier of the action for ordering purposes.</summary>
    /// <remarks>This is index and key - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public int Id { get; set; }

    /// <summary>Network identifier of the neighbor/follower proximity server.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] ServerId { get; set; }

    /// <summary>When was the action created.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public DateTime Timestamp { get; set; }

    /// <summary>Time before which the action can not be executed, or null if it can be executed at any time.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? ExecuteAfter { get; set; }

    /// <summary>Type of the action.</summary>
    /// <remarks>This is index - see ProximityServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public NeighborhoodActionType Type { get; set; }

    /// <summary>User defined activity ID that in combination with TargetActivityOwnerId must form a unique identifier of the activity.</summary>
    /// <remarks>
    /// This is index - see ProximityServer.Data.Context.OnModelCreating.
    /// </remarks>
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public uint TargetActivityId { get; set; }

    /// <summary>Network identifier of the activity creator.</summary>
    /// <remarks>
    /// This is index - see ProximityServer.Data.Context.OnModelCreating.
    /// This property is optional - see ProximityServer.Data.Context.OnModelCreating.
    /// </remarks>
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] TargetActivityOwnerId { get; set; }

    /// <summary>Description of the action as a JSON encoded string.</summary>
    public string AdditionalData { get; set; }

    /// <summary>
    /// Returns true if the action is one of the activity actions, which means its target server 
    /// is the proximity server's follower.
    /// </summary>
    /// <returns>true if the action is one of the activity actions, false otherwise.</returns>
    public bool IsActivityAction()
    {
      return Type >= NeighborhoodActionType.AddActivity;
    }
  }
}
