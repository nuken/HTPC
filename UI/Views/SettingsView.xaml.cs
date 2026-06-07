using System.Windows;
using System.Windows.Controls;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class SettingsView : UserControl
{
    private readonly ServerManagerService _serverManager;

    public SettingsView(ServerManagerService serverManager)
    {
        InitializeComponent();
        _serverManager = serverManager;
        LoadServers();
    }

    private void LoadServers()
    {
        SavedServersList.ItemsSource = _serverManager.GetAllServers();
    }

    private void SaveServer_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        string ip = TxtIp.Text.Trim();
        
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip) || !int.TryParse(TxtPort.Text, out int port))
        {
            MessageBox.Show("Please enter a valid Name, IP, and Port.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _serverManager.AddServer(name, ip, port);
        
        TxtName.Clear();
        TxtIp.Clear();
        TxtPort.Text = "8089";
        LoadServers();
    }
	
	public event EventHandler? OnHomeRequested;

    private void Home_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnHomeRequested?.Invoke(this, EventArgs.Empty);
    }
}