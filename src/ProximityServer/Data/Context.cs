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
    /// <summary>Name of the database file.</summary>
    public const string DatabaseFileName = "ProximityServer.db";

    /// <summary>Access to profile server's settings in the database.</summary>
    public DbSet<Setting> Settings { get; set; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      string currentDirectory = Directory.GetCurrentDirectory();
      optionsBuilder.UseSqlite(string.Format("Filename={0}", Path.Combine(currentDirectory, DatabaseFileName)));

      Microsoft.Extensions.Logging.LoggerFactory loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
      loggerFactory.AddProvider(new DbLoggerProvider());
      optionsBuilder.UseLoggerFactory(loggerFactory);
      optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);
    }
  }
}
