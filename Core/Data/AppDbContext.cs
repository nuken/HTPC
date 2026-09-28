using Microsoft.EntityFrameworkCore;
using HTPC.Core.Models;
using System.IO;
using System;

namespace HTPC.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<ServerConfig> ServerConfigs { get; set; }
    public DbSet<PlaybackState> PlaybackStates { get; set; }
	public DbSet<VODCatalogItem> VodCatalog { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // This command ensures the database file is physically created on the hard drive
        // the very first time the application runs.
        Database.EnsureCreated(); 
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Lock the database path to the compiled .exe location
        string exeDir = System.IO.Path.GetDirectoryName(System.Environment.ProcessPath) ?? System.AppDomain.CurrentDomain.BaseDirectory;
        string dbPath = System.IO.Path.Combine(exeDir, "htpc_data.db");
        
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }
}