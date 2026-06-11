using System.Windows;

namespace HTPC;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Catch silent crashes and show them in a popup!
        this.DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"FATAL ERROR: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Crash Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; 
        };
    }
}