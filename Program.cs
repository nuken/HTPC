using System;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HTPC.Services;

namespace HTPC;

public class Program
{
    [STAThread] // Mandatory for WPF/COM Interop
    public static void Main(string[] args)
    {
        // 1. Build the Generic Host
        var hostBuilder = Host.CreateDefaultBuilder(args)
            
            // Configure Kestrel Web Server for Remote API
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // Existing status check
                        endpoints.MapGet("/api/status", () => "HTPC Core API is online.");

                        // NEW: Remote Control Endpoints
                        // The framework automatically grabs our Singleton MpvPlaybackService from Dependency Injection
                        endpoints.MapGet("/api/pause", (MpvPlaybackService player) => 
                        { 
                            player.Pause(); 
                            return "Video Paused"; 
                        });

                        endpoints.MapGet("/api/resume", (MpvPlaybackService player) => 
                        { 
                            player.Resume(); 
                            return "Video Resumed"; 
                        });

                        endpoints.MapGet("/api/stop", (MpvPlaybackService player) => 
                        { 
                            player.Stop(); 
                            return "Video Stopped"; 
                        });
                    });
                });
                
                // Bind Kestrel to port 5001 (accessible locally and on your network)
                webBuilder.UseUrls("http://localhost:55001");
            })
            
            // Configure Dependency Injection
           .ConfigureServices((context, services) =>
            {
                services.AddSingleton<App>();
                services.AddSingleton<MpvPlaybackService>();
                services.AddDbContext<HTPC.Core.Data.AppDbContext>();
                services.AddSingleton<ServerManagerService>();
				services.AddHttpClient();
				services.AddSingleton<UpdateService>();
                
                // NEW: Register the native HTTP connection factory
                services.AddHttpClient();
                
                services.AddSingleton<MediaLibraryService>();
                services.AddTransient<HTPC.UI.Views.MoviesView>();
                services.AddTransient<HTPC.UI.Views.DashboardView>();
                services.AddTransient<HTPC.UI.Views.PlayerView>();
                services.AddTransient<HTPC.UI.Views.SettingsView>();
                services.AddSingleton<HTPC.UI.Windows.MainWindow>();
				services.AddTransient<HTPC.UI.Views.GuideView>();
				services.AddTransient<HTPC.UI.Views.ShowsView>();
				services.AddTransient<HTPC.UI.Views.VideosView>();
				services.AddTransient<HTPC.UI.Views.MultiviewSetupView>();
            });

        using var host = hostBuilder.Build();
        
        // 2. Start the Background Host
        host.Start();

        // --- NEW: Force Database Initialization ---
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HTPC.Core.Data.AppDbContext>();
            db.Database.EnsureCreated();
        }
        // ------------------------------------------

        // 3. Force initialization of our player service
        var playerService = host.Services.GetRequiredService<MpvPlaybackService>();

        // Start the WPF UI Thread and launch the SPA Shell
        var wpfApp = host.Services.GetRequiredService<App>();
        var mainWindow = host.Services.GetRequiredService<HTPC.UI.Windows.MainWindow>();
        wpfApp.Run(mainWindow);

        // 5. Clean up when the application exits
        host.StopAsync().GetAwaiter().GetResult();
    }
}