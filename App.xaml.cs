using System;
using System.Windows;

namespace HTPC
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Catch errors from background threads
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.ExceptionObject.ToString(), "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Catch errors from the main UI thread
            this.DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.Exception.ToString(), "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true; 
                Environment.Exit(1); // Force close after showing the message
            };

            base.OnStartup(e);
        }
    }
}