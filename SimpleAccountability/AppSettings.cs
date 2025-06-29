using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimpleAccountability
{
    public class AppSettings
    {
        public int FrequencyMinutes { get; set; } = 100;
        public string DestinationEmail { get; set; } = "";
        public string SmtpUsername { get; set; } = "";

        private string _encryptedPassword = "";

        public bool IsActive { get; set; } = false;

        // A stable per‑install Message-ID for threading
        public string ThreadId { get; set; } = "";

        public string SmtpPassword
        {
            get
            {
                if (string.IsNullOrEmpty(_encryptedPassword)) return "";
                var encBytes = Convert.FromBase64String(_encryptedPassword);
                var clearBytes = ProtectedData.Unprotect(
                    encBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clearBytes);
            }
            set
            {
                var clearBytes = Encoding.UTF8.GetBytes(value);
                var encBytes = ProtectedData.Protect(
                    clearBytes, null, DataProtectionScope.CurrentUser);
                _encryptedPassword = Convert.ToBase64String(encBytes);
            }
        }

        public void Save(string path)
        {
            // Ensure ThreadId exists
            if (string.IsNullOrWhiteSpace(ThreadId))
                ThreadId = $"<thread-{Guid.NewGuid()}@simpleaccountability>";

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
        }

        public static AppSettings Load(string path)
        {
            if (!File.Exists(path))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<AppSettings>(json)
                           ?? new AppSettings();
                // Guarantee a ThreadId
                if (string.IsNullOrWhiteSpace(s.ThreadId))
                    s.ThreadId = $"<thread-{Guid.NewGuid()}@simpleaccountability>";
                return s;
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}
