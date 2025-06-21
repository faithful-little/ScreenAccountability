using System;
using System.IO;
using System.Windows;

namespace SimpleAccountability
{
    public partial class App : Application
    {
        private MainWindow? _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure directories exist
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimpleAccountability");
            Directory.CreateDirectory(appDataDir);

            // Create and show the MainWindow (constructor handles hiding if needed)
            _window = new MainWindow();
            if (!_window.IsVisible)  // Ensure UI shows on fresh install
                _window.Show();
        }
    }
}
