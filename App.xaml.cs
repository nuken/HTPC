using System;
using System.Windows;

namespace HTPC;

public partial class App : Application
{
    public App()
    {
        // 1. Catch exceptions on the main UI thread
        this.DispatcherUnhandledException += (sender, e) =>
        {
            MessageBox.Show($"UI Thread Crash!\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                            "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            e.Handled = true; // Prevents the app from closing instantly
        };

        // 2. Catch exceptions on background/async threads
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Background Thread Crash!\n\n{ex.Message}\n\n{ex.StackTrace}", 
                                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        // 3. Catch Async Task exceptions that were never awaited
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            MessageBox.Show($"Unobserved Task Crash!\n\n{e.Exception.InnerException?.Message}", 
                            "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved();
        };
    }
}