using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Timers;                    // unambiguous Timer
using System.Windows;
using System.Windows.Forms;             // NotifyIcon
using Microsoft.Win32;
using Application = System.Windows.Application;
using Timer = System.Timers.Timer;

using System.Diagnostics;
using Microsoft.Win32;


namespace SimpleAccountability
{
    public partial class MainWindow : Window
    {
        private readonly string _appDataDir;
        private readonly string _configPath;
        private readonly string _screensDir;

        private AppSettings _settings;
        private Timer? _timer;
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;

        public MainWindow()
        {
            InitializeComponent();

            // 1) Set up folders
            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimpleAccountability");
            _configPath = Path.Combine(_appDataDir, "settings.json");
            _screensDir = Path.Combine(_appDataDir, "Screenshots");
            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_screensDir);

            // 2) Load settings
            _settings = AppSettings.Load(_configPath);

            // 3) Prefill UI
            txtEmail.Text = _settings.SmtpUsername;
            txtToEmail.Text = _settings.DestinationEmail;
            txtFrequency.Text = _settings.FrequencyMinutes.ToString();

            // 4) Build tray (even if hidden now)
            BuildTrayIcon();

            // 5) If first‑run (no config file) or not yet active, show UI
            if (!_settings.IsActive)
            {
                ShowSettings();
            }
            else
            {
                // Auto‑start monitoring
                ActivateMonitoring();
                Hide();
            }

            // 6) Send startup ACK & cleanup last month
            EmailSender.SendAck(_settings);
            CleanupLastMonth();
        }
        private void RegisterAutoStart()
        {
            // Grab the real on‑disk EXE
            string exePath = Process.GetCurrentProcess().MainModule!.FileName;
            string quoted = $"\"{exePath}\"";

            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key!.SetValue("SimpleAccountability", quoted);
        }
        private void StartMonitoring_Click(object sender, RoutedEventArgs e)
        {
            // Save UI inputs
            _settings.SmtpUsername = txtEmail.Text.Trim();
            _settings.SmtpPassword = txtPassword.Password;
            _settings.DestinationEmail = txtToEmail.Text.Trim();
            if (!int.TryParse(txtFrequency.Text, out int f) || f <= 0) f = 10;
            _settings.FrequencyMinutes = f;

            // Mark active and persist
            _settings.IsActive = true;
            _settings.Save(_configPath);

            // Add AutoStart if not already set
            RegisterAutoStart();

            //// Register autostart if not already
            //Registry.SetValue(
            //    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            //    "SimpleAccountability",
            //    System.Reflection.Assembly.GetExecutingAssembly().Location);

            // Activate & alert
            ActivateMonitoring();
            System.Windows.MessageBox.Show(
                "Monitoring is now running in the background.",
                "App Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Hide();
        }

        private void BuildTrayIcon()
        {
            // Load embedded icon
            var iconUri = new Uri(
                "pack://application:,,,/SimpleAccountability;component/Resources/app.ico");
            using var iconStream = Application.GetResourceStream(iconUri)!.Stream;
            var trayIconImage = new System.Drawing.Icon(iconStream);

            _trayIcon = new NotifyIcon
            {
                Icon = trayIconImage,
                Visible = true,
                Text = "Screenshot Monitor"
            };

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Settings…", null, (s, e) => ShowSettings());
            _trayMenu.Items.Add("Activate", null, (s, e) => ActivateMonitoring());
            _trayMenu.Items.Add("Deactivate", null, (s, e) => DeactivateMonitoring());
            _trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            // Disable Activate if already active
            _trayMenu.Items[1].Enabled = !_settings.IsActive;
            _trayMenu.Items[2].Enabled = _settings.IsActive;

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += (s, e) => ShowSettings();
        }

        private void ShowSettings()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ActivateMonitoring()
        {
            EmailSender.SendActivation(_settings);
            StartTimer();

            _settings.IsActive = true;
            _settings.Save(_configPath);

            _trayMenu!.Items[1].Enabled = false; // Activate
            _trayMenu.Items[2].Enabled = true;  // Deactivate
        }

        private void DeactivateMonitoring()
        {
            _timer?.Stop();
            EmailSender.SendDeactivation(_settings);

            _settings.IsActive = false;
            _settings.Save(_configPath);

            _trayMenu!.Items[1].Enabled = true;  // Activate
            _trayMenu.Items[2].Enabled = false; // Deactivate
        }

        private void StartTimer()
        {
            _timer?.Stop();
            double intervalMs = _settings.FrequencyMinutes * 60_000;
            _timer = new Timer(intervalMs);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private async void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            var rnd = new Random();
            await System.Threading.Tasks.Task.Delay(
                rnd.Next(_settings.FrequencyMinutes * 60_000));

            try
            {
                var bytes = ScreenshotHelper.CaptureScreenToBytes();
                EmailSender.SendScreenshot(_settings, bytes);

                // archive
                string monthFld = Path.Combine(
                    _screensDir, DateTime.Now.ToString("yyyy‑MM"));
                Directory.CreateDirectory(monthFld);
                File.WriteAllBytes(
                    Path.Combine(
                        monthFld,
                        $"{DateTime.Now:yyyy‑MM‑dd_HH‑mm‑ss}.jpg"),
                    bytes);
            }
            catch { /* ignore */ }
        }

        private void CleanupLastMonth()
        {
            string prev = DateTime.Now.AddMonths(-1).ToString("yyyy‑MM");
            var fld = Path.Combine(_screensDir, prev);
            if (Directory.Exists(fld))
                Directory.Delete(fld, true);
        }

        private void ExitApplication()
        {
            EmailSender.SendExit(_settings);
            _trayIcon!.Visible = false;
            Application.Current.Shutdown();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string msg =
@"1. Go to https://myaccount.google.com/security
2. Enable 2‑Step Verification
3. Click 'App passwords'
4. Create one for Mail → Other → ScreenshotMonitor
5. Paste the 16‑character password here";
            System.Windows.MessageBox.Show(
                msg,
                "Gmail App Password Steps",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
