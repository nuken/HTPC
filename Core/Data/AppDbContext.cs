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
        // Do NOT call EnsureCreated() here; call InitializeSchema() once in Program.cs
    }

    public void InitializeSchema()
    {
        Database.EnsureCreated();

        // 1. Ensure VodCatalog table exists for users upgrading from older versions
        Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""VodCatalog"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VodCatalog"" PRIMARY KEY AUTOINCREMENT,
                ""TmdbId"" INTEGER NOT NULL,
                ""Title"" TEXT NULL,
                ""Year"" INTEGER NOT NULL,
                ""PosterUrl"" TEXT NULL,
                ""Overview"" TEXT NULL,
                ""Genre"" TEXT NULL,
                ""ProviderKey"" TEXT NULL,
                ""NativeDeepLink"" TEXT NULL,
                ""IsActive"" INTEGER NOT NULL,
                ""LastVerified"" TEXT NOT NULL
            );
        ");

        // 2. Check if DefaultCollectionId exists before running ALTER TABLE so EF Core doesn't log EventId 20102
        using var conn = Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ServerConfigs') WHERE name = 'DefaultCollectionId';";
        bool columnExists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;

        if (!columnExists)
        {
            Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerConfigs"" ADD COLUMN ""DefaultCollectionId"" TEXT NULL;");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string dbPath = Path.Combine(exeDir, "htpc_data.db");
        
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }
}