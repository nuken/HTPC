using System;
using System.Threading;
using System.Windows;
using HTPC.Services;

namespace HTPC
{
    public partial class App : Application
    {
        // Give this a unique name so Windows knows exactly which app to track
        private static Mutex _mutex = new Mutex(true, "HTPC_Unique_App_Mutex_Lock");

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. MUTEX LOCK: Check if another instance is already running
            if (!_mutex.WaitOne(TimeSpan.Zero, true))
            {
                MessageBox.Show("The application is already running in the background.", "HTPC", MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            // 2. Catch errors from background threads
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.ExceptionObject.ToString(), "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // 3. Catch errors from the main UI thread
            this.DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.Exception.ToString(), "Fatal Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
                Environment.Exit(1); // Force close after showing the message
            };

            // 4. Load the user's saved theme preference
            string savedTheme = PreferencesManager.LoadTheme();
            ApplyTheme(savedTheme);

            base.OnStartup(e);
        }

        // --- NEW: THEME SWITCHING LOGIC ---
        public void ApplyTheme(string themeName)
        {
            try
            {
                // Format the URI to point to the new UI/Themes folder
                string themePath = $"UI/Themes/{themeName}Theme.xaml";
                var themeDict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };

                // Clear any dynamically loaded dictionaries and inject the new theme
                Current.Resources.MergedDictionaries.Clear();
                Current.Resources.MergedDictionaries.Add(themeDict);
            }
            catch (Exception ex)
            {
                // Fallback logging in case the dictionary fails to load
                Console.WriteLine($"Failed to load theme {themeName}: {ex.Message}");
            }
        }
    }
}
