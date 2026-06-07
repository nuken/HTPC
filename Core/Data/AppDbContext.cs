using Microsoft.EntityFrameworkCore;
using HTPC.Core.Models;
using System.IO;
using System;

namespace HTPC.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<ServerConfig> ServerConfigs { get; set; }
    public DbSet<PlaybackState> PlaybackStates { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // This command ensures the database file is physically created on the hard drive
        // the very first time the application runs.
        Database.EnsureCreated(); 
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Save the database file directly in the app's running directory for portability
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "htpc_data.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }
}