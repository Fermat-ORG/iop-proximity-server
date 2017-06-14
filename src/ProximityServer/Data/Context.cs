using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProximityServer.Data.Models;
using ProximityServer.Kernel;
using IopCommon;

namespace ProximityServer.Data
{
  /// <summary>
  /// Database context that is used everytime a component reads from or writes to the database.
  /// </summary>
  public class Context : DbContext
  {
    /// <summary>Default name of the database file.</summary>
    public const string DefaultDatabaseFileName = "ProximityServer.db";

    /// <summary>Access to proximity server's settings in the database.</summary>
    public DbSet<Setting> Settings { get; set; }

    /// <summary>Database table with primary activities.</summary>
    public DbSet<PrimaryActivity> PrimaryActivities { get; set; }

    /// <summary>Database table with activities of neighbors.</summary>
    public DbSet<NeighborActivity> NeighborActivities { get; set; }

    /// <summary>Neighbor proximity servers.</summary>
    public DbSet<Neighbor> Neighbors { get; set; }

    /// <summary>Planned actions related to the neighborhood.</summary>
    public DbSet<NeighborhoodAction> NeighborhoodActions { get; set; }

    /// <summary>Follower servers.</summary>
    public DbSet<Follower> Followers { get; set; }



    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      string currentDirectory = Directory.GetCurrentDirectory();
      string path = Path.Combine(currentDirectory, DefaultDatabaseFileName);

      string dbFileName = Config.Configuration != null ? Config.Configuration.DatabaseFileName : path;
      optionsBuilder.UseSqlite(string.Format("Filename={0}", dbFileName));

      Microsoft.Extensions.Logging.LoggerFactory loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
      loggerFactory.AddProvider(new DbLoggerProvider());
      optionsBuilder.UseLoggerFactory(loggerFactory);
      optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);


      modelBuilder.Entity<PrimaryActivity>().HasKey(i => new { i.DbId });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.ActivityId, i.OwnerIdentityId }).IsUnique();
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.OwnerIdentityId });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.LocationLatitude, i.LocationLongitude, i.PrecisionRadius });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.ExtraData });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.StartTime });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.ExpirationTime });
      modelBuilder.Entity<PrimaryActivity>().HasIndex(i => new { i.ExpirationTime, i.StartTime, i.LocationLatitude, i.LocationLongitude, i.PrecisionRadius, i.Type, i.OwnerIdentityId });

      modelBuilder.Entity<PrimaryActivity>().Property(i => i.LocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<PrimaryActivity>().Property(i => i.LocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);

      modelBuilder.Entity<NeighborActivity>().HasKey(i => new { i.DbId });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.ActivityId, i.OwnerIdentityId }).IsUnique();
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.OwnerIdentityId });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.LocationLatitude, i.LocationLongitude, i.PrecisionRadius });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.ExtraData });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.StartTime });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.ExpirationTime });
      modelBuilder.Entity<NeighborActivity>().HasIndex(i => new { i.ExpirationTime, i.StartTime, i.LocationLatitude, i.LocationLongitude, i.PrecisionRadius, i.Type, i.OwnerIdentityId });

      modelBuilder.Entity<NeighborActivity>().Property(i => i.LocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<NeighborActivity>().Property(i => i.LocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);


      modelBuilder.Entity<Neighbor>().HasKey(i => i.DbId);
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.NetworkId }).IsUnique();
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.IpAddress, i.PrimaryPort });
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.LastRefreshTime });
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.Initialized });

      modelBuilder.Entity<Neighbor>().Property(i => i.LocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<Neighbor>().Property(i => i.LocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);

      modelBuilder.Entity<Follower>().HasKey(i => i.DbId);
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.NetworkId }).IsUnique();
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.IpAddress, i.PrimaryPort });
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.LastRefreshTime });
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.Initialized });


      modelBuilder.Entity<NeighborhoodAction>().HasKey(i => i.Id);
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Id }).IsUnique();
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ServerId });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Timestamp });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ExecuteAfter });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.TargetActivityId, i.TargetActivityOwnerId });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ServerId, i.Type, i.TargetActivityId, i.TargetActivityOwnerId });
      modelBuilder.Entity<NeighborhoodAction>().Property(e => e.TargetActivityOwnerId).IsRequired(false);
    }
  }
}
