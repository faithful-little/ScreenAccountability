#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using System.Security.Cryptography;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Timer = System.Timers.Timer;
using WpfMessageBox = System.Windows.MessageBox;

namespace SimpleAccountability
{
    public partial class MainWindow : Window
    {
        private readonly string _appDataDir;
        private readonly string _configPath;
        private readonly string _screensDir;
        private readonly string _pendingDir;

        private AppSettings _settings;
        private Timer? _timer;
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private readonly Random _rng = new();

        public MainWindow()
        {
            InitializeComponent();

            _appDataDir = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "SimpleAccountability");
            _configPath = Path.Combine(_appDataDir, "settings.json");
            _screensDir = Path.Combine(_appDataDir, "Screenshots");
            _pendingDir = Path.Combine(_screensDir, "Pending");

            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_screensDir);
            Directory.CreateDirectory(_pendingDir);

            _settings = AppSettings.Load(_configPath);

            // Only show UI if not fully configured
            bool needSetup =
                !_settings.IsActive ||
                string.IsNullOrWhiteSpace(_settings.SmtpUsername) ||
                string.IsNullOrWhiteSpace(_settings.SmtpPassword) ||
                string.IsNullOrWhiteSpace(_settings.DestinationEmail);

            if (needSetup)
            {
                // Prefill UI fields
                txtEmail.Text = _settings.SmtpUsername;
                txtToEmail.Text = _settings.DestinationEmail;
                txtFrequency.Text = _settings.FrequencyMinutes.ToString();
                BuildTrayIcon();
                ShowSettings();
            }
            else
            {
                BuildTrayIcon();
                ActivateMonitoring();
                Hide();
            }

            EmailSender.SendAck(_settings);
            CleanupLastMonth();
        }

        private void RegisterAutoStart()
        {
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
            if (!int.TryParse(txtFrequency.Text, out int f) || f <= 0)
                f = _settings.FrequencyMinutes > 0 ? _settings.FrequencyMinutes : 10;
            _settings.FrequencyMinutes = f;

            _settings.IsActive = true;
            _settings.Save(_configPath);

            RegisterAutoStart();
            ActivateMonitoring();

            WpfMessageBox.Show(
                "Monitoring is now running in the background.",
                "App Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Hide();
        }

        private void BuildTrayIcon()
        {
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
            _trayMenu.Items.Add("Settings…", null, (_, __) => ShowSettings());
            _trayMenu.Items.Add("Activate", null, (_, __) => ActivateMonitoring());
            _trayMenu.Items.Add("Deactivate", null, (_, __) => DeactivateMonitoring());
            _trayMenu.Items.Add("Exit", null, (_, __) => ExitApplication());

            _trayMenu.Items[1].Enabled = !_settings.IsActive;
            _trayMenu.Items[2].Enabled = _settings.IsActive;

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += (_, __) => ShowSettings();
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
            ScheduleNextScreenshot();

            _settings.IsActive = true;
            _settings.Save(_configPath);

            _trayMenu!.Items[1].Enabled = false;
            _trayMenu.Items[2].Enabled = true;
        }

        private void DeactivateMonitoring()
        {
            _timer?.Stop();
            EmailSender.SendDeactivation(_settings);

            _settings.IsActive = false;
            _settings.Save(_configPath);

            _trayMenu!.Items[1].Enabled = true;
            _trayMenu.Items[2].Enabled = false;
        }

        private void ScheduleNextScreenshot()
        {
            _timer?.Stop();

            // Random interval between 0 and 2×FrequencyMinutes (in milliseconds)
            int maxMs = _settings.FrequencyMinutes * 10 * 60_000;
            int delayMs = _rng.Next(0, maxMs);
            _timer = new Timer(delayMs) { AutoReset = false };
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
        }

        private async void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // First, replay any pending screenshots
            ProcessPendingScreenshots();

            try
            {
                // Capture & send the new one
                var bytes = ScreenshotHelper.CaptureScreenToBytes();
                EmailSender.SendScreenshot(_settings, bytes);

                // Archive locally
                string monthFld = Path.Combine(
                    _screensDir,
                    DateTime.Now.ToString("yyyy-MM"));
                Directory.CreateDirectory(monthFld);
                File.WriteAllBytes(
                    Path.Combine(
                        monthFld,
                        $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg"),
                    bytes);
            }
            catch (System.Net.Mail.SmtpException)
            {
                // Offline or auth failure → queue encrypted
                try
                {
                    var bytes = ScreenshotHelper.CaptureScreenToBytes();
                    SavePending(bytes);
                }
                catch { /* swallow */ }
            }
            catch { /* swallow all others */ }

            // Schedule the next shot at a new random delay
            ScheduleNextScreenshot();
        }

        private void SavePending(byte[] raw)
        {
            var encrypted = ProtectedData.Protect(
                raw, null, DataProtectionScope.CurrentUser);
            string path = Path.Combine(
                _pendingDir,
                $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.dat");
            File.WriteAllBytes(path, encrypted);
        }

        private void ProcessPendingScreenshots()
        {
            foreach (var file in Directory.GetFiles(_pendingDir, "*.dat"))
            {
                try
                {
                    var encrypted = File.ReadAllBytes(file);
                    var decrypted = ProtectedData.Unprotect(
                        encrypted, null, DataProtectionScope.CurrentUser);
                    EmailSender.SendScreenshot(_settings, decrypted);
                    File.Delete(file);
                }
                catch
                {
                    // Leave for next retry
                }
            }
        }

        private void CleanupLastMonth()
        {
            string prev = DateTime.Now.AddMonths(-1).ToString("yyyy-MM");
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
3. Click 'App passwords' at https://myaccount.google.com/apppasswords
4. Create one for Mail → Other → ScreenshotMonitor
5. Paste the 16‑character password here";
            WpfMessageBox.Show(
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
