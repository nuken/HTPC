using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using HTPC.Core.Models;
using HTPC.Core.Data;

namespace HTPC.Services;

public class ServerManagerService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ServerManagerService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
	
	public ServerConfig? GetActiveServer()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ServerConfigs.FirstOrDefault(s => s.IsActive);
    }

    public List<ServerConfig> GetAllServers()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ServerConfigs.ToList();
    }

    public void AddServer(string name, string ip, int port)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // If this is the first server being added, make it active by default
        bool isFirst = !db.ServerConfigs.Any();

        var newServer = new ServerConfig
        {
            Name = name,
            IpAddress = ip,
            Port = port,
            IsActive = isFirst
        };

        db.ServerConfigs.Add(newServer);
        db.SaveChanges();
    }

    public void SetActiveServer(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var allServers = db.ServerConfigs.ToList();
        foreach (var server in allServers)
        {
            server.IsActive = (server.Id == serverId);
        }
        
        db.SaveChanges();
    }
	
	public void SetDefaultCollection(string collectionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var activeServer = db.ServerConfigs.FirstOrDefault(s => s.IsActive);
        if (activeServer != null)
        {
            activeServer.DefaultCollectionId = collectionId;
            db.SaveChanges();
        }
    }
}