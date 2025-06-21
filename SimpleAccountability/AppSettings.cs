using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimpleAccountability
{
    public class AppSettings
    {
        public int FrequencyMinutes { get; set; } = 10;
        public string DestinationEmail { get; set; } = "";
        public string SmtpUsername { get; set; } = "";

        // Encrypted password backing field
        private string _encryptedPassword = "";

        // Whether monitoring is active
        public bool IsActive { get; set; } = false;

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
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
        }

        public static AppSettings Load(string path)
        {
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}
